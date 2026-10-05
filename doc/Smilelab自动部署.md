# Smilelab 推送自动发布

源码仓库 https://git.dcad.ai/dcad/smilelab 的 `main` 是开发/演示发布分支。推送 main 自动运行 `.forgejo/workflows/deploy.yml`：SDK、生成契约、OpenAPI、完整镜像、隔离平台数据库、浏览器及真实 Odoo 桥接检查全部通过后，部署这个已验证的镜像。`codex/**` 只验证；其他分支不自动发布。也可在 Actions 手动重跑 main。失败检查阻止发布。

在已有本机仓库中：

```bash
git push smilelab HEAD:main
```

新开发者可以克隆仓库，检出 main 后正常推送：

```bash
git clone https://git.dcad.ai/dcad/smilelab.git
cd smilelab
git push origin main
```

查看运行状态：https://git.dcad.ai/dcad/smilelab/actions 。整个发布不因后续推送而中途取消，独立运行容量为1。服务端再次核对 Forgejo main：过期提交跳过，不能覆盖较新发布。版本和商业状态仍由平台数据库维护，源码推送不重置患者或订单数据。

## 发布过程

CI 在专属容器和独立 Docker-in-Docker daemon 内运行，未挂载 VPS Docker socket、运行数据卷、`/srv/dcad` 或运行秘密。只使用合成数据，测试完成清理临时容器和卷。runner 与 dind 共用容器网络和 CI 工作卷，所以测试的临时 loopback 端口与只读源码绑定可用；无 CI 主机端口。

main 的成功检查通过受限 SSH 通道传送验证镜像。密钥只能执行基础设施的 `forgejo-deploy-smilelab.py`；拒绝 shell、任意参数、端口转发和其他仓库。服务端从 Forgejo 归档同一提交的源码，校验归档配置 SHA256、每个文件系统层 SHA256、revision/source 标签及导入后的运行配置和层，再调用规范 `deploy-smilelab.sh`。Docker28/29导入会规范化旧配置、改变本地镜像ID，元数据因此同时记录CI配置digest和主机ID；不会重建另一份产物。发布前保存完整一致快照，升级自有 Odoo 桥接并等待平台健康；保留管理员密码和员工密钥。付款继续为 demo。

发布后自动检查两域名 HTTPS、工作台、订单契约、八员工角色、受保护的 ERP 路由和邻接服务。这些检查不创建患者、订单或临床/商业记录。测试失败无发布；发布升级失败保留红色运行和服务器日志/快照，不盲目回退已迁移的数据库。

## 运维入口

- 专属服务配置：dcad-infra 的 `compose/smilelab-ci.yml`；注册信息仅在私有 runner 数据卷，runner 只注册到此仓库。
- CI daemon 上限：2 CPU / 5 GiB；runner：1 CPU / 2 GiB。单任务串行，内层测试也有各自限额。历史 CI 镜像和缓存清理约一周前未使用内容。
- 最近成功发布：`/srv/dcad/work/smilelab-ci/current.json`；日志：该目录 `logs/`。
- 完整快照：`/srv/dcad/backups/smilelab/`。恢复应遵循 infra 文档，并同时恢复两库、源码、文件卷、秘密及对应镜像。
- 发布 key 和 SSH 主机公钥在 Forgejo 仓库 Actions secrets，分别为 `SMILELAB_PUBLISH_KEY` 与 `SMILELAB_SSH_KNOWN_HOSTS`；正文不交付或输出密钥。

GitHub 现有验证保留；本部署由 Forgejo main 触发。当前是开发/演示环境，快照仍在同一主机，真实支付、外部物流及工厂设备接入不在此配置范围。
