#!/usr/bin/env python3
"""在 VPS 上生成开发环境密钥，绝不输出密钥内容。"""
import os
from pathlib import Path
import secrets

root = Path('/srv/dcad/secrets')
root.mkdir(mode=0o700, parents=True, exist_ok=True)
env_path = root / 'chipmunk.env'
if not env_path.exists():
    keys = ['PLATFORM_DB_PASSWORD', 'ODOO_DB_PASSWORD', 'ODOO_INTEGRATION_KEY', 'CHIPMUNK_DEV_API_KEY', 'CHIPMUNK_MEDIA_SIGNING_KEY', 'ODOO_ADMIN_PASSWORD', 'ODOO_MASTER_PASSWORD']
    descriptor = os.open(env_path, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
    with os.fdopen(descriptor, 'w') as output:
        output.write(''.join(f'{key}={secrets.token_hex(32)}\n' for key in keys))
        output.write('CHIPMUNK_WECHAT_APP_ID=\nCHIPMUNK_WECHAT_APP_SECRET=\n')
env = dict(line.split('=', 1) for line in env_path.read_text().splitlines() if '=' in line)
config = root / 'odoo.conf'
if not config.exists():
    descriptor = os.open(config, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
    with os.fdopen(descriptor, 'w') as output:
        output.write(f'''[options]
admin_passwd = {env['ODOO_MASTER_PASSWORD']}
db_host = odoo-db
db_port = 5432
db_user = odoo
db_password = {env['ODOO_DB_PASSWORD']}
db_name = smilelab_demo
dbfilter = ^smilelab_demo$
list_db = False
proxy_mode = True
addons_path = /usr/lib/python3/dist-packages/odoo/addons,/mnt/extra-addons
data_dir = /var/lib/odoo
workers = 2
max_cron_threads = 1
db_maxconn = 10
limit_memory_soft = 536870912
limit_memory_hard = 805306368
limit_time_cpu = 120
limit_time_real = 240
log_level = info
''')
print('Secrets prepared; values were not printed.')
