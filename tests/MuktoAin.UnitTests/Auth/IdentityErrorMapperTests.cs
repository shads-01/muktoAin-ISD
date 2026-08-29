using Microsoft.AspNetCore.Identity;
using MuktoAin.Web.Auth;

namespace MuktoAin.UnitTests.Auth;

// Covers the non-Identity logic added to AccountController's Register action:
// translating Identity's default IdentityResult error codes into bilingual,
// field-targeted ModelState messages (see IdentityErrorMapper).
public class IdentityErrorMapperTests
{
    [Theory]
    [InlineData("PasswordTooShort")]
    [InlineData("PasswordRequiresUpper")]
    [InlineData("PasswordRequiresLower")]
    [InlineData("PasswordRequiresDigit")]
    [InlineData("PasswordRequiresNonAlphanumeric")]
    [InlineData("PasswordRequiresUniqueChars")]
    [InlineData("UserAlreadyHasPassword")]
    public void Map_PasswordPolicyCodes_ReturnPasswordField(string code)
    {
        var error = new IdentityError { Code = code, Description = "original" };

        var (field, message) = IdentityErrorMapper.Map(error);

        Assert.Equal("Password", field);
        Assert.Contains("Password", message, StringComparison.OrdinalIgnoreCase);
        // Never leaks Identity's raw English-only description for localizable codes.
        Assert.DoesNotContain("original", message);
    }

    [Theory]
    [InlineData("DuplicateUserName")]
    [InlineData("DuplicateEmail")]
    [InlineData("InvalidEmail")]
    public void Map_EmailCodes_ReturnEmailField(string code)
    {
        var error = new IdentityError { Code = code, Description = "original" };

        var (field, message) = IdentityErrorMapper.Map(error);

        Assert.Equal("Email", field);
        Assert.Contains("email", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("original", message);
    }

    [Fact]
    public void Map_UnknownCode_FallsBackToModelLevelWithOriginalDescription()
    {
        var error = new IdentityError { Code = "ConcurrencyFailure", Description = "ugly english description" };

        var (field, message) = IdentityErrorMapper.Map(error);

        Assert.Null(field);
        Assert.Equal("ugly english description", message);
    }
}