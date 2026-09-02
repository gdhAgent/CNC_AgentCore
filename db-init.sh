#!/bin/sh
# db-init.sh —— CNC_AgentCore 一次性 schema 应用（容器内运行）
# 依序对 db 跑 db/migrations/001..007.sql（幂等，可安全重跑）。
# 连接参数全部来自环境变量(libpq 命名，由 compose 注入)，不写死凭据。
set -eu

echo "[db-init] wait for PostgreSQL ${PGHOST:-db}:${PGPORT:-5432}/${PGDATABASE:-cnc_kb}"
i=0
until pg_isready -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -q 2>/dev/null; do
    i=$((i+1))
    if [ "$i" -ge 60 ]; then
        echo "[db-init] database not reachable after ~120s" >&2
        exit 1
    fi
    sleep 2
done

for f in /migrations/*.sql; do
    [ -e "$f" ] || continue
    echo "[db-init] apply $(basename "$f")"
    psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" \
         --set ON_ERROR_STOP=1 --single-transaction -f "$f"
done

echo "[db-init] schema applied (kb/ops/log + base_items + role_permissions)."
echo "[db-init] 注：迁移不含用户账号行；登录账号需另跑 CNC_Agent 的 scripts/seed_users.py（哈希与 .NET 兼容）。"
