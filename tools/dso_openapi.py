"""DSO 已实现路由的契约定义；供统一 OpenAPI 生成器使用。"""


def extend(paths, schemas):
    def field(kind="string", **options):
        return {"type": kind, **options}

    def ref(name):
        return {"$ref": "#/components/schemas/" + name}

    def operation(method, path, summary, response=None, body=None, required=(), idem=False, paged=False):
        parameters = [{"name": part[1:-1], "in": "path", "required": True, "schema": field()} for part in path.split("/") if part.startswith("{")]
        if idem:
            parameters.append({"name": "Idempotency-Key", "in": "header", "required": True, "schema": field(minLength=1, maxLength=128), "description": "同一业务重试保持相同键和请求正文；更改正文返回409"})
        if paged:
            parameters += [{"name": "page", "in": "query", "schema": field("integer", minimum=1, maximum=10000, default=1)}, {"name": "pageSize", "in": "query", "schema": field("integer", minimum=1, maximum=100, default=20)}]
        data = field("object") if response is None else ref(response[:-2]) if response.endswith("[]") else ref(response)
        if response and response.endswith("[]"):
            data = field("array", items=data)
        result = {
            "summary": summary, "tags": ["DSO / " + path.split("/")[1]],
            "operationId": "dso_" + method + "_" + path.strip("/").replace("/", "_").replace("{", "").replace("}", ""),
            "security": [{"bearerAuth": []}], "parameters": parameters,
            "responses": {"200": {"description": "成功", "content": {"application/json": {"schema": {"allOf": [ref("Envelope"), field("object", properties={"data": data})]}}}}},
        }
        if body is not None:
            result["requestBody"] = {"required": True, "content": {"application/json": {"schema": field("object", properties=body, required=list(required), additionalProperties=False)}}}
        for code, message in [(400, "参数无效"), (401, "凭据无效或过期"), (403, "角色权限不足"), (404, "资源不存在或不在授权范围"), (409, "幂等、版本或状态冲突"), (413, "正文超过64KiB"), (429, "频控"), (500, "服务器错误")]:
            result["responses"][str(code)] = {"description": message, "content": {"application/json": {"schema": ref("Envelope")}}}
        paths.setdefault("/api/dso/v1" + path, {})[method] = result

    def shape(name, properties, required=()):
        schemas[name] = field("object", properties=properties, required=list(required))

    version = field("integer", minimum=1)
    date = field(format="date-time")
    nullable = field(nullable=True)
    roles = ["patient", "doctor", "assistant", "consultant", "clinic_manager", "regional_manager", "supplier", "platform_operator", "platform_admin"]
    shape("DsoContext", {"userId": field(), "tenantId": field(), "organizationId": field(), "clinicId": field(), "roles": field("array", items=field(enum=roles)), "environment": field(enum=["dev-demo"])})
    shape("DsoClinic", {"id": field(), "name": field(), "organizationId": field()})
    shape("DsoCareTeam", {"doctorId": field(), "name": field(), "grantedAt": date})
    shape("DsoDoctor", {"id": field(), "name": field(), "active": field("boolean"), "canSchedule": field("boolean")})
    profile = field("object", properties={"gender": field(enum=["male", "female", "other", "unknown"]), "birthDate": field(format="date"), "allergies": field(maxLength=3000), "medicalHistory": field(maxLength=3000), "emergencyContact": field("object", properties={"name": field(maxLength=100), "phone": field(maxLength=32), "relationship": field(maxLength=100)}, required=["name", "phone"], additionalProperties=False)}, additionalProperties=False)
    shape("DsoPatient", {"id": field(), "displayName": field(), "profile": profile, "version": version, "createdAt": date})
    shape("DsoSlot", {"id": field(), "doctorId": field(), "startsAt": date, "endsAt": date, "status": field(enum=["open", "closed"]), "available": field("boolean")})
    shape("DsoAppointment", {"id": field(), "patientId": field(), "patientName": field(), "doctorId": field(), "doctorName": field(), "slotId": field(), "startsAt": date, "endsAt": date, "status": field(enum=["booked", "arrived", "completed", "cancelled", "no_show"]), "version": version, "createdAt": date})
    teeth = [q * 10 + n for q in range(1, 9) for n in range(1, 9 if q <= 4 else 6)]
    content = field("object", properties={**{name: field(maxLength=8000) for name in ["chiefComplaint", "history", "examination", "assessment", "plan"]}, "toothChart": field("array", maxItems=52, items=field("object", properties={"tooth": field("integer", enum=teeth), "finding": field(maxLength=1000)}, required=["tooth"], additionalProperties=False))}, additionalProperties=False, description="整体内容不超过32KiB；牙位不能重复。")
    schemas["DsoClinicalContent"] = content
    shape("DsoRecord", {"id": field(), "patientId": field(), "authorId": field(), "status": field(enum=["draft", "signed"]), "version": version, "content": ref("DsoClinicalContent"), "previousId": nullable, "createdAt": date, "signedAt": field(format="date-time", nullable=True)})
    stages = ["new", "contacted", "qualified", "won", "lost"]
    shape("DsoLead", {"id": field(), "patientId": nullable, "originRef": field(), "title": field(), "stage": field(enum=stages), "assignedTo": nullable, "version": version, "createdAt": date})
    shape("DsoActivity", {"id": field(), "leadId": field(), "type": field(enum=["call", "follow_up", "visit"]), "summary": field(), "dueAt": date, "status": field(enum=["pending", "done", "cancelled"]), "version": version, "createdAt": date, "completedAt": field(format="date-time", nullable=True)})
    shape("DsoOutbox", {"id": field(), "eventType": field(), "aggregateId": field(), "status": field(enum=["pending", "processing", "delivered", "dead_letter"]), "attempts": field("integer"), "nextAttempt": date, "leaseUntil": field(format="date-time", nullable=True), "hasError": field("boolean"), "createdAt": date, "deliveredAt": field(format="date-time", nullable=True)})
    shape("DsoAudit", {"id": field("integer"), "clinicId": nullable, "actorId": field(), "action": field(), "resourceId": field(), "requestId": field(), "details": field("object"), "createdAt": date})
    shape("DsoSummary", {"clinicId": field(), "patients": field("integer"), "bookedAppointments": field("integer"), "draftRecords": field("integer"), "integrationQueue": field("array", items=field("object")), "aiMode": field(enum=["mock"]), "storage": field(enum=["disk"])})

    operation("get", "/context", "当前会话的可信租户、机构、门诊和角色", "DsoContext")
    operation("get", "/clinics/mine", "本人获授权门诊", "DsoClinic[]")
    operation("post", "/context/switch", "切换当前会话门诊；随后刷新 context", body={"clinicId": field()}, required=["clinicId"])
    operation("get", "/doctors", "当前门诊的活动医生", "DsoDoctor[]")
    operation("get", "/slots", "未来可预约时段", "DsoSlot[]", paged=True)
    paths["/api/dso/v1/slots"]["get"]["parameters"].append({"name": "doctorId", "in": "query", "schema": field()})
    operation("post", "/slots", "医生/管理者创建未来180天内、最长4小时时段", "DsoSlot", {"doctorId": field(), "startsAt": date, "endsAt": date}, ["doctorId", "startsAt", "endsAt"], idem=True)
    operation("post", "/slots/{id}/close", "关闭未被占用时段", body={})
    operation("get", "/appointments", "员工按权限查看门诊预约", "DsoAppointment[]", paged=True)
    operation("get", "/appointments/mine", "患者本人预约", "DsoAppointment[]", paged=True)
    for endpoint in ['/appointments', '/appointments/mine']:
        paths['/api/dso/v1' + endpoint]['get']['parameters'] += [{'name': name, 'in':'query', 'schema':date, 'description':'from/to 必须同时传入，正区间最长93天；返回与区间重叠的预约或时段'} for name in ['from','to']]
    operation("post", "/appointments", "患者预约时段；共享临床资料需显式同意", "DsoAppointment", {"slotId": field(), "shareWithDoctor": field("boolean", default=False)}, ["slotId"], idem=True)
    operation("get", "/appointments/{id}", "查看获授权预约", "DsoAppointment")
    operation("post", "/appointments/{id}/status", "有权限的状态转换；患者仅可取消未来本人预约", "DsoAppointment", {"version": version, "status": field(enum=["arrived", "completed", "cancelled", "no_show"])}, ["version", "status"])
    operation("get", "/patients", "医生仅获授权患者；管理者仅最小人口信息", "DsoPatient[]", paged=True)
    operation("get", "/patients/me", "患者本人档案", "DsoPatient")
    operation("patch", "/patients/me", "本人更新档案；需当前 version", "DsoPatient", {"displayName": field(minLength=1, maxLength=100), "profile": profile, "version": version}, ["displayName", "profile", "version"])
    operation("get", "/patients/me/care-team", "本人临床资料授权医生", "DsoCareTeam[]")
    for method in ["post", "delete"]:
        operation(method, "/patients/me/care-team/{doctorId}", "本人授予/撤销当前门诊医生访问临床资料", body={} if method == "post" else None)
    operation("get", "/patients/{id}", "查看获授权患者；不向管理角色暴露临床 profile", "DsoPatient")
    operation("get", "/patients/me/records", "本人已签署病历", "DsoRecord[]", paged=True)
    operation("get", "/patients/{id}/records", "获授权医生查看患者病历", "DsoRecord[]", paged=True)
    operation("post", "/patients/{id}/records", "医生创建草稿或修订已签署记录", "DsoRecord", {"content": ref("DsoClinicalContent"), "previousId": nullable}, ["content"], idem=True)
    operation("get", "/clinical-records/{id}", "查看获授权病历；患者只能读取已签署版本", "DsoRecord")
    operation("patch", "/clinical-records/{id}", "作者修改草稿；签署记录数据库禁止覆盖", "DsoRecord", {"content": ref("DsoClinicalContent"), "version": version}, ["content", "version"])
    operation("post", "/clinical-records/{id}/sign", "作者签署病历并通知患者", "DsoRecord", {"version": version}, ["version"])
    operation("get", "/crm/leads", "平台内咨询线索", "DsoLead[]", paged=True)
    operation("post", "/crm/leads", "创建线索及最小商业引用 outbox 事件", "DsoLead", {"title": field(minLength=1, maxLength=150), "patientId": nullable}, ["title"], idem=True)
    operation("get", "/crm/leads/{id}", "获授权线索", "DsoLead")
    operation("patch", "/crm/leads/{id}", "线索阶段/分配；won/lost不能重新打开", "DsoLead", {"version": version, "stage": field(enum=stages), "assignedTo": nullable}, ["version"])
    operation("get", "/crm/leads/{id}/activities", "获授权线索的跟进", "DsoActivity[]", paged=True)
    operation("post", "/crm/leads/{id}/activities", "创建跟进；到期时间不能超过一年", "DsoActivity", {"type": field(enum=["call", "follow_up", "visit"]), "summary": field(minLength=1, maxLength=300), "dueAt": date}, ["type", "summary", "dueAt"], idem=True)
    operation("patch", "/crm/activities/{id}", "完成/取消待处理跟进", "DsoActivity", {"version": version, "status": field(enum=["done", "cancelled"])}, ["version", "status"])
    operation("get", "/operations/summary", "平台运维查看聚合计数，不读取临床正文", "DsoSummary")
    operation("get", "/operations/outbox", "租户商业队列状态，不返回 payload", "DsoOutbox[]", paged=True)
    paths["/api/dso/v1/operations/outbox"]["get"]["parameters"].append({"name": "status", "in": "query", "schema": field(enum=["pending", "processing", "delivered", "dead_letter"])})
    operation("get", "/operations/audit", "租户审计元数据", "DsoAudit[]", paged=True)
    operation("post", "/operations/outbox/{id}/retry", "记录原因后重放死信，保持原事件 ID", body={"reason": field(minLength=1, maxLength=300)}, required=["reason"])

    import copy
    paths["/api/dso/v1/patients/me"]["put"] = copy.deepcopy(paths["/api/dso/v1/patients/me"]["patch"])
    paths["/api/dso/v1/patients/me"]["put"]["operationId"] = "dso_put_patients_me"
    paths["/api/dso/v1/patients/me"]["put"]["summary"] = "完整替换本人资料；微信小程序兼容方法"
    staff = copy.deepcopy(paths['/api/v1/auth/mp-login']['post'])
    staff.update(summary='独立员工开发凭据登录；患者开发密钥无效', operationId='post_auth_staff_login')
    staff['parameters'] = [{'name':'X-Staff-Dev-Key','in':'header','required':True,'schema':field(minLength=32,maxLength=256)}]
    staff['requestBody'] = {'required':True,'content':{'application/json':{'schema':field('object',properties={'identity':field(example='staff:doctor')},required=['identity'])}}}
    paths['/api/v1/auth/staff-login'] = {'post':staff}
    cancel = copy.deepcopy(paths['/api/v1/auth/logout']['post'])
    cancel.update(summary='取消本人排队/运行中的持久化演示任务',operationId='post_ai_simulation_cancel')
    cancel['parameters'] = [{'name':'taskId','in':'path','required':True,'schema':field()}]
    paths['/api/v1/ai/simulations/{taskId}/cancel'] = {'post':cancel}
    schemas['Simulation']['properties'].update({'jobStatus':field(enum=['queued','running','succeeded','failed','cancelled']),'provider':field(enum=['mock']),'failureCode':field(nullable=True)})
    schemas['UploadResult']['properties'].update({'width':field('integer'),'height':field('integer')})
    for suffix in ['appointments/mine','patients/me/records','patients/me','patients/me/care-team','slots','appointments','appointments/{id}','appointments/{id}/status','patients/me/care-team/{doctorId}']:
        source = paths.get('/api/dso/v1/' + suffix)
        if source:
            paths['/api/v1/' + suffix] = copy.deepcopy(source)
            for op in paths['/api/v1/' + suffix].values():
                op['operationId'] = 'mini_' + op['operationId']

    paths["/api/v1/appointments"].pop("get", None)
