using System.Text.Json.Nodes;

namespace Chipmunk.Platform;

public sealed class Commercial(Store store, User user, HttpContext context)
{
    private readonly DsoAccess access = new(store, user, context);
    private const string Projection = "jsonb_build_object('id',l.id,'patientId',l.patient_id,'originRef',l.origin_ref,'title',l.title,'stage',l.stage,'assignedTo',l.assigned_to,'version',l.version,'createdAt',l.created_at)";
    private string Scope => user.Has("consultant", "clinic_manager", "regional_manager", "platform_operator", "platform_admin") ? "TRUE" :
        "(l.created_by=@actor OR EXISTS(SELECT 1 FROM dso_care_team t JOIN dso_doctor d ON d.id=t.doctor_id WHERE t.patient_id=l.patient_id AND t.tenant_id=@tenant AND t.clinic_id=@clinic AND d.user_id=@actor AND d.active))";
    private void Require() => user.Require("doctor", "consultant", "clinic_manager", "regional_manager", "platform_operator", "platform_admin");

    public async Task<JsonArray> List()
    {
        Require(); var (limit, offset) = access.Page();
        var stage = context.Request.Query["stage"].ToString();
        if (stage != "" && !new[] { "new", "contacted", "qualified", "won", "lost" }.Contains(stage)) throw new ApiError(400, "线索阶段无效", "Invalid CRM stage");
        return await access.JsonRows("SELECT " + Projection + "::text FROM dso_crm_lead l WHERE l.tenant_id=@tenant AND l.clinic_id=@clinic AND " + Scope + " AND (@stage='' OR l.stage=@stage) ORDER BY l.created_at DESC,l.id LIMIT @limit OFFSET @offset", ("stage", stage), ("limit", limit), ("offset", offset));
    }

    public async Task<JsonObject> Get(string id, bool locked = false)
    {
        Require();
        var rows = await access.Rows("SELECT " + Projection + "::text FROM dso_crm_lead l WHERE l.id=@id AND l.tenant_id=@tenant AND l.clinic_id=@clinic AND " + Scope + (locked ? " FOR UPDATE" : ""), ("id", id));
        if (rows.Count == 0) throw DsoAccess.Missing();
        return JsonNode.Parse(rows[0][0])!.AsObject();
    }

    public async Task<JsonObject> Create(JsonObject body)
    {
        Require(); Validation.Only(body, "title", "patientId");
        var title = Validation.Text(body, "title", 150);
        var patient = Validation.Text(body, "patientId", 128, false);
        if (patient != "") await access.Patient(patient, user.Has("doctor"));
        return await Idempotency.Run(store, user, context, "crm.create", body, async () =>
        {
            var id = "lead_" + Guid.NewGuid().ToString("N");
            await access.Execute("INSERT INTO dso_crm_lead(id,tenant_id,clinic_id,patient_id,origin_ref,title,created_by) VALUES(@id,@tenant,@clinic,@patient,@id,@title,@actor)", ("id", id), ("patient", patient == "" ? null : patient), ("title", title));
            await Emit(id, "crm.lead.created", new JsonObject { ["name"] = "Smilelab opportunity " + id, ["platformRef"] = id });
            await store.Audit(user, context, "crm.lead_created", id);
            return await Get(id);
        });
    }

    // 小程序咨询同步到牙科 CRM；沿用原商业引用，维持现有集成映射契约。
    public async Task FromPatient(string origin, string title)
    {
        var patient = await access.OwnPatient();
        await access.Execute("INSERT INTO dso_crm_lead(id,tenant_id,clinic_id,patient_id,origin_ref,title,created_by) VALUES(@id,@tenant,@clinic,@patient,@origin,@title,@actor) ON CONFLICT(tenant_id,origin_ref) DO NOTHING", ("id", "lead_" + Guid.NewGuid().ToString("N")), ("patient", patient), ("origin", origin), ("title", title));
    }

    private async Task Assignee(string id)
    {
        if (id == "") return;
        var rows = await access.Rows("SELECT 1 FROM platform_user u JOIN platform_role_assignment r ON r.user_id=u.id AND r.tenant_id=u.tenant_id JOIN platform_clinic c ON c.id=@clinic AND c.tenant_id=@tenant WHERE u.id=@id AND u.tenant_id=@tenant AND r.role IN ('doctor','consultant','clinic_manager','regional_manager','platform_operator','platform_admin') AND (r.clinic_id=@clinic OR r.organization_id=c.organization_id OR (r.clinic_id IS NULL AND r.organization_id IS NULL))", ("id", id));
        if (rows.Count == 0) throw DsoAccess.Missing();
    }

