#!/usr/bin/env python3
"""独立 Odoo/PostgreSQL 验证商业桥接；容器均有专属标签，不接触线上数据库。"""
import argparse
import concurrent.futures
import json
import os
from pathlib import Path
import secrets
import subprocess
import tempfile
import time
import unittest
import urllib.error
import urllib.request

ODOO = "odoo:19.0@sha256:dd9013e669caaa23d26765dc55814655eaeecca7cfc2c265dbabae913bce22fd"
POSTGRES = "postgres:16-alpine@sha256:721873c34ceb9f8d8fc265984940dc982404c105f19ad51be9fdc5970a6080ea"
RUN = "dso-bridge-test-" + secrets.token_hex(5)
DB, WEB, INIT, VOLUME = [RUN + suffix for suffix in ["-db", "-web", "-init", "-data"]]
KEY = secrets.token_hex(32)
BASE = ""


def command(*args, input=None, check=True):
    result = subprocess.run(args, input=input, text=True, capture_output=True, timeout=360)
    if check and result.returncode:
        raise RuntimeError("Isolated Docker command failed: " + str(args[:3]) + "\n" + result.stderr[-2000:])
    return result


def sql(statement):
    return command("docker", "exec", "-i", DB, "psql", "-X", "-U", "test", "-d", "dso_bridge_test", "-At", "-v", "ON_ERROR_STOP=1", input=statement).stdout.strip()


def send(body, expected=200, token=None):
    data = body if isinstance(body, bytes) else json.dumps(body).encode()
    request = urllib.request.Request(BASE + "/chipmunk/integration/events", data=data, headers={"Content-Type": "application/json", "Authorization": "Bearer " + (token or KEY)}, method="POST")
    try:
        response = urllib.request.urlopen(request, timeout=20)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        raw = response.read()
        try:
            result = json.loads(raw)
        except ValueError:
            raise AssertionError(f"Non-JSON bridge response: HTTP {response.status}") from None
        if response.status != expected:
            raise AssertionError(f"Bridge expected {expected}, got {response.status}: {result.get('message')}")
        return result


def event(suffix, tenant="tenant_demo"):
    return {"eventId": "evt_" + suffix, "tenantId": tenant, "eventType": "crm.lead.created", "platformId": "lead_" + suffix, "payload": {"name": "Synthetic commercial " + suffix, "platformRef": "lead_" + suffix}}


