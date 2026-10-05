using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Npgsql;

namespace Chipmunk.Platform;

// 事务 outbox + 有限退避；只同步商业引用，不传患者病历或照片。
public sealed class OdooSync(NpgsqlDataSource source, IConfiguration config, ILogger<OdooSync> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(5000, stoppingToken);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var c = await source.OpenConnectionAsync(stoppingToken); await using var tx = await c.BeginTransactionAsync(stoppingToken);
                var store = new Store(c);
                var rows = await store.Rows("SELECT id,tenant_id,event_type,aggregate_id,payload::text,attempts FROM integration_outbox WHERE status='pending' AND next_attempt<=now() ORDER BY created_at FOR UPDATE SKIP LOCKED LIMIT 1");
                if (rows.Count != 0)
                {
                    var row = rows[0]; var attempts = int.Parse(row[5]) + 1;
                    try
                    {
                        var url = config["ODOO_INTERNAL_URL"] ?? "http://odoo:8069";
                        using var request = new HttpRequestMessage(HttpMethod.Post, url + "/chipmunk/integration/events");
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Auth.Secret(config, "ODOO_INTEGRATION_KEY"));
                        request.Content = JsonContent.Create(new { eventId = row[0], tenantId = row[1], eventType = row[2], platformId = row[3], payload = JsonNode.Parse(row[4]) });
                        using var response = await client.SendAsync(request, stoppingToken);
                        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Odoo HTTP " + (int)response.StatusCode);
                        var result = JsonNode.Parse(await response.Content.ReadAsStringAsync(stoppingToken))!;
                        if (result["code"]?.GetValue<int>() != 0) throw new InvalidOperationException("Odoo rejected event");
                        var data = result["data"]!;
                        await store.Execute("INSERT INTO integration_mapping(tenant_id,entity_type,platform_id,odoo_model,odoo_id) VALUES(@tenant,'crm.lead',@platform,@model,@odoo) ON CONFLICT(tenant_id,entity_type,platform_id) DO UPDATE SET odoo_model=EXCLUDED.odoo_model,odoo_id=EXCLUDED.odoo_id",
                            ("tenant", row[1]), ("platform", row[3]), ("model", data["model"]!.GetValue<string>()), ("odoo", data["id"]!.GetValue<int>()));
                        await store.Execute("UPDATE integration_outbox SET status='delivered',attempts=@attempt WHERE id=@id", ("attempt", attempts), ("id", row[0]));
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        await store.Execute("UPDATE integration_outbox SET attempts=@attempt,status=@status,last_error=@error,next_attempt=now()+make_interval(secs=>@delay) WHERE id=@id",
                            ("attempt", attempts), ("status", attempts >= 10 ? "dead_letter" : "pending"), ("error", e.GetType().Name + ": " + e.Message), ("delay", Math.Min(3600, 5 * (1 << Math.Min(attempts, 9)))), ("id", row[0]));
                        logger.LogWarning("Odoo同步重试 {EventId} {Attempt}", row[0], attempts);
                    }
                }
                await tx.CommitAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException) { logger.LogWarning(e, "Odoo同步循环异常"); }
            await Task.Delay(2000, stoppingToken);
        }
    }
}
