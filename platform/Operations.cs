using System.Text.Json.Nodes;

namespace Chipmunk.Platform;

public sealed class Operations(Store store, User user, HttpContext context)
{
    private readonly DsoAccess access = new(store, user, context);
    private void Require() => user.Require("platform_operator", "platform_admin");
    public async Task<JsonObject> Summary()
    {
        Require();
        var queue = await access.JsonRows("SELECT jsonb_build_object('status',status,'count',count(*),'oldestCreatedAt',min(created_at))::text FROM integration_outbox WHERE tenant_id=@tenant GROUP BY status ORDER BY status");
        var counts = await access.Rows("SELECT (SELECT count(*) FROM dso_patient WHERE tenant_id=@tenant AND clinic_id=@clinic),(SELECT count(*) FROM dso_appointment WHERE tenant_id=@tenant AND clinic_id=@clinic AND status='booked'),(SELECT count(*) FROM dso_clinical_record WHERE tenant_id=@tenant AND clinic_id=@clinic AND status='draft')");
        return new JsonObject { ["clinicId"] = user.Clinic, ["patients"] = int.Parse(counts[0][0]), ["bookedAppointments"] = int.Parse(counts[0][1]), ["draftRecords"] = int.Parse(counts[0][2]), ["integrationQueue"] = queue, ["aiMode"] = "mock", ["storage"] = "disk" };
    }
    public async Task<JsonArray> Outbox()
    {
        Require(); var (limit, offset) = access.Page();
        var status = context.Request.Query["status"].ToString();
        if (status != "" && status is not ("pending" or "processing" or "delivered" or "dead_letter")) throw new ApiError(400, "队列状态无效", "Invalid queue status");
        return await access.JsonRows("SELECT jsonb_build_object('id',id,'eventType',event_type,'aggregateId',aggregate_id,'status',status,'attempts',attempts,'nextAttempt',next_attempt,'leaseUntil',lease_until,'hasError',last_error IS NOT NULL,'createdAt',created_at,'deliveredAt',delivered_at)::text FROM integration_outbox WHERE tenant_id=@tenant AND (@status='' OR status=@status) ORDER BY created_at DESC,id LIMIT @limit OFFSET @offset", ("status", status), ("limit", limit), ("offset", offset));
    }
    public async Task<JsonObject> Retry(string id, JsonObject body)
    {
        Require(); Validation.Only(body, "reason"); var reason = Validation.Text(body, "reason", 300);
        var changed = await access.Execute("UPDATE integration_outbox SET status='pending',attempts=0,next_attempt=now(),lease_token=NULL,lease_until=NULL,last_error=NULL,updated_at=now() WHERE id=@id AND tenant_id=@tenant AND status='dead_letter'", ("id", id));
        if (changed != 1) throw new ApiError(409, "事件不存在或不在死信队列", "Event is missing or is not dead-lettered");
        await store.Audit(user, context, "integration.dead_letter_retried", id, new JsonObject { ["reason"] = reason });
        return new JsonObject { ["id"] = id, ["status"] = "pending" };
    }
    public async Task<JsonArray> Audit()
    {
        Require(); var (limit, offset) = access.Page();
        return await access.JsonRows("SELECT jsonb_build_object('id',id,'clinicId',clinic_id,'actorId',user_id,'action',action,'resourceId',resource_id,'requestId',request_id,'details',details,'createdAt',created_at)::text FROM platform_audit WHERE tenant_id=@tenant ORDER BY created_at DESC,id DESC LIMIT @limit OFFSET @offset", ("limit", limit), ("offset", offset));
    }
}
