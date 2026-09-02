# CNC 机台智能知识库（.NET 10 实现）

面向 **MES / 设备运维** 场景的工业垂直领域 **RAG + Agent** 系统：输入白话故障描述或报警码（如 `SV0401`），返回可溯源的原文片段与带引用编号的结构化 AI 分析。本仓库为 **ASP.NET Core（.NET 10）** 实现。

> 同一产品另有 **Python (FastAPI)** 实现：🔗 [`CNC_Agent`](https://github.com/gdhAgent/CNC_Agent)
> 两版共享同一套 PostgreSQL / pgvector 的 schema（`db/migrations/` 逐字节一致）与 API 口径，可对照查看不同技术栈下的实现与部署差异。

---

## 功能特性

| 能力 | 说明 |
|---|---|
| 混合检索 | 向量（bge-m3）+ 中文全文（jieba 分词 + tsvector）+ 报警码精确命中 + RRF 融合 + Rerank |
| 引用溯源 | 上下文按 `[n]` 编号注入，LLM 输出标注来源，后处理剔除越界引用 |
| 抑制幻觉 | 强制引用 + 结构化输出 + 拒答门控（召回为空 / 置信度低不调用 LLM） |
| 数据闭环 | 拒答与差评进入待补充清单 → 补录 → 立即可检索 |
| Agent | 受限工具路由（状态机，`max_rounds=2`）：检索知识 / 报警码 / 机台历史工单 |
| 语言 | .NET 10 + ASP.NET Core Minimal API；EF Core + Dapper（原生 SQL）访问 PostgreSQL |

> 定位：本系统只做 **检索与辅助分析**，不直接控制机台、不自动下发指令。输出仅供参考，实际作业以厂商手册与现场规程为准（见文末免责声明）。

## 系统架构

```
前端 (Vue3 + Vite + TS) ── HTTP/SSE ──► ASP.NET Core (Minimal API)
                                          ├─ Agent 路由器（受限工具状态机）
                                          ├─ 检索链路：报警码精确命中 / 向量召回 / 全文召回
                                          │      → RRF 融合 → Rerank → 置信度门控
                                          ├─ LLM (DeepSeek) · Embedding/Rerank (SiliconFlow)
                                          └─ PostgreSQL 17 + pgvector + pg_trgm
                                             ├─ kb  schema：知识（alarms / chunks / documents / term_dict）
                                             ├─ ops schema：业务（machines / maintenance_logs / users / role_permissions）
                                             └─ log schema：运行（query_logs / query_trace_steps / feedbacks / kb_suggestions）
```

- 三个 schema 物理隔离：`kb` 可整库重建，不影响 `ops` / `log`。
- Provider 抽象：LLM / Embedding / Rerank 统一接口，可切换厂商（含内网离线模型）。
- 全链路 Trace：`log.query_trace_steps` 记录检索步骤，供排查页时间轴与排名对比。

## 目录结构

```
CNC_AgentCore/
├─ src/
│  ├─ CNC_AgentCore.Api/           入口 / Minimal API Endpoints / 中间件
│  ├─ CNC_AgentCore.Application/   用例、仓储抽象、Agent、检索链路
│  ├─ CNC_AgentCore.Domain/        领域实体与约定
│  └─ CNC_AgentCore.Infrastructure/ 持久化（EF Core + Dapper + pgvector）、
│                                    LLM/Embedding Provider、鉴权、健康检查
├─ db/migrations/                  schema 迁移 001…007（与 Python 版一致，纯 SQL、幂等）
├─ samples/.env.example            配置模板（无真实密钥）
├─ assets/screenshots/             界面截图（演示后补充）
├─ Dockerfile / docker-compose.yml / db-init.sh / DOCKER.md    容器化部署
└─ CNC_AgentCore.slnx / Directory.Build.props / Directory.Packages.props
```

## 快速开始

推荐 Docker 一键起 `PostgreSQL+pgvector` 与后端；schema 由 `db-init` 服务自动应用（本应用不内建迁移执行）。

### 方式一：Docker（推荐，含 Windows）

```bash
cp .env.docker.example .env.docker    # 填入 PG_SUPERPASSWORD / API Key / JWT_SECRET
docker compose --env-file .env.docker up -d --build
curl http://localhost:8000/health
```

- `db-init` 会依序应用 `db/migrations/001..007.sql`，成功后才启动 `api`。
- 详细命令与账号说明见 [`DOCKER.md`](DOCKER.md)。

### 方式二：本地运行（需要 .NET 10 SDK + PostgreSQL 17 + pgvector）

```bash
cp samples/.env.example .env           # 填写 PG_CONNECTION_STRING / JWT_SECRET / API Key
# 先建 schema：psql 依序执行 db/migrations/001…007.sql（或用上方 Docker 的 db-init 服务跑一遍）
dotnet run --project src/CNC_AgentCore.Api
```

健康检查：`GET /health` 返回 db / llm / embedding / rerank 四项状态；`GET /` 返回服务信息。

## 前端

UI 是独立的 Vue3 + Vite 项目（`CNC_Web_Agent`，将与本项目同期开源）。在前端仓库根目录：

```bash
npm install
npm run dev        # http://localhost:5173 ，/api 代理到后端 8000
```

## 界面截图

> 演示环境运行后补充截图，放至 `assets/screenshots/` 并替换下方占位即可。

| 主界面（左右分栏 + 流式） | 检索排查页（时间轴） | 知识管理 / 录入 |
|---|---|---|
| ![主界面](assets/screenshots/main.png) | ![检索排查](assets/screenshots/trace.png) | ![知识管理](assets/screenshots/knowledge.png) |

## 配置项

| 组 | 键 | 说明 |
|---|---|---|
| 数据库 | `PG_CONNECTION_STRING` | Npgsql 连接串（含 `Database=cnc_kb`） |
| LLM | `DEEPSEEK_API_KEY / DEEPSEEK_BASE_URL / DEEPSEEK_MODEL` | 问答模型（OpenAI 兼容） |
| 检索 | `SILICONFLOW_API_KEY / SILICONFLOW_BASE_URL`、`EMBEDDING_MODEL / EMBEDDING_DIM / RERANK_MODEL`、`RERANK_THRESHOLD` | 向量化与重排 |
| 鉴权 | `JWT_SECRET / JWT_ALGORITHM / JWT_TTL_SEC / JWT_ISSUER` | 登录与令牌（HS256，Secret ≥ 32 字节，过短自动补齐） |
| 运行 | `ASPNETCORE_URLS`、`RERANK_THRESHOLD` | 监听地址与拒答阈值 |

## 数据与免责声明

- **报警码数据**整理自厂商公开技术文档与公开维修资料（不含任何厂商原始手册 PDF），仅供学习与技术演示，不用于商业用途；如有权利异议请联系移除。
- **设备台账与维修工单**为脚本生成的仿真数据（库中以 `is_demo = true` 标记），不含真实企业信息。
- 本系统为**故障检索与辅助分析工具**，输出仅供参考，**不可作为机床操作、维修或安全决策的唯一依据**，实际作业请遵循设备厂商官方手册与工厂安全规程。
- 真实 API Key 一律放本机 `.env`（已被 `.gitignore` 忽略），仓库内只提供占位模板。

## Roadmap

- [x] 混合检索 / Agent / SSE 流式 / 数据闭环（当前版本）
- [x] Python (FastAPI) 原版（见 [`CNC_Agent`](https://github.com/gdhAgent/CNC_Agent)）
- [ ] 界面截图与演示素材补充
- [ ] 离线模型（Ollama 等）部署支持
