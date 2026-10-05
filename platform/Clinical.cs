using System.Text;
using System.Text.Json.Nodes;

namespace Chipmunk.Platform;

public sealed class Clinical(Store store, User user, HttpContext context)
{
    private readonly DsoAccess access = new(store, user, context);
    private const string Projection = "jsonb_build_object('id',id,'patientId',patient_id,'authorId',author_id,'status',status,'version',version,'content',content,'previousId',previous_id,'createdAt',created_at,'signedAt',signed_at)";

    public async Task<JsonArray> Patients()
    {
        user.Require("doctor", "consultant", "clinic_manager", "regional_manager", "platform_admin");
        var (limit, offset) = access.Page();
        var predicate = user.Has("consultant", "clinic_manager", "regional_manager", "platform_admin") ? "TRUE" :
            "EXISTS(SELECT 1 FROM dso_care_team t JOIN dso_doctor d ON d.id=t.doctor_id WHERE t.patient_id=p.id AND t.tenant_id=@tenant AND t.clinic_id=@clinic AND d.user_id=@actor AND d.active)";
        var rows = await access.JsonRows("SELECT jsonb_build_object('id',p.id,'displayName',p.display_name,'version',p.version,'createdAt',p.created_at)::text FROM dso_patient p WHERE p.tenant_id=@tenant AND p.clinic_id=@clinic AND " + predicate + " ORDER BY p.created_at DESC,p.id LIMIT @limit OFFSET @offset", ("limit", limit), ("offset", offset));
        await store.Audit(user, context, "patient.directory_read", user.Clinic);
        return rows;
    }

    public async Task<JsonObject> OwnProfile() => await access.Patient(await access.OwnPatient(), true);
    public async Task<JsonObject> UpdateProfile(JsonObject body)
    {
        Validation.Only(body, "displayName", "profile", "version");
        var id = await access.OwnPatient();
        var name = Auth.Required(body, "displayName");
        if (name.Length > 100 || body["profile"] is not JsonObject profile || Encoding.UTF8.GetByteCount(profile.ToJsonString()) > 8192)
            throw new ApiError(400, "患者资料格式无效", "Invalid patient profile");
        var allowed = new[] { "birthDate", "gender", "allergies", "medicalHistory", "emergencyContact" };
        if (profile.Any(x => !allowed.Contains(x.Key))) throw new ApiError(400, "包含不支持的资料字段", "Unsupported patient profile field");
        foreach (var field in new[] { "allergies", "medicalHistory" }) if (profile.ContainsKey(field)) Validation.Text(profile, field, 3000, false);
        if (profile.ContainsKey("gender") && Validation.Text(profile, "gender", 32) is not ("female" or "male" or "other" or "unknown")) throw new ApiError(400, "gender值无效", "Invalid gender value");
        if (profile.ContainsKey("birthDate") && (!DateOnly.TryParseExact(Validation.Text(profile, "birthDate", 10), "yyyy-MM-dd", out var birth) || birth > DateOnly.FromDateTime(DateTime.UtcNow))) throw new ApiError(400, "出生日期无效", "Invalid birth date");
        if (profile.ContainsKey("emergencyContact"))
        {
            if (profile["emergencyContact"] is not JsonObject emergency) throw new ApiError(400, "紧急联系人必须为对象", "Emergency contact must be an object");
            Validation.Only(emergency, "name", "phone", "relationship");
            Validation.Text(emergency, "name", 100); Validation.Text(emergency, "phone", 32);
            Validation.Text(emergency, "relationship", 100, false);
        }
        var changed = await access.Execute("UPDATE dso_patient SET display_name=@name,profile=CAST(@profile AS jsonb),version=version+1,updated_at=now() WHERE id=@id AND tenant_id=@tenant AND clinic_id=@clinic AND user_id=@actor AND version=@version",
            ("name", name), ("profile", profile.ToJsonString()), ("id", id), ("version", DsoAccess.Version(body)));
        if (changed != 1) throw new ApiError(409, "患者资料版本冲突", "Patient profile version conflict");
        await store.Audit(user, context, "patient.profile_updated", id);
        return await access.Patient(id, true);
    }

