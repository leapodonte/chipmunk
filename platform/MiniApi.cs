using System.Globalization;
using System.Text.Json.Nodes;

namespace Chipmunk.Platform;

public sealed class MiniApi(Store store, IConfiguration config, HttpContext context)
{
    private string Method => context.Request.Method;
    private static readonly TimeZoneInfo China = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
    internal static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, China);
    private static string S(JsonObject body, string name, string fallback = "") => body[name]?.GetValue<string>() ?? fallback;
    private JsonArray Page(IEnumerable<JsonObject> items)
    {
        var page = int.TryParse(context.Request.Query["page"], out var p) ? p : 1;
        var size = int.TryParse(context.Request.Query["pageSize"], out var s) ? s : 10;
        if (page < 1 || page > 100000 || size < 1 || size > 100) throw new ApiError(400, "分页参数无效", "Invalid pagination; pageSize must be 1–100");
        return Store.Array(items.Skip((page - 1) * size).Take(size));
    }
    public async Task<JsonNode?> Handle(string path, JsonObject body)
    {
        if (path == "auth/mp-login" && Method == "POST" || path == "auth/phone-login" && Method == "POST")
        {
            await store.Rate("login:" + context.Connection.RemoteIpAddress, 60);
            return await Auth.Login(store, context, config, body, path.Contains("phone"));
        }
        if (path == "auth/sms-code" && Method == "POST") { Auth.DevKey(context, config); Auth.Required(body, "phone"); return new JsonObject { ["sent"] = false, ["demo"] = true, ["smsCode"] = "123456" }; }
        var user = await Auth.Current(store, context);
        await store.Lock("user:" + user.Id);
        await store.Rate("api:" + user.Id, 600, 60);
        if (path == "auth/logout" && Method == "POST") { await store.Execute("DELETE FROM platform_session WHERE token_hash=@hash", ("hash", Auth.Hash(context.Request.Headers.Authorization.ToString()[7..]))); return new JsonObject { ["ok"] = true }; }
        if (path == "users/me" && Method == "GET") return new JsonObject {
            ["id"] = user.Id, ["nickname"] = user.Nickname, ["phone"] = user.Phone.Length == 11 ? user.Phone[..3] + " **** " + user.Phone[^4..] : "",
            ["avatar"] = "", ["isMember"] = false, ["hasBoundDoctor"] = await CurrentTreatment(user) is not null,
            ["tenantId"] = user.Tenant, ["organizationId"] = "org_demo", ["clinicId"] = user.Clinic, ["roles"] = new JsonArray("patient") };
        if (path == "users/me/questionnaire" && Method == "POST")
        {
            if (body["answers"] is not JsonObject) throw new ApiError(400, "answers必须为对象", "answers must be an object");
            var record = await store.Add(user, "questionnaire", new JsonObject { ["answers"] = body["answers"]!.DeepClone(), ["createdAt"] = Now.ToString("O") });
            await store.Audit(user, context, "questionnaire.submitted", S(record, "id"));
            return new JsonObject { ["recommendedDoctorId"] = "d_001" };
        }
        if (path == "questionnaire/template" && Method == "GET") return new JsonObject { ["version"] = "frontend-v0.1", ["questions"] = new JsonArray(), ["source"] = "frontend" };
        var parts = path.Split('/');
        if (path == "doctors/recommend" && Method == "GET") return Store.Array(await store.List(user, "doctor", true));
        if (parts.Length == 2 && parts[0] == "doctors" && Method == "GET") return await store.Get(user, "doctor", parts[1], true);
        if (parts.Length == 3 && parts[0] == "doctors" && parts[2] == "bind" && Method == "POST")
        {
            var doctor = await store.Get(user, "doctor", parts[1], true);
            var current = await CurrentTreatment(user);
            if (current is not null)
            {
                if (current["doctor"]?["id"]?.GetValue<string>() == parts[1]) return current;
                throw new ApiError(409, "已有疗程，请先结束后重新绑定", "An active treatment already exists");
            }
            var treatment = await store.Add(user, "treatment", new JsonObject {
                ["status"] = "consulting", ["doctor"] = doctor.DeepClone(), ["members"] = new JsonArray(new JsonObject { ["doctorId"] = parts[1], ["role"] = "primary" }),
                ["currentStep"] = 0, ["totalSteps"] = 0, ["nextChangeDate"] = "", ["stepDays"] = 0, ["stepDayIndex"] = 0,
                ["checkinScore"] = 0, ["scoreLevel"] = "average", ["startDate"] = Now.ToString("yyyy.MM.dd"), ["endDate"] = "", ["createdAt"] = Now.ToString("yyyy.MM.dd"), ["isDemo"] = true });
            await store.Audit(user, context, "treatment.doctor_bound", S(treatment, "id"));
            await Emit(user, "crm.lead.created", S(treatment, "id"), new JsonObject { ["name"] = "花栗鼠演示咨询", ["platformRef"] = S(treatment, "id") });
            return treatment;
        }
        if (path == "treatments/current" && Method == "GET") return await CurrentTreatment(user);
        if (path == "treatments/mine" && Method == "GET") return Store.Array(await store.List(user, "treatment"));
        if (parts.Length == 2 && parts[0] == "treatments" && Method == "GET") return await store.Get(user, "treatment", parts[1]);
        if (path == "checkins/today" && Method == "GET") return await CheckinStatus(user);
        if (path == "checkins/records" && Method == "GET") return Page(await store.List(user, "checkin"));
        if (path == "checkins" && Method == "POST") return await Checkin(user, body);
        if (path == "posts" && Method == "GET" || path == "posts/search" && Method == "GET")
        {
            var posts = await Posts(user);
            var topic = context.Request.Query["topic"].ToString(); var keyword = context.Request.Query["keyword"].ToString();
            return Page(posts.Where(x => (topic is "" or "all" || S(x, "topic") == topic) && (keyword == "" || S(x, "title").Contains(keyword, StringComparison.OrdinalIgnoreCase))));
        }
        if (parts.Length >= 2 && parts[0] == "posts")
        {
            var post = (await Posts(user)).FirstOrDefault(x => S(x, "id") == parts[1]) ?? throw new ApiError(404, "帖子不存在", "Post not found");
            if (parts.Length == 2 && Method == "GET") return post;
            if (parts.Length == 3 && parts[2] == "like" && Method == "POST")
            {
                if (!(await store.List(user, "like")).Any(x => S(x, "postId") == parts[1])) await store.Add(user, "like", new JsonObject { ["postId"] = parts[1] });
                return new JsonObject { ["ok"] = true };
            }
        }
        if (path == "media/upload-token" && Method == "POST") return await Media.Grant(store, config, user, body);
        if (parts.Length == 3 && parts[0] == "media" && parts[2] == "url" && Method == "GET")
        {
            var media = await store.Get(user, "media", parts[1]); Media.Ready(media);
            return new JsonObject { ["url"] = Media.Download(config, media), ["expiresIn"] = 900 };
        }
        if (path == "ai/simulations" && Method == "POST")
        {
            var media = await Media.ByKey(store, user, Auth.Required(body, "imageKey"));
            if (S(media, "scene") != "ai_photo") throw new ApiError(400, "AI任务需要ai_photo照片", "AI task requires an ai_photo upload");
            await store.Rate("ai:" + user.Id, 20, 86400);
            var task = await store.Add(user, "simulation", new JsonObject { ["imageKey"] = S(media, "objectKey"), ["mediaId"] = S(media, "id"), ["createdAtEpoch"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ["isMock"] = true });
            await store.Audit(user, context, "ai.mock_created", S(task, "id"));
            return new JsonObject { ["taskId"] = S(task, "id"), ["isMock"] = true };
        }
        if (path == "ai/simulations/mine" && Method == "GET")
        {
            var result = new List<JsonObject>(); foreach (var task in await store.List(user, "simulation")) result.Add(await Simulation(user, task));
            return Page(result);
        }
        if (parts.Length == 3 && parts[0] == "ai" && parts[1] == "simulations" && Method == "GET") return await Simulation(user, await store.Get(user, "simulation", parts[2]));
        if (path == "messages" && Method == "GET") return Page(await store.List(user, "message"));
        if (path == "messages/read-all" && Method == "POST")
        {
            foreach (var message in await store.List(user, "message")) { message["read"] = true; await store.Update(user, message); }
            return new JsonObject { ["ok"] = true };
        }
        if (parts.Length == 3 && parts[0] == "messages" && parts[2] == "read" && Method == "POST")
        {
            var message = await store.Get(user, "message", parts[1]); message["read"] = true; await store.Update(user, message); return new JsonObject { ["ok"] = true };
        }
        if (path == "consultations" && Method == "POST")
        {
            var id = "consult_" + Guid.NewGuid().ToString("N");
            await store.Add(user, "consultation", new JsonObject { ["status"] = "requested", ["createdAt"] = Now.ToString("O") }, id: id);
            await Emit(user, "crm.lead.created", id, new JsonObject { ["name"] = "花栗鼠演示咨询", ["platformRef"] = id });
            return new JsonObject { ["id"] = id, ["status"] = "requested" };
        }
        if (Method == "GET" && path is "appointments/mine" or "patients/me/records" or "reports/mine" or "coupons/mine" or "mall/products") return new JsonArray();
        if (path == "invitations/mine" && Method == "GET") return new JsonObject { ["inviteCode"] = user.Id, ["invitedCount"] = 0, ["rewards"] = new JsonArray(), ["enabled"] = false };
        if (path == "memberships/open" && Method == "POST") throw new ApiError(501, "会员支付尚未接入", "Membership payments are not implemented");
        throw new ApiError(404, "接口不存在或请求方法不正确", "Endpoint not found or method unsupported");
    }
    private async Task<JsonObject?> CurrentTreatment(User user) => (await store.List(user, "treatment")).FirstOrDefault(x => S(x, "status") != "closed");
    private Task Emit(User user, string type, string aggregate, JsonObject payload) => store.Execute(
        "INSERT INTO integration_outbox(id,tenant_id,event_type,aggregate_id,payload) VALUES(@id,@tenant,@type,@aggregate,CAST(@payload AS jsonb))",
        ("id", "evt_" + Guid.NewGuid().ToString("N")), ("tenant", user.Tenant), ("type", type), ("aggregate", aggregate), ("payload", payload.ToJsonString()));
    private async Task<List<JsonObject>> Posts(User user)
    {
        var posts = await store.List(user, "post", true);
        var likes = await store.Rows("SELECT body->>'postId',count(*) FROM platform_resource WHERE kind='like' AND tenant_id=@tenant AND clinic_id=@clinic GROUP BY body->>'postId'", ("tenant", user.Tenant), ("clinic", user.Clinic));
        foreach (var p in posts) p["likes"] = int.Parse(likes.FirstOrDefault(x => x[0] == S(p, "id"))?[1] ?? "0");
        return posts;
    }
    private async Task<JsonObject> Simulation(User user, JsonObject task)
    {
        var elapsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - task["createdAtEpoch"]!.GetValue<long>();
        var media = await store.Get(user, "media", S(task, "mediaId"));
        return new JsonObject { ["taskId"] = S(task, "id"), ["status"] = elapsed >= 3 ? "done" : "analyzing", ["progress"] = elapsed >= 3 ? 100 : Math.Clamp((int)elapsed * 33, 0, 99),
            ["qualityChecks"] = new JsonArray(), ["resultText"] = elapsed >= 3 ? "开发演示：未进行AI分析，前后图为同一原图。" : "",
            ["beforeImage"] = Media.Download(config, media), ["afterImage"] = elapsed >= 3 ? Media.Download(config, media) : "", ["isMock"] = true };
    }
    private async Task<JsonObject> Checkin(User user, JsonObject body)
    {
        if (await CurrentTreatment(user) is null) throw new ApiError(409, "请先绑定医生", "Bind a doctor before check-in");
        var key = context.Request.Headers["Idempotency-Key"].ToString();
        if (key.Length > 128) throw new ApiError(400, "幂等键过长", "Idempotency key exceeds 128 characters");
        if (key != "")
        {
            var previous = await store.Rows("SELECT body_hash,result::text FROM platform_idempotency WHERE user_id=@user AND scope='checkin' AND key=@key", ("user", user.Id), ("key", key));
            if (previous.Count != 0) { if (previous[0][0] != Auth.Hash(body.ToJsonString())) throw new ApiError(409, "幂等键对应其他请求", "Idempotency key reused with different body"); return JsonNode.Parse(previous[0][1])!.AsObject(); }
        }
        var events = await store.List(user, "checkin");
        var last = events.FirstOrDefault(); var type = S(body, "type", last is null || S(last, "type") == "remove" ? "wear" : "remove");
        if (type is not ("wear" or "remove")) throw new ApiError(400, "打卡类型无效", "type must be wear or remove");
        if (last is not null && S(last, "type") == type || last is null && type == "remove") throw new ApiError(409, "戴套和取套必须交替", "Wear and remove events must alternate");
        var imageKey = S(body, "imageKey"); if (imageKey != "") { var image = await Media.ByKey(store, user, imageKey); if (S(image, "scene") != "checkin_photo") throw new ApiError(400, "请上传打卡照片", "Use a checkin_photo upload"); }
        var record = await store.Add(user, "checkin", new JsonObject { ["type"] = type, ["imageKey"] = imageKey, ["time"] = Now.ToString("O"), ["date"] = Now.ToString("yyyy-MM-dd"), ["epoch"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
        await store.Audit(user, context, "checkin.created", S(record, "id"));
        var status = await CheckinStatus(user);
        if (key != "") await store.Execute("INSERT INTO platform_idempotency(user_id,scope,key,body_hash,result) VALUES(@user,'checkin',@key,@hash,CAST(@result AS jsonb))", ("user", user.Id), ("key", key), ("hash", Auth.Hash(body.ToJsonString())), ("result", status.ToJsonString()));
        return status;
    }
    private async Task<JsonObject> CheckinStatus(User user)
    {
        var events = (await store.List(user, "checkin")).OrderBy(x => x["epoch"]!.GetValue<long>()).ToList();
        var now = DateTimeOffset.UtcNow; var midnight = new DateTimeOffset(Now.Date, TimeSpan.FromHours(8)).ToUnixTimeSeconds();
        long seconds = 0; long? worn = null;
        foreach (var e in events) { var stamp = e["epoch"]!.GetValue<long>(); if (S(e, "type") == "wear") worn = stamp; else if (worn.HasValue) { seconds += Math.Max(0, stamp - Math.Max(worn.Value, midnight)); worn = null; } }
        if (worn.HasValue) seconds += Math.Max(0, now.ToUnixTimeSeconds() - Math.Max(worn.Value, midnight));
        var dates = events.Select(x => S(x, "date")).ToHashSet(); var day = Now.Date; var streak = 0;
        if (!dates.Contains(day.ToString("yyyy-MM-dd"))) day = day.AddDays(-1);
        while (dates.Contains(day.ToString("yyyy-MM-dd"))) { streak++; day = day.AddDays(-1); }
        var treatment = await CurrentTreatment(user); var minutes = Math.Min(1440, seconds / 60);
        return new JsonObject { ["todayWearMinutes"] = minutes, ["targetMinutes"] = 1200, ["continuousDays"] = streak, ["checkedToday"] = dates.Contains(Now.ToString("yyyy-MM-dd")),
            ["periodScore"] = Math.Min(100, minutes * 100 / 1200), ["scoreChangePercent"] = 0, ["periodStart"] = treatment?["startDate"]?.DeepClone() ?? JsonValue.Create(""), ["periodEnd"] = treatment?["endDate"]?.DeepClone() ?? JsonValue.Create(""), ["scoreMode"] = "demo_today_ratio" };
    }
}
