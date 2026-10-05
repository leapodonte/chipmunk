"""HTTPS 端到端测试，只使用人工生成的测试数据，不输出密钥或token。"""
import base64
import json
import os
from pathlib import Path
import subprocess
import time
import urllib.error
import urllib.request
import uuid

BASE = os.environ.get('SMOKE_BASE', 'https://app.smilelab.ai')
secrets = dict(line.split('=', 1) for line in Path('/srv/dcad/secrets/chipmunk.env').read_text().splitlines() if '=' in line)
checks = []
def call(method, path, body=None, token=None, headers=None, expected=200):
    h = {'Content-Type': 'application/json', **(headers or {})}
    if token:
        h['Authorization'] = 'Bearer ' + token
    data = None if body is None else json.dumps(body, ensure_ascii=False, separators=(',', ':')).encode()
    request = urllib.request.Request(BASE + path if path.startswith('/') else path, data=data, headers=h, method=method)
    try:
        response = urllib.request.urlopen(request, timeout=30)
    except urllib.error.HTTPError as error:
        response = error
    raw = response.read()
    assert response.status == expected, f'{method} {path.split("?")[0]}: expected {expected}, got {response.status}'
    result = json.loads(raw) if raw else None
    if expected == 200 and isinstance(result, dict) and 'code' in result:
        assert result['code'] == 0, f'Business failure at {path}'
        return result['data']
    return result
def ok(name):
    checks.append(name)
    print('PASS', name)

