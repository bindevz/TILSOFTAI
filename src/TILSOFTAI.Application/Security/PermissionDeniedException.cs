namespace TILSOFTAI.Application.Security;

public sealed class PermissionDeniedException(string message, Exception? innerException = null) : Exception(message, innerException);