    public async Task<JsonObject> Update(string id, JsonObject body)
    {
        Require(); Validation.Only(body, "version", "stage", "assignedTo");
        var lead = await Get(id, true);
        if (DsoAccess.Version(body) != lead["version"]!.GetValue<int>()) throw new ApiError(409, "线索版本冲突", "CRM lead version conflict");
        var stage = Validation.Text(body, "stage", 32, false);
        var current = lead["stage"]!.GetValue<string>();
        if (stage == "") stage = current;
        if (!new[] { "new", "contacted", "qualified", "won", "lost" }.Contains(stage) || current is "won" or "lost" && stage != current)
            throw new ApiError(409, "已结束线索不能直接重开", "Invalid stage or closed lead cannot be reopened");
        var assigned = body.ContainsKey("assignedTo") ? Validation.Text(body, "assignedTo", 128, false) : lead["assignedTo"]?.GetValue<string>() ?? "";
        await Assignee(assigned);
        await access.Execute("UPDATE dso_crm_lead SET stage=@stage,assigned_to=@assigned,version=version+1,updated_at=now() WHERE id=@id AND tenant_id=@tenant AND clinic_id=@clinic", ("stage", stage), ("assigned", assigned == "" ? null : assigned), ("id", id));
        await store.Audit(user, context, "crm.lead_updated", id);
        return await Get(id);
    }

    public async Task<JsonArray> Activities(string lead)
    {
        await Get(lead); var (limit, offset) = access.Page();
        return await access.JsonRows("SELECT jsonb_build_object('id',id,'leadId',lead_id,'type',type,'summary',summary,'dueAt',due_at,'status',status,'version',version,'completedAt',completed_at)::text FROM dso_crm_activity WHERE tenant_id=@tenant AND clinic_id=@clinic AND lead_id=@lead ORDER BY due_at,id LIMIT @limit OFFSET @offset", ("lead", lead), ("limit", limit), ("offset", offset));
    }

    public async Task<JsonObject> AddActivity(string lead, JsonObject body)
    {
        await Get(lead); Validation.Only(body, "type", "summary", "dueAt");
        var type = Validation.Text(body, "type", 32);
        if (!new[] { "call", "follow_up", "visit" }.Contains(type)) throw new ApiError(400, "跟进类型无效", "Invalid activity type");
        var summary = Validation.Text(body, "summary", 300); var due = Validation.Time(body, "dueAt");
        if (due > DateTimeOffset.UtcNow.AddDays(365)) throw new ApiError(400, "跟进时间不能超过一年", "Activity due date cannot be more than one year ahead");
        return await Idempotency.Run(store, user, context, "crm.activity:" + lead, body, async () =>
        {
            var id = "activity_" + Guid.NewGuid().ToString("N");
            await access.Execute("INSERT INTO dso_crm_activity(id,tenant_id,clinic_id,lead_id,type,summary,due_at,created_by) VALUES(@id,@tenant,@clinic,@lead,@type,@summary,@due,@actor)", ("id", id), ("lead", lead), ("type", type), ("summary", summary), ("due", due));
            await store.Audit(user, context, "crm.activity_created", id);
            return new JsonObject { ["id"] = id, ["leadId"] = lead, ["type"] = type, ["summary"] = summary, ["dueAt"] = due.ToString("O"), ["status"] = "pending", ["version"] = 1 };
        });
    }

    public async Task<JsonObject> FinishActivity(string id, JsonObject body)
    {
        Validation.Only(body, "version", "status");
        var rows = await access.Rows("SELECT lead_id,status,version FROM dso_crm_activity WHERE id=@id AND tenant_id=@tenant AND clinic_id=@clinic FOR UPDATE", ("id", id));
        if (rows.Count == 0) throw DsoAccess.Missing();
        await Get(rows[0][0]);
        var status = Validation.Text(body, "status", 32);
        if (status is not ("done" or "cancelled")) throw new ApiError(400, "跟进状态无效", "Activity status must be done or cancelled");
        if (rows[0][1] != "pending" || int.Parse(rows[0][2]) != DsoAccess.Version(body)) throw new ApiError(409, "跟进状态或版本冲突", "Activity state or version conflict");
        await access.Execute("UPDATE dso_crm_activity SET status=@status,version=version+1,completed_at=now() WHERE id=@id AND tenant_id=@tenant AND clinic_id=@clinic", ("id", id), ("status", status));
        await store.Audit(user, context, "crm.activity_" + status, id);
        return new JsonObject { ["id"] = id, ["status"] = status, ["version"] = int.Parse(rows[0][2]) + 1 };
    }

    public Task Emit(string origin, string type, JsonObject payload) => store.Execute("INSERT INTO integration_outbox(id,tenant_id,event_type,aggregate_id,payload) VALUES(@id,@tenant,@type,@aggregate,CAST(@payload AS jsonb))", ("id", "evt_" + Guid.NewGuid().ToString("N")), ("tenant", user.Tenant), ("type", type), ("aggregate", origin), ("payload", payload.ToJsonString()));
}
