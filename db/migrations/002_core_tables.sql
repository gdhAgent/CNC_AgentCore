-- 002_core_tables.sql
-- 全部核心表 DDL（一次性建齐，避免运行时 ALTER）
-- 命名规范：所有对象全限定名 schema.table，不依赖 search_path
-- 幂等：CREATE TABLE/INDEX/COMMENT 全部 IF NOT EXISTS

-- =====================================================================
-- kb.term_dict  (工业术语同义词词典，依赖最少，先建)
-- =====================================================================
CREATE TABLE IF NOT EXISTS kb.term_dict (
    id            BIGSERIAL PRIMARY KEY,
    canonical     VARCHAR(128) NOT NULL,            -- 标准词："主轴"
    synonyms      TEXT[]       NOT NULL,            -- {"spindle","主轴头","刀轴","SP"}
    scope         VARCHAR(32)  NOT NULL DEFAULT 'general',
    UNIQUE (canonical, scope)
);
COMMENT ON TABLE kb.term_dict IS '工业术语同义词词典，用于查询扩展与 jieba 自定义词典';

-- =====================================================================
-- ops.machines  (设备台账 - MES 设备主数据)
-- =====================================================================
CREATE TABLE IF NOT EXISTS ops.machines (
    id            BIGSERIAL PRIMARY KEY,
    asset_no      VARCHAR(64)  NOT NULL UNIQUE,
    name          VARCHAR(128) NOT NULL,
    brand         VARCHAR(64)  NOT NULL,
    model         VARCHAR(64),
    controller    VARCHAR(64),
    workshop      VARCHAR(64),
    line_no       VARCHAR(64),
    install_date  DATE,
    status        VARCHAR(16)  NOT NULL DEFAULT 'running'
                  CHECK (status IN ('running','idle','repair','scrapped')),
    spec          JSONB        NOT NULL DEFAULT '{}'::jsonb,
    is_demo       BOOLEAN      NOT NULL DEFAULT false,
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at    TIMESTAMPTZ  NOT NULL DEFAULT now()
);
COMMENT ON TABLE ops.machines IS 'CNC 设备台账，对应 MES 设备主数据';

-- =====================================================================
-- kb.documents  (入库文档元信息)
-- =====================================================================
CREATE TABLE IF NOT EXISTS kb.documents (
    id            BIGSERIAL PRIMARY KEY,
    title         VARCHAR(256) NOT NULL,
    doc_type      VARCHAR(32)  NOT NULL
                  CHECK (doc_type IN ('manual','alarm_table','maintenance_std','sop','faq','other')),
    brand         VARCHAR(64),
    model_scope   TEXT[]       NOT NULL DEFAULT '{}',
    source_file   VARCHAR(512),
    file_hash     CHAR(64)     UNIQUE,
    page_count    INT,
    lang          VARCHAR(8)   NOT NULL DEFAULT 'zh',
    status        VARCHAR(16)  NOT NULL DEFAULT 'pending'
                  CHECK (status IN ('pending','parsing','ready','failed')),
    error_msg     TEXT,
    meta          JSONB        NOT NULL DEFAULT '{}'::jsonb,
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at    TIMESTAMPTZ  NOT NULL DEFAULT now()
);
COMMENT ON TABLE kb.documents IS '知识文档元数据（手册/报警表/SOP/FAQ）';

-- =====================================================================
-- kb.chunks  (父子分块：检索用小块，喂 LLM 用父块)
-- =====================================================================
CREATE TABLE IF NOT EXISTS kb.chunks (
    id            BIGSERIAL PRIMARY KEY,
    doc_id        BIGINT       NOT NULL REFERENCES kb.documents(id) ON DELETE CASCADE,
    parent_id     BIGINT       REFERENCES kb.chunks(id) ON DELETE CASCADE,
    level         SMALLINT     NOT NULL DEFAULT 1,
    seq           INT          NOT NULL,
    heading_path  TEXT,
    content       TEXT         NOT NULL,
    content_len   INT          NOT NULL,
    page_from     INT,
    page_to       INT,
    tsv           TSVECTOR,
    embedding     VECTOR(1024),
    meta          JSONB        NOT NULL DEFAULT '{}'::jsonb,
    origin        VARCHAR(16)  NOT NULL DEFAULT 'ingest'
                  CHECK (origin IN ('ingest','manual','feedback')),
    created_by    VARCHAR(64),
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT now()
);
COMMENT ON COLUMN kb.chunks.tsv IS 'jieba 预分词 + to_tsvector(simple) 生成的全文索引列';
COMMENT ON COLUMN kb.chunks.embedding IS 'bge-m3 1024 维向量；NULL 表示该块不参与向量检索';
COMMENT ON COLUMN kb.chunks.origin IS '知识来源：ingest=批量入库 | manual=手工录入 | feedback=补录闭环';

