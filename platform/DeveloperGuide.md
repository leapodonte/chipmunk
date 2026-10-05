# Smilelab 小程序 API 开发文档

日期：2026-10-05。版本：0.1.0。环境：开发与演示。

本后端用于花栗鼠小程序前端联调，按《小程序前端API接口设计》提供 REST 接口。患者、医生、疗程、打卡、媒体与任务保存在平台自己的 PostgreSQL 中；Odoo 只接收商业咨询引用。本文说明真实持久化的功能、演示功能以及暂未接入的功能，方便开发者避免把演示结果当成真实业务结果。

## 地址与配置

| 用途 | 地址 |
| --- | --- |
| 平台 API 根地址 | `https://app.smilelab.ai` |
| 业务 Base URL | `https://app.smilelab.ai/api/v1` |
| 健康检查 | `GET https://app.smilelab.ai/health` |
| OpenAPI 3 文档 | `GET https://app.smilelab.ai/api/openapi.json` |
| 开发文档入口 | `https://app.smilelab.ai/api/docs` |
| 中文文档下载 | `https://app.smilelab.ai/api/developer-guide` |
| Odoo 管理入口 | `https://odoo.smilelab.ai` |
| 本地磁盘对象 API | 上传凭证返回的 `/storage/objects/{id}` URL |

前端配置：

```dotenv
VITE_USE_MOCK=false
VITE_API_BASE_URL=https://app.smilelab.ai
```

若网络封装已自动追加 `/api/v1`，不要在环境变量中重复追加。当前仓库 `app/` 为空，本次部署未修改尚未迁入的前端项目。需在实际小程序项目中接入这些地址。

微信小程序后台配置：request、uploadFile、downloadFile 合法域名均为 `https://app.smilelab.ai`。仅患者小程序无需配置 Odoo 域名。浏览器本地联调允许 `http://localhost:5173` 与 `http://localhost:8080`；其他 Origin 要由运维增加到 `CORS_ORIGINS`。微信正式发布条件与真实微信登录尚需 AppID/AppSecret 配置。

## 通用协议

所有业务接口使用 JSON，不是 Odoo JSON-RPC。除登录和演示验证码接口外，携带 `Authorization: Bearer <token>`。

```json
{"code":0,"message":"ok","data":{}}
```

错误响应同时使用对应 HTTP 状态，如 401、404、409、413、429、501、503，且 `data` 为 `null`。检查 HTTP 状态和 `code`，不要把错误当空列表。`Accept-Language: en` 获取英文错误，默认简体中文。响应 `X-Request-Id` 可用于排查。分页 `page` 从1开始，`pageSize` 默认10，最大100，列表 `data` 为数组；当前演示版每类资源最多读取最新1000条，不提供总数。

Token 为不透明随机字符串，并非 JWT；有效24小时，数据库只存哈希。再次登录签发新 token，`POST /auth/logout` 撤销当前 token。租户、机构、门诊由服务端身份上下文确定，客户端传入任意 Tenant/Clinic 头不会改变权限。演示版仅开放 `tenant_demo → org_demo → clinic_demo`。

## 登录与首次进入

### 开发演示登录

`POST /auth/mp-login`，增加 `X-Dev-Key: <开发密钥>`：

```json
{"code":"demo:frontend-alice"}
```

```json
{"code":0,"message":"ok","data":{"token":"<opaque-token>","userId":"u_<id>","isNewUser":true,"expiresIn":86400,"authMode":"demo"}}
```

同一个 `demo:` 标识再次登录返回同一用户；不同标识生成不同用户。开发密钥通过单独的凭据文件交付，不在文档或仓库中保存。它只用于开发登录，不需要随每个业务接口发送。禁止打包进正式发布的小程序。

### 真实微信登录

同一路径传 `{ "code": "wx.login获得的临时代码" }`。服务端通过 HTTPS 调用 code2session；未配置凭据时返回503，不会把任意 code 当成微信身份。配置项为 `CHIPMUNK_WECHAT_APP_ID` 和 `CHIPMUNK_WECHAT_APP_SECRET`。前端不持有 AppSecret。

