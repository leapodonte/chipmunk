# 第三方组件与许可证记录

本文件记录本轮 DSO 组件复用，不改变本仓库自有代码的许可。精确依赖版本见 `package-lock.json`、`platform/Chipmunk.Platform.csproj` 和 Dockerfile 中的镜像摘要。

| 组件 | 本轮版本 | 许可 / 原始证据 | 使用方式 |
| --- | --- | --- | --- |
| Vue | 3.5.43 | MIT，npm 包原始 LICENSE | 工作台运行时 |
| Vue I18n | 11.4.13 | MIT，npm 包原始 LICENSE | 简体中文/英语 |
| Ant Design Vue | 4.2.6 | [MIT](https://github.com/vueComponent/ant-design-vue/blob/main/LICENSE) | 表单、表格、弹窗等组件 |
| FullCalendar Vue 标准插件 | 7.1.0 | [MIT](https://github.com/fullcalendar/fullcalendar/blob/main/LICENSE.md) | 标准 timeGrid/dayGrid；未使用 Premium 插件 |
| temporal-polyfill | 1.0.1 | BSD-2-Clause，npm 包许可 | FullCalendar 日期兼容 |
| TypeScript | 5.9.3 | Apache-2.0，npm 包 LICENSE | 构建；固定兼容 vue-tsc 3.3.12 |
| Vite | 8.3.2 | MIT，npm 包 LICENSE | 构建工具 |
| Playwright | 1.63.0 | Apache-2.0，npm 包 LICENSE | 隔离浏览器测试 |
| Npgsql | 10.0.0 | [PostgreSQL License](https://github.com/npgsql/npgsql/blob/main/LICENSE) | PostgreSQL 驱动 |
| SkiaSharp / Linux NativeAssets.NoDependencies | 4.153.1 | [MIT](https://github.com/mono/SkiaSharp/blob/main/LICENSE.md)；原始 native 附属许可随包核对 | 实际图片解码和像素上限验证 |
| Odoo Community | 19.0-20260926 | [模块级许可证](https://github.com/odoo/odoo/tree/19.0) | 独立商业运营模块 |
| 自有 chipmunk_bridge | 19.0.2.0.0 | 模块 manifest 明确 LGPL-3 | 最小商业事件桥接、租户公司映射 |
| openapi-spec-validator | 0.7.2 | [Apache-2.0](https://github.com/python-openapi/openapi-spec-validator/blob/0.7.2/LICENSE) | 仅契约验证工具 |

`tools/generate-third-party-notices.mjs` 从已安装的锁定 npm 包提取许可原文，输出 `backend/public/THIRD_PARTY_NOTICES.txt`，随工作台一起构建并可从 `/workspace/THIRD_PARTY_NOTICES.txt` 下载。清单包括开发工具的依赖；不能把它当作经过裁剪的运行时 SBOM。无包根目录许可原文的组件列在文件末尾，仍须核对其包元数据和上游许可。

OCA queue_job、OpenIddict、Keycloak、Hangfire、Medplum、Firely SDK、AWS SDK 本轮仅研究，未安装到服务。选型理由及对应原始链接见 `doc/DSO复用项目评估与组件决策.md`。
