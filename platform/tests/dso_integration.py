#!/usr/bin/env python3
"""在独立临时 Docker 网络/数据库中验证 DSO，不写入线上患者数据。"""
import argparse
import base64
import concurrent.futures
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import secrets
import subprocess
import struct
import sys
import tempfile
import threading
import time
import unittest
import urllib.error
import urllib.request
import urllib.parse
import zlib

POSTGRES = "postgres:16-alpine@sha256:721873c34ceb9f8d8fc265984940dc982404c105f19ad51be9fdc5970a6080ea"
PYTHON = "python:3.14-alpine@sha256:f6a589d43c42b9e7f7dc67a12d37132491f362859a5d750607710cc56da3bc72"
RUN = "dso-test-" + secrets.token_hex(5)
DB = RUN + "-db"
API = RUN + "-api"
DEV = secrets.token_hex(32)
STAFF = secrets.token_hex(32)
STAFF_KEYS = {}
BASE = ""
ENV_FILE = ""
IMAGE = ""
PNG = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=")


def command(*args, input=None, check=True):
    result = subprocess.run(args, input=input, text=True, capture_output=True)
    if check and result.returncode:
        raise RuntimeError(f"Command failed: {args[0]} {args[1:3]}: {result.stderr[-3000:]}")
    return result


def sql(statement, check=True):
    return command("docker", "exec", "-i", DB, "psql", "-U", "test", "-d", "dso_test", "-v", "ON_ERROR_STOP=1", "-At", input=statement, check=check)


def request(path, token=None, method="GET", body=None, headers=None):
    fields = {"Content-Type": "application/json", "Accept-Language": "en"}
    fields.update(headers or {})
    if token:
        fields["Authorization"] = "Bearer " + token
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(BASE + path, data, fields, method=method)
    try:
        with urllib.request.urlopen(req, timeout=15) as response:
            return response.status, json.load(response)
    except urllib.error.HTTPError as error:
        return error.code, json.load(error)


def call(path, token=None, method="GET", body=None, headers=None, expected=200):
    status, result = request(path, token, method, body, headers)
    if status != expected:
        raise AssertionError(f"{method} {path}: expected {expected}, got {status}; {result.get('message')}")
    return result.get("data")


def upload(grant, data, expected=200):
    url = urllib.parse.urlsplit(grant["uploadUrl"])
    req = urllib.request.Request(BASE + url.path + "?" + url.query, data=data, headers={"Content-Type": "image/png"}, method="PUT")
    try:
        response = urllib.request.urlopen(req, timeout=15)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        result = json.load(response)
        if response.status != expected:
            raise AssertionError(f"Upload expected {expected}, got {response.status}: {result.get('message')}")
        return result.get("data")


def wide_png(width, height=1):
    def chunk(kind, payload):
        return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload))
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress((b"\0" + b"\0" * width * 4) * height)) + chunk(b"IEND", b"")