`inviteCode` 参数目前预留，不建立奖励关系。开发手机号登录 `POST /auth/phone-login` 需开发密钥和 `{ "phone":"13800138000", "smsCode":"123456" }`。`POST /auth/sms-code` 需开发密钥和 phone，返回 `{ "sent":false, "demo":true, "smsCode":"123456" }`；不发送真实短信。

### 推荐接入顺序

1. 登录并保存 token。
2. `GET /users/me`。
3. 新用户 `POST /users/me/questionnaire`，请求 `{ "answers": {"q1":"crooked","q2":"no"} }`，响应 `{ "recommendedDoctorId":"d_001" }`。
4. `GET /doctors/recommend`，展示标注为演示的医生。
5. 用户确认后 `POST /doctors/d_001/bind`，返回咨询疗程并记录审计。
6. `GET /treatments/current`、`GET /checkins/today`、`GET /posts`、`GET /messages` 填充页面。

## 用户 医生与疗程

| 方法 | 路径 | 返回或行为 |
| --- | --- | --- |
| GET | `/users/me` | `{id,nickname,phone,avatar,isMember,hasBoundDoctor,tenantId,organizationId,clinicId,roles}`；手机号掩码 |
| POST | `/users/me/questionnaire` | 保存 `answers` 对象；固定演示推荐 `d_001`，不是医疗建议 |
| GET | `/questionnaire/template` | `{version:"frontend-v0.1",questions:[],source:"frontend"}`；题目继续由前端维护 |
| GET | `/doctors/recommend` | `Doctor[]`，演示医生 `d_001`、`d_002` |
| GET | `/doctors/{id}` | Doctor，字段对齐前端设计，`verified:false`、`isDemo:true` |
| POST | `/doctors/{id}/bind` | Treatment；同一医生重复绑定返回已有疗程，换医生返回409 |
| GET | `/treatments/current` | 当前 Treatment 或 `null` |
| GET | `/treatments/mine` | 当前用户全部演示疗程数组 |
| GET | `/treatments/{id}` | 本人疗程，其他用户资源返回404 |

新建疗程是 `consulting`，`currentStep`、`totalSteps` 为0；未提供治疗计划编辑接口。日期兼容前端 `yyyy.MM.dd` 字符串。医生关系保存于疗程 `doctor` 与 `members` 中，不写入用户表。绑定和咨询产生异步 Odoo CRM 商业事件，Odoo 不可用时仍能完成患者端操作。

## 打卡

| 方法 | 路径 | 入参与行为 |
| --- | --- | --- |
| GET | `/checkins/today` | CheckinStatus |
| POST | `/checkins` | `{type?:"wear"或"remove",imageKey?:"..."}` |
| GET | `/checkins/records` | 分页本人事件数组，字段 `{id,type,imageKey,time,date,epoch}` |

先绑定医生。首次事件必须为 `wear`，之后 wear/remove 交替；省略 type 时服务端自动选择下一事件。可使用 `Idempotency-Key`（最长128字符）；相同键和相同 JSON 返回上次结果，不重复计时；相同键但不同 JSON 返回409。重试时保持字段顺序不变。imageKey 必须是本人已上传的 `checkin_photo`。

```json
{"todayWearMinutes":0,"targetMinutes":1200,"continuousDays":1,"checkedToday":true,"periodScore":0,"scoreChangePercent":0,"periodStart":"2026.10.05","periodEnd":"","scoreMode":"demo_today_ratio"}
```

佩戴分钟按真实事件区间计算，日边界为 `Asia/Shanghai`；连续天数按有事件的日期计算。演示评分仅为今日佩戴分钟与1200分钟目标的比例，不是已验证的医疗依从性评分；未计算上期变化。生产评分和医生审核待后续实现。

