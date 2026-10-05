using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Npgsql;

namespace Chipmunk.Platform;

public sealed record AiMedia(string Id, string Path, string Sha256);
public interface IAiProvider
{
    string Name { get; }
    Task<JsonObject> Analyze(AiMedia media, CancellationToken cancellationToken);
}

// 显式 mock：只验证输入原件完整性，绝不生成诊断、预测或变形后图片。
public sealed class MockAiProvider(IConfiguration config) : IAiProvider
{
    public string Name => "mock";
    public async Task<JsonObject> Analyze(AiMedia media, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(media.Path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken)).ToLowerInvariant();
        if (!Auth.Equal(hash, media.Sha256)) throw new InvalidDataException("Media integrity mismatch");
        var delay = int.TryParse(config["AI_MOCK_DELAY_MS"], out var milliseconds) ? Math.Clamp(milliseconds, 0, 5000) : 2000;
        await Task.Delay(delay, cancellationToken);
        return new JsonObject { ["isMock"] = true, ["qualityChecks"] = new JsonArray(), ["resultText"] = "开发演示：未进行AI分析，前后图为同一原图。" };
    }
}

public static class AiTasks
{
    public static async Task<JsonObject> Create(Store store, User user, HttpContext context, JsonObject body)
    {
        Validation.Only(body, "imageKey");
        var media = await Media.ByKey(store, user, Auth.Required(body, "imageKey"));
        if (media["scene"]!.GetValue<string>() != "ai_photo") throw new ApiError(400, "AI任务需要ai_photo照片", "AI task requires an ai_photo upload");
        await store.Rate("ai:" + user.Id, 20, 86400);
        var task = await store.Add(user, "simulation", new JsonObject { ["imageKey"] = media["objectKey"]!.DeepClone(), ["mediaId"] = media["id"]!.DeepClone(), ["isMock"] = true });
        var id = task["id"]!.GetValue<string>();
        await store.Execute("INSERT INTO dso_ai_job(id,tenant_id,clinic_id,user_id,media_id) VALUES(@id,@tenant,@clinic,@user,@media)", ("id", id), ("tenant", user.Tenant), ("clinic", user.Clinic), ("user", user.Id), ("media", media["id"]!.GetValue<string>()));
        await store.Audit(user, context, "ai.mock_queued", id);
        return new JsonObject { ["taskId"] = id, ["status"] = "queued", ["isMock"] = true };
    }

    public static async Task<JsonObject> Get(Store store, IConfiguration config, User user, JsonObject task)
    {
        var rows = await store.Rows("SELECT status,progress,output::text,failure_code FROM dso_ai_job WHERE id=@id AND tenant_id=@tenant AND clinic_id=@clinic AND user_id=@user", ("id", task["id"]!.GetValue<string>()), ("tenant", user.Tenant), ("clinic", user.Clinic), ("user", user.Id));
        if (rows.Count != 1) throw DsoAccess.Missing();
        var row = rows[0];
        var output = row[2] == "" ? new JsonObject() : JsonNode.Parse(row[2])!.AsObject();
        var media = await store.Get(user, "media", task["mediaId"]!.GetValue<string>());
        return new JsonObject { ["taskId"] = task["id"]!.DeepClone(), ["status"] = row[0] == "succeeded" ? "done" : row[0] is "failed" or "cancelled" ? "quality_failed" : "analyzing", ["jobStatus"] = row[0], ["progress"] = int.Parse(row[1]),
            ["qualityChecks"] = output["qualityChecks"]?.DeepClone() ?? new JsonArray(), ["resultText"] = output["resultText"]?.DeepClone() ?? JsonValue.Create(""),
            ["beforeImage"] = Media.Download(config, media), ["afterImage"] = row[0] == "succeeded" ? Media.Download(config, media) : "", ["isMock"] = true, ["provider"] = "mock", ["failureCode"] = row[3] == "" ? null : row[3] };
    }