class DsoTests(unittest.TestCase):
    counter = 0

    @classmethod
    def setUpClass(cls):
        provision = Path(__file__).resolve().parents[2] / "tools" / "provision-dso-staff.py"
        cls.credential_file = Path(ENV_FILE).parent / "staff-credentials.json"
        cls.provision_command = [sys.executable, str(provision), "--container", DB, "--database", "dso_test", "--user", "test", "--output", str(cls.credential_file)]
        command(*cls.provision_command)
        credentials = json.loads(cls.credential_file.read_text())
        STAFF_KEYS.update({name: value["key"] for name, value in credentials["accounts"].items()})
        cls.staff = {}
        for identity in ["doctor", "doctor2", "manager", "consultant", "operator"]:
            cls.staff[identity] = call("/api/v1/auth/staff-login", method="POST", body={"identity": "staff:" + identity}, headers={"X-Staff-Dev-Key": STAFF_KEYS[identity]})["token"]
        cls.alice = call("/api/v1/auth/mp-login", method="POST", body={"code": "demo:dso-alice"}, headers={"X-Dev-Key": DEV})["token"]
        cls.bob = call("/api/v1/auth/mp-login", method="POST", body={"code": "demo:dso-bob"}, headers={"X-Dev-Key": DEV})["token"]
        cls.patient = call("/api/dso/v1/patients/me", cls.alice)["id"]
        cls.bob_patient = call("/api/dso/v1/patients/me", cls.bob)["id"]
        cls.foreign = secrets.token_hex(32)
        sql("INSERT INTO platform_tenant VALUES('tenant_foreign','Synthetic foreign'); INSERT INTO platform_organization VALUES('org_foreign','tenant_foreign','Synthetic foreign'); INSERT INTO platform_clinic(id,organization_id,name,tenant_id) VALUES('clinic_foreign','org_foreign','Synthetic foreign','tenant_foreign'); INSERT INTO platform_user(id,tenant_id,clinic_id,identity_key,nickname) VALUES('foreign_user','tenant_foreign','clinic_foreign','test:foreign','Synthetic foreign'); INSERT INTO platform_role_assignment(id,user_id,tenant_id,clinic_id,role) VALUES('foreign_role','foreign_user','tenant_foreign','clinic_foreign','patient'); INSERT INTO platform_session(token_hash,user_id,clinic_id,expires_at) VALUES('" + hashlib.sha256(cls.foreign.encode()).hexdigest() + "','foreign_user','clinic_foreign',now()+interval '1 hour');")

    def slot(self, doctor="d_001", minutes=30):
        type(self).counter += 1
        start = dt.datetime.now(dt.timezone.utc).replace(second=0, microsecond=0) + dt.timedelta(days=10, hours=type(self).counter)
        body = {"doctorId": doctor, "startsAt": start.isoformat(), "endsAt": (start + dt.timedelta(minutes=minutes)).isoformat()}
        return call("/api/dso/v1/slots", self.staff["manager"], "POST", body, {"Idempotency-Key": secrets.token_hex(8)})

    def grant(self, token=None, doctor="d_001"):
        call("/api/dso/v1/patients/me/care-team/" + doctor, token or self.alice, "POST", {})

    def record(self, content=None):
        self.grant()
        return call(f"/api/dso/v1/patients/{self.patient}/records", self.staff["doctor"], "POST", {"content": content or {"chiefComplaint": "Synthetic test complaint", "toothChart": [{"tooth": 11, "finding": "synthetic"}]}}, {"Idempotency-Key": secrets.token_hex(8)})

    def test_00_staff_provisioning_preserves_keys_and_rejects_conflicts(self):
        before = self.credential_file.read_bytes()
        command(*self.provision_command)
        self.assertEqual(self.credential_file.read_bytes(), before)
        changed = json.loads(before)
        changed["accounts"]["doctor"]["key"] = secrets.token_hex(32)
        conflict = self.credential_file.with_name("staff-conflict.json")
        conflict.write_text(json.dumps(changed))
        os.chmod(conflict, 0o600)
        conflicting_command = self.provision_command[:-1] + [str(conflict)]
        self.assertNotEqual(command(*conflicting_command, check=False).returncode, 0)
        self.assertEqual(sql("SELECT count(*) FROM platform_staff_credential").stdout.strip(), "5")
        self.assertEqual(sql("SELECT token_hash FROM platform_staff_credential WHERE user_id='staff_demo_doctor'").stdout.strip(), hashlib.sha256(STAFF_KEYS["doctor"].encode()).hexdigest())

    def test_01_existing_schema_upgrade_and_restart_ledger(self):
        global BASE
        self.assertEqual(sql("SELECT count(*) FROM platform_migration").stdout.strip(), "7")
        self.assertEqual(sql("SELECT role FROM platform_role_assignment WHERE user_id='legacy_user'").stdout.strip(), "patient")
        self.assertEqual(sql("SELECT clinic_id FROM platform_session WHERE user_id='legacy_user'").stdout.strip(), "clinic_demo")
        violations = sql("INSERT INTO platform_resource(id,tenant_id,clinic_id,owner_id,kind,body) VALUES('bad','tenant_foreign','clinic_demo',NULL,'test','{}')", check=False)
        self.assertNotEqual(violations.returncode, 0)
        self.assertIn("foreign key", violations.stderr)
        sql("INSERT INTO platform_session(token_hash,user_id,clinic_id,expires_at) VALUES('maintenance-expired','legacy_user','clinic_demo',now()-interval '9 days'); INSERT INTO platform_rate_limit(scope,bucket,count,expires_at) VALUES('maintenance-expired',0,1,now()-interval '1 day')")
        legacy_start = dt.datetime.now(dt.timezone.utc) + dt.timedelta(days=50)
        legacy_body = {"doctorId":"d_001","startsAt":legacy_start.isoformat(),"endsAt":(legacy_start+dt.timedelta(minutes=15)).isoformat()}
        legacy_slot = call('/api/dso/v1/slots', self.staff['manager'], 'POST', legacy_body, {'Idempotency-Key':'legacy-clinic-replay'})
        sql("UPDATE platform_idempotency SET scope='slots.create' WHERE user_id='staff_demo_manager' AND key='legacy-clinic-replay'; DELETE FROM platform_migration WHERE version='007_clinic_idempotency.sql'")
        command("docker", "restart", API)
        BASE = "http://127.0.0.1:" + command("docker", "port", API, "8080/tcp").stdout.strip().rsplit(":", 1)[1]
        for _ in range(60):
            try:
                if request("/api/dso/v1/context", self.alice)[0] == 200:
                    break
            except OSError:
                time.sleep(0.25)
        else:
            self.fail("API did not recover after restart")
        self.assertEqual(sql("SELECT count(*) FROM platform_migration").stdout.strip(), "7")
        self.assertEqual(call('/api/dso/v1/slots', self.staff['manager'], 'POST', legacy_body, {'Idempotency-Key':'legacy-clinic-replay'})['id'], legacy_slot['id'])
        for _ in range(20):
            if sql("SELECT (SELECT count(*) FROM platform_session WHERE token_hash='maintenance-expired')+(SELECT count(*) FROM platform_rate_limit WHERE scope='maintenance-expired')").stdout.strip() == "0":
                break
            time.sleep(0.1)
        else:
            self.fail('Expiry cleanup must work while Odoo synchronization is disabled')

    def test_02_identity_roles_cannot_be_injected(self):
        context = call("/api/dso/v1/context", self.alice, headers={"X-Tenant-Id": "tenant_foreign", "X-Role": "platform_admin"})
        self.assertEqual(context["tenantId"], "tenant_demo")
        self.assertEqual(context["roles"], ["patient"])
        call("/api/v1/auth/staff-login", method="POST", body={"identity": "staff:manager"}, headers={"X-Staff-Dev-Key": DEV}, expected=401)
        call("/api/v1/auth/staff-login", method="POST", body={"identity": "staff:operator"}, headers={"X-Staff-Dev-Key": STAFF_KEYS['doctor']}, expected=401)
        call("/api/dso/v1/context/switch", self.alice, "POST", {"clinicId": "clinic_foreign"}, expected=404)
        call("/api/dso/v1/patients", self.alice, expected=403)
        self.assertTrue(call("/api/dso/v1/patients", self.staff["consultant"]))
        call(f"/api/dso/v1/patients/{self.patient}/records", self.staff["consultant"], expected=404)

    def test_09_crm_pipeline_activities_and_clinical_boundary(self):
        headers = {"Idempotency-Key": "commercial-lead"}
        body = {"title": "Synthetic commercial opportunity", "patientId": self.patient}
        lead = call("/api/dso/v1/crm/leads", self.staff["consultant"], "POST", body, headers)
        self.assertEqual(call("/api/dso/v1/crm/leads", self.staff["consultant"], "POST", body, headers), lead)
        call("/api/dso/v1/crm/leads", self.alice, expected=403)
        call("/api/dso/v1/crm/leads/" + lead["id"], self.foreign, expected=403)
        payload = json.loads(sql("SELECT payload::text FROM integration_outbox WHERE aggregate_id='" + lead["id"] + "'").stdout.strip())
        self.assertEqual(set(payload), {"name", "platformRef"})
        self.assertNotIn("Synthetic commercial opportunity", payload["name"])
        qualified = call("/api/dso/v1/crm/leads/" + lead["id"], self.staff["consultant"], "PATCH", {"version": 1, "stage": "qualified", "assignedTo": "staff_demo_consultant"})
        self.assertEqual(qualified["version"], 2)
        call("/api/dso/v1/crm/leads/" + lead["id"], self.staff["consultant"], "PATCH", {"version": 1, "stage": "won"}, expected=409)
        activity = call("/api/dso/v1/crm/leads/" + lead["id"] + "/activities", self.staff["consultant"], "POST", {"type": "follow_up", "summary": "Synthetic follow-up", "dueAt": (dt.datetime.now(dt.timezone.utc) + dt.timedelta(days=1)).isoformat()}, {"Idempotency-Key": "activity"})
        finished = call("/api/dso/v1/crm/activities/" + activity["id"], self.staff["consultant"], "PATCH", {"version": 1, "status": "done"})
        self.assertEqual(finished["status"], "done")
        call("/api/dso/v1/crm/activities/" + activity["id"], self.staff["consultant"], "PATCH", {"version": 1, "status": "done"}, expected=409)

    def test_10_operator_queue_access_and_dead_letter_replay(self):
        call("/api/dso/v1/operations/summary", self.staff["doctor"], expected=403)
        call("/api/dso/v1/operations/outbox", self.alice, expected=403)
        sql("INSERT INTO integration_outbox(id,tenant_id,event_type,aggregate_id,payload,status,attempts) VALUES('evt_dead_test','tenant_demo','crm.lead.created','dead_test','{}','dead_letter',10)")
        queue = call("/api/dso/v1/operations/outbox?status=dead_letter", self.staff["operator"])
        self.assertTrue(any(x["id"] == "evt_dead_test" for x in queue))
        retried = call("/api/dso/v1/operations/outbox/evt_dead_test/retry", self.staff["operator"], "POST", {"reason": "Synthetic recovery verification"})
        self.assertEqual(retried["status"], "pending")
        call("/api/dso/v1/operations/outbox/evt_dead_test/retry", self.staff["operator"], "POST", {"reason": "Repeat"}, expected=409)
        self.assertTrue(call("/api/dso/v1/operations/audit", self.staff["operator"]))

    def test_11_media_decode_pixel_limits_and_database_rollback_cleanup(self):
        grant = call("/api/v1/media/upload-token", self.alice, "POST", {"scene": "ai_photo", "ext": "png"})
        uploaded = upload(grant, PNG)
        metadata = json.loads(sql("SELECT body::text FROM platform_resource WHERE id='" + uploaded["mediaId"] + "'").stdout.strip())
        self.assertEqual((metadata["width"], metadata["height"]), (1, 1))
        self.assertEqual(metadata["sha256"], hashlib.sha256(PNG).hexdigest())
        self.assertTrue(metadata["uploaderId"])
        corrupt = call("/api/v1/media/upload-token", self.alice, "POST", {"scene": "ai_photo", "ext": "png"})
        upload(corrupt, PNG[:24], expected=400)
        huge = call("/api/v1/media/upload-token", self.alice, "POST", {"scene": "ai_photo", "ext": "png"})
        upload(huge, wide_png(5000), expected=400)
        maximum = call('/api/v1/media/upload-token', self.alice, 'POST', {'scene':'ai_photo','ext':'png'})
        large = wide_png(4000,3000)
        self.assertEqual(upload(maximum, large)['size'], len(large))
        maximum_metadata = json.loads(sql("SELECT body::text FROM platform_resource WHERE id='" + maximum['objectKey'].rsplit('/',1)[1].split('.')[0] + "'").stdout.strip())
        self.assertEqual((maximum_metadata['width'],maximum_metadata['height']),(4000,3000))
        over_pixels = call('/api/v1/media/upload-token', self.alice, 'POST', {'scene':'ai_photo','ext':'png'})
        upload(over_pixels, wide_png(4000,3001), expected=400)
        broken = call("/api/v1/media/upload-token", self.alice, "POST", {"scene": "ai_photo", "ext": "png"})
        identifier = broken["objectKey"].rsplit("/", 1)[1].split(".")[0]
        sql("CREATE FUNCTION force_test_media_failure() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.id='" + identifier + "' THEN RAISE EXCEPTION 'Synthetic media persistence failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER test_media_failure BEFORE UPDATE ON platform_resource FOR EACH ROW EXECUTE FUNCTION force_test_media_failure();")
        try:
            upload(broken, PNG, expected=500)
            self.assertEqual(command("docker", "exec", API, "test", "-e", "/tmp/dso-media/" + identifier + ".png", check=False).returncode, 1)
        finally:
            sql("DROP TRIGGER test_media_failure ON platform_resource; DROP FUNCTION force_test_media_failure();")
        restored = upload(broken, PNG)
        self.assertEqual(restored["mediaId"], identifier)

    def test_12_persistent_ai_job_completion_privacy_and_cancellation(self):
        grant = call("/api/v1/media/upload-token", self.alice, "POST", {"scene": "ai_photo", "ext": "png"})
        upload(grant, PNG)
        task = call("/api/v1/ai/simulations", self.alice, "POST", {"imageKey": grant["objectKey"]})
        call("/api/v1/ai/simulations/" + task["taskId"], self.bob, expected=404)
        deadline = time.monotonic() + 15
        while time.monotonic() < deadline:
            result = call("/api/v1/ai/simulations/" + task["taskId"], self.alice)
            if result["jobStatus"] == "succeeded":
                break
            time.sleep(0.2)
        else:
            self.fail("Persistent mock AI job did not complete")
        self.assertEqual(result["status"], "done")
        self.assertTrue(result["isMock"])
        self.assertEqual(result["beforeImage"].split("?")[0], result["afterImage"].split("?")[0])
        self.assertEqual(sql("SELECT status FROM dso_ai_job WHERE id='" + task["taskId"] + "'").stdout.strip(), "succeeded")
        task2 = call("/api/v1/ai/simulations", self.alice, "POST", {"imageKey": grant["objectKey"]})
        cancelled = call("/api/v1/ai/simulations/" + task2["taskId"] + "/cancel", self.alice, "POST", {})
        self.assertEqual(cancelled["jobStatus"], "cancelled")
        time.sleep(2.5)
        self.assertEqual(call("/api/v1/ai/simulations/" + task2["taskId"], self.alice)["jobStatus"], "cancelled")

    def test_03_slot_permissions_overlap_and_idempotency(self):
        slot = self.slot()
        body = {"doctorId": "d_001", "startsAt": slot["startsAt"], "endsAt": slot["endsAt"]}
        call("/api/dso/v1/slots", self.alice, "POST", body, {"Idempotency-Key": "patient-denied"}, expected=403)
        call("/api/dso/v1/slots", self.staff["doctor2"], "POST", body, {"Idempotency-Key": "wrong-doctor"}, expected=404)
        call("/api/dso/v1/slots", self.staff["manager"], "POST", body, {"Idempotency-Key": "overlap"}, expected=409)
        body["startsAt"] = "2027-01-01T08:00:00"
        call("/api/dso/v1/slots", self.staff["manager"], "POST", body, {"Idempotency-Key": "no-zone"}, expected=400)

    def test_04_concurrent_booking_exactly_one_winner(self):
        slot = self.slot()
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            futures = [pool.submit(request, "/api/dso/v1/appointments", token, "POST", {"slotId": slot["id"]}, {"Idempotency-Key": "race-" + str(i)}) for i, token in enumerate([self.alice, self.bob])]
            statuses = sorted(f.result()[0] for f in futures)
        self.assertEqual(statuses, [200, 409])
        count = sql("SELECT count(*) FROM dso_appointment WHERE slot_id='" + slot["id"] + "' AND status!='cancelled'").stdout.strip()
        self.assertEqual(count, "1")

    def test_05_appointment_replay_privacy_and_transitions(self):
        slot = self.slot()
        body = {"slotId": slot["id"], "shareWithDoctor": True}
        headers = {"Idempotency-Key": "booking-replay"}
        booking = call("/api/v1/appointments", self.alice, "POST", body, headers)
        replay = call("/api/v1/appointments", self.alice, "POST", dict(reversed(list(body.items()))), headers)
        self.assertEqual(booking, replay)
        call("/api/v1/appointments", self.alice, "POST", {"slotId": self.slot()["id"]}, headers, expected=409)
        call("/api/dso/v1/appointments/" + booking["id"], self.bob, expected=404)
        call("/api/dso/v1/appointments/" + booking["id"], self.foreign, expected=404)
        call("/api/dso/v1/appointments/" + booking["id"], self.staff["doctor2"], expected=404)
        call("/api/dso/v1/slots/" + slot["id"] + "/close", self.staff["manager"], "POST", {}, expected=409)
        call("/api/v1/appointments/" + booking["id"] + "/status", self.alice, "POST", {"status": "completed", "version": 1}, expected=409)
        cancelled = call("/api/v1/appointments/" + booking["id"] + "/status", self.alice, "POST", {"status": "cancelled", "version": 1})
        self.assertEqual(cancelled["version"], 2)
        call("/api/v1/appointments/" + booking["id"] + "/status", self.alice, "POST", {"status": "cancelled", "version": 1}, expected=409)
        self.assertTrue(any(x["id"] == booking["id"] for x in call("/api/v1/appointments/mine", self.alice)))
        closed = call("/api/dso/v1/slots/" + slot["id"] + "/close", self.staff["manager"], "POST", {})
        self.assertEqual(closed["status"], "closed")

    def test_06_clinical_care_team_and_draft_visibility(self):
        call(f"/api/dso/v1/patients/{self.bob_patient}/records", self.staff["doctor"], expected=404)
        record = self.record()
        self.assertFalse(any(x["id"] == record["id"] for x in call("/api/v1/patients/me/records", self.alice)))
        call("/api/dso/v1/clinical-records/" + record["id"], self.alice, expected=404)
        call("/api/dso/v1/clinical-records/" + record["id"], self.bob, expected=404)
        call("/api/dso/v1/clinical-records/" + record["id"], self.foreign, expected=404)
        call("/api/dso/v1/clinical-records/" + record["id"], self.staff["manager"], expected=404)
        self.grant(doctor="d_002")
        call("/api/dso/v1/clinical-records/" + record["id"] + "/sign", self.staff["doctor2"], "POST", {"version": 1}, expected=403)

    def test_07_sign_immutability_amendments_and_revocation(self):
        record = self.record()
        path = "/api/dso/v1/clinical-records/" + record["id"]
        call(path, self.staff["doctor"], "PATCH", {"version": 999, "content": {"plan": "Synthetic"}}, expected=409)
        updated = call(path, self.staff["doctor"], "PATCH", {"version": 1, "content": {"plan": "Synthetic revised plan"}})
        signed = call(path + "/sign", self.staff["doctor"], "POST", {"version": updated["version"]})
        self.assertEqual(signed["status"], "signed")
        self.assertEqual(call(path, self.alice)["id"], record["id"])
        call(path, self.staff["doctor"], "PATCH", {"version": signed["version"], "content": {"plan": "overwrite"}}, expected=409)
        attempt = sql("UPDATE dso_clinical_record SET content='{}' WHERE id='" + record["id"] + "'", check=False)
        self.assertNotEqual(attempt.returncode, 0)
        self.assertIn("immutable", attempt.stderr)
        amended = call(f"/api/dso/v1/patients/{self.patient}/records", self.staff["doctor"], "POST", {"previousId": record["id"], "content": {"plan": "Synthetic amendment"}}, {"Idempotency-Key": "amendment"})
        self.assertEqual(amended["previousId"], record["id"])
        call("/api/v1/patients/me/care-team/d_001", self.alice, "DELETE")
        call(path, self.staff["doctor"], expected=404)
        self.grant()
        self.assertEqual(call(path, self.staff["doctor"])["id"], record["id"])

    def test_08_profile_versions_and_notifications(self):
        profile = call("/api/v1/patients/me", self.alice)
        body = {"displayName": "Synthetic Alice", "profile": {"allergies": "Synthetic only"}, "version": profile["version"]}
        updated = call("/api/v1/patients/me", self.alice, "PUT", body)
        self.assertEqual(updated["version"], profile["version"] + 1)
        call("/api/v1/patients/me", self.alice, "PATCH", body, expected=409)
        manager_view = call("/api/dso/v1/patients/" + self.patient, self.staff["manager"])
        self.assertNotIn("profile", manager_view)
        self.assertTrue(any(x.get("type") == "clinical" for x in call("/api/v1/messages", self.alice)))
        self.assertGreater(int(sql("SELECT count(*) FROM platform_audit WHERE action='clinical.record_signed'").stdout.strip()), 0)

    def test_13_calendar_date_range_and_scheduling_capabilities(self):
        slot = self.slot()
        booking = call('/api/v1/appointments', self.alice, 'POST', {'slotId': slot['id']}, {'Idempotency-Key': 'calendar-booking'})
        start = dt.datetime.fromisoformat(slot['startsAt'].replace('Z', '+00:00'))
        end = dt.datetime.fromisoformat(slot['endsAt'].replace('Z', '+00:00'))
        query = urllib.parse.urlencode({'from': start.isoformat(), 'to': end.isoformat()})
        rows = call('/api/dso/v1/appointments?' + query, self.staff['doctor'])
        self.assertEqual([row['id'] for row in rows], [booking['id']])
        empty = urllib.parse.urlencode({'from': (end + dt.timedelta(hours=1)).isoformat(), 'to': (end + dt.timedelta(hours=2)).isoformat()})
        self.assertEqual(call('/api/dso/v1/appointments?' + empty, self.staff['doctor']), [])
        self.assertEqual([row['id'] for row in call('/api/v1/slots?' + query, self.alice)], [slot['id']])
        self.assertEqual(call('/api/v1/slots?' + empty, self.alice), [])
        invalid = urllib.parse.urlencode({'from': start.isoformat(), 'to': (start + dt.timedelta(days=94)).isoformat()})
        call('/api/dso/v1/appointments?' + invalid, self.staff['doctor'], expected=400)
        call('/api/v1/slots?' + invalid, self.alice, expected=400)
        call('/api/dso/v1/appointments?from=2026-10-01', self.staff['doctor'], expected=400)
        doctors = call('/api/dso/v1/doctors', self.staff['doctor'])
        self.assertEqual([doctor['id'] for doctor in doctors if doctor['canSchedule']], ['d_001'])
        self.assertTrue(all(doctor['canSchedule'] for doctor in call('/api/dso/v1/doctors', self.staff['manager'])))
        self.assertTrue(all(not doctor['canSchedule'] for doctor in call('/api/dso/v1/doctors', self.alice)))

    def test_14_organization_and_clinic_scope_switching(self):
        regional_key = secrets.token_hex(32)
        second_token = secrets.token_hex(32)
        sql("INSERT INTO platform_clinic(id,organization_id,name,tenant_id) VALUES('clinic_second','org_demo','Synthetic second clinic','tenant_demo'); INSERT INTO platform_organization(id,tenant_id,name) VALUES('org_other','tenant_demo','Synthetic other organization'); INSERT INTO platform_clinic(id,organization_id,name,tenant_id) VALUES('clinic_other','org_other','Synthetic unrelated clinic','tenant_demo'); INSERT INTO platform_user(id,tenant_id,clinic_id,identity_key,nickname) VALUES('regional_user','tenant_demo','clinic_demo','staff:regional','Synthetic regional manager'),('patient_second','tenant_demo','clinic_second','demo:second','Synthetic second clinic patient'); INSERT INTO platform_role_assignment(id,user_id,tenant_id,organization_id,role) VALUES('regional_role','regional_user','tenant_demo','org_demo','regional_manager'); INSERT INTO platform_role_assignment(id,user_id,tenant_id,clinic_id,role) VALUES('second_patient_role','patient_second','tenant_demo','clinic_second','patient'); INSERT INTO dso_doctor(id,tenant_id,clinic_id,display_name) VALUES('doctor_second','tenant_demo','clinic_second','Synthetic second clinic doctor');")
        sql("INSERT INTO platform_staff_credential(user_id,token_hash) VALUES('regional_user','" + hashlib.sha256(regional_key.encode()).hexdigest() + "')")
        regional = call('/api/v1/auth/staff-login', method='POST', body={'identity':'staff:regional'}, headers={'X-Staff-Dev-Key':regional_key})['token']
        second_token = call('/api/v1/auth/mp-login', method='POST', body={'code':'demo:second'}, headers={'X-Dev-Key':DEV})['token']
        self.assertEqual(call('/api/v1/users/me', second_token)['clinicId'], 'clinic_second')
        self.assertEqual({clinic['id'] for clinic in call('/api/dso/v1/clinics/mine', regional)}, {'clinic_demo','clinic_second'})
        call('/api/dso/v1/context/switch', regional, 'POST', {'clinicId':'clinic_other'}, expected=404)
        call('/api/dso/v1/context/switch', self.staff['doctor'], 'POST', {'clinicId':'clinic_second'}, expected=404)
        first_lead = call('/api/dso/v1/crm/leads', regional, 'POST', {'title':'Synthetic shared idempotency key'}, {'Idempotency-Key':'clinic-scoped-lead'})
        call('/api/dso/v1/context/switch', regional, 'POST', {'clinicId':'clinic_second'})
        self.assertEqual(call('/api/dso/v1/context', regional)['roles'], ['regional_manager'])
        second_lead = call('/api/dso/v1/crm/leads', regional, 'POST', {'title':'Synthetic shared idempotency key'}, {'Idempotency-Key':'clinic-scoped-lead'})
        self.assertNotEqual(first_lead['id'], second_lead['id'])
        call('/api/dso/v1/crm/leads/' + first_lead['id'], regional, expected=404)
        second_patient = call('/api/dso/v1/patients/me', second_token)['id']
        self.assertEqual([row['id'] for row in call('/api/dso/v1/patients', regional)], [second_patient])
        call('/api/dso/v1/patients/' + self.patient, regional, expected=404)
        start = dt.datetime.now(dt.timezone.utc) + dt.timedelta(days=20)
        slot = call('/api/dso/v1/slots', regional, 'POST', {'doctorId':'doctor_second','startsAt':start.isoformat(),'endsAt':(start+dt.timedelta(minutes=30)).isoformat()}, {'Idempotency-Key':'second-clinic-slot'})
        call('/api/v1/appointments', self.alice, 'POST', {'slotId':slot['id']}, {'Idempotency-Key':'cross-clinic-booking','X-Clinic-Id':'clinic_second'}, expected=404)
        appointment = call('/api/v1/appointments', second_token, 'POST', {'slotId':slot['id']}, {'Idempotency-Key':'second-clinic-booking'})
        self.assertEqual(call('/api/dso/v1/appointments', regional)[0]['id'], appointment['id'])
        call('/api/dso/v1/context/switch', regional, 'POST', {'clinicId':'clinic_demo'})
        call('/api/dso/v1/appointments/' + appointment['id'], regional, expected=404)
        self.assertEqual(call('/api/dso/v1/crm/leads', regional, 'POST', {'title':'Synthetic shared idempotency key'}, {'Idempotency-Key':'clinic-scoped-lead'})['id'], first_lead['id'])

    def test_15_json_object_limits_and_duplicate_fields(self):
        for payload, expected in [(b'null',400),(b'[]',400),(b'{"identity":"staff:doctor","identity":"staff:manager"}',400),(b'{"nested":{"x":1,"x":2}}',400),(b'{"padding":"'+b'x'*65536+b'"}',413)]:
            req = urllib.request.Request(BASE + "/api/v1/auth/staff-login", data=payload, headers={"Content-Type":"application/json","X-Staff-Dev-Key":STAFF_KEYS["doctor"]}, method="POST")
            with self.assertRaises(urllib.error.HTTPError) as caught:
                urllib.request.urlopen(req, timeout=10)
            self.assertEqual(caught.exception.code, expected)
        call("/api/dso/v1/context", self.staff["doctor"])

    def test_16_upload_admission_releases_after_slow_clients(self):
        grants = [call('/api/v1/media/upload-token', token, 'POST', {'scene':'ai_photo','ext':'png'}) for token in [self.alice,self.bob,self.alice]]
        release = threading.Event()
        def slow_upload(grant):
            url = urllib.parse.urlsplit(grant['uploadUrl'])
            def chunks():
                yield PNG[:24]
                release.wait(5)
                yield PNG[24:]
            req = urllib.request.Request(BASE+url.path+'?'+url.query, data=chunks(), headers={'Content-Type':'image/png'}, method='PUT')
            with urllib.request.urlopen(req, timeout=10) as response:
                return response.status
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            futures = [pool.submit(slow_upload, grant) for grant in grants[:2]]
            try:
                for attempt in range(30):
                    busy = int(sql("SELECT count(*) FROM pg_stat_activity WHERE datname='dso_test' AND state='idle in transaction' AND query LIKE '%r.body::text%'").stdout.strip())
                    if busy >= 2: break
                    time.sleep(0.05)
                else: self.fail('Both slow uploads must be admitted before testing the capacity bound')
                self.assertEqual(upload(grants[2], PNG, expected=429), None)
                self.assertEqual(request('/health')[0], 200)
            finally:
                release.set()
            self.assertEqual([future.result() for future in futures], [200,200])
        self.assertEqual(upload(grants[2], PNG)['size'], len(PNG))

    def test_99_failed_login_counts_survive_rollback(self):
        statuses = [request("/api/v1/auth/staff-login", method="POST", body={"identity": "staff:doctor"}, headers={"X-Staff-Dev-Key": "wrong"})[0] for _ in range(61)]
        self.assertIn(401, statuses)
        self.assertEqual(statuses[-1], 429)


