# TILSOFTAI Atomic Orchestrator (Mode B) — Project README

This repository implements an **“Atomic Orchestrator”** in **Mode B (manual tool loop)**:
- **C# + SQL Server** provide **tools, data, schema, and bounded evidence**.
- The **LLM** performs reasoning: selects tools, requests datasets, produces an **Analysis Plan (DSL)**, and writes **Insight text**.
- The server executes the plan using a deterministic engine (**AtomicDataEngine**) with **native joins (Option B)**.
- The system enforces a strict **Data Boundary**: the LLM never receives raw, large datasets.

> Design goal: keep orchestration logic **data-driven**, minimize C# heuristics, and prevent timeouts via **prompt compaction** and **history trimming**.

---

## 1) Core Principles (Non‑Negotiables)

### 1.1 Mode B Pipeline (Manual Tool Loop)
**Standard flow**
1. **User asks** a question.
2. LLM calls `atomic.catalog.search` to select a Stored Procedure (SP) from `dbo.TILSOFTAI_SPCatalog`.
3. LLM calls `atomic.query.execute(spName, params)` to fetch *bounded* datasets.
4. Server returns:
   - `schema_digest` (small, tool evidence to LLM)
   - `engine_datasets` with `datasetId` references (stored server-side)
   - **List Preview Markdown** (server-rendered for the client; *not* sent to LLM)
5. LLM reasons on schema + small evidence and calls `analytics.run(datasetId, plan)`.
6. Server runs **AtomicDataEngine** using the DSL plan (including join Option B) and returns:
   - bounded summary evidence (schema + top rows preview)
   - **Insight Preview Markdown** (server-rendered for the client)
7. LLM outputs **Insight text only** (no tables).
8. Server composes final output in **3 Markdown blocks**:
   1) Insight text (LLM)
   2) Insight preview table (server markdown)
   3) List preview (server markdown, optional)

### 1.2 Data Boundary
- **LLM never receives large raw rowsets.**
- Tool handlers may produce a “full envelope” for server use, but **tool messages injected into the LLM conversation are compacted**:
  - drop `envelope.data` (rows)
  - prune evidence arrays/strings by budget
  - truncate if exceeding max bytes, add `truncated` warning

### 1.3 No Keyword Heuristics in C#
- C# must not hard-code keyword routing logic.
- Tool selection and analysis plan are driven by:
  - SQL catalog metadata (Domain/Entity/Intent/ParamsJson/ExampleJson/SchemaHintsJson)
  - RS0 schema metadata emitted by SPs
  - bounded tool evidence (counts, schema digest, small previews)

---

## 2) High-Level Architecture

### 2.1 Major Components
- **TILSOFTAI.Api**: HTTP entrypoint, configuration, middleware, DI.
- **TILSOFTAI.Orchestration**: Mode B chat pipeline, tools, compactor, markdown renderers.
- **TILSOFTAI.Application**: business services (AnalyticsService), dataset lifecycle.
- **TILSOFTAI.Analytics**: AtomicDataEngine (filter/group/agg/join/sort/top/select).
- **TILSOFTAI.Infrastructure**: SQL repository, dataset store implementations (InMemory/Redis).

### 2.2 Execution Context (Per-request state)
A request-scoped context stores **client artifacts** and **recent evidence** without inflating prompts:
- `LastListPreviewMarkdown`
- `LastInsightPreviewMarkdown`
- `LastSchemaDigest` / dataset digests (bounded)

---

## 3) Tools (LLM-Callable)

### 3.1 Tool: `atomic.catalog.search`
**Purpose**
- Search SPCatalog for the best SP to answer the user intent.

**Input**
- `query` (user question or condensed intent)
- optional `topK`

**Output (bounded)**
- `results[]` (SP name + domain/entity + param specs + examples + schema hints digest)
- evidence: same but compact and prunable

### 3.2 Tool: `atomic.query.execute`
**Purpose**
- Execute an SP to produce bounded datasets + schema digest.

**Input**
- `spName`
- `params` (filtered by allowlist from ParamsJson; defaults from ParamsJson)

**Server behavior**
- Executes SP
- Builds `schema_digest` from RS0
- Stores engine datasets into dataset store with `datasetId`
- Renders **list preview markdown** for the client

**Output to LLM (bounded evidence)**
- `schema_digest`
- `engine_datasets[]`: `{ datasetId, tables[], rowCount, columnCount }`
- summary counts / warnings

**Client artifact**
- `LastListPreviewMarkdown` (never injected into LLM prompt)

