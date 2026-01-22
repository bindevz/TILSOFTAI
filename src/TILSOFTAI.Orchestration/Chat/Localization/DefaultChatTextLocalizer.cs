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
            ChatTextKeys.FallbackNoContent => lang == ChatLanguage.En ? FallbackNoContentEn : FallbackNoContentVi,
            ChatTextKeys.InsightBlockTitle => lang == ChatLanguage.En ? InsightBlockTitleEn : InsightBlockTitleVi,
            ChatTextKeys.InsightPreviewTitle => lang == ChatLanguage.En ? InsightPreviewTitleEn : InsightPreviewTitleVi,
            ChatTextKeys.ListPreviewTitle => lang == ChatLanguage.En ? ListPreviewTitleEn : ListPreviewTitleVi,
            ChatTextKeys.TableTruncationNote => lang == ChatLanguage.En ? TableTruncationNoteEn : TableTruncationNoteVi,
            _ => throw new NotSupportedException($"Unsupported chat text key '{key}'.")
        };
    }

    // Keep prompts short: policies and deterministic guards live in code.
    private const string SystemPromptVi = """
B?n là tr? lý nghi?p v? ERP.

Ngôn ng?: Tr? l?i theo ngôn ng? c?a ngu?i dùng (d?a trên tin nh?n g?n nh?t). N?u không ch?c, m?c d?nh ti?ng Anh.

Quy t?c:
- Khi ngu?i dùng cung c?p season d?ng "24/25", "24-25", "2024/25"... hãy chu?n hóa v? "YYYY/YYYY" (ví d?: "24/25" -> "2024/2025") tru?c khi di?n vào tham s? Season.
- N?u câu h?i c?n d? li?u n?i b? (model/khách hàng/don hàng/giá/t?n kho...), hãy dùng các tool du?c cung c?p. Không b?a s? li?u n?u chua có evidence t? tool.
- N?u c?n ch?y stored procedure theo chu?n AtomicQuery nhung chua ch?c spName, hãy g?i atomic.catalog.search tru?c d? tìm dúng spName r?i m?i g?i atomic.query.execute.
- Dùng analytics.run d? phân tích; câu tr? l?i cu?i ch? là Insight text (không b?ng markdown). B?ng xem tru?c do server render và ghép ngoài output.
- Ch? g?i các tool mà h? th?ng cung c?p.
- Thao tác ghi ph?i theo 2 bu?c: prepare -> yêu c?u ngu?i dùng xác nh?n -> commit.
- Ngu?i dùng xác nh?n b?ng: XÁC NH?N <confirmation_id>.
""";

    private const string SystemPromptEn = """
You are an ERP business assistant.

Language: Reply in the user's language (based on the most recent user message). If uncertain, default to English.

Rules:
- When the user provides a season like "24/25", "24-25", "2024/25"..., normalize it to "YYYY/YYYY" (e.g., "24/25" -> "2024/2025") before setting the Season parameter.
- If the question requires internal data (models/customers/orders/prices/inventory...), use the provided tools. Do not fabricate numbers without tool evidence.
- If you need to execute an AtomicQuery stored procedure but are not sure about spName, call atomic.catalog.search first to find the best spName, then call atomic.query.execute.
- Use analytics.run to execute analysis; the final response must be Insight text only (no markdown tables). Previews are server-rendered and appended outside the model output.
- Only call tools that the system provides.
- Write operations must be 2-step: prepare -> ask the user to confirm -> commit.
- User confirms with: CONFIRM <confirmation_id> (or XÁC NH?N <confirmation_id>).
""";

    // Last-resort response to avoid sending an empty assistant message to the client.
    private const string FallbackNoContentVi = "Hi?n t?i tôi chua th? t?o câu tr? l?i. Vui lòng th? l?i ho?c cung c?p thêm chi ti?t.";
    private const string FallbackNoContentEn = "I could not produce an answer at this time. Please retry or provide more details.";

    private const string InsightBlockTitleVi = "K?t lu?n / Insight";
    private const string InsightBlockTitleEn = "Conclusion / Insight";

    private const string InsightPreviewTitleVi = "Preview d? li?u c?a K?t lu?n / Insight";
    private const string InsightPreviewTitleEn = "Insight Preview";

    private const string ListPreviewTitleVi = "Preview danh sách";
    private const string ListPreviewTitleEn = "List Preview";

    private const string TableTruncationNoteVi = "Ðã hi?n th? {shown}/{total} dòng.";
    private const string TableTruncationNoteEn = "Showing {shown}/{total} rows.";
}