## 内容与消息

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/posts?topic=all&page=1&pageSize=10` | Post[]；topic 支持 all/doctor/patient/science/case/diary/mutual |
| GET | `/posts/search?keyword=演示` | 按标题检索 |
| GET | `/posts/{id}` | 帖子详情 |
| POST | `/posts/{id}/like` | 幂等点赞 `{ok:true}`，重复请求不重复计数 |
| GET | `/messages?page=1&pageSize=20` | 本人 AppMessage[] |
| POST | `/messages/{id}/read` | `{ok:true}`，跨用户返回404 |
| POST | `/messages/read-all` | `{ok:true}` |

Doctor/Post/Message 字段沿用所提供前端设计。种子内容、评分与医生身份明确标记为演示；图片地址为空，前端应使用已有占位图。评论、医生私信、内容审核与微信订阅消息尚未实现。

## 磁盘对象存储模拟

这是服务端磁盘存储 API，兼容前端获取凭证后上传的流程，不是完整阿里云 OSS 服务。不提供真实 STS、S3 协议、跨地域副本或 CDN。开发版文件流量仍经过 VPS 的 Caddy 与平台进程；生产切换 OSS/S3 适配器后业务继续使用 objectKey。

### 获取凭证

`POST /media/upload-token`：

```json
{"scene":"ai_photo","ext":"png"}
```

scene 为 `checkin_photo`、`ai_photo`、`avatar`；ext 为 jpg/jpeg/png/webp，默认jpg。返回包含：

```json
{
  "uploadUrl":"https://app.smilelab.ai/storage/objects/media_<id>?expires=<epoch>&signature=<signature>",
  "objectKey":"chipmunk-private/tenant_demo/u_<id>/ai_photo/media_<id>.png",
  "accessKeyId":"disk-demo","policy":"<base64-policy>","signature":"<signature>",
  "expiresIn":300,"maxSize":10485760,"method":"POST","storageProvider":"disk-mock",
  "formData":{"key":"<objectKey>","OSSAccessKeyId":"disk-demo","policy":"<policy>","signature":"<signature>","success_action_status":"200"}
}
```

上传能力由 uploadUrl 内短期签名授权，不再要求 bearer token。必须完整保留查询参数；policy/accessKeyId 是本地兼容字段，不是真实云凭据。

### 小程序上传

```typescript
const grant = await api.post('/media/upload-token', { scene: 'ai_photo', ext: 'jpg' })
const result = await new Promise<any>((resolve, reject) => {
  wx.uploadFile({
    url: grant.uploadUrl,
    filePath: localPhotoPath,
    name: 'file',
    formData: grant.formData,
    success(res) {
      const body = JSON.parse(res.data)
      if (res.statusCode === 200 && body.code === 0) resolve(body.data)
      else reject(body)
    },
    fail: reject
  })
})
await api.post('/ai/simulations', { imageKey: result.objectKey })
```

也支持 `PUT uploadUrl` 直接上传原始二进制。成功响应 `{code:0,message:"ok",data:{objectKey,mediaId,size,sha256,url}}`。数据库只存元数据，文件位于 `chipmunk_media_objects` Docker 卷。每文件最大10MiB，每用户100MiB，磁盘余量低于10GiB时拒绝新凭证。签名/扩展名不匹配拒绝；只校验文件头，不替代完整解码、恶意文件扫描或临床图像质检。

原对象不可覆盖，重复上传返回409；重新拍照应申请新 objectKey。AI/打卡只能引用本人已完成上传的对象。私有目录不通过 Caddy file_server 暴露。下载 URL 有效15分钟，持有链接者在有效期内可读，应按凭据保护。`GET /media/{mediaId}/url` 携带 bearer token 刷新本人下载链接。

## AI 模拟

`POST /ai/simulations` 请求 `{imageKey}`（本人 `ai_photo`），返回 `{taskId,isMock:true}`。

`GET /ai/simulations/{taskId}` 约3秒内返回 analyzing，随后 done；800毫秒轮询即可。此处是按任务创建时间推进的演示状态机，不执行模型、图像质量判断、队列或 GPU 工作。

```json
{"taskId":"simulation_<id>","status":"done","progress":100,"qualityChecks":[],"resultText":"开发演示：未进行AI分析，前后图为同一原图。","beforeImage":"<signed-url>","afterImage":"<same-original-signed-url>","isMock":true}
```

前后图均指向原图；前端必须展示开发演示标识，不能展示真实诊断或治疗效果。`GET /ai/simulations/mine?page=1` 为本人历史。每日每用户最多20次成功任务创建。持久化任务队列已实现，内部 jobStatus 支持 queued/running/succeeded/failed/cancelled；真实 AI Gateway 与 GPU Worker 待接入。POST /ai/simulations/{taskId}/cancel 取消未完成任务。

## 咨询与预留功能

`POST /consultations` 创建本人的咨询记录 `{id,status:"requested"}`，异步写入 Odoo CRM 商业线索。只同步商业标题、平台引用，不传手机号、健康问卷、照片或病历。

预约 `/appointments/mine` 和病历 `/patients/me/records` 已实现真实工作流，详见后面的 DSO 开发文档。以下 GET 路径仍提供空数组：`/reports/mine`、`/coupons/mine`、`/mall/products`。它们不表示完整业务已实现。`/invitations/mine` 返回 `{inviteCode,invitedCount:0,rewards:[],enabled:false}`。`POST /memberships/open` 返回501，不创建会员或模拟付款。

## Odoo 边界与交付范围

Odoo 19 Community，独立 PostgreSQL 与数据卷；安装 CRM、Sales、Purchase、Inventory、HR、Invoicing 及 `chipmunk_bridge`。Enterprise 工资、订阅及高级会计功能不在本次交付范围；本地化财务配置需后续评估。

平台通过内部 `POST http://odoo:8069/chipmunk/integration/events` 同步 `crm.lead.created`，该接口在公网 Caddy 上返回404。服务身份密钥仅保存在服务器，患者前端不访问 Odoo、不持有集成密钥。适配器把演示租户固定映射到 Smilelab Demo 公司。

