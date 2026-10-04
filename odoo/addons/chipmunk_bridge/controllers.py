import hmac
import json
import os
from odoo import http
from odoo.http import request


class Bridge(http.Controller):
    @http.route('/chipmunk/integration/events', type='http', auth='public', methods=['POST'], csrf=False, save_session=False)
    def event(self, **kwargs):
        secret = os.environ.get('ODOO_INTEGRATION_KEY', '')
        auth = request.httprequest.headers.get('Authorization', '')
        if len(secret) < 32 or not hmac.compare_digest(auth, 'Bearer ' + secret):
            return request.make_json_response({'code': 401, 'message': 'Unauthorized', 'data': None}, status=401)
        try:
            body = request.httprequest.get_json(force=True)
            if not isinstance(body, dict) or any(not isinstance(body.get(k), str) or not body[k] or len(body[k]) > 128 for k in ['eventId', 'tenantId', 'eventType', 'platformId']):
                raise ValueError('Invalid event fields')
            if body['tenantId'] != 'tenant_demo' or body['eventType'] != 'crm.lead.created':
                return request.make_json_response({'code': 403, 'message': 'Tenant or event type not allowed', 'data': None}, status=403)
            # 序列化同一事件，重试不会重复创建商业线索。
            request.env.cr.execute('SELECT pg_advisory_xact_lock(hashtextextended(%s,0))', [body['eventId']])
            events = request.env['chipmunk.integration.event'].sudo()
            existing = events.search([('event_id', '=', body['eventId'])], limit=1)
            if existing:
                result = {'model': existing.odoo_model, 'id': existing.odoo_id, 'duplicate': True}
            else:
                # 固定演示租户到指定公司，客户端不能选择任意公司。
                company = request.env.ref('base.main_company').sudo()
                payload = body.get('payload') or {}
                name = payload.get('name', '花栗鼠演示咨询')
                if not isinstance(name, str) or len(name) > 256:
                    raise ValueError('Invalid commercial name')
                lead = request.env['crm.lead'].sudo().with_company(company).create({
                    'name': name, 'type': 'lead', 'company_id': company.id,
                    'chipmunk_platform_ref': body['platformId'],
                    'description': '仅商业引用；患者、疗程和医疗媒体保存在花栗鼠平台。',
                })
                events.create({'event_id': body['eventId'], 'tenant_id': body['tenantId'], 'platform_id': body['platformId'], 'odoo_model': 'crm.lead', 'odoo_id': lead.id, 'company_id': company.id})
                result = {'model': 'crm.lead', 'id': lead.id, 'duplicate': False}
            return request.make_json_response({'code': 0, 'message': 'ok', 'data': result})
        except (ValueError, TypeError, json.JSONDecodeError):
            return request.make_json_response({'code': 400, 'message': 'Invalid event', 'data': None}, status=400)
