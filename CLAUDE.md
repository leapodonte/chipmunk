# CLAUDE.md

本文件为 Claude Code (claude.ai/code) 在本仓库中工作时提供指导。

## 项目概述

Chipmunk 是一个牙齿矫正综合平台。根据 `doc/用户需求文档1.0.txt` 与 `doc/开发计划1.0.md`，项目包含平台管理后台（backend）、医生端管理后台（doctor）、患者微信小程序（app），以及后台接口服务（api）。

代码库为中文语境：注释、界面文字、需求文档、错误信息均为中文。新代码请遵循此约定。

## 常用命令

```bash
# 构建整个解决方案
dotnet build api/Admin.sln

# 运行接口服务（开发环境地址见 launchSettings.json：http://localhost:5151）
dotnet run --project api/Admin.Api

# 初始化/同步数据库表结构并写入种子数据（数据库不存在时会自动创建）
dotnet run --project api/Admin.Api -- --init
```

运行依赖（见 `api/Admin.Api/appsettings.json`）：PostgreSQL `127.0.0.1:5432`（数据库 `admin`）、Redis `127.0.0.1:6379`。`--init` 会写入 `admin` 账号（密码 `Admin@12345`）、模块、角色和系统配置。

目前没有测试项目。

## 目录结构

- `api/` — 后台接口服务（**纯 Web API，无 UI**）。ASP.NET Core **net10.0** + FreeSql；后续如需定时任务，使用 **FreeScheduler**。解决方案 `api/Admin.sln`，含三个项目：
  - `api/Admin.Models` — 仅存放 FreeSql 实体类（PostgreSQL），不含业务逻辑。
  - `api/Admin.Services` — 业务服务、DTO（`DTO/`）、`CodeException`、`EncryptorService`、`WechatService`（微信小程序登录）、本地化资源（`Properties/Resources*.resx`）。只依赖 Models。
  - `api/Admin.Api` — Web API 宿主：控制器（`Controllers/Admin/`、`Controllers/App/`）、过滤器（`Filters/`）、会话与日志服务（`Services/`）。
- `app/` — 小程序端，使用 **UniApp** 开发。目前只做微信小程序，后续在同一套代码上扩展 Android 和 iOS。
- `backend/` — 后端管理（平台管理后台前端），**Vue + Ant Design Vue**。
- `doctor/` — 医生端管理前端，**Vue + Ant Design Vue**。规划中，目录尚未创建。
- `doc/` — 需求文档与开发计划（中文）。
- `docker/` — 各服务部署配置（`api/`、`db/` PostgreSQL、`nginx/`、`redis/`，以及 `docker/docker/` 下的 Docker 离线安装包）。注意：部署路径 `/opt/smilecheck/` 与镜像名 `smilecheck/api` 沿用自上一个项目，dockerfile 内容需与新产物名（`Admin.Api`）核对。

**国际化规则**：三个前端（`app/`、`backend/`、`doctor/`）的界面文字必须通过国际化（i18n）展示，只配置两种语言：**简体中文（默认）**和**英语**。

## 架构

### 分层

`Admin.Api (控制器 + 过滤器) → Admin.Services (业务逻辑 + DTO + 本地化资源) → Admin.Models (FreeSql 实体) → PostgreSQL`

Services 层绝不引用 Web 宿主。DTO 统一放在 `Admin.Services/DTO`，由服务层和控制器共用。api 无任何 UI 项目。

### 双模式入口

`api/Admin.Api/Program.cs`（顶层语句写法）根据启动参数行为不同：带 `--init` 时执行 `SystemService.InitSystemAsync()`（对每个实体做 FreeSql CodeFirst `SyncStructure` + 种子数据）后退出；否则启动 Web 应用。Web 模式下服务注册（`IFreeSql`/`RedisClient` 单例、各 Scoped 服务）、请求本地化、CORS 头中间件、`MapControllers` **均已启用**——新增服务记得在 `Program.cs` 注册。

### API 约定（核心模式）

两类 API，按路由和校验过滤器区分（注意路由**没有** `/api` 前缀）：
- `admin/[controller]` — 管理后台，`[AdminCheck]`（`Filters/AdminCheckAttribute.cs`）
- `app/[controller]` — 微信小程序，`[AppCheck]`；登录接口为 `POST app/user/wx-mp-login`，走微信 `jscode2session`

