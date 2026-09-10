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
/// Headings match the case's language (LabourComplaintHeadings); ASCII-art
/// dividers were removed as noise outside a monospace render, and the
/// AI-authored rights explanation is run through AiTextSanitizer before
/// being embedded (2026-09-10 styling fix).
/// </summary>
public class LabourComplaintTemplate : IDocumentTemplate
{
    public DocumentType DocumentType => DocumentType.LabourComplaint;

    public Task<string> RenderAsync(Case caseEntity, RightsExplanationDto explanation)
    {
        var language = caseEntity.Language;
        var districtName = caseEntity.District?.Name ?? "________";
        var sb = new StringBuilder();

        var isEnglish = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase);

        // ── Header ──────────────────────────────────────────────
        sb.AppendLine(isEnglish ? "TO" : "বরাবর,");
        sb.AppendLine(isEnglish
            ? "The Inspector General / District Labour Court"
            : "কলকারখানা ও প্রতিষ্ঠান পরিদর্শন অধিদপ্তর / শ্রম আদালত");
        sb.AppendLine(isEnglish ? $"{districtName}, Bangladesh." : $"{districtName}, বাংলাদেশ।");
        sb.AppendLine();

        // ── Subject ─────────────────────────────────────────────
        var primarySection = explanation.CitedSections.FirstOrDefault();
        var sectionRef = primarySection != null
            ? $"Section {primarySection.SectionNumber} of"
            : string.Empty;
        sb.AppendLine($"Subject: Complaint Under {sectionRef} the Bangladesh Labour Act, 2006");
        sb.AppendLine();

        // ── Salutation ──────────────────────────────────────────
        sb.AppendLine(isEnglish ? "Respected Sir/Madam," : "মহোদয়,");
        sb.AppendLine();

        // ── Complainant Introduction ────────────────────────────
        sb.AppendLine($"I, the undersigned, resident of {districtName}, do hereby submit this complaint " +
                       "for the following violation(s) of the Bangladesh Labour Act, 2006:");
        sb.AppendLine();

        // ── Facts of the Case ───────────────────────────────────
        sb.AppendLine(LabourComplaintHeadings.For(LabourComplaintHeadings.FactsOfTheCase, language) + ":");
        sb.AppendLine(caseEntity.Description);
        sb.AppendLine();

        // ── Applicable Legal Provisions ─────────────────────────
        sb.AppendLine(LabourComplaintHeadings.For(LabourComplaintHeadings.ApplicableLegalProvisions, language) + ":");
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

        // ── Rights Explanation (AI-authored — sanitized) ────────
        var sanitizedExplanation = AiTextSanitizer.Sanitize(explanation.Explanation);
        if (!string.IsNullOrWhiteSpace(sanitizedExplanation))
        {
            sb.AppendLine(LabourComplaintHeadings.For(LabourComplaintHeadings.YourRights, language) + ":");
            sb.AppendLine(sanitizedExplanation);
            sb.AppendLine();
        }

        // ── Relief Sought ───────────────────────────────────────
        sb.AppendLine(LabourComplaintHeadings.For(LabourComplaintHeadings.ReliefSought, language) + ":");
        sb.AppendLine("Based on the above facts and the applicable legal provisions cited herein,");
        sb.AppendLine("the complainant respectfully prays for appropriate relief including but not");
        sb.AppendLine("limited to compensation, reinstatement, and/or any other remedy the");
        sb.AppendLine("Honourable Court deems fit and proper.");
        sb.AppendLine();

        // ── Declaration ─────────────────────────────────────────
        sb.AppendLine(LabourComplaintHeadings.For(LabourComplaintHeadings.Declaration, language) + ":");
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
        sb.AppendLine(Disclaimers.Legal);
        sb.AppendLine(Disclaimers.LegalBangla);

        return Task.FromResult(sb.ToString());
    }
}
