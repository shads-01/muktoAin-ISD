using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;

namespace MuktoAin.Application.Services;

public class LawyerVerificationService
{
    private readonly IRepository<LawyerProfile> _profileRepo;
    private readonly IAdminAuditService _audit;

    public LawyerVerificationService(IRepository<LawyerProfile> profileRepo, IAdminAuditService audit)
    {
        _profileRepo = profileRepo;
        _audit = audit;
    }

    public async Task<int> ApplyAsync(int userId, LawyerApplicationDto dto)
    {
        var existing = await _profileRepo.GetAllAsync();
        if (existing.Any(p => p.UserId == userId))
            throw new InvalidOperationException("Verification already submitted");

        var profile = new LawyerProfile
        {
            UserId = userId,
            BarRegistrationNumber = dto.BarRegistrationNumber,
            Specialization = dto.Specialization,
            VerificationStatus = VerificationStatus.Pending
        };

        await _profileRepo.AddAsync(profile);
        await _profileRepo.SaveChangesAsync();
        return profile.LawyerProfileId;
    }

    public async Task VerifyAsync(int lawyerProfileId, int adminUserId, bool approve, string? reason = null)
    {
        var profile = await _profileRepo.GetByIdAsync(lawyerProfileId);
        if (profile == null) throw new ArgumentException("Profile not found");

        profile.VerificationStatus = approve
            ? VerificationStatus.Approved
            : VerificationStatus.Rejected;
        profile.VerifiedByAdminId = adminUserId;
        profile.VerifiedAt = DateTime.UtcNow;
        profile.RejectionReason = approve ? null : (reason ?? string.Empty);

        await _profileRepo.SaveChangesAsync();

        // AUD-7: bar-verification decision is a human-in-the-loop safeguard
        // action — record which admin made it, on whom, and why (rejections).
        await _audit.LogAdminActionAsync(
            adminUserId,
            approve ? "ApproveLawyerVerification" : "RejectLawyerVerification",
            targetUserId: profile.UserId,
            targetEntityId: lawyerProfileId,
            details: approve ? null : reason);
    }

    public async Task<IEnumerable<LawyerProfile>> GetPendingApplicationsAsync()
    {
        var all = await _profileRepo.GetAllAsync();
        return all.Where(p => p.VerificationStatus == VerificationStatus.Pending);
    }
}
