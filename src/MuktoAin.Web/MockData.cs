using Microsoft.AspNetCore.Mvc.Rendering;
using MuktoAin.Web.ViewModels;

namespace MuktoAin.Web;

public static class MockData
{
    public static List<SelectListItem> Categories => new()
    {
        new("শ্রম অভিযোগ · Labour Complaint", "1"),
        new("সাধারণ ডায়েরি · General Diary (GD)", "2"),
        new("তথ্য অধিকার · RTI Request", "3"),
        new("ভোক্তা অধিকার · Consumer Complaint", "4")
    };

    public static List<SelectListItem> Districts => new()
    {
        new("Dhaka (ঢাকা)", "1"),
        new("Chattogram (চট্টগ্রাম)", "2"),
        new("Rajshahi (রাজশাহী)", "3"),
        new("Khulna (খুলনা)", "4"),
        new("Sylhet (সিলেট)", "5"),
        new("Barishal (বরিশাল)", "6"),
        new("Rangpur (রংপুর)", "7"),
        new("Mymensingh (ময়মনসিংহ)", "8"),
        new("Gazipur (গাজীপুর)", "9"),
        new("Narayanganj (নারায়ণগঞ্জ)", "10"),
        new("Cumilla (কুমিল্লা)", "11"),
        new("Bogura (বগুড়া)", "12"),
        new("Cox's Bazar (কক্সবাজার)", "13"),
        new("Jessore (যশোর)", "14"),
        new("Pabna (পাবনা)", "15"),
        new("Tangail (টাঙ্গাইল)", "16")
    };

    public static CaseDetailViewModel SampleCase => new()
    {
        CaseId = 42,
        TrackingCode = "MKT-2026-0042",
        Title = "আমার বেতন ৩ মাস দেয়নি",
        Description = "আমি একটি গার্মেন্টস কারখানায় কাটিং বিভাগে কাজ করি। গত ৩ মাস ধরে আমার এবং আরও কয়েকজন সহকর্মীর বেতন বকেয়া রাখা হয়েছে। মালিকপক্ষ কোনো নির্দিষ্ট তারিখ দিচ্ছে না।",
        CategoryName = "শ্রম অভিযোগ (Labour Complaint)",
        DistrictName = "Dhaka",
        Status = "Submitted",
        CreatedAt = DateTime.UtcNow.AddDays(-2)
    };

