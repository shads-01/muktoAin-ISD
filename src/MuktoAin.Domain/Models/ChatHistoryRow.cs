using MuktoAin.Domain.Enums;

namespace MuktoAin.Domain.Models;

public class ChatHistoryRow
{
    public int ChatSessionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public int MessageCount { get; set; }
    public ChatSessionStatus Status { get; set; }
    public int? CaseId { get; set; }
}
