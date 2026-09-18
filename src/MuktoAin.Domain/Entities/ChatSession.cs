using MuktoAin.Domain.Enums;

namespace MuktoAin.Domain.Entities;

// A citizen chat conversation on the home page. InProgress sessions are
// resumable from the history sidebar; Committed sessions have become
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

    // Conversational intake (spec 3.2): structured slots the model re-emits each
    // turn as an opaque JSON object — C# never parses its shape, only stores it
    // (last write wins) and flattens it for the explain turn / commit.
    public string? CaseFileJson { get; set; }

    // Citizen's detected language ("bn" | "en"), used by the explain turn.
    public string? Language { get; set; }

    public ChatSessionStatus Status { get; set; } = ChatSessionStatus.InProgress;

    // Consecutive safety-blocked turns (layer-1 filter or layer-2 intent).
    // A normal turn resets it; at ChatService's threshold the session locks.
    public int BlockedStreak { get; set; }

    public int? CommittedCaseId { get; set; }
    public Case? CommittedCase { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
