namespace TILSOFTAI.SqlTests;

public static class TestAssert
{
    public static void Contains(string value, string expected)
    {
        if (!value.Contains(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Expected to find '{expected}'.");
    }

    public static void DoesNotContain(string value, string unexpected)
    {
        if (value.Contains(unexpected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Did not expect to find '{unexpected}'.");
    }
}

