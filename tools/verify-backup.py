"""验证最新开发快照，临时数据库完成恢复后删除；文件在内存中校验。"""
import hashlib
import json
from pathlib import Path
import subprocess
import tarfile

root=Path('/srv/dcad/backups/smilelab')
snapshot=sorted(p for p in root.iterdir() if p.is_dir() and p.name.endswith('Z'))[-1]
subprocess.run(['sha256sum','-c','SHA256SUMS'],cwd=snapshot,check=True)
def sql(container,user,database,text):
    return subprocess.check_output(['docker','exec',container,'psql','-U',user,'-d',database,'-Atc',text],text=True).strip()
for container,user,database,dump,table in [
    ('chipmunk-chipmunk-db-1','chipmunk','chipmunk_platform','platform.dump','platform_user'),
    ('chipmunk-odoo-db-1','odoo','smilelab_demo','odoo.dump','crm_lead')]:
    target='smilelab_restore_check'
    sql(container,user,'postgres',f'CREATE DATABASE {target}')
    try:
        with (snapshot/dump).open('rb') as data:
            subprocess.run(['docker','exec','-i',container,'pg_restore','-U',user,'-d',target,'--no-owner','--exit-on-error'],stdin=data,check=True)
        count=int(sql(container,user,target,f'SELECT count(*) FROM {table}'))
        assert count>0
        print('PASS restored',dump,'and checked',table)
    finally:
        sql(container,user,'postgres',f'DROP DATABASE {target}')
rows=sql('chipmunk-chipmunk-db-1','chipmunk','chipmunk_platform',"SELECT json_agg(json_build_object('id',id,'ext',body->>'ext','sha256',body->>'sha256')) FROM platform_resource WHERE kind='media' AND body->>'uploaded'='true'")
objects=json.loads(rows or '[]')
with tarfile.open(snapshot/'media.tar.gz','r:gz') as archive:
    for obj in objects:
        file=archive.extractfile('./'+obj['id']+'.'+obj['ext'])
        assert file and hashlib.sha256(file.read()).hexdigest()==obj['sha256']
with tarfile.open(snapshot/'odoo-data.tar.gz','r:gz') as archive:
    assert any('/filestore/smilelab_demo/' in m.name for m in archive.getmembers())
print('PASS backup media byte hashes and Odoo filestore presence')
Path('/tmp/smilelab-backup-verification.json').write_text(json.dumps({'snapshot':snapshot.name,'databasesRestored':2,'mediaHashesVerified':len(objects),'odooFilestorePresent':True,'offsiteBackup':False,'date':'2026-10-05'},indent=2)+'\n')
