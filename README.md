# TILSOFTAI (ERP AI Orchestrator)

TILSOFTAI là nền tảng **AI Orchestrator cho ERP**: kết nối Open WebUI (OpenAI-compatible client) với LLM (LM Studio/OpenAI-compatible) và SQL Server thông qua API .NET 8, theo mô hình **Tool/Function Calling** với **guardrails chuẩn enterprise**.

Phiên bản **ver26** kế thừa ver25 và tập trung vào 3 mục tiêu:
1) **Tách nghiệp vụ theo Module** (hiện tại migrate **Models**), Core không phình theo số lượng nghiệp vụ.
2) **Chọn tool thông minh** để tránh overload (không load toàn bộ tools vào LLM mỗi lượt), đồng thời hỗ trợ **câu hỏi nối tiếp** (follow-up) không nhắc lại chủ thể.
3) **Clean code**: loại bỏ cơ chế registry/dispatcher hardcode cũ, giảm điểm nghẽn khi mở rộng.

ver26 bổ sung:
- **Auto-map TabularData từ DbDataReader** (không khai báo cột trong Repository).
- **SQL-first Semantics**: mặc định ResultSet-0 là metadata mô tả ngữ nghĩa cột; tool payload trả kèm `data.schema` để LLM không suy luận bừa.

ver21 bổ sung thêm:
- **Đa ngôn ngữ (VI/EN)**: user chat tiếng Anh → trả lời tiếng Anh; chat tiếng Việt → trả lời tiếng Việt. Ngôn ngữ được suy luận theo lượt chat và được lưu theo ConversationId để hỗ trợ follow-up ngắn.
- **Regex đa ngôn ngữ trong code** (ChatTextPatterns): follow-up, reset filters, extract confirmation id (VI/EN) — giảm phụ thuộc system prompt.
- **Giảm phụ thuộc vào system prompt**: system prompt rút gọn, đồng thời thêm `ChatTuning` (temperature thấp) để giảm nhiễu/hallucination.

ver20 đã bổ sung thêm:
- **ConversationId do server generate** và echo về client qua response header `X-Conversation-Id`.
- **Conversation state store**: lưu “last query” (tool + filters chuẩn hoá theo filters-catalog) để các câu follow-up có thể **patch/merge filters** mà không cần hard-code key.
- **Redis store (tuỳ chọn)**: thay thế InMemory bằng Redis với sliding TTL cấu hình được và payload versioning.

ver22 hotfix:
- Sửa lỗi thiếu type `ChatCompletionMessage` do sai namespace trong `ILanguageResolver` (trỏ nhầm sang `TILSOFTAI.Orchestration.Llm`).

---

## 1. Nguyên tắc thiết kế (Enterprise Guardrails)

- **LLM không được**: sinh SQL, truy cập DB trực tiếp, chạy business logic, tạo side-effects.
- **LLM chỉ được**: đọc ngôn ngữ tự nhiên và quyết định **tool + arguments**.
- **Fail-closed**: intent/tool không hợp lệ hoặc không whitelisted → từ chối.
- **Contract-first**: dữ liệu tool trả về có `kind/schemaVersion` và được bọc bởi **enterprise envelope**.
- **Traceability**: mọi tool call có `policy`, `source`, `telemetry`, `evidence`.

---

## 2. Kiến trúc tổng quan

### 2.1. Luồng xử lý (High level)
1. Open WebUI gửi request theo chuẩn OpenAI `/v1/chat/completions` tới API.
2. `ChatPipeline` dựng system prompt + chọn module/tools cần expose.
3. LLM:
   - Trả lời trực tiếp (không cần dữ liệu nội bộ), hoặc
   - Gọi tool (function calling) để lấy evidence.
4. `ToolInvoker` thực thi tool call:
   - Whitelist tool (`ToolRegistry`)
   - Validate input theo spec (`ToolInputSpecCatalog` + `DynamicIntentValidator`)
   - RBAC authorize (đặc biệt với WRITE)
   - Dispatch tới handler thông qua `ToolDispatcher`
