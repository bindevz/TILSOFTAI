namespace TILSOFTAI.Orchestration.Chat.Localization;

public interface ILanguageResolver
{
    string Resolve(IReadOnlyCollection<ChatCompletionMessage> incomingMessages);
}
