"""离线配置独立员工演示账号；密钥只写私有文件，数据库仅保存 SHA-256。"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import subprocess

ACCOUNTS = {
    "doctor": ("doctor", "林医生（演示）", "d_001"),
    "doctor2": ("doctor", "陈医生（演示）", "d_002"),
    "manager": ("clinic_manager", "门诊经理（演示）", None),
    "consultant": ("consultant", "咨询顾问（演示）", None),
    "operator": ("platform_operator", "平台运维（演示）", None),
}


def literal(value):
    return "'" + value.replace("'", "''") + "'"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--container", default="chipmunk-chipmunk-db-1")
    parser.add_argument("--database", default="chipmunk_platform")
    parser.add_argument("--user", default="chipmunk")
    parser.add_argument("--output", required=True, help="现有文件保留密钥；首次文件以 0600 创建")
    args = parser.parse_args()
    if any(not re.fullmatch(r"[A-Za-z0-9_-]+", value) for value in (args.container, args.database, args.user)):
        parser.error("Invalid database identifier")
    output = Path(args.output).resolve()
    if output.exists():
        if output.is_symlink() or (os.name != "nt" and output.stat().st_mode & 0o077):
            raise SystemExit("Credential file must be private and must not be a symbolic link")
        document = json.loads(output.read_text(encoding="utf-8"))
        if document.get("tenantId") != "tenant_demo" or document.get("clinicId") != "clinic_demo":
            raise SystemExit("Credential file belongs to another context")
        keys = {name: document["accounts"][name]["key"] for name in ACCOUNTS}
        if any(not re.fullmatch(r"[a-f0-9]{64}", key) for key in keys.values()):
            raise SystemExit("Credential file contains invalid keys")
    else:
        keys = {name: secrets.token_hex(32) for name in ACCOUNTS}
        document = {
            "environment": "development-demo", "workspace": "https://app.smilelab.ai/workspace/",
            "tenantId": "tenant_demo", "clinicId": "clinic_demo",
            "authentication": "POST /api/v1/auth/staff-login; X-Staff-Dev-Key; body identity",
            "accounts": {name: {"identity": "staff:" + name, "role": role, "key": keys[name]} for name, (role, _, _) in ACCOUNTS.items()},
        }
        output.parent.mkdir(parents=True, exist_ok=True)
        descriptor = os.open(output, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, "w", encoding="utf-8") as file:
            json.dump(document, file, ensure_ascii=False, indent=2)
            file.write("\n")
    statements = ["BEGIN; SELECT pg_advisory_xact_lock(hashtext('provision-dso-demo-staff'));",
        "DO $$ BEGIN IF NOT EXISTS(SELECT 1 FROM platform_migration WHERE version='006_staff_credentials.sql') THEN RAISE EXCEPTION 'DSO migration 006 required'; END IF; END $$;"]
    # 已存在账号的凭据必须匹配；本工具不会隐式轮换密钥或扩权其他账号。
    for name, (role, display, doctor) in ACCOUNTS.items():
        user = "staff_demo_" + name
        digest = hashlib.sha256(keys[name].encode()).hexdigest()
        statements += [
            f"INSERT INTO platform_user(id,tenant_id,clinic_id,identity_key,nickname) VALUES({literal(user)},'tenant_demo','clinic_demo',{literal('staff:' + name)},{literal(display)}) ON CONFLICT(id) DO NOTHING;",
            f"DO $$ BEGIN IF NOT EXISTS(SELECT 1 FROM platform_user WHERE id={literal(user)} AND tenant_id='tenant_demo' AND clinic_id='clinic_demo' AND identity_key={literal('staff:' + name)}) THEN RAISE EXCEPTION 'Existing staff identity mismatch'; END IF; IF EXISTS(SELECT 1 FROM platform_staff_credential WHERE user_id={literal(user)} AND token_hash<>{literal(digest)}) THEN RAISE EXCEPTION 'Existing staff credential mismatch'; END IF; END $$;",
            f"DELETE FROM platform_role_assignment WHERE user_id={literal(user)} AND role='patient';",
            f"INSERT INTO platform_role_assignment(id,user_id,tenant_id,clinic_id,role) VALUES({literal('role_' + user)},{literal(user)},'tenant_demo','clinic_demo',{literal(role)}) ON CONFLICT(id) DO NOTHING;",
            f"INSERT INTO platform_staff_credential(user_id,token_hash) VALUES({literal(user)},{literal(digest)}) ON CONFLICT(user_id) DO NOTHING;",
            f"DO $$ BEGIN IF NOT EXISTS(SELECT 1 FROM platform_role_assignment WHERE user_id={literal(user)} AND tenant_id='tenant_demo' AND clinic_id='clinic_demo' AND role={literal(role)}) OR EXISTS(SELECT 1 FROM platform_staff_credential WHERE user_id={literal(user)} AND revoked) THEN RAISE EXCEPTION 'Existing staff role or credential revoked'; END IF; END $$;",
        ]
        if doctor:
            statements += [f"UPDATE dso_doctor SET user_id={literal(user)} WHERE id={literal(doctor)} AND tenant_id='tenant_demo' AND clinic_id='clinic_demo' AND (user_id IS NULL OR user_id={literal(user)});",
                f"DO $$ BEGIN IF NOT EXISTS(SELECT 1 FROM dso_doctor WHERE id={literal(doctor)} AND user_id={literal(user)} AND tenant_id='tenant_demo' AND clinic_id='clinic_demo') THEN RAISE EXCEPTION 'Doctor mapping mismatch'; END IF; END $$;"]
    statements.append("COMMIT;")
    result = subprocess.run(["docker", "exec", "-i", args.container, "psql", "-X", "-v", "ON_ERROR_STOP=1", "-U", args.user, "-d", args.database], input="\n".join(statements), text=True, capture_output=True)
    if result.returncode:
        # 不转发 SQL 文本或数据库错误上下文，避免密钥摘要/身份泄露到 CI 日志。
        raise SystemExit("Staff provisioning failed; transaction rolled back. Private file retained for a safe retry.")
    print("Provisioned 5 separate staff demo accounts; credentials saved privately. Existing credentials preserved.")


if __name__ == "__main__":
    main()
