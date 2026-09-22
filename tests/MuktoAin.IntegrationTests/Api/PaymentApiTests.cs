using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Infrastructure.Data;
using MuktoAin.IntegrationTests.Helpers;

namespace MuktoAin.IntegrationTests.Api;

public class PaymentApiTests : IClassFixture<MuktoAinWebApplicationFactory>
{
    private readonly MuktoAinWebApplicationFactory _factory;

    public PaymentApiTests(MuktoAinWebApplicationFactory factory) => _factory = factory;

    private async Task<User> NewUserAsync(UserRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TestData.SeedBaselineAsync(db);
        return await TestData.NewUserAsync(db, "Payer", role);
    }

    private static HttpContent JsonBody(object body) => JsonContent.Create(body);

    [Fact]
    public async Task Honorarium_NonPositiveAmount_Returns_400_WithErrorJson()
    {
        var user = await NewUserAsync(UserRole.Citizen);
        var client = _factory.CreateAuthenticatedClient(user.Id, UserRole.Citizen);

        var response = await client.PostAsync("/Payment/Honorarium",
            JsonBody(new { caseId = 1, amount = 0m }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", json);
    }

    [Fact]
    public async Task Honorarium_UnknownCase_Returns_404()
    {
        var user = await NewUserAsync(UserRole.Citizen);
        var client = _factory.CreateAuthenticatedClient(user.Id, UserRole.Citizen);

        var response = await client.PostAsync("/Payment/Honorarium",
            JsonBody(new { caseId = 999999, amount = 500m }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("Case not found", json);
    }

    [Fact]
    public async Task Honorarium_NonOwnerCitizen_Returns_403_IdorGuard()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TestData.SeedBaselineAsync(db);
        var owner = await TestData.NewUserAsync(db, "Owner", UserRole.Citizen);
        var intruder = await TestData.NewUserAsync(db, "Intruder", UserRole.Citizen);
        var c = await TestData.NewCaseAsync(db, owner.Id);

        var client = _factory.CreateAuthenticatedClient(intruder.Id, UserRole.Citizen);
        var response = await client.PostAsync("/Payment/Honorarium",
            JsonBody(new { caseId = c.CaseId, amount = 500m }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Honorarium_AnonymousCase_WithMatchingTrackingCode_Succeeds()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TestData.SeedBaselineAsync(db);
        var code = Guid.NewGuid().ToString("N");
        var c = await TestData.NewCaseAsync(db, userId: null, anonymous: true, trackingCode: code);

        var client = _factory.CreateClient(); // guest, no principal — code is the credential
        var response = await client.PostAsync("/Payment/Honorarium",
            JsonBody(new { caseId = c.CaseId, amount = 500m, trackingCode = code }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.StartsWith("/GatewaySim/Checkout/", doc.RootElement.GetProperty("gatewayUrl").GetString());
    }

    [Fact]
    public async Task Honorarium_AdminOverride_AllowedForAnyCase()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TestData.SeedBaselineAsync(db);
        var owner = await TestData.NewUserAsync(db, "Owner", UserRole.Citizen);
        var admin = await TestData.NewUserAsync(db, "Admin", UserRole.Admin);
        var c = await TestData.NewCaseAsync(db, owner.Id);

        var client = _factory.CreateAuthenticatedClient(admin.Id, UserRole.Admin);
        var response = await client.PostAsync("/Payment/Honorarium",
            JsonBody(new { caseId = c.CaseId, amount = 500m }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":true", json);
    }

    [Fact]
    public async Task TopUp_ValidAmount_ReturnsGatewayCheckoutUrl_OrderStaysPending()
    {
        var user = await NewUserAsync(UserRole.Citizen);
        var client = _factory.CreateAuthenticatedClient(user.Id, UserRole.Citizen);

        var response = await client.PostAsync("/Payment/TopUp", JsonBody(new { amount = 300m }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.StartsWith("/GatewaySim/Checkout/", doc.RootElement.GetProperty("gatewayUrl").GetString());

        // Nothing is Paid until the gateway confirms (see PaymentFlowE2ETests).
        var orderId = doc.RootElement.GetProperty("orderId").GetInt32();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = await db.PaymentOrders.AsNoTracking().SingleAsync(o => o.PaymentOrderId == orderId);
        Assert.Equal(PaymentStatus.Pending, order.Status);
    }

    [Fact]
    public async Task Status_NonOwner_Returns_403_IdorGuard()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TestData.SeedBaselineAsync(db);
        var owner = await TestData.NewUserAsync(db, "Owner", UserRole.Citizen);
        var intruder = await TestData.NewUserAsync(db, "Intruder", UserRole.Citizen);
        var order = new PaymentOrder
        {
            UserId = owner.Id, Purpose = PaymentPurpose.TopUp, Status = PaymentStatus.Paid,
            Amount = 100m, Commission = 0m, NetToLawyer = 100m,
            GatewayRef = "SANDBOX-TOP-TEST", CreatedAt = DateTime.UtcNow, PaidAt = DateTime.UtcNow,
        };
        db.PaymentOrders.Add(order);
        await db.SaveChangesAsync();

        var client = _factory.CreateAuthenticatedClient(intruder.Id, UserRole.Citizen);
        var response = await client.GetAsync($"/Payment/Status/{order.PaymentOrderId}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Status_Owner_ReturnsOrderJson()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var owner = await TestData.NewUserAsync(db, "Owner", UserRole.Citizen);
        var order = new PaymentOrder
        {
            UserId = owner.Id, Purpose = PaymentPurpose.TopUp, Status = PaymentStatus.Paid,
            Amount = 100m, Commission = 0m, NetToLawyer = 100m, CreatedAt = DateTime.UtcNow,
        };
        db.PaymentOrders.Add(order);
        await db.SaveChangesAsync();

        var client = _factory.CreateAuthenticatedClient(owner.Id, UserRole.Citizen);
        var response = await client.GetAsync($"/Payment/Status/{order.PaymentOrderId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"Paid\"", json);
    }
}
