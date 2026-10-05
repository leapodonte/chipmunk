# Smilelab DSO 开发接口与工作流

版本：0.2，2026-10-05。本文描述已实现的开发/演示功能。工作台与小程序共用独立的 `chipmunk_platform` 数据库，Odoo 使用 `smilelab_demo`；旧 `api/Admin.Api` 服务未被替换或初始化。

## 入口与认证

| 用途 | 入口 |
| --- | --- |
| 医生、门诊管理、咨询、运维工作台 | `https://app.smilelab.ai/workspace/` |
| 患者小程序 API | `https://app.smilelab.ai/api/v1` |
| DSO 员工及临床 API | `https://app.smilelab.ai/api/dso/v1` |
| 完整 OpenAPI | `https://app.smilelab.ai/api/openapi.json` |
| Odoo 商业后台 | `https://odoo.smilelab.ai` |

患者使用既有开发登录 `POST /api/v1/auth/mp-login`，开发身份 `demo:你的固定测试标识`，请求头 `X-Dev-Key`。员工使用 **每账号独立** 的随机凭据：

```http
POST /api/v1/auth/staff-login
Content-Type: application/json
X-Staff-Dev-Key: <单独交付的该员工账号密钥>

{"identity":"staff:doctor"}
```

返回 `{code:0,message:"ok",data:{token,userId,expiresIn:86400,authMode:"staff-demo",isNewUser:false}}`。后续请求使用 `Authorization: Bearer <token>`。员工凭据与患者开发密钥不能互换；医生凭据不能登录运维账号。演示账号包括 `staff:doctor`、`staff:doctor2`、`staff:manager`、`staff:consultant`、`staff:operator`。工作台暂提供四个常用账号选择，第二位医生可直接通过 API 登录。密钥只保存在离线私有交付文件，服务数据库保存 SHA-256；工作台会话只保存在内存，刷新后重新登录。

`GET /api/dso/v1/context` 返回可信的 `userId,tenantId,organizationId,clinicId,roles,environment`。**客户端提交的租户、角色或医生 ID 不授予权限。** `GET clinics/mine` 列出获授权门诊；`POST context/switch` 的正文为 `{"clinicId":"..."}`，成功后重新获取 context。切换只影响当前 token。

统一成功响应 `{code:0,message:"ok",data:...}`。HTTP 状态与错误 `code` 一致；400 参数错误、401 会话过期、403 角色不足、404 不在资源授权范围、409 状态/版本/幂等冲突、413 正文过大、429 频控。使用响应头 `X-Request-Id` 定位问题。请求头 `Accept-Language: en` 返回英语错误；默认中文。业务 JSON 上限 64KiB，正文必须是对象，重复属性名会被拒绝。

## 幂等、版本与分页

创建时段、预约、病历、CRM 线索、跟进时，必须带 `Idempotency-Key`（1–128 字符，建议 UUID）。重试保持相同键及正文，返回原结果；同键改正文返回409。对象属性顺序不影响幂等哈希，数组顺序影响。幂等范围包含用户、租户、当前门诊与业务操作，不能用其他员工的键访问资源。

修改资源时提交上次读取的正整数 `version`。成功版本递增，409 后刷新并让用户确认新状态，不能自动覆盖。DSO 列表支持 `page`（默认1，最大10000）与 `pageSize`（默认20，最大100），返回数组。工作台每页20条，翻页时查询服务端；API 未返回总数，满页时提供下一页，精确整页的数据集可能出现一个空的末页。日历按可见日期范围独立加载，最多1000条，超过时提示缩小视图。预约与排班列表支持 `from`、`to`，范围不超过93天，按时间区间相交过滤。

日期时间必须为带 `Z` 或 `+08:00` 等明确偏移的 ISO 8601，数据库使用 timestamptz。工作台按浏览器时区显示，不能把本地未标时区字符串直接发送至 API。

## 预约和医生排班

1. 员工 `GET /api/dso/v1/doctors` 读取门诊医生。
2. 医生仅给自己排班，门诊/区域管理者可管理获授权门诊排班。`POST slots`：