    public static async Task<JsonObject> Cancel(Store store, User user, HttpContext context, string id)
    {
        var changed = await store.Execute("UPDATE dso_ai_job SET status='cancelled',failure_code='cancelled_by_user',lease_token=NULL,lease_until=NULL,finished_at=now() WHERE id=@id AND tenant_id=@tenant AND clinic_id=@clinic AND user_id=@user AND status IN ('queued','running')", ("id", id), ("tenant", user.Tenant), ("clinic", user.Clinic), ("user", user.Id));
        if (changed != 1) throw new ApiError(409, "任务不存在或已结束", "Task is missing or already finished");
        await store.Audit(user, context, "ai.task_cancelled", id);
        return new JsonObject { ["taskId"] = id, ["jobStatus"] = "cancelled", ["isMock"] = true };
    }
}

public sealed class AiWorker(NpgsqlDataSource source, IConfiguration config, IAiProvider provider, ILogger<AiWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var lease = Guid.NewGuid().ToString("N"); List<string[]> rows;
                await using (var c = await source.OpenConnectionAsync(stoppingToken))
                {
                    var store = new Store(c);
                    await store.Execute("UPDATE dso_ai_job SET status='failed',failure_code='worker_attempts_exhausted',lease_token=NULL,lease_until=NULL,finished_at=now() WHERE status='running' AND lease_until<now() AND attempts>=3");
                    rows = await store.Rows("UPDATE dso_ai_job SET status='running',progress=30,attempts=attempts+1,lease_token=@lease,lease_until=now()+interval '45 seconds',started_at=COALESCE(started_at,now()) WHERE id=(SELECT id FROM dso_ai_job WHERE attempts<3 AND (status='queued' AND next_attempt<=now() OR status='running' AND lease_until<now()) ORDER BY created_at,id FOR UPDATE SKIP LOCKED LIMIT 1) RETURNING id,media_id,attempts", ("lease", lease));
                }
                if (rows.Count == 0) { await Task.Delay(500, stoppingToken); continue; }
                var row = rows[0];
                try
                {
                    JsonObject metadata;
                    await using (var c = await source.OpenConnectionAsync(stoppingToken))
                    {
                        var data = await new Store(c).Rows("SELECT r.body::text FROM platform_resource r JOIN dso_ai_job j ON j.media_id=r.id AND j.tenant_id=r.tenant_id AND j.clinic_id=r.clinic_id AND j.user_id=r.owner_id WHERE j.id=@id AND r.kind='media'", ("id", row[0]));
                        if (data.Count != 1) throw new InvalidDataException("Media metadata missing");
                        metadata = JsonNode.Parse(data[0][0])!.AsObject(); Media.Ready(metadata);
                    }
                    var root = Path.GetFullPath(config["MEDIA_ROOT"] ?? "/data/objects");
                    var output = await provider.Analyze(new AiMedia(row[1], Path.Combine(root, row[1] + "." + metadata["ext"]!.GetValue<string>()), metadata["sha256"]!.GetValue<string>()), stoppingToken);
                    await using var done = await source.OpenConnectionAsync(stoppingToken);
                    await new Store(done).Execute("UPDATE dso_ai_job SET status='succeeded',progress=100,output=CAST(@output AS jsonb),lease_token=NULL,lease_until=NULL,failure_code=NULL,finished_at=now() WHERE id=@id AND status='running' AND lease_token=@lease", ("output", output.ToJsonString()), ("id", row[0]), ("lease", lease));
                }
                catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    await using var failed = await source.OpenConnectionAsync(stoppingToken);
                    await new Store(failed).Execute("UPDATE dso_ai_job SET status=@status,failure_code='media_processing_unavailable',next_attempt=now()+interval '5 seconds',lease_token=NULL,lease_until=NULL,finished_at=CASE WHEN @status='failed' THEN now() ELSE NULL END WHERE id=@id AND status='running' AND lease_token=@lease", ("status", int.Parse(row[2]) >= 3 ? "failed" : "queued"), ("id", row[0]), ("lease", lease));
                    logger.LogWarning("AI演示任务重试 {TaskId} {Attempt} {ErrorType}", row[0], row[2], e.GetType().Name);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested) { logger.LogWarning(e, "AI任务循环异常"); await Task.Delay(1000, stoppingToken); }
        }
    }
}
