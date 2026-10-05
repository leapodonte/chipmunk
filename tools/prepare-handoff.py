"""导出分开的管理员/开发者凭据，并安装每日备份计划；不打印秘密。"""
import json
import os
from pathlib import Path
import subprocess

root=Path('/srv/dcad/secrets')
env=dict(line.split('=',1) for line in (root/'chipmunk.env').read_text().splitlines() if '=' in line)
files={
    'smilelab-developer-credentials.json':{'apiBaseUrl':'https://app.smilelab.ai/api/v1','documentation':'https://app.smilelab.ai/api/docs','developmentHeader':'X-Dev-Key','developmentKey':env['CHIPMUNK_DEV_API_KEY'],'loginExample':{'code':'demo:frontend-alice'},'warning':'开发环境密钥，不得打包进正式发布的小程序'},
    'smilelab-odoo-admin-credentials.json':{'url':'https://odoo.smilelab.ai','database':'smilelab_demo','login':'admin@smilelab.ai','password':env['ODOO_ADMIN_PASSWORD']},
}
for name,data in files.items():
    fd=os.open(root/name,os.O_WRONLY|os.O_CREAT|os.O_TRUNC,0o600)
    with os.fdopen(fd,'w') as output: json.dump(data,output,ensure_ascii=False,indent=2)
result=subprocess.run(['crontab','-l'],text=True,capture_output=True)
lines=[line for line in result.stdout.splitlines() if '# smilelab-dev-backup' not in line]
lines.append('15 3 * * * /srv/dcad/infra/scripts/backup-smilelab.sh > /srv/dcad/backups/smilelab/last-backup.log 2>&1 # smilelab-dev-backup')
subprocess.run(['crontab','-'],input='\n'.join(lines)+'\n',text=True,check=True)
print('Credential files prepared separately; daily backup scheduled at 03:15 Europe/Paris.')
