using System.ComponentModel.DataAnnotations;
using MuktoAin.Web.ViewModels;
using Xunit;

namespace MuktoAin.UnitTests.ViewModels;

public class CaseSubmitViewModelTests
{
    private static bool TryValidate(CaseSubmitViewModel vm, out List<ValidationResult> results)
    {
        var context = new ValidationContext(vm);
        results = new List<ValidationResult>();
        return Validator.TryValidateObject(vm, context, results, validateAllProperties: true);
    }

    private static CaseSubmitViewModel ValidVm() => new()
    {
        CategoryId = 1,
        DistrictId = 1,
        Title = "৩ মাসের বকেয়া বেতন",
        Description = "নিয়োগকর্তা গত তিন মাস ধরে বেতন পরিশোধ করছেন না।",
        Language = "bn",
    };

    [Fact]
    public void Valid_Model_Passes()
    {
        Assert.True(TryValidate(ValidVm(), out _));
    }

    [Fact]
    public void Empty_Title_Fails()
    {
        var vm = ValidVm();
        vm.Title = "";
        Assert.False(TryValidate(vm, out var results));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CaseSubmitViewModel.Title)));
    }

    [Fact]
    public void Title_Over_250_Chars_Fails()
    {
        var vm = ValidVm();
        vm.Title = new string('অ', 251);
        Assert.False(TryValidate(vm, out var results));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CaseSubmitViewModel.Title)));
    }

    [Fact]
    public void Description_Under_20_Chars_Fails()
    {
        var vm = ValidVm();
        vm.Description = "too short";
        Assert.False(TryValidate(vm, out var results));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CaseSubmitViewModel.Description)));
    }

    [Fact]
    public void Description_Over_5000_Chars_Fails()
    {
        var vm = ValidVm();
        vm.Description = new string('ক', 5001);
        Assert.False(TryValidate(vm, out var results));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CaseSubmitViewModel.Description)));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("bangla")]
    [InlineData("BN-1")]
    public void Invalid_Language_Fails(string language)
    {
        var vm = ValidVm();
        vm.Language = language;
        Assert.False(TryValidate(vm, out var results));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CaseSubmitViewModel.Language)));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("bn")]
    public void Valid_Language_Passes(string language)
    {
        var vm = ValidVm();
        vm.Language = language;
        Assert.True(TryValidate(vm, out _));
    }

    [Fact]
    public void DistrictId_Zero_Fails()
    {
        var vm = ValidVm();
        vm.DistrictId = 0;
        Assert.False(TryValidate(vm, out var results));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CaseSubmitViewModel.DistrictId)));
    }

    [Fact]
    public void CategoryId_Negative_Fails()
    {
        var vm = ValidVm();
        vm.CategoryId = -1;
        Assert.False(TryValidate(vm, out var results));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CaseSubmitViewModel.CategoryId)));
    }
}
