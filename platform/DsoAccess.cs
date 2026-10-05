using System.Text.Json.Nodes;

namespace Chipmunk.Platform;

public sealed class DsoAccess(Store store, User user, HttpContext context)
{
    public Task<List<string[]>> Rows(string sql, params (string, object?)[] values) => store.Rows(sql,
        new[] { ("tenant", (object?)user.Tenant), ("clinic", (object?)user.Clinic), ("actor", (object?)user.Id) }.Concat(values).ToArray());
    public Task<int> Execute(string sql, params (string, object?)[] values) => store.Execute(sql,
        new[] { ("tenant", (object?)user.Tenant), ("clinic", (object?)user.Clinic), ("actor", (object?)user.Id) }.Concat(values).ToArray());
    public async Task<JsonArray> JsonRows(string sql, params (string, object?)[] values) =>
        new((await Rows(sql, values)).Select(x => JsonNode.Parse(x[0])).ToArray());

    public async Task<string> OwnPatient()
    {
        user.Require("patient");
        var rows = await Rows("SELECT id FROM dso_patient WHERE tenant_id=@tenant AND clinic_id=@clinic AND user_id=@actor");
        if (rows.Count != 0) return rows[0][0];
        var id = "patient_" + Guid.NewGuid().ToString("N");
        await Execute("INSERT INTO dso_patient(id,tenant_id,clinic_id,user_id,display_name) VALUES(@id,@tenant,@clinic,@actor,@name)", ("id", id), ("name", user.Nickname));
        return id;
    }

    public async Task<JsonObject> Patient(string id, bool clinical = false)
    {
        var rows = await Rows("SELECT jsonb_build_object('id',p.id,'displayName',p.display_name,'version',p.version,'profile',p.profile,'createdAt',p.created_at)::text,p.user_id FROM dso_patient p WHERE p.id=@id AND p.tenant_id=@tenant AND p.clinic_id=@clinic", ("id", id));
        if (rows.Count == 0) throw Missing();
        var own = rows[0][1] == user.Id && user.Has("patient");
        var careTeam = await IsClinicianFor(id);
        if (!own && !careTeam && (clinical || !user.Has("consultant", "clinic_manager", "regional_manager", "platform_admin"))) throw Missing();
        await store.Audit(user, context, clinical ? "clinical.patient_read" : "patient.read", id);
        var result = JsonNode.Parse(rows[0][0])!.AsObject();
        if (!clinical && !own) result.Remove("profile");
        return result;
    }

    public async Task<bool> IsClinicianFor(string patient) => user.Has("doctor") && (await Rows("SELECT 1 FROM dso_care_team t JOIN dso_doctor d ON d.id=t.doctor_id AND d.tenant_id=t.tenant_id AND d.clinic_id=t.clinic_id WHERE t.tenant_id=@tenant AND t.clinic_id=@clinic AND t.patient_id=@id AND d.user_id=@actor AND d.active", ("id", patient))).Count != 0;

    public async Task<JsonObject> Doctor(string id, bool manage = false)
    {
        var rows = await Rows("SELECT jsonb_build_object('id',id,'name',display_name,'active',active)::text,user_id FROM dso_doctor WHERE id=@id AND tenant_id=@tenant AND clinic_id=@clinic AND active", ("id", id));
        if (rows.Count == 0) throw Missing();
        if (manage && !user.Has("clinic_manager", "regional_manager", "platform_admin") && !(user.Has("doctor") && rows[0][1] == user.Id)) throw Missing();
        return JsonNode.Parse(rows[0][0])!.AsObject();
    }

    public async Task Notify(string patient, string title, string content, string type, string reference)
    {
        var rows = await Rows("SELECT user_id FROM dso_patient WHERE id=@id AND tenant_id=@tenant AND clinic_id=@clinic", ("id", patient));
        if (rows.Count != 1) throw Missing();
        var recipient = new User(rows[0][0], user.Tenant, user.Clinic, "", "");
        await store.Add(recipient, "message", new JsonObject { ["title"] = title, ["content"] = content, ["type"] = type, ["referenceId"] = reference, ["time"] = DateTimeOffset.UtcNow.ToString("O"), ["read"] = false });
    }

    public (int Limit, int Offset) Page()
    {
        var page = int.TryParse(context.Request.Query["page"], out var p) ? p : 1;
        var size = int.TryParse(context.Request.Query["pageSize"], out var s) ? s : 20;
        if (page < 1 || page > 10000 || size < 1 || size > 100) throw new ApiError(400, "分页参数无效", "Invalid pagination");
        return (size, (page - 1) * size);
    }
    public static ApiError Missing() => new(404, "资源不存在或不可访问", "Resource not found or inaccessible");
    public static int Version(JsonObject body) => body["version"] is JsonValue v && v.TryGetValue<int>(out var version) && version > 0
        ? version : throw new ApiError(400, "version必须为正整数", "version must be a positive integer");
}