-- =====================================================================
-- kb.alarms  (报警码独立成表 - 精确短路的核心)
-- =====================================================================
CREATE TABLE IF NOT EXISTS kb.alarms (
    id            BIGSERIAL PRIMARY KEY,
    brand         VARCHAR(64)  NOT NULL,
    controller    VARCHAR(64),
    code          VARCHAR(32)  NOT NULL,
    code_norm     VARCHAR(32)  NOT NULL,
    category      VARCHAR(64),
    severity      VARCHAR(16)  NOT NULL DEFAULT 'unknown'
                  CHECK (severity IN ('info','warning','fault','fatal','unknown')),
    name          VARCHAR(256) NOT NULL,
    description   TEXT,
    cause         TEXT,
    action        TEXT,
    safety_note   TEXT,
    doc_id        BIGINT       REFERENCES kb.documents(id) ON DELETE SET NULL,
    page_no       INT,
    tsv           TSVECTOR,
    embedding     VECTOR(1024),
    meta          JSONB        NOT NULL DEFAULT '{}'::jsonb,
    origin        VARCHAR(16)  NOT NULL DEFAULT 'ingest'
                  CHECK (origin IN ('ingest','manual','feedback')),
    created_by    VARCHAR(64),
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT now()
);
COMMENT ON TABLE kb.alarms IS '报警码主表，支持精确短路 + 向量召回双通道';
COMMENT ON COLUMN kb.alarms.code_norm IS '归一化码（去前导零/统一大写），用于匹配';

-- =====================================================================
-- ops.maintenance_logs  (维修工单 - MES 业务挂载点)
-- =====================================================================
CREATE TABLE IF NOT EXISTS ops.maintenance_logs (
    id            BIGSERIAL PRIMARY KEY,
    machine_id    BIGINT       NOT NULL REFERENCES ops.machines(id) ON DELETE CASCADE,
    order_no      VARCHAR(64)  UNIQUE,
    alarm_code    VARCHAR(32),
    fault_type    VARCHAR(64),
    symptom       TEXT         NOT NULL,
    root_cause    TEXT,
    action_taken  TEXT,
    parts_used    JSONB        NOT NULL DEFAULT '[]'::jsonb,
    engineer      VARCHAR(64),
    downtime_min  INT,
    started_at    TIMESTAMPTZ  NOT NULL,
    finished_at   TIMESTAMPTZ,
    embedding     VECTOR(1024),
    is_demo       BOOLEAN      NOT NULL DEFAULT false,
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT now()
);
COMMENT ON TABLE ops.maintenance_logs IS '维修工单，对应 MES 报修单据；用于相似历史故障检索';

-- =====================================================================
-- log.query_logs  (查询日志 - 可观测 + 数据闭环 + 看板)
-- =====================================================================
CREATE TABLE IF NOT EXISTS log.query_logs (
    id                BIGSERIAL PRIMARY KEY,
    trace_id          UUID         NOT NULL DEFAULT gen_random_uuid(),
    session_id        VARCHAR(64),
    user_code         VARCHAR(64),
    raw_query         TEXT         NOT NULL,
    rewritten_query   TEXT,
    detected_codes    TEXT[]       NOT NULL DEFAULT '{}',
    route             VARCHAR(32)  NOT NULL,
    tool_calls        JSONB        NOT NULL DEFAULT '[]'::jsonb,
    retrieved         JSONB        NOT NULL DEFAULT '[]'::jsonb,
    top_score         REAL,
    answer            TEXT,
    refused           BOOLEAN      NOT NULL DEFAULT false,
    latency_ms        INT,
    latency_breakdown JSONB        NOT NULL DEFAULT '{}'::jsonb,
    prompt_tokens     INT,
    completion_tokens INT,
    feedback          SMALLINT,
    feedback_note     TEXT,
    created_at        TIMESTAMPTZ  NOT NULL DEFAULT now()
);
COMMENT ON TABLE log.query_logs IS '所有查询主日志 - 不可重建的数据资产';

-- =====================================================================
-- log.query_trace_steps  (步骤级追踪 - 检索排查页数据源)
-- =====================================================================
CREATE TABLE IF NOT EXISTS log.query_trace_steps (
    id           BIGSERIAL PRIMARY KEY,
    query_log_id BIGINT       NOT NULL REFERENCES log.query_logs(id) ON DELETE CASCADE,
    trace_id     UUID         NOT NULL,
    seq          SMALLINT     NOT NULL,
    step         VARCHAR(32)  NOT NULL
                 CHECK (step IN ('normalize','code_extract','exact_match','vector_recall',
                                 'fulltext_recall','rrf_fusion','rerank','threshold_gate',
                                 'tool_call','llm_generate','post_check')),
    status       VARCHAR(16)  NOT NULL DEFAULT 'ok'
                 CHECK (status IN ('ok','skipped','failed','timeout')),
    started_at   TIMESTAMPTZ  NOT NULL,
    ms           INT          NOT NULL,
    input        JSONB        NOT NULL DEFAULT '{}'::jsonb,
    output       JSONB        NOT NULL DEFAULT '{}'::jsonb,
    note         TEXT,
    created_at   TIMESTAMPTZ  NOT NULL DEFAULT now()
);
COMMENT ON TABLE log.query_trace_steps IS '每步推理追踪 - 支撑检索排查页可视化';

-- =====================================================================
-- log.feedbacks  (用户反馈 - 闭环入口)
-- =====================================================================
CREATE TABLE IF NOT EXISTS log.feedbacks (
    id           BIGSERIAL PRIMARY KEY,
    query_log_id BIGINT       NOT NULL REFERENCES log.query_logs(id) ON DELETE CASCADE,
    trace_id     UUID         NOT NULL,
    user_code    VARCHAR(64),
    verdict      SMALLINT     NOT NULL CHECK (verdict IN (1, -1)),
    reason       VARCHAR(32)
                 CHECK (reason IN ('not_relevant','wrong_answer','incomplete',
                                   'outdated','no_source','other')),
    bad_refs     INT[]        NOT NULL DEFAULT '{}',
    comment      TEXT,
    correction   TEXT,
    handled      BOOLEAN      NOT NULL DEFAULT false,
    created_at   TIMESTAMPTZ  NOT NULL DEFAULT now()
);
COMMENT ON TABLE log.feedbacks IS '用户反馈：赞/踩 + 纠错 + 引用错误标注';

-- =====================================================================
-- log.kb_suggestions  (待补充知识清单 - 闭环收口)
-- =====================================================================
CREATE TABLE IF NOT EXISTS log.kb_suggestions (
    id             BIGSERIAL PRIMARY KEY,
    source         VARCHAR(24)  NOT NULL
                   CHECK (source IN ('refused','negative_feedback','manual','low_score')),
    trace_id       UUID,
    question       TEXT         NOT NULL,
    suggested_type VARCHAR(24)  NOT NULL DEFAULT 'faq'
                   CHECK (suggested_type IN ('alarm','faq','manual_chunk','maintenance_tip')),
    draft_content  TEXT,
    status         VARCHAR(16)  NOT NULL DEFAULT 'open'
                   CHECK (status IN ('open','in_progress','resolved','rejected')),
    resolved_ref   JSONB,
    handler        VARCHAR(64),
    created_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
    resolved_at    TIMESTAMPTZ
);
COMMENT ON TABLE log.kb_suggestions IS '待补充知识清单：拒答/差评自动汇集，人工补录后闭环';

-- =====================================================================
-- log.eval_items  (评估集 - 消融实验依据)
-- =====================================================================
CREATE TABLE IF NOT EXISTS log.eval_items (
    id         BIGSERIAL PRIMARY KEY,
    question   TEXT         NOT NULL,
    q_type     VARCHAR(32)  NOT NULL
               CHECK (q_type IN ('alarm_code','symptom','maintenance','param','device_history','multi_turn')),
    expected   JSONB        NOT NULL,
    difficulty SMALLINT     NOT NULL DEFAULT 1,
    note       TEXT,
    created_at TIMESTAMPTZ  NOT NULL DEFAULT now()
);
COMMENT ON TABLE log.eval_items IS '消融实验评估集：100 条手工标注问题';

-- =====================================================================
-- kb.import_jobs  (导入任务追踪 - 三种录入方式之 Excel 批量)
-- =====================================================================
CREATE TABLE IF NOT EXISTS kb.import_jobs (
    id            BIGSERIAL PRIMARY KEY,
    job_type      VARCHAR(24)  NOT NULL
                  CHECK (job_type IN ('alarm','faq','machine','maintenance')),
    filename      VARCHAR(256) NOT NULL,
    file_hash     CHAR(64),
    total_rows    INT          NOT NULL DEFAULT 0,
    valid_rows    INT          NOT NULL DEFAULT 0,
    dup_rows      INT          NOT NULL DEFAULT 0,
    error_rows    INT          NOT NULL DEFAULT 0,
    imported_rows INT          NOT NULL DEFAULT 0,
    vectorized    INT          NOT NULL DEFAULT 0,
    dup_strategy  VARCHAR(16)  NOT NULL DEFAULT 'skip'
                  CHECK (dup_strategy IN ('skip','overwrite','duplicate')),
    status        VARCHAR(16)  NOT NULL DEFAULT 'validating'
                  CHECK (status IN ('validating','previewing','importing','done','failed','cancelled')),
    errors        JSONB        NOT NULL DEFAULT '[]'::jsonb,
    created_by    VARCHAR(64),
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT now(),
    finished_at   TIMESTAMPTZ
);
COMMENT ON TABLE kb.import_jobs IS 'Excel 批量导入任务状态追踪';
