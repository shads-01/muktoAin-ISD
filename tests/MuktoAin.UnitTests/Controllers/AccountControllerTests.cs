using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Web.Controllers;
using MuktoAin.Web.ViewModels;

namespace MuktoAin.UnitTests.Controllers;

// Unit coverage for AccountController's non-Identity logic: the lawyer-bar-number
// guard, bilingual redirects, suspended/locked messaging, and the IdentityResult
// -> ModelState error mapping. Password hashing, claim factories and Identity's
// own internals are mocked at the boundary and not re-tested here.
public class AccountControllerTests
{
    private readonly Mock<UserManager<User>> _userManager;
    private readonly Mock<SignInManager<User>> _signInManager;
    private readonly Mock<IRepository<LawyerProfile>> _lawyerProfileRepo;
    private readonly AccountController _controller;

    public AccountControllerTests()
    {
        _lawyerProfileRepo = new Mock<IRepository<LawyerProfile>>();
        _lawyerProfileRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        _userManager = NewUserManager();
        _signInManager = NewSignInManager(_userManager.Object);

        var httpContext = new DefaultHttpContext();
        _controller = new AccountController(
            _signInManager.Object,
            _userManager.Object,
            _lawyerProfileRepo.Object,
            Mock.Of<ILogger<AccountController>>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
        };
    }

