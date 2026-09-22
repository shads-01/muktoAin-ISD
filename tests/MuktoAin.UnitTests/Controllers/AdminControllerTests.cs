using System.Linq;
using Microsoft.AspNetCore.Authorization;
using MuktoAin.Web.Controllers;
using Xunit;

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

    [Fact]
    public void TelemetryEndpoints_DoNotAllowAnonymous()
    {
        var progressMethod = typeof(AdminController).GetMethod(nameof(AdminController.EmbeddingProgress));
        Assert.NotNull(progressMethod);
        Assert.False(progressMethod!.IsDefined(typeof(AllowAnonymousAttribute), inherit: true));

        var keyStatusMethod = typeof(AdminController).GetMethod(nameof(AdminController.GeminiKeyStatus));
        Assert.NotNull(keyStatusMethod);
        Assert.False(keyStatusMethod!.IsDefined(typeof(AllowAnonymousAttribute), inherit: true));
    }

    [Theory]
    [InlineData(nameof(AdminController.CreateAdmin))]
    [InlineData(nameof(AdminController.SuspendAdmin))]
    [InlineData(nameof(AdminController.PromoteAdmin))]
    [InlineData(nameof(AdminController.RefundOrder))]
    [InlineData(nameof(AdminController.ApprovePayout))]
    public void Action_IsGatedBySuperAdminOnlyPolicy(string actionName)
    {
        var methods = typeof(AdminController).GetMethods()
            .Where(m => m.Name == actionName)
            .ToList();

        Assert.NotEmpty(methods);
        Assert.All(methods, m =>
        {
            var attr = m.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .Cast<AuthorizeAttribute>()
                .SingleOrDefault(a => a.Policy == "SuperAdminOnly");
            Assert.NotNull(attr);
        });
    }
}
