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
public class ConsumerComplaintTemplate : IDocumentTemplate, IBanglaDocumentVariant
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

    public async Task<string> RenderBanglaOnlyAsync(Case caseEntity, RightsExplanationDto explanation)
    {
        var districtName = caseEntity.District?.Name;
        var sb = new StringBuilder();

        sb.AppendLine("বরাবর");
        sb.AppendLine("মহাপরিচালক / দায়িত্বপ্রাপ্ত কর্মকর্তা");
        sb.AppendLine("জাতীয় ভোক্তা অধিকার সংরক্ষণ অধিদপ্তর (ডিএনসিআরপি)");
        sb.AppendLine($"জেলা কার্যালয়: {districtName ?? BanglaOnlyRender.Placeholder}, বাংলাদেশ");
        sb.AppendLine();

        var primarySection = explanation.CitedSections.FirstOrDefault();
        var sectionRef = primarySection != null
            ? $" ভোক্তা অধিকার সংরক্ষণ আইন, ২০০৯-এর ধারা {primarySection.SectionNumber}-এর অধীনে"
            : string.Empty;
        sb.AppendLine($"বিষয়: {sectionRef.TrimStart()} অভিযোগ");
        sb.AppendLine();

        sb.AppendLine("মহোদয়,");
        sb.AppendLine();

        sb.AppendLine($"আমি, স্বাক্ষরকারী ভোক্তা, {districtName ?? BanglaOnlyRender.Placeholder}-এর বাসিন্দা, " +
                       "সংশ্লিষ্ট প্রতিষ্ঠান/বিক্রেতা/সেবাদাতার বিরুদ্ধে ভোক্তা-বিরোধী কার্যকলাপ ও " +
                       "ভোক্তা অধিকার সংরক্ষণ আইন, ২০০৯ (২০০৯ সনের ২৬ নং আইন)-এর বিধিভঙ্গের বিষয়ে " +
                       "এই আনুষ্ঠানিক অভিযোগ জমা দিচ্ছি:");
        sb.AppendLine();

        sb.AppendLine("অভিযোগের ঘটনাবলি:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        sb.AppendLine(caseEntity.Description);
        sb.AppendLine();

        sb.AppendLine("প্রযোজ্য আইনি বিধান:");
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
            sb.AppendLine("• ভোক্তা অধিকার সংরক্ষণ আইন, ২০০৯ (প্রাসঙ্গিক ভোক্তা-বিরোধী কার্যকলাপ সংক্রান্ত বিধান)");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(explanation.Explanation))
        {
            sb.AppendLine("ভোক্তা আইনে আপনার অধিকার:");
            sb.AppendLine(BanglaOnlyRender.Rule);
            sb.AppendLine(explanation.Explanation);
            sb.AppendLine();
        }

        sb.AppendLine("প্রার্থিত প্রতিকার:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        sb.AppendLine("উপরোক্ত ঘটনা ও প্রযোজ্য আইনি বিধানের ভিত্তিতে অভিযোগকারী বিনীতভাবে প্রার্থনা করছেন:");
        sb.AppendLine("১. প্রতিবাদী প্রতিষ্ঠানের বিরুদ্ধে দ্রুত তদন্ত ও শুনানি আয়োজন করা হোক;");
        sb.AppendLine("২. যথাযথ প্রতিস্থাপন, পূর্ণ আর্থিক ফেরত বা আইনগত ক্ষতিপূরণ প্রদান করা হোক;");
        sb.AppendLine("৩. আইন অনুযায়ী জরিমানা আরোপ করা হোক এবং আদায়কৃত জরিমানার ২৫% ধারা ৭৬(৪) মোতাবেক অভিযোগকারীকে প্রদান করা হোক।");
        sb.AppendLine();

        sb.AppendLine("সংযুক্ত প্রমাণপত্র:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        sb.AppendLine("• ক্রয় রশিদ / মানি রশিদ / ক্যাশ মেমো / অর্ডার নিশ্চিতকরণ");
        sb.AppendLine("• পণ্যের ছবি, প্যাকেজিং, ব্যাচ নম্বর বা ওয়ারেন্টি নথি (প্রযোজ্য ক্ষেত্রে)");
        sb.AppendLine("• প্রতিবাদী সঙ্গে যোগাযোগের রেকর্ড / অভিযোগের স্মৃতিচিহ্ন");
        sb.AppendLine();

        sb.AppendLine("ঘোষণা:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        sb.AppendLine("আমি এই মর্মে ঘোষণা করছি যে, উপরোক্ত বিবরণ আমার জ্ঞান, তথ্য ও বিশ্বাসমতে সত্য ও সঠিক, " +
                       "এবং এই অভিযোগে আমি কোনো অপরিহার্য তথ্য গোপন করিনি।");
        sb.AppendLine();

        return BanglaOnlyRender.AppendClosing(sb, "অভিযোগকারী", districtName);
    }
}