    public async Task<JsonObject> CareTeam(string doctorId, bool grant)
    {
        var id = await access.OwnPatient();
        await access.Doctor(doctorId);
        if (grant) await access.Execute("INSERT INTO dso_care_team(tenant_id,clinic_id,patient_id,doctor_id) VALUES(@tenant,@clinic,@patient,@doctor) ON CONFLICT DO NOTHING", ("patient", id), ("doctor", doctorId));
        else await access.Execute("DELETE FROM dso_care_team WHERE tenant_id=@tenant AND clinic_id=@clinic AND patient_id=@patient AND doctor_id=@doctor", ("patient", id), ("doctor", doctorId));
        await store.Audit(user, context, grant ? "clinical.doctor_access_granted" : "clinical.doctor_access_revoked", id);
        return new JsonObject { ["patientId"] = id, ["doctorId"] = doctorId, ["granted"] = grant };
    }

    public async Task<JsonArray> Team()
    {
        var id = await access.OwnPatient();
        return await access.JsonRows("SELECT jsonb_build_object('doctorId',d.id,'name',d.display_name,'grantedAt',t.created_at)::text FROM dso_care_team t JOIN dso_doctor d ON d.id=t.doctor_id WHERE t.tenant_id=@tenant AND t.clinic_id=@clinic AND t.patient_id=@patient ORDER BY d.id", ("patient", id));
    }

    public async Task<JsonArray> Records(string patient)
    {
        await access.Patient(patient, true);
        var (limit, offset) = access.Page();
        var predicate = await access.IsClinicianFor(patient) ? "TRUE" : "status='signed'";
        var result = await access.JsonRows("SELECT " + Projection + "::text FROM dso_clinical_record WHERE tenant_id=@tenant AND clinic_id=@clinic AND patient_id=@patient AND " + predicate + " ORDER BY created_at DESC,id LIMIT @limit OFFSET @offset", ("patient", patient), ("limit", limit), ("offset", offset));
        await store.Audit(user, context, "clinical.records_read", patient);
        return result;
    }

    public async Task<JsonObject> Get(string id, bool locked = false)
    {
        var rows = await access.Rows("SELECT " + Projection + "::text FROM dso_clinical_record WHERE id=@id AND tenant_id=@tenant AND clinic_id=@clinic" + (locked ? " FOR UPDATE" : ""), ("id", id));
        if (rows.Count != 1) throw DsoAccess.Missing();
        var result = JsonNode.Parse(rows[0][0])!.AsObject();
        await access.Patient(result["patientId"]!.GetValue<string>(), true);
        if (!await access.IsClinicianFor(result["patientId"]!.GetValue<string>()) && result["status"]!.GetValue<string>() != "signed") throw DsoAccess.Missing();
        await store.Audit(user, context, "clinical.record_read", id);
        return result;
    }