class BridgeTests(unittest.TestCase):
    def test_01_service_auth_and_unmapped_tenant(self):
        send(event("invalid_auth"), 401, "invalid")
        send(event("foreign", "unmapped_tenant"), 403)
        send(dict(event("wrong_type"), eventType="patient.record.updated"), 403)

    def test_02_body_and_payload_validation(self):
        send(b"{", 400)
        send(b"x" * 65537, 413)
        send(dict(event("unknown"), patientName="Synthetic private"), 400)
        send(dict(event("clinical"), payload={"name": "Synthetic", "medicalHistory": "Synthetic private"}), 400)
        send(dict(event("wrong_ref"), payload={"platformRef": "different"}), 400)
        send(dict(event("bad_type"), payload=[]), 400)
        send(dict(event("blank"), payload={"name": "   "}), 400)

    def test_03_replay_and_changed_body_conflict(self):
        body = event("replay")
        first = send(body)["data"]
        second = send(body)["data"]
        self.assertEqual(first["id"], second["id"])
        self.assertFalse(first["duplicate"])
        self.assertTrue(second["duplicate"])
        send(dict(body, payload={"name": "Changed synthetic", "platformRef": body["platformId"]}), 409)
        send(dict(body, platformId="changed", payload={"name": "Changed synthetic"}), 409)
        self.assertEqual(sql("SELECT count(*) FROM chipmunk_integration_event WHERE event_id='evt_replay'"), "1")

    def test_04_concurrent_event_only_creates_one_lead(self):
        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
            results = list(pool.map(lambda _: send(event("concurrent"))["data"], range(8)))
        self.assertEqual(len({row["id"] for row in results}), 1)
        self.assertEqual(sum(not row["duplicate"] for row in results), 1)
        self.assertEqual(sql("SELECT count(*) FROM crm_lead WHERE chipmunk_platform_ref='lead_concurrent'"), "1")

    def test_05_admin_mapping_and_access_control(self):
        code = """
from odoo.exceptions import AccessError
company = env['res.company'].create({'name': 'Synthetic second company'})
env['chipmunk.tenant.company'].create({'tenant_id': 'tenant_second', 'company_id': company.id})
try:
    env['chipmunk.tenant.company'].with_user(env.ref('base.public_user')).search([])
    raise AssertionError('Public user read tenant mapping')
except AccessError:
    pass
env.cr.commit()
print('SYNTHETIC_MAPPING_AND_ACL_OK')
"""
        result = command("docker", "exec", "-i", WEB, "odoo", "shell", "-c", "/etc/odoo/odoo.conf", "-d", "dso_bridge_test", "--no-http", "--workers=0", input=code)
        self.assertIn("SYNTHETIC_MAPPING_AND_ACL_OK", result.stdout)
        remote = send(event("second_company", "tenant_second"))["data"]["id"]
        expected = sql("SELECT company_id FROM chipmunk_tenant_company WHERE tenant_id='tenant_second'")
        self.assertEqual(sql(f"SELECT company_id FROM crm_lead WHERE id={int(remote)}"), expected)
        send(dict(event("replay"), tenantId="tenant_second"), 409)
        sql("UPDATE chipmunk_tenant_company SET active=false WHERE tenant_id='tenant_second'")
        send(event("disabled", "tenant_second"), 403)

    def test_06_legacy_event_hash_upgrade_does_not_duplicate(self):
        body = event("legacy")
        remote = send(body)["data"]["id"]
        sql("UPDATE chipmunk_integration_event SET payload_hash=NULL WHERE event_id='evt_legacy'")
        replay = send(body)["data"]
        self.assertEqual(replay["id"], remote)
        self.assertTrue(replay["duplicate"])
        self.assertEqual(sql("SELECT length(payload_hash) FROM chipmunk_integration_event WHERE event_id='evt_legacy'"), "64")
        sql("UPDATE chipmunk_integration_event SET payload_hash=NULL WHERE event_id='evt_legacy'")
        send(dict(body, payload={"name": "Changed legacy name"}), 409)

    def test_07_admin_views_and_mapping_seed(self):
        self.assertEqual(sql("SELECT count(*) FROM chipmunk_tenant_company WHERE tenant_id='tenant_demo' AND active"), "1")
        self.assertEqual(sql("SELECT count(*) FROM ir_model_data WHERE module='chipmunk_bridge' AND name IN ('tenant_company_list','tenant_company_form','integration_event_list','integration_menu')"), "4")


