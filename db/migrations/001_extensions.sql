-- 001_extensions.sql
-- 扩展 + 三 schema + search_path
-- 幂等：所有对象 IF NOT EXISTS

-- 1. 必备扩展
CREATE EXTENSION IF NOT EXISTS vector;     -- pgvector：向量类型与余弦距离算子
CREATE EXTENSION IF NOT EXISTS pg_trgm;    -- 三元组模糊匹配（报警码/型号容错）
CREATE EXTENSION IF NOT EXISTS btree_gin;  -- 复合 GIN 索引支持

-- 2. 三 schema 分区
--    kb : 知识域（可重建，删除后重灌不影响日志与反馈）
--    ops: 业务域（对应 MES 主数据）
--    log: 运行域（数据资产，绝不删除）
CREATE SCHEMA IF NOT EXISTS kb;
CREATE SCHEMA IF NOT EXISTS ops;
CREATE SCHEMA IF NOT EXISTS log;

-- 3. 集群默认 search_path：方便 psql 交互，但业务 SQL 一律写全限定名（见 PLAN.md §3）
ALTER DATABASE cnc_kb SET search_path TO kb, ops, log, public;
