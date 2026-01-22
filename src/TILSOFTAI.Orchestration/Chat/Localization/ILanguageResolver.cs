using TILSOFTAI.Orchestration.Chat;

namespace TILSOFTAI.Orchestration.Chat.Localization;

public interface ILanguageResolver
{
    ChatLanguage Resolve(IReadOnlyCollection<ChatCompletionMessage> incomingMessages);
}
