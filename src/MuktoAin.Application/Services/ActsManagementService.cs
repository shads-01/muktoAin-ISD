using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;

namespace MuktoAin.Application.Services;

// T-3.1 / FR-17: admin CRUD over the legislative corpus plus the SHA-256
// content-hash integrity re-index (FR-17's "incremental re-embedding based on
// section ContentHash"). The service never touches Qdrant or EmbeddingBatchJob
// directly: marking a chunk stale (VectorId = NULL) enrolls it in the
// EmbeddingBatchJob's existing work query on its next loop pass.
public class ActsManagementService : IActsManagementService
{
    private readonly IActRepository _actRepo;
    private readonly IActSectionRepository _sectionRepo;
    private readonly IActSectionChunkRepository _chunkRepo;
    private readonly IVectorStore _vectorStore;
    private readonly IRepository<AnswerCache> _cacheRepo;
    private readonly ILogger<ActsManagementService> _logger;

    private const int MaxPageSize = 100;

    public ActsManagementService(
        IActRepository actRepo,
        IActSectionRepository sectionRepo,
        IActSectionChunkRepository chunkRepo,
        IVectorStore vectorStore,
        IRepository<AnswerCache> cacheRepo,
        ILogger<ActsManagementService> logger)
    {
        _actRepo = actRepo;
        _sectionRepo = sectionRepo;
        _chunkRepo = chunkRepo;
        _vectorStore = vectorStore;
        _cacheRepo = cacheRepo;
        _logger = logger;
    }

    // A7: when law text changes (stale chunks on re-index, act deletion), all
    // cached rights explanations are potentially wrong — drop the whole
    // ANSWER_CACHE so the next question regenerates fresh. CitedJson doesn't
    // carry act ids, so a per-act purge would require a schema change; a full
    // clear is correct and rare.
    private async Task InvalidateAnswerCacheAsync()
    {
        var rows = await _cacheRepo.GetAllAsync();
        foreach (var row in rows)
        {
            await _cacheRepo.DeleteAsync(row);
        }
        await _cacheRepo.SaveChangesAsync();
    }

    public async Task<ActPageDto> GetActsAsync(string? keyword, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        keyword = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();

        var (acts, total) = await _actRepo.GetPagedAsync(keyword, page, pageSize);

        return new ActPageDto(
            acts.Select(a => new ActListDto(
                a.ActId,
                a.Title,
                a.ActNumber,
                a.Year,
                a.Language,
                a.IsRepealed,
                a.Sections.Count,
                a.SourceUrl,
                a.ImportedAt)).ToList(),
            page,
            pageSize,
            total);
    }

