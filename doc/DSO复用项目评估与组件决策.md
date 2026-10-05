# DSO 复用项目评估与组件决策

评估日期：2026-10-05。范围是已部署的 Smilelab 开发/演示环境；依据用户提供的《花栗鼠_DSO平台总体技术架构设计_V1.0》划分业务边界。本文是选型记录，不把开源项目的宣传描述当作本平台的合规或生产就绪证明。

## 业务边界与现有资产

患者、医生、门诊、预约、临床病历、医疗媒体、牙科 CRM、AI 网关和小程序接口归平台。Odoo 负责标准商业 CRM、销售、采购、库存、人事、发票等企业运营。平台与 Odoo 使用独立数据库，通过服务端适配器、事务 outbox 和集成映射连接。临床正文、照片、病史及患者联系方式不随商业事件发送给 Odoo。

现有平台是 .NET 10 + Npgsql/PostgreSQL 的独立服务；旧 `api/Admin.Api` 未被替换，也不调用其受人工限制的 `--init`。优先补齐模块和可验证的接口，不为复用框架重写已部署的服务。

## 项目比较

| 项目 | 可复用能力 | 许可证证据 | 当前决策与原因 |
|---|---|---|---|
| Odoo Community 19 | CRM、销售、库存、采购、HR、会计基础 | [Odoo 源码](https://github.com/odoo/odoo/tree/19.0)；按模块核对许可证 | 已部署，继续复用标准运营模块。企业版特性不按社区版已实现处理。临床业务留在平台。 |
| OCA `queue_job` 19.0 | Odoo ORM 异步任务与管理界面 | [19.0 模块 manifest](https://github.com/OCA/queue/blob/19.0/queue_job/__manifest__.py) 明确 LGPL-3；仓库总许可证不能替代模块许可证 | 候选，暂不安装。当前跨数据库事件队列需由平台持有，先实现租约、退避、死信和人工重放；后续 Odoo 内部重型业务可复用。 |
| OpenIddict | .NET OAuth/OIDC 服务端、客户端与 token 验证 | [仓库](https://github.com/openiddict/openiddict-core)，Apache-2.0 | 推荐作为后续正式身份接入候选。它是框架，仍需要登录、账户恢复、授权及协议一致性测试。当前演示使用隔离的员工开发凭证与可撤销的服务器会话。 |
| Keycloak | 独立身份管理服务、OIDC/SSO 管理 | [仓库及许可证](https://github.com/keycloak/keycloak)，Apache-2.0 | 正式 SSO 的备选；本轮不增加额外身份服务及运维面。应比较企业 SSO/MFA 需求后再决定与 OpenIddict 的取舍。 |
| Hangfire | .NET 持久化后台作业、调度与管理界面 | [仓库许可证](https://github.com/HangfireIO/Hangfire#license)，LGPL-3.0-or-later；扩展存储/商业功能单独核对 | 候选，不因为有作业界面就引入第二套事件事务模型。当前 PostgreSQL outbox 与业务事务一起提交，适合少量集成事件；复杂批处理再评估。 |
| Medplum | FHIR 存储、SDK、医疗 UI、SMART/OIDC | [仓库](https://github.com/medplum/medplum)，Apache-2.0 | 中长期医疗互操作参考。整套平台会新增服务和模型体系；本轮不迁移所有临床数据，也不声称已提供 FHIR 服务。 |
| Firely .NET SDK | FHIR 资源模型、序列化与客户端 | [SDK](https://github.com/FirelyTeam/firely-net-sdk)、[许可证](https://github.com/FirelyTeam/firely-net-sdk/blob/develop/LICENSE)，BSD-3-Clause | 若需 FHIR 导出，优先评估 SDK 而不是自己拼装一个声称兼容 FHIR 的服务。需明确 R4/R5、实施指南及验证器后接入。 |
| Vue 3 + Ant Design Vue | 管理及医生工作台表单、表格、反馈组件 | [组件仓库](https://github.com/vueComponent/ant-design-vue)、[MIT 许可证](https://github.com/vueComponent/ant-design-vue/blob/main/LICENSE) | 选作本轮工作台组件来源，与项目现有管理前端方向一致。保留依赖锁文件和第三方声明，API 权限仍由后端强制执行。 |
| FullCalendar 标准插件 | 排班及预约日历 | [仓库](https://github.com/fullcalendar/fullcalendar)、[标准代码 MIT 许可证](https://github.com/fullcalendar/fullcalendar/blob/main/LICENSE.md) | 已采用 7.1.0 的 Vue、日期网格和时间网格标准插件。资源排班等 Premium 插件需另外评估授权，不能从总仓库 MIT 推断所有插件免费。 |
| AWS SDK for .NET | S3 客户端与存储适配 | [官方仓库](https://github.com/aws/aws-sdk-net)，Apache-2.0 | 后续真实对象存储适配候选。当前按用户要求使用私有磁盘和签名能力 URL，不能声称磁盘实现具有完整 S3/OSS 协议兼容性。 |

| SkiaSharp 4.153.1 | 解码媒体、验证尺寸与单帧限制 | [官方 MIT 许可证](https://github.com/mono/SkiaSharp/blob/main/LICENSE.md)；原生包第三方声明随服务交付 | 已采用，仅验证原始图片，不执行诊断或修改原图。保留原生依赖的许可文本。 |
| openapi-spec-validator 0.7.2 | OpenAPI 3 契约检查 | [官方 Apache-2.0 许可证](https://github.com/python-openapi/openapi-spec-validator/blob/0.7.2/LICENSE) | 仅开发和 CI 使用，补足生成器一致性检查，防止无效 schema 交付给客户端。 |

依赖固定在锁文件；Vue 3.5.43、Ant Design Vue 4.2.6、Vue I18n 11.4.13 和 TypeScript 5.9.3 组成已验证的构建组合。第三方声明见根目录 `THIRD_PARTY_NOTICES.md` 和工作台 `/workspace/THIRD_PARTY_NOTICES.txt`；构建工具也被列入声明，不代表它们全部进入浏览器运行时。

## 本轮实现顺序与验收

1. 数据库迁移账本和校验和；机构、门诊、用户、资源之间的复合外键阻止跨租户关联。
2. 员工权限与患者权限隔离；门诊切换只能使用数据库中有效授权。患者可显式授予/撤销医生的临床访问权限。
3. 医生排班、单席预约、乐观版本控制和通知；以真实数据库并发测试证明同一时段只允许一个预约成功。
4. 平台临床档案；草稿仅医生可读，患者只读已签署内容，签署后由数据库触发器保护不可覆盖，通过关联的新病历修订。
5. 牙科 CRM、商业事件同步与可观测的重试/死信；Odoo 只接收经过适配器筛选的商业引用。
6. 工作台和开发者契约；页面操作使用真实接口，演示能力明确标注，不把 AI 占位图、短信占位和空模块包装成已实现功能。

每阶段使用临时 Docker 网络、数据库和合成数据验证。线上部署前执行已有小程序 HTTPS 测试和备份恢复验证，保留可回退的镜像及迁移前快照。

## 关键技术依据

[PostgreSQL 16 显式锁文档](https://www.postgresql.org/docs/16/explicit-locking.html)说明行锁和事务锁语义；预约使用锁定时段及唯一索引防止竞态，事件队列使用短事务认领，不在数据库锁内等待外部 HTTP。

[Odoo 19 JSON-2 官方文档](https://www.odoo.com/documentation/19.0/developer/reference/external_api.html)是后续通用 API 适配参考。当前窄范围桥接端点便于把允许的事件、租户和公司映射集中校验；不要向小程序开放 Odoo 管理 API 或管理员凭证。

## 未完成的生产要求

正式微信凭据、短信供应商、真实 AI/GPU 服务、正式身份登录/MFA、机构法律及数据保留要求、离站备份/PITR、负载测试、医疗审计评审、灾备演练及跨系统财务核对仍需单独落实。已有签署状态只表示平台的不可变工作流，不等同于具有法律效力的电子签名。