```json
{"doctorId":"d_001","startsAt":"2026-10-12T08:00:00+02:00","endsAt":"2026-10-12T08:30:00+02:00"}
```

时段开始须在未来180天内，最长4小时，同医生时段不能重叠。`GET slots?doctorId=d_001` 返回未来可预约时段；`POST slots/{id}/close` 只能关闭未占用时段。

3. 患者 `POST /api/v1/appointments`：`{"slotId":"slot_...","shareWithDoctor":true}`。`shareWithDoctor` 默认 false；设 true 才同时授予该医生临床资料访问权。预约与授权及患者消息在同一事务提交。并发预约同一时段最多一人成功。
4. 患者 `GET /api/v1/appointments/mine`，员工 `GET /api/dso/v1/appointments`。患者只看本人，医生只看自己预约，管理/助理按门诊角色访问。
5. `POST appointments/{id}/status`：`{"version":1,"status":"arrived"}`。

允许 `booked → arrived/cancelled/no_show`、`arrived → completed/cancelled`。完成不得早于开始，no_show 不得早于结束。患者仅可取消未来的本人 booked 预约。取消后时段可重新预约。终态不能覆盖。预约返回患者/医生显示名、ID、时段、状态与版本。

## 患者授权、档案和病历

患者：`GET/PUT /api/v1/patients/me`（也接受 PATCH）。更新示例：

```json
{"version":1,"displayName":"测试患者","profile":{"gender":"unknown","birthDate":"2000-01-01","allergies":"测试资料","medicalHistory":"测试资料","emergencyContact":{"name":"测试联系人","phone":"000000","relationship":"其他"}}}
```

姓名≤100字符，profile≤8KiB；allergies/medicalHistory各≤3000；联系人姓名/关系≤100、电话≤32，姓名和电话必填。只接受列出的字段；出生日期不可在未来。

患者 `GET patients/me/care-team` 查看授权；`POST patients/me/care-team/{doctorId}` 授权，`DELETE` 撤销，须同门诊活动医生。撤销立即影响后续 API 访问。已下载文件和已读信息不能远程收回。

员工 `GET /api/dso/v1/patients`：医生仅看到自己获授权患者；咨询/管理角色仅得到最小人口信息。`GET patients/{id}` 对获授权医生返回 profile，对非临床管理角色不返回 profile。管理者、运维不会因岗位自动获得病历正文。

获授权医生：

| 操作 | 路由与正文 |
| --- | --- |
| 列出患者病历 | `GET patients/{id}/records` |
| 创建草稿 | `POST patients/{id}/records`，`{"content":{...}}`，带幂等键 |
| 读取单条 | `GET clinical-records/{id}` |
| 作者修改草稿 | `PATCH clinical-records/{id}`，`{"version":1,"content":{...}}` |
| 作者签署 | `POST clinical-records/{id}/sign`，`{"version":2}` |
| 修订签署记录 | 新建草稿时提供 `previousId`；原记录保留 |

content 支持 `chiefComplaint,history,examination,assessment,plan,toothChart`，每段文字≤8000字符，总内容≤32KiB。牙位使用 FDI 编码，恒牙11–18/21–28/31–38/41–48，乳牙51–55/61–65/71–75/81–85，不能重复：

```json
{"content":{"chiefComplaint":"合成演示主诉","plan":"合成演示计划","toothChart":[{"tooth":11,"finding":"合成牙位记录"}]}}
```

签署记录由数据库触发器禁止 UPDATE/DELETE，后续以新记录关联 `previousId` 修订。签署不是具备法律认证的电子签名。患者 `GET /api/v1/patients/me/records` 只能看到本人已签署记录，草稿不向患者开放。临床读取与修改均有元数据审计。尚未提供影像检查/DICOM、处方、收费或临床文件附件模块。

## 平台 CRM 与 Odoo 边界

`GET/POST /api/dso/v1/crm/leads`，创建正文 `{"title":"合成咨询线索","patientId":"patient_..."}`，patientId 可省略。创建线索与商业 outbox 在同一事务提交。

