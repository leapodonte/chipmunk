import hashlib
import hmac
import json
import os
from odoo import http
from odoo.http import request
from odoo.exceptions import ConcurrencyError
from psycopg2.errors import UniqueViolation
from werkzeug.exceptions import RequestEntityTooLarge


class Bridge(http.Controller):
    @staticmethod
    def error(code, message):
        return request.make_json_response({'code': code, 'message': message, 'data': None}, status=code)

    @http.route('/chipmunk/integration/events', type='http', auth='public', methods=['POST'], csrf=False, save_session=False)
    def event(self, **kwargs):
        secret = os.environ.get('ODOO_INTEGRATION_KEY', '')
        auth = request.httprequest.headers.get('Authorization', '')
        if len(secret) < 32 or not hmac.compare_digest(auth.encode(), ('Bearer ' + secret).encode()):
            return self.error(401, 'Unauthorized')
        try:
            if request.httprequest.content_length and request.httprequest.content_length > 65536:
                return self.error(413, 'Event exceeds 64KiB')
            request.httprequest.max_content_length = 65536
            raw = request.httprequest.get_data()
            if len(raw) > 65536:
                return self.error(413, 'Event exceeds 64KiB')
            body = json.loads(raw)
            fields = ['eventId', 'tenantId', 'eventType', 'platformId']
            if not isinstance(body, dict) or set(body) - set(fields + ['payload']) or any(not isinstance(body.get(k), str) or not body[k].strip() or len(body[k]) > 128 for k in fields):
                raise ValueError('Invalid event fields')
            if body['eventType'] != 'crm.lead.created':
                return self.error(403, 'Event type not allowed')
            mapping = request.env['chipmunk.tenant.company'].sudo().search([('tenant_id', '=', body['tenantId']), ('active', '=', True)], limit=1)
            if not mapping:
                return self.error(403, 'Tenant company mapping not configured')
            company = mapping.company_id
            payload = body.get('payload', {})
            if not isinstance(payload, dict) or set(payload) - {'name', 'platformRef'}:
                raise ValueError('Unsupported commercial payload')
            name = payload.get('name', 'Smilelab opportunity ' + body['platformId'])
            if not isinstance(name, str) or not name.strip() or len(name) > 256:
                raise ValueError('Invalid commercial name')
            if 'platformRef' in payload and payload['platformRef'] != body['platformId']:
                raise ValueError('Commercial reference mismatch')
            digest = hashlib.sha256(json.dumps(body, ensure_ascii=False, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
            # 同一事件串行；相同编号更改租户、引用或正文必须报告冲突。
            request.env.cr.execute('SELECT pg_advisory_xact_lock(hashtextextended(%s,0))', [body['eventId']])
            events = request.env['chipmunk.integration.event'].sudo()
            existing = events.search([('event_id', '=', body['eventId'])], limit=1)
            if existing:
                if existing.tenant_id != body['tenantId'] or existing.platform_id != body['platformId'] or existing.company_id != company:
                    return self.error(409, 'Event identity conflict')
                if existing.payload_hash and not hmac.compare_digest(existing.payload_hash, digest):
                    return self.error(409, 'Event payload conflict')
                if not existing.payload_hash:
                    # 升级前事件只在原商业名称一致时补记摘要，不覆盖旧线索。
                    lead = request.env['crm.lead'].sudo().browse(existing.odoo_id).exists()
                    if existing.odoo_model != 'crm.lead' or not lead or lead.name != name:
                        return self.error(409, 'Legacy event payload conflict')
                    existing.write({'payload_hash': digest})
                result = {'model': existing.odoo_model, 'id': existing.odoo_id, 'duplicate': True}
            else:
                # 白名单字段映射；公司只能来自管理员维护的租户映射。
                try:
                    with request.env.cr.savepoint():
                        lead = request.env['crm.lead'].sudo().with_company(company).create({
                            'name': name, 'type': 'lead', 'company_id': company.id,
                            'user_id': False, 'team_id': False,
                            'chipmunk_platform_ref': body['platformId'],
                            'description': '仅商业引用；患者、疗程和医疗媒体保存在花栗鼠平台。',
                        })
                        events.create({'event_id': body['eventId'], 'tenant_id': body['tenantId'], 'platform_id': body['platformId'], 'payload_hash': digest, 'odoo_model': 'crm.lead', 'odoo_id': lead.id, 'company_id': company.id})
                except UniqueViolation as error:
                    if error.diag.constraint_name != 'chipmunk_integration_event_event_unique':
                        raise
                    # Odoo 使用可重复读；等待锁之后仍可能持有旧快照。
                    # 让框架回滚整个事务并重试，不能在旧快照中查询已提交事件。
                    raise ConcurrencyError('Commercial event requires a fresh transaction snapshot') from error
                result = {'model': 'crm.lead', 'id': lead.id, 'duplicate': False}
            return request.make_json_response({'code': 0, 'message': 'ok', 'data': result})
        except RequestEntityTooLarge:
            return self.error(413, 'Event exceeds 64KiB')
        except (ValueError, TypeError, UnicodeDecodeError, json.JSONDecodeError):
            return self.error(400, 'Invalid event')