所有控制器都标注 `[ApiController, AdminResponse, AdminCheck]`（或 `AppCheck`）：

1. **请求签名**：非 form 请求必须携带签名头。Admin 端为 `Admin-Signature` / `Admin-Timestamp` / `Admin-Session` / `Admin-RequestId`；App 端为对应的 `App-*` 头。签名 = `HMAC-SHA256(body + timestamp, RequestKey)` 十六进制串（见 `EncryptorService.SignRequestBody`），时间戳 ±15 分钟容差，签名不符抛 CodeException 402。
2. **会话**存于 Redis：管理端 `AdminSessionService`，小程序端 `AppSessionService`（键前缀 `app:sessions`，60 分钟滑动过期），均在宿主 `Services/`。过滤器解析会话后放入 `HttpContext.Items["session"]`，控制器通过 `this.GetSession()` 读取。登录接口在过滤器的 `noCheckingSessionActions_` 白名单中。**已知问题**：`AppCheckAttribute` 校验会话时误用了 `AdminSessionService`，应为 `AppSessionService`（待修复）。
3. **请求**：控制器用 `this.GetRequestAsync<T>()` 解析请求体，反序列化为继承 `Request` 的 DTO 并执行 DataAnnotations 校验（失败抛 CodeException 400）。
4. **响应**：统一返回 `Response<T>` `{ code, msg, data }`，`code == 0` 表示成功。业务错误抛 `CodeException(code, message)`；`AdminResponse` 捕获（其他异常转为 500），经 `AdminLogService` 记录请求/响应后序列化为响应包。
5. **本地化**：用户可见消息使用 `Admin.Services/Properties` 中的 `IStringLocalizer<Resources>`（`Resources.resx` 中性语言 + `Resources.zh-Hans.resx`）——新增消息加在资源文件里，不要硬编码字符串。`Program.cs` 启用了 en-US / zh-Hans 请求本地化，但 `DefaultRequestCulture` 当前为 en-US，与"默认简体中文"的需求不符，接入前端前需调整。

### 数据约定（FreeSql）

- 连接以 `DataType.PostgreSQL` 构建；枚举通过 `Aop.ConfigEntityProperty` 映射为 `int`；启用 `UseJsonMap()`；Web 模式 `UseAutoSyncStructure(false)`（表结构只在 `--init` 时同步）。新建 FreeSql 入口时必须复刻这套配置（参考 `Program.cs`）。
- 实体属性用小写命名，时间列 `created_at` / `updated_at` / `deleted_at` 为 **Unix 秒（`long`）** 而非 DateTime；删除是软删除（写 `deleted_at`）。唯一索引通常附带 `deleted_at`（见 `User` 的写法：`"open_id ASC,deleted_at ASC"`）。枚举属性配中文文档注释（见 `UserType`/`UserState`）。
- 表名/索引名用 `[Table(Name=...)]` / `[Index(...)]` 显式声明。建表时请根据字段的业务属性（查询、排序、唯一性）考虑是否加索引，用 `FreeSql.DataAnnotations.IndexAttribute` 添加。
- **新表检查清单**：在 `Admin.Models` 新增实体后，必须同时：(1) 在 `SystemService.InitSystemAsync()`（`Admin.Services/SystemService.cs`）中增加对应的 `_fsql.CodeFirst.SyncStructure<T>()`；(2) 如该表有默认数据，也在这里写入种子数据。
- `corp` / `corp_department` / `corp_account`（机构/部门/机构账号）实体已存在但未纳入初始化（`InitSystemAsync` 中被注释），启用时记得取消注释并补种子数据。

### 安全

`EncryptorService` 保存共享 HMAC 密钥：密码做双重哈希（先用客户端密钥、再用服务端密钥，见 `EncodePassword`），请求体用 `RequestKey` 签名，系统配置用 `SignKey` 做完整性签名。客户端/服务端密钥的用途不可混用（比对方式为 `EncodePasswordServer(EncodePasswordClient(pwd))`）。

### 已知残留问题

- `Admin.Api.csproj` 中引用的 `Properties/Resources.resx` / `Resources.zh-Hans.resx` 文件并不存在（实际资源在 `Admin.Services/Properties`），csproj 相关条目是无效残留。
- `Admin.Models/Defs.cs` 为空文件占位。
