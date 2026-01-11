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
            ChatTextKeys.FeatureNotAvailable => lang == ChatLanguage.En ? FeatureNotAvailableEn : FeatureNotAvailableVi,
            ChatTextKeys.NoToolsMode => lang == ChatLanguage.En ? NoToolsModeEn : NoToolsModeVi,
            ChatTextKeys.SynthesizeNoTools => lang == ChatLanguage.En ? SynthesizeNoToolsEn : SynthesizeNoToolsVi,
            ChatTextKeys.PreviousQueryHint => lang == ChatLanguage.En ? PreviousQueryHintEn : PreviousQueryHintVi,
            _ => string.Empty
        };
    }

    // Keep prompts short: policies and deterministic guards live in code.
    private const string SystemPromptVi = """
Bạn là trợ lý nghiệp vụ ERP.

Ngôn ngữ: Trả lời bằng tiếng Việt.

Quy tắc:
- Nếu câu hỏi cần dữ liệu nội bộ (model/khách hàng/đơn hàng/giá/tồn kho...), hãy dùng tools được cung cấp. Không bịa số liệu nếu chưa có evidence từ tool.
- Nếu cần chạy stored procedure theo chuẩn AtomicQuery mà chưa chắc spName, hãy gọi atomic_catalog_search trước để tìm đúng spName rồi mới gọi atomic_query_execute.
- Nếu không chắc filters/hành động hợp lệ, dùng filters-catalog / actions-catalog.
- Chỉ gọi tool có trong danh sách hệ thống.
- Thao tác ghi phải theo 2 bước: prepare -> yêu cầu người dùng xác nhận -> commit.
- Người dùng xác nhận bằng: XÁC NHẬN <confirmation_id>.
""";

    private const string SystemPromptEn = """
You are an ERP business assistant.

Language: Reply in English.

Rules:
- If the question requires internal data (models/customers/orders/prices/inventory...), use the provided tools. Do not fabricate numbers without tool evidence.
- If you need to execute an AtomicQuery stored procedure but are not sure about spName, call atomic_catalog_search first to find the best spName, then call atomic_query_execute.
- If you are unsure about valid filters or write parameters, use filters-catalog / actions-catalog.
- Only call tools that the system provides.
- Write operations must be 2-step: prepare -> ask the user to confirm -> commit.
- User confirms with: CONFIRM <confirmation_id> (or XÁC NHẬN <confirmation_id>).
""";

    private const string FeatureNotAvailableVi = "Hiện tại tôi chưa được cập nhật tính năng này! Vui lòng liên hệ quản trị hệ thống để kích hoạt hoặc bổ sung tool phù hợp.";
    private const string FeatureNotAvailableEn = "This capability is not available yet. Please contact your system administrator to enable or add the appropriate tool.";

    private const string NoToolsModeVi = "Bạn đang ở chế độ KHÔNG có tools. Hãy trả lời tự nhiên. KHÔNG tự tạo tool-call syntax.";
    private const string NoToolsModeEn = "Tools are not available for this turn. Reply normally. DO NOT fabricate tool-call syntax.";

    private const string SynthesizeNoToolsVi = "Bạn đã có đủ kết quả từ tools. Hãy trả lời trực tiếp, ngắn gọn, dựa trên evidence trong hội thoại. KHÔNG gọi tool. Không nhắc tới thông báo kỹ thuật nội bộ.";
    private const string SynthesizeNoToolsEn = "You already have tool results. Provide a concise final answer based on the evidence in the conversation. DO NOT call tools. Do not mention internal technical notices.";

    private const string PreviousQueryHintVi = "Ngữ cảnh truy vấn trước đó (dùng để hiểu câu hỏi nối tiếp): ";
    private const string PreviousQueryHintEn = "Previous query context (to understand the follow-up question): ";
}