    public static CaseResultViewModel SampleCaseResult => new()
    {
        CaseId = 42,
        Title = "আমার বেতন ৩ মাস দেয়নি",
        Status = "Submitted",
        CategoryName = "শ্রম অভিযোগ (Labour Complaint)",
        DistrictName = "Dhaka",
        CreatedAt = DateTime.UtcNow.AddDays(-2),
        RightsExplanation = "<p><strong>বাংলাদেশ শ্রম আইন, ২০০৬</strong>-এর ধারা ১২৩ অনুযায়ী, কোনো প্রতিষ্ঠানে মজুরি মেয়াদ শেষ হওয়ার পরবর্তী <strong>৭ কার্যদিবসের মধ্যে</strong> শ্রমিককে তার প্রাপ্য মজুরি পরিশোধ করা নিয়োগকর্তার জন্য আইনত বাধ্যতামূলক।</p><p>বিনা কারণে ৩ মাস মজুরি বকেয়া রাখা একটি শাস্তিযোগ্য অপরাধ (ধারা ২৮৯)। আপনি শ্রম আদালত বা কলকারখানা ও প্রতিষ্ঠান পরিদর্শন অধিদপ্তরে (DIFE) লিখিত অভিযোগ দাখিল করতে পারেন।</p>",
        CitedSections = new()
        {
            new()
            {
                ActTitle = "বাংলাদেশ শ্রম আইন, ২০০৬ (The Bangladesh Labour Act, 2006)",
                SectionNumber = "১২৩ (Section 123)",
                SectionText = "মজুরি পরিশোধের সময়সীমা: প্রত্যেক নিয়োগকারী তাহার প্রতিষ্ঠানে নিযুক্ত শ্রমিকের মজুরি পরিশোধের জন্য মজুরি মেয়াদ শেষ হইবার পরবর্তী অনধিক সাত কার্যদিবসের মধ্যে উহা পরিশোধ করিবেন।",
                RelevanceScore = "0.94"
            },
            new()
            {
                ActTitle = "বাংলাদেশ শ্রম আইন, ২০০৬ (The Bangladesh Labour Act, 2006)",
                SectionNumber = "২৮৯ (Section 289)",
                SectionText = "ধারা ১২৩ এর বিধান লঙ্ঘনের দণ্ড: যদি কোনো নিয়োগকারী ১২৩ ধারার কোনো বিধান লঙ্ঘন করেন, তবে তিনি অনধিক তিন মাস পর্যন্ত কারাদণ্ড অথবা দশ হাজার টাকা পর্যন্ত অর্থদণ্ড কিংবা উভয় দণ্ডে দণ্ডনীয় হইবেন।",
                RelevanceScore = "0.88"
            }
        },
        DocumentId = 101,
        DocumentStatus = "UnderReview",
        DocumentContent = @"বরাবর
মহাপরিদর্শক / উপ-মহাপরিদর্শক
কলকারখানা ও প্রতিষ্ঠান পরিদর্শন অধিদপ্তর (DIFE)
ঢাকা কার্যালয়, বাংলাদেশ।

বিষয়: বাংলাদেশ শ্রম আইন ২০০৬-এর ধারা ১২৩ অনুসারে বকেয়া মজুরি আদায়ের জন্য অভিযোগ।

মহোদয়,
বিনীত নিবেদন এই যে, আমি নিম্নস্বাক্ষরকারী মোঃ রফিকুল ইসলাম, ঢাকা জেলার আশুলিয়াস্থ একটি তৈরি পোশাক প্রস্তুতকারী প্রতিষ্ঠানে বিগত ২ বছর যাবৎ কাটিং সেকশনে অপারেটর হিসেবে কর্মরত আছি। আমার মাসিক মূল মজুরি ১২,৫০০/- টাকা।

বিগত ৩ (তিন) মাস যাবৎ অর্থাৎ নভেম্বর ২০২৫ হইতে জানুয়ারি ২০২৬ পর্যন্ত আমার ন্যায্য মজুরি পরিশোধ করা হয় নাই। উক্ত বিষয়ে কারখানা কর্তৃপক্ষের সাথে একাধিকবার মৌখিক ও লিখিত আবেদন করা সত্ত্বেও কোনো সদুত্তর পাওয়া যায়নি।

অতএব, মহোদয়ের নিকট বিনীত প্রার্থনা, বাংলাদেশ শ্রম আইন ২০০৬ এর বিধান মোতাবেক তদন্তপূর্বক আমার বকেয়া মজুরি আদায় এবং সংশ্লিষ্ট নিয়োগকর্তার বিরুদ্ধে আইনানুগ ব্যবস্থা গ্রহণে মর্জি হয়।

বিনীত,
মোঃ রফিকুল ইসলাম
মোবাইল: ০১৭১২-XXXXXX
তারিখ: ২৫ আগস্ট ২০২৬",
        CanDownloadPdf = false
    };

    public static List<CaseListItemViewModel> SampleCases => new()
    {
        new()
        {
            CaseId = 42,
            TrackingCode = "MKT-2026-0042",
            Title = "আমার বেতন ৩ মাস দেয়নি",
            CategoryName = "শ্রম অভিযোগ",
            Status = "UnderReview",
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        },
        new()
        {
            CaseId = 38,
            TrackingCode = "MKT-2026-0038",
            Title = "মোবাইল ফোন ও জাতীয় পরিচয়পত্র হারিয়ে যাওয়া সংক্রান্ত জিডি",
            CategoryName = "সাধারণ ডায়েরি (GD)",
            Status = "Finalized",
            CreatedAt = DateTime.UtcNow.AddDays(-7)
        },
        new()
        {
            CaseId = 29,
            TrackingCode = "MKT-2026-0029",
            Title = "পৌরসভার ড্রেনেজ প্রকল্পের বাজেট তথ্য চেয়ে আবেদন",
            CategoryName = "তথ্য অধিকার (RTI)",
            Status = "Finalized",
            CreatedAt = DateTime.UtcNow.AddDays(-14)
        },
        new()
        {
            CaseId = 15,
            TrackingCode = "MKT-2026-0015",
            Title = "অনলাইন শপ থেকে ত্রুটিপূর্ণ পণ্য সরবরাহ ও রিফান্ড না দেওয়া",
            CategoryName = "ভোক্তা অধিকার",
            Status = "Submitted",
            CreatedAt = DateTime.UtcNow.AddHours(-18)
        }
    };

