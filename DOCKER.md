# CNC_AgentCore (.NET 10) —— Docker/Linux 发布手册

独立自包含栈：`db`(pgvector/pgvector:pg17) → `api`(.NET Web API)。
**schema 与演示主数据**由 db 服务在**空数据卷首次初始化**时自动导入 `db/cnc_kb.sql`（单文件：建表 + 演示主数据），导入完成、db 健康后 api 才启动。
配置不用本仓库开发 `.env`，统一走 `.env.docker`。

## 快速开始

```bash
# 1) 准备配置（填真密钥：PG_SUPERPASSWORD / API Key / JWT_SECRET）
cp .env.docker.example .env.docker

# 2) 构建并启动（首次需联网 nuget.org 还原）
docker compose --env-file .env.docker up -d --build

# 3) 查看状态 / 日志
docker compose --env-file .env.docker ps          # db healthy / api healthy
docker compose --env-file .env.docker logs db     # 首启可见导入 db/cnc_kb.sql
docker compose --env-file .env.docker logs -f api
```

## 验证

```bash
curl http://localhost:8000/                       # {"name":"CNC_AgentCore",...}
curl http://localhost:8000/health                 # db/llm/embedding/rerank 探针（缺 key→skipped）

# 演示主数据是否已导入（应分别 ≈ 25 / 30 / 200）
docker compose --env-file .env.docker exec db psql -U postgres -d cnc_kb -t -c \
  "select 'alarms='||(select count(*) from kb.alarms)||' machines='||(select count(*) from ops.machines)||' workorders='||(select count(*) from ops.maintenance_logs)"
```

## 登录账号（重要）

`db/cnc_kb.sql` 含演示主数据、**不含登录账号**（避免仓库携带任何凭据）。两版密码哈希兼容，可用配套 **Python 版**脚本创建演示账号（一次即可，幂等）：

```bash
# 1) 在 CNC_Agent 仓库先构建 backend 镜像： docker compose --env-file .env.docker build backend
# 2) 对着本栈的库执行（替换 <项目网络名> 与真实密码，例如 --network cnc_agentcore_default）
docker run --rm \
  --network <网络名，如 cnc_agentcore_default> \
  -e PG_HOST=db -e PG_PORT=5432 -e PG_SUPERUSER=postgres \
  -e PG_SUPERPASSWORD=<真密码> -e PG_DB=cnc_kb \
  --entrypoint python cnc-kb-python-backend:latest scripts/seed_users.py
```

> 若不需要登录、只想看查询/检索界面：业务查询端点默认开放，主界面可直接使用演示数据。

## 停止 / 清理

```bash
docker compose --env-file .env.docker down          # 停止，保留数据卷(pgdata)
docker compose --env-file .env.docker down -v       # 连数据卷一起删（数据丢失！）
```

> 想重建到初始演示状态：`down -v` 后再 `up`，db 会重新导入 `db/cnc_kb.sql`。

## 上线

```bash
docker build -t <registry>/cnc-agentcore-api:<tag> --platform linux/amd64 .
docker push <registry>/cnc-agentcore-api:<tag>
# 服务器：放好 .env.docker 后
REGISTRY=<registry>/ TAG=<tag> docker compose --env-file .env.docker up -d
```

> 备份：`docker exec <容器> pg_dump -U postgres cnc_kb | gzip > backup.sql.gz`
