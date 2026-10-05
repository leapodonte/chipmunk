"""在Odoo容器中执行：检查服务认证、租户白名单与事件幂等。"""
import json
import os
import urllib.error
import urllib.request
import uuid

event = {'eventId':'evt_verify_' + uuid.uuid4().hex,'tenantId':'tenant_demo','eventType':'crm.lead.created','platformId':'consult_bridge_verify','payload':{'name':'Bridge idempotency test'}}
def send(body, token, expected):
    req=urllib.request.Request('http://127.0.0.1:8069/chipmunk/integration/events',data=json.dumps(body).encode(),headers={'Content-Type':'application/json','Authorization':'Bearer '+token},method='POST')
    try: response=urllib.request.urlopen(req,timeout=30)
    except urllib.error.HTTPError as e: response=e
    assert response.status==expected
    return json.load(response)
send(event,'invalid',401)
send(dict(event,tenantId='other-tenant'),os.environ['ODOO_INTEGRATION_KEY'],403)
a=send(event,os.environ['ODOO_INTEGRATION_KEY'],200)
b=send(event,os.environ['ODOO_INTEGRATION_KEY'],200)
assert a['data']['id']==b['data']['id'] and not a['data']['duplicate'] and b['data']['duplicate']
print('PASS Odoo service identity, tenant restriction and replay idempotency')
