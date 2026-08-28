using Moq;
using MuktoAin.Application.Documents;
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using Xunit;

namespace MuktoAin.UnitTests.Services;

public class DocumentGeneratorTests
{
    [Theory]
    [InlineData(1, DocumentType.LabourComplaint)]
    [InlineData(2, DocumentType.GeneralDiary)]
    [InlineData(3, DocumentType.RtiRequest)]
    [InlineData(4, DocumentType.ConsumerComplaint)]
    public void GetDocumentType_ValidCategories_ReturnsExpectedDocumentType(int categoryId, DocumentType expectedType)
    {
        var generator = new DocumentGenerator(Enumerable.Empty<IDocumentTemplate>());

        var result = generator.GetDocumentType(categoryId);

        Assert.Equal(expectedType, result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-1)]
    public void GetDocumentType_UnknownCategory_ThrowsArgumentException(int categoryId)
    {
        var generator = new DocumentGenerator(Enumerable.Empty<IDocumentTemplate>());

        Assert.Throws<ArgumentException>(() => generator.GetDocumentType(categoryId));
    }

    [Fact]
    public async Task GenerateAsync_MatchingTemplateFound_DelegatesToTemplate()
    {
        var mockTemplate = new Mock<IDocumentTemplate>();
        mockTemplate.Setup(t => t.DocumentType).Returns(DocumentType.LabourComplaint);
        mockTemplate.Setup(t => t.RenderAsync(It.IsAny<Case>(), It.IsAny<RightsExplanationDto>()))
            .ReturnsAsync("Rendered Labour Complaint Content");

        var generator = new DocumentGenerator(new[] { mockTemplate.Object });
        var caseEntity = new Case { CategoryId = 1, Description = "Unpaid wage test" };
        var explanation = new RightsExplanationDto("Your rights", Array.Empty<CitedSectionDto>(), "Disclaimer");

        var result = await generator.GenerateAsync(caseEntity, explanation);

        Assert.Equal("Rendered Labour Complaint Content", result);
        mockTemplate.Verify(t => t.RenderAsync(caseEntity, explanation), Times.Once);
    }

    [Fact]
    public async Task GenerateAsync_TemplateMissing_ThrowsInvalidOperationException()
    {
        var generator = new DocumentGenerator(Enumerable.Empty<IDocumentTemplate>());
        var caseEntity = new Case { CategoryId = 1 };
        var explanation = new RightsExplanationDto("Explanation", Array.Empty<CitedSectionDto>(), "Disclaimer");

        await Assert.ThrowsAsync<InvalidOperationException>(() => generator.GenerateAsync(caseEntity, explanation));
    }
}
