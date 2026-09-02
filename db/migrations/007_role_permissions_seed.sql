-- 007_role_permissions_seed.sql —— 默认权限矩阵（admin / operator / viewer × 10 页）
--
-- 三角色口径：
--   admin     全部页面 + 全部动作（包括 users.manage / permissions.manage）
--   operator  业务页（chat/knowledge/entry/workorders 等）+ 可编辑；
--             无 base-data 入口（不能改字典 / 用户 / 权限）
--   viewer    只读视图（chat / trace / logs / dashboard / workorders 列表 / vectors 视图）
--             无任何修改动作；可点 feedback
--
-- 动作码命名约定：<domain>.<verb>，例如 alarms.create / documents.upload / workorders.delete
-- 不强制按 domain 拆表，单表 TEXT[] 已够用

-- ============================================================
-- admin：全权限（10 页 × 全动作）
-- ============================================================
INSERT INTO ops.role_permissions (role, page_code, can_access, actions) VALUES
    ('admin', 'chat',        true, ARRAY['view','query','feedback']),
    ('admin', 'knowledge',   true, ARRAY['view','documents.upload','documents.delete','chunks.view']),
    ('admin', 'entry',       true, ARRAY['view','alarms.create','alarms.edit','alarms.delete','faqs.create','faqs.edit','faqs.delete','import.template','export']),
    ('admin', 'trace',       true, ARRAY['view']),
    ('admin', 'logs',        true, ARRAY['view','feedback']),
    ('admin', 'suggestions', true, ARRAY['view','suggestions.approve','suggestions.reject','suggestions.resolve']),
    ('admin', 'dashboard',   true, ARRAY['view']),
    ('admin', 'workorders',  true, ARRAY['view','workorders.create','workorders.edit','workorders.delete']),
    ('admin', 'vectors',     true, ARRAY['view','vectors.vectorize']),
    ('admin', 'base-data',   true, ARRAY['view','base_items.edit','users.manage','permissions.manage'])
ON CONFLICT (role, page_code) DO UPDATE SET
    can_access = EXCLUDED.can_access,
    actions    = EXCLUDED.actions,
    updated_at = now();

-- ============================================================
-- operator：业务可编辑，禁 base-data
-- ============================================================
INSERT INTO ops.role_permissions (role, page_code, can_access, actions) VALUES
    ('operator', 'chat',        true,  ARRAY['view','query','feedback']),
    ('operator', 'knowledge',   true,  ARRAY['view','documents.upload','documents.delete','chunks.view']),
    ('operator', 'entry',       true,  ARRAY['view','alarms.create','alarms.edit','alarms.delete','faqs.create','faqs.edit','faqs.delete','import.template','export']),
    ('operator', 'trace',       true,  ARRAY['view']),
    ('operator', 'logs',        true,  ARRAY['view','feedback']),
    ('operator', 'suggestions', true,  ARRAY['view','suggestions.approve','suggestions.reject','suggestions.resolve']),
    ('operator', 'dashboard',   true,  ARRAY['view']),
    ('operator', 'workorders',  true,  ARRAY['view','workorders.create','workorders.edit']),
    ('operator', 'vectors',     true,  ARRAY['view']),
    ('operator', 'base-data',   false, ARRAY[]::TEXT[])
ON CONFLICT (role, page_code) DO UPDATE SET
    can_access = EXCLUDED.can_access,
    actions    = EXCLUDED.actions,
    updated_at = now();

-- ============================================================
-- viewer：只读视图（chat / trace / logs / dashboard / workorders 列表 / vectors 视图）
-- ============================================================
INSERT INTO ops.role_permissions (role, page_code, can_access, actions) VALUES
    ('viewer', 'chat',        true,  ARRAY['view','query','feedback']),
    ('viewer', 'knowledge',   true,  ARRAY['view','chunks.view']),
    ('viewer', 'entry',       false, ARRAY[]::TEXT[]),
    ('viewer', 'trace',       true,  ARRAY['view']),
    ('viewer', 'logs',        true,  ARRAY['view','feedback']),
    ('viewer', 'suggestions', true,  ARRAY['view']),
    ('viewer', 'dashboard',   true,  ARRAY['view']),
    ('viewer', 'workorders',  true,  ARRAY['view']),
    ('viewer', 'vectors',     true,  ARRAY['view']),
    ('viewer', 'base-data',   false, ARRAY[]::TEXT[])
ON CONFLICT (role, page_code) DO UPDATE SET
    can_access = EXCLUDED.can_access,
    actions    = EXCLUDED.actions,
    updated_at = now();