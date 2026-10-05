"""共享订单的已实现契约；模拟付款不可作为实际交易使用。"""
import copy


def extend(paths, schemas):
    def f(kind='string', **kwargs): return {'type': kind, **kwargs}
    def ref(name): return {'$ref': '#/components/schemas/' + name}
    states=['requested','pending_payment','paid','sales_validated','manufacturing_ready','manufacturing','qa_pending','rework_required','qa_passed','shipped','delivered','rejected','cancelled']
    address=f('object',properties={name:f(maxLength=limit) for name,limit in [('recipient',100),('phone',32),('address',500)]},required=['recipient','phone','address'],additionalProperties=False)
    schemas['OrderAddress']=address
    schemas['OrderEvent']=f('object',properties={'version':f('integer'),'action':f(),'fromStatus':f(nullable=True),'toStatus':f(enum=states),'actorRole':f(),'details':f('object'),'createdAt':f(format='date-time')})
    schemas['OrderProduct']=f('object',properties={'code':f(),'name':f(),'priceMinor':f('integer'),'currency':f(enum=['CNY']),'demo':f('boolean',enum=[True])})
    schemas['OrderPayment']=f('object',nullable=True,properties={'receiptId':f(),'amountMinor':f('integer'),'currency':f(),'provider':f(enum=['demo']),'isSimulated':f('boolean',enum=[True]),'paidAt':f(format='date-time')})
    schemas['SharedOrder']=f('object',properties={**{name:f() for name in ['id','clinicId','patientId','doctorId','productCode','productName','batchRef','carrier','trackingNumber']},'quantity':f('integer'),'amountMinor':f('integer'),'currency':f(enum=['CNY']),'status':f(enum=states),'version':f('integer'),'nextRole':f(),'requestText':f(),'productionSpec':f(),'shippingAddress':ref('OrderAddress'),'timeline':f('array',items=ref('OrderEvent')),'payment':ref('OrderPayment'),'allowedActions':f('array',items=f()),'sourceOfTruth':f(enum=['platform']),'paymentMode':f(enum=['demo']),'createdAt':f(format='date-time'),'updatedAt':f(format='date-time')},required=['id','status','version','nextRole','amountMinor','sourceOfTruth','allowedActions'],description='当前状态与只追加事件账本在同一事务提交；敏感字段按角色省略，幂等重放返回当前快照。')
    roles=schemas['DsoContext']['properties']['roles']['items']['enum'];roles += ['sales','manufacturer','quality']
    def op(path,method,response='SharedOrder',body=None,required=(),idem=False,array=False):
        parameters=[{'name':'id','in':'path','required':True,'schema':f()}] if '{id}' in path else []
        if idem:parameters.append({'name':'Idempotency-Key','in':'header','required':True,'schema':f(minLength=1,maxLength=128)})
        data=f('array',items=ref(response)) if array else ref(response)
        result={'summary':'共享订单：'+path,'operationId':method+'_shared_'+path.strip('/').replace('/','_').replace('{','').replace('}',''),'tags':['共享订单'],'security':[{'bearerAuth':[]}],'parameters':parameters,'responses':{'200':{'description':'成功，返回当前订单视图','content':{'application/json':{'schema':{'allOf':[ref('Envelope'),f('object',properties={'data':data})]}}}}}}
        for code in [400,401,403,404,409,413,429,500,503]:result['responses'][str(code)]={'description':'参数、权限、阶段/版本冲突或服务未配置','content':{'application/json':{'schema':ref('Envelope')}}}
        if body is not None:result['requestBody']={'required':True,'content':{'application/json':{'schema':f('object',properties=body,required=list(required),additionalProperties=False)}}}
        paths.setdefault('/api/dso/v1'+path,{})[method]=result
    op('/orders/products','get','OrderProduct',array=True)
    op('/orders','get',array=True)
    paths['/api/dso/v1/orders']['get']['parameters'] += [{'name':name,'in':'query','schema':f('integer',minimum=1,maximum=maximum)} for name,maximum in [('page',10000),('pageSize',100)]]
    request={'doctorId':f(),'productCode':f(enum=['retainer_upper','retainer_lower','retainer_pair']),'quantity':f('integer',minimum=1,maximum=4),'requestText':f(minLength=1,maxLength=2000),'shippingAddress':address}
    op('/orders','post',body=request,required=request.keys(),idem=True)
    op('/orders/{id}','get')
    actions={
      'doctor_approve':{'productionSpec':f(minLength=1,maxLength=2000)},'doctor_reject':{'reason':f(minLength=1,maxLength=300)},
      'pay_demo':{'confirmSimulation':f('boolean',enum=[True])},'sales_validate':{},'manufacturer_validate':{},'start_manufacturing':{'batchRef':f(minLength=1,maxLength=100)},'finish_manufacturing':{},
      'qa_pass':{'checks':f('object',properties={name:f('boolean',enum=[True]) for name in ['identity','specification','finish','packaging']},required=['identity','specification','finish','packaging'],additionalProperties=False)},
      'qa_fail':{'checks':f('object',properties={name:f('boolean') for name in ['identity','specification','finish','packaging']},required=['identity','specification','finish','packaging'],additionalProperties=False),'reason':f(minLength=1,maxLength=300)},
      'ship':{'carrier':f(minLength=1,maxLength=100),'trackingNumber':f(minLength=1,maxLength=100)},'confirm_delivery':{},'cancel':{}
    }
    for action,fields in actions.items():
        body={'version':f('integer',minimum=1),**fields};op('/orders/{id}/actions/'+action,'post',body=body,required=body.keys(),idem=True)
    for path,methods in list(paths.items()):
        if path.startswith('/api/dso/v1/orders') and ('/actions/' not in path or path.rsplit('/',1)[-1] in ['pay_demo','confirm_delivery','cancel']):
            mini=path.replace('/api/dso/v1/','/api/v1/');paths[mini]=copy.deepcopy(methods)
            for operation in paths[mini].values():operation['operationId']='mini_'+operation['operationId']
