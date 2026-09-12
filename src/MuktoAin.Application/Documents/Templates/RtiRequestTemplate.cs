using System.Text;
using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Constants;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;

namespace MuktoAin.Application.Documents.Templates;

/// <summary>
/// Right to Information (RTI) application template under Section 8 of the Right to Information Act, 2009.
/// Follows the format specified in Arpita_plan.md Step 3.2 —
/// structured application to designated public authorities in Bangladesh.
/// </summary>
public class RtiRequestTemplate : IDocumentTemplate
{
    public DocumentType DocumentType => DocumentType.RtiRequest;

    public Task<string> RenderAsync(Case caseEntity, RightsExplanationDto explanation)
    {
        var districtName = caseEntity.District?.Name ?? "________";
        var sb = new StringBuilder();

        // ── Header ──────────────────────────────────────────────
        sb.AppendLine("TO");
        sb.AppendLine("The Designated Officer / RTI Officer");
        sb.AppendLine("[Public Authority / Government Department Name]");
        sb.AppendLine($"{districtName}, Bangladesh");
        sb.AppendLine();

        // ── Subject ─────────────────────────────────────────────
        sb.AppendLine("Subject: Application for Information Under Section 8 of the Right to Information Act, 2009");
        sb.AppendLine();

        // ── Salutation ──────────────────────────────────────────
        sb.AppendLine("Sir/Madam,");
        sb.AppendLine();

        // ── Applicant Introduction ──────────────────────────────
        sb.AppendLine($"Under the provisions of Section 8 of the Right to Information Act, 2009 (Act No. XX of 2009), " +
                       $"I, the undersigned citizen of Bangladesh, resident of {districtName}, do hereby request the following " +
                       "official information and documents from your designated office:");
        sb.AppendLine();

        // ── Information Requested ───────────────────────────────
        sb.AppendLine("INFORMATION REQUESTED:");
        sb.AppendLine(new string('─', 40));
        sb.AppendLine(caseEntity.Description);
        sb.AppendLine();

        // ── Applicable Legal Provisions ─────────────────────────
        sb.AppendLine("APPLICABLE LEGAL PROVISIONS / JUSTIFICATION:");
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
            sb.AppendLine("• Right to Information Act, 2009, Section 8 (Procedure for obtaining information)");
            sb.AppendLine("• Right to Information Act, 2009, Section 9 (Time limit for supplying information)");
            sb.AppendLine();
        }

        // ── Statutory Rights & Timelines ────────────────────────
        if (!string.IsNullOrWhiteSpace(explanation.Explanation))
        {
            sb.AppendLine("STATUTORY RIGHTS & TIMELINES UNDER RTI ACT, 2009:");
            sb.AppendLine(new string('─', 40));
            sb.AppendLine(explanation.Explanation);
            sb.AppendLine();
        }

        // ── Preferred Format of Information ─────────────────────
        sb.AppendLine("PREFERRED FORMAT OF INFORMATION:");
        sb.AppendLine(new string('─', 40));
        sb.AppendLine("The requested information may kindly be supplied in printed/photocopied certified format");
        sb.AppendLine("or electronic format (e-mail/data storage) pursuant to Section 8(4) of the Act.");
        sb.AppendLine("I undertake to pay the requisite official reproduction fees prescribed under government rules.");
        sb.AppendLine();

        // ── Declaration ─────────────────────────────────────────
        sb.AppendLine("DECLARATION:");
        sb.AppendLine(new string('─', 40));
        sb.AppendLine("I hereby declare that I am a citizen of Bangladesh and this information request is submitted");
        sb.AppendLine("in good faith for legitimate civic transparency and legal rights preservation under Bangladeshi law.");
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
