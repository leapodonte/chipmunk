-- 平台是唯一订单真相源；当前状态与只追加事件在同一事务提交。
ALTER TABLE platform_role_assignment DROP CONSTRAINT platform_role_assignment_role_check;
ALTER TABLE platform_role_assignment ADD CONSTRAINT platform_role_assignment_role_check CHECK(role IN ('patient','doctor','assistant','consultant','clinic_manager','regional_manager','supplier','platform_operator','platform_admin','sales','manufacturer','quality'));
ALTER TABLE platform_role_assignment ADD CONSTRAINT order_staff_clinic_scope CHECK(role NOT IN ('sales','manufacturer','quality') OR clinic_id IS NOT NULL);
CREATE TABLE dso_order_product(
 code text PRIMARY KEY, name text NOT NULL, price_minor integer NOT NULL CHECK(price_minor>0),
 currency text NOT NULL CHECK(currency='CNY'), active boolean NOT NULL DEFAULT true);
INSERT INTO dso_order_product VALUES('retainer_upper','上颌保持器（演示）',20000,'CNY',true),('retainer_lower','下颌保持器（演示）',20000,'CNY',true),('retainer_pair','上下颌保持器（演示）',36000,'CNY',true);
CREATE TABLE dso_order(
 id text PRIMARY KEY, tenant_id text NOT NULL, clinic_id text NOT NULL,
 patient_id text NOT NULL, doctor_id text NOT NULL, product_code text NOT NULL REFERENCES dso_order_product(code),
 product_name text NOT NULL, quantity integer NOT NULL CHECK(quantity BETWEEN 1 AND 4),
 amount_minor integer NOT NULL CHECK(amount_minor>0), currency text NOT NULL CHECK(currency='CNY'),
 request_text text NOT NULL, shipping_address jsonb NOT NULL, production_spec text NOT NULL DEFAULT '',
 status text NOT NULL DEFAULT 'requested' CHECK(status IN ('requested','pending_payment','paid','sales_validated','manufacturing_ready','manufacturing','qa_pending','rework_required','qa_passed','shipped','delivered','rejected','cancelled')),
 version integer NOT NULL DEFAULT 1 CHECK(version>0), batch_ref text NOT NULL DEFAULT '',
 carrier text NOT NULL DEFAULT '', tracking_number text NOT NULL DEFAULT '',
 created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(id,tenant_id,clinic_id),
 FOREIGN KEY(patient_id,tenant_id,clinic_id) REFERENCES dso_patient(id,tenant_id,clinic_id),
 FOREIGN KEY(doctor_id,tenant_id,clinic_id) REFERENCES dso_doctor(id,tenant_id,clinic_id));
CREATE INDEX order_queue ON dso_order(tenant_id,clinic_id,status,created_at DESC);
CREATE TABLE dso_order_event(
 order_id text NOT NULL, tenant_id text NOT NULL, clinic_id text NOT NULL, version integer NOT NULL,
 action text NOT NULL, from_status text, to_status text NOT NULL,
 actor_id text NOT NULL, actor_role text NOT NULL, details jsonb NOT NULL DEFAULT '{}',
 CHECK((action='request' AND actor_role='patient' AND to_status='requested' AND version=1) OR
 (action='doctor_approve' AND actor_role='doctor' AND to_status='pending_payment') OR
 (action='doctor_reject' AND actor_role='doctor' AND to_status='rejected') OR
 (action='cancel' AND actor_role='patient' AND to_status='cancelled') OR
 (action='pay_demo' AND actor_role='patient' AND to_status='paid') OR
 (action='sales_validate' AND actor_role='sales' AND to_status='sales_validated') OR
 (action='manufacturer_validate' AND actor_role='manufacturer' AND to_status='manufacturing_ready') OR
 (action='start_manufacturing' AND actor_role='manufacturer' AND to_status='manufacturing') OR
 (action='finish_manufacturing' AND actor_role='manufacturer' AND to_status='qa_pending') OR
 (action IN ('qa_pass','qa_fail') AND actor_role='quality' AND to_status=CASE WHEN action='qa_pass' THEN 'qa_passed' ELSE 'rework_required' END) OR
 (action='ship' AND actor_role='manufacturer' AND to_status='shipped') OR
 (action='confirm_delivery' AND actor_role='patient' AND to_status='delivered')),
 created_at timestamptz NOT NULL DEFAULT now(), PRIMARY KEY(order_id,version),
 FOREIGN KEY(order_id,tenant_id,clinic_id) REFERENCES dso_order(id,tenant_id,clinic_id),
 FOREIGN KEY(actor_id,tenant_id) REFERENCES platform_user(id,tenant_id));
CREATE TABLE dso_order_payment(
 id text PRIMARY KEY, order_id text NOT NULL UNIQUE, tenant_id text NOT NULL, clinic_id text NOT NULL,
 amount_minor integer NOT NULL, currency text NOT NULL, provider text NOT NULL CHECK(provider='demo'),
 paid_at timestamptz NOT NULL DEFAULT now(),
 FOREIGN KEY(order_id,tenant_id,clinic_id) REFERENCES dso_order(id,tenant_id,clinic_id));