### 3.3 Tool: `analytics.run`
**Purpose**
- Run analysis plan DSL on a stored dataset (`datasetId`), including native join Option B.

**Input**
- `datasetId`
- `plan` (pipeline DSL)

**Server behavior**
- Validate plan against schema digest
- Execute engine
- Render **insight preview markdown** for the client

**Output to LLM (bounded evidence)**
- `summary_schema` (bounded)
- `preview_rows` (top N, bounded)
- warnings / error details (when ok=false)

**Client artifact**
- `LastInsightPreviewMarkdown`

---

## 4) Contracts and Envelopes

All tool responses are wrapped as an envelope:
- `kind`: e.g., `atomic.query.execute.v1`, `analytics.run.v1`
- `schemaVersion`: versioned contract
- `ok`: boolean
- `data`: server-side payload (may include raw rows, but stripped from LLM prompt)
- `evidence`: bounded, LLM-facing

> The **ToolResultCompactor** is the “prompt firewall” ensuring LLM receives bounded evidence only.

---

## 5) ToolResultCompactor (Prompt Firewall)

### 5.1 What it removes / bounds
- Always removes `envelope.data` from the tool message injected into the LLM conversation.
- Prunes:
  - depth (`MaxDepth`)
  - array length (`MaxArrayElements`)
  - string length (`MaxStringLength`)
  - total serialized bytes (`MaxBytes`)

### 5.2 Why it matters
- Prevents prompt bloat and reduces risk of:
  - `TaskCanceledException`
  - “Client disconnected” from LM Studio
  - slow tool loops due to growing tool messages

---

## 6) ChatPipeline (Manual Tool Loop)

### 6.1 Behavior
- Maintains conversation messages (system + recent user + essential tool context).
- Loop:
  - call model with current messages + exposed tools
  - if tool calls exist:
    - invoke tools server-side
    - compact tool results
    - append compacted tool results as role=tool messages
  - stop when model returns final insight text

### 6.2 Prompt trimming rules (budget based)
- Always keep:
  - system message
  - last user message(s)
  - most recent essential tool evidence (catalog result + latest dataset digest)
- Prefer trimming:
  - older tool messages first
  - older assistant messages next

---

## 7) AtomicDataEngine (Analysis Plan DSL)

### 7.1 Supported operations (typical)
- `filter`
- `groupBy` + aggregations (`count`, `sum`, `avg`, `min`, `max`)
- `sort`
- `topN` / `limit`
- `select`
- **`join` (Option B native join)**

### 7.2 Join step (Option B)
Example:
```json
{
  "op": "join",
  "rightDatasetId": "ds_123",
  "leftKeys": ["collectionId"],
  "rightKeys": ["collectionId"],
  "how": "left",
  "rightPrefix": "r_",
  "selectRight": ["collectionName","rangeName"]
}
```

**Guardrails**
- Missing keys → warning + skip join
- Cap join explosion:
  - `MaxJoinRows`
  - `MaxJoinMatchesPerLeft`
- Right columns are prefixed to avoid collisions.

---

## 8) SQL Server Schema (V2)

### 8.1 Apply the schema
A schema script is provided:
- `sql/tilsoftai_atomic_schema_v2.sql`

It ensures:
- `dbo.TILSOFTAI_SPCatalog` includes:
  - `ParamsJson` (param spec)
  - `ExampleJson` (usage examples)
  - `SchemaHintsJson` (join/semantic hints)
- `dbo.TILSOFTAI_TableKindSignals` exists (optional)
- `dbo.TILSOFTAI_sp_catalog_search` exists (optional LIKE-based search)

### 8.2 SPCatalog JSON conventions
**ParamsJson** (array)
```json
[
  { "name":"@Page", "sqlType":"int", "required":false, "default":0, "description_en":"@Page=0 means dataset mode" },
  { "name":"@Size", "sqlType":"int", "required":false, "default":20000, "description_en":"max rows for bounded engine dataset" }
]
```

**SchemaHintsJson** (object)
```json
{
  "tables": [
    {
      "tableName": "sales_engine",
      "tableKind": "fact",
      "delivery": "engine",
      "primaryKey": ["saleId"],
      "foreignKeys": [
        { "column": "collectionId", "refTable": "collections_engine", "refColumn": "collectionId" }
      ],
      "measureHints": ["amount", "qty"],
      "dimensionHints": ["season", "date", "collectionId"]
    }
  ]
}
```

---

## 9) Stored Procedure Output Contract (Atomic SP Pattern)

Atomic SPs should emit:

- **RS0 (Schema metadata)**
  - contains:
    - `recordType` = `resultset` | `column`
    - `resultSetIndex`
    - for resultset rows: `tableName`, `tableKind`, `delivery`, `primaryKey`, `joinHints`, etc.
    - for column rows: `columnName`, `sqlType`, `tabularType`, `role`, `semanticType`, etc.

- **RS1 (Summary)**
  - row counts and other small summary numbers

- **RS2.. (Engine datasets)**
  - bounded rowsets used for analytics engine, stored server-side by `datasetId`

- **RSx (Display preview tables)**
  - top N rows for client list preview (server-rendered markdown)

---

## 10) Configuration & Timeouts

### 10.1 LLM settings
- Configure LM Studio / LLM endpoint in `appsettings.json` (or env vars).
- Timeout strategy:
  - model timeout: 180–300s typical
  - HttpClient timeout clamp: up to 1800s
  - Keep prompts bounded to avoid timeouts.

### 10.2 SQL settings
- Set SQL command timeout to handle bounded but possibly heavy queries (e.g., 180s).
- Prefer SPs that return bounded engine datasets.

---

## 11) Security & Secrets

**Do not commit credentials.**
- Use environment variables or Secret Manager for:
  - SQL connection strings
  - Redis connection strings
  - any API keys

Recommended:
- `appsettings.json` contains placeholders only.
- `appsettings.Development.json` is local-only (gitignored), or use User Secrets.

---

## 12) Suggested Test Scenarios (Required)

### Test 1: “Model list”
1) LLM calls `atomic.query.execute`  
2) Server returns schema digest + datasetId evidence; list preview markdown captured
3) LLM calls `analytics.run` with group/agg plan  
4) Server returns bounded summary evidence; insight preview markdown captured
5) Final response has 3 markdown blocks

### Test 2: “Sales by collection (join)”
- Query returns sales and collections datasets
- LLM plan joins and aggregates by collection
- Engine runs native join; bounded evidence

### Test 3: Stress prompt
- SP returns multiple tables
- Verify:
  - tool outputs in LLM conversation do NOT include raw rows
  - prompt remains under budget
  - no timeout / disconnect

---

## 13) Roadmap (Deep Analytics Extensions)

If you need deeper analytics while keeping the data boundary:
- Add DSL steps: `derive`, `dateBucket`, `profile`, `percentOfTotal`, `pareto`
- Add `persistResult=true` to store analytics outputs as a new dataset for follow-up questions
- Add `analytics.explain` (optional) to validate/normalize plan and estimate impact before execution

---

## 14) How to Run (Typical)

### Prerequisites
- .NET SDK (matching solution)
- SQL Server
- (Optional) Redis
- LM Studio (or other OpenAI-compatible chat completion endpoint)

### Steps
1) Apply SQL schema:
   - Run `sql/tilsoftai_atomic_schema_v2.sql` on your database.
2) Seed `dbo.TILSOFTAI_SPCatalog`:
   - Insert SP entries with ParamsJson/ExampleJson/SchemaHintsJson.
3) Configure `appsettings.json` / environment variables:
   - SQL connection string
   - Redis (optional)
   - LLM endpoint + timeouts
4) Start API:
   - `dotnet run --project src/TILSOFTAI.Api`
5) Use your client to send chat requests to the API (check Swagger/OpenAPI in development).

---

## 15) Troubleshooting

### Timeouts (TaskCanceledException)
- Ensure:
  - ToolResultCompactor is active
  - history trimming is enabled
  - SPs return bounded datasets (avoid huge rowsets)
  - LLM timeout configuration is sufficient (180–300s)

### Tool loop fails repeatedly
- Verify tool names exposed: `atomic.catalog.search`, `atomic.query.execute`, `analytics.run`
- Verify tool schema matches ToolRegistry allowed arguments
- Check tool errors in envelope evidence (do not rely on raw data)

### Join returns too many rows / truncated
- Increase bounds carefully (or refine join keys / add filters)
- Prefer aggregation after join (groupBy + sum/count) and enforce MaxJoinRows

---

## Appendix A — Final Output Format

The API response should contain **exactly**:

1) **Insight** (LLM text only)  
2) **Insight Preview** (markdown table, server-rendered)  
3) **List Preview** (markdown list/table, server-rendered, optional)

The LLM must **not** output tables directly.

---

## Appendix B — Glossary

- **Schema Digest**: compact representation of tables/columns/roles/semantics for reasoning.
- **DatasetId**: server-side handle to a cached engine dataset (Redis/InMemory).
- **Evidence**: bounded tool output used by the LLM to reason (never raw large rows).
- **Mode B**: manual tool loop orchestrated by server; model iterates tool calls until ready.