`GET/PATCH crm/leads/{id}`。更新为 `{"version":1,"stage":"contacted","assignedTo":"staff_demo_consultant"}`，允许阶段 `new/contacted/qualified/won/lost`；won/lost 不能重新打开。assignedTo 只能指向本门诊获授权的员工，可传 null 取消分配。

`GET/POST crm/leads/{id}/activities`，创建 `{"type":"follow_up","summary":"合成跟进","dueAt":"2026-10-12T10:00:00+02:00"}`，type 为 call/follow_up/visit，summary≤300，dueAt 不可超过未来一年；`PATCH crm/activities/{id}`，`{"version":1,"status":"done"}` 或 cancelled。

平台保存咨询标题、患者关联、跟进与阶段。**当前 Odoo 仅接收生成的商业机会名称和平台引用**，不接收患者姓名、电话、线索标题、病历正文或图片。平台的 CRM 阶段和跟进暂未同步到 Odoo；不要将 Odoo 的阶段视为平台状态。

投递为至少一次：短事务领取45秒租约，HTTP 在事务外执行，远端按事件 ID 幂等。失败退避重试，最多10次后进入死信，超时和失效租约可恢复。

运维角色：`GET operations/summary`、`GET operations/outbox?status=dead_letter`、`GET operations/audit`。返回计数、引用和审计元数据，不返回 outbox 正文、上游错误详情或临床内容。`POST operations/outbox/{id}/retry`，`{"reason":"上游配置修复后的合成验证"}`，原因≤300，保留原事件ID并审计。

## 磁盘 OSS 与持久化 AI mock

使用原小程序媒体接口：申请上传凭证→签名 PUT/POST→保存 objectKey→申请短期下载 URL。每文件10MiB、每患者100MiB，图片解码限制单边4096、总像素1200万、单帧；损坏图片和多帧图片拒绝。服务保存原始字节的 SHA-256、宽高、上传者、时间、租户、门诊与私有存储标记。上传后不能覆盖；数据库失败时清理本次新建磁盘文件。当前单 API 进程最多同时处理2个上传，繁忙时返回429和 `Retry-After: 2`，尚未读取正文或写入文件；客户端可延迟重试同一未过期凭证。生产需另行安排病毒扫描、保留策略和异地备份。

`POST /api/v1/ai/simulations`，`{"imageKey":"本人已上传ai_photo的objectKey"}`。任务状态持久化，内部 `jobStatus` 为 queued/running/succeeded/failed/cancelled；兼容界面 `status` 为 analyzing/done/quality_failed。`GET ai/simulations/{taskId}` 轮询，`POST ai/simulations/{taskId}/cancel` 取消排队/运行任务，终态取消409。

当前 provider=mock、isMock=true，只返回原图占位，不进行诊断、图像分析或美化。worker 校验原图 SHA-256，失败公开通用 failureCode；数据库租约保证重启后可恢复，已取消任务不会被旧 worker 覆盖。未配置真实模型或 GPU。

## 本地复用与部署验证

`packages/smilelab-client` 提供共享 TypeScript 类型和客户端，已用于工作台。调用示例：

```ts
import { SmilelabClient, newIdempotencyKey } from '@smilelab/client';
const client = new SmilelabClient({ baseUrl: 'https://app.smilelab.ai', token: () => token });
const context = await client.dso('context');
const lead = await client.dso('crm/leads', 'POST', { title: '合成演示' }, newIdempotencyKey());
```

客户端不内置密钥、不自动重试写操作、不持久化会话。开发环境 CORS 已允许 localhost:5173 和 localhost:8080；其他本地端口需由部署配置明确加入。

完整镜像从仓库根目录构建：`docker build -f platform/Dockerfile.full -t chipmunk-platform:demo .`。独立集成测试 `python3 platform/tests/dso_integration.py --image <镜像>` 创建临时 Docker 网络和数据库，测试不会写线上数据；UI 测试需显式私有 fixture 路径和隔离环境 URL。迁移检查已应用脚本校验和，不运行旧系统 `--init`。员工首次配置使用 `tools/provision-dso-staff.py --output <私有路径>`，重复执行保留现有密钥，冲突回滚。

