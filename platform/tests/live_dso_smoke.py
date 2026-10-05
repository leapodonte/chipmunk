"""授权的 HTTPS 开发演示检查，写入明确标记的合成患者数据，不输出秘密。"""
import datetime as dt
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
checks=[]

def call(path,token=None,method='GET',body=None,headers=None,expected=200):
    fields={'Content-Type':'application/json','Accept-Language':'en',**(headers or {})}
    if token: fields['Authorization']='Bearer '+token
    request=urllib.request.Request(BASE+path,data=None if body is None else json.dumps(body).encode(),headers=fields,method=method)
    try: response=urllib.request.urlopen(request,timeout=30)
    except urllib.error.HTTPError as error: response=error
    with response:
        assert response.status==expected,f'{method} {path}: expected {expected}, got {response.status}'
        result=json.load(response)
        return result.get('data')

def ok(name):
    checks.append(name)
    print('PASS',name)

staff={}
for name,account in credentials['accounts'].items():
    staff[name]=call('/api/v1/auth/staff-login',method='POST',body={'identity':account['identity']},headers={'X-Staff-Dev-Key':account['key']})['token']
    context=call('/api/dso/v1/context',staff[name])
    assert context['roles']==[account['role']] and context['clinicId']=='clinic_demo'
ok('five separate staff accounts and trusted roles')
call('/api/v1/auth/staff-login',method='POST',body={'identity':'staff:manager'},headers={'X-Staff-Dev-Key':credentials['accounts']['doctor']['key']},expected=401)
ok('staff keys cannot authenticate another identity')
run=uuid.uuid4().hex
patient_token=call('/api/v1/auth/mp-login',method='POST',body={'code':'demo:dso-live-'+run},headers={'X-Dev-Key':env['CHIPMUNK_DEV_API_KEY']})['token']
patient=call('/api/dso/v1/patients/me',patient_token)
patient=call('/api/dso/v1/patients/me',patient_token,'PUT',{'displayName':'Synthetic DSO verification '+run[:8],'version':patient['version'],'profile':{'gender':'unknown'}})
call('/api/dso/v1/patients/'+patient['id'],staff['doctor'],expected=404)
call('/api/dso/v1/patients/me/care-team/d_001',patient_token,'POST',{})
call('/api/dso/v1/patients/'+patient['id'],staff['doctor'])
call('/api/dso/v1/patients/'+patient['id'],staff['doctor2'],expected=404)
ok('explicit patient care-team authorization and doctor isolation')
start=dt.datetime.now(dt.timezone.utc)+dt.timedelta(days=30,seconds=int(run[:4],16))
slot=call('/api/dso/v1/slots',staff['manager'],'POST',{'doctorId':'d_001','startsAt':start.isoformat(),'endsAt':(start+dt.timedelta(minutes=15)).isoformat()},{'Idempotency-Key':'live-slot-'+run})
appointment=call('/api/v1/appointments',patient_token,'POST',{'slotId':slot['id'],'shareWithDoctor':True},{'Idempotency-Key':'live-book-'+run})
assert call('/api/v1/appointments',patient_token,'POST',{'slotId':slot['id'],'shareWithDoctor':True},{'Idempotency-Key':'live-book-'+run})['id']==appointment['id']
call('/api/dso/v1/appointments/'+appointment['id']+'/status',patient_token,'POST',{'version':appointment['version'],'status':'cancelled'})
call('/api/dso/v1/slots/'+slot['id']+'/close',staff['manager'],'POST',{})
ok('real mini-program booking, replay and cancellation')
record=call('/api/dso/v1/patients/'+patient['id']+'/records',staff['doctor'],'POST',{'content':{'chiefComplaint':'Synthetic smoke verification only','toothChart':[{'tooth':11,'finding':'Synthetic'}]}},{'Idempotency-Key':'live-record-'+run})
call('/api/dso/v1/clinical-records/'+record['id'],patient_token,expected=404)
signed=call('/api/dso/v1/clinical-records/'+record['id']+'/sign',staff['doctor'],'POST',{'version':record['version']})
assert any(row['id']==record['id'] for row in call('/api/v1/patients/me/records',patient_token))
call('/api/dso/v1/clinical-records/'+record['id'],staff['doctor'],'PATCH',{'version':signed['version'],'content':{'plan':'Forbidden overwrite'}},expected=409)
ok('draft privacy, signed patient record and immutable content')
call('/api/dso/v1/patients/me/care-team/d_001',patient_token,'DELETE')
call('/api/dso/v1/clinical-records/'+record['id'],staff['doctor'],expected=404)
ok('clinical consent revocation')
lead=call('/api/dso/v1/crm/leads',staff['consultant'],'POST',{'title':'Synthetic live CRM '+run[:8],'patientId':patient['id']},{'Idempotency-Key':'live-lead-'+run})
call('/api/dso/v1/crm/leads',patient_token,expected=403)
call('/api/dso/v1/operations/outbox',staff['doctor'],expected=403)
for attempt in range(30):
    queue=call('/api/dso/v1/operations/outbox?status=delivered&pageSize=100',staff['operator'])
    if any(row['aggregateId']==lead['id'] for row in queue): break
    time.sleep(1)
else: raise AssertionError('Commercial event not delivered to Odoo')
assert call('/api/dso/v1/operations/audit',staff['operator'])
ok('CRM delivery to actual Odoo and operations role boundary')
for token in [patient_token,*staff.values()]:
    call('/api/v1/auth/logout',token,'POST',{})
    call('/api/dso/v1/context',token,expected=401)
ok('all smoke sessions revoked')
Path('/tmp/smilelab-live-dso-smoke.json').write_text(json.dumps({'passed':len(checks),'checks':checks,'base':BASE,'date':'2026-10-05','syntheticPatientId':patient['id'],'syntheticLeadId':lead['id']},indent=2)+'\n')
print('All',len(checks),'live DSO checks passed')
