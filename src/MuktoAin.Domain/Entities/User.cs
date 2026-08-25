using Microsoft.AspNetCore.Identity;
using MuktoAin.Domain.Enums;

namespace MuktoAin.Domain.Entities;

// Decision point (Step 1.3, Option A): inherits IdentityUser<int> directly
// rather than keeping a plain POCO mapped separately by Shads's Infrastructure layer.
// This pulls Microsoft.Extensions.Identity.Stores into Domain as a pragmatic, deliberate
// exception to the "zero dependencies" rule -- it is a thin abstractions package with no
// EF Core dependency. IdentityUser<int> already supplies Id (PK), UserName, Email,
// PhoneNumber, etc.; only the fields specific to this domain are added below.
// Coordinate with Shads before relying on this in ASP.NET Core Identity configuration (S-1.1).
public class User : IdentityUser<int>
{
    public string FullName { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public AccountStatus AccountStatus { get; set; }
    public string PreferredLanguage { get; set; } = "bn";

    // Enforces "admins can only be created by admins" (design.md 2.1)
    public int? CreatedByAdminId { get; set; }
    public User? CreatedByAdmin { get; set; }

    public DateTime CreatedAt { get; set; }
}