class QueueTests(unittest.TestCase):
    workers = []

    @classmethod
    def setUpClass(cls):
        name = RUN + "-odoo-stub"
        stub = Path(__file__).with_name("odoo_stub.py").resolve()
        cls.key = next(x.split("=", 1)[1] for x in Path(ENV_FILE).read_text().splitlines() if x.startswith("ODOO_INTEGRATION_KEY="))
        command("docker", "run", "-d", "--name", name, "--network", RUN, "--label", "smilelab.test=" + RUN, "--cpus", "0.5", "--memory", "128m", "--env-file", ENV_FILE, "-p", "127.0.0.1::8080", "-v", str(stub) + ":/stub.py:ro", PYTHON, "python", "-B", "/stub.py")
        cls.workers.append(name)
        port = command("docker", "port", name, "8080/tcp").stdout.strip().rsplit(":", 1)[1]
        cls.stub_url = "http://127.0.0.1:" + port
        for _ in range(60):
            try:
                cls.stub_request()
                break
            except OSError:
                time.sleep(0.25)
        else:
            raise RuntimeError("Odoo stub did not start")
        for i in range(2):
            worker = RUN + "-worker-" + str(i)
            command("docker", "run", "-d", "--name", worker, "--network", RUN, "--label", "smilelab.test=" + RUN, "--cpus", "0.5", "--memory", "256m", "--env-file", ENV_FILE, "-e", "ODOO_SYNC_ENABLED=true", "-e", "AI_WORKER_ENABLED=false", "-e", "ODOO_TIMEOUT_SECONDS=1", "-e", f"ODOO_INTERNAL_URL=http://{name}:8080", IMAGE)
            cls.workers.append(worker)

    @classmethod
    def stub_request(cls, body=None):
        req = urllib.request.Request(cls.stub_url + ("/control" if body is not None else "/state"), data=None if body is None else json.dumps(body).encode(), headers={"Content-Type": "application/json", "Authorization": "Bearer " + cls.key}, method="GET" if body is None else "POST")
        with urllib.request.urlopen(req, timeout=5) as response:
            return json.load(response)

    @classmethod
    def tearDownClass(cls):
        for name in reversed(cls.workers):
            command("docker", "rm", "-f", "-v", name, check=False)

    def wait_status(self, identifier, status, timeout=20):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            value = sql("SELECT status FROM integration_outbox WHERE id='" + identifier + "'").stdout.strip()
            if value == status:
                return
            time.sleep(0.2)
        error = sql("SELECT last_error FROM integration_outbox WHERE id='" + identifier + "'").stdout.strip()
        self.fail(f"Outbox {identifier} did not reach {status}; last state {value}; {error}")

    def test_01_multiple_workers_claim_without_duplicate_creation(self):
        for i in range(12):
            sql(f"INSERT INTO integration_outbox(id,tenant_id,event_type,aggregate_id,payload) VALUES('evt_multi_{i}','tenant_demo','crm.lead.created','multi_{i}','{{\"name\":\"Synthetic\"}}')")
        for i in range(12):
            self.wait_status("evt_multi_" + str(i), "delivered")
            self.assertEqual(self.stub_request()["counts"].get("evt_multi_" + str(i)), 1)
        self.assertEqual(sql("SELECT count(*) FROM integration_mapping WHERE platform_id LIKE 'multi_%'").stdout.strip(), "12")

    def test_02_expired_lease_recovers_original_remote_result(self):
        self.stub_request({"seen": {"evt_stale": 999}})
        sql("INSERT INTO integration_outbox(id,tenant_id,event_type,aggregate_id,payload,status,attempts,lease_token,lease_until) VALUES('evt_stale','tenant_demo','crm.lead.created','stale','{}','processing',1,'old-worker',now()-interval '1 second')")
        self.wait_status("evt_stale", "delivered")
        self.assertEqual(sql("SELECT attempts FROM integration_outbox WHERE id='evt_stale'").stdout.strip(), "2")
        self.assertEqual(sql("SELECT odoo_id FROM integration_mapping WHERE platform_id='stale'").stdout.strip(), "999")

    def test_03_final_failure_dead_letter_and_manual_recovery(self):
        self.stub_request({"failureAdd": "evt_fail"})
        sql("INSERT INTO integration_outbox(id,tenant_id,event_type,aggregate_id,payload,attempts) VALUES('evt_fail','tenant_demo','crm.lead.created','failure','{}',9)")
        self.wait_status("evt_fail", "dead_letter")
        self.assertEqual(sql("SELECT attempts FROM integration_outbox WHERE id='evt_fail'").stdout.strip(), "10")
        self.stub_request({"failureRemove": "evt_fail"})
        call("/api/dso/v1/operations/outbox/evt_fail/retry", DsoTests.staff["operator"], "POST", {"reason": "Stub recovered"})
        self.wait_status("evt_fail", "delivered")
        self.assertEqual(sql("SELECT attempts FROM integration_outbox WHERE id='evt_fail'").stdout.strip(), "1")
        self.assertEqual(sql("SELECT details->>'reason' FROM platform_audit WHERE resource_id='evt_fail' AND action='integration.dead_letter_retried'").stdout.strip(), "Stub recovered")

    def test_04_expired_last_attempt_is_dead_lettered(self):
        sql("INSERT INTO integration_outbox(id,tenant_id,event_type,aggregate_id,payload,status,attempts,lease_token,lease_until) VALUES('evt_exhausted','tenant_demo','crm.lead.created','exhausted','{}','processing',10,'lost-worker',now()-interval '1 second')")
        self.wait_status("evt_exhausted", "dead_letter")
        self.assertNotIn("evt_exhausted", self.stub_request()["counts"])

    def test_05_http_timeout_does_not_stop_worker_host(self):
        self.stub_request({"delays": {"evt_timeout": 2}})
        sql("INSERT INTO integration_outbox(id,tenant_id,event_type,aggregate_id,payload) VALUES('evt_timeout','tenant_demo','crm.lead.created','timeout','{}')")
        self.wait_status("evt_timeout", "pending")
        deadline = time.monotonic() + 15
        while time.monotonic() < deadline:
            if sql("SELECT attempts||':'||status FROM integration_outbox WHERE id='evt_timeout'").stdout.strip() == "1:pending":
                break
            time.sleep(0.2)
        else:
            self.fail("Timeout did not consume an attempt")
        self.stub_request({"delays": {"evt_timeout": 0}})
        sql("UPDATE integration_outbox SET next_attempt=now() WHERE id='evt_timeout'")
        self.wait_status("evt_timeout", "delivered")
        for name in self.workers[1:]:
            self.assertEqual(command("docker", "inspect", "--format", "{{.State.Running}}", name).stdout.strip(), "true")


