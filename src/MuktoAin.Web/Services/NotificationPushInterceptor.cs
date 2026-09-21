using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MuktoAin.Domain.Entities;
using MuktoAin.Web.Hubs;

namespace MuktoAin.Web.Services;

// Pushes a real-time "notificationsChanged" signal to every user whose
// NOTIFICATION rows were added, updated or deleted by a successful save.
// Hooking SaveChanges (instead of each service) covers every writer --
// NotificationService plus the services that add rows via the repository
// directly -- and keeps SignalR out of the Application layer.
// Scoped: one instance per DbContext, so the pending set is never shared.
public class NotificationPushInterceptor : SaveChangesInterceptor
{
    private readonly IHubContext<NotificationHub> _hub;
    private readonly ILogger<NotificationPushInterceptor> _logger;
    private readonly HashSet<int> _pendingUserIds = new();

    public NotificationPushInterceptor(
        IHubContext<NotificationHub> hub, ILogger<NotificationPushInterceptor> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        PushAsync().GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        await PushAsync();
        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => _pendingUserIds.Clear();

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _pendingUserIds.Clear();
        return Task.CompletedTask;
    }

    private void Collect(DbContext? context)
    {
        if (context == null) return;
        foreach (var entry in context.ChangeTracker.Entries<Notification>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                _pendingUserIds.Add(entry.Entity.UserId);
        }
    }

    private async Task PushAsync()
    {
        if (_pendingUserIds.Count == 0) return;
        var userIds = _pendingUserIds.Select(id => id.ToString()).ToList();
        _pendingUserIds.Clear();
        try
        {
            await _hub.Clients.Users(userIds).SendAsync(NotificationHub.ChangedEvent);
        }
        catch (Exception ex)
        {
            // The row is already saved; clients still catch up via polling.
            _logger.LogWarning(ex, "Failed to push notification change to {Count} user(s)", userIds.Count);
        }
    }
}
