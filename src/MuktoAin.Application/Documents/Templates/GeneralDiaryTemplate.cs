using System.Text;
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Constants;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;

namespace MuktoAin.Application.Documents.Templates;

/// <summary>
/// Bangladesh Police General Diary (GD) entry application template.
/// Follows the format specified in Arpita_plan.md Step 3.1 —
/// structured application for filing a GD at a police station.
/// </summary>
public class GeneralDiaryTemplate : IDocumentTemplate
{
    public DocumentType DocumentType => DocumentType.GeneralDiary;

    public Task<string> RenderAsync(Case caseEntity, RightsExplanationDto explanation)
    {
        var districtName = caseEntity.District?.Name ?? "________";
        var sb = new StringBuilder();

        // ── Header ──────────────────────────────────────────────
        sb.AppendLine("TO");
        sb.AppendLine("The Officer-in-Charge");
        sb.AppendLine($"Police Station / Thana, {districtName}, Bangladesh");
        sb.AppendLine();

        // ── Subject ─────────────────────────────────────────────
        var primarySection = explanation.CitedSections.FirstOrDefault();
        var sectionRef = primarySection != null
            ? $" (Relating to {primarySection.ActTitle}, Section {primarySection.SectionNumber})"
            : string.Empty;
        sb.AppendLine($"Subject: General Diary (GD) Entry Application{sectionRef}");
        sb.AppendLine();

        // ── Salutation ──────────────────────────────────────────
        sb.AppendLine("Respected Sir/Madam,");
        sb.AppendLine();

        // ── Applicant Introduction ──────────────────────────────
        sb.AppendLine($"I, the undersigned, resident of {districtName}, do hereby respectfully submit this " +
                       "application to record a General Diary (GD) entry regarding the incident/circumstances described below:");
        sb.AppendLine();

        // ── Facts of the Case / Statement ───────────────────────
        sb.AppendLine("STATEMENT OF FACTS:");
        sb.AppendLine(new string('─', 40));
        sb.AppendLine(caseEntity.Description);
        sb.AppendLine();

        // ── Applicable Legal Provisions ─────────────────────────
        sb.AppendLine("APPLICABLE LEGAL PROVISIONS:");
        sb.AppendLine(new string('─', 40));
        if (explanation.CitedSections.Count > 0)
        {
            foreach (var section in explanation.CitedSections)
            {
                sb.AppendLine($"• {section.ActTitle}, Section {section.SectionNumber}:");
                sb.AppendLine($"  {section.SectionText}");
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("  [No specific sections retrieved — consult the duty officer or a qualified advocate]");
            sb.AppendLine();
        }

        // ── Rights Explanation ──────────────────────────────────
        if (!string.IsNullOrWhiteSpace(explanation.Explanation))
        {
            sb.AppendLine("APPLICABLE LEGAL RIGHTS & REMEDIES:");
            sb.AppendLine(new string('─', 40));
            sb.AppendLine(explanation.Explanation);
            sb.AppendLine();
        }

        // ── Prayer ──────────────────────────────────────────────
        sb.AppendLine("PRAYER:");
        sb.AppendLine(new string('─', 40));
        sb.AppendLine("I most humbly pray that the aforementioned facts and circumstances be duly recorded");
        sb.AppendLine("in the General Diary register of your police station, necessary inquiry or investigation");
        sb.AppendLine("be initiated, and appropriate lawful measures be taken for legal protection and safety.");
        sb.AppendLine();

        // ── Declaration ─────────────────────────────────────────
        sb.AppendLine("DECLARATION:");
        sb.AppendLine(new string('─', 40));
        sb.AppendLine("I hereby declare that the information provided above is true and correct to");
        sb.AppendLine("the best of my knowledge and belief. I understand that providing false or misleading");
        sb.AppendLine("information to law enforcement entails legal consequences under Bangladeshi law.");
        sb.AppendLine();

        // ── Signature Block ─────────────────────────────────────
        sb.AppendLine($"Date: {DateTime.UtcNow:dd MMMM, yyyy}");
        sb.AppendLine("Applicant: ________________________");
        sb.AppendLine($"District: {districtName}");
        sb.AppendLine();

        // ── Disclaimer Stamp (Surface 3 of 3) ───────────────────
        sb.AppendLine(new string('═', 60));
        sb.AppendLine(Disclaimers.Legal);
        sb.AppendLine(Disclaimers.LegalBangla);
        sb.AppendLine(new string('═', 60));

        return Task.FromResult(sb.ToString());
    }
}