开发环境尚未实现正式 SSO/MFA、真实短信/支付/AI、FHIR 数据交换、异地备份/PITR、自动租户开户和生产医疗合规流程。Odoo 已提供管理员维护的租户—公司映射；未映射或停用的租户事件会被拒绝。具体复用项目、许可与后续计划见 `DSO复用项目评估与组件决策.md`。

## UniApp / 微信小程序联调

`packages/smilelab-client/src/uni.ts` 提供 `SmilelabUniClient`，通过注入的 `uni.request` 和 `uni.uploadFile` 工作，不依赖浏览器 fetch、Response、AbortController 或本地存储。原生请求方法兼容性见 [DCloud 官方说明](https://en.uniapp.dcloud.io/api/request/request.html)：微信列有 PUT、DELETE，而不同小程序平台的支持不一致。本人资料完整替换提供 `PUT /api/v1/patients/me`；工作台 PATCH 保留。仅当前微信目标已准备，其他平台仍需适配。

```ts
import { SmilelabUniClient } from '@smilelab/client/uni';
// 应用自己的会话状态；不要把开发密钥写入源码或正式小程序包。
let token: string | null = null;
const api = new SmilelabUniClient({
  transport: {
    request: options => uni.request(options),
    uploadFile: options => uni.uploadFile(options),
  },
  token: () => token,
  language: () => 'zh-Hans',
});
// 正式微信 code 来自 uni.login；当前需先配置服务端微信凭据。
const auth = await api.demoLogin('demo:你的固定测试标识', 临时输入的开发密钥);
token = auth.token;
const profile = await api.profile();
await api.updateProfile({ displayName: profile.displayName, version: profile.version, profile: {} });
const slots = await api.slots({ doctorId: 'd_001', page: 1, pageSize: 20 });
// 业务操作生成并保留一个唯一键；网络重试时复用同一个键及正文。
const slot = slots.find(item => item.available);
if (!slot) throw new Error('请先由门诊经理发布一个可预约时段');
const booking = await api.book(slot.id, 患者明确选择共享给医生, 已保存的本次预约操作唯一键);
const grant = await api.uploadGrant('ai_photo', 'png');
const upload = api.upload(grant, 临时图片文件路径);
const media = await upload.promise; // upload.cancel() 可终止原生传输
const task = await api.simulate(media.objectKey);
const result = await api.simulation(task.taskId); // 仅 jobStatus=succeeded 时展示完成
```

包导出 TypeScript 源码，使用项目 workspace 或源文件别名接入；TypeScript 设置 `moduleResolution: "Bundler"`、`allowImportingTsExtensions: true`、`noEmit: true`（由 UniApp 构建器产出）。上例的中文占位变量需要由页面提供；先登录获得 token 再执行个人接口。

在微信后台为 request、uploadFile、downloadFile 配置 `https://app.smilelab.ai` 的合法域名。签名 URL、开发密钥和 Bearer token 不写入日志；图片 URL 只是短期访问能力，需持久保存 objectKey/mediaId，过期后通过本人媒体 URL 接口重新申请。SDK 不自动重试写入，也不自动接受医生共享：`book` 的 shareWithDoctor 由页面根据患者明确选择传入。

错误为 `ApiError`，包含 HTTP status 与 requestId；原生网络/超时/取消的 status 为0。401清除会话并重新登录；409刷新资源并确认状态，不能自动覆盖。取消网络请求不保证服务器事务未提交，重试预约应保持原幂等键。已上传文件不能覆盖；响应丢失时先核对上传状态，不能直接无限重试。

适配器已通过8个原生传输契约测试，服务端 PUT 和日期区间通过真实 PostgreSQL 测试。微信开发者工具、真机及正式 code 登录尚未验证；当前仓库 `app/` 仍是占位目录，本轮交付的是小程序后端与可复用联调组件。