    [Fact]
    public async Task Login_WhenSuspendedUser_ReturnsViewWithSuspendedMessage()
    {
        _userManager.Setup(m => m.FindByEmailAsync("suspended@example.com"))
            .ReturnsAsync(new User { Id = 3, Role = UserRole.Citizen, AccountStatus = AccountStatus.Suspended });

        var result = await _controller.Login(new LoginViewModel { Email = "suspended@example.com", Password = "Citizen@123" });

        var view = Assert.IsType<ViewResult>(result);
        var error = Assert.Single(_controller.ModelState[string.Empty]!.Errors);
        Assert.Contains("suspended", error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        _signInManager.Verify(m => m.PasswordSignInAsync(
            It.IsAny<User>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Login_WhenLockedOut_ReturnsViewWithLockedMessage()
    {
        _userManager.Setup(m => m.FindByEmailAsync("citizen@example.com"))
            .ReturnsAsync(new User { Id = 4, Role = UserRole.Citizen, AccountStatus = AccountStatus.Active });
        _signInManager.Setup(m => m.PasswordSignInAsync(
            It.IsAny<User>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.LockedOut);

        var result = await _controller.Login(new LoginViewModel { Email = "citizen@example.com", Password = "Wrong1@" });

        var view = Assert.IsType<ViewResult>(result);
        var error = Assert.Single(_controller.ModelState[string.Empty]!.Errors);
        Assert.Contains("locked", error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_WhenSucceededForLawyer_RedirectsToLawyerQueue()
    {
        _userManager.Setup(m => m.FindByEmailAsync("lawyer@example.com"))
            .ReturnsAsync(new User { Id = 5, Role = UserRole.Lawyer, AccountStatus = AccountStatus.Active });
        _signInManager.Setup(m => m.PasswordSignInAsync(
            It.IsAny<User>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

        var result = await _controller.Login(new LoginViewModel { Email = "lawyer@example.com", Password = "Lawyer@123" });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Queue", redirect.ActionName);
        Assert.Equal("Lawyer", redirect.ControllerName);
    }

    [Fact]
    public async Task Login_WhenSucceededForCitizen_RedirectsToLegalAidHome()
    {
        _userManager.Setup(m => m.FindByEmailAsync("citizen@example.com"))
            .ReturnsAsync(new User { Id = 6, Role = UserRole.Citizen, AccountStatus = AccountStatus.Active });
        _signInManager.Setup(m => m.PasswordSignInAsync(
            It.IsAny<User>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

        var result = await _controller.Login(new LoginViewModel { Email = "citizen@example.com", Password = "Citizen@123" });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Home", redirect.ControllerName);
    }

    [Fact]
    public async Task Register_LawyerWithoutBarNumber_AddsFieldErrorAndDoesNotCreateUser()
    {
        var model = new RegisterViewModel
        {
            FullName = "Test Lawyer",
            Email = "lawyer2@muktoain.bd",
            Password = "Lawyer@123",
            ConfirmPassword = "Lawyer@123",
            Role = "Lawyer",
            BarRegistrationNumber = "   "
        };

        var result = await _controller.Register(model);

        Assert.IsType<ViewResult>(result);
        Assert.Contains("BarRegistrationNumber", _controller.ModelState.Keys);
        var error = Assert.Single(_controller.ModelState["BarRegistrationNumber"]!.Errors);
        Assert.Contains("Bar Registration Number", error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        _userManager.Verify(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Register_WhenIdentityPasswordPolicyFails_MapsErrorToPasswordField()
    {
        var model = new RegisterViewModel
        {
            FullName = "Test Citizen",
            Email = "citizen2@muktoain.bd",
            Password = "short",
            ConfirmPassword = "short",
            Role = "Citizen"
        };

        _userManager.Setup(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(
                new IdentityError { Code = "PasswordTooShort", Description = "Passwords must be at least 8 characters." }));

        var result = await _controller.Register(model);

        Assert.IsType<ViewResult>(result);
        Assert.Contains("Password", _controller.ModelState.Keys);
        var error = Assert.Single(_controller.ModelState["Password"]!.Errors);
        Assert.Contains("8", error.ErrorMessage);
    }

    [Fact]
    public async Task Register_LawyerWithBarNumber_PersistsPendingLawyerProfileAndRedirectsToLogin()
    {
        var model = new RegisterViewModel
        {
            FullName = "Test Lawyer",
            Email = "lawyer3@muktoain.bd",
            Password = "Lawyer@123",
            ConfirmPassword = "Lawyer@123",
            Role = "Lawyer",
            BarRegistrationNumber = "  BAR-2026-9999  ",
            Specialization = "Family Law"
        };

        _userManager.Setup(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()))
            .Callback<User, string>((u, _) => u.Id = 42)
            .ReturnsAsync(IdentityResult.Success);

        LawyerProfile? addedProfile = null;
        _lawyerProfileRepo.Setup(r => r.AddAsync(It.IsAny<LawyerProfile>()))
            .Callback<LawyerProfile>(p => addedProfile = p)
            .Returns(Task.CompletedTask);

        var result = await _controller.Register(model);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Login", redirect.ActionName);

        Assert.NotNull(addedProfile);
        Assert.Equal(42, addedProfile!.UserId);
        Assert.Equal("BAR-2026-9999", addedProfile.BarRegistrationNumber);
        Assert.Equal(VerificationStatus.Pending, addedProfile.VerificationStatus);
        Assert.Equal("Family Law", addedProfile.Specialization);
        _lawyerProfileRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task Register_Citizen_DoesNotCreateLawyerProfile()
    {
        var model = new RegisterViewModel
        {
            FullName = "Test Citizen",
            Email = "citizen3@muktoain.bd",
            Password = "Citizen@123",
            ConfirmPassword = "Citizen@123",
            Role = "Citizen"
        };

        _userManager.Setup(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()))
            .Callback<User, string>((u, _) => u.Id = 43)
            .ReturnsAsync(IdentityResult.Success);

        var result = await _controller.Register(model);

        Assert.IsType<RedirectToActionResult>(result);
        _lawyerProfileRepo.Verify(r => r.AddAsync(It.IsAny<LawyerProfile>()), Times.Never);
        _lawyerProfileRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task Profile_Get_WhenAuthenticatedCitizen_ReturnsViewWithCitizenData()
    {
        var user = new User
        {
            Id = 12,
            Email = "citizen@muktoain.bd",
            FullName = "Sanjida Erin",
            Role = UserRole.Citizen,
            AccountStatus = AccountStatus.Active
        };
        _userManager.Setup(m => m.GetUserAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>())).ReturnsAsync(user);

        var result = await _controller.Profile();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProfileViewModel>(view.Model);
        Assert.Equal("Sanjida Erin", model.FullName);
        Assert.Equal("citizen@muktoain.bd", model.Email);
        Assert.Equal("Citizen", model.Role);
    }

    [Fact]
    public async Task Profile_Post_WhenValid_UpdatesUserAndRedirects()
    {
        var user = new User
        {
            Id = 15,
            Email = "lawyer@muktoain.bd",
            FullName = "Adv. Hasan",
            Role = UserRole.Lawyer
        };
        _userManager.Setup(m => m.GetUserAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>())).ReturnsAsync(user);
        _userManager.Setup(m => m.UpdateAsync(It.IsAny<User>())).ReturnsAsync(IdentityResult.Success);
        _lawyerProfileRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<LawyerProfile, bool>>>()))
            .ReturnsAsync(new List<LawyerProfile> { new LawyerProfile { UserId = 15, BarRegistrationNumber = "DHA-999" } });

        var model = new ProfileViewModel
        {
            FullName = "Adv. Shahadat Hasan",
            PhoneNumber = "01700000000",
            Specialization = "Labour & Cyber Law"
        };

        var result = await _controller.Profile(model);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AccountController.Profile), redirect.ActionName);
        Assert.Equal("Adv. Shahadat Hasan", user.FullName);
    }

    [Fact]
    public async Task ChangePassword_Post_WhenValid_ChangesPasswordAndSetsSuccess()
    {
        var user = new User { Id = 20, Email = "user@muktoain.bd" };
        _userManager.Setup(m => m.GetUserAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>())).ReturnsAsync(user);
        _userManager.Setup(m => m.ChangePasswordAsync(user, "OldPass@123", "NewPass@123")).ReturnsAsync(IdentityResult.Success);

        var model = new ChangePasswordViewModel
        {
            CurrentPassword = "OldPass@123",
            NewPassword = "NewPass@123",
            ConfirmNewPassword = "NewPass@123"
        };

        var result = await _controller.ChangePassword(model);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AccountController.Profile), redirect.ActionName);
        Assert.True(_controller.TempData.ContainsKey("Success"));
    }

    private static Mock<UserManager<User>> NewUserManager()
    {
        var store = new Mock<IUserStore<User>>();
        return new Mock<UserManager<User>>(
            store.Object,
            Options.Create(new IdentityOptions()),
            Mock.Of<IPasswordHasher<User>>(),
            Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(),
            Mock.Of<ILookupNormalizer>(),
            new IdentityErrorDescriber(),
            null!,
            Mock.Of<ILogger<UserManager<User>>>());
    }

    private static Mock<SignInManager<User>> NewSignInManager(UserManager<User> userManager)
    {
        return new Mock<SignInManager<User>>(
            userManager,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<User>>(),
            Options.Create(new IdentityOptions()),
            Mock.Of<ILogger<SignInManager<User>>>(),
            Mock.Of<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>(),
            Mock.Of<IUserConfirmation<User>>());
    }
}