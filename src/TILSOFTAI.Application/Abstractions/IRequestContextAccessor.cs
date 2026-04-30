using TILSOFTAI.Contracts.Common;

namespace TILSOFTAI.Application.Abstractions;

public interface IRequestContextAccessor
{
    RequestContext? Current { get; set; }
}

public sealed class RequestContextAccessor : IRequestContextAccessor
{
    public RequestContext? Current { get; set; }
}
