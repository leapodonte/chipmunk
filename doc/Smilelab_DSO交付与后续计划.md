# Smilelab DSO 开发演示交付

交付日期：2026-10-05。已部署产品运行版本 `98651c7`，基础设施版本 `18a6bc0`。后续文档与只读检查工具提交不改变该运行版本。目标为开发联调与演示，尚未交付生产医疗系统或完整患者小程序界面。

## 入口与凭据

| 用途 | 地址 |
|---|---|
| 医生、门诊经理、咨询顾问、平台运维工作台 | https://app.smilelab.ai/workspace/ |
| API 文档导航 | https://app.smilelab.ai/api/docs |
| OpenAPI 3 契约 | https://app.smilelab.ai/api/openapi.json |
| 可下载开发指南 | https://app.smilelab.ai/api/developer-guide |
| Odoo Community 19 | https://odoo.smilelab.ai |

现有 `app` 与 `odoo` DNS 即可，不需要额外 api/oss 子域名。Caddy 自动管理 HTTPS，两个数据库和内部商业桥接不发布公网端口。工作台默认中文，可切换英语；密钥和 token 不写浏览器持久化存储。

开发凭据在独立私有交付文件 `smilelab-developer-credentials.json`、`smilelab-staff-demo.json`、`smilelab-odoo-admin-credentials.json` 中，不放入源码、截图或普通报告。员工文件提供五个彼此独立的演示账号；工作台直接提供四种角色登录，助手可通过 API 登录。Odoo 管理员登录名为 `admin@smilelab.ai`，既有密码保留。

## 已实现的边界

平台拥有租户、组织、门诊、可信用户角色、患者档案与医生授权、排班预约、不可变签署病历和修订、FDI 牙位记录、咨询线索和跟进、访问审计、商业 outbox 与死信恢复。预约创建有幂等和单席并发保护；业务更新使用版本检查；门诊切换后的幂等缓存互相隔离。

小程序后端支持演示登录、档案与问卷、治疗、佩戴打卡、医生和预约、消息、内容、私有媒体及持久化模拟 AI 作业。SDK 同时提供浏览器和原生 UniApp 请求/上传适配，患者档案使用原生支持的 PUT。失败状态、取消和请求追踪可供前端处理。未配置真实微信 AppID/Secret 时，真实微信 code 登录返回明确的未配置错误。

媒体存放在私有磁盘卷，通过有期限的上传授权和签名下载访问。每文件10MiB、每患者100MiB，真实解码验证图片，限制尺寸、像素数和同时上传数量；保留原始字节哈希。模拟 AI 明确标记 mock，不作临床诊断。

Odoo 已安装商业基础模块，自有桥接具有租户—公司映射、管理员权限、事件去重和修改重放拒绝。目前仅新建商业线索单向同步，发送生成的引用，不发送患者姓名、病历正文、照片或咨询标题。Odoo 财务、税务、库存和订阅的真实业务流程仍需配置。

复用评估、固定依赖与许可见 [组件决策](DSO复用项目评估与组件决策.md) 和 [第三方声明](../THIRD_PARTY_NOTICES.md)。Vue、Ant Design Vue、FullCalendar 标准插件和 SkiaSharp 已集成；FHIR、正式身份服务和外部对象存储保留明确扩展边界。`dom-scroll-into-view` 上游完整许可文本缺口已记录，不能宣称全部许可材料齐备。

## 验证与容量

产品 CI 验证24组独立 PostgreSQL 权限/并发/工作流/恢复检查、8组原生 SDK 契约、6组真实浏览器流程、7组真实 Odoo 19 桥接检查及 OpenAPI。npm 生产依赖与 .NET 包审计在交付时均未报告已知漏洞。线上另通过16组小程序 HTTPS 检查、8组 DSO HTTPS 检查及四角色只读浏览器检查；既有 dcad 环境11组检查通过。所有写入检查使用明确标记的合成资料。

主机8vCPU、约23GiB内存、193GiB磁盘。03:31 UTC 采样可用内存20,924MiB、磁盘84GiB；四服务空闲内存约600MiB，运行内存上限合计4.5GiB。适合轻量开发和演示。自托管 runner 未限额，构建仍可能争抢资源。短时隔离读测量不能作为生产用户数量承诺；完整数据和限制见 [容量报告](Smilelab_DSO容量与运行边界.md)。

## SSH 与日常操作

本机 SSH 配置可直接连接：

```powershell
ssh -o BatchMode=yes -o ConnectTimeout=10 dcad@dcad.ai
```

在 VPS 查看服务与近期日志：

```bash
docker compose --env-file /srv/dcad/secrets/chipmunk.env -f /srv/dcad/infra/compose/chipmunk.yml ps
docker compose --env-file /srv/dcad/secrets/chipmunk.env -f /srv/dcad/infra/compose/chipmunk.yml logs --tail=100 chipmunk-api odoo
curl -fsS https://app.smilelab.ai/health
```

源码位于 `/srv/dcad/src/chipmunk`，秘密位于 `/srv/dcad/secrets`。基础设施由 `dcad-infra` 仓库及 Deploy infra workflow 发布，不直接手改服务器配置。产品从通过 CI 的提交打包到独立 staging 目录，再执行仓库发布脚本；脚本备份两库、两文件卷、源码、密钥和 API 镜像，升级自有桥接与平台迁移。当前平台迁移账本七项。

每日03:15 Europe/Paris执行同主机完整备份，保留约七天。手动备份会短暂停止写服务：

```bash
bash /srv/dcad/infra/scripts/backup-smilelab.sh
```

部署前快照为 `20261005T032920Z`；部署后快照为 `20261005T033120Z`，包含可导入的 API 镜像。完整隔离恢复演练64.6秒通过：导入归档镜像、恢复两库及七项迁移账本，验证五个员工账号、Odoo 管理员登录与真实桥接、工作台及11个媒体下载字节哈希。具体结果记录在交付目录的统计 JSON 中。恢复时数据库、源码、文件、密钥与镜像必须选择同一快照，不能只回退镜像。恢复工具使用隔离临时容器和卷，不覆盖线上数据；当前只验证同主机恢复，没有替代 VPS、异地备份或 PITR 验证。

## 后续优先顺序

1. 对接真实微信登录并在真机/开发者工具验证 SDK，完成患者与医生小程序 UI；配置真实短信与支付。
2. 选择正式身份系统，替换开发密钥，配置 MFA、员工生命周期、备份账户及会话策略。
3. 配置异地加密备份、恢复目标及 PITR，按真实媒体增长控制快照占用；限制共享 runner 资源。
4. 与业务负责人明确患者授权、病历签署与修订、保存/删除周期及所在地区的医疗和隐私要求。
5. 完成 Odoo 会计本地化和真实商业流程，再明确哪些商业对象需要双向同步；临床数据继续独立。
6. 按真实模型接口替换模拟 AI，验证数据处理边界；另行实施病历附件、DICOM/FHIR，而非将照片模拟当作这些功能。

患者 `app/` 与医生 `doctor/` 目录仍为占位，正式 AI、FHIR、附件、真实支付及生产医疗合规不属于已完成部分。
