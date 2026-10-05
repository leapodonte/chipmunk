from odoo import fields, models


class IntegrationEvent(models.Model):
    _name = 'chipmunk.integration.event'
    _description = '花栗鼠商业集成事件'

    event_id = fields.Char(required=True, index=True)
    tenant_id = fields.Char(required=True, index=True)
    platform_id = fields.Char(required=True, index=True)
    odoo_model = fields.Char(required=True)
    odoo_id = fields.Integer(required=True)
    company_id = fields.Many2one('res.company', required=True)
    payload_hash = fields.Char(index=True, readonly=True)
    _event_unique = models.Constraint('UNIQUE(event_id)', '事件编号必须唯一')


class TenantCompany(models.Model):
    _name = 'chipmunk.tenant.company'
    _description = '花栗鼠租户商业公司映射'

    tenant_id = fields.Char(required=True, index=True)
    company_id = fields.Many2one('res.company', required=True, ondelete='restrict')
    active = fields.Boolean(default=True)
    _tenant_unique = models.Constraint('UNIQUE(tenant_id)', '租户商业公司映射必须唯一')


class CommercialLead(models.Model):
    _inherit = 'crm.lead'

    chipmunk_platform_ref = fields.Char(index=True, readonly=True)
