"""从已实现路由构建前端联调契约；不包含真实密钥。"""
import json
from pathlib import Path

paths = {}
def add(method, path, summary, fields=None, public=False):
    operation = {'summary': summary, 'operationId': method + '_' + path.strip('/').replace('/', '_').replace('{', '').replace('}', ''),
                 'security': [] if public else [{'bearerAuth': []}],
                 'responses': {'200': {'description': '成功；返回 {code:0,message:"ok",data:...}', 'content': {'application/json': {'schema': {'$ref': '#/components/schemas/Envelope'}}}}}}
    if '{' in path:
        operation['parameters'] = [{'name': s[1:-1], 'in': 'path', 'required': True, 'schema': {'type': 'string'}} for s in path.split('/') if s.startswith('{')]
    if method == 'post':
        properties = {name: {'type': kind} for name, kind, _ in (fields or [])}
        required = [name for name, _, required in (fields or []) if required]
        operation['requestBody'] = {'required': bool(required), 'content': {'application/json': {'schema': {'type': 'object', 'properties': properties, 'required': required}}}}
    for code, message in [('400','参数无效'),('401','未授权'),('403','签名无效'),('404','资源不存在'),('409','状态冲突'),('429','频控'),('500','服务器错误'),('503','上游未配置')]:
        operation['responses'][code] = {'description': message, 'content': {'application/json': {'schema': {'$ref': '#/components/schemas/Envelope'}}}}
    paths.setdefault('/api/v1' + path, {})[method] = operation

add('post','/auth/mp-login','微信登录或 demo:固定测试身份登录',[('code','string',True),('inviteCode','string',False)],True)
add('post','/auth/phone-login','开发手机号登录，需 X-Dev-Key，smsCode=123456',[('phone','string',True),('smsCode','string',True)],True)
add('post','/auth/sms-code','演示验证码；不发送真实短信',[('phone','string',True)],True)
add('post','/auth/logout','撤销当前 token')
add('get','/users/me','当前患者资料和租户上下文')
add('post','/users/me/questionnaire','保存问卷并返回推荐医生',[('answers','object',True)])
for path,summary in [('/questionnaire/template','问卷模板来源'),('/doctors/recommend','演示医生列表'),('/doctors/{id}','医生详情'),('/treatments/current','当前疗程或null'),('/treatments/mine','疗程历史'),('/treatments/{id}','本人疗程详情'),('/checkins/today','今日佩戴分钟与演示评分'),('/checkins/records','打卡历史'),('/posts','分页内容列表'),('/posts/search','内容标题搜索'),('/posts/{id}','内容详情'),('/media/{id}/url','刷新本人媒体15分钟下载URL'),('/ai/simulations/mine','本人模拟历史'),('/ai/simulations/{taskId}','异步演示任务状态'),('/messages','本人消息列表'),('/appointments/mine','预约占位空列表'),('/patients/me/records','档案占位空列表'),('/reports/mine','报告占位空列表'),('/invitations/mine','邀请功能关闭状态'),('/coupons/mine','优惠券占位空列表'),('/mall/products','商城占位空列表')]: add('get',path,summary)
for path,summary in [('/doctors/{id}/bind','绑定医生并创建咨询疗程和商业事件'),('/posts/{id}/like','幂等点赞'),('/messages/{id}/read','消息已读'),('/messages/read-all','全部已读'),('/consultations','创建咨询和Odoo商业线索'),('/memberships/open','未实现，返回501，不模拟扣款')]: add('post',path,summary)
add('post','/checkins','交替戴套/取套，建议 Idempotency-Key',[('type','string',False),('imageKey','string',False)])
add('post','/media/upload-token','磁盘OSS模拟上传凭证，有效300秒',[('scene','string',True),('ext','string',False)])
add('post','/ai/simulations','创建模拟任务：不执行真实AI推理',[('imageKey','string',True)])
for path in ['/auth/mp-login','/auth/phone-login','/auth/sms-code']:
    paths['/api/v1'+path]['post'].setdefault('parameters',[]).append({'name':'X-Dev-Key','in':'header','required':False,'description':'开发登录必需；真实wx.login code不需要','schema':{'type':'string'}})
