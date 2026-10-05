{
    'name': 'Chipmunk Enterprise Integration',
    'author': 'Smilelab',
    'version': '19.0.3.0.0',
    'license': 'LGPL-3',
    'depends': ['crm', 'sale_management', 'purchase', 'stock', 'hr', 'account'],
    'data': ['security/ir.model.access.csv', 'data/tenant_company.xml', 'views/integration.xml'],
    'installable': True,
}
