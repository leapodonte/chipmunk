using System.Text.Json.Nodes;

namespace Chipmunk.Platform;

public sealed class DsoApi(Store store, IConfiguration config, HttpContext context)
{
    public async Task<JsonNode?> Handle(string path, JsonObject body)
    {
        var user = await Auth.Current(store, context);
        await store.Lock("user:" + user.Id);
        await store.Rate("api:" + user.Id, 600, 60);
        return await HandleAuthorized(store, config, context, user, path, body);
    }

    public static async Task<JsonNode?> HandleAuthorized(Store store, IConfiguration config, HttpContext context, User user, string path, JsonObject body)
    {
        var method = context.Request.Method;
        var parts = path.Split('/');
        var access = new DsoAccess(store, user, context);
        var appointments = new Appointments(store, user, context);
        var clinical = new Clinical(store, user, context);
        var commercial = new Commercial(store, user, context);
        var operations = new Operations(store, user, context);
        if (path == "context" && method == "GET") return new JsonObject { ["userId"] = user.Id, ["tenantId"] = user.Tenant, ["organizationId"] = user.Organization, ["clinicId"] = user.Clinic, ["roles"] = new JsonArray(user.Roles.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()), ["environment"] = "dev-demo" };
        if (path == "clinics/mine" && method == "GET") return await access.JsonRows("SELECT jsonb_build_object('id',c.id,'name',c.name,'organizationId',c.organization_id)::text FROM platform_clinic c WHERE c.tenant_id=@tenant AND EXISTS(SELECT 1 FROM platform_role_assignment r WHERE r.user_id=@actor AND r.tenant_id=@tenant AND (r.clinic_id=c.id OR r.organization_id=c.organization_id OR (r.clinic_id IS NULL AND r.organization_id IS NULL))) ORDER BY c.id");
        if (path == "context/switch" && method == "POST")
        {
            Validation.Only(body, "clinicId");
            var clinic = Auth.Required(body, "clinicId");
            var rows = await access.Rows("SELECT 1 FROM platform_clinic c WHERE c.id=@target AND c.tenant_id=@tenant AND EXISTS(SELECT 1 FROM platform_role_assignment r WHERE r.user_id=@actor AND r.tenant_id=@tenant AND (r.clinic_id=c.id OR r.organization_id=c.organization_id OR (r.clinic_id IS NULL AND r.organization_id IS NULL)))", ("target", clinic));
            if (rows.Count == 0) throw DsoAccess.Missing();
            await access.Execute("UPDATE platform_session SET clinic_id=@target WHERE token_hash=@hash AND user_id=@actor", ("target", clinic), ("hash", Auth.Hash(context.Request.Headers.Authorization.ToString()[7..])));
            await store.Audit(user, context, "identity.clinic_switched", clinic);
            return new JsonObject { ["clinicId"] = clinic, ["refreshContext"] = true };
        }
        if (path == "doctors" && method == "GET") return await access.JsonRows("SELECT jsonb_build_object('id',id,'name',display_name,'active',active,'canSchedule',(@manager OR COALESCE(user_id=@actor,false)))::text FROM dso_doctor WHERE tenant_id=@tenant AND clinic_id=@clinic AND active ORDER BY id", ("manager", user.Has("clinic_manager", "regional_manager", "platform_admin")));
        if (path == "slots" && method == "GET") return await appointments.Slots();
        if (path == "slots" && method == "POST") return await appointments.CreateSlot(body);
        if (parts.Length == 3 && parts[0] == "slots" && parts[2] == "close" && method == "POST") return await appointments.CloseSlot(parts[1]);
        if (path == "appointments/mine" && method == "GET") return await appointments.List(true);
        if (path == "appointments" && method == "GET") return await appointments.List(false);
        if (path == "appointments" && method == "POST") return await appointments.Book(body);
        if (parts.Length == 2 && parts[0] == "appointments" && method == "GET") return await appointments.Get(parts[1]);
        if (parts.Length == 3 && parts[0] == "appointments" && parts[2] == "status" && method == "POST") return await appointments.Transition(parts[1], body);
        if (path == "patients" && method == "GET") return await clinical.Patients();
        if (path == "patients/me" && method == "GET") return await clinical.OwnProfile();
        if (path == "patients/me" && method is "PATCH" or "PUT") return await clinical.UpdateProfile(body);
        if (path == "patients/me/records" && method == "GET") return await clinical.Records(await access.OwnPatient());
        if (path == "patients/me/care-team" && method == "GET") return await clinical.Team();
        if (parts.Length == 4 && parts[0] == "patients" && parts[1] == "me" && parts[2] == "care-team" && method is "POST" or "DELETE") return await clinical.CareTeam(parts[3], method == "POST");
        if (parts.Length == 2 && parts[0] == "patients" && method == "GET") return await access.Patient(parts[1], user.Has("doctor", "patient"));
        if (parts.Length == 3 && parts[0] == "patients" && parts[2] == "records" && method == "GET") return await clinical.Records(parts[1]);
        if (parts.Length == 3 && parts[0] == "patients" && parts[2] == "records" && method == "POST") return await clinical.Create(parts[1], body);
        if (parts.Length == 2 && parts[0] == "clinical-records" && method == "GET") return await clinical.Get(parts[1]);
        if (parts.Length == 2 && parts[0] == "clinical-records" && method == "PATCH") return await clinical.Update(parts[1], body, false);
        if (parts.Length == 3 && parts[0] == "clinical-records" && parts[2] == "sign" && method == "POST") return await clinical.Update(parts[1], body, true);
        if (path == "crm/leads" && method == "GET") return await commercial.List();
        if (path == "crm/leads" && method == "POST") return await commercial.Create(body);
        if (parts.Length == 3 && parts[0] == "crm" && parts[1] == "leads" && method == "GET") return await commercial.Get(parts[2]);
        if (parts.Length == 3 && parts[0] == "crm" && parts[1] == "leads" && method == "PATCH") return await commercial.Update(parts[2], body);
        if (parts.Length == 4 && parts[0] == "crm" && parts[1] == "leads" && parts[3] == "activities" && method == "GET") return await commercial.Activities(parts[2]);
        if (parts.Length == 4 && parts[0] == "crm" && parts[1] == "leads" && parts[3] == "activities" && method == "POST") return await commercial.AddActivity(parts[2], body);
        if (parts.Length == 3 && parts[0] == "crm" && parts[1] == "activities" && method == "PATCH") return await commercial.FinishActivity(parts[2], body);
        if (path == "operations/summary" && method == "GET") return await operations.Summary();
        if (path == "operations/outbox" && method == "GET") return await operations.Outbox();
        if (path == "operations/audit" && method == "GET") return await operations.Audit();
        if (parts.Length == 4 && parts[0] == "operations" && parts[1] == "outbox" && parts[3] == "retry" && method == "POST") return await operations.Retry(parts[2], body);
        throw new ApiError(404, "DSO接口不存在或方法不正确", "DSO endpoint not found or method unsupported");
    }
}