def main():
    global BASE
    parser = argparse.ArgumentParser()
    parser.add_argument("--addons", default=str(Path(__file__).resolve().parents[2] / "odoo" / "addons"))
    parser.add_argument("--result", default="/tmp/smilelab-odoo-tests.json")
    args = parser.parse_args()
    addons = Path(args.addons).resolve()
    if not addons.joinpath("chipmunk_bridge", "__manifest__.py").is_file():
        raise SystemExit("Bridge addon not found")
    created = []
    started = time.monotonic()
    with tempfile.TemporaryDirectory(prefix=RUN) as temp:
        password = secrets.token_hex(32)
        env = Path(temp) / "test.env"
        env.write_text(f"POSTGRES_USER=test\nPOSTGRES_PASSWORD={password}\nHOST={DB}\nUSER=test\nPASSWORD={password}\nODOO_INTEGRATION_KEY={KEY}\n")
        os.chmod(env, 0o600)
        config = Path(temp) / "odoo.conf"
        config.write_text(f"[options]\nadmin_passwd={secrets.token_hex(32)}\ndb_host={DB}\ndb_port=5432\ndb_user=test\ndb_password={password}\ndb_name=dso_bridge_test\ndbfilter=^dso_bridge_test$\nlist_db=False\naddons_path=/usr/lib/python3/dist-packages/odoo/addons,/mnt/extra-addons\nworkers=0\nmax_cron_threads=0\nproxy_mode=False\n")
        # 临时父目录 0700；只读绑定文件需供容器内 UID100 读取。
        os.chmod(config, 0o644)
        try:
            command("docker", "network", "create", "--label", "smilelab.test=" + RUN, RUN)
            command("docker", "volume", "create", "--label", "smilelab.test=" + RUN, VOLUME)
            command("docker", "run", "-d", "--name", DB, "--network", RUN, "--label", "smilelab.test=" + RUN, "--cpus", "0.5", "--memory", "256m", "--env-file", str(env), POSTGRES)
            created.append(DB)
            for _ in range(60):
                if command("docker", "exec", DB, "pg_isready", "-h", "127.0.0.1", "-U", "test", check=False).returncode == 0:
                    break
                time.sleep(0.5)
            common = ["--network", RUN, "--label", "smilelab.test=" + RUN, "--cpus", "2", "--memory", "2g", "--env-file", str(env), "-v", str(addons) + ":/mnt/extra-addons:ro", "-v", str(config) + ":/etc/odoo/odoo.conf:ro", "-v", VOLUME + ":/var/lib/odoo"]
            print("Initializing isolated Odoo 19 bridge and dependencies", flush=True)
            created.append(INIT)
            command("docker", "run", "--name", INIT, *common, ODOO, "odoo", "-c", "/etc/odoo/odoo.conf", "-d", "dso_bridge_test", "-i", "chipmunk_bridge", "--without-demo", "--stop-after-init")
            print("Starting isolated Odoo HTTP service", flush=True)
            command("docker", "run", "-d", "--name", WEB, *common, "-p", "127.0.0.1::8069", ODOO)
            created.append(WEB)
            BASE = "http://127.0.0.1:" + command("docker", "port", WEB, "8069/tcp").stdout.strip().rsplit(":", 1)[1]
            for _ in range(90):
                try:
                    with urllib.request.urlopen(BASE + "/web/health", timeout=2) as response:
                        if response.status == 200:
                            break
                except OSError:
                    time.sleep(0.5)
            else:
                raise RuntimeError("Isolated Odoo health timeout")
            result = unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(BridgeTests))
            Path(args.result).write_text(json.dumps({"status":"passed" if result.wasSuccessful() else "failed","tests":result.testsRun,"failures":len(result.failures),"errors":len(result.errors),"durationSeconds":round(time.monotonic()-started,2),"isolation":"temporary Odoo/PostgreSQL containers, network and filestore"},indent=2)+"\n")
            if not result.wasSuccessful():
                logs = command("docker", "logs", "--tail", "80", WEB, check=False)
                diagnostic = (logs.stdout + logs.stderr).replace(KEY, '[REDACTED]').replace(password, '[REDACTED]')
                print(diagnostic[-12000:])
                raise SystemExit(1)
        finally:
            for container in reversed(created):
                label = command("docker", "inspect", "--format", '{{index .Config.Labels "smilelab.test"}}', container, check=False)
                if label.stdout.strip() == RUN:
                    command("docker", "rm", "-f", "-v", container, check=False)
            label = command("docker", "volume", "inspect", "--format", '{{index .Labels "smilelab.test"}}', VOLUME, check=False)
            if label.stdout.strip() == RUN:
                command("docker", "volume", "rm", VOLUME, check=False)
            command("docker", "network", "rm", RUN, check=False)


if __name__ == "__main__":
    main()