5. Tool handler gọi tầng Application/Infrastructure (services/repositories/SP).
6. Tool output bọc theo **Envelope v1** (schemaVersion >= 2) và trả về LLM.
7. LLM tổng hợp câu trả lời dựa trên `data/evidence`.

### 2.2. ConversationId (server-generated)
API sử dụng header `X-Conversation-Id` để gom nhiều lượt chat về cùng một hội thoại.

- Nếu client **không gửi** `X-Conversation-Id`: server tự generate và echo lại trong response header.
- Nếu client **có gửi** `X-Conversation-Id`: server giữ nguyên và echo lại trong response header.

Khuyến nghị: Open WebUI/client lưu `X-Conversation-Id` của response đầu tiên và gửi lại cho các request tiếp theo.

### 2.3. Tách nghiệp vụ theo Module (ver13)
Mục tiêu: tránh việc mọi tool/logic bị dồn vào các lớp trung tâm (ví dụ `ToolDispatcher`).

- **Core (Orchestration)** giữ các thành phần dùng chung:
  - Dispatch, whitelist, validation, envelope, governance filters.
- **Module** chỉ chứa nghiệp vụ của domain:
  - Tool handlers (execute)
  - Tool specs (input validation)
  - Tool registrations (whitelist metadata)
  - Filter/Action catalogs cho domain
  - (tùy chọn) plugin exposure policy để giảm overload

Hiện tại đã migrate domain **Models** sang module.

---

## 3. Cơ chế Tool Governance (Core)

### 3.1. ToolRegistry (Whitelist)
`ToolRegistry` được build từ các provider:
- `IToolRegistrationProvider` (mỗi module đóng góp danh sách tool được phép)

Chức năng:
- `IsWhitelisted(tool)`
- `TryValidate(tool, arguments, out intent, out requiresWrite)`

### 3.2. ToolInputSpecCatalog + DynamicIntentValidator
`ToolInputSpecCatalog` được build từ các provider:
- `IToolInputSpecProvider` (mỗi module đóng góp specs cho tool của mình)

`DynamicIntentValidator`:
- Reject unknown args (chống tool injection)
- Parse typed args (int/bool/decimal/guid/map)
- Enforce required fields
- Enforce paging constraints

### 3.3. ToolDispatcher (Core dispatch mỏng)
`ToolDispatcher` **không chứa business logic**.

- Mỗi tool được xử lý bởi một `IToolHandler` (module sở hữu).
- DI resolve handler theo `ToolName`.

Điều này cho phép nhiều team phát triển tool độc lập mà không đụng file trung tâm.

---

## 4. Cơ chế chọn tool thông minh (tránh overload)

Ở quy mô lớn, không nên expose toàn bộ tools cho LLM mỗi lượt. Ver20 triển khai 2 lớp chọn lọc (và bổ sung state cho follow-up):

### 4.1. Level 2: ModuleRouter (chọn module theo message, hỗ trợ follow-up)

Trong thực tế hội thoại ERP, người dùng thường hỏi nối tiếp rất ngắn (vd: "mùa 24/25?", "còn màu đỏ?", "thế 2023?") và không lặp lại chủ thể.

Ver20 bổ sung bước **Context-aware routing text** trong `ChatPipeline`:
- Nếu câu user cuối là follow-up ngắn, hệ thống ghép thêm 1–2 turn trước (user/assistant) để routing không bị rỗng.
- Mục tiêu: vẫn chọn đúng module/tool pack cho câu hỏi nối tiếp.

Sau đó `ModuleRouter.SelectModules(routingText, context)` trả về danh sách module liên quan.

Ver20 bổ sung thêm **conversation state fallback**:
- Mỗi khi tool READ chạy thành công, hệ thống lưu `lastQuery` (tool + filters đã canonicalize theo filters-catalog) theo `X-Conversation-Id`.
- Nếu một câu follow-up quá ngắn khiến router không nhận ra module, hệ thống fallback về module của `lastQuery` để vẫn expose đúng tool-pack.

