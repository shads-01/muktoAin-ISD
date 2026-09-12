using System.Text;
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Constants;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;

namespace MuktoAin.Application.Documents.Templates;

/// <summary>
/// Consumer rights complaint template under the Consumer Rights Protection Act, 2009 (ভোক্তা অধিকার সংরক্ষণ আইন, ২০০৯).
/// Follows the format specified in Arpita_plan.md Step 3.3 —
/// structured complaint to the Directorate of National Consumer Rights Protection (DNCRP) or District Committee.
/// </summary>
public class ConsumerComplaintTemplate : IDocumentTemplate
{
    public DocumentType DocumentType => DocumentType.ConsumerComplaint;

    public Task<string> RenderAsync(Case caseEntity, RightsExplanationDto explanation)
    {
        var districtName = caseEntity.District?.Name ?? "________";
        var sb = new StringBuilder();

        // ── Header ──────────────────────────────────────────────
        sb.AppendLine("TO");
        sb.AppendLine("The Director General / Designated Officer");
        sb.AppendLine("Directorate of National Consumer Rights Protection (DNCRP)");
        sb.AppendLine($"District Office: {districtName}, Bangladesh");
        sb.AppendLine();

        // ── Subject ─────────────────────────────────────────────
        var primarySection = explanation.CitedSections.FirstOrDefault();
        var sectionRef = primarySection != null
            ? $" Under Section {primarySection.SectionNumber} of"
            : string.Empty;
        sb.AppendLine($"Subject: Complaint{sectionRef} the Consumer Rights Protection Act, 2009");
        sb.AppendLine();

        // ── Salutation ──────────────────────────────────────────
        sb.AppendLine("Respected Sir/Madam,");
        sb.AppendLine();

        // ── Complainant Introduction ────────────────────────────
        sb.AppendLine($"I, the undersigned consumer, resident of {districtName}, do hereby submit this formal complaint " +
                       "against the concerned enterprise/merchant/service provider for anti-consumer practice(s) and statutory " +
                       "violation(s) under the Consumer Rights Protection Act, 2009 (Act No. 26 of 2009):");
        sb.AppendLine();

        // ── Facts of the Complaint ──────────────────────────────
        sb.AppendLine("FACTS OF THE COMPLAINT:");
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
            sb.AppendLine("• Consumer Rights Protection Act, 2009 (Relevant anti-consumer practice provisions)");
            sb.AppendLine();
        }

        // ── Rights Explanation ──────────────────────────────────
        if (!string.IsNullOrWhiteSpace(explanation.Explanation))
        {
            sb.AppendLine("YOUR RIGHTS UNDER CONSUMER LAW:");
            sb.AppendLine(new string('─', 40));
            sb.AppendLine(explanation.Explanation);
            sb.AppendLine();
        }

        // ── Relief / Remedy Sought ──────────────────────────────
        sb.AppendLine("RELIEF / REMEDY SOUGHT:");
        sb.AppendLine(new string('─', 40));
        sb.AppendLine("Based on the aforementioned facts and applicable statutory provisions, the complainant respectfully prays:");
        sb.AppendLine("1. That an immediate inquiry and hearing be conducted against the respondent enterprise;");
        sb.AppendLine("2. That appropriate replacement, full financial refund, or statutory compensation be awarded;");
        sb.AppendLine("3. That administrative fines be imposed under the Act and 25% of any realized penalty be disbursed to the complainant as per Section 76(4).");
        sb.AppendLine();

        // ── Supporting Evidence / Attachments ───────────────────
        sb.AppendLine("SUPPORTING EVIDENCE / ATTACHMENTS:");
        sb.AppendLine(new string('─', 40));
        sb.AppendLine("• Purchase receipt / money receipt / cash memo / order confirmation");
        sb.AppendLine("• Product photograph, packaging, batch number, or warranty documents (if applicable)");
        sb.AppendLine("• Communication records / complaint logs with the respondent");
        sb.AppendLine();

        // ── Declaration ─────────────────────────────────────────
        sb.AppendLine("DECLARATION:");
        sb.AppendLine(new string('─', 40));
        sb.AppendLine("I hereby declare that the particulars furnished above are true and correct to the best of my knowledge,");
        sb.AppendLine("information, and belief, and that I have not concealed any material fact in this complaint.");
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
