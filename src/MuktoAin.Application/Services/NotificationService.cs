using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;

namespace MuktoAin.Application.Services;

public class NotificationService
{
    private readonly IRepository<Notification> _repo;
    private readonly UserManager<User> _userManager;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        IRepository<Notification> repo, UserManager<User> userManager, ILogger<NotificationService> logger)
    {
        _repo = repo;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task NotifyAsync(
        int userId, NotificationType type,
        int? caseId = null, int? documentId = null, int? lawyerProfileId = null)
    {
        try
        {
            await _repo.AddAsync(new Notification
            {
                UserId = userId,
                Type = type,
                RelatedCaseId = caseId,
                RelatedDocumentId = documentId,
                RelatedLawyerProfileId = lawyerProfileId,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
            await _repo.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Never let a notification failure break the caller's real operation
            // (case submission, review, verification, payment).
            _logger.LogWarning(ex, "Failed to write notification for user {UserId}, type {Type}", userId, type);
        }
    }

    public async Task NotifyAllAdminsAsync(
        NotificationType type, int? caseId = null, int? documentId = null, int? lawyerProfileId = null)
    {
        try
        {
            var admins = _userManager.Users.Where(u => u.Role == UserRole.Admin).ToList();
            foreach (var admin in admins)
            {
                await NotifyAsync(admin.Id, type, caseId, documentId, lawyerProfileId);
            }
        }
        catch (Exception ex)
        {
            // Never let a notification failure break the caller's real operation
            // (case submission, review, verification, payment).
            _logger.LogWarning(ex, "Failed to notify admins for type {Type}", type);
        }
    }

    public async Task<IReadOnlyList<NotificationDto>> GetRecentAsync(int userId, int take = 10)
    {
        var all = await _repo.GetAllAsync();
        return all.Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .Select(ToDto)
            .ToList();
    }

    public async Task<int> GetUnreadCountAsync(int userId)
    {
        var all = await _repo.GetAllAsync();
        return all.Count(n => n.UserId == userId && !n.IsRead);
    }

    // Bell badge count: notifications the user hasn't seen in the dropdown yet.
    public async Task<int> GetUnseenCountAsync(int userId)
    {
        var all = await _repo.GetAllAsync();
        return all.Count(n => n.UserId == userId && !n.IsSeen);
    }

    public async Task MarkAllSeenAsync(int userId)
    {
        var mine = (await _repo.GetAllAsync()).Where(n => n.UserId == userId && !n.IsSeen);
        foreach (var n in mine) n.IsSeen = true;
        await _repo.SaveChangesAsync();
    }

    public async Task<PagedResult<NotificationDto>> GetPagedAsync(int userId, int page, int pageSize)
    {
        var mine = (await _repo.GetAllAsync())
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .ToList();

        var items = mine.Skip((page - 1) * pageSize).Take(pageSize).Select(ToDto).ToList();
        return new PagedResult<NotificationDto>(items, mine.Count, page, pageSize);
    }

    public async Task<bool> MarkReadAsync(int notificationId, int userId)
    {
        var all = await _repo.GetAllAsync();
        var n = all.FirstOrDefault(x => x.NotificationId == notificationId);
        if (n == null || n.UserId != userId) return false;

        n.IsRead = true;
        n.IsSeen = true;
        await _repo.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(int notificationId, int userId)
    {
        var all = await _repo.GetAllAsync();
        var n = all.FirstOrDefault(x => x.NotificationId == notificationId);
        if (n == null || n.UserId != userId) return false;

        await _repo.DeleteAsync(n);
        await _repo.SaveChangesAsync();
        return true;
    }

    public async Task MarkAllReadAsync(int userId)
    {
        var mine = (await _repo.GetAllAsync()).Where(n => n.UserId == userId && !n.IsRead);
        foreach (var n in mine) { n.IsRead = true; n.IsSeen = true; }
        await _repo.SaveChangesAsync();
    }

    private static NotificationDto ToDto(Notification n) => new(
        n.NotificationId, n.Type, n.RelatedCaseId, n.RelatedDocumentId, n.RelatedLawyerProfileId,
        n.IsRead, n.CreatedAt);
}
