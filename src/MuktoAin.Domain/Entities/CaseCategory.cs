namespace MuktoAin.Domain.Entities;

public class CaseCategory
{
    public int CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public ICollection<Case> Cases { get; set; } = new List<Case>();
}
