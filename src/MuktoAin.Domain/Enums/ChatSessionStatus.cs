namespace MuktoAin.Domain.Enums;

public enum ChatSessionStatus
{
    InProgress = 0,
    Committed = 1,

    // 3-strike safety escalation: consecutive blocked turns lock the chat.
    // Locked sessions refuse all further asks with a canned reply and are
    // excluded from resume lookup (GetOrCreateSessionAsync filters InProgress).
    Blocked = 2
}