def main():
    global BASE, ENV_FILE, IMAGE
    parser = argparse.ArgumentParser()
    parser.add_argument("--image", default="chipmunk-platform:dso-candidate")
    parser.add_argument("--result", default="/tmp/smilelab-dso-tests.json")
    parser.add_argument("--ui-handoff", help="将临时 UI 测试凭据保存到私有文件；仅在显式指定时保留测试环境")
    parser.add_argument("--hold-seconds", type=int, default=1800)
    args = parser.parse_args()
    IMAGE = args.image
    started = time.monotonic()
    created = []
    with tempfile.TemporaryDirectory(prefix=RUN + "-") as temp:
        env = Path(temp) / "test.env"
        ENV_FILE = str(env)
        env.write_text("\n".join(["POSTGRES_USER=test", "POSTGRES_DB=dso_test", "POSTGRES_PASSWORD=" + secrets.token_hex(32), f"PLATFORM_DATABASE=Host={DB};Database=dso_test;Username=test;Password=placeholder;Maximum Pool Size=15", "DEMO_AUTH=true", "DEV_API_KEY=" + DEV, "STAFF_DEMO_KEY=" + STAFF, "MEDIA_SIGNING_KEY=" + secrets.token_hex(32), "ODOO_INTEGRATION_KEY=" + secrets.token_hex(32), "ODOO_SYNC_ENABLED=false", "PUBLIC_URL=http://127.0.0.1", "MEDIA_ROOT=/tmp/dso-media"]) + "\n")
        fields = env.read_text().splitlines()
        password = next(x.split("=", 1)[1] for x in fields if x.startswith("POSTGRES_PASSWORD="))
        env.write_text(env.read_text().replace("Password=placeholder", "Password=" + password))
        os.chmod(env, 0o600)
        try:
            command("docker", "network", "create", "--label", "smilelab.test=" + RUN, RUN)
            command("docker", "run", "-d", "--name", DB, "--network", RUN, "--label", "smilelab.test=" + RUN, "--cpus", "0.5", "--memory", "256m", "--env-file", str(env), POSTGRES)
            created.append(DB)
            for _ in range(60):
                if command("docker", "exec", DB, "pg_isready", "-h", "127.0.0.1", "-U", "test", check=False).returncode == 0:
                    break
                time.sleep(0.5)
            else:
                raise RuntimeError("Isolated test database did not become ready")
            schema = Path(__file__).resolve().parents[1] / "schema.sql"
            sql(schema.read_text())
            sql("INSERT INTO platform_tenant VALUES('tenant_demo','Legacy demo'); INSERT INTO platform_organization VALUES('org_demo','tenant_demo','Legacy demo'); INSERT INTO platform_clinic VALUES('clinic_demo','org_demo','Legacy demo'); INSERT INTO platform_user(id,tenant_id,clinic_id,identity_key,nickname) VALUES('legacy_user','tenant_demo','clinic_demo','demo:legacy','Legacy synthetic'); INSERT INTO platform_session(token_hash,user_id,expires_at) VALUES('legacy-session-hash','legacy_user',now()+interval '1 hour');")
            command("docker", "run", "-d", "--name", API, "--network", RUN, "--label", "smilelab.test=" + RUN, "--cpus", "1", "--memory", "512m", "-p", "127.0.0.1::8080", "--env-file", str(env), args.image)
            created.append(API)
            published = command("docker", "port", API, "8080/tcp", check=False)
            if published.returncode != 0:
                logs = command("docker", "logs", API, check=False)
                state = command("docker", "inspect", "--format", "{{.State.Status}} {{json .NetworkSettings.Ports}}", API, check=False)
                raise RuntimeError("Isolated API port unavailable: " + state.stdout + (logs.stdout + logs.stderr)[-4000:])
            port = published.stdout.strip().rsplit(":", 1)[1]
            BASE = "http://127.0.0.1:" + port
            for _ in range(60):
                try:
                    with urllib.request.urlopen(BASE + "/health", timeout=2) as response:
                        if response.status == 200:
                            break
                except (OSError, urllib.error.URLError):
                    time.sleep(0.5)
            else:
                logs = command("docker", "logs", API).stdout[-3000:]
                raise RuntimeError("Isolated API did not become ready: " + logs)
            suite = unittest.TestSuite([unittest.defaultTestLoader.loadTestsFromTestCase(DsoTests), unittest.defaultTestLoader.loadTestsFromTestCase(QueueTests)])
            result = unittest.TextTestRunner(verbosity=2).run(suite)
            report = {"status": "passed" if result.wasSuccessful() else "failed", "tests": result.testsRun, "failures": len(result.failures), "errors": len(result.errors), "durationSeconds": round(time.monotonic() - started, 2), "isolation": "temporary Docker network and database; synthetic fixtures", "image": args.image}
            Path(args.result).write_text(json.dumps(report, indent=2) + "\n")
            if not result.wasSuccessful():
                raise SystemExit(1)
            if args.ui_handoff:
                sql("DELETE FROM platform_rate_limit WHERE scope LIKE 'staff-login:%'")
                handoff = Path(args.ui_handoff).resolve()
                release = handoff.with_suffix(".done")
                if release.exists():
                    release.unlink()
                descriptor = os.open(handoff, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
                with os.fdopen(descriptor, "w") as output:
                    json.dump({"apiBase": BASE, "staffKeys": STAFF_KEYS, "devKey": DEV, "staffTokens": DsoTests.staff, "aliceToken": DsoTests.alice, "alicePatientId": DsoTests.patient}, output)
                print("UI_SESSION_READY " + str(handoff), flush=True)
                deadline = time.monotonic() + max(1, min(args.hold_seconds, 3600))
                while time.monotonic() < deadline and not release.exists():
                    time.sleep(1)
                handoff.unlink(missing_ok=True)
                release.unlink(missing_ok=True)
        finally:
            for container in reversed(created + QueueTests.workers):
                label = command("docker", "inspect", "--format", '{{index .Config.Labels "smilelab.test"}}', container, check=False)
                if label.stdout.strip() == RUN:
                    command("docker", "rm", "-f", "-v", container, check=False)
            command("docker", "network", "rm", RUN, check=False)


if __name__ == "__main__":
    main()