CREATE FUNCTION protect_order_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Order events and payment receipts are append-only' USING ERRCODE='23514'; END $$;
CREATE TRIGGER order_event_immutable BEFORE UPDATE OR DELETE ON dso_order_event FOR EACH ROW EXECUTE FUNCTION protect_order_append_only();
CREATE TRIGGER order_payment_immutable BEFORE UPDATE OR DELETE ON dso_order_payment FOR EACH ROW EXECUTE FUNCTION protect_order_append_only();
CREATE FUNCTION protect_order_transition() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Orders cannot be deleted' USING ERRCODE='23514'; END IF;
 IF ROW(NEW.id,NEW.tenant_id,NEW.clinic_id,NEW.patient_id,NEW.doctor_id,NEW.product_code,NEW.product_name,NEW.quantity,NEW.amount_minor,NEW.currency,NEW.request_text,NEW.shipping_address,NEW.created_at) IS DISTINCT FROM ROW(OLD.id,OLD.tenant_id,OLD.clinic_id,OLD.patient_id,OLD.doctor_id,OLD.product_code,OLD.product_name,OLD.quantity,OLD.amount_minor,OLD.currency,OLD.request_text,OLD.shipping_address,OLD.created_at) OR NEW.version<>OLD.version+1
 THEN RAISE EXCEPTION 'Order identity, quote and version are immutable' USING ERRCODE='23514'; END IF;
 IF NOT ((OLD.status='requested' AND NEW.status IN ('pending_payment','rejected','cancelled')) OR
 (OLD.status='pending_payment' AND NEW.status IN ('paid','cancelled')) OR
 (OLD.status='paid' AND NEW.status='sales_validated') OR
 (OLD.status='sales_validated' AND NEW.status='manufacturing_ready') OR
 (OLD.status IN ('manufacturing_ready','rework_required') AND NEW.status='manufacturing') OR
 (OLD.status='manufacturing' AND NEW.status='qa_pending') OR
 (OLD.status='qa_pending' AND NEW.status IN ('qa_passed','rework_required')) OR
 (OLD.status='qa_passed' AND NEW.status='shipped') OR (OLD.status='shipped' AND NEW.status='delivered'))
 THEN RAISE EXCEPTION 'Illegal order transition' USING ERRCODE='23514'; END IF;
 IF OLD.status<>'requested' AND NEW.production_spec<>OLD.production_spec THEN RAISE EXCEPTION 'Approved specification is frozen' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER order_transition_guard BEFORE UPDATE OR DELETE ON dso_order FOR EACH ROW EXECUTE FUNCTION protect_order_transition();
CREATE FUNCTION verify_order_ledger() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE o dso_order; e dso_order_event; n integer; p dso_order_payment;
BEGIN
 SELECT * INTO o FROM dso_order WHERE id=CASE WHEN TG_TABLE_NAME='dso_order' THEN (to_jsonb(NEW)->>'id') ELSE (to_jsonb(NEW)->>'order_id') END;
 SELECT * INTO e FROM dso_order_event WHERE order_id=o.id ORDER BY version DESC LIMIT 1;
 SELECT count(*) INTO n FROM dso_order_event WHERE order_id=o.id;
 IF e.order_id IS NULL OR e.version<>o.version OR e.to_status<>o.status OR n<>o.version THEN RAISE EXCEPTION 'Order snapshot must match complete event ledger' USING ERRCODE='23514'; END IF;
 IF EXISTS(SELECT 1 FROM (SELECT version,from_status,lag(to_status) OVER(ORDER BY version) AS previous_status FROM dso_order_event WHERE order_id=o.id) history WHERE (version=1 AND from_status IS NOT NULL) OR (version>1 AND from_status IS DISTINCT FROM previous_status)) THEN RAISE EXCEPTION 'Order history must be continuous' USING ERRCODE='23514'; END IF;
 IF o.status IN ('paid','sales_validated','manufacturing_ready','manufacturing','qa_pending','rework_required','qa_passed','shipped','delivered') THEN
  SELECT * INTO p FROM dso_order_payment WHERE order_id=o.id;
  IF p.id IS NULL OR p.amount_minor<>o.amount_minor OR p.currency<>o.currency THEN RAISE EXCEPTION 'Paid order requires exact payment receipt' USING ERRCODE='23514'; END IF;
 ELSIF EXISTS(SELECT 1 FROM dso_order_payment WHERE order_id=o.id) THEN RAISE EXCEPTION 'Unpaid order cannot have a settled receipt' USING ERRCODE='23514';
 END IF;
 RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER order_ledger_consistent AFTER INSERT OR UPDATE ON dso_order DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION verify_order_ledger();
CREATE CONSTRAINT TRIGGER order_event_ledger_consistent AFTER INSERT ON dso_order_event DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION verify_order_ledger();
CREATE CONSTRAINT TRIGGER order_payment_consistent AFTER INSERT ON dso_order_payment DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION verify_order_ledger();
