using Microsoft.AspNetCore.Authorization;
using MuktoAin.Web.Controllers;

namespace MuktoAin.UnitTests.Controllers;

// Testing_Plan.md ADM-03 / ADM-09: a non-admin must never reach /Admin/*.
// AdminController has no service seam that isolates authorization from the
// rest of its (heavy) constructor, so the guard is verified declaratively --
// confirming the [Authorize(Roles = "Admin")] attribute is present and scoped
// to Admin is what actually gates every action, VerifyLawyer included.
public class AdminControllerTests
{
    [Fact]
    public void AdminController_IsGatedByAdminRoleAuthorization()
    {
        var attribute = typeof(AdminController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attribute);
        Assert.Equal("Admin", attribute!.Roles);
    }
}
