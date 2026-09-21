using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Infrastructure.Data;
using MuktoAin.IntegrationTests.Helpers;

namespace MuktoAin.IntegrationTests.Api;

public class DocumentApiTests : IClassFixture<MuktoAinWebApplicationFactory>
{
    private readonly MuktoAinWebApplicationFactory _factory;

    public DocumentApiTests(MuktoAinWebApplicationFactory factory) => _factory = factory;

    private async Task<(User Citizen, Case Case, GeneratedDocument Doc)> SeedOwnedDocumentAsync(
        DocumentStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TestData.SeedBaselineAsync(db);
        var citizen = await TestData.NewUserAsync(db, "Owner Citizen", UserRole.Citizen);
        var other = await TestData.NewUserAsync(db, "Other Citizen", UserRole.Citizen);
        _ = other; // keeps the second user materialized for clarity
        var c = await TestData.NewCaseAsync(db, citizen.Id);
        var doc = TestData.NewDocument(c.CaseId, status);
        db.GeneratedDocuments.Add(doc);
        await db.SaveChangesAsync();
        return (citizen, c, doc);
    }

    [Fact]
    public async Task Preview_UnknownDocumentId_Returns_404()
    {
        var client = _factory.CreateClient(); // unauthenticated is fine — 404 fires before authz
        var response = await client.GetAsync("/Document/Preview/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Preview_ZeroOrNegativeId_Returns_404()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/Document/Preview/0");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Preview_NonOwnerCitizen_Returns_403_IdorGuard()
    {
        var (_, _, doc) = await SeedOwnedDocumentAsync(DocumentStatus.Draft);

        // Fresh citizen who does NOT own the case
        using var scope = _factory.Services.CreateScope();
        var db2 = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var intruder = await TestData.NewUserAsync(db2, "Intruder", UserRole.Citizen);

        var client = _factory.CreateAuthenticatedClient(intruder.Id, UserRole.Citizen);
        var response = await client.GetAsync($"/Document/Preview/{doc.DocumentId}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Preview_OwnerCitizen_Returns_200_WithDraftContent()
    {
        var (citizen, _, doc) = await SeedOwnedDocumentAsync(DocumentStatus.Draft);
        var client = _factory.CreateAuthenticatedClient(citizen.Id, UserRole.Citizen);
        var response = await client.GetAsync($"/Document/Preview/{doc.DocumentId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Draft body", html);
    }

    [Fact]
    public async Task Preview_Admin_Returns_200_ForAnyCase()
    {
        var (_, _, doc) = await SeedOwnedDocumentAsync(DocumentStatus.Draft);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminUser = await TestData.NewUserAsync(db, "Admin", UserRole.Admin);

        var client = _factory.CreateAuthenticatedClient(adminUser.Id, UserRole.Admin);
        var response = await client.GetAsync($"/Document/Preview/{doc.DocumentId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Download_BeforeLawyerApproval_RedirectsToPreview_ReviewGate()
    {
        var (citizen, _, doc) = await SeedOwnedDocumentAsync(DocumentStatus.UnderReview);
        var client = _factory.CreateAuthenticatedClient(citizen.Id, UserRole.Citizen, allowAutoRedirect: false);
        var response = await client.GetAsync($"/Document/Download/{doc.DocumentId}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"/Document/Preview/{doc.DocumentId}",
            response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Download_AfterLawyerApproval_ReturnsPdfBytes()
    {
        var (citizen, _, doc) = await SeedOwnedDocumentAsync(DocumentStatus.Approved);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        doc.ContentFinal = doc.ContentDraft; // UpdateStatusAsync-equivalent state
        await db.SaveChangesAsync();

        var client = _factory.CreateAuthenticatedClient(citizen.Id, UserRole.Citizen);
        var response = await client.GetAsync($"/Document/Download/{doc.DocumentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(bytes);
        // QuestPDF files start with the PDF magic marker
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }
}
