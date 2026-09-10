using Moq;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Interfaces.Repositories;
using Xunit;

namespace MuktoAin.UnitTests.Services;

public class DocumentTranslationServiceTests
{
    private readonly Mock<IRepository<DocumentTranslation>> _translationRepo = new();
    private readonly Mock<MuktoAin.Domain.Interfaces.IAiService> _aiService = new();

    private DocumentTranslationService MakeService() =>
        new(_translationRepo.Object, _aiService.Object);

    [Fact]
    public async Task GetOrTranslateAsync_CacheHit_ReturnsCachedContent_NoAiCall()
    {
        var cached = new DocumentTranslation
        {
            DocumentTranslationId = 1,
            DocumentId = 42,
            Language = "en",
            TranslatedContent = "Cached English text",
            CreatedAt = DateTime.UtcNow
        };
        _translationRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { cached });

        var service = MakeService();
        var result = await service.GetOrTranslateAsync(42, "বাংলা লেখা", "bn", "en");

        Assert.Equal("Cached English text", result.Content);
        Assert.True(result.IsTranslated);
        _aiService.Verify(a => a.GenerateContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _translationRepo.Verify(r => r.AddAsync(It.IsAny<DocumentTranslation>()), Times.Never);
    }

    [Fact]
    public async Task GetOrTranslateAsync_CacheMiss_CallsAiAndPersists()
    {
        _translationRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(Array.Empty<DocumentTranslation>());
        _aiService.Setup(a => a.GenerateContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Freshly translated English text of reasonable length");

        var service = MakeService();
        var result = await service.GetOrTranslateAsync(42, "মূল বাংলা লেখা যথেষ্ট দীর্ঘ", "bn", "en");

        Assert.Equal("Freshly translated English text of reasonable length", result.Content);
        Assert.True(result.IsTranslated);
        _translationRepo.Verify(r => r.AddAsync(It.Is<DocumentTranslation>(
            t => t.DocumentId == 42 && t.Language == "en" && t.TranslatedContent == "Freshly translated English text of reasonable length")), Times.Once);
        _translationRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task GetOrTranslateAsync_EmptyAiResult_ThrowsAndDoesNotCache()
    {
        _translationRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(Array.Empty<DocumentTranslation>());
        _aiService.Setup(a => a.GenerateContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        var service = MakeService();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetOrTranslateAsync(42, "একটি দীর্ঘ মূল বাংলা লেখা এখানে আছে", "bn", "en"));
        _translationRepo.Verify(r => r.AddAsync(It.IsAny<DocumentTranslation>()), Times.Never);
    }

    [Fact]
    public async Task GetOrTranslateAsync_PromptIncludesSourceAndTargetLanguageAndContent()
    {
        _translationRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(Array.Empty<DocumentTranslation>());
        string? capturedPrompt = null;
        _aiService.Setup(a => a.GenerateContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((p, _) => capturedPrompt = p)
            .ReturnsAsync("Translated output long enough to pass the quality check");

        var service = MakeService();
        await service.GetOrTranslateAsync(42, "মূল বিষয়বস্তু এখানে যথেষ্ট দীর্ঘ", "bn", "en");

        Assert.NotNull(capturedPrompt);
        Assert.Contains("bn", capturedPrompt);
        Assert.Contains("en", capturedPrompt);
        Assert.Contains("মূল বিষয়বস্তু এখানে যথেষ্ট দীর্ঘ", capturedPrompt);
    }
}
