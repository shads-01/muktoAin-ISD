using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using MuktoAin.Application.DTOs;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using Moq;

namespace MuktoAin.UnitTests.Services;

public class ActsManagementServiceTests
{
    private readonly Mock<IActRepository> _actRepo = new();
    private readonly Mock<IActSectionRepository> _sectionRepo = new();
    private readonly Mock<IActSectionChunkRepository> _chunkRepo = new();
    private readonly Mock<IVectorStore> _vectorStore = new();
    private readonly Mock<ILogger<ActsManagementService>> _logger = new();
    private readonly Mock<IRepository<AnswerCache>> _cacheRepo = new();
    private readonly ActsManagementService _service;

    public ActsManagementServiceTests()
    {
        _service = new ActsManagementService(
            _actRepo.Object,
            _sectionRepo.Object,
            _chunkRepo.Object,
            _vectorStore.Object,
            _cacheRepo.Object,
            _logger.Object);
    }

    private static Act NewAct(int id = 1, string title = "The Labour Act, 2006") => new()
    {
        ActId = id,
        Title = title,
        ActNumber = "2006",
        Year = 2006,
        PublicationDate = "01/10/2006",
        Language = "english",
        SourceUrl = "http://example.com/act",
        ImportedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    // ---------- GetActsAsync ----------

    [Fact]
    public async Task GetActsAsync_ClampsPageAndPageSize()
    {
        _actRepo
            .Setup(r => r.GetPagedAsync(null, 1, 100))
            .ReturnsAsync((new List<Act>(), 0));

        var result = await _service.GetActsAsync(keyword: null, page: 0, pageSize: 500);

        Assert.Equal(1, result.Page);
        Assert.Equal(100, result.PageSize);
        _actRepo.Verify(r => r.GetPagedAsync(null, 1, 100), Times.Once);
    }

    [Fact]
    public async Task GetActsAsync_TrimsKeywordAndMapsRowsToListDtos()
    {
        var act = NewAct();
        act.Sections.Add(new ActSection { OrdinalPosition = 1, SectionText = "s1" });
        act.Sections.Add(new ActSection { OrdinalPosition = 2, SectionText = "s2" });
        _actRepo
            .Setup(r => r.GetPagedAsync("labour", 1, 20))
            .ReturnsAsync((new List<Act> { act } as IReadOnlyList<Act>, 1));

        var result = await _service.GetActsAsync("  labour  ", 1, 20);

        Assert.Equal(1, result.TotalCount);
        var dto = Assert.Single(result.Items);
        Assert.Equal(act.ActId, dto.ActId);
        Assert.Equal(act.Title, dto.Title);
        Assert.Equal(2, dto.SectionCount);
        Assert.Equal(act.ImportedAt, dto.ImportedAt);
        _actRepo.Verify(r => r.GetPagedAsync("labour", 1, 20), Times.Once);
    }

    // ---------- GetActAsync ----------

    [Fact]
    public async Task GetActAsync_UnknownId_ReturnsNull()
    {
        _actRepo.Setup(r => r.GetWithSectionsAsync(999)).ReturnsAsync((Act?)null);

        var result = await _service.GetActAsync(999);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetActAsync_MapsSectionsOrderedByOrdinal_AndCountsStaleChunks()
    {
        var act = NewAct();
        act.Sections.Add(new ActSection
        {
            SectionId = 10,
            ActId = act.ActId,
            OrdinalPosition = 2,
            SectionTitle = "Second",
            SectionText = "second text"
        });
        act.Sections.Add(new ActSection
        {
            SectionId = 11,
            ActId = act.ActId,
            OrdinalPosition = 1,
            SectionTitle = "First",
            SectionText = "first text"
        });
        _actRepo.Setup(r => r.GetWithSectionsAsync(act.ActId)).ReturnsAsync(act);

        var staleChunk = new ActSectionChunk
        {
            ChunkId = 1,
            SectionId = 10,
            ChunkOrder = 1,
            ChunkText = "changed text",
            TokenCount = 2,
            VectorId = "v-old",
            ContentHash = "0000000000000000000000000000000000000000000000000000000000000000"
        };
        var freshChunk = new ActSectionChunk
        {
            ChunkId = 2,
            SectionId = 10,
            ChunkOrder = 2,
            ChunkText = "unchanged text",
            TokenCount = 1,
            VectorId = "v-fresh",
            ContentHash = Sha256("unchanged text"),
            LastEmbeddedAt = DateTime.UtcNow
        };
        var pendingChunk = new ActSectionChunk
        {
            ChunkId = 3,
            SectionId = 10,
            ChunkOrder = 3,
            ChunkText = "never embedded",
            TokenCount = 2
        };
        _chunkRepo
            .Setup(r => r.GetByActIdAsync(act.ActId))
            .ReturnsAsync(new[] { staleChunk, freshChunk, pendingChunk });

        var result = await _service.GetActAsync(act.ActId);

        Assert.NotNull(result);
        // Sections come back in OrdinalPosition order regardless of load order.
        Assert.Equal(new[] { 1, 2 }, result!.Sections.Select(s => s.OrdinalPosition));
        var second = result.Sections.Single(s => s.SectionId == 10);
        Assert.Equal(3, second.ChunkCount);
        Assert.Equal(1, second.StaleChunkCount); // pending is NOT stale; fresh is not stale
        var first = result.Sections.Single(s => s.SectionId == 11);
        Assert.Equal(0, first.ChunkCount);
        Assert.Equal(0, first.StaleChunkCount);
    }

    // ---------- CreateActAsync ----------

    [Fact]
    public async Task CreateActAsync_BlankTitle_FailsWithoutTouchingRepositories()
    {
        var result = await _service.CreateActAsync(new ActSaveDto("   ", "1", 2006, "", "", false, ""));

        Assert.False(result.Success);
        Assert.Equal("Title is required.", result.Error);
        _actRepo.Verify(r => r.AddAsync(It.IsAny<Act>()), Times.Never);
    }

    [Fact]
    public async Task CreateActAsync_YearOutOfRange_Fails()
    {
        var result = await _service.CreateActAsync(new ActSaveDto("Some Act", "", 1500, "", "", false, ""));

        Assert.False(result.Success);
        Assert.Equal("Year must be between 1600 and 2100.", result.Error);
    }

    [Fact]
    public async Task CreateActAsync_DuplicateTitleYear_Fails()
    {
        _actRepo
            .Setup(r => r.ExistsByTitleYearAsync("The Labour Act, 2006", 2006, null))
            .ReturnsAsync(true);

        var result = await _service.CreateActAsync(
            new ActSaveDto("The Labour Act, 2006", "", 2006, "", "english", false, ""));

        Assert.False(result.Success);
        Assert.Contains("already exists", result.Error);
        _actRepo.Verify(r => r.AddAsync(It.IsAny<Act>()), Times.Never);
    }

    [Fact]
    public async Task CreateActAsync_Valid_AddsActWithTrimmedFieldsAndReturnsId()
    {
        _actRepo
            .Setup(r => r.ExistsByTitleYearAsync("The Labour Act, 2006", 2006, null))
            .ReturnsAsync(false);
        _actRepo
            .Setup(r => r.AddAsync(It.IsAny<Act>()))
            .Callback<Act>(a => a.ActId = 42)
            .Returns(Task.CompletedTask);

        var result = await _service.CreateActAsync(
            new ActSaveDto("  The Labour Act, 2006  ", " 2006 ", 2006, " 01/10/2006 ", "  english  ", false, " http://example.com "));

        Assert.True(result.Success);
        Assert.Equal(42, result.ActId);
        _actRepo.Verify(r => r.AddAsync(It.Is<Act>(a =>
            a.Title == "The Labour Act, 2006"
            && a.ActNumber == "2006"
            && a.Year == 2006
            && a.Language == "english"
            && a.TokenCount == 0)), Times.Once);
        _actRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    // ---------- UpdateActAsync ----------

    [Fact]
    public async Task UpdateActAsync_UnknownId_Fails()
    {
        _actRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((Act?)null);

        var result = await _service.UpdateActAsync(999, new ActSaveDto("t", "", 2006, "", "", false, ""));

        Assert.False(result.Success);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task UpdateActAsync_TitleYearTakenByAnotherAct_Fails()
    {
        var act = NewAct(id: 1);
        _actRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(act);
        _actRepo
            .Setup(r => r.ExistsByTitleYearAsync("Other Act", 1999, 1))
            .ReturnsAsync(true);

        var result = await _service.UpdateActAsync(1, new ActSaveDto("Other Act", "", 1999, "", "", false, ""));

        Assert.False(result.Success);
        Assert.Contains("Another act", result.Error);
        _actRepo.Verify(r => r.UpdateAsync(It.IsAny<Act>()), Times.Never);
    }

    [Fact]
    public async Task UpdateActAsync_Valid_UpdatesMetadataOnly()
    {
        var act = NewAct(id: 1);
        act.Sections.Add(new ActSection { OrdinalPosition = 1, SectionText = "existing text" });
        _actRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(act);
        _actRepo
            .Setup(r => r.ExistsByTitleYearAsync("The Labour (Amended) Act, 2006", 2006, 1))
            .ReturnsAsync(false);

        var result = await _service.UpdateActAsync(1, new ActSaveDto(
            "The Labour (Amended) Act, 2006", "2006-A", 2006, "05/10/2006", "english", IsRepealed: true, "http://example.com/amended"));

        Assert.True(result.Success);
        Assert.Equal(1, result.ActId);
        Assert.Equal("The Labour (Amended) Act, 2006", act.Title);
        Assert.Equal("2006-A", act.ActNumber);
        Assert.True(act.IsRepealed);
        // Metadata-only: sections untouched, so existing chunk hashes stay valid.
        Assert.Empty(act.Sections.Single().Chunks);
        _actRepo.Verify(r => r.UpdateAsync(act), Times.Once);
        _actRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    // ---------- DeleteActAsync ----------

    [Fact]
    public async Task DeleteActAsync_UnknownId_Fails()
    {
        _actRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((Act?)null);

        var result = await _service.DeleteActAsync(999);

        Assert.False(result.Success);
        Assert.Contains("not found", result.Error);
        _actRepo.Verify(r => r.DeleteWithChildrenAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task DeleteActAsync_BlockedWhenCaseCitationsReferenceTheAct()
    {
        _actRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(NewAct(id: 1));
        _sectionRepo.Setup(r => r.CountCaseReferencesAsync(1)).ReturnsAsync(3);

        var result = await _service.DeleteActAsync(1);

        Assert.False(result.Success);
        Assert.Contains("3 case citation", result.Error);
        _actRepo.Verify(r => r.DeleteWithChildrenAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task DeleteActAsync_BlockedWhenScenarioMappingsReferenceTheAct()
    {
        _actRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(NewAct(id: 1));
        _sectionRepo.Setup(r => r.CountCaseReferencesAsync(1)).ReturnsAsync(0);
        _sectionRepo.Setup(r => r.CountScenarioMappingsAsync(1)).ReturnsAsync(2);

        var result = await _service.DeleteActAsync(1);

        Assert.False(result.Success);
        Assert.Contains("2 scenario mapping", result.Error);
        _actRepo.Verify(r => r.DeleteWithChildrenAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task DeleteActAsync_Clean_DeletesQdrantVectorsThenCascadeDeletes()
    {
        _actRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(NewAct(id: 1));
        _sectionRepo.Setup(r => r.CountCaseReferencesAsync(1)).ReturnsAsync(0);
        _sectionRepo.Setup(r => r.CountScenarioMappingsAsync(1)).ReturnsAsync(0);
        _chunkRepo.Setup(r => r.GetByActIdAsync(1)).ReturnsAsync(new[]
        {
            new ActSectionChunk { ChunkId = 1, SectionId = 10, ChunkOrder = 1, ChunkText = "a", TokenCount = 1, VectorId = "v-1", ContentHash = "h1" },
            new ActSectionChunk { ChunkId = 2, SectionId = 10, ChunkOrder = 2, ChunkText = "b", TokenCount = 1 }, // never embedded
            new ActSectionChunk { ChunkId = 3, SectionId = 10, ChunkOrder = 3, ChunkText = "c", TokenCount = 1, VectorId = "v-2", ContentHash = "h2" },
        });

        var result = await _service.DeleteActAsync(1);

        Assert.True(result.Success);
        _vectorStore.Verify(v => v.DeleteAsync("v-1"), Times.Once);
        _vectorStore.Verify(v => v.DeleteAsync("v-2"), Times.Once);
        _vectorStore.Verify(v => v.DeleteAsync(It.IsAny<string>()), Times.Exactly(2));
        _actRepo.Verify(r => r.DeleteWithChildrenAsync(1), Times.Once);
    }

    [Fact]
    public async Task DeleteActAsync_QdrantOutage_StillDeletesAct()
    {
        _actRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(NewAct(id: 1));
        _sectionRepo.Setup(r => r.CountCaseReferencesAsync(1)).ReturnsAsync(0);
        _sectionRepo.Setup(r => r.CountScenarioMappingsAsync(1)).ReturnsAsync(0);
        _chunkRepo.Setup(r => r.GetByActIdAsync(1)).ReturnsAsync(new[]
        {
            new ActSectionChunk { ChunkId = 1, SectionId = 10, ChunkOrder = 1, ChunkText = "a", TokenCount = 1, VectorId = "v-1", ContentHash = "h1" },
        });
        _vectorStore
            .Setup(v => v.DeleteAsync("v-1"))
            .ThrowsAsync(new HttpRequestException("qdrant down"));

        var result = await _service.DeleteActAsync(1);

        // Orphaned points are recoverable (FIX-QDRANT-1 reconciliation); a wedged
        // delete is not. SQL deletion must win.
        Assert.True(result.Success);
        _actRepo.Verify(r => r.DeleteWithChildrenAsync(1), Times.Once);
    }

    // ---------- ReindexActAsync ----------

    [Fact]
    public async Task ReindexActAsync_UnknownId_ReturnsNull()
    {
        _actRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((Act?)null);

        var result = await _service.ReindexActAsync(999);

        Assert.Null(result);
    }

    [Fact]
    public async Task ReindexActAsync_ClassifiesFreshStalePending_AndStampsStaleChunks()
    {
        _actRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(NewAct(id: 1));
        _chunkRepo.Setup(r => r.GetByActIdAsync(1)).ReturnsAsync(new[]
        {
            // Fresh: stored hash matches the current chunk text.
            new ActSectionChunk { ChunkId = 1, SectionId = 10, ChunkOrder = 1, ChunkText = "unchanged", TokenCount = 1, VectorId = "v-1", ContentHash = Sha256("unchanged") },
            // Stale: text was edited after the last embed -- stored hash no longer matches.
            new ActSectionChunk { ChunkId = 2, SectionId = 10, ChunkOrder = 2, ChunkText = "edited", TokenCount = 1, VectorId = "v-2", ContentHash = "stale-hash-from-last-embed" },
            // Pending: never stamped by EmbeddingBatchJob (its own work query will pick it up).
            new ActSectionChunk { ChunkId = 3, SectionId = 10, ChunkOrder = 3, ChunkText = "never embedded", TokenCount = 2 },
        });

        var result = await _service.ReindexActAsync(1);

        Assert.NotNull(result);
        Assert.Equal(1, result!.ActId);
        Assert.Equal(3, result.TotalChunks);
        Assert.Equal(1, result.FreshChunks);
        Assert.Equal(1, result.StaleChunks);
        Assert.Equal(1, result.PendingChunks);

        // Only the stale chunk is re-enrolled: VectorId nulled (so the job's
        // "VectorId IS NULL" query re-embeds it) and the recomputed hash stored.
        _chunkRepo.Verify(
            r => r.MarkStaleAsync(2, Sha256("edited")),
            Times.Once);
        _chunkRepo.Verify(
            r => r.MarkStaleAsync(It.IsAny<int>(), It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task ReindexActAsync_StoresHashInEmbeddingBatchJobFormat_LowercaseHex()
    {
        _actRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(NewAct(id: 1));
        _chunkRepo.Setup(r => r.GetByActIdAsync(1)).ReturnsAsync(new[]
        {
            new ActSectionChunk { ChunkId = 1, SectionId = 10, ChunkOrder = 1, ChunkText = "বাংলা টেক্সট", TokenCount = 3, VectorId = "v-1", ContentHash = "stale" },
        });

        await _service.ReindexActAsync(1);

        // Same expression as EmbeddingBatchJob.ComputeSha256 -- lowercase hex of
        // the UTF-8 bytes -- so the stored hash stays comparable across systems.
        _chunkRepo.Verify(
            r => r.MarkStaleAsync(1, Sha256("বাংলা টেক্সট")),
            Times.Once);
    }

    private static string Sha256(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    // ---------- A7: ANSWER_CACHE invalidation on law changes ----------

    [Fact]
    public async Task ReindexActAsync_StaleChunksFound_ClearsAnswerCache()
    {
        _actRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(NewAct(id: 1));
        _chunkRepo.Setup(r => r.GetByActIdAsync(1)).ReturnsAsync(new[]
        {
            new ActSectionChunk { ChunkId = 1, SectionId = 10, ChunkOrder = 1, ChunkText = "edited", TokenCount = 1, VectorId = "v-1", ContentHash = "stale-hash" },
        });
        _cacheRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<AnswerCache>
        {
            new() { AnswerCacheId = 1, QueryHash = "h1", Answer = "a" },
            new() { AnswerCacheId = 2, QueryHash = "h2", Answer = "b" }
        });

        await _service.ReindexActAsync(1);

        // Stale chunks mean the law text changed — every cached rights
        // explanation is potentially outdated and must be dropped.
        _cacheRepo.Verify(r => r.GetAllAsync(), Times.Once);
        _cacheRepo.Verify(r => r.DeleteAsync(It.IsAny<AnswerCache>()), Times.Exactly(2));
        _cacheRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task ReindexActAsync_AllFresh_NoCacheClear()
    {
        _actRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(NewAct(id: 1));
        _chunkRepo.Setup(r => r.GetByActIdAsync(1)).ReturnsAsync(new[]
        {
            new ActSectionChunk { ChunkId = 1, SectionId = 10, ChunkOrder = 1, ChunkText = "same", TokenCount = 1, VectorId = "v-1", ContentHash = Sha256("same") },
        });

        await _service.ReindexActAsync(1);

        _cacheRepo.Verify(r => r.DeleteAsync(It.IsAny<AnswerCache>()), Times.Never);
    }

    [Fact]
    public async Task DeleteActAsync_Clean_ClearsAnswerCache()
    {
        _actRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(NewAct(id: 1));
        _sectionRepo.Setup(r => r.CountCaseReferencesAsync(1)).ReturnsAsync(0);
        _sectionRepo.Setup(r => r.CountScenarioMappingsAsync(1)).ReturnsAsync(0);
        _chunkRepo.Setup(r => r.GetByActIdAsync(1)).ReturnsAsync(new[]
        {
            new ActSectionChunk { ChunkId = 1, SectionId = 10, ChunkOrder = 1, ChunkText = "a", TokenCount = 1, VectorId = "v-1", ContentHash = "h1" },
        });
        _cacheRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new List<AnswerCache>
        {
            new() { AnswerCacheId = 1, QueryHash = "h1", Answer = "a" },
            new() { AnswerCacheId = 2, QueryHash = "h2", Answer = "b" }
        });

        var result = await _service.DeleteActAsync(1);

        Assert.True(result.Success);
        _cacheRepo.Verify(r => r.DeleteAsync(It.IsAny<AnswerCache>()), Times.Exactly(2));
    }
}
