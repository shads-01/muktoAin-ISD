namespace MuktoAin.Domain.Entities;

public class District
{
    public byte DistrictId { get; set; }
    public string Name { get; set; } = string.Empty;

    public ICollection<Case> Cases { get; set; } = new List<Case>();
}
