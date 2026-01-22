namespace TILSOFTAI.Api.Localization;

public interface IApiTextLocalizer
{
    string Get(string key, params object[] args);
}
