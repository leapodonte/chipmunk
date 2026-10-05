-- 临床与预约由平台持有，Odoo 中只保留商业引用。
CREATE TABLE dso_doctor(
 id text PRIMARY KEY, tenant_id text NOT NULL, clinic_id text NOT NULL, user_id text,
 display_name text NOT NULL, active boolean NOT NULL DEFAULT true,
 UNIQUE(id,tenant_id,clinic_id),
 FOREIGN KEY(clinic_id,tenant_id) REFERENCES platform_clinic(id,tenant_id),
 FOREIGN KEY(user_id,tenant_id) REFERENCES platform_user(id,tenant_id));
INSERT INTO dso_doctor(id,tenant_id,clinic_id,display_name)
 SELECT id,tenant_id,clinic_id,body->>'name' FROM platform_resource WHERE kind='doctor' AND owner_id IS NULL;
CREATE TABLE dso_patient(
 id text PRIMARY KEY, tenant_id text NOT NULL, clinic_id text NOT NULL, user_id text NOT NULL,
 display_name text NOT NULL, profile jsonb NOT NULL DEFAULT '{}', version integer NOT NULL DEFAULT 1,
 created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(id,tenant_id,clinic_id), UNIQUE(user_id,clinic_id),
 FOREIGN KEY(clinic_id,tenant_id) REFERENCES platform_clinic(id,tenant_id),
 FOREIGN KEY(user_id,tenant_id) REFERENCES platform_user(id,tenant_id));
CREATE TABLE dso_care_team(
 tenant_id text NOT NULL, clinic_id text NOT NULL, patient_id text NOT NULL, doctor_id text NOT NULL,
 created_at timestamptz NOT NULL DEFAULT now(), PRIMARY KEY(patient_id,doctor_id),
 FOREIGN KEY(patient_id,tenant_id,clinic_id) REFERENCES dso_patient(id,tenant_id,clinic_id),
 FOREIGN KEY(doctor_id,tenant_id,clinic_id) REFERENCES dso_doctor(id,tenant_id,clinic_id));
CREATE TABLE dso_slot(
 id text PRIMARY KEY, tenant_id text NOT NULL, clinic_id text NOT NULL, doctor_id text NOT NULL,
 starts_at timestamptz NOT NULL, ends_at timestamptz NOT NULL, status text NOT NULL DEFAULT 'open',
 created_by text NOT NULL, created_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(id,tenant_id,clinic_id), CHECK(ends_at>starts_at), CHECK(ends_at<=starts_at+interval '4 hours'),
 CHECK(status IN ('open','closed')),
 FOREIGN KEY(doctor_id,tenant_id,clinic_id) REFERENCES dso_doctor(id,tenant_id,clinic_id),
 FOREIGN KEY(created_by,tenant_id) REFERENCES platform_user(id,tenant_id));
CREATE INDEX slot_calendar ON dso_slot(tenant_id,clinic_id,doctor_id,starts_at);
CREATE TABLE dso_appointment(
 id text PRIMARY KEY, tenant_id text NOT NULL, clinic_id text NOT NULL, patient_id text NOT NULL,
 slot_id text NOT NULL, status text NOT NULL DEFAULT 'booked', version integer NOT NULL DEFAULT 1,
 created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
 CHECK(status IN ('booked','arrived','completed','cancelled','no_show')),
 FOREIGN KEY(patient_id,tenant_id,clinic_id) REFERENCES dso_patient(id,tenant_id,clinic_id),
 FOREIGN KEY(slot_id,tenant_id,clinic_id) REFERENCES dso_slot(id,tenant_id,clinic_id));
CREATE UNIQUE INDEX appointment_active_slot ON dso_appointment(slot_id) WHERE status IN ('booked','arrived','completed','no_show');
CREATE INDEX appointment_patient ON dso_appointment(tenant_id,clinic_id,patient_id,created_at DESC);
CREATE TABLE dso_clinical_record(
 id text PRIMARY KEY, tenant_id text NOT NULL, clinic_id text NOT NULL, patient_id text NOT NULL,
 author_id text NOT NULL, status text NOT NULL DEFAULT 'draft', version integer NOT NULL DEFAULT 1,
 content jsonb NOT NULL, previous_id text REFERENCES dso_clinical_record(id),
 created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(), signed_at timestamptz,
 UNIQUE(id,tenant_id,clinic_id,patient_id), CHECK(status IN ('draft','signed')),
 CHECK((status='signed')=(signed_at IS NOT NULL)),
 FOREIGN KEY(patient_id,tenant_id,clinic_id) REFERENCES dso_patient(id,tenant_id,clinic_id),
 FOREIGN KEY(author_id,tenant_id) REFERENCES platform_user(id,tenant_id));
ALTER TABLE dso_clinical_record ADD CONSTRAINT clinical_revision_same_patient FOREIGN KEY(previous_id,tenant_id,clinic_id,patient_id) REFERENCES dso_clinical_record(id,tenant_id,clinic_id,patient_id);
CREATE INDEX clinical_patient ON dso_clinical_record(tenant_id,clinic_id,patient_id,created_at DESC);
CREATE FUNCTION protect_signed_clinical_record() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF OLD.status='signed' THEN RAISE EXCEPTION 'Signed clinical records are immutable' USING ERRCODE='23514'; END IF;
 IF TG_OP='DELETE' THEN RETURN OLD; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER clinical_immutable BEFORE UPDATE OR DELETE ON dso_clinical_record FOR EACH ROW EXECUTE FUNCTION protect_signed_clinical_record();
