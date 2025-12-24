using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Llm;

namespace TILSOFTAI.Orchestration.Chat;

public sealed class SemanticResolver
{
    public SeasonCode NormalizeSeason(string value)
    {
        try
        {
            return SeasonCode.Parse(value);
        }
        catch (ArgumentException ex)
        {
            throw new ResponseContractException(ex.Message);
        }
    }

    public MetricCode NormalizeMetric(string value)
    {
        try
        {
            return MetricCode.Parse(value);
        }
        catch (ArgumentException ex)
        {
            throw new ResponseContractException(ex.Message);
        }
    }
}