call('GET', '/health'); ok('HTTPS health')
spec = call('GET', '/api/openapi.json'); assert spec['openapi'] == '3.0.3'; ok('OpenAPI published')
call('GET','/api/v1/users/me',expected=401)
call('POST','/api/v1/auth/mp-login',{'code':'demo:attacker'},expected=401)
call('POST','/api/v1/auth/mp-login',{'code':'not-a-real-wechat-code'},expected=503); ok('authentication failures')
identity = 'demo:smoke-' + uuid.uuid4().hex
a = call('POST','/api/v1/auth/mp-login',{'code':identity},headers={'X-Dev-Key':secrets['CHIPMUNK_DEV_API_KEY']})
b = call('POST','/api/v1/auth/mp-login',{'code':identity+'-other'},headers={'X-Dev-Key':secrets['CHIPMUNK_DEV_API_KEY']})
repeat = call('POST','/api/v1/auth/mp-login',{'code':identity},headers={'X-Dev-Key':secrets['CHIPMUNK_DEV_API_KEY']})
assert repeat['userId'] == a['userId'] and not repeat['isNewUser']; ok('persistent identity')
token=a['token']; other=b['token']
profile=call('GET','/api/v1/users/me',token=token,headers={'X-Tenant-Id':'evil'})
assert profile['id']==a['userId'] and profile['tenantId']=='tenant_demo'; ok('trusted tenant context')
assert call('GET','/api/v1/treatments/current',token=token) is None
call('POST','/api/v1/users/me/questionnaire',{'answers':{'q1':'test'}},token)
assert len(call('GET','/api/v1/doctors/recommend',token=token))==2
t=call('POST','/api/v1/doctors/d_001/bind',{},token)
again=call('POST','/api/v1/doctors/d_001/bind',{},token); assert again['id']==t['id']
call('POST','/api/v1/doctors/d_002/bind',{},token,expected=409)
call('GET','/api/v1/treatments/'+t['id'],token=other,expected=404); ok('questionnaire and treatment ownership')
key=uuid.uuid4().hex
status=call('POST','/api/v1/checkins',{'type':'wear'},token,{'Idempotency-Key':key})
assert status['checkedToday']
assert call('POST','/api/v1/checkins',{'type':'wear'},token,{'Idempotency-Key':key})==status
call('POST','/api/v1/checkins',{'type':'remove'},token,{'Idempotency-Key':key},expected=409)
call('POST','/api/v1/checkins',{'type':'wear'},token,expected=409)
call('POST','/api/v1/checkins',{'type':'remove'},token)
assert len(call('GET','/api/v1/checkins/records',token=token))==2; ok('wear transitions and idempotency')
posts=call('GET','/api/v1/posts?topic=science&pageSize=1',token=token); assert len(posts)==1
post=posts[0]; before=post['likes']
call('POST','/api/v1/posts/'+post['id']+'/like',{},token); call('POST','/api/v1/posts/'+post['id']+'/like',{},token)
assert call('GET','/api/v1/posts/'+post['id'],token=token)['likes']==before+1
assert len(call('GET','/api/v1/posts/search?keyword=science',token=token))==1; ok('content pagination and idempotent likes')
call('POST','/api/v1/media/upload-token',{'scene':'ai_photo','ext':'../../php'},token,expected=400)
grant=call('POST','/api/v1/media/upload-token',{'scene':'ai_photo','ext':'png'},token)
call('POST','/api/v1/ai/simulations',{'imageKey':grant['objectKey']},token,expected=409)
image=base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a5e0AAAAASUVORK5CYII=')
boundary='smoke-'+uuid.uuid4().hex
multipart=(f'--{boundary}\r\nContent-Disposition: form-data; name="key"\r\n\r\n{grant["objectKey"]}\r\n--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="test.png"\r\nContent-Type: image/png\r\n\r\n').encode()+image+f'\r\n--{boundary}--\r\n'.encode()
req=urllib.request.Request(grant['uploadUrl'],data=multipart,headers={'Content-Type':'multipart/form-data; boundary='+boundary},method='POST')
with urllib.request.urlopen(req,timeout=30) as response: uploaded=json.load(response)['data']
assert uploaded['size']==len(image)
with urllib.request.urlopen(uploaded['url'],timeout=30) as response: assert response.read()==image
raw_grant=call('POST','/api/v1/media/upload-token',{'scene':'avatar','ext':'png'},token)
bad=urllib.request.Request(raw_grant['uploadUrl'],data=b'not-an-image',method='PUT')
try: urllib.request.urlopen(bad,timeout=30); raise AssertionError('invalid image accepted')
except urllib.error.HTTPError as e: assert e.code==400
raw=urllib.request.Request(raw_grant['uploadUrl'],data=image,headers={'Content-Type':'image/png'},method='PUT')
with urllib.request.urlopen(raw,timeout=30) as response: raw_result=json.load(response)['data']
assert raw_result['size']==len(image)
expired=raw_result['url'].split('?')[0]+'?expires=1&signature=expired'
call('GET',expired,expected=403)
call('GET','/api/v1/media/'+uploaded['mediaId']+'/url',token=other,expected=404)
call('POST','/api/v1/ai/simulations',{'imageKey':grant['objectKey']},other,expected=404)
call('GET',uploaded['url'].replace('signature=','signature=bad'),expected=403)
req=urllib.request.Request(grant['uploadUrl'],data=image,method='PUT')
try: urllib.request.urlopen(req,timeout=30); raise AssertionError('overwrite allowed')
except urllib.error.HTTPError as e: assert e.code==409
ok('disk upload, byte equality, signatures and private ownership')
chunked=urllib.request.Request(BASE+'/api/v1/users/me/questionnaire',data=iter([b'{"answers":{"q":"',b'x'*65536,b'"}}']),headers={'Content-Type':'application/json','Authorization':'Bearer '+token},method='POST')
try: urllib.request.urlopen(chunked,timeout=30); raise AssertionError('oversized chunked JSON accepted')
except urllib.error.HTTPError as e: assert e.code==413
ok('chunked JSON request bound')
task=call('POST','/api/v1/ai/simulations',{'imageKey':grant['objectKey']},token)
result=call('GET','/api/v1/ai/simulations/'+task['taskId'],token=token); assert result['isMock']
time.sleep(3)
result=call('GET','/api/v1/ai/simulations/'+task['taskId'],token=token); assert result['status']=='done' and result['isMock'] and '未进行AI' in result['resultText']
call('GET','/api/v1/ai/simulations/'+task['taskId'],token=other,expected=404); ok('explicit mock AI and task ownership')
messages=call('GET','/api/v1/messages',token=token); assert messages
call('POST','/api/v1/messages/'+messages[0]['id']+'/read',{},other,expected=404)
call('POST','/api/v1/messages/'+messages[0]['id']+'/read',{},token)
call('POST','/api/v1/messages/read-all',{},token)
assert all(x['read'] for x in call('GET','/api/v1/messages',token=token)); ok('private messages')
call('POST','/api/v1/memberships/open',{},token,expected=501)
for path in ['appointments/mine','patients/me/records','reports/mine','coupons/mine','mall/products']: assert call('GET','/api/v1/'+path,token=token)==[]
ok('reserved feature behavior')
consultation=call('POST','/api/v1/consultations',{},token)
for attempt in range(15):
    sql="SELECT count(*) FROM integration_mapping WHERE platform_id='"+consultation['id']+"'"
    count=subprocess.check_output(['docker','exec','chipmunk-chipmunk-db-1','psql','-U','chipmunk','-d','chipmunk_platform','-Atc',sql],text=True).strip()
    if count=='1': break
    time.sleep(2)
assert count=='1','Odoo outbox delivery missing'
ok('transactional outbox to actual Odoo CRM')
for path in ['https://odoo.smilelab.ai/chipmunk/integration/events','https://odoo.smilelab.ai/web/database/manager']:
    try: urllib.request.urlopen(path,timeout=30); raise AssertionError('internal route exposed')
    except urllib.error.HTTPError as e: assert e.code==404
req=urllib.request.Request('https://odoo.smilelab.ai/web/session/authenticate',data=json.dumps({'jsonrpc':'2.0','method':'call','params':{'db':'smilelab_demo','login':'admin@smilelab.ai','password':secrets['ODOO_ADMIN_PASSWORD']},'id':1}).encode(),headers={'Content-Type':'application/json'},method='POST')
with urllib.request.urlopen(req,timeout=30) as response: login=json.load(response)
assert login.get('result',{}).get('uid'), 'Odoo login failed'
ok('Odoo administrator login and blocked internal routes')
call('POST','/api/v1/auth/logout',{},token)
call('GET','/api/v1/users/me',token=token,expected=401); ok('token revocation')
report={'passed':len(checks),'checks':checks,'base':BASE,'date':'2026-10-05','limitations':['real WeChat/SMS/AI not configured','load and production clinical workflows not tested']}
Path('/tmp/smilelab-smoke-result.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
print('All',len(checks),'checks passed')
