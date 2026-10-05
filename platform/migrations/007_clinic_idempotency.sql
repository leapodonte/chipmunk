-- 将旧业务幂等响应归到资源所属门诊，保留同门诊重试结果，避免切换门诊后返回其他门诊数据。
UPDATE platform_idempotency i SET scope=i.scope||':'||r.tenant_id||':'||r.clinic_id
FROM dso_slot r, platform_user u
WHERE i.scope='slots.create' AND i.result->>'id'=r.id AND i.user_id=u.id AND r.tenant_id=u.tenant_id;

UPDATE platform_idempotency i SET scope=i.scope||':'||r.tenant_id||':'||r.clinic_id
FROM dso_appointment r, platform_user u
WHERE i.scope='appointments.book' AND i.result->>'id'=r.id AND i.user_id=u.id AND r.tenant_id=u.tenant_id;

UPDATE platform_idempotency i SET scope=i.scope||':'||r.tenant_id||':'||r.clinic_id
FROM dso_clinical_record r, platform_user u
WHERE i.scope='clinical.create:'||r.patient_id AND i.result->>'id'=r.id AND i.user_id=u.id AND r.tenant_id=u.tenant_id;

UPDATE platform_idempotency i SET scope=i.scope||':'||r.tenant_id||':'||r.clinic_id
FROM dso_crm_lead r, platform_user u
WHERE i.scope='crm.create' AND i.result->>'id'=r.id AND i.user_id=u.id AND r.tenant_id=u.tenant_id;

UPDATE platform_idempotency i SET scope=i.scope||':'||r.tenant_id||':'||r.clinic_id
FROM dso_crm_activity r, platform_user u
WHERE i.scope='crm.activity:'||r.lead_id AND i.result->>'id'=r.id AND i.user_id=u.id AND r.tenant_id=u.tenant_id;

-- 原版小程序打卡仅在用户的固定所属门诊运行，其缓存没有资源ID。
UPDATE platform_idempotency i SET scope=i.scope||':'||u.tenant_id||':'||u.clinic_id
FROM platform_user u WHERE i.scope='checkin' AND i.user_id=u.id;
