using MuktoAin.Domain.Enums;

namespace MuktoAin.Domain.Entities;

// A citizen chat conversation on the home page. InProgress sessions are
// resumable from the recent-chats strip; Committed sessions have become
// cases and their transcript stays attached to the case forever.
public class ChatSession
{
    public int ChatSessionId { get; set; }

    // NULL for guest sessions — guests are matched by SessionKey instead
    public int? UserId { get; set; }
    public User? User { get; set; }

    // Random 22-char key kept in the guest's browser session (ASP.NET session
    // value "mkt-chatkey"). Unique constraint in DB.
    public string? SessionKey { get; set; }

    public string Title { get; set; } = string.Empty;

    public ChatSessionStatus Status { get; set; } = ChatSessionStatus.InProgress;

    public int? CommittedCaseId { get; set; }
    public Case? CommittedCase { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
