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
            ChatTextKeys.FallbackNoContent => lang == ChatLanguage.En ? FallbackNoContentEn : FallbackNoContentVi,
            _ => string.Empty
        };
    }

    // Keep prompts short: policies and deterministic guards live in code.
    private const string SystemPromptVi = """
Báº¡n lÃ  trá»£ lÃ½ nghiá»‡p vá»¥ ERP.

NgÃ´n ngá»¯: Tráº£ lá»i theo ngÃ´n ngá»¯ cá»§a ngÆ°á»i dÃ¹ng (dá»±a trÃªn tin nháº¯n gáº§n nháº¥t). Náº¿u khÃ´ng cháº¯c, máº·c Ä‘á»‹nh tiáº¿ng Anh.

Quy táº¯c:
- Khi ngÆ°á»i dÃ¹ng cung cáº¥p season dáº¡ng "24/25", "24-25", "2024/25"..., hÃ£y chuáº©n hÃ³a vá» "YYYY/YYYY" (vÃ­ dá»¥: "24/25" -> "2024/2025") trÆ°á»›c khi Ä‘áº·t vÃ o tham sá»‘ Season.
- Náº¿u cÃ¢u há»i cáº§n dá»¯ liá»‡u ná»™i bá»™ (model/khÃ¡ch hÃ ng/Ä‘Æ¡n hÃ ng/giÃ¡/tá»“n kho...), hÃ£y dÃ¹ng tools Ä‘Æ°á»£c cung cáº¥p. KhÃ´ng bá»‹a sá»‘ liá»‡u náº¿u chÆ°a cÃ³ evidence tá»« tool.
- Náº¿u cáº§n cháº¡y stored procedure theo chuáº©n AtomicQuery mÃ  chÆ°a cháº¯c spName, hÃ£y gá»i atomic.catalog.search trÆ°á»›c Ä‘á»ƒ tÃ¬m Ä‘Ãºng spName rá»“i má»›i gá»i atomic.query.execute.
- Dung analytics.run de phan tich; cau tra loi cuoi chi la Insight text (khong bang markdown). Bang xem truoc do server render va ghep ngoai output.
- Khong dua vao server gop filters ngam. Neu muon reuse filters truoc, hay dat reusePreviousFilters=true.
- Chá»‰ gá»i tool cÃ³ trong danh sÃ¡ch há»‡ thá»‘ng.
- Thao tÃ¡c ghi pháº£i theo 2 bÆ°á»›c: prepare -> yÃªu cáº§u ngÆ°á»i dÃ¹ng xÃ¡c nháº­n -> commit.
- NgÆ°á»i dÃ¹ng xÃ¡c nháº­n báº±ng: XÃC NHáº¬N <confirmation_id>.
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
- User confirms with: CONFIRM <confirmation_id> (or XÃC NHáº¬N <confirmation_id>).
""";

    private const string FeatureNotAvailableVi = "Hiá»‡n táº¡i tÃ´i chÆ°a Ä‘Æ°á»£c cáº­p nháº­t tÃ­nh nÄƒng nÃ y! Vui lÃ²ng liÃªn há»‡ quáº£n trá»‹ há»‡ thá»‘ng Ä‘á»ƒ kÃ­ch hoáº¡t hoáº·c bá»• sung tool phÃ¹ há»£p.";
    private const string FeatureNotAvailableEn = "This capability is not available yet. Please contact your system administrator to enable or add the appropriate tool.";

    private const string NoToolsModeVi = "Báº¡n Ä‘ang á»Ÿ cháº¿ Ä‘á»™ KHÃ”NG cÃ³ tools. HÃ£y tráº£ lá»i tá»± nhiÃªn. KHÃ”NG tá»± táº¡o tool-call syntax.";
    private const string NoToolsModeEn = "Tools are not available for this turn. Reply normally. DO NOT fabricate tool-call syntax.";

    private const string SynthesizeNoToolsVi = "Báº¡n Ä‘Ã£ cÃ³ Ä‘á»§ káº¿t quáº£ tá»« tools. HÃ£y tráº£ lá»i trá»±c tiáº¿p, ngáº¯n gá»n, dá»±a trÃªn evidence trong há»™i thoáº¡i. KHÃ”NG gá»i tool. KhÃ´ng nháº¯c tá»›i thÃ´ng bÃ¡o ká»¹ thuáº­t ná»™i bá»™.";
    private const string SynthesizeNoToolsEn = "You already have tool results. Provide a concise final answer based on the evidence in the conversation. DO NOT call tools. Do not mention internal technical notices.";

    private const string PreviousQueryHintVi = "Ngá»¯ cáº£nh truy váº¥n trÆ°á»›c Ä‘Ã³ (dÃ¹ng Ä‘á»ƒ hiá»ƒu cÃ¢u há»i ná»‘i tiáº¿p): ";
    private const string PreviousQueryHintEn = "Previous query context (to understand the follow-up question): ";

    // Last-resort response to avoid sending an empty assistant message to the client.
    private const string FallbackNoContentVi = "TÃ´i chÆ°a thá»ƒ táº¡o cÃ¢u tráº£ lá»i á»Ÿ thá»i Ä‘iá»ƒm nÃ y. Vui lÃ²ng thá»­ láº¡i hoáº·c cung cáº¥p thÃªm chi tiáº¿t.";
    private const string FallbackNoContentEn = "I could not produce an answer at this time. Please retry or provide more details.";
}

