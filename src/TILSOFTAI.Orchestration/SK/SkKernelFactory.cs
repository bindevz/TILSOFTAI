using Microsoft.SemanticKernel;
using TILSOFTAI.Orchestration.Llm;

namespace TILSOFTAI.Orchestration.SK;

public sealed class SkKernelFactory
{
    private readonly LmStudioOptions _lm;

    public SkKernelFactory(LmStudioOptions lm)
    {
        _lm = lm;
    }

    public Kernel CreateKernel(string? requestedModel)
    {
        var builder = Kernel.CreateBuilder();

        var logical = string.IsNullOrWhiteSpace(requestedModel) ? _lm.Model : requestedModel;
        if (!_lm.ModelMap.TryGetValue(logical, out var mapped))
        {
            mapped = _lm.ModelMap[_lm.Model];
        }

        // LM Studio OpenAI-compatible endpoint
        var endpoint = new Uri(_lm.BaseUrl.TrimEnd('/') + "/v1");

        builder.AddOpenAIChatCompletion(
            modelId: mapped,
            apiKey: "lm-studio",
            endpoint: endpoint);

        return builder.Build();
    }
}
