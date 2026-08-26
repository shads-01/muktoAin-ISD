using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;

namespace MuktoAin.Application.Services;

public class LawyerVerificationService
{
    private readonly IRepository<LawyerProfile> _profileRepo;

    public LawyerVerificationService(IRepository<LawyerProfile> profileRepo)
    {
        _profileRepo = profileRepo;
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

    public async Task VerifyAsync(int lawyerProfileId, int adminUserId, bool approve)
    {
        var profile = await _profileRepo.GetByIdAsync(lawyerProfileId);
        if (profile == null) throw new ArgumentException("Profile not found");

        profile.VerificationStatus = approve
            ? VerificationStatus.Approved
            : VerificationStatus.Rejected;
        profile.VerifiedByAdminId = adminUserId;
        profile.VerifiedAt = DateTime.UtcNow;

        await _profileRepo.SaveChangesAsync();
    }

    public async Task<IEnumerable<LawyerProfile>> GetPendingApplicationsAsync()
    {
        var all = await _profileRepo.GetAllAsync();
        return all.Where(p => p.VerificationStatus == VerificationStatus.Pending);
    }
}