    public static SearchViewModel SampleSearchResults(string query, int page)
    {
        var vm = new SearchViewModel
        {
            Query = query,
            Page = page,
            PageSize = 5,
            TotalResults = 18,
            Results = new()
            {
                new()
                {
                    SectionId = 123,
                    ActTitle = "The Bangladesh Labour Act, 2006 (২০০৬ সনের ৪২ নং আইন)",
                    SectionNumber = "ধারা ১২৩ (Section 123)",
                    SectionTitle = "মজুরি পরিশোধের সময়সীমা",
                    SectionTextSnippet = "প্রত্যেক নিয়োগকারী তাহার প্রতিষ্ঠানে নিযুক্ত শ্রমিকের মজুরি পরিশোধের জন্য মজুরি মেয়াদ শেষ হইবার পরবর্তী অনধিক সাত কার্যদিবসের মধ্যে উহা পরিশোধ করিবেন..."
                },
                new()
                {
                    SectionId = 124,
                    ActTitle = "The Bangladesh Labour Act, 2006 (২০০৬ সনের ৪২ নং আইন)",
                    SectionNumber = "ধারা ১২৪ (Section 124)",
                    SectionTitle = "মজুরি হইতে কর্তন",
                    SectionTextSnippet = "শ্রমিকের মজুরি হইতে এই আইনের বিধান অনুযায়ী অনুমোদিত কর্তন ব্যতীত অন্য কোনো প্রকার কর্তন করা যাইবে না..."
                },
                new()
                {
                    SectionId = 289,
                    ActTitle = "The Bangladesh Labour Act, 2006 (২০০৬ সনের ৪২ নং আইন)",
                    SectionNumber = "ধারা ২৮৯ (Section 289)",
                    SectionTitle = "ধারা ১২৩ এর বিধান লঙ্ঘনের দণ্ড",
                    SectionTextSnippet = "যদি কোনো নিয়োগকারী ১২৩ ধারার কোনো বিধান লঙ্ঘন করেন, তবে তিনি অনধিক তিন মাস পর্যন্ত কারাদণ্ড অথবা দশ হাজার টাকা অর্থদণ্ডে দণ্ডনীয় হইবেন..."
                }
            }
        };
        return vm;
    }

    public static List<CategoryViewModel> CategoriesDetailed => new()
    {
        new()
        {
            CategoryId = 1,
            NameBn = "শ্রম অধিকার ও অভিযোগ",
            NameEn = "Labour Complaint",
            DescriptionBn = "বকেয়া মজুরি, অন্যায় বরখাস্ত, ওভারটাইম বকেয়া, মাতৃত্বকালীন সুবিধা ও কর্মক্ষেত্রের ক্ষতিপূরণ সংক্রান্ত আইনি সুরক্ষা।",
            DescriptionEn = "Unpaid wages, unlawful termination, overtime disputes, maternity benefits, and compensation.",
            Icon = "briefcase"
        },
        new()
        {
            CategoryId = 2,
            NameBn = "সাধারণ ডায়েরি (GD)",
            NameEn = "General Diary",
            DescriptionBn = "জাতীয় পরিচয়পত্র, পাসপোর্ট বা গুরুত্বপূর্ণ সার্টিফিকেট হারানো, পারিবারিক ও ব্যক্তিগত নিরাপত্তা হুমকি ও থানায় জিডি।",
            DescriptionEn = "Lost identification documents, safety threats, harassment, and police diary lodging.",
            Icon = "shield"
        },
        new()
        {
            CategoryId = 3,
            NameBn = "তথ্য অধিকার আবেদন (RTI)",
            NameEn = "Right to Information",
            DescriptionBn = "সরকারি, আধা-সরকারি বা সংবিধিবদ্ধ সংস্থার উন্নয়ন বাজেট, প্রশাসনিক সিদ্ধান্ত ও জনস্বার্থমূলক তথ্য চেয়ে আবেদন।",
            DescriptionEn = "Official RTI Form-A applications for government and statutory body public records.",
            Icon = "file-text"
        },
        new()
        {
            CategoryId = 4,
            NameBn = "ভোক্তা অধিকার সংরক্ষণ",
            NameEn = "Consumer Protection",
            DescriptionBn = "মেয়াদোত্তীর্ণ বা নকল পণ্য, ওজনে কম, নির্ধারিত মূল্যের চেয়ে অতিরিক্ত দাম দাবি ও সেবায় প্রতারণার প্রতিকার।",
            DescriptionEn = "Counterfeit products, defective merchandise, price gouging, and consumer arbitration.",
            Icon = "shopping-bag"
        }
    };

