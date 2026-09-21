using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Infrastructure.Data;
using MuktoAin.IntegrationTests.Helpers;

namespace MuktoAin.IntegrationTests.Api;

public class CaseApiTests : IClassFixture<MuktoAinWebApplicationFactory>
{
    private readonly MuktoAinWebApplicationFactory _factory;

    public CaseApiTests(MuktoAinWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task SubmitOptions_ReturnsDistrictsJson()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/Case/SubmitOptions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"districts\"", json);
        Assert.Contains("Dhaka", json);
    }

    [Fact]
    public async Task Submit_Post_MissingTitle_RerendersForm_WithModelStateError()
    {
        var client = _factory.CreateClient();
        var response = await AntiForgeryHelper.PostFormAsync(
            client, "/Case/Submit", "/Case/Submit",
            new Dictionary<string, string>
            {
                ["CategoryId"] = "1",
                ["DistrictId"] = "1",
                ["Title"] = "",
                ["Description"] = "Wages unpaid for three months despite repeated requests.",
                ["Language"] = "bn",
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // form re-rendered
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("শিরোনাম", html); // the failed form comes back to the user
    }

    [Fact]
    public async Task Submit_Post_ValidAnonymousSubmission_RedirectsToResultWithTrackingCode()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TestData.SeedBaselineAsync(db);

        var client = _factory.CreateClientWithoutRedirects();
        var response = await AntiForgeryHelper.PostFormAsync(
            client, "/Case/Submit", "/Case/Submit",
            new Dictionary<string, string>
            {
                ["CategoryId"] = "1",
                ["DistrictId"] = "1",
                ["Title"] = "Unpaid wages complaint",
                ["Description"] = "Employer has not paid wages for three months despite repeated requests.",
                ["Language"] = "en",
                ["IsAnonymous"] = "true",
            });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.ToString() ?? "";
        Assert.StartsWith("/Case/Result/", location);
        Assert.Contains("code=", location); // anonymous tracking code is round-tripped
    }

    [Fact]
    public async Task Result_NonOwnerCitizen_Returns_404_IdorGuard()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TestData.SeedBaselineAsync(db);
        var owner = await TestData.NewUserAsync(db, "Owner", UserRole.Citizen);
        var intruder = await TestData.NewUserAsync(db, "Intruder", UserRole.Citizen);
        var c = await TestData.NewCaseAsync(db, owner.Id);

        var client = _factory.CreateAuthenticatedClient(intruder.Id, UserRole.Citizen);
        var response = await client.GetAsync($"/Case/Result/{c.CaseId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Result_AnonymousCase_WithValidTrackingCode_Returns_200()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TestData.SeedBaselineAsync(db);
        var code = Guid.NewGuid().ToString("N");
        var c = await TestData.NewCaseAsync(db, userId: null, anonymous: true, trackingCode: code);

        var client = _factory.CreateClient(); // no auth — guest + code path
        var response = await client.GetAsync($"/Case/Result/{c.CaseId}?code={code}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Track_WithValidCode_RedirectsToResult()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TestData.SeedBaselineAsync(db);
        var code = Guid.NewGuid().ToString("N");
        var c = await TestData.NewCaseAsync(db, userId: null, anonymous: true, trackingCode: code);

        var client = _factory.CreateClientWithoutRedirects();
        var response = await client.GetAsync($"/Case/Track?code={code}");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"/Case/Result/{c.CaseId}", response.Headers.Location?.ToString());
    }
}
