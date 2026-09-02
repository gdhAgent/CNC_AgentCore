-- 003_indexes.sql
-- 全部索引：HNSW 向量、GIN 全文、pg_trgm 模糊、btree 元数据
-- 幂等：CREATE INDEX IF NOT EXISTS

-- =============================================================
-- 1. 向量索引（HNSW, 余弦距离）
-- =============================================================
CREATE INDEX IF NOT EXISTS idx_chunks_embedding
    ON kb.chunks USING hnsw (embedding vector_cosine_ops) WITH (m = 16, ef_construction = 64);

CREATE INDEX IF NOT EXISTS idx_alarms_embedding
    ON kb.alarms USING hnsw (embedding vector_cosine_ops) WITH (m = 16, ef_construction = 64);

CREATE INDEX IF NOT EXISTS idx_mlogs_embedding
    ON ops.maintenance_logs USING hnsw (embedding vector_cosine_ops) WITH (m = 16, ef_construction = 64);

-- =============================================================
-- 2. 全文索引（GIN, tsv 列）
-- =============================================================
CREATE INDEX IF NOT EXISTS idx_chunks_tsv  ON kb.chunks  USING gin (tsv);
CREATE INDEX IF NOT EXISTS idx_alarms_tsv  ON kb.alarms  USING gin (tsv);

-- =============================================================
-- 3. 报警码：精确 + 模糊
-- =============================================================
-- 唯一索引（覆盖表达式列）
CREATE UNIQUE INDEX IF NOT EXISTS uq_alarms_key
    ON kb.alarms (brand, COALESCE(controller,''), code_norm);

CREATE INDEX IF NOT EXISTS idx_alarms_code_norm
    ON kb.alarms (code_norm);

CREATE INDEX IF NOT EXISTS idx_alarms_code_trgm
    ON kb.alarms USING gin (code_norm gin_trgm_ops);

-- =============================================================
-- 4. 元数据过滤 / 关联查询索引
-- =============================================================
CREATE INDEX IF NOT EXISTS idx_chunks_doc
    ON kb.chunks (doc_id, level, seq);

CREATE INDEX IF NOT EXISTS idx_docs_model_scope
    ON kb.documents USING gin (model_scope);

CREATE INDEX IF NOT EXISTS idx_mlogs_machine_time
    ON ops.maintenance_logs (machine_id, started_at DESC);

CREATE INDEX IF NOT EXISTS idx_mlogs_alarm
    ON ops.maintenance_logs (alarm_code)
    WHERE alarm_code IS NOT NULL;

-- =============================================================
-- 5. 日志分析索引
-- =============================================================
CREATE INDEX IF NOT EXISTS idx_qlogs_time
    ON log.query_logs (created_at DESC);

CREATE INDEX IF NOT EXISTS idx_qlogs_codes
    ON log.query_logs USING gin (detected_codes);

CREATE INDEX IF NOT EXISTS idx_trace_steps_log
    ON log.query_trace_steps (query_log_id, seq);

CREATE INDEX IF NOT EXISTS idx_trace_steps_trace
    ON log.query_trace_steps (trace_id);

CREATE INDEX IF NOT EXISTS idx_feedbacks_log
    ON log.feedbacks (query_log_id);

CREATE INDEX IF NOT EXISTS idx_feedbacks_open
    ON log.feedbacks (created_at DESC)
    WHERE handled = false;

CREATE INDEX IF NOT EXISTS idx_kbsug_status
    ON log.kb_suggestions (status, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_import_jobs_time
    ON kb.import_jobs (created_at DESC);