for path in ['/posts','/posts/search','/checkins/records','/messages','/ai/simulations/mine']:
    op=paths['/api/v1'+path]['get']; op['parameters']=op.get('parameters',[])+[{'name':'page','in':'query','schema':{'type':'integer','minimum':1,'default':1}},{'name':'pageSize','in':'query','schema':{'type':'integer','minimum':1,'maximum':100,'default':10}}]
paths['/api/v1/posts']['get']['parameters'].append({'name':'topic','in':'query','schema':{'type':'string','enum':['all','doctor','patient','science','case','diary','mutual']}})
paths['/api/v1/posts/search']['get']['parameters'].append({'name':'keyword','in':'query','schema':{'type':'string'}})
paths['/api/v1/checkins']['post']['parameters']=[{'name':'Idempotency-Key','in':'header','schema':{'type':'string','maxLength':128}}]
paths['/api/v1/memberships/open']['post']['responses']={'501':{'description':'尚未接入支付'}}
schemas = {}
def shape(name, fields):
    schemas[name]={'type':'object','properties':{k:{'type':v} if isinstance(v,str) else v for k,v in fields.items()}}
shape('LoginResult',{'token':'string','userId':'string','isNewUser':'boolean','expiresIn':'integer','authMode':'string'})
shape('UserProfile',{'id':'string','nickname':'string','phone':'string','avatar':'string','isMember':'boolean','hasBoundDoctor':'boolean','tenantId':'string','organizationId':'string','clinicId':'string','roles':{'type':'array','items':{'type':'string'}}})
shape('Doctor',{'id':'string','name':'string','title':'string','years':'integer','hospital':'string','avatar':'string','verified':'boolean','honorBadges':{'type':'array','items':{'type':'string'}},'stats':{'type':'object','properties':{'fans':{'type':'integer'},'rating':{'type':'number'},'served':{'type':'integer'}}},'specialtyTags':{'type':'array','items':{'type':'string'}},'specialtyText':'string','highlight':'string','isDemo':'boolean'})
shape('Treatment',{'id':'string','status':'string','doctor':{'$ref':'#/components/schemas/Doctor'},'members':{'type':'array','items':{'type':'object'}},'currentStep':'integer','totalSteps':'integer','nextChangeDate':'string','stepDays':'integer','stepDayIndex':'integer','checkinScore':'integer','scoreLevel':'string','startDate':'string','endDate':'string','createdAt':'string','isDemo':'boolean'})
shape('CheckinStatus',{'todayWearMinutes':'integer','targetMinutes':'integer','continuousDays':'integer','checkedToday':'boolean','periodScore':'integer','scoreChangePercent':'integer','periodStart':'string','periodEnd':'string','scoreMode':'string'})
shape('CheckinRecord',{'id':'string','type':'string','imageKey':'string','time':'string','date':'string','epoch':'integer'})
shape('Post',{'id':'string','title':'string','cover':'string','author':{'type':'object','properties':{'name':{'type':'string'},'avatar':{'type':'string'}}},'likes':'integer','topic':'string','content':'string','isDemo':'boolean'})
shape('Message',{'id':'string','title':'string','content':'string','time':'string','read':'boolean','type':'string'})
shape('UploadGrant',{'uploadUrl':'string','objectKey':'string','accessKeyId':'string','policy':'string','signature':'string','expiresIn':'integer','maxSize':'integer','method':'string','storageProvider':'string','formData':{'type':'object','additionalProperties':{'type':'string'}}})
shape('UploadResult',{'objectKey':'string','mediaId':'string','size':'integer','sha256':'string','url':'string'})
shape('Simulation',{'taskId':'string','status':'string','progress':'integer','qualityChecks':{'type':'array','items':{'type':'object'}},'resultText':'string','beforeImage':'string','afterImage':'string','isMock':'boolean'})
shape('Ok',{'ok':'boolean'})
response_types={'/auth/mp-login':'LoginResult','/auth/phone-login':'LoginResult','/users/me':'UserProfile','/doctors/recommend':'Doctor[]','/doctors/{id}':'Doctor','/doctors/{id}/bind':'Treatment','/treatments/current':'Treatment','/treatments/mine':'Treatment[]','/treatments/{id}':'Treatment','/checkins/today':'CheckinStatus','/checkins':'CheckinStatus','/checkins/records':'CheckinRecord[]','/posts':'Post[]','/posts/search':'Post[]','/posts/{id}':'Post','/messages':'Message[]','/media/upload-token':'UploadGrant','/ai/simulations/{taskId}':'Simulation','/ai/simulations/mine':'Simulation[]'}
for path,methods in paths.items():
    result_type=response_types.get(path.removeprefix('/api/v1'))
    for method,operation in methods.items():
        if result_type and '200' in operation['responses']:
            data_schema={'type':'array','items':{'$ref':'#/components/schemas/'+result_type[:-2]}} if result_type.endswith('[]') else {'$ref':'#/components/schemas/'+result_type}
            if path.endswith('/treatments/current'): data_schema={'nullable':True,'allOf':[data_schema]}
            operation['responses']['200']['content']['application/json']['schema']={'allOf':[{'$ref':'#/components/schemas/Envelope'},{'type':'object','properties':{'data':data_schema}}]}
