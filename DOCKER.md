# CNC_AgentCore (.NET 10) —— Docker/Linux 发布手册

独立自包含栈：`db`(pgvector/pgvector:pg17) → `db-init`(一次性 psql 迁移) → `api`(.NET Web API)。
app 不自动建库/迁移，schema 由 `db-init` 依序应用 `db/migrations/001..007.sql`，成功后 api 才启动。
配置不用本仓库开发 `.env`，统一走 `.env.docker`。

## 快速开始

```bash
# 1) 准备配置（填真密钥：PG_SUPERPASSWORD / API Key / JWT_SECRET）
cp .env.docker.example .env.docker

# 2) 构建并启动（首次需联网 nuget.org 还原）
docker compose --env-file .env.docker up -d --build

# 3) 查看状态 / 日志
docker compose --env-file .env.docker ps          # db healthy / db-init exited 0 / api healthy
docker compose --env-file .env.docker logs db-init   # 应显示 apply 001..007
docker compose --env-file .env.docker logs -f api
```

## 验证

```bash
curl http://localhost:8000/                       # {"name":"CNC_AgentCore",...}
curl http://localhost:8000/health                 # db/llm/embedding/rerank 探针（缺 key→skipped）

# schema 是否就绪
docker compose --env-file .env.docker exec db psql -U postgres -d cnc_kb -c "\dt kb.*"
docker compose --env-file .env.docker exec db psql -U postgres -d cnc_kb -c "select 1 from ops.role_permissions limit 1"
```

## 登录账号与数据（重要）

schema 迁移与 Python 版逐字节一致，**不含用户账号行**（006 只建 `ops.users` 表，007 只种权限矩阵）。
要让 `.NET` 栈可直接登录，两种方式任选：

1. 用同源 Python 项目的种子灌一次（两后端 PBKDF2 哈希格式兼容）：
   在 `D:\project\CNC_Agent` 先 `docker compose --env-file .env.docker build backend`，再对着**本栈**的库执行：
   ```bash
   # 例子：把 A 镜像挂到 B 的 compose 网络，种子连到 B 的 db 服务（替换成实际项目/网络名）
   docker run --rm \
     --network <cnc_agentcore_default> \
     -e PG_HOST=db -e PG_PORT=5432 -e PG_SUPERUSER=postgres \
     -e PG_SUPERPASSWORD=<真密码> -e PG_DB=cnc_kb \
     --entrypoint python cnc-kb-python-backend:latest scripts/seed_users.py
   ```
2. 真迁移场景：把 `api` 的 DSN 指向已填充的库（如现有 Python 栈），二者 schema 相同可直接复用数据——
   在 `.env.docker` 里覆盖 `PG_CONNECTION_STRING`（同时让 db/db-init 指向外部即可）。

## 停止 / 清理

```bash
docker compose --env-file .env.docker down          # 停止，保留数据卷(pgdata)
docker compose --env-file .env.docker down -v       # 连数据卷一起删（数据丢失！）
docker compose --env-file .env.docker run --rm db-init   # 需要时手动重放迁移(幂等)
```

## 多栈共存 / 上线

与 Python 版同时跑：把 `.env.docker` 的 `BACKEND_PORT` 改不同端口（如 8001）。本 compose 未设顶层 `name:`，互不干扰。

```bash
docker build -t <registry>/cnc-agentcore-api:<tag> --platform linux/amd64 .
docker push <registry>/cnc-agentcore-api:<tag>
# 服务器：放好 .env.docker 后
REGISTRY=<registry>/ TAG=<tag> docker compose --env-file .env.docker up -d
```

> 备份：`docker exec <容器> pg_dump -U postgres cnc_kb | gzip > backup.sql.gz`
