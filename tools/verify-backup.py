"""校验选定快照，在随机临时数据库恢复；所有文件校验使用恢复后的数据库。"""
import argparse
import hashlib
import json
from pathlib import Path
import secrets
import subprocess
import tarfile

parser = argparse.ArgumentParser()
parser.add_argument("--snapshot", help="默认选择最近的完整快照，仅允许备份根目录中的直接子目录")
args = parser.parse_args()
root = Path('/srv/dcad/backups/smilelab').resolve()
snapshot = Path(args.snapshot).resolve() if args.snapshot else sorted(p for p in root.iterdir() if p.is_dir() and p.name.endswith('Z'))[-1]
if snapshot.parent != root or not snapshot.name.endswith('Z'):
    raise SystemExit('Snapshot must be a complete direct child of the Smilelab backup directory')
subprocess.run(['sha256sum','-c','SHA256SUMS'],cwd=snapshot,check=True)

def sql(container,user,database,text):
    return subprocess.check_output(['docker','exec',container,'psql','-X','-v','ON_ERROR_STOP=1','-U',user,'-d',database,'-Atc',text],text=True).strip()

objects=[]
checks={}
for container,user,dump,table in [
    ('chipmunk-chipmunk-db-1','chipmunk','platform.dump','platform_user'),
    ('chipmunk-odoo-db-1','odoo','odoo.dump','crm_lead')]:
    target='smilelab_restore_'+secrets.token_hex(8)
    sql(container,user,'postgres',f'CREATE DATABASE {target}')
    try:
        with (snapshot/dump).open('rb') as data:
            subprocess.run(['docker','exec','-i',container,'pg_restore','-U',user,'-d',target,'--no-owner','--exit-on-error'],stdin=data,check=True)
        count=int(sql(container,user,target,f'SELECT count(*) FROM {table}'))
        assert count>0
        if dump=='platform.dump':
            rows=sql(container,user,target,"SELECT coalesce(json_agg(json_build_object('id',id,'ext',body->>'ext','sha256',body->>'sha256')),'[]'::json) FROM platform_resource WHERE kind='media' AND body->>'uploaded'='true'")
            objects=json.loads(rows)
            if sql(container,user,target,"SELECT to_regclass('platform_migration') IS NOT NULL")=='t':
                checks['migrationLedgerEntries']=int(sql(container,user,target,'SELECT count(*) FROM platform_migration'))
            for name in ['dso_appointment','dso_clinical_record','platform_staff_credential','dso_order','dso_order_event','dso_order_payment']:
                if sql(container,user,target,f"SELECT to_regclass('{name}') IS NOT NULL")=='t':
                    checks[name]=int(sql(container,user,target,f'SELECT count(*) FROM {name}'))
        else:
            if sql(container,user,target,"SELECT to_regclass('chipmunk_tenant_company') IS NOT NULL")=='t':
                checks['odooTenantCompanyMappings']=int(sql(container,user,target,'SELECT count(*) FROM chipmunk_tenant_company'))
        print('PASS restored',dump,'and checked',table)
    finally:
        sql(container,user,'postgres',f'DROP DATABASE {target}')
with tarfile.open(snapshot/'media.tar.gz','r:gz') as archive:
    for obj in objects:
        file=archive.extractfile('./'+obj['id']+'.'+obj['ext'])
        assert file and hashlib.sha256(file.read()).hexdigest()==obj['sha256']
with tarfile.open(snapshot/'odoo-data.tar.gz','r:gz') as archive:
    assert any('/filestore/smilelab_demo/' in m.name for m in archive.getmembers())
if (snapshot/'source.tar.gz').exists():
    with tarfile.open(snapshot/'source.tar.gz','r:gz') as archive:
        assert any(m.name=='platform/Program.cs' for m in archive.getmembers())
        assert any(m.name=='odoo/addons/chipmunk_bridge/__manifest__.py' for m in archive.getmembers())
    checks['sourceArchiveVerified']=True
if (snapshot/'staff-demo.json').exists():
    staff=json.loads((snapshot/'staff-demo.json').read_text())
    assert len(staff['accounts'])>=5 and checks.get('platform_staff_credential',0)>=len(staff['accounts'])
    checks['staffCredentialBackupVerified']=True
print('PASS snapshot media byte hashes, Odoo filestore and available source/credential archives')
Path('/tmp/smilelab-backup-verification.json').write_text(json.dumps({'snapshot':snapshot.name,'databasesRestored':2,'mediaHashesVerified':len(objects),'odooFilestorePresent':True,'offsiteBackup':False,'checks':checks,'date':'2026-10-05'},indent=2)+'\n')
