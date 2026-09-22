using Microsoft.EntityFrameworkCore;
using Moq;
using MuktoAin.Application.DTOs;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Common;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces;
using MuktoAin.Infrastructure.Data;
using MuktoAin.Infrastructure.Repositories;
using MuktoAin.IntegrationTests.Support;

namespace MuktoAin.IntegrationTests.Repositories;

// AUD-4: real SQL Server rowversion behaviour, which EF InMemory can't model.
// Two DbContexts stand in for two lawyers' requests racing for one document.
[Collection("MuktoAinSqlDb")]
public class LawyerClaimRaceSqlTests
{
    private readonly SqlDatabaseFixture _fx;

    public LawyerClaimRaceSqlTests(SqlDatabaseFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Stale_Document_Save_Throws_ConcurrencyConflictException()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var (docId, lawyerA, lawyerB) = await SeedUnclaimedDocumentAsync();

        await using var ctxA = _fx.CreateContext();
        await using var ctxB = _fx.CreateContext();
        var repoA = new Repository<GeneratedDocument>(ctxA);
        var repoB = new Repository<GeneratedDocument>(ctxB);
        var a = (await repoA.GetByIdAsync(docId))!;
        var b = (await repoB.GetByIdAsync(docId))!;

        a.AssignedLawyerProfileId = lawyerA;
        await repoA.SaveChangesAsync();

        b.AssignedLawyerProfileId = lawyerB;
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => repoB.SaveChangesAsync());

        await using var verify = _fx.CreateContext();
        var stored = await verify.GeneratedDocuments.SingleAsync(d => d.DocumentId == docId);
        Assert.Equal(lawyerA, stored.AssignedLawyerProfileId);
    }

    [SkippableFact]
    public async Task ClaimAsync_Second_Lawyer_With_Stale_Read_Gets_False()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var (docId, lawyerA, lawyerB) = await SeedUnclaimedDocumentAsync();

        await using var ctxA = _fx.CreateContext();
        await using var ctxB = _fx.CreateContext();

        // Lawyer B's request reads the document while it's still unclaimed...
        await ctxB.GeneratedDocuments.SingleAsync(d => d.DocumentId == docId);

        // ...then lawyer A claims it first.
        Assert.True(await CreateService(ctxA).ClaimAsync(docId, lawyerA));

        // B's stale copy passes the in-memory "unclaimed" check; the
        // rowversion check in the UPDATE is what stops it.
        Assert.False(await CreateService(ctxB).ClaimAsync(docId, lawyerB));

        await using var verify = _fx.CreateContext();
        var stored = await verify.GeneratedDocuments.SingleAsync(d => d.DocumentId == docId);
        Assert.Equal(lawyerA, stored.AssignedLawyerProfileId);
    }

    [SkippableFact]
    public async Task SubmitReviewAsync_Case_Conflict_Leaves_Nothing_Half_Saved()
    {
        Skip.IfNot(_fx.DatabaseAvailable);
        var (docId, lawyerA, _) = await SeedUnclaimedDocumentAsync();

        int caseId;
        await using (var claim = _fx.CreateContext())
        {
            var d = await claim.GeneratedDocuments.SingleAsync(x => x.DocumentId == docId);
            d.AssignedLawyerProfileId = lawyerA;
            await claim.SaveChangesAsync();
            caseId = d.CaseId;
        }

        await using var reviewCtx = _fx.CreateContext();
        // The reviewing request has already read the case...
        await reviewCtx.Cases.SingleAsync(c => c.CaseId == caseId);

        // ...when someone else updates that case.
        await using (var other = _fx.CreateContext())
        {
            var c = await other.Cases.SingleAsync(x => x.CaseId == caseId);
            c.UpdatedAt = DateTime.UtcNow;
            await other.SaveChangesAsync();
        }

        var ok = await CreateService(reviewCtx).SubmitReviewAsync(new SubmitReviewDto(
            docId, lawyerA, ReviewDecision.Approved, "looks fine", null));

        Assert.False(ok);
        await using var verify = _fx.CreateContext();
        Assert.False(await verify.LawyerReviews.AnyAsync(r => r.DocumentId == docId));
        var stored = await verify.GeneratedDocuments.SingleAsync(x => x.DocumentId == docId);
        Assert.Equal(DocumentStatus.UnderReview, stored.Status);
        Assert.Equal(CaseStatus.UnderReview,
            (await verify.Cases.SingleAsync(x => x.CaseId == caseId)).Status);
    }

    private static LawyerReviewService CreateService(AppDbContext ctx)
    {
        var encryption = new Mock<IEncryptionService>().Object;
        var caseRepo = new CaseRepository(ctx);
        var categoryRepo = new Repository<CaseCategory>(ctx);
        var districtRepo = new Repository<District>(ctx);
        var notificationRepo = new Repository<Notification>(ctx);
        var caseService = new CaseService(caseRepo, categoryRepo, districtRepo, encryption, notificationRepo);
        return new LawyerReviewService(
            new Repository<GeneratedDocument>(ctx), new Repository<LawyerReview>(ctx),
            new Repository<LawyerProfile>(ctx), caseRepo, categoryRepo, districtRepo,
            new Repository<CaseActReference>(ctx), new Repository<ActSection>(ctx),
            new Repository<Act>(ctx), encryption, caseService, notificationRepo);
    }

    private async Task<(int docId, int lawyerA, int lawyerB)> SeedUnclaimedDocumentAsync()
    {
        var districtId = await _fx.GetDistrictIdAsync();
        var categoryId = await _fx.GetCategoryIdAsync();
        var now = DateTime.UtcNow;

        await using var ctx = _fx.CreateContext();
        var profiles = new[] { "A", "B" }.Select(tag => new LawyerProfile
        {
            User = new User
            {
                FullName = $"Race Lawyer {tag}",
                Email = $"race-{tag}-{Guid.NewGuid():N}@test.local",
                Role = UserRole.Lawyer,
            },
            BarRegistrationNumber = $"RACE-{tag}-{Guid.NewGuid():N}"[..20],
            VerificationStatus = VerificationStatus.Approved,
        }).ToArray();
        ctx.LawyerProfiles.AddRange(profiles);

        var doc = new GeneratedDocument
        {
            Case = new Case
            {
                CategoryId = categoryId,
                DistrictId = (byte)districtId,
                Title = "Claim race test",
                Description = "Seeded by AUD-4",
                Language = "en",
                Status = CaseStatus.UnderReview,
                CreatedAt = now,
                UpdatedAt = now,
            },
            DocumentType = DocumentType.GeneralDiary,
            ContentDraft = "draft",
            Status = DocumentStatus.UnderReview,
            CreatedAt = now,
        };
        ctx.GeneratedDocuments.Add(doc);
        await ctx.SaveChangesAsync();

        return (doc.DocumentId, profiles[0].LawyerProfileId, profiles[1].LawyerProfileId);
    }
}
