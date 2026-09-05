namespace MuktoAin.Domain.Entities;

public class ChatMessage
{
    public int ChatMessageId { get; set; }
    public int ChatSessionId { get; set; }
    public ChatSession ChatSession { get; set; } = null!;

    // "user" or "assistant"
    public string Role { get; set; } = "user";

    public string Content { get; set; } = string.Empty;

    // JSON array of { "sectionId": n, "actTitle": "...", "sectionNumber": "..." }
    public string? CitedJson { get; set; }

    public DateTime CreatedAt { get; set; }
}
