namespace TILSOFTAI.Orchestration.SK.Planning;

public sealed class PlannerRouter
{
    public bool ShouldUseLoop(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText)) return false;

        // Heuristic đơn giản, bạn có thể tinh chỉnh sau
        var lower = userText.ToLowerInvariant();

        // Câu có nhiều ý / yêu cầu phân tích / so sánh / báo cáo / xu hướng...
        var keywords = new[]
        {
            "phân tích", "so sánh", "xu hướng", "báo cáo", "report", "trend",
            "tổng hợp", "đánh giá", "đề xuất", "nguyên nhân", "root cause",
            "theo thị trường", "theo khách hàng", "lợi nhuận"
        };

        var score = 0;
        if (userText.Length >= 220) score += 2;
        if (userText.Length >= 400) score += 2;

        foreach (var k in keywords)
            if (lower.Contains(k)) score += 2;

        // Có nhiều liên từ/cụm đa bước
        if (lower.Contains("và") || lower.Contains("sau đó") || lower.Contains("tiếp theo"))
            score += 2;

        return score >= 6;
    }
}
