-- 005_base_items.sql —— W4 UI 优化批 9：基础数据维护（kb.base_items）
--
-- 把硬编码的 brand / category / severity / fault_type 下拉数据迁入数据库
-- 由前端管理页（/admin/base-data）CRUD 维护，业务侧只读
--
-- 字段：
--   kind        枚举：brand / category / severity / fault_type
--   code        同 kind 下的唯一标识（API 兼容用，不建议改）
--   label_zh    中文显示名
--   label_en    英文显示名（前端展示用 "label_en (label_zh)"）
--   sort_order  排序权重，越小越靠前
--   is_active   是否启用（软删除 = false）

CREATE TABLE IF NOT EXISTS kb.base_items (
    id            BIGSERIAL PRIMARY KEY,
    kind          VARCHAR(24)  NOT NULL
                  CHECK (kind IN ('brand','category','severity','fault_type')),
    code          VARCHAR(64)  NOT NULL,
    label_zh      VARCHAR(128) NOT NULL,
    label_en      VARCHAR(128) NOT NULL,
    sort_order    INT          NOT NULL DEFAULT 100,
    is_active     BOOLEAN      NOT NULL DEFAULT true,
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    UNIQUE (kind, code)
);

CREATE INDEX IF NOT EXISTS idx_base_items_kind_active ON kb.base_items (kind, is_active, sort_order);

COMMENT ON TABLE  kb.base_items IS '基础数据字典（品牌/类别/严重度/故障类型），由 /admin/base-data 维护';
COMMENT ON COLUMN kb.base_items.code IS '同 kind 下唯一标识（API 兼容用，谨慎改）';
COMMENT ON COLUMN kb.base_items.label_en IS '英文显示名（前端展示用 "label_en (label_zh)"）';

-- ===== 种子数据（idempotent）=====

-- 品牌
INSERT INTO kb.base_items (kind, code, label_zh, label_en, sort_order) VALUES
    ('brand', 'FANUC',       '发那科',     'FANUC',       10),
    ('brand', 'MITSUBISHI',   '三菱',       'MITSUBISHI',  20),
    ('brand', 'SIEMENS',      '西门子',     'SIEMENS',     30),
    ('brand', 'HEIDENHAIN',   '海德汉',     'HEIDENHAIN',  40),
    ('brand', 'GENERIC',      '通用',       'GENERIC',     50)
ON CONFLICT (kind, code) DO UPDATE
    SET label_zh = EXCLUDED.label_zh,
        label_en = EXCLUDED.label_en,
        sort_order = EXCLUDED.sort_order,
        is_active = true,
        updated_at = now();

-- 报警码类别
INSERT INTO kb.base_items (kind, code, label_zh, label_en, sort_order) VALUES
    ('category', 'servo',      '伺服',          'servo',          10),
    ('category', 'spindle',    '主轴',          'spindle',        20),
    ('category', 'pmc',        'PMC',           'PMC',            30),
    ('category', 'overtravel', '超程',          'overtravel',     40),
    ('category', 'program',    '程序错误',      'program',        50),
    ('category', 'hydraulic',  '液压',          'hydraulic',      60),
    ('category', 'pneumatic',  '气动',          'pneumatic',      70),
    ('category', 'other',      '其他',          'other',         999)
ON CONFLICT (kind, code) DO UPDATE
    SET label_zh = EXCLUDED.label_zh,
        label_en = EXCLUDED.label_en,
        sort_order = EXCLUDED.sort_order,
        is_active = true,
        updated_at = now();

-- 严重度
INSERT INTO kb.base_items (kind, code, label_zh, label_en, sort_order) VALUES
    ('severity', 'info',    '提示',   'info',    10),
    ('severity', 'warning', '警告',   'warning', 20),
    ('severity', 'fault',   '故障',   'fault',   30),
    ('severity', 'fatal',   '严重',   'fatal',   40),
    ('severity', 'unknown', '未知',   'unknown', 50)
ON CONFLICT (kind, code) DO UPDATE
    SET label_zh = EXCLUDED.label_zh,
        label_en = EXCLUDED.label_en,
        sort_order = EXCLUDED.sort_order,
        is_active = true,
        updated_at = now();

-- 工单故障类型
INSERT INTO kb.base_items (kind, code, label_zh, label_en, sort_order) VALUES
    ('fault_type', '机械',  '机械故障',  'Mechanical',  10),
    ('fault_type', '电气',  '电气故障',  'Electrical',  20),
    ('fault_type', '液压',  '液压故障',  'Hydraulic',   30),
    ('fault_type', '气动',  '气动故障',  'Pneumatic',   40),
    ('fault_type', '软件',  '软件故障',  'Software',    50)
ON CONFLICT (kind, code) DO UPDATE
    SET label_zh = EXCLUDED.label_zh,
        label_en = EXCLUDED.label_en,
        sort_order = EXCLUDED.sort_order,
        is_active = true,
        updated_at = now();