using System.ComponentModel.DataAnnotations;
using MuktoAin.Web.ViewModels;

namespace MuktoAin.UnitTests.ViewModels;

// Covers ProfileViewModel.PhoneNumber validation. The built-in [Phone]
// DataAnnotation is notoriously loose (it accepts things like "017"), which
// let malformed numbers through the profile-edit form. These tests pin the
// Bangladesh-specific format that replaces it.
public class ProfileViewModelTests
{
    private static List<ValidationResult> ValidatePhoneNumber(string? phoneNumber)
    {
        var model = new ProfileViewModel { FullName = "Test User", PhoneNumber = phoneNumber };
        var context = new ValidationContext(model) { MemberName = nameof(ProfileViewModel.PhoneNumber) };
        var results = new List<ValidationResult>();
        Validator.TryValidateProperty(model.PhoneNumber, context, results);
        return results;
    }

    [Theory]
    [InlineData("01712345678")]
    [InlineData("01812345678")]
    [InlineData("01912345678")]
    [InlineData("+8801712345678")]
    [InlineData("8801712345678")]
    public void Valid_BangladeshMobileNumbers_PassValidation(string phoneNumber)
    {
        var results = ValidatePhoneNumber(phoneNumber);

        Assert.Empty(results);
    }

    [Theory]
    [InlineData("017")]
    [InlineData("01712345")]
    [InlineData("017123456789")]
    [InlineData("01212345678")]
    [InlineData("abcdefghijk")]
    public void Invalid_MalformedNumbers_FailValidation(string phoneNumber)
    {
        var results = ValidatePhoneNumber(phoneNumber);

        Assert.NotEmpty(results);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Blank_PhoneNumber_PassesValidation_AsItIsOptional(string? phoneNumber)
    {
        var results = ValidatePhoneNumber(phoneNumber);

        Assert.Empty(results);
    }
}
