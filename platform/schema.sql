CREATE TABLE IF NOT EXISTS platform_tenant(id text PRIMARY KEY, name text NOT NULL);
CREATE TABLE IF NOT EXISTS platform_organization(id text PRIMARY KEY, tenant_id text NOT NULL REFERENCES platform_tenant(id), name text NOT NULL);
CREATE TABLE IF NOT EXISTS platform_clinic(id text PRIMARY KEY, organization_id text NOT NULL REFERENCES platform_organization(id), name text NOT NULL);
CREATE TABLE IF NOT EXISTS platform_user(
 id text PRIMARY KEY, tenant_id text NOT NULL REFERENCES platform_tenant(id), clinic_id text NOT NULL REFERENCES platform_clinic(id),
 identity_key text NOT NULL, nickname text NOT NULL, phone text NOT NULL DEFAULT '', created_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(tenant_id,identity_key));
CREATE TABLE IF NOT EXISTS platform_session(token_hash text PRIMARY KEY, user_id text NOT NULL REFERENCES platform_user(id), expires_at timestamptz NOT NULL);
CREATE TABLE IF NOT EXISTS platform_resource(
 id text PRIMARY KEY, tenant_id text NOT NULL REFERENCES platform_tenant(id), clinic_id text NOT NULL REFERENCES platform_clinic(id),
 owner_id text REFERENCES platform_user(id), kind text NOT NULL, body jsonb NOT NULL, created_at timestamptz NOT NULL DEFAULT now());
CREATE INDEX IF NOT EXISTS platform_resource_scope ON platform_resource(tenant_id,clinic_id,kind,owner_id,created_at DESC);
CREATE TABLE IF NOT EXISTS platform_audit(id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
 tenant_id text NOT NULL, user_id text NOT NULL, action text NOT NULL, resource_id text NOT NULL, ip text NOT NULL,
 request_id text NOT NULL, created_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS integration_outbox(id text PRIMARY KEY, tenant_id text NOT NULL, event_type text NOT NULL,
 aggregate_id text NOT NULL, payload jsonb NOT NULL, status text NOT NULL DEFAULT 'pending', attempts integer NOT NULL DEFAULT 0,
 next_attempt timestamptz NOT NULL DEFAULT now(), last_error text, created_at timestamptz NOT NULL DEFAULT now());
CREATE TABLE IF NOT EXISTS integration_mapping(tenant_id text NOT NULL, entity_type text NOT NULL,
 platform_id text NOT NULL, odoo_model text NOT NULL, odoo_id integer NOT NULL,
 PRIMARY KEY(tenant_id,entity_type,platform_id));
CREATE TABLE IF NOT EXISTS platform_idempotency(user_id text NOT NULL, scope text NOT NULL, key text NOT NULL,
 body_hash text NOT NULL, result jsonb NOT NULL, created_at timestamptz NOT NULL DEFAULT now(), PRIMARY KEY(user_id,scope,key));
CREATE TABLE IF NOT EXISTS platform_rate_limit(scope text NOT NULL, bucket bigint NOT NULL, count integer NOT NULL,
 PRIMARY KEY(scope,bucket));