storage_params=[{'name':'id','in':'path','required':True,'schema':{'type':'string'}},{'name':'expires','in':'query','required':True,'schema':{'type':'integer'}},{'name':'signature','in':'query','required':True,'schema':{'type':'string'}}]
paths['/storage/objects/{id}']={
    'post':{'summary':'磁盘模拟OSS表单上传，使用签名URL，不是Bearer','security':[],'parameters':storage_params,'requestBody':{'required':True,'content':{'multipart/form-data':{'schema':{'type':'object','required':['file','key'],'properties':{'file':{'type':'string','format':'binary'},'key':{'type':'string'}}}}}},'responses':{'200':{'description':'文件保存成功','content':{'application/json':{'schema':{'$ref':'#/components/schemas/Envelope'}}}},'403':{'description':'签名过期'},'409':{'description':'不可覆盖'},'413':{'description':'大小或容量超限'}}},
    'put':{'summary':'签名URL原始二进制上传','security':[],'parameters':storage_params,'requestBody':{'required':True,'content':{'application/octet-stream':{'schema':{'type':'string','format':'binary'}}}},'responses':{'200':{'description':'文件保存成功'}}},
    'get':{'summary':'有效签名URL下载私有图片，支持Range','security':[],'parameters':storage_params,'responses':{'200':{'description':'原始图片','content':{'image/png':{'schema':{'type':'string','format':'binary'}},'image/jpeg':{'schema':{'type':'string','format':'binary'}},'image/webp':{'schema':{'type':'string','format':'binary'}}}},'403':{'description':'无效签名'}}}}
schemas['Envelope']={'type':'object','required':['code','message','data'],'properties':{'code':{'type':'integer'},'message':{'type':'string'},'data':{'nullable':True,'description':'由各接口定义返回类型'}}}
document={'openapi':'3.0.3','info':{'title':'Smilelab 花栗鼠小程序开发API','version':'0.1.0','description':'开发演示环境。AI返回原图占位，SMS不发送；医疗数据不进入Odoo。'},'servers':[{'url':'https://app.smilelab.ai'}],'paths':paths,'components':{'securitySchemes':{'bearerAuth':{'type':'http','scheme':'bearer'}},'schemas':schemas}}
Path(__file__).resolve().parents[1].joinpath('platform/openapi.json').write_text(json.dumps(document,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
Path(__file__).resolve().parents[1].joinpath('platform/DeveloperGuide.md').write_text(Path(__file__).resolve().parents[1].joinpath('doc/Smilelab小程序API开发文档.md').read_text(encoding='utf-8'),encoding='utf-8')