平台 outbox 与业务记录在同一事务写入；后台自动重试，最多10次后进入 dead_letter。`integration_mapping` 保存平台ID与OdooID，Odoo用事件ID保证重复事件不创建重复线索。双向同步、销售报价同步和多公司动态映射尚未实现。

## 本地与运维

平台源码 `platform/` 使用 ASP.NET Core 10 + Npgsql，为隔离的 REST v1 开发宿主；现有 `api/Admin.Api` 与旧 `App-*` 签名协议保留，不会被此宿主替换或自动迁移。原前端文档要求的 Bearer `/api/v1` 协议与旧协议不同，开发者应明确选用此文档。

本次采用独立 `platform_resource` JSONB 资源表快速实现演示领域，并按租户、门诊、所有者建索引；这是初期开发模型，不是全球设计中所有独立领域库的完成版本。未来拆分应保留稳定平台ID、权限域、对象键与 outbox 协议。

生产前必须补齐真实身份、角色审批、完整临床权限、真实AI、审计保留策略与备份异地副本。当前每日备份脚本提供数据库和文件卷快照，已验证数据库恢复和媒体字节哈希，未提供PITR。此环境只使用测试数据。

## 请求示例

PowerShell 7 / Windows PowerShell 均可使用：

```powershell
$devKey = Read-Host '输入单独交付的开发密钥'
$login = Invoke-RestMethod -Method Post -Uri 'https://app.smilelab.ai/api/v1/auth/mp-login' -ContentType 'application/json' -Headers @{'X-Dev-Key'=$devKey} -Body '{"code":"demo:frontend-alice"}'
$headers = @{Authorization=('Bearer ' + $login.data.token)}
Invoke-RestMethod -Uri 'https://app.smilelab.ai/api/v1/users/me' -Headers $headers
```

不要直接在 PowerShell 5.1 粘贴 Linux 的 `&&` 或 `rm -rf`。服务器操作先 `ssh dcad@dcad.ai` 再运行 Linux 命令。


---

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
