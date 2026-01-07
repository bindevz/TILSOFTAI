# TILSOFTAI (ERP AI Orchestrator)

TILSOFTAI là nền tảng **AI Orchestrator cho ERP**: kết nối Open WebUI (OpenAI-compatible client) với LLM (LM Studio/OpenAI-compatible) và SQL Server thông qua API .NET 8, theo mô hình **Tool/Function Calling** với **guardrails chuẩn enterprise**.

Phiên bản **ver13** tập trung vào 3 mục tiêu:
1) **Tách nghiệp vụ theo Module** (hiện tại migrate **Models**), Core không phình theo số lượng nghiệp vụ.
2) **Chọn tool thông minh** để tránh overload (không load toàn bộ tools vào LLM mỗi lượt).
3) **Clean code**: loại bỏ cơ chế registry/dispatcher hardcode cũ, giảm điểm nghẽn khi mở rộng.

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

### 2.2. Tách nghiệp vụ theo Module (ver13)
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

Ở quy mô lớn, không nên expose toàn bộ tools cho LLM mỗi lượt. Ver13 triển khai 2 lớp chọn lọc:

### 4.1. Level 2: ModuleRouter (chọn module theo message)
`ModuleRouter.SelectModules(userMessage, context)` trả về danh sách module liên quan.

Ví dụ:
- Người dùng nói về **model/sản phẩm/giá/attribute** → chọn module `models`.
- Nếu có module business → luôn thêm module `common` (catalog tools).

Nếu không match module nào → expose **0 tool** (LLM trả lời tự nhiên theo system prompt, không được bịa tool).

### 4.2. Level 3: Plugin Exposure Policy (chọn tool pack trong module)
Trong 1 module có thể có nhiều tool. Ver13 chia plugin theo **tool packs** và chỉ expose pack cần thiết.

- Interface: `IPluginExposurePolicy`
- `ModelsPluginExposurePolicy` chọn pack dựa trên heuristic:
  - Hỏi về **options/attributes/constraints** → expose `ModelsOptionsToolsPlugin`
  - Hỏi về **price/cost** → expose `ModelsPriceToolsPlugin`
  - Hỏi về **create/commit** → expose `ModelsWriteToolsPlugin`
  - Mặc định luôn expose `ModelsQueryToolsPlugin`

Kết quả: giảm số function exposures, giảm token overhead, tăng ổn định.

---

## 5. Modules hiện có (ver13)

### 5.1. Common module
- `filters.catalog`
- `actions.catalog`

Vị trí:
- `src/TILSOFTAI.Orchestration/Modules/Common/`

### 5.2. Models module (đã migrate)
Tool handlers:
- `models.search`
- `models.count`
- `models.stats`
- `models.options`
- `models.get`
- `models.attributes.list`
- `models.price.analyze`
- `models.create.prepare`
- `models.create.commit`

Plugin packs:
- `ModelsQueryToolsPlugin`
- `ModelsOptionsToolsPlugin`
- `ModelsPriceToolsPlugin`
- `ModelsWriteToolsPlugin`

Vị trí:
- `src/TILSOFTAI.Orchestration/Modules/Models/`
- `src/TILSOFTAI.Orchestration/SK/Plugins/`

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
- “Có bao nhiêu model?” → `models.count` hoặc `models.stats`
- “Mùa 24/25 có bao nhiêu model?” → season normalize + `models.stats`

### Options flow
- “Model A có những tùy chọn nào?” → `models.search` → `models.options`

### Write flow
- “Tạo model …” → `models.create.prepare` → người dùng xác nhận → `models.create.commit`

### Error expected
- Thiếu quyền → envelope `ok=false`, `error.code=FORBIDDEN`, LLM không retry.

