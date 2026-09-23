using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Infrastructure.Data;
using MuktoAin.IntegrationTests.Helpers;

namespace MuktoAin.IntegrationTests.Api;

// System-level coverage for the notification subsystem, driven over real HTTP
// against the real NotificationController, NotificationService and
// LawyerQueueNotifier. /Notification/Unread is the endpoint the bell polls
// after a `notificationsChanged` signal arrives on the SignalR hub, so
// asserting on it is what "the badge updates without a page refresh" means
// from the server's side; the hub itself carries no content to assert on.
public class NotificationApiTests : IClassFixture<MuktoAinWebApplicationFactory>
{
    private readonly MuktoAinWebApplicationFactory _factory;

    public NotificationApiTests(MuktoAinWebApplicationFactory factory) => _factory = factory;

    // NOTIF-01
    [Fact]
    public async Task BellEndpoint_ReflectsANewNotification_WithoutReloadingAnyPage()
    {
        var (citizen, caseId) = await SeedCitizenWithCaseAsync();
        var client = _factory.CreateAuthenticatedClient(citizen.Id, UserRole.Citizen);

        var before = await BellAsync(client);
        Assert.Equal(0, before.GetProperty("count").GetInt32());

        await NotifyAsync(citizen.Id, NotificationType.CaseSubmitted, caseId);

        var after = await BellAsync(client);
        Assert.Equal(1, after.GetProperty("count").GetInt32());
        var item = after.GetProperty("items").EnumerateArray().Single();
        Assert.False(item.GetProperty("isRead").GetBoolean());
        Assert.Equal($"/Case/Result?id={caseId}", item.GetProperty("url").GetString());
        Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("textBn").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("textEn").GetString()));
    }

    // NOTIF-02
    [Fact]
    public async Task AdminApprovesAVerification_NotifiesThatLawyer_LinkedToLawyerStatus()
    {
        var (admin, lawyerUserId, profileId) = await SeedPendingLawyerAsync();
        var adminClient = _factory.CreateAuthenticatedClient(admin.Id, UserRole.Admin, allowAutoRedirect: false);

        var response = await adminClient.PostAsync("/Admin/VerifyLawyer",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["lawyerProfileId"] = profileId.ToString(),
                ["approve"] = "true",
            }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var lawyerClient = _factory.CreateAuthenticatedClient(lawyerUserId, UserRole.Lawyer);
        var bell = await BellAsync(lawyerClient);
        Assert.Equal(1, bell.GetProperty("count").GetInt32());
        Assert.Equal("/Lawyer/Status", bell.GetProperty("items").EnumerateArray().Single()
            .GetProperty("url").GetString());

        await using var scope = NewScope(out var db);
        Assert.Equal(1, await db.Notifications.CountAsync(n =>
            n.UserId == lawyerUserId && n.Type == NotificationType.LawyerVerified));
    }

    // NOTIF-03
    [Fact]
    public async Task DocumentEnteringTheQueue_NotifiesVerifiedLawyersOnly()
    {
        var (citizen, caseId, verifiedLawyerUserId, pendingLawyerUserId) = await SeedQueueScenarioAsync();
        var client = _factory.CreateAuthenticatedClient(citizen.Id, UserRole.Citizen, allowAutoRedirect: false);

        var response = await client.PostAsync($"/Case/SendToLawyer/{caseId}", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["id"] = caseId.ToString() }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        await using var scope = NewScope(out var db);
        Assert.Equal(1, await db.Notifications.CountAsync(n =>
            n.UserId == verifiedLawyerUserId && n.Type == NotificationType.NewCaseInQueue && n.RelatedCaseId == caseId));
        Assert.Equal(0, await db.Notifications.CountAsync(n => n.UserId == pendingLawyerUserId));
        Assert.Equal(DocumentStatus.UnderReview,
            (await db.GeneratedDocuments.AsNoTracking().SingleAsync(d => d.CaseId == caseId)).Status);
    }

    // NOTIF-04
    [Fact]
    public async Task MarkRead_OnSomeoneElsesNotification_IsForbidden_AndLeavesItUnread()
    {
        var (userA, caseId) = await SeedCitizenWithCaseAsync();
        var userB = await NewUserAsync(UserRole.Citizen);
        var notificationId = await NotifyAsync(userA.Id, NotificationType.CaseSubmitted, caseId);

        // Forbid() routes through the access-denied path, so follow redirects
        // to land on the status a browser would actually see.
        var clientB = _factory.CreateAuthenticatedClient(userB.Id, UserRole.Citizen);
        var response = await clientB.PostAsync("/Notification/MarkRead", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["id"] = notificationId.ToString() }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var scope = NewScope(out var db);
        Assert.False((await db.Notifications.AsNoTracking()
            .SingleAsync(n => n.NotificationId == notificationId)).IsRead);
    }

    // NOTIF-05
    [Fact]
    public async Task BellEndpoint_NeverReturnsAnotherUsersNotifications()
    {
        var (userA, caseId) = await SeedCitizenWithCaseAsync();
        var userB = await NewUserAsync(UserRole.Citizen);
        await NotifyAsync(userA.Id, NotificationType.CaseSubmitted, caseId);

        var bellB = await BellAsync(_factory.CreateAuthenticatedClient(userB.Id, UserRole.Citizen));

        Assert.Equal(0, bellB.GetProperty("count").GetInt32());
        Assert.Empty(bellB.GetProperty("items").EnumerateArray());
    }

    // NOTIF-06
    [Fact]
    public async Task BellEndpoint_RejectsAnAnonymousCaller()
    {
        var response = await _factory.CreateClientWithoutRedirects().GetAsync("/Notification/Unread");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- helpers ------------------------------------------------------------

    private static async Task<JsonElement> BellAsync(HttpClient client)
    {
        var response = await client.GetAsync("/Notification/Unread");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private async Task<int> NotifyAsync(int userId, NotificationType type, int? caseId = null)
    {
        await using var scope = NewScope(out var db);
        var n = new Notification
        {
            UserId = userId,
            Type = type,
            RelatedCaseId = caseId,
            CreatedAt = DateTime.UtcNow,
        };
        db.Notifications.Add(n);
        await db.SaveChangesAsync();
        return n.NotificationId;
    }

    private async Task<User> NewUserAsync(UserRole role)
    {
        await using var scope = NewScope(out var db);
        await TestData.SeedBaselineAsync(db);
        return await TestData.NewUserAsync(db, role.ToString(), role);
    }

    private async Task<(User Citizen, int CaseId)> SeedCitizenWithCaseAsync()
    {
        await using var scope = NewScope(out var db);
        await TestData.SeedBaselineAsync(db);
        var citizen = await TestData.NewUserAsync(db, "Citizen", UserRole.Citizen);
        var c = await TestData.NewCaseAsync(db, citizen.Id);
        return (citizen, c.CaseId);
    }

    private async Task<(User Admin, int LawyerUserId, int ProfileId)> SeedPendingLawyerAsync()
    {
        await using var scope = NewScope(out var db);
        await TestData.SeedBaselineAsync(db);
        var admin = await TestData.NewUserAsync(db, "Admin", UserRole.Admin);
        var lawyerUser = await TestData.NewUserAsync(db, "PendingLawyer", UserRole.Lawyer);
        var profile = new LawyerProfile
        {
            UserId = lawyerUser.Id,
            BarRegistrationNumber = $"BAR-{Guid.NewGuid():N}"[..16],
            VerificationStatus = VerificationStatus.Pending,
        };
        db.LawyerProfiles.Add(profile);
        await db.SaveChangesAsync();
        return (admin, lawyerUser.Id, profile.LawyerProfileId);
    }

    // One approved lawyer and one still-pending lawyer, so the queue fan-out
    // can be shown to skip the unverified one.
    private async Task<(User Citizen, int CaseId, int VerifiedLawyerUserId, int PendingLawyerUserId)> SeedQueueScenarioAsync()
    {
        await using var scope = NewScope(out var db);
        await TestData.SeedBaselineAsync(db);
        var citizen = await TestData.NewUserAsync(db, "QueueCitizen", UserRole.Citizen);
        var verified = await TestData.NewUserAsync(db, "VerifiedLawyer", UserRole.Lawyer);
        var pending = await TestData.NewUserAsync(db, "UnverifiedLawyer", UserRole.Lawyer);
        db.LawyerProfiles.AddRange(
            new LawyerProfile
            {
                UserId = verified.Id,
                BarRegistrationNumber = $"BAR-{Guid.NewGuid():N}"[..16],
                VerificationStatus = VerificationStatus.Approved,
            },
            new LawyerProfile
            {
                UserId = pending.Id,
                BarRegistrationNumber = $"BAR-{Guid.NewGuid():N}"[..16],
                VerificationStatus = VerificationStatus.Pending,
            });
        var c = await TestData.NewCaseAsync(db, citizen.Id);
        db.GeneratedDocuments.Add(TestData.NewDocument(c.CaseId, DocumentStatus.Draft));
        await db.SaveChangesAsync();
        return (citizen, c.CaseId, verified.Id, pending.Id);
    }

    private AsyncServiceScope NewScope(out AppDbContext db)
    {
        var scope = _factory.Services.CreateAsyncScope();
        db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return scope;
    }
}
