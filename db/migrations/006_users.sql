-- 006_users.sql —— V1.5 用户与权限
--
-- 设计目标（按甲方决策）：
--   1. 认证：JWT (HS256) + PBKDF2-SHA256 密码哈希
--   2. 三角色：admin / operator / viewer
--   3. 权限粒度：页面可见性 + 关键操作都控
--   4. 一张表搞定：ops.role_permissions(role, page_code, can_access, actions[])
--
-- 表说明：
--   ops.users                 用户主表（密码哈希自含 salt + iter，零依赖）
--   ops.role_permissions      角色 × 页面 → (can_access, actions[])
--
-- 默认账号 + 默认权限矩阵由 006_seed_data.sql 单独提供（密码哈希需 Python 计算），
-- 此处只建表 + 表注释 + 索引。

-- ===== 用户主表 =====
CREATE TABLE IF NOT EXISTS ops.users (
    id              BIGSERIAL PRIMARY KEY,
    username        VARCHAR(64)  NOT NULL UNIQUE,           -- 登录名，区分大小写
    display_name    VARCHAR(128) NOT NULL,                   -- 顶栏 + 个人信息展示
    password_hash   VARCHAR(256) NOT NULL,                   -- 格式 "pbkdf2_sha256$<iter>$<salt_b64>$<hash_b64>"
    role            VARCHAR(32)  NOT NULL
                    CHECK (role IN ('admin','operator','viewer')),
    is_active       BOOLEAN      NOT NULL DEFAULT true,      -- 软删除 / 停用
    last_login_at   TIMESTAMPTZ,                             -- 用于审计 + "最近活跃"
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ  NOT NULL DEFAULT now(),
    created_by      VARCHAR(64)                             -- 哪个用户创建的（V1 留字符串，登录态没强制）
);

COMMENT ON TABLE  ops.users IS '系统用户主表；密码哈希自含 salt+iter，零额外依赖';
COMMENT ON COLUMN ops.users.password_hash IS '格式 pbkdf2_sha256$<iterations>$<salt_b64>$<hash_b64>；由 app/core/security.py 编解码';

CREATE INDEX IF NOT EXISTS idx_users_role_active ON ops.users (role, is_active);
CREATE INDEX IF NOT EXISTS idx_users_last_login   ON ops.users (last_login_at DESC NULLS LAST);

-- ===== 权限矩阵（单表设计：role + page_code 联合唯一）=====
CREATE TABLE IF NOT EXISTS ops.role_permissions (
    id          BIGSERIAL    PRIMARY KEY,
    role        VARCHAR(32)  NOT NULL
                CHECK (role IN ('admin','operator','viewer')),
    page_code   VARCHAR(64)  NOT NULL,                       -- chat / knowledge / entry / ...
    can_access  BOOLEAN      NOT NULL DEFAULT true,          -- 页面是否对该角色可见
    actions     TEXT[]       NOT NULL DEFAULT '{}',          -- 该页面下允许的动作码（如 alarms.create）
    updated_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_by  VARCHAR(64),
    UNIQUE (role, page_code)
);

COMMENT ON TABLE  ops.role_permissions IS '角色权限矩阵：单表同时承载页面可见性 (can_access) 与动作权限 (actions[])';
COMMENT ON COLUMN ops.role_permissions.actions IS '动作码集合；前端 canDoAction() 查询；后端装饰器校验';

CREATE INDEX IF NOT EXISTS idx_role_perm_role ON ops.role_permissions (role);