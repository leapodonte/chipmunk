"""授权开发演示的共享订单 HTTPS 检查；只用合成资料和模拟付款，不输出秘密。"""
import json
import os
from pathlib import Path
import time
import urllib.error
import urllib.request
import uuid

BASE=os.environ.get('SMOKE_BASE','https://app.smilelab.ai')
root=Path('/srv/dcad/secrets')
credentials=json.loads((root/'smilelab-staff-demo.json').read_text())
env=dict(line.split('=',1) for line in (root/'chipmunk.env').read_text().splitlines() if '=' in line)


def call(path,token=None,body=None,headers=None,expected=200):
    fields={'Content-Type':'application/json',**(headers or {})}
    if token:fields['Authorization']='Bearer '+token
    request=urllib.request.Request(BASE+path,None if body is None else json.dumps(body).encode(),fields,method='GET' if body is None else 'POST')
    try:response=urllib.request.urlopen(request,timeout=30)
    except urllib.error.HTTPError as error:response=error
    with response:
        assert response.status==expected, f'{path}: expected {expected}, got {response.status}'
        return json.load(response).get('data')


def main():
    tokens=[]
    try:
        staff={}
        for name in ['doctor','doctor2','manager','sales','manufacturer','quality']:
            account=credentials['accounts'][name]
            staff[name]=call('/api/v1/auth/staff-login',body={'identity':account['identity']},headers={'X-Staff-Dev-Key':account['key']})['token'];tokens.append(staff[name])
        patient=call('/api/v1/auth/mp-login',body={'code':'demo:workspace-patient'},headers={'X-Dev-Key':env['CHIPMUNK_DEV_API_KEY']})['token'];tokens.append(patient)
        run=uuid.uuid4().hex
        order=call('/api/v1/orders',patient,{'doctorId':'d_001','productCode':'retainer_pair','quantity':1,'requestText':'Synthetic shared-order verification '+run[:8],'shippingAddress':{'recipient':'Synthetic recipient','phone':'000000','address':'Synthetic development address'}},{'Idempotency-Key':'live-order-'+run})
        identifier=order['id']
        call('/api/dso/v1/orders/'+identifier,staff['doctor2'],expected=404)
        checks=[]
        qa=dict.fromkeys(['identity','specification','finish','packaging'],True)
        steps=[('doctor_approve',staff['doctor'],{'productionSpec':'Synthetic approved retainer specification'},'pending_payment'),('pay_demo',patient,{'confirmSimulation':True},'paid'),('sales_validate',staff['sales'],{},'sales_validated'),('manufacturer_validate',staff['manufacturer'],{},'manufacturing_ready'),('start_manufacturing',staff['manufacturer'],{'batchRef':'DEMO-'+run[:12]},'manufacturing'),('finish_manufacturing',staff['manufacturer'],{},'qa_pending'),('qa_pass',staff['quality'],{'checks':qa},'qa_passed'),('ship',staff['manufacturer'],{'carrier':'Demo carrier','trackingNumber':'DEMO-'+run[:12]},'shipped'),('confirm_delivery',patient,{},'delivered')]
        for action,actor,extra,status in steps:
            body={'version':order['version'],**extra};headers={'Idempotency-Key':'live-'+action+'-'+run}
            order=call('/api/dso/v1/orders/'+identifier+'/actions/'+action,actor,body,headers)
            assert order['status']==status and order['sourceOfTruth']=='platform'
            replay=call('/api/dso/v1/orders/'+identifier+'/actions/'+action,actor,body,headers)
            assert replay['version']==order['version']
            for viewer in [patient,staff['doctor'],staff['manager'],staff['sales'],staff['manufacturer'],staff['quality']]:
                view=call('/api/dso/v1/orders/'+identifier,viewer)
                assert (view['status'],view['version'])==(status,order['version'])
                assert len(view['timeline'])==order['version']
            checks.append(action);print('PASS shared order stage:',status,flush=True)
        assert order['version']==10 and order['payment']['isSimulated'] and order['payment']['amountMinor']==36000
        report={'orderId':identifier,'orderUrl':BASE+'/workspace/?order='+identifier,'patientDemoCode':'demo:workspace-patient','stagesVerified':len(checks),'participantViewsVerified':6,'timelineEvents':10,'finalStatus':order['status'],'paymentSimulated':True,'sourceOfTruth':'platform','base':BASE}
        Path('/tmp/smilelab-shared-order-smoke.json').write_text(json.dumps(report,indent=2)+'\n')
        print('PASS six participant views and one immutable order history')
    finally:
        for token in tokens:
            try:call('/api/v1/auth/logout',token,{})
            except Exception:pass


if __name__=='__main__':main()
