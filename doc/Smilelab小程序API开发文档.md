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

前后图均指向原图；前端必须展示开发演示标识，不能展示真实诊断或治疗效果。`GET /ai/simulations/mine?page=1` 为本人历史。每日每用户最多20次成功任务创建。生产 AI Gateway、任务队列、质量失败流程与 GPU Worker 待接入。

## 咨询与预留功能

`POST /consultations` 创建本人的咨询记录 `{id,status:"requested"}`，异步写入 Odoo CRM 商业线索。只同步商业标题、平台引用，不传手机号、健康问卷、照片或病历。

以下 GET 路径已提供空数组，方便尚未完成页面联调：`/appointments/mine`、`/patients/me/records`、`/reports/mine`、`/coupons/mine`、`/mall/products`。它们不表示完整业务已实现。`/invitations/mine` 返回 `{inviteCode,invitedCount:0,rewards:[],enabled:false}`。`POST /memberships/open` 返回501，不创建会员或模拟付款。

## Odoo 边界与交付范围

Odoo 19 Community，独立 PostgreSQL 与数据卷；安装 CRM、Sales、Purchase、Inventory、HR、Invoicing 及 `chipmunk_bridge`。Enterprise 工资、订阅及高级会计功能不在本次交付范围；本地化财务配置需后续评估。

平台通过内部 `POST http://odoo:8069/chipmunk/integration/events` 同步 `crm.lead.created`，该接口在公网 Caddy 上返回404。服务身份密钥仅保存在服务器，患者前端不访问 Odoo、不持有集成密钥。适配器把演示租户固定映射到 Smilelab Demo 公司。

平台 outbox 与业务记录在同一事务写入；后台自动重试，最多10次后进入 dead_letter。`integration_mapping` 保存平台ID与OdooID，Odoo用事件ID保证重复事件不创建重复线索。双向同步、销售报价同步和多公司动态映射尚未实现。

## 本地与运维

平台源码 `platform/` 使用 ASP.NET Core 10 + Npgsql，为隔离的 REST v1 开发宿主；现有 `api/Admin.Api` 与旧 `App-*` 签名协议保留，不会被此宿主替换或自动迁移。原前端文档要求的 Bearer `/api/v1` 协议与旧协议不同，开发者应明确选用此文档。

本次采用独立 `platform_resource` JSONB 资源表快速实现演示领域，并按租户、门诊、所有者建索引；这是初期开发模型，不是全球设计中所有独立领域库的完成版本。未来拆分应保留稳定平台ID、权限域、对象键与 outbox 协议。

生产前必须补齐真实身份、角色审批、完整临床权限、真实AI、审计保留策略、备份异地副本与恢复演练。当前每日备份脚本提供数据库和文件卷快照，未提供PITR。此环境只使用测试数据。

## 请求示例

PowerShell 7 / Windows PowerShell 均可使用：

```powershell
$devKey = Read-Host '输入单独交付的开发密钥'
$login = Invoke-RestMethod -Method Post -Uri 'https://app.smilelab.ai/api/v1/auth/mp-login' -ContentType 'application/json' -Headers @{'X-Dev-Key'=$devKey} -Body '{"code":"demo:frontend-alice"}'
$headers = @{Authorization=('Bearer ' + $login.data.token)}
Invoke-RestMethod -Uri 'https://app.smilelab.ai/api/v1/users/me' -Headers $headers
```

不要直接在 PowerShell 5.1 粘贴 Linux 的 `&&` 或 `rm -rf`。服务器操作先 `ssh dcad@dcad.ai` 再运行 Linux 命令。
