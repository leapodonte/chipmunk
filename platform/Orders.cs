using System.Text.Json.Nodes;

namespace Chipmunk.Platform;

// 所有角色通过同一订单和事件账本操作；付款金额只由服务端产品快照决定。
public sealed class Orders(Store store, User user, HttpContext context, IConfiguration config)
{
    private readonly DsoAccess access = new(store, user, context);
    private const string Projection = "jsonb_build_object('id',o.id,'clinicId',o.clinic_id,'patientId',o.patient_id,'doctorId',o.doctor_id,'productCode',o.product_code,'productName',o.product_name,'quantity',o.quantity,'amountMinor',o.amount_minor,'currency',o.currency,'status',o.status,'version',o.version,'requestText',o.request_text,'shippingAddress',o.shipping_address,'productionSpec',o.production_spec,'batchRef',o.batch_ref,'carrier',o.carrier,'trackingNumber',o.tracking_number,'createdAt',o.created_at,'updatedAt',o.updated_at)";
    private string Scope => "((@patientRole AND p.user_id=@actor) OR (@doctorRole AND d.user_id=@actor AND d.active) OR @staffRole)";
    private (string, object?)[] Flags => [("patientRole", user.Has("patient")), ("doctorRole", user.Has("doctor")), ("staffRole", user.Has("sales", "manufacturer", "quality", "clinic_manager", "regional_manager", "platform_operator", "platform_admin"))];
    private void RequireRead() => user.Require("patient", "doctor", "sales", "manufacturer", "quality", "clinic_manager", "regional_manager", "platform_operator", "platform_admin");
    public Task<JsonArray> Products() => access.JsonRows("SELECT jsonb_build_object('code',code,'name',name,'priceMinor',price_minor,'currency',currency,'demo',true)::text FROM dso_order_product WHERE active ORDER BY code");
    private async Task<JsonObject> Raw(string id, bool locked = false)
    {
        RequireRead();
        var rows = await access.Rows("SELECT " + Projection + "::text FROM dso_order o JOIN dso_patient p ON p.id=o.patient_id JOIN dso_doctor d ON d.id=o.doctor_id WHERE o.id=@id AND o.tenant_id=@tenant AND o.clinic_id=@clinic AND " + Scope + (locked ? " FOR UPDATE OF o" : ""), Flags.Concat(new[] { ("id", (object?)id) }).ToArray());
        if (rows.Count == 0) throw DsoAccess.Missing();
        return JsonNode.Parse(rows[0][0])!.AsObject();
    }
    private static string S(JsonObject o, string name) => o[name]!.GetValue<string>();
    private string[] Actions(string status) => status switch {
        "requested" => user.Has("patient") ? ["cancel"] : user.Has("doctor") ? ["doctor_approve", "doctor_reject"] : [],
        "pending_payment" => user.Has("patient") ? ["pay_demo", "cancel"] : [],
        "paid" => user.Has("sales") ? ["sales_validate"] : [],
        "sales_validated" => user.Has("manufacturer") ? ["manufacturer_validate"] : [],
        "manufacturing_ready" or "rework_required" => user.Has("manufacturer") ? ["start_manufacturing"] : [],
        "manufacturing" => user.Has("manufacturer") ? ["finish_manufacturing"] : [],
        "qa_pending" => user.Has("quality") ? ["qa_pass", "qa_fail"] : [],
        "qa_passed" => user.Has("manufacturer") ? ["ship"] : [],
        "shipped" => user.Has("patient") ? ["confirm_delivery"] : [], _ => []
    };
    private JsonObject View(JsonObject order)
    {
        var clinical = user.Has("patient", "doctor");
        if (!clinical) order.Remove("requestText");
        if (!clinical && !user.Has("manufacturer", "quality")) order.Remove("productionSpec");
        if (!user.Has("patient", "sales", "manufacturer")) order.Remove("shippingAddress");
        order["allowedActions"] = new JsonArray(Actions(S(order, "status")).Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
        order["paymentMode"] = "demo";
        order["sourceOfTruth"] = "platform";
        order["nextRole"] = S(order, "status") switch { "requested" => "doctor", "pending_payment" or "shipped" => "patient", "paid" => "sales", "qa_pending" => "quality", "sales_validated" or "manufacturing_ready" or "manufacturing" or "rework_required" or "qa_passed" => "manufacturer", _ => "none" };
        return order;
    }
    public async Task<JsonArray> List()
    {
        RequireRead(); var (limit, offset) = access.Page();
        var rows = await access.Rows("SELECT " + Projection + "::text FROM dso_order o JOIN dso_patient p ON p.id=o.patient_id JOIN dso_doctor d ON d.id=o.doctor_id WHERE o.tenant_id=@tenant AND o.clinic_id=@clinic AND " + Scope + " ORDER BY o.created_at DESC,o.id LIMIT @limit OFFSET @offset", Flags.Concat(new[] { ("limit", (object?)limit), ("offset", (object?)offset) }).ToArray());
        return new JsonArray(rows.Select(x => (JsonNode?)View(JsonNode.Parse(x[0])!.AsObject())).ToArray());
    }
    public async Task<JsonObject> Get(string id)
    {
        // 同一事务持有订单行锁，避免当前版本与随后读取的历程来自不同提交。
        var order = View(await Raw(id, true));
        order["timeline"] = await access.JsonRows("SELECT jsonb_build_object('version',version,'action',action,'fromStatus',from_status,'toStatus',to_status,'actorRole',actor_role,'details',CASE WHEN action='doctor_reject' AND NOT @clinical THEN details-'reasonText' ELSE details END,'createdAt',created_at)::text FROM dso_order_event WHERE order_id=@id AND tenant_id=@tenant AND clinic_id=@clinic ORDER BY version", ("id", id), ("clinical", user.Has("patient", "doctor")));
        order["payment"] = (await access.JsonRows("SELECT jsonb_build_object('receiptId',id,'amountMinor',amount_minor,'currency',currency,'provider',provider,'isSimulated',true,'paidAt',paid_at)::text FROM dso_order_payment WHERE order_id=@id AND tenant_id=@tenant AND clinic_id=@clinic", ("id", id))).FirstOrDefault()?.DeepClone();
        await store.Audit(user, context, "order.read", id);
        return order;
    }
    private static JsonObject Address(JsonObject body)
    {
        if (body["shippingAddress"] is not JsonObject address) throw new ApiError(400, "需要收货信息", "Shipping address required");
        Validation.Only(address, "recipient", "phone", "address");
        return new JsonObject { ["recipient"] = Validation.Text(address, "recipient", 100), ["phone"] = Validation.Text(address, "phone", 32), ["address"] = Validation.Text(address, "address", 500) };
    }
    public async Task<JsonObject> Create(JsonObject body)
    {
        user.Require("patient"); Validation.Only(body, "doctorId", "productCode", "quantity", "requestText", "shippingAddress");
        var patient = await access.OwnPatient(); var doctor = Validation.Text(body, "doctorId", 128);
        await access.Doctor(doctor);
        var product = Validation.Text(body, "productCode", 80); var request = Validation.Text(body, "requestText", 2000);
        if (body["quantity"] is not JsonValue q || !q.TryGetValue<int>(out var quantity) || quantity is < 1 or > 4) throw new ApiError(400, "数量须为1–4", "Quantity must be 1–4");
        var address = Address(body);
        var products = await access.Rows("SELECT name,price_minor,currency FROM dso_order_product WHERE code=@code AND active", ("code", product));
        if (products.Count == 0) throw DsoAccess.Missing();
        var created = await Idempotency.Run(store, user, context, "order.create", body, async () => {
            var id = "order_" + Guid.NewGuid().ToString("N");
            await access.Execute("INSERT INTO dso_order(id,tenant_id,clinic_id,patient_id,doctor_id,product_code,product_name,quantity,amount_minor,currency,request_text,shipping_address) VALUES(@id,@tenant,@clinic,@patient,@doctor,@product,@name,@quantity,@amount,@currency,@request,CAST(@address AS jsonb))", ("id", id), ("patient", patient), ("doctor", doctor), ("product", product), ("name", products[0][0]), ("quantity", quantity), ("amount", checked(int.Parse(products[0][1]) * quantity)), ("currency", products[0][2]), ("request", request), ("address", address.ToJsonString()));
            await Event(id, 1, "request", null, "requested", "patient", new JsonObject());
            await Mirror(id);
            await access.Notify(patient, "订单已申请", "您的申请已提交给医生审核。", "order", id);
            return await Get(id);
        });
        return await Get(S(created, "id"));
    }
    private Task Event(string id, int version, string action, string? from, string to, string role, JsonObject details) => access.Execute("INSERT INTO dso_order_event(order_id,tenant_id,clinic_id,version,action,from_status,to_status,actor_id,actor_role,details) VALUES(@id,@tenant,@clinic,@version,@action,@from,@to,@actor,@role,CAST(@details AS jsonb))", ("id", id), ("version", version), ("action", action), ("from", from), ("to", to), ("role", role), ("details", details.ToJsonString()));
    private async Task Mirror(string id)
    {
        var order = await Raw(id);
        await new Commercial(store, user, context).Emit(id, "order.snapshot", new JsonObject {
            ["platformRef"] = id, ["version"] = order["version"]!.DeepClone(), ["status"] = order["status"]!.DeepClone(),
            ["amountMinor"] = order["amountMinor"]!.DeepClone(), ["currency"] = order["currency"]!.DeepClone(),
            ["productCode"] = order["productCode"]!.DeepClone(), ["quantity"] = order["quantity"]!.DeepClone(), ["paymentMode"] = "demo"
        });
    }
    public async Task<JsonObject> Act(string id, string action, JsonObject body)
    {
        var requiredRole = action switch {
            "doctor_approve" or "doctor_reject" => "doctor",
            "pay_demo" or "cancel" or "confirm_delivery" => "patient",
            "sales_validate" => "sales",
            "manufacturer_validate" or "start_manufacturing" or "finish_manufacturing" or "ship" => "manufacturer",
            "qa_pass" or "qa_fail" => "quality", _ => throw new ApiError(400, "订单操作无效", "Invalid order action")
        };
        user.Require(requiredRole);
        // 先验证最新授权再查幂等缓存，撤销身份不能继续重放受保护响应。
        await Raw(id);
        await Idempotency.Run(store, user, context, "order.action:" + id + ":" + action, body, async () => {
            var order = await Raw(id, true); var from = S(order, "status");
            if (!Actions(from).Contains(action)) throw new ApiError(409, "当前角色或订单阶段不允许此操作", "Action is not permitted for your role or order stage");
            if (DsoAccess.Version(body) != order["version"]!.GetValue<int>()) throw new ApiError(409, "订单已更新，请刷新后确认", "Order changed; refresh and confirm");
            var details = new JsonObject(); string to, role;
            var spec = S(order, "productionSpec"); var batch = S(order, "batchRef"); var carrier = S(order, "carrier"); var tracking = S(order, "trackingNumber");
            switch (action) {
                case "doctor_approve":
                    Validation.Only(body, "version", "productionSpec"); spec = Validation.Text(body, "productionSpec", 2000); to = "pending_payment"; role = "doctor"; break;
                case "doctor_reject":
                    Validation.Only(body, "version", "reason"); details["reasonText"] = Validation.Text(body, "reason", 300); to = "rejected"; role = "doctor"; details["reason"] = "doctor_rejected"; break;
                case "cancel": Validation.Only(body, "version"); to = "cancelled"; role = "patient"; break;
                case "pay_demo":
                    Validation.Only(body, "version", "confirmSimulation");
                    if (config["DEMO_AUTH"] != "true" || (config["PAYMENT_MODE"] ?? "demo") != "demo") throw new ApiError(503, "演示付款未启用", "Demo payment is disabled");
                    if (!Validation.Boolean(body, "confirmSimulation")) throw new ApiError(400, "须确认这是模拟付款", "Confirm this is a simulated payment");
                    await access.Execute("INSERT INTO dso_order_payment(id,order_id,tenant_id,clinic_id,amount_minor,currency,provider) VALUES(@receipt,@id,@tenant,@clinic,@amount,@currency,'demo')", ("receipt", "demo_" + Guid.NewGuid().ToString("N")), ("id", id), ("amount", order["amountMinor"]!.GetValue<int>()), ("currency", S(order, "currency")));
                    details["isSimulated"] = true; to = "paid"; role = "patient"; break;
                case "sales_validate": Validation.Only(body, "version"); to = "sales_validated"; role = "sales"; break;
                case "manufacturer_validate": Validation.Only(body, "version"); to = "manufacturing_ready"; role = "manufacturer"; break;
                case "start_manufacturing":
                    Validation.Only(body, "version", "batchRef"); batch = Validation.Text(body, "batchRef", 100); details["batchRef"] = batch; to = "manufacturing"; role = "manufacturer"; break;
                case "finish_manufacturing": Validation.Only(body, "version"); to = "qa_pending"; role = "manufacturer"; break;
                case "qa_pass": case "qa_fail":
                    Validation.Only(body, "version", "checks", "reason");
                    if (body["checks"] is not JsonObject checks) throw new ApiError(400, "需要质检清单", "QA checklist required");
                    Validation.Only(checks, "identity", "specification", "finish", "packaging");
                    foreach (var check in new[] { "identity", "specification", "finish", "packaging" }) {
                        if (!checks.ContainsKey(check)) throw new ApiError(400, "质检清单不完整", "QA checklist incomplete");
                        var passed = Validation.Boolean(checks, check);
                        if (action == "qa_pass" && !passed) throw new ApiError(409, "全部质检项通过才能放行", "All QA checks must pass before release");
                    }
                    var lastMaker = await access.Rows("SELECT actor_id FROM dso_order_event WHERE order_id=@id AND action='finish_manufacturing' ORDER BY version DESC LIMIT 1", ("id", id));
                    if (lastMaker.Count != 1 || lastMaker[0][0] == user.Id) throw new ApiError(409, "质检须由独立人员执行", "QA requires a different person from the production finisher");
                    details["checks"] = checks.DeepClone();
                    if (action == "qa_fail") {
                        if (checks.All(x => x.Value!.GetValue<bool>())) throw new ApiError(400, "退回返工须有未通过的质检项", "Rework requires at least one failed QA check");
                        details["reasonText"] = Validation.Text(body, "reason", 300); details["reason"] = "quality_rework";
                    }
                    to = action == "qa_pass" ? "qa_passed" : "rework_required"; role = "quality"; break;
                case "ship":
                    Validation.Only(body, "version", "carrier", "trackingNumber"); carrier = Validation.Text(body, "carrier", 100); tracking = Validation.Text(body, "trackingNumber", 100);
                    details["carrier"] = carrier; details["trackingNumber"] = tracking; to = "shipped"; role = "manufacturer"; break;
                case "confirm_delivery": Validation.Only(body, "version"); to = "delivered"; role = "patient"; break;
                default: throw new ApiError(400, "订单操作无效", "Invalid order action");
            }
            var version = order["version"]!.GetValue<int>() + 1;
            await access.Execute("UPDATE dso_order SET status=@status,version=@version,production_spec=@spec,batch_ref=@batch,carrier=@carrier,tracking_number=@tracking,updated_at=now() WHERE id=@id AND tenant_id=@tenant AND clinic_id=@clinic", ("status", to), ("version", version), ("spec", spec), ("batch", batch), ("carrier", carrier), ("tracking", tracking), ("id", id));
            await Event(id, version, action, from, to, role, details);
            await Mirror(id);
            await store.Audit(user, context, "order." + action, id, new JsonObject { ["version"] = version, ["status"] = to });
            await access.Notify(S(order, "patientId"), "订单进度已更新", "订单进度已更新，请打开订单查看。", "order", id);
            return await Get(id);
        });
        // 重试返回当前快照；幂等结果只确认原操作已提交，不让界面倒退到旧状态。
        return await Get(id);
    }
}
