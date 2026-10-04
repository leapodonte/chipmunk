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
    _event_unique = models.Constraint('UNIQUE(event_id)', '事件编号必须唯一')


class CommercialLead(models.Model):
    _inherit = 'crm.lead'

    chipmunk_platform_ref = fields.Char(index=True, readonly=True)