Ví dụ:
- Người dùng nói về **model/sản phẩm/giá/attribute** → chọn module `models`.
- Nếu có module business → luôn thêm module `common` (catalog tools).

Nếu không match module nào → expose **0 tool** (LLM trả lời tự nhiên theo system prompt, không được bịa tool).

Ngoài ra, nếu **0 tool** được expose thì hệ thống chạy completion ở chế độ **không tools** để tránh một số model open-source (LM Studio) sinh "pseudo tool-call" trong content.

Ver20 thêm một tầng bảo vệ cho follow-up:
- Khi router không match module (do câu hỏi quá ngắn), hệ thống fallback theo **lastQuery.resource** từ ConversationStateStore.
- Khi model gọi tool mà chỉ truyền “delta filters”, server sẽ **patch/merge** filters với lastQuery dựa trên **filters-catalog** (không hard-code key).

#### 4.1.1. Cấu hình ConversationStateStore (InMemory/Redis)

Mặc định cấu hình dùng **InMemoryConversationStateStore** phù hợp chạy đơn node/local. Khi triển khai multi-node, bật Redis để state được chia sẻ giữa các instance.

Cấu hình trong `appsettings.json`:

```json
"ConversationStateStore": {
  "Provider": "InMemory", // hoặc "Redis"
  "Ttl": "00:30:00",
  "SlidingTtlEnabled": true,
  "PayloadVersion": 1,
  "KeyPrefix": "tilsoftai:conv:",
  "Redis": {
    "ConnectionString": "localhost:6379,abortConnect=false",
    "Database": -1
  }
}
```

Ghi chú:
- **SlidingTtlEnabled**: mỗi lần đọc sẽ refresh TTL (giống “sliding session”).
- **PayloadVersion**: wrapper version để bạn có thể nâng schema state về sau mà vẫn tương thích.
- Khi `Provider=Redis` nhưng thiếu `Redis.ConnectionString`, API sẽ báo cấu hình thiếu khi service Redis được resolve.

#### 4.1.2. Cấu hình ChatTuning (ver21)

`ChatTuning` điều khiển các tham số generation để giảm phụ thuộc vào prompt và giảm hallucination (khuyến nghị để temperature thấp):

```json
"ChatTuning": {
  "ToolCallTemperature": 0.1,
  "SynthesisTemperature": 0.2,
  "NoToolsTemperature": 0.3,
  "MaxRoutingContextChars": 1200
}
```


### 4.2. Level 3: Plugin Exposure Policy (chọn tool pack trong module)
Trong 1 module có thể có nhiều tool. Ver20 chia plugin theo **tool packs** và chỉ expose pack cần thiết.

- Interface: `IPluginExposurePolicy`
Ghi chú (ver26.4+): Models module đã được thay bằng tool generic `atomic.query.execute` chạy theo template Atomic (RS0 schema, RS1 summary, RS2..N tables). Vì vậy cơ chế “expose theo Models tool packs” không còn dùng.

Kết quả: giảm số function exposures, giảm token overhead, tăng ổn định.

---

## 5. Modules hiện có (ver19)

### 5.1. Common module
- `filters.catalog`
- `actions.catalog`

Vị trí:
- `src/TILSOFTAI.Orchestration/Modules/Common/`

### 5.2. Atomic Query (generic)
Tool handlers:
- `atomic.query.execute`

Mục tiêu:
- Thực thi stored procedure theo chuẩn `TILSOFTAI_sp_AtomicQuery_Template`.
- Parse kết quả theo RS0/RS1/RS2..N và tự routing dữ liệu (display/engine/both) theo schema.

Vị trí:
- `src/TILSOFTAI.Orchestration/Modules/Analytics/` (tool handler)

---

## 6. Enterprise Envelope Contract (Stage 2)

Mọi tool output đều bọc trong envelope:

```json
{
  "kind": "tilsoft.envelope.v1",
  "schemaVersion": 2,
  "generatedAtUtc": "2026-01-02T00:00:00Z",
  "meta": {
    "tenantId": "…",
    "userId": "…",
    "roles": ["…"],
    "correlationId": "…"
  },
  "ok": true,
  "data": { "...payload contract..." },
  "error": null,
  "telemetry": {
    "requestId": "…",
    "traceId": "…",
    "durationMs": 123
  },
  "policy": {
    "decision": "allow",
    "reasonCode": "OK",
    "rolesEvaluated": ["…"]
  },
  "source": {
    "type": "sql",
    "name": "dbo.TILSOFTAI_sp_models_stats_v1"
  },
  "evidence": [
    { "type": "metric", "key": "totalCount", "value": 13051 }
  ]
}
```

Quy tắc cho LLM:
- `ok=false` → đọc `error` + `policy`, **không retry cùng tool**.
- `ok=true` → đọc `data` và ưu tiên `evidence`.

---

## 7. Catalog (Filters & Actions)

- `filters.catalog`: liệt kê resource/filters hợp lệ (kèm alias/hints nếu có).
- `actions.catalog`: liệt kê WRITE actions chuẩn enterprise (prepare/commit, args, examples).

Các contract schema/examples nằm tại:
- `governance/contracts/v1/`

---

## 8. Hướng dẫn thêm tool mới (trong module hiện tại)

Ví dụ thêm tool `models.something`:

1) **Spec & validation**
- Thêm `ToolInputSpec` trong `ModelsToolInputSpecProvider`.

2) **Whitelist**
- Thêm `ToolDefinition` trong `ModelsToolRegistrationProvider`.

3) **Handler**
- Tạo `IToolHandler` mới trong `Modules/Models/Handlers/`.
- Register DI trong `TILSOFTAI.Api/Program.cs`:
  - `AddScoped<IToolHandler, YourNewHandler>()`

4) **Expose cho LLM**
- Thêm method tương ứng trong đúng plugin pack (`ModelsQuery/Options/Price/Write`).
- (Nếu cần) update `ModelsPluginExposurePolicy` để pack đó được expose đúng tình huống.

Lưu ý:
- Không đưa business logic vào core.
- Không cho phép tool call nếu tool chưa có spec + whitelist + handler.

---

## 9. Cấu hình & chạy dự án

### 9.1. Prerequisites
- .NET SDK phù hợp target framework của solution
- SQL Server
- LM Studio (OpenAI-compatible) hoặc provider tương thích

### 9.2. appsettings.json (SQL + LLM)

```json
{
  "ConnectionStrings": {
    "SqlServer": "Server=...;Database=...;User Id=...;Password=...;TrustServerCertificate=True;"
  },
  "LmStudio": {
    "BaseUrl": "http://localhost:1234",
    "Model": "local-model",
    "ModelMap": {
      "local-model": "local-model"
    }
  }
}
```

### 9.3. Run API
- Run project `TILSOFTAI.Api`
- Endpoint OpenAI-compatible: `/v1/chat/completions`

---

## 10. Testing checklist (khuyến nghị)

### Read flow
- “Có bao nhiêu model?” → `atomic.query.execute` (spName: `dbo.TILSOFTAI_sp_models_search`, đọc `RS1.summary.totalCount`).
- “Mùa 24/25 có bao nhiêu model?” → `atomic.query.execute` với `params.season`/`params.Season` tuỳ SP.

### Options flow
- “Model A có những tùy chọn nào?” → `atomic.query.execute` với stored procedure phù hợp (kết quả theo template Atomic).

### Write flow
- “Tạo/chỉnh dữ liệu …” → dùng `actions.catalog` để chọn action phù hợp, sau đó theo 2 bước prepare → confirm → commit.

### Error expected
- Thiếu quyền → envelope `ok=false`, `error.code=FORBIDDEN`, LLM không retry.

