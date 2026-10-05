ALTER TABLE integration_outbox ADD COLUMN lease_token text;
ALTER TABLE integration_outbox ADD COLUMN lease_until timestamptz;
ALTER TABLE integration_outbox ADD COLUMN delivered_at timestamptz;
ALTER TABLE integration_outbox ADD COLUMN updated_at timestamptz NOT NULL DEFAULT now();
ALTER TABLE integration_outbox ADD CONSTRAINT outbox_status_valid CHECK(status IN ('pending','processing','delivered','dead_letter'));
CREATE INDEX outbox_lease ON integration_outbox(lease_until) WHERE status='processing';
ALTER TABLE platform_rate_limit ADD COLUMN expires_at timestamptz NOT NULL DEFAULT now()+interval '2 days';
CREATE INDEX rate_limit_expiry ON platform_rate_limit(expires_at);
ALTER TABLE platform_audit ADD COLUMN clinic_id text;
ALTER TABLE platform_audit ADD COLUMN details jsonb NOT NULL DEFAULT '{}';
CREATE TABLE dso_crm_lead(
 id text PRIMARY KEY, tenant_id text NOT NULL, clinic_id text NOT NULL, patient_id text,
 origin_ref text NOT NULL, title text NOT NULL, stage text NOT NULL DEFAULT 'new',
 assigned_to text, version integer NOT NULL DEFAULT 1, created_by text NOT NULL,
 created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,origin_ref), UNIQUE(id,tenant_id,clinic_id),
 CHECK(stage IN ('new','contacted','qualified','won','lost')),
 FOREIGN KEY(clinic_id,tenant_id) REFERENCES platform_clinic(id,tenant_id),
 FOREIGN KEY(patient_id,tenant_id,clinic_id) REFERENCES dso_patient(id,tenant_id,clinic_id),
 FOREIGN KEY(assigned_to,tenant_id) REFERENCES platform_user(id,tenant_id),
 FOREIGN KEY(created_by,tenant_id) REFERENCES platform_user(id,tenant_id));
CREATE INDEX crm_lead_pipeline ON dso_crm_lead(tenant_id,clinic_id,stage,created_at DESC);
CREATE TABLE dso_crm_activity(
 id text PRIMARY KEY, tenant_id text NOT NULL, clinic_id text NOT NULL, lead_id text NOT NULL,
 type text NOT NULL, summary text NOT NULL, due_at timestamptz NOT NULL,
 status text NOT NULL DEFAULT 'pending', version integer NOT NULL DEFAULT 1, created_by text NOT NULL,
 created_at timestamptz NOT NULL DEFAULT now(), completed_at timestamptz,
 CHECK(type IN ('call','follow_up','visit')), CHECK(status IN ('pending','done','cancelled')),
 FOREIGN KEY(lead_id,tenant_id,clinic_id) REFERENCES dso_crm_lead(id,tenant_id,clinic_id),
 FOREIGN KEY(created_by,tenant_id) REFERENCES platform_user(id,tenant_id));
CREATE INDEX crm_activity_due ON dso_crm_activity(tenant_id,clinic_id,status,due_at);