    public async Task<ActDetailDto?> GetActAsync(int actId)
    {
        var act = await _actRepo.GetWithSectionsAsync(actId);
        if (act is null) return null;

        var chunks = (await _chunkRepo.GetByActIdAsync(actId)).ToList();
        var chunksBySection = chunks
            .GroupBy(c => c.SectionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var sections = act.Sections
            .OrderBy(s => s.OrdinalPosition)
            .Select(s =>
            {
                var sectionChunks = chunksBySection.GetValueOrDefault(s.SectionId, []);
                return new ActSectionDto(
                    s.SectionId,
                    s.OrdinalPosition,
                    s.SectionNumber,
                    s.SectionTitle,
                    s.SectionText,
                    sectionChunks.Count,
                    sectionChunks.Count(c => c.ContentHash is not null
                        && !string.Equals(c.ContentHash, ComputeSha256(c.ChunkText), StringComparison.Ordinal)));
            })
            .ToList();

        return new ActDetailDto(
            act.ActId,
            act.Title,
            act.ActNumber,
            act.Year,
            act.PublicationDate,
            act.Language,
            act.IsRepealed,
            act.TokenCount,
            act.SourceUrl,
            act.ImportedAt,
            sections);
    }

    public async Task<ActSaveResultDto> CreateActAsync(ActSaveDto dto)
    {
        var validationError = Validate(dto);
        if (validationError is not null) return ActSaveResultDto.Fail(validationError);

        var title = dto.Title.Trim();
        if (await _actRepo.ExistsByTitleYearAsync(title, dto.Year))
        {
            return ActSaveResultDto.Fail(
                $"An act titled '{title}' already exists for {dto.Year} (the (Title, Year) natural key is shared with the import pipeline).");
        }

        var act = new Act
        {
            Title = title,
            ActNumber = dto.ActNumber.Trim(),
            Year = dto.Year,
            PublicationDate = dto.PublicationDate.Trim(),
            Language = string.IsNullOrWhiteSpace(dto.Language) ? "unknown" : dto.Language.Trim(),
            IsRepealed = dto.IsRepealed,
            // Metadata-only row: sections arrive via the T-1.8 import pipeline, so
            // TokenCount starts at 0 like a freshly-registered act shell.
            TokenCount = 0,
            SourceUrl = dto.SourceUrl.Trim(),
            ImportedAt = DateTime.UtcNow
        };

        await _actRepo.AddAsync(act);
        await _actRepo.SaveChangesAsync();
        return ActSaveResultDto.Ok(act.ActId);
    }

    public async Task<ActSaveResultDto> UpdateActAsync(int actId, ActSaveDto dto)
    {
        var validationError = Validate(dto);
        if (validationError is not null) return ActSaveResultDto.Fail(validationError);

        var act = await _actRepo.GetByIdAsync(actId);
        if (act is null)
        {
            return ActSaveResultDto.Fail($"Act {actId} was not found.");
        }

        var title = dto.Title.Trim();
        if (await _actRepo.ExistsByTitleYearAsync(title, dto.Year, excludeActId: actId))
        {
            return ActSaveResultDto.Fail($"Another act titled '{title}' already exists for {dto.Year}.");
        }

        // Metadata only. Sections/footnotes/chunks are untouched: untouched chunk
        // text means every stored ContentHash stays valid, so no re-index needed.
        act.Title = title;
        act.ActNumber = dto.ActNumber.Trim();
        act.Year = dto.Year;
        act.PublicationDate = dto.PublicationDate.Trim();
        act.Language = string.IsNullOrWhiteSpace(dto.Language) ? act.Language : dto.Language.Trim();
        act.IsRepealed = dto.IsRepealed;
        act.SourceUrl = dto.SourceUrl.Trim();

        await _actRepo.UpdateAsync(act);
        await _actRepo.SaveChangesAsync();
        return ActSaveResultDto.Ok(act.ActId);
    }

    public async Task<ActDeleteResultDto> DeleteActAsync(int actId)
    {
        var act = await _actRepo.GetByIdAsync(actId);
        if (act is null)
        {
            return ActDeleteResultDto.Fail($"Act {actId} was not found.");
        }

        // FK-safety guards BEFORE any SQL runs -- both FKs are NO ACTION in
        // scripts/02_schema.sql, so letting SQL throw would surface a 500.
        var caseReferences = await _sectionRepo.CountCaseReferencesAsync(actId);
        if (caseReferences > 0)
        {
            return ActDeleteResultDto.Fail(
                $"Cannot delete: {caseReferences} case citation(s) in CASE_ACT_REFERENCE reference this act's sections.");
        }

        var mappings = await _sectionRepo.CountScenarioMappingsAsync(actId);
        if (mappings > 0)
        {
            return ActDeleteResultDto.Fail(
                $"Cannot delete: {mappings} scenario mapping(s) point at this act's sections. Remove them on the Scenarios page first.");
        }

        // Best-effort Qdrant cleanup BEFORE the SQL cascade: orphaned vector
        // points are recoverable via reconciliation (FIX-QDRANT-1), but a Qdrant
        // outage must never wedge the admin delete.
        var chunks = await _chunkRepo.GetByActIdAsync(actId);
        foreach (var vectorId in chunks
                     .Where(c => c.VectorId is not null)
                     .Select(c => c.VectorId!))
        {
            try
            {
                await _vectorStore.DeleteAsync(vectorId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "DeleteActAsync: Qdrant delete failed for {VectorId} (orphaned point is reconcilable): {Message}",
                    vectorId, ex.Message);
            }
        }

        await _actRepo.DeleteWithChildrenAsync(actId);
        await InvalidateAnswerCacheAsync();
        return ActDeleteResultDto.Ok();
    }

    public async Task<ActReindexResultDto?> ReindexActAsync(int actId)
    {
        var act = await _actRepo.GetByIdAsync(actId);
        if (act is null) return null;

        int fresh = 0, stale = 0, pending = 0;
        foreach (var chunk in await _chunkRepo.GetByActIdAsync(actId))
        {
            // ContentHash is null only for rows EmbeddingBatchJob has not stamped
            // yet -- those are already enrolled in the job's "VectorId IS NULL"
            // work query, so there is nothing to re-enroll here.
            if (chunk.ContentHash is null)
            {
                pending++;
                continue;
            }

            var hash = ComputeSha256(chunk.ChunkText);
            if (string.Equals(chunk.ContentHash, hash, StringComparison.Ordinal))
            {
                fresh++;
                continue;
            }

            // Text changed since the last embed. Nulling VectorId re-enrolls the
            // chunk in EmbeddingBatchJob's next pass; storing the recomputed hash
            // keeps the dedupe scan's "hash matches, skip" check correct.
            await _chunkRepo.MarkStaleAsync(chunk.ChunkId, hash);
            stale++;
        }

        // Only clear when the law text actually changed — a no-op re-index
        // (all fresh/pending) must not nuke a warm cache.
        if (stale > 0)
        {
            await InvalidateAnswerCacheAsync();
        }

        return new ActReindexResultDto(actId, fresh + stale + pending, fresh, stale, pending);
    }

    private static string? Validate(ActSaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title)) return "Title is required.";
        if (dto.Title.Trim().Length > 500) return "Title must be 500 characters or fewer (ACT.Title NVARCHAR(500)).";
        if (dto.ActNumber.Trim().Length > 50) return "Act number must be 50 characters or fewer (ACT.ActNumber NVARCHAR(50)).";
        if (dto.Year is < 1600 or > 2100) return "Year must be between 1600 and 2100.";
        if (dto.PublicationDate.Trim().Length > 100) return "Publication date must be 100 characters or fewer.";
        if (dto.Language.Trim().Length > 20) return "Language must be 20 characters or fewer.";
        if (dto.SourceUrl.Trim().Length > 1000) return "Source URL must be 1000 characters or fewer.";
        return null;
    }

    // MUST match EmbeddingBatchJob.ComputeSha256 (Infrastructure/VectorStore/
    // EmbeddingBatchJob.cs) exactly -- lowercase hex of the UTF-8 bytes -- because
    // these hashes are compared against the ContentHash column the job wrote.
    private static string ComputeSha256(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
