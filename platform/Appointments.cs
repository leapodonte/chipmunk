using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Chipmunk.Platform;

public sealed class Appointments(Store store, User user, HttpContext context)
{
    private readonly DsoAccess access = new(store, user, context);
    private const string Projection = "jsonb_build_object('id',a.id,'patientId',a.patient_id,'patientName',p.display_name,'doctorId',s.doctor_id,'doctorName',d.display_name,'slotId',s.id,'startsAt',s.starts_at,'endsAt',s.ends_at,'status',a.status,'version',a.version,'createdAt',a.created_at)";

    private static DateTimeOffset Time(JsonObject body, string name)
    {
        var value = Auth.Required(body, name);
        if (!Regex.IsMatch(value, @"(Z|[+-]\d{2}:\d{2})$") || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            throw new ApiError(400, "时间必须包含明确时区", "Time must be ISO 8601 with an explicit timezone");
        return time.ToUniversalTime();
    }

    public async Task<JsonObject> CreateSlot(JsonObject body)
    {
        Validation.Only(body, "doctorId", "startsAt", "endsAt");
        user.Require("doctor", "clinic_manager", "regional_manager", "platform_admin");
        var doctor = Auth.Required(body, "doctorId");
        await access.Doctor(doctor, true);
        var start = Time(body, "startsAt"); var end = Time(body, "endsAt");
        if (start < DateTimeOffset.UtcNow || start > DateTimeOffset.UtcNow.AddDays(180) || end <= start || end - start > TimeSpan.FromHours(4))
            throw new ApiError(400, "排班需在未来180天内，时长不超过4小时", "Slot must start in the next 180 days and last at most four hours");
        return await Idempotency.Run(store, user, context, "slots.create", body, async () =>
        {
            await store.Lock("doctor-calendar:" + user.Tenant + ":" + doctor);
            var overlaps = await access.Rows("SELECT 1 FROM dso_slot WHERE tenant_id=@tenant AND clinic_id=@clinic AND doctor_id=@doctor AND status='open' AND starts_at<@end AND ends_at>@start", ("doctor", doctor), ("start", start), ("end", end));
            if (overlaps.Count != 0) throw new ApiError(409, "排班时段重叠", "Doctor has an overlapping slot");
            var id = "slot_" + Guid.NewGuid().ToString("N");
            await access.Execute("INSERT INTO dso_slot(id,tenant_id,clinic_id,doctor_id,starts_at,ends_at,created_by) VALUES(@id,@tenant,@clinic,@doctor,@start,@end,@actor)", ("id", id), ("doctor", doctor), ("start", start), ("end", end));
            await store.Audit(user, context, "appointment.slot_created", id);
            return new JsonObject { ["id"] = id, ["doctorId"] = doctor, ["startsAt"] = start.ToString("O"), ["endsAt"] = end.ToString("O"), ["status"] = "open" };
        });
    }

    public async Task<JsonArray> Slots()
    {
        var doctor = context.Request.Query["doctorId"].ToString();
        if (doctor != "") await access.Doctor(doctor);
        var (limit, offset) = access.Page();
        var (filtered, start, end) = Range();
        return await access.JsonRows("SELECT jsonb_build_object('id',s.id,'doctorId',s.doctor_id,'startsAt',s.starts_at,'endsAt',s.ends_at,'available',NOT EXISTS(SELECT 1 FROM dso_appointment a WHERE a.slot_id=s.id AND a.status!='cancelled'))::text FROM dso_slot s WHERE s.tenant_id=@tenant AND s.clinic_id=@clinic AND s.status='open' AND s.starts_at>now() AND (@doctor='' OR s.doctor_id=@doctor) AND (NOT @filtered OR (s.starts_at<@end AND s.ends_at>@start)) ORDER BY s.starts_at,s.id LIMIT @limit OFFSET @offset", ("doctor", doctor), ("limit", limit), ("offset", offset), ("filtered", filtered), ("start", start.UtcDateTime), ("end", end.UtcDateTime));
    }

    public async Task<JsonObject> CloseSlot(string id)
    {
        user.Require("doctor", "clinic_manager", "regional_manager", "platform_admin");
        var slots = await access.Rows("SELECT doctor_id,status FROM dso_slot WHERE id=@id AND tenant_id=@tenant AND clinic_id=@clinic FOR UPDATE", ("id", id));
        if (slots.Count == 0) throw DsoAccess.Missing();
        await access.Doctor(slots[0][0], true);
        if ((await access.Rows("SELECT 1 FROM dso_appointment WHERE slot_id=@id AND tenant_id=@tenant AND clinic_id=@clinic AND status!='cancelled'", ("id", id))).Count != 0)
            throw new ApiError(409, "已有预约，不能关闭时段", "An occupied slot cannot be closed");
        await access.Execute("UPDATE dso_slot SET status='closed' WHERE id=@id AND tenant_id=@tenant AND clinic_id=@clinic", ("id", id));
        await store.Audit(user, context, "appointment.slot_closed", id);
        return new JsonObject { ["id"] = id, ["status"] = "closed" };
    }

    public async Task<JsonObject> Book(JsonObject body)
    {
        Validation.Only(body, "slotId", "shareWithDoctor");
        var share = Validation.Boolean(body, "shareWithDoctor");
        var patient = await access.OwnPatient();
        var slot = Auth.Required(body, "slotId");
        return await Idempotency.Run(store, user, context, "appointments.book", body, async () =>
        {
            var rows = await access.Rows("SELECT doctor_id FROM dso_slot WHERE id=@id AND tenant_id=@tenant AND clinic_id=@clinic AND status='open' AND starts_at>now() FOR UPDATE", ("id", slot));
            if (rows.Count == 0) throw DsoAccess.Missing();
            await access.Doctor(rows[0][0]);
            if ((await access.Rows("SELECT 1 FROM dso_appointment WHERE slot_id=@id AND tenant_id=@tenant AND clinic_id=@clinic AND status!='cancelled'", ("id", slot))).Count != 0)
                throw new ApiError(409, "时段已被预约", "Slot has already been booked");
            var id = "appointment_" + Guid.NewGuid().ToString("N");
            await access.Execute("INSERT INTO dso_appointment(id,tenant_id,clinic_id,patient_id,slot_id) VALUES(@id,@tenant,@clinic,@patient,@slot)", ("id", id), ("patient", patient), ("slot", slot));
            if (share)
            {
                await access.Execute("INSERT INTO dso_care_team(tenant_id,clinic_id,patient_id,doctor_id) VALUES(@tenant,@clinic,@patient,@doctor) ON CONFLICT DO NOTHING", ("patient", patient), ("doctor", rows[0][0]));
                await store.Audit(user, context, "clinical.doctor_access_granted", patient);
            }
            await access.Notify(patient, "预约成功", "您的预约已登记，请查看预约详情。", "appointment", id);
            await store.Audit(user, context, "appointment.booked", id);
            return await Get(id);
        });
    }

    public async Task<JsonArray> List(bool own)
    {
        string predicate;
        if (own) { user.Require("patient"); predicate = "p.user_id=@actor"; }
        else
        {
            user.Require("doctor", "assistant", "clinic_manager", "regional_manager", "platform_admin");
            predicate = user.Has("clinic_manager", "regional_manager", "platform_admin", "assistant") ? "TRUE" : "d.user_id=@actor";
        }
        var (limit, offset) = access.Page();
        var (filtered, start, end) = Range();
        return await access.JsonRows("SELECT " + Projection + "::text FROM dso_appointment a JOIN dso_slot s ON s.id=a.slot_id JOIN dso_patient p ON p.id=a.patient_id JOIN dso_doctor d ON d.id=s.doctor_id WHERE a.tenant_id=@tenant AND a.clinic_id=@clinic AND " + predicate + " AND (NOT @filtered OR (s.starts_at<@end AND s.ends_at>@start)) ORDER BY s.starts_at DESC,a.id LIMIT @limit OFFSET @offset", ("limit", limit), ("offset", offset), ("filtered", filtered), ("start", start.UtcDateTime), ("end", end.UtcDateTime));
    }

    private (bool Filtered, DateTimeOffset Start, DateTimeOffset End) Range()
    {
        var range = new JsonObject { ["from"] = context.Request.Query["from"].ToString(), ["to"] = context.Request.Query["to"].ToString() };
        var filtered = range["from"]!.GetValue<string>() != "" || range["to"]!.GetValue<string>() != "";
        var start = filtered ? Validation.Time(range, "from") : DateTimeOffset.UtcNow;
        var end = filtered ? Validation.Time(range, "to") : start;
        if (filtered && (end <= start || end - start > TimeSpan.FromDays(93))) throw new ApiError(400, "时间查询区间必须为不超过93天的正区间", "Time range must be positive and no longer than 93 days");
        return (filtered, start, end);
    }

    public async Task<JsonObject> Get(string id, bool locked = false)
    {
        var rows = await access.Rows("SELECT " + Projection + "::text,p.user_id,d.user_id FROM dso_appointment a JOIN dso_slot s ON s.id=a.slot_id JOIN dso_patient p ON p.id=a.patient_id JOIN dso_doctor d ON d.id=s.doctor_id WHERE a.id=@id AND a.tenant_id=@tenant AND a.clinic_id=@clinic" + (locked ? " FOR UPDATE OF a" : ""), ("id", id));
        if (rows.Count == 0 || !(rows[0][1] == user.Id && user.Has("patient") || rows[0][2] == user.Id && user.Has("doctor") || user.Has("clinic_manager", "regional_manager", "platform_admin", "assistant"))) throw DsoAccess.Missing();
        return JsonNode.Parse(rows[0][0])!.AsObject();
    }

    public async Task<JsonObject> Transition(string id, JsonObject body)
    {
        Validation.Only(body, "status", "version");
        var appointment = await Get(id, true);
        var target = Auth.Required(body, "status"); var current = appointment["status"]!.GetValue<string>();
        if (DsoAccess.Version(body) != appointment["version"]!.GetValue<int>()) throw new ApiError(409, "预约已更新，请刷新后重试", "Appointment version conflict");
        if (user.Has("patient") && !user.Has("doctor", "assistant", "clinic_manager", "regional_manager", "platform_admin"))
        {
            if (target != "cancelled" || current != "booked" || DateTimeOffset.Parse(appointment["startsAt"]!.GetValue<string>(), CultureInfo.InvariantCulture) <= DateTimeOffset.UtcNow)
                throw new ApiError(409, "患者只能取消尚未开始的预约", "Patients can only cancel a future booked appointment");
        }
        else
        {
            user.Require("doctor", "assistant", "clinic_manager", "regional_manager", "platform_admin");
            if (!(current == "booked" && target is "arrived" or "cancelled" or "no_show" || current == "arrived" && target is "completed" or "cancelled"))
                throw new ApiError(409, "无效预约状态转换", "Invalid appointment transition");
            if (target == "no_show" && DateTimeOffset.Parse(appointment["endsAt"]!.GetValue<string>(), CultureInfo.InvariantCulture) > DateTimeOffset.UtcNow || target == "completed" && DateTimeOffset.Parse(appointment["startsAt"]!.GetValue<string>(), CultureInfo.InvariantCulture) > DateTimeOffset.UtcNow)
                throw new ApiError(409, "预约时间尚未到达，不能标记此状态", "Appointment time has not been reached for this transition");
        }
        await access.Execute("UPDATE dso_appointment SET status=@status,version=version+1,updated_at=now() WHERE id=@id AND tenant_id=@tenant AND clinic_id=@clinic", ("status", target), ("id", id));
        await access.Notify(appointment["patientId"]!.GetValue<string>(), "预约状态更新", "预约状态已更新，请查看详情。", "appointment", id);
        await store.Audit(user, context, "appointment." + target, id);
        return await Get(id);
    }
}
