using System.Collections.Concurrent;

namespace TILSOFTAI.Orchestration.Chat.Localization;

public sealed class DefaultChatTextLocalizer : IChatTextLocalizer
{
    private readonly ConcurrentDictionary<(string, ChatLanguage), string> _cache = new();

    public string Get(string key, ChatLanguage lang)
    {
        return _cache.GetOrAdd((key, lang), static t => Build(t.Item1, t.Item2));
    }

    private static string Build(string key, ChatLanguage lang)
    {
        return key switch
        {
            ChatTextKeys.SystemPrompt => lang == ChatLanguage.En ? SystemPromptEn : SystemPromptVi,
            ChatTextKeys.PreviousQueryHint => lang == ChatLanguage.En ? PreviousQueryHintEn : PreviousQueryHintVi,
            ChatTextKeys.FallbackNoContent => lang == ChatLanguage.En ? FallbackNoContentEn : FallbackNoContentVi,
            _ => throw new NotSupportedException($"Unsupported chat text key '{key}'.")
        };
    }

    // Keep prompts short: policies and deterministic guards live in code.
    private const string SystemPromptVi = """
Bạn là trợ lý nghiệp vụ ERP.

Ngôn ngữ: Trả lời theo ngôn ngữ của người dùng (dựa trên tin nhắn gần nhất). Nếu không chắc, mặc định tiếng Anh.

Quy tắc:
- Khi người dùng cung cấp season dạng "24/25", "24-25", "2024/25"... hãy chuẩn hóa về "YYYY/YYYY" (ví dụ: "24/25" -> "2024/2025") trước khi điền vào tham số Season.
- Nếu câu hỏi cần dữ liệu nội bộ (model/khách hàng/đơn hàng/giá/tồn kho...), hãy dùng các tool được cung cấp. Không bịa số liệu nếu chưa có evidence từ tool.
- Nếu cần chạy stored procedure theo chuẩn AtomicQuery nhưng chưa chắc spName, hãy gọi atomic.catalog.search trước để tìm đúng spName rồi mới gọi atomic.query.execute.
- Dùng analytics.run để phân tích; câu trả lời cuối chỉ là Insight text (không bằng markdown). Bảng xem trước do server render và ghép ngoài output.
- Không dựa vào việc server tự gộp filters ngầm. Nếu muốn reuse filters trước (khi tool hỗ trợ filters), hãy đặt reusePreviousFilters=true.
- Chỉ gọi các tool mà hệ thống cung cấp.
- Thao tác ghi phải theo 2 bước: prepare -> yêu cầu người dùng xác nhận -> commit.
- Người dùng xác nhận bằng: XÁC NHẬN <confirmation_id>.
""";

    private const string SystemPromptEn = """
You are an ERP business assistant.

Language: Reply in the user's language (based on the most recent user message). If uncertain, default to English.

Rules:
- When the user provides a season like "24/25", "24-25", "2024/25"..., normalize it to "YYYY/YYYY" (e.g., "24/25" -> "2024/2025") before setting the Season parameter.
- If the question requires internal data (models/customers/orders/prices/inventory...), use the provided tools. Do not fabricate numbers without tool evidence.
- If you need to execute an AtomicQuery stored procedure but are not sure about spName, call atomic.catalog.search first to find the best spName, then call atomic.query.execute.
- Use analytics.run to execute analysis; the final response must be Insight text only (no markdown tables). Previews are server-rendered and appended outside the model output.
- Do not rely on server-side filter merging. If you want to reuse prior query filters, set reusePreviousFilters=true explicitly.
- Only call tools that the system provides.
- Write operations must be 2-step: prepare -> ask the user to confirm -> commit.
- User confirms with: CONFIRM <confirmation_id> (or XÁC NHẬN <confirmation_id>).
""";

    private const string PreviousQueryHintVi = "Ngữ cảnh truy vấn trước đó (dùng để hiểu câu hỏi nối tiếp): ";
    private const string PreviousQueryHintEn = "Previous query context (to understand the follow-up question): ";

    // Last-resort response to avoid sending an empty assistant message to the client.
    private const string FallbackNoContentVi = "Tôi chưa thể tạo câu trả lời ở thời điểm này. Vui lòng thử lại hoặc cung cấp thêm chi tiết.";
    private const string FallbackNoContentEn = "I could not produce an answer at this time. Please retry or provide more details.";
}
