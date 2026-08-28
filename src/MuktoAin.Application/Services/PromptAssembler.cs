using System.Text;
using MuktoAin.Domain.Constants;
using MuktoAin.Domain.Enums;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Domain.Interfaces.Services;
using MuktoAin.Domain.Models;

namespace MuktoAin.Application.Services;

public class PromptAssembler : IPromptAssembler
{
    private readonly IScenarioMappingRepository _scenarioMappingRepo;

    public PromptAssembler(IScenarioMappingRepository scenarioMappingRepo)
    {
        _scenarioMappingRepo = scenarioMappingRepo;
    }

    public async Task<string> AssemblePromptAsync(
        string problemDescription,
        IEnumerable<RetrievedSection> sections,
        string language,
        AiRequestType requestType,
        string? documentType = null,
        CancellationToken ct = default)
    {
        var targetLanguage = string.Equals(language, "bn", StringComparison.OrdinalIgnoreCase) ? "Bengali" : "English";
        var disclaimer = Disclaimers.ForLanguage(language);

        var contextBuilder = new StringBuilder();
        var sectionList = sections?.ToList() ?? new List<RetrievedSection>();

        if (sectionList.Count > 0)
        {
            foreach (var section in sectionList)
            {
                contextBuilder.AppendLine($"Act: {section.ActTitle}, Section {section.SectionNumber}: {section.SectionText}");
                contextBuilder.AppendLine();
            }
        }
        else
        {
            contextBuilder.AppendLine("No specific statutory sections retrieved for this problem.");
            contextBuilder.AppendLine();
        }

        // Check scenario mappings for keyword boost hints
        if (!string.IsNullOrWhiteSpace(problemDescription))
        {
            try
            {
                var mappings = (await _scenarioMappingRepo.SearchByKeywordAsync(problemDescription)).ToList();
                if (mappings.Count > 0)
                {
                    contextBuilder.AppendLine("Curated Scenario Guidance:");
                    foreach (var m in mappings)
                    {
                        var note = string.IsNullOrWhiteSpace(m.Notes) ? string.Empty : $" ({m.Notes})";
                        contextBuilder.AppendLine($"- Keyword '{m.ScenarioKeyword}' maps to Section ID {m.SectionId}{note}");
                    }
                    contextBuilder.AppendLine();
                }
            }
            catch
            {
                // Graceful degradation: scenario mapping search failure shouldn't block prompt assembly
            }
        }

        var context = contextBuilder.ToString().TrimEnd();

        var template = requestType switch
        {
            AiRequestType.Drafting => PromptTemplates.DocumentDrafting,
            _ => PromptTemplates.RightsExplanation,
        };

        var prompt = template
            .Replace("{problem}", problemDescription?.Trim() ?? string.Empty)
            .Replace("{language}", targetLanguage)
            .Replace("{context}", context)
            .Replace("{disclaimer}", disclaimer)
            .Replace("{documentType}", documentType ?? "Legal Document");

        return prompt;
    }
}
