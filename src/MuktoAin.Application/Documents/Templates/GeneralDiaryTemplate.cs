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
public class GeneralDiaryTemplate : IDocumentTemplate, IBanglaDocumentVariant
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

    public async Task<string> RenderBanglaOnlyAsync(Case caseEntity, RightsExplanationDto explanation)
    {
        var districtName = caseEntity.District?.Name;
        var sb = new StringBuilder();

        sb.AppendLine("বরাবর");
        sb.AppendLine("ভারপ্রাপ্ত কর্মকর্তা (ওসি)");
        sb.AppendLine($"থানা, {districtName ?? BanglaOnlyRender.Placeholder}, বাংলাদেশ");
        sb.AppendLine();

        var primarySection = explanation.CitedSections.FirstOrDefault();
        var sectionRef = primarySection != null
            ? $" ({primarySection.ActTitle}, ধারা {primarySection.SectionNumber} সংশ্লিষ্ট)"
            : string.Empty;
        sb.AppendLine($"বিষয়: সাধারণ ডায়েরি (জিডি) ভুক্তির আবেদন{sectionRef}");
        sb.AppendLine();

        sb.AppendLine("মহোদয়,");
        sb.AppendLine();

        sb.AppendLine($"আমি, স্বাক্ষরকারী, {districtName ?? BanglaOnlyRender.Placeholder}-এর বাসিন্দা, " +
                       "নিম্নবর্ণিত ঘটনা/পরিস্থিতি সংক্রান্ত একটি সাধারণ ডায়েরি (জিডি) ভুক্তি রেকর্ডের জন্য " +
                       "এই আবেদনটি বিনীতভাবে জমা দিচ্ছি:");
        sb.AppendLine();

        sb.AppendLine("ঘটনার বিবরণ:");
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
            sb.AppendLine("  [নির্দিষ্ট কোনো ধারা পাওয়া যায়নি — ডিউটি কর্মকর্তা বা একজন যোগ্য আইনজীবীর পরামর্শ নিন]");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(explanation.Explanation))
        {
            sb.AppendLine("প্রযোজ্য আইনি অধিকার ও প্রতিকার:");
            sb.AppendLine(BanglaOnlyRender.Rule);
            sb.AppendLine(explanation.Explanation);
            sb.AppendLine();
        }

        sb.AppendLine("প্রার্থনা:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        sb.AppendLine("আমি সর্বান্তঃকরণে প্রার্থনা করছি যে, উপরোক্ত ঘটনা আপনার থানার সাধারণ ডায়েরি খাতায় যথাযথভাবে রেকর্ড করা হোক, " +
                       "প্রয়োজনীয় তদন্ত শুরু করা হোক এবং আইনগত সুরক্ষা ও নিরাপত্তার জন্য উপযুক্ত ব্যবস্থা গ্রহণ করা হোক।");
        sb.AppendLine();

        sb.AppendLine("ঘোষণা:");
        sb.AppendLine(BanglaOnlyRender.Rule);
        sb.AppendLine("আমি এই মর্মে ঘোষণা করছি যে, উপরোক্ত তথ্য আমার জ্ঞান ও বিশ্বাসমতে সত্য ও সঠিক। " +
                       "আইন-শৃঙ্খলা বাহিনীকে মিথ্যা বা বিভ্রান্তিকর তথ্য প্রদান বাংলাদেশি আইনে শাস্তিযোগ্য।");
        sb.AppendLine();

        return BanglaOnlyRender.AppendClosing(sb, "আবেদনকারী", districtName);
    }
}
