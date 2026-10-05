ALTER TABLE platform_resource ADD CONSTRAINT resource_owner_scope_unique UNIQUE(id,tenant_id,clinic_id,owner_id);
CREATE UNIQUE INDEX media_object_key ON platform_resource((body->>'objectKey')) WHERE kind='media';
CREATE TABLE dso_ai_job(
 id text PRIMARY KEY, tenant_id text NOT NULL, clinic_id text NOT NULL, user_id text NOT NULL,
 media_id text NOT NULL, status text NOT NULL DEFAULT 'queued', progress integer NOT NULL DEFAULT 0,
 provider text NOT NULL DEFAULT 'mock', attempts integer NOT NULL DEFAULT 0, output jsonb,
 lease_token text, lease_until timestamptz, next_attempt timestamptz NOT NULL DEFAULT now(),
 failure_code text, created_at timestamptz NOT NULL DEFAULT now(), started_at timestamptz, finished_at timestamptz,
 CHECK(status IN ('queued','running','succeeded','failed','cancelled')), CHECK(progress BETWEEN 0 AND 100),
 FOREIGN KEY(id,tenant_id,clinic_id,user_id) REFERENCES platform_resource(id,tenant_id,clinic_id,owner_id),
 FOREIGN KEY(media_id,tenant_id,clinic_id,user_id) REFERENCES platform_resource(id,tenant_id,clinic_id,owner_id));
CREATE INDEX ai_job_claim ON dso_ai_job(status,next_attempt,lease_until);
INSERT INTO dso_ai_job(id,tenant_id,clinic_id,user_id,media_id,status,progress,output,started_at,finished_at)
 SELECT s.id,s.tenant_id,s.clinic_id,s.owner_id,m.id,'succeeded',100,
 '{"isMock":true,"qualityChecks":[],"resultText":"开发演示：未进行AI分析，前后图为同一原图。"}'::jsonb,s.created_at,now()
 FROM platform_resource s JOIN platform_resource m ON m.id=s.body->>'mediaId' AND m.tenant_id=s.tenant_id AND m.clinic_id=s.clinic_id AND m.owner_id=s.owner_id
 WHERE s.kind='simulation' AND m.kind='media' AND m.body->>'uploaded'='true';
