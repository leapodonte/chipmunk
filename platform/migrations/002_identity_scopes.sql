-- 将租户边界变为数据库约束，补充机构/门诊级角色；已有演示数据原地升级。
ALTER TABLE platform_organization ADD CONSTRAINT organization_tenant_unique UNIQUE(id,tenant_id);
ALTER TABLE platform_clinic ADD COLUMN tenant_id text;
UPDATE platform_clinic c SET tenant_id=o.tenant_id FROM platform_organization o WHERE c.organization_id=o.id;
ALTER TABLE platform_clinic ALTER COLUMN tenant_id SET NOT NULL;
ALTER TABLE platform_clinic ADD CONSTRAINT clinic_organization_tenant_fk FOREIGN KEY(organization_id,tenant_id) REFERENCES platform_organization(id,tenant_id);
ALTER TABLE platform_clinic ADD CONSTRAINT clinic_tenant_unique UNIQUE(id,tenant_id);
ALTER TABLE platform_user ADD CONSTRAINT user_tenant_unique UNIQUE(id,tenant_id);
ALTER TABLE platform_user ADD CONSTRAINT user_clinic_tenant_fk FOREIGN KEY(clinic_id,tenant_id) REFERENCES platform_clinic(id,tenant_id);
ALTER TABLE platform_resource ADD CONSTRAINT resource_clinic_tenant_fk FOREIGN KEY(clinic_id,tenant_id) REFERENCES platform_clinic(id,tenant_id);
ALTER TABLE platform_resource ADD CONSTRAINT resource_owner_tenant_fk FOREIGN KEY(owner_id,tenant_id) REFERENCES platform_user(id,tenant_id);
ALTER TABLE platform_session ADD COLUMN clinic_id text REFERENCES platform_clinic(id);
UPDATE platform_session s SET clinic_id=u.clinic_id FROM platform_user u WHERE s.user_id=u.id;
ALTER TABLE platform_session ALTER COLUMN clinic_id SET NOT NULL;
CREATE TABLE platform_role_assignment(
 id text PRIMARY KEY, user_id text NOT NULL, tenant_id text NOT NULL,
 organization_id text, clinic_id text, role text NOT NULL,
 created_at timestamptz NOT NULL DEFAULT now(),
 FOREIGN KEY(user_id,tenant_id) REFERENCES platform_user(id,tenant_id),
 FOREIGN KEY(organization_id,tenant_id) REFERENCES platform_organization(id,tenant_id),
 FOREIGN KEY(clinic_id,tenant_id) REFERENCES platform_clinic(id,tenant_id),
 CHECK(role IN ('patient','doctor','assistant','consultant','clinic_manager','regional_manager','supplier','platform_operator','platform_admin')),
 CHECK(NOT (organization_id IS NOT NULL AND clinic_id IS NOT NULL)),
 CHECK(role NOT IN ('patient','doctor','assistant','consultant','clinic_manager') OR clinic_id IS NOT NULL));
CREATE UNIQUE INDEX role_assignment_scope ON platform_role_assignment(user_id,role,COALESCE(organization_id,''),COALESCE(clinic_id,''));
INSERT INTO platform_role_assignment(id,user_id,tenant_id,clinic_id,role)
 SELECT 'role_patient_'||id,id,tenant_id,clinic_id,'patient' FROM platform_user;
CREATE INDEX session_expiry ON platform_session(expires_at);
CREATE INDEX outbox_pending ON integration_outbox(next_attempt,created_at) WHERE status='pending';
