using MuktoAin.Application.Documents.Templates;
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using Xunit;

namespace MuktoAin.UnitTests.Documents;

public class LabourComplaintTemplateTests
{
    private static Case MakeCase(string language = "bn") => new Case
    {
        CaseId = 1,
        Description = "তিন মাসের বকেয়া মজুরি পরিশোধ করা হয়নি।",
        Language = language,
        District = new District { Name = "Dhaka" }
    };

    private static RightsExplanationDto MakeExplanation(string explanation) =>
        new(explanation, Array.Empty<CitedSectionDto>(), "Disclaimer text");

    [Fact]
    public async Task RenderAsync_BanglaCase_UsesBanglaHeadings()
    {
        var template = new LabourComplaintTemplate();
        var result = await template.RenderAsync(MakeCase("bn"), MakeExplanation("আপনার অধিকার আছে।"));

        Assert.Contains("মামলার ঘটনা", result);
        Assert.Contains("প্রযোজ্য আইনি বিধান", result);
        Assert.Contains("প্রার্থিত প্রতিকার", result);
        Assert.DoesNotContain("FACTS OF THE CASE", result);
        Assert.DoesNotContain("APPLICABLE LEGAL PROVISIONS", result);
    }

    [Fact]
    public async Task RenderAsync_EnglishCase_UsesEnglishHeadings()
    {
        var template = new LabourComplaintTemplate();
        var result = await template.RenderAsync(MakeCase("en"), MakeExplanation("You have rights."));

        Assert.Contains("Facts of the Case", result);
        Assert.Contains("Applicable Legal Provisions", result);
        Assert.Contains("Relief Sought", result);
        Assert.DoesNotContain("মামলার ঘটনা", result);
    }

    [Fact]
    public async Task RenderAsync_NoAsciiDividers()
    {
        var template = new LabourComplaintTemplate();
        var result = await template.RenderAsync(MakeCase(), MakeExplanation("ব্যাখ্যা"));

        Assert.DoesNotContain('─', result);
        Assert.DoesNotContain('═', result);
    }

    [Fact]
    public async Task RenderAsync_StripsMarkdownFromAiExplanation()
    {
        var template = new LabourComplaintTemplate();
        var explanation = "**নিয়োগকর্তা মাসিক মজুরি** পরিশোধ করতে বাধ্য থাকিবেন।\n- আপনি দাবি করতে পারেন\n- আপনি ক্ষতিপূরণ চাইতে পারেন";

        var result = await template.RenderAsync(MakeCase(), MakeExplanation(explanation));

        Assert.DoesNotContain("**", result);
        Assert.DoesNotContain("\n- ", result);
    }

    [Fact]
    public async Task RenderAsync_CollapsesRepeatedBlankLinesFromAiExplanation()
    {
        var template = new LabourComplaintTemplate();
        var explanation = "প্রথম লাইন।\n\n\n\nদ্বিতীয় লাইন।";

        var result = await template.RenderAsync(MakeCase(), MakeExplanation(explanation));

        Assert.DoesNotContain("\n\n\n", result);
    }
}