    public static LawyerReviewViewModel SampleLawyerReview => new()
    {
        DocumentId = 101,
        CaseId = 42,
        CaseTitle = "আমার বেতন ৩ মাস দেয়নি",
        CategoryName = "শ্রম অভিযোগ (Labour Complaint)",
        ContentDraft = @"বরাবর
মহাপরিদর্শক / উপ-মহাপরিদর্শক
কলকারখানা ও প্রতিষ্ঠান পরিদর্শন অধিদপ্তর (DIFE)
ঢাকা কার্যালয়, বাংলাদেশ।

বিষয়: বাংলাদেশ শ্রম আইন ২০০৬-এর ধারা ১২৩ অনুসারে বকেয়া মজুরি আদায়ের জন্য অভিযোগ।

মহোদয়,
বিনীত নিবেদন এই যে, আমি নিম্নস্বাক্ষরকারী মোঃ রফিকুল ইসলাম, ঢাকা জেলার আশুলিয়াস্থ একটি তৈরি পোশাক প্রস্তুতকারী প্রতিষ্ঠানে বিগত ২ বছর যাবৎ কাটিং সেকশনে অপারেটর হিসেবে কর্মরত আছি। আমার মাসিক মূল মজুরি ১২,৫০০/- টাকা।

বিগত ৩ (তিন) মাস যাবৎ অর্থাৎ নভেম্বর ২০২৫ হইতে জানুয়ারি ২০২৬ পর্যন্ত আমার ন্যায্য মজুরি পরিশোধ করা হয় নাই। উক্ত বিষয়ে কারখানা কর্তৃপক্ষের সাথে একাধিকবার মৌখিক ও লিখিত আবেদন করা সত্ত্বেও কোনো সদুত্তর পাওয়া যায়নি।

অতএব, মহোদয়ের নিকট বিনীত প্রার্থনা, বাংলাদেশ শ্রম আইন ২০০৬ এর বিধান মোতাবেক তদন্তপূর্বক আমার বকেয়া মজুরি আদায় এবং সংশ্লিষ্ট নিয়োগকর্তার বিরুদ্ধে আইনানুগ ব্যবস্থা গ্রহণে মর্জি হয়।

বিনীত,
মোঃ রফিকুল ইসলাম
মোবাইল: ০১৭১২-XXXXXX
তারিখ: ২৫ আগস্ট ২০২৬",
        EditedContent = @"বরাবর
মহাপরিদর্শক / উপ-মহাপরিদর্শক
কলকারখানা ও প্রতিষ্ঠান পরিদর্শন অধিদপ্তর (DIFE)
ঢাকা কার্যালয়, বাংলাদেশ।

বিষয়: বাংলাদেশ শ্রম আইন ২০০৬-এর ধারা ১২৩ ও ৩৩ অনুসারে বকেয়া মজুরি পরিশোধ ও আইনানুগ ক্ষতিপূরণ আদায়ের অভিযোগ।

মহোদয়,
বিনীত নিবেদন এই যে, আমি নিম্নস্বাক্ষরকারী মোঃ রফিকুল ইসলাম, পিতা: আব্দুল জব্বার, ঢাকা জেলার আশুলিয়াস্থ একটি তৈরি পোশাক প্রস্তুতকারী প্রতিষ্ঠানে বিগত ২ বছর যাবৎ কাটিং সেকশনে অপারেটর হিসেবে কর্মরত আছি। আমার মাসিক মূল মজুরি ১২,৫০০/- (বারো হাজার পাঁচশত) টাকা।

বিগত ৩ (তিন) মাস যাবৎ অর্থাৎ নভেম্বর ২০২৫ হইতে জানুয়ারি ২০২৬ পর্যন্ত আমার বৈধ প্রাপ্য মজুরি (সর্বমোট ৩৭,৫০০/- টাকা) পরিশোধ করা হয় নাই। উক্ত বিষয়ে কারখানা কর্তৃপক্ষের সাথে একাধিকবার লিখিত দাবি জানানো সত্ত্বেও কোনো সমাধান প্রদান করা হয়নি।

অতএব, মহোদয়ের নিকট বিনীত প্রার্থনা, বাংলাদেশ শ্রম আইন ২০০৬ এর ধারা ১২৩ ও ২৮৯ এর বিধান মোতাবেক অবিলম্বে তদন্তপূর্বক আমার বকেয়া মজুরি আদায় এবং সংশ্লিষ্ট নিয়োগকর্তার বিরুদ্ধে উপযুক্ত আইনানুগ ব্যবস্থা গ্রহণে মর্জি হয়।

বিনীত,
মোঃ রফিকুল ইসলাম
মোবাইল: ০১৭১২-XXXXXX
তারিখ: ২৫ আগস্ট ২০২৬",
        Decision = "Approved",
        Comments = "আইনের ধারা ১২৩ ও ২৮৯ যথাযথভাবে উল্লেখ করা হয়েছে। আবেদনপত্রটি সম্পূর্ণ এবং গ্রহণযোগ্য।"
    };

