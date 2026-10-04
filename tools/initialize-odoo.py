"""通过 odoo shell 执行；env 由 shell 注入。"""
import os
admin = env.ref('base.user_admin')
admin.write({'login': 'admin@smilelab.ai', 'password': os.environ['ODOO_ADMIN_PASSWORD'], 'name': 'Smilelab Demo Administrator'})
env.ref('base.main_company').write({'name': 'Smilelab Demo'})
env['ir.config_parameter'].sudo().set_param('web.base.url', 'https://odoo.smilelab.ai')
env['ir.config_parameter'].sudo().set_param('web.base.url.freeze', 'True')
env.cr.commit()
print('Odoo administrator initialized; password was not printed.')
