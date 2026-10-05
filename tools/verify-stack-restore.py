"""从完整 Smilelab 快照启动隔离应用栈，验证数据与文件恢复；不改变线上卷。"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import subprocess
import tarfile
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request

POSTGRES='postgres:16-alpine@sha256:721873c34ceb9f8d8fc265984940dc982404c105f19ad51be9fdc5970a6080ea'
ODOO='odoo:19.0@sha256:dd9013e669caaa23d26765dc55814655eaeecca7cfc2c265dbabae913bce22fd'
ALPINE='alpine:3.22'
RUN='smilelab-restore-'+secrets.token_hex(6)
LABEL='smilelab.restore='+RUN
containers=[]
volumes=[]
REDACTIONS=[]

def command(*args,input=None,check=True):
    result=subprocess.run(args,input=input,text=True,capture_output=True,timeout=360)
    if check and result.returncode:
        raise RuntimeError('Restore command failed: '+str(args[:3]))
    return result

def sql(container,user,database,statement):
    return command('docker','exec','-i',container,'psql','-X','-v','ON_ERROR_STOP=1','-U',user,'-d',database,'-At',input=statement).stdout.strip()

def start(name,image,arguments):
    command('docker','run','-d','--name',name,'--network',RUN,'--label',LABEL,*arguments,image)
    containers.append(name)

def port(name):
    for attempt in range(30):
        result=command('docker','port',name,'8080/tcp' if name.endswith('-api') else '8069/tcp',check=False)
        if result.returncode==0 and result.stdout.strip():
            return 'http://127.0.0.1:'+result.stdout.strip().rsplit(':',1)[1]
        state=command('docker','inspect','--format','{{.State.Status}}',name,check=False).stdout.strip()
        if state in ('exited','dead'):
            logs=command('docker','logs','--tail','8',name,check=False)
            detail=logs.stdout+logs.stderr
            for value in REDACTIONS:
                if value: detail=detail.replace(value,'[REDACTED]')
            raise RuntimeError('Restored service exited: '+name+' '+detail)
        time.sleep(0.2)
    raise RuntimeError('Restored service port was not published')

def wait(url):
    for attempt in range(90):
        try:
            with urllib.request.urlopen(url,timeout=3) as response:
                if response.status==200: return
        except (OSError,urllib.error.URLError): pass
        time.sleep(1)
    raise RuntimeError('Restored service did not become ready')

def call(base,path,body=None,token=None,headers=None):
    fields={'Content-Type':'application/json',**(headers or {})}
    if token: fields['Authorization']='Bearer '+token
    req=urllib.request.Request(base+path,data=None if body is None else json.dumps(body).encode(),headers=fields,method='GET' if body is None else 'POST')
    with urllib.request.urlopen(req,timeout=30) as response: return json.load(response)

def main():
    global REDACTIONS
    parser=argparse.ArgumentParser()
    parser.add_argument('--snapshot',required=True)
    parser.add_argument('--output',default='/tmp/smilelab-stack-restore.json')
    args=parser.parse_args()
    snapshot=Path(args.snapshot).resolve()
    if snapshot.parent!=Path('/srv/dcad/backups/smilelab') or not snapshot.name.endswith('Z'):
        raise SystemExit('Expected a complete Smilelab snapshot directory')
    subprocess.run(['sha256sum','-c','SHA256SUMS'],cwd=snapshot,check=True,stdout=subprocess.DEVNULL)
    image=(snapshot/'platform-image.txt').read_text().strip()
    if not re.fullmatch(r'sha256:[a-f0-9]{64}',image): raise SystemExit('Invalid snapshot image ID')
    image_archive=snapshot/'platform-image.tar.gz'
    if image_archive.exists():
        command('docker','image','load','--input',str(image_archive))
    assert command('docker','image','inspect','--format','{{.Id}}',image).stdout.strip()==image
    env=dict(line.split('=',1) for line in (snapshot/'chipmunk.env').read_text().splitlines() if '=' in line)
    credentials=json.loads((snapshot/'staff-demo.json').read_text())
    REDACTIONS=[*env.values(),*(account['key'] for account in credentials['accounts'].values())]
    pdb,odb,api,odoo=[RUN+suffix for suffix in ['-platform-db','-odoo-db','-api','-odoo']]
    started=time.monotonic()
    with tempfile.TemporaryDirectory(prefix=RUN+'-') as directory:
        root=Path(directory)
        with tarfile.open(snapshot/'source.tar.gz','r:gz') as archive:
            expected_migrations=1+sum(member.isfile() and member.name.startswith('platform/migrations/') and member.name.endswith('.sql') for member in archive.getmembers())
            members=[member for member in archive.getmembers() if member.name.startswith('odoo/addons/')]
            archive.extractall(root,members=members,filter='data')
        addons=root/'odoo/addons'
        assert (addons/'chipmunk_bridge/__manifest__.py').is_file()
        for path in [addons,*addons.rglob('*')]:
            os.chmod(path,0o755 if path.is_dir() else 0o644)
        def private(name,text,mode=0o600):
            path=root/name
            path.write_text(text)
            os.chmod(path,mode)
            return str(path)
        pgenv=private('platform-db.env','POSTGRES_USER=chipmunk\nPOSTGRES_DB=chipmunk_platform\nPOSTGRES_PASSWORD='+env['PLATFORM_DB_PASSWORD']+'\n')
        ogen=private('odoo-db.env','POSTGRES_USER=odoo\nPOSTGRES_DB=smilelab_demo\nPOSTGRES_PASSWORD='+env['ODOO_DB_PASSWORD']+'\n')
        key=secrets.token_hex(32)
        apienv=private('api.env','\n'.join(['PLATFORM_DATABASE=Host='+pdb+';Database=chipmunk_platform;Username=chipmunk;Password='+env['PLATFORM_DB_PASSWORD']+';Maximum Pool Size=15','DEV_API_KEY='+env['CHIPMUNK_DEV_API_KEY'],'MEDIA_SIGNING_KEY='+env['CHIPMUNK_MEDIA_SIGNING_KEY'],'ODOO_INTEGRATION_KEY='+key,'ODOO_INTERNAL_URL=http://'+odoo+':8069','DEMO_AUTH=true','PUBLIC_URL=http://127.0.0.1','MEDIA_ROOT=/data/objects'])+'\n')
        odoenv=private('odoo.env','HOST='+odb+'\nUSER=odoo\nPASSWORD='+env['ODOO_DB_PASSWORD']+'\nODOO_INTEGRATION_KEY='+key+'\n')
        config=private('odoo.conf','[options]\ndb_host = '+odb+'\ndb_user = odoo\ndb_password = '+env['ODOO_DB_PASSWORD']+'\ndb_name = smilelab_demo\ndbfilter = ^smilelab_demo$\nlist_db = False\naddons_path = /usr/lib/python3/dist-packages/odoo/addons,/mnt/extra-addons\nworkers = 0\nmax_cron_threads = 0\n',0o644)
        try:
            command('docker','network','create','--label',LABEL,RUN)
            for name,pgfile in [(pdb,pgenv),(odb,ogen)]:
                start(name,POSTGRES,['--cpus','0.5','--memory','512m','--env-file',pgfile])
                for attempt in range(60):
                    if command('docker','exec',name,'pg_isready',check=False).returncode==0: break
                    time.sleep(0.5)
                else: raise RuntimeError('Restored database did not become ready')
            print('PASS isolated restore databases ready',flush=True)
            for name,user,database,dump in [(pdb,'chipmunk','chipmunk_platform','platform.dump'),(odb,'odoo','smilelab_demo','odoo.dump')]:
                with (snapshot/dump).open('rb') as file:
                    subprocess.run(['docker','exec','-i',name,'pg_restore','-U',user,'-d',database,'--no-owner','--exit-on-error'],stdin=file,check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
            assert int(sql(pdb,'chipmunk','chipmunk_platform','SELECT count(*) FROM platform_migration'))==expected_migrations
            print('PASS restored both databases and migration ledger',flush=True)
            for suffix,archive,owner in [('media','media.tar.gz','1654:1654'),('odoo-data','odoo-data.tar.gz','100:100')]:
                name=RUN+'-'+suffix
                command('docker','volume','create','--label',LABEL,name)
                volumes.append(name)
                command('docker','run','--rm','-u','0','-v',name+':/restore','-v',str(snapshot/archive)+':/archive:ro',ALPINE,'sh','-c','tar xzf /archive -C /restore && chown -R '+owner+' /restore')
            start(odoo,ODOO,['--cpus','2','--memory','2g','-p','127.0.0.1::8069','--env-file',odoenv,'-v',volumes[1]+':/var/lib/odoo','-v',str(addons)+':/mnt/extra-addons:ro','-v',config+':/etc/odoo/odoo.conf:ro'])
            obase=port(odoo)
            wait(obase+'/web/health')
            login=call(obase,'/web/session/authenticate',{'jsonrpc':'2.0','method':'call','params':{'db':'smilelab_demo','login':'admin@smilelab.ai','password':env['ODOO_ADMIN_PASSWORD']},'id':1})
            assert login.get('result',{}).get('uid')
            bridge=call(obase,'/chipmunk/integration/events',{'eventId':'evt_restore_'+RUN,'tenantId':'tenant_demo','eventType':'crm.lead.created','platformId':'lead_restore_'+RUN,'payload':{'name':'Synthetic isolated restore verification'}},headers={'Authorization':'Bearer '+key})
            assert bridge['code']==0
            start(api,image,['--cpus','1','--memory','512m','-p','127.0.0.1::8080','--env-file',apienv,'-v',volumes[0]+':/data/objects'])
            abase=port(api)
            wait(abase+'/health')
            tokens=[]
            for name,account in credentials['accounts'].items():
                result=call(abase,'/api/v1/auth/staff-login',{'identity':account['identity']},headers={'X-Staff-Dev-Key':account['key']})
                token=result['data']['token'];tokens.append(token)
                context=call(abase,'/api/dso/v1/context',token=token)['data']
                assert context['roles']==[account['role']]
            media=sql(pdb,'chipmunk','chipmunk_platform',"SELECT coalesce(json_agg(jsonb_build_object('id',r.id,'user',r.owner_id,'clinic',r.clinic_id,'sha256',r.body->>'sha256')),'[]'::json) FROM platform_resource r WHERE kind='media' AND body->>'uploaded'='true'")
            objects=json.loads(media)
            verified=0
            for obj in objects:
                token=secrets.token_hex(32)
                digest=hashlib.sha256(token.encode()).hexdigest()
                # 值来自校验过的快照；参数先编码为 JSON，再由 PostgreSQL 解析，避免拼接资源字符串。
                payload=json.dumps({'hash':digest,'user':obj['user'],'clinic':obj['clinic']}).replace("'","''")
                sql(pdb,'chipmunk','chipmunk_platform',"INSERT INTO platform_session(token_hash,user_id,clinic_id,expires_at) SELECT p->>'hash',p->>'user',p->>'clinic',now()+interval '5 minutes' FROM (SELECT '"+payload+"'::jsonb p) x")
                grant=call(abase,'/api/v1/media/'+urllib.parse.quote(obj['id'],safe='')+'/url',token=token)['data']
                url=urllib.parse.urlsplit(grant['url'])
                with urllib.request.urlopen(abase+url.path+'?'+url.query,timeout=20) as response: data=response.read()
                assert hashlib.sha256(data).hexdigest()==obj['sha256'];verified+=1
            with urllib.request.urlopen(abase+'/workspace/',timeout=20) as response:
                assert b'<div id="app">' in response.read()
            report={'snapshot':snapshot.name,'isolated':True,'imageArchiveLoaded':image_archive.exists(),'databasesRestored':2,'apiHealthy':True,'odooAdminLogin':True,'bridgeDelivery':True,'staffAccountsVerified':len(tokens),'mediaDownloadsHashVerified':verified,'workspaceLoaded':True,'elapsedSeconds':round(time.monotonic()-started,1),'limitations':['same-host recovery rehearsal; no replacement VPS or offsite backup']}
            Path(args.output).write_text(json.dumps(report,indent=2)+'\n')
            print(json.dumps(report))
        finally:
            for name in reversed(containers):
                if command('docker','inspect','--format','{{index .Config.Labels "smilelab.restore"}}',name,check=False).stdout.strip()==RUN:
                    command('docker','rm','-f',name,check=False)
            for name in volumes:
                if command('docker','volume','inspect','--format','{{index .Labels "smilelab.restore"}}',name,check=False).stdout.strip()==RUN:
                    command('docker','volume','rm',name,check=False)
            command('docker','network','rm',RUN,check=False)

if __name__=='__main__': main()