    public static AdminDashboardViewModel SampleAnalytics => new()
    {
        TotalCases = 1284,
        CasesThisWeek = 86,
        PendingReviews = 23,
        VerificationsWaiting = 5,
        AiCallsToday = 342,
        AiFailureRate = 2.1,
        CategoryStats = new()
        {
            new() { Name = "শ্রম · Labour", Percentage = 38, ColorClass = "primary" },
            new() { Name = "ডায়েরি · GD", Percentage = 27, ColorClass = "gold" },
            new() { Name = "তথ্য · RTI", Percentage = 20, ColorClass = "info" },
            new() { Name = "ভোক্তা · Consumer", Percentage = 15, ColorClass = "green" }
        },
        DistrictStats = new()
        {
            new() { Name = "Dhaka", Count = 412, Percentage = 100 },
            new() { Name = "Chattogram", Count = 187, Percentage = 45 },
            new() { Name = "Khulna", Count = 121, Percentage = 29 },
            new() { Name = "Rajshahi", Count = 98, Percentage = 24 },
            new() { Name = "Sylhet", Count = 74, Percentage = 18 }
        },
        VerificationQueue = new()
        {
            new() { ApplicationId = 1, ApplicantName = "Adv. Nusrat Jahan", BarRegNo = "DHA-1187", AppliedDate = "Aug 21", Status = "Pending" },
            new() { ApplicationId = 2, ApplicantName = "Adv. Shafiqul Islam", BarRegNo = "CTT-0564", AppliedDate = "Aug 22", Status = "Pending" },
            new() { ApplicationId = 3, ApplicantName = "Adv. Mahmuda Akter", BarRegNo = "RAJ-0231", AppliedDate = "Aug 23", Status = "Pending" },
            new() { ApplicationId = 4, ApplicantName = "Adv. Tanvir Ahmed", BarRegNo = "DHA-1298", AppliedDate = "Aug 24", Status = "Pending" },
            new() { ApplicationId = 5, ApplicantName = "Adv. Priya Das", BarRegNo = "SYL-0190", AppliedDate = "Aug 25", Status = "Pending" }
        }
    };
}