    private static JsonObject Content(JsonObject body)
    {
        if (body["content"] is not JsonObject content || Encoding.UTF8.GetByteCount(content.ToJsonString()) > 32768)
            throw new ApiError(400, "病历content必须为不超过32KiB的对象", "Clinical content must be an object of at most 32 KiB");
        var allowed = new[] { "chiefComplaint", "history", "examination", "assessment", "plan", "toothChart" };
        if (content.Count == 0 || content.Any(x => !allowed.Contains(x.Key))) throw new ApiError(400, "病历内容字段无效", "Unsupported or empty clinical content");
        foreach (var (key, value) in content)
        {
            if (key == "toothChart")
            {
                if (value is not JsonArray teeth || teeth.Count > 52 || teeth.Any(x => x is not JsonObject tooth || tooth["tooth"] is not JsonValue number || !number.TryGetValue<int>(out var n) || !(n / 10 is >= 1 and <= 8 && n % 10 is >= 1 and <= 8 && (n / 10 <= 4 || n % 10 <= 5))))
                    throw new ApiError(400, "牙位必须使用有效FDI编码", "Tooth chart requires valid FDI tooth numbers");
                var unique = new HashSet<int>();
                foreach (var tooth in teeth.Cast<JsonObject>())
                {
                    Validation.Only(tooth, "tooth", "finding"); Validation.Text(tooth, "finding", 1000, false);
                    if (!unique.Add(tooth["tooth"]!.GetValue<int>())) throw new ApiError(400, "牙位不能重复", "Duplicate tooth number");
                }
            }
            else if (value is not JsonValue text || !text.TryGetValue<string>(out var s) || s.Length > 8000)
                throw new ApiError(400, "病历文本字段无效", "Clinical text field is invalid");
        }
        return content;
    }

    public async Task<JsonObject> Create(string patient, JsonObject body)
    {
        user.Require("doctor"); await access.Patient(patient, true);
        if (!await access.IsClinicianFor(patient)) throw DsoAccess.Missing();
        var content = Content(body);
        Validation.Only(body, "content", "previousId");
        var previous = Validation.Text(body, "previousId", 128, false);
        if (previous != "")
        {
            var original = await Get(previous);
            if (original["patientId"]!.GetValue<string>() != patient || original["status"]!.GetValue<string>() != "signed")
                throw new ApiError(409, "修订必须引用同一患者的已签署病历", "An amendment must reference a signed record for the same patient");
        }
        return await Idempotency.Run(store, user, context, "clinical.create:" + patient, body, async () =>
        {
            var id = "record_" + Guid.NewGuid().ToString("N");
            await access.Execute("INSERT INTO dso_clinical_record(id,tenant_id,clinic_id,patient_id,author_id,content,previous_id) VALUES(@id,@tenant,@clinic,@patient,@actor,CAST(@content AS jsonb),@previous)",
                ("id", id), ("patient", patient), ("content", content.ToJsonString()), ("previous", previous == "" ? null : previous));
            await store.Audit(user, context, previous == "" ? "clinical.record_created" : "clinical.amendment_created", id);
            return await Get(id);
        });
    }

    public async Task<JsonObject> Update(string id, JsonObject body, bool sign)
    {
        Validation.Only(body, sign ? ["version"] : ["version", "content"]);
        user.Require("doctor");
        var record = await Get(id, true);
        if (record["authorId"]!.GetValue<string>() != user.Id) throw new ApiError(403, "只能修改或签署本人病历", "Only the author can edit or sign this record");
        if (record["status"]!.GetValue<string>() != "draft") throw new ApiError(409, "已签署病历不可修改，请创建修订", "Signed records are immutable; create an amendment");
        if (record["version"]!.GetValue<int>() != DsoAccess.Version(body)) throw new ApiError(409, "病历版本冲突", "Clinical record version conflict");
        if (sign)
        {
            await access.Execute("UPDATE dso_clinical_record SET status='signed',signed_at=now(),updated_at=now(),version=version+1 WHERE id=@id AND tenant_id=@tenant AND clinic_id=@clinic", ("id", id));
            await access.Notify(record["patientId"]!.GetValue<string>(), "新病历可查看", "您的医生已签署一份病历，请在健康档案中查看。", "clinical", id);
        }
        else await access.Execute("UPDATE dso_clinical_record SET content=CAST(@content AS jsonb),updated_at=now(),version=version+1 WHERE id=@id AND tenant_id=@tenant AND clinic_id=@clinic", ("id", id), ("content", Content(body).ToJsonString()));
        await store.Audit(user, context, sign ? "clinical.record_signed" : "clinical.record_updated", id);
        return await Get(id);
    }
}
