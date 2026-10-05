using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Npgsql;

namespace Chipmunk.Platform;

// 短事务租约认领 + 至少一次投递；Odoo 通过 eventId 实现幂等。
public sealed class OdooSync(NpgsqlDataSource source, IConfiguration config, ILogger<OdooSync> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timeout = int.TryParse(config["ODOO_TIMEOUT_SECONDS"], out var seconds) ? Math.Clamp(seconds, 1, 30) : 15;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeout) };
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var lease = Guid.NewGuid().ToString("N");
                List<string[]> rows;
                await using (var c = await source.OpenConnectionAsync(stoppingToken))
                {
                    var store = new Store(c);
                    await store.Execute("UPDATE integration_outbox SET status='dead_letter',lease_token=NULL,lease_until=NULL,last_error='Worker lease expired after final attempt',updated_at=now() WHERE status='processing' AND lease_until<now() AND attempts>=10");
                    rows = await store.Rows("""
                        UPDATE integration_outbox SET status='processing',lease_token=@lease,lease_until=now()+interval '45 seconds',attempts=attempts+1,updated_at=now()
                        WHERE id=(SELECT id FROM integration_outbox WHERE attempts<10 AND (status='pending' AND next_attempt<=now() OR status='processing' AND lease_until<now()) ORDER BY created_at,id FOR UPDATE SKIP LOCKED LIMIT 1)
                        RETURNING id,tenant_id,event_type,aggregate_id,payload::text,attempts
                        """, ("lease", lease));
                }
                if (rows.Count == 0) { await Task.Delay(2000, stoppingToken); continue; }
                var row = rows[0]; var attempts = int.Parse(row[5]);
                try
                {
                    var url = config["ODOO_INTERNAL_URL"] ?? "http://odoo:8069";
                    using var request = new HttpRequestMessage(HttpMethod.Post, url.TrimEnd('/') + "/chipmunk/integration/events");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Auth.Secret(config, "ODOO_INTEGRATION_KEY"));
                    request.Content = JsonContent.Create(new { eventId = row[0], tenantId = row[1], eventType = row[2], platformId = row[3], payload = JsonNode.Parse(row[4]) });
                    using var response = await client.SendAsync(request, stoppingToken);
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Odoo HTTP " + (int)response.StatusCode);
                    var result = JsonNode.Parse(await response.Content.ReadAsStringAsync(stoppingToken))!;
                    var data = result["data"];
                    if (result["code"]?.GetValue<int>() != 0 || data?["model"]?.GetValue<string>() != "crm.lead" || data["id"]?.GetValue<int>() is not > 0)
                        throw new InvalidOperationException("Invalid Odoo bridge response");
                    await using var c = await source.OpenConnectionAsync(stoppingToken); await using var tx = await c.BeginTransactionAsync(stoppingToken);
                    var store = new Store(c);
                    var claimed = await store.Rows("SELECT id FROM integration_outbox WHERE id=@id AND status='processing' AND lease_token=@lease FOR UPDATE", ("id", row[0]), ("lease", lease));
                    if (claimed.Count != 0)
                    {
                        await store.Execute("INSERT INTO integration_mapping(tenant_id,entity_type,platform_id,odoo_model,odoo_id) VALUES(@tenant,'crm.lead',@platform,@model,@odoo) ON CONFLICT(tenant_id,entity_type,platform_id) DO UPDATE SET odoo_model=EXCLUDED.odoo_model,odoo_id=EXCLUDED.odoo_id",
                            ("tenant", row[1]), ("platform", row[3]), ("model", data["model"]!.GetValue<string>()), ("odoo", data["id"]!.GetValue<int>()));
                        await store.Execute("UPDATE integration_outbox SET status='delivered',delivered_at=now(),last_error=NULL,lease_token=NULL,lease_until=NULL,updated_at=now() WHERE id=@id AND lease_token=@lease", ("id", row[0]), ("lease", lease));
                    }
                    await tx.CommitAsync(stoppingToken);
                }
                catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    await using var c = await source.OpenConnectionAsync(stoppingToken);
                    await new Store(c).Execute("UPDATE integration_outbox SET status=@status,last_error=@error,next_attempt=now()+make_interval(secs=>@delay),lease_token=NULL,lease_until=NULL,updated_at=now() WHERE id=@id AND lease_token=@lease",
                        ("status", attempts >= 10 ? "dead_letter" : "pending"), ("error", e.GetType().Name + ": " + e.Message), ("delay", Math.Min(3600, 5 * (1 << Math.Min(attempts, 9))) + Random.Shared.Next(0, 5)), ("id", row[0]), ("lease", lease));
                    logger.LogWarning("Odoo同步重试 {EventId} {Attempt}", row[0], attempts);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested) { logger.LogWarning(e, "Odoo同步循环异常"); await Task.Delay(2000, stoppingToken); }
        }
    }
}
