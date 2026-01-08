using TILSOFTAI.Orchestration.Chat;
using TILSOFTAI.Orchestration.SK.Conversation;

namespace TILSOFTAI.Orchestration.Chat.Localization;

public interface ILanguageResolver
{
    ChatLanguage Resolve(IReadOnlyCollection<ChatCompletionMessage> incomingMessages,
        ConversationState? conversationState);
}
