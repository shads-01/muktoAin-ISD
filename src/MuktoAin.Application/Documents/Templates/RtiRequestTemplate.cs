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
public class RtiRequestTemplate : IDocumentTemplate, IBanglaDocumentVariant
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

    public async Task<string> RenderBanglaOnlyAsync(Case caseEntity, RightsExplanationDto explanation)
    {
        var districtName = caseEntity.District?.Name;
        var sb = new StringBuilder();

        sb.AppendLine("বরাবর");
        sb.AppendLine("দায়িত্বপ্রাপ্ত কর্মকর্তা / তথ্য কর্মকর্তা");
        sb.AppendLine("[সরকারি সংস্থা / দপ্তরের নাম]");
        sb.AppendLine($"{districtName ?? BanglaOnlyRender.Placeholder}, বাংলাদেশ");
        sb.AppendLine();

        sb.AppendLine("বিষয়: তথ্য অধিকার আইন, ২০০৯-এর ধারা ৮-এর অধীনে তথ্য প্রাপ্তির আবেদন");
        sb.AppendLine();

        sb.AppendLine("মহোদয়,");
        sb.AppendLine();

        sb.AppendLine($"তথ্য অধিকার আইন, ২০০৯-এর ধারা ৮-এর বিধান অনুসারে, আমি, স্বাক্ষরকারী বাংলাদেশি নাগরিক, " +
                       $"{districtName ?? BanglaOnlyRender.Placeholder}-এর বাসিন্দা, আপনার দায়িত্বপ্রাপ্ত দপ্তর হতে " +
                       "নিম্নলিখিত সরকারি তথ্য ও নথি প্রার্থনা করছি:");
        sb.AppendLine();

        sb.AppendLine("প্রার্থিত তথ্য:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        sb.AppendLine(caseEntity.Description);
        sb.AppendLine();

        sb.AppendLine("প্রযোজ্য আইনি বিধান / যৌক্তিকতা:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        if (explanation.CitedSections.Count > 0)
        {
            foreach (var section in explanation.CitedSections)
            {
                sb.AppendLine($"• {section.ActTitle}, ধারা {section.SectionNumber}:");
                sb.AppendLine($"  {section.SectionText}");
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("• তথ্য অধিকার আইন, ২০০৯, ধারা ৮ (তথ্য প্রাপ্তির পদ্ধতি)");
            sb.AppendLine("• তথ্য অধিকার আইন, ২০০৯, ধারা ৯ (তথ্য সরবরাহের সময়সীমা)");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(explanation.Explanation))
        {
            sb.AppendLine("আইনগত অধিকার ও সময়সীমা (তথ্য অধিকার আইন, ২০০৯):");
            sb.AppendLine(BanglaOnlyRender.Rule);
            sb.AppendLine(explanation.Explanation);
            sb.AppendLine();
        }

        sb.AppendLine(" তথ্যের পছন্দসই রূপ:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        sb.AppendLine("আইনের ধারা ৮(৪) অনুসারে প্রার্থিত তথ্য মুদ্রিত/ফটোকপি করা প্রত্যয়িত রূপে " +
                       "বা ইলেকট্রনিক রূপে (ই-মেইল/ডেটা সংরক্ষণ) প্রদান করা হোক।");
        sb.AppendLine("সরকারি বিধি অনুযায়ী নির্ধারিত প্রাতিষ্ঠানিক অনুলিপি ফি পরিশোধ করতে আমি প্রতিশ্রুতিবদ্ধ।");
        sb.AppendLine();

        sb.AppendLine("ঘোষণা:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        sb.AppendLine("আমি এই মর্মে ঘোষণা করছি যে, আমি বাংলাদেশের নাগরিক এবং এই তথ্য আবেদনটি " +
                       "বাংলাদেশি আইনের অধীনে বৈধ নাগরিক স্বচ্ছতা ও আইনি অধিকার সংরক্ষণের সুশৃঙ্খল উদ্দেশ্যে জমা দেওয়া হয়েছে।");
        sb.AppendLine();

        return BanglaOnlyRender.AppendClosing(sb, "আবেদনকারী", districtName);
    }
}
