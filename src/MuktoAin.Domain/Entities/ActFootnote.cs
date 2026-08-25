namespace MuktoAin.Domain.Entities;

public class ActFootnote
{
    public int FootnoteId { get; set; }
    public int ActId { get; set; }
    public Act Act { get; set; } = null!;

    public int FootnoteOrder { get; set; }

    // Crucial amendment records
    public string FootnoteText { get; set; } = string.Empty;
}
