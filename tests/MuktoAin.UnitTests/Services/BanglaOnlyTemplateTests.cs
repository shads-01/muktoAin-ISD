using Moq;
using MuktoAin.Application.Documents;
using MuktoAin.Application.Documents.Templates;
using MuktoAin.Application.DTOs;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Constants;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using Xunit;

namespace MuktoAin.UnitTests.Services;

public class BanglaOnlyTemplateTests
{
    // All four concrete templates — mirrors Program.cs DI registrations.
    public static TheoryData<IDocumentTemplate, DocumentType> AllTemplates => new()
    {
        { new LabourComplaintTemplate(), DocumentType.LabourComplaint },
        { new GeneralDiaryTemplate(), DocumentType.GeneralDiary },
        { new RtiRequestTemplate(), DocumentType.RtiRequest },
        { new ConsumerComplaintTemplate(), DocumentType.ConsumerComplaint },
    };

    private static Case BuildCase(string description, string language = "bn") => new()
    {
        CaseId = 30,
        CategoryId = 1,
        District = new District { DistrictId = 1, Name = "ঢাকা" },
        DistrictId = 1,
        Title = "অবৈধ বেতন কাটা",
        Description = description,
        Language = language,
    };

    private static RightsExplanationDto BuildExplanation(bool withSections = true) => new(
        Explanation: "শ্রম আইন অনুযায়ী আপনার বেতন পাওয়ার অধিকার রয়েছে।",
        CitedSections: withSections
            ? new List<CitedSectionDto>
              {
                  new(1, "Bangladesh Labour Act, 2006", "123",
                      "The wages of every worker shall be paid before the expiry of the seventh working day.",
                      0.92f, "Vector")
              }
            : new List<CitedSectionDto>(),
        Disclaimer: Disclaimers.LegalBangla);

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public async Task RenderBanglaOnlyAsync_ContainsNoEnglishTemplateCopy(IDocumentTemplate template, DocumentType _)
    {
        var rendered = await ((IBanglaDocumentVariant)template)
            .RenderBanglaOnlyAsync(BuildCase("নিয়োগকর্তা তিন মাস বেতন দেয়নি।"), BuildExplanation());

        Assert.DoesNotContain("FACTS OF THE CASE", rendered);
        Assert.DoesNotContain("STATEMENT OF FACTS", rendered);
        Assert.DoesNotContain("APPLICABLE LEGAL PROVISIONS", rendered);
        Assert.DoesNotContain("RELIEF SOUGHT", rendered);
        Assert.DoesNotContain("DECLARATION:", rendered);
        Assert.DoesNotContain("Respected", rendered);
        Assert.DoesNotContain("Subject:", rendered);
        // English disclaimer must NOT appear in the Bangla-only variant
        Assert.DoesNotContain(Disclaimers.Legal, rendered);
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public async Task RenderBanglaOnlyAsync_ContainsBanglaSkeletonAndBanglaDisclaimer(
        IDocumentTemplate template, DocumentType docType)
    {
        var rendered = await ((IBanglaDocumentVariant)template)
            .RenderBanglaOnlyAsync(BuildCase("নিয়োগকর্তা তিন মাস বেতন দেয়নি।"), BuildExplanation());

        Assert.Contains("বরাবর", rendered);                      // header salutation
        var factsHeading = docType switch
        {
            DocumentType.LabourComplaint => "মামলার ঘটনাবলি",
            DocumentType.GeneralDiary => "ঘটনার বিবরণ",
            DocumentType.RtiRequest => "প্রার্থিত তথ্য",
            DocumentType.ConsumerComplaint => "অভিযোগের ঘটনাবলি",
            _ => "ঘটনা"
        };
        Assert.Contains(factsHeading, rendered);
        Assert.Contains("প্রযোজ্য আইনি বিধান", rendered);
        Assert.Contains("ঘোষণা", rendered);
        Assert.Contains(Disclaimers.LegalBangla, rendered);      // surface 3 of 3 preserved
        Assert.Contains("তারিখ:", rendered);
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public async Task RenderBanglaOnlyAsync_MixedLanguage_UserEnglishContentStillRenderedVerbatim(
        IDocumentTemplate template, DocumentType _)
    {
        const string englishDescription =
            "The employer has withheld wages for three months despite written requests.";
        var rendered = await ((IBanglaDocumentVariant)template)
            .RenderBanglaOnlyAsync(BuildCase(englishDescription), BuildExplanation());

        // Citizen-supplied content is DATA, not template copy — it flows through
        // untouched even when the skeleton is Bangla-only.
        Assert.Contains(englishDescription, rendered);
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public async Task RenderBanglaOnlyAsync_NoCitedSections_RendersBanglaFallback(
        IDocumentTemplate template, DocumentType docType)
    {
        var rendered = await ((IBanglaDocumentVariant)template)
            .RenderBanglaOnlyAsync(BuildCase("বিবরণ"), BuildExplanation(withSections: false));

        if (docType is DocumentType.LabourComplaint or DocumentType.GeneralDiary)
        {
            Assert.Contains("[নির্দিষ্ট কোনো ধারা পাওয়া যায়নি", rendered);
        }
        else if (docType == DocumentType.RtiRequest)
        {
            Assert.Contains("তথ্য অধিকার আইন, ২০০৯", rendered);
        }
        else if (docType == DocumentType.ConsumerComplaint)
        {
            Assert.Contains("ভোক্তা অধিকার সংরক্ষণ আইন, ২০০৯", rendered);
        }
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public async Task RenderBanglaOnlyAsync_MissingDistrict_UsesUnderscorePlaceholder(
        IDocumentTemplate template, DocumentType _)
    {
        var c = BuildCase("বিবরণ");
        c.District = null!;
        var rendered = await ((IBanglaDocumentVariant)template).RenderBanglaOnlyAsync(c, BuildExplanation());

        Assert.Contains("________", rendered);
    }

    [Fact]
    public async Task DocumentGenerator_GenerateBanglaOnlyAsync_RoutesLabourCaseToBanglaVariant()
    {
        var generator = new DocumentGenerator(new IDocumentTemplate[]
        {
            new LabourComplaintTemplate(), new GeneralDiaryTemplate(),
            new RtiRequestTemplate(), new ConsumerComplaintTemplate(),
        });
        var c = BuildCase("নিয়োগকর্তা তিন মাস বেতন দেয়নি।");
        c.CategoryId = 1; // Labour per DocumentGenerator.MapCategoryToDocumentType

        var rendered = await generator.GenerateBanglaOnlyAsync(c, BuildExplanation());

        Assert.Contains("প্রযোজ্য আইনি বিধান", rendered);
        Assert.DoesNotContain("APPLICABLE LEGAL PROVISIONS", rendered);
    }

    [Fact]
    public async Task DocumentService_GenerateDocumentAsync_BanglaCase_UsesBanglaOnlyTemplate()
    {
        var docRepo = new Mock<IRepository<GeneratedDocument>>();
        var caseRepo = new Mock<ICaseRepository>();
        var districtRepo = new Mock<IRepository<District>>();
        var categoryRepo = new Mock<IRepository<CaseCategory>>();
        var pdfExporter = new Mock<IPdfExporter>();
        pdfExporter.Setup(p => p.GeneratePdf(It.IsAny<GeneratedDocument>(), It.IsAny<Case>()))
            .Returns(Array.Empty<byte>());
        var c = BuildCase("নিয়োগকর্তা তিন মাস বেতন দেয়নি।", language: "bn");
        caseRepo.Setup(r => r.GetByIdAsync(30)).ReturnsAsync(c);
        districtRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(c.District);
        categoryRepo.Setup(r => r.GetByIdAsync(1))
            .ReturnsAsync(new CaseCategory { CategoryId = 1, Name = "Labour", NameBn = "শ্রম" });

        var service = new DocumentService(
            new DocumentGenerator(new IDocumentTemplate[]
            {
                new LabourComplaintTemplate(), new GeneralDiaryTemplate(),
                new RtiRequestTemplate(), new ConsumerComplaintTemplate(),
            }),
            docRepo.Object, caseRepo.Object, districtRepo.Object, categoryRepo.Object, pdfExporter.Object);

        var dto = await service.GenerateDocumentAsync(30, BuildExplanation());

        Assert.Contains("প্রযোজ্য আইনি বিধান", dto.ContentDraft);
        Assert.DoesNotContain("APPLICABLE LEGAL PROVISIONS", dto.ContentDraft);
    }
}
