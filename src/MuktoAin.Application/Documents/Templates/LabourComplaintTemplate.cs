using System.Text;
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Constants;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;

namespace MuktoAin.Application.Documents.Templates;

/// <summary>
/// Bangladesh District Labour Court complaint template.
/// Follows the format specified in Arpita_plan.md Step 2.3 —
/// structured complaint under the Bangladesh Labour Act, 2006.
/// </summary>
public class LabourComplaintTemplate : IDocumentTemplate
{
    public DocumentType DocumentType => DocumentType.LabourComplaint;

    public Task<string> RenderAsync(Case caseEntity, RightsExplanationDto explanation)
    {
        var districtName = caseEntity.District?.Name ?? "________";
        var sb = new StringBuilder();

        // ── Header ──────────────────────────────────────────────
        sb.AppendLine("TO");
        sb.AppendLine("The Inspector General / District Labour Court");
        sb.AppendLine($"{districtName}, Bangladesh");
        sb.AppendLine();

        // ── Subject ─────────────────────────────────────────────
        var primarySection = explanation.CitedSections.FirstOrDefault();
        var sectionRef = primarySection != null
            ? $"Section {primarySection.SectionNumber} of"
            : string.Empty;
        sb.AppendLine($"Subject: Complaint Under {sectionRef} the Bangladesh Labour Act, 2006");
        sb.AppendLine();

        // ── Salutation ──────────────────────────────────────────
        sb.AppendLine("Respected Sir/Madam,");
        sb.AppendLine();

        // ── Complainant Introduction ────────────────────────────
        sb.AppendLine($"I, the undersigned, resident of {districtName}, do hereby submit this complaint " +
                       "for the following violation(s) of the Bangladesh Labour Act, 2006:");
        sb.AppendLine();

        // ── Facts of the Case ───────────────────────────────────
        sb.AppendLine("FACTS OF THE CASE:");
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
            sb.AppendLine("  [No specific sections retrieved — consult a qualified advocate]");
            sb.AppendLine();
        }

        // ── Rights Explanation ──────────────────────────────────
        if (!string.IsNullOrWhiteSpace(explanation.Explanation))
        {
            sb.AppendLine("YOUR RIGHTS UNDER APPLICABLE LAW:");
            sb.AppendLine(new string('─', 40));
            sb.AppendLine(explanation.Explanation);
            sb.AppendLine();
        }

        // ── Relief Sought ───────────────────────────────────────
        sb.AppendLine("RELIEF SOUGHT:");
        sb.AppendLine(new string('─', 40));
        sb.AppendLine("Based on the above facts and the applicable legal provisions cited herein,");
        sb.AppendLine("the complainant respectfully prays for appropriate relief including but not");
        sb.AppendLine("limited to compensation, reinstatement, and/or any other remedy the");
        sb.AppendLine("Honourable Court deems fit and proper.");
        sb.AppendLine();

        // ── Declaration ─────────────────────────────────────────
        sb.AppendLine("DECLARATION:");
        sb.AppendLine(new string('─', 40));
        sb.AppendLine("I hereby declare that the information provided above is true and correct to");
        sb.AppendLine("the best of my knowledge and belief. I understand that any false statement");
        sb.AppendLine("may result in legal consequences.");
        sb.AppendLine();

        // ── Signature Block ─────────────────────────────────────
        sb.AppendLine($"Date: {DateTime.UtcNow:dd MMMM, yyyy}");
        sb.AppendLine("Complainant: ________________________");
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
