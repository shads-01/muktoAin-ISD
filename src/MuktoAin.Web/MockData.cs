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

    public static List<AdminUserViewModel> SampleUsers => new()
    {
        new()
        {
            UserId = 2,
            FullName = "শাদস (Shads)",
            Email = "admin@muktoain.bd",
            Role = "Admin",
            AccountStatus = "Active",
            CreatedAt = new DateTime(2026, 7, 3)
        },
        new()
        {
            UserId = 11,
            FullName = "মোঃ রফিকুল ইসলাম (Md. Rafiqul Islam)",
            Email = "rafiqul@example.com",
            Role = "Citizen",
            AccountStatus = "Active",
            CreatedAt = new DateTime(2026, 7, 19)
        },
        new()
        {
            UserId = 14,
            FullName = "আরিফা আক্তার (Arifa Akter)",
            Email = "arifa@example.com",
            Role = "Citizen",
            AccountStatus = "Active",
            CreatedAt = new DateTime(2026, 8, 2)
        },
        new()
        {
            UserId = 23,
            FullName = "অ্যাডভোকেট নুসরাত জাহান (Adv. Nusrat Jahan)",
            Email = "nusrat.law@example.com",
            Role = "Lawyer",
            AccountStatus = "Active",
            CreatedAt = new DateTime(2026, 7, 25)
        },
        new()
        {
            UserId = 31,
            FullName = "অ্যাডভোকেট তানভীর আহমেদ (Adv. Tanvir Ahmed)",
            Email = "tanvir.law@example.com",
            Role = "Lawyer",
            AccountStatus = "Suspended",
            CreatedAt = new DateTime(2026, 6, 30)
        },
        new()
        {
            UserId = 45,
            FullName = "কবিতা রানী দাস (Kabita Rani Das)",
            Email = "kabita.das@example.com",
            Role = "Citizen",
            AccountStatus = "Suspended",
            CreatedAt = new DateTime(2026, 5, 18)
        },
        new()
        {
            UserId = 58,
            FullName = "রাকিব হাসান (Rakib Hasan)",
            Email = "rakib.h@example.com",
            Role = "Citizen",
            AccountStatus = "Active",
            CreatedAt = new DateTime(2026, 8, 20)
        },
        new()
        {
            UserId = 61,
            FullName = "অ্যাডভোকেট মাহমুদা আক্তার (Adv. Mahmuda Akter)",
            Email = "mahmuda.law@example.com",
            Role = "Lawyer",
            AccountStatus = "Active",
            CreatedAt = new DateTime(2026, 7, 12)
        }
    };

    public static List<AdminLawyerViewModel> SampleLawyers => new()
    {
        new()
        {
            LawyerProfileId = 1,
            UserId = 14,
            ApplicantName = "অ্যাডভোকেট নুসরাত জাহান (Adv. Nusrat Jahan)",
            BarRegistrationNumber = "DHA-1187",
            VerificationStatus = "Approved",
            Specialization = "শ্রম আইন (Labour Law)",
            AppliedAt = new DateTime(2026, 7, 25)
        },
        new()
        {
            LawyerProfileId = 2,
            UserId = 19,
            ApplicantName = "অ্যাডভোকেট শফিকুল ইসলাম (Adv. Shafiqul Islam)",
            BarRegistrationNumber = "CTT-0564",
            VerificationStatus = "Pending",
            Specialization = "ভোক্তা অধিকার (Consumer Law)",
            AppliedAt = new DateTime(2026, 8, 22)
        },
        new()
        {
            LawyerProfileId = 3,
            UserId = 28,
            ApplicantName = "অ্যাডভোকেট মাহমুদা আক্তার (Adv. Mahmuda Akter)",
            BarRegistrationNumber = "RAJ-0231",
            VerificationStatus = "Pending",
            Specialization = "পারিবারিক আইন (Family Law)",
            AppliedAt = new DateTime(2026, 8, 23)
        },
        new()
        {
            LawyerProfileId = 4,
            UserId = 31,
            ApplicantName = "অ্যাডভোকেট তানভীর আহমেদ (Adv. Tanvir Ahmed)",
            BarRegistrationNumber = "DHA-1298",
            VerificationStatus = "Rejected",
            Specialization = "দেওয়ানি মামলা (Civil Litigation)",
            AppliedAt = new DateTime(2026, 8, 10)
        },
        new()
        {
            LawyerProfileId = 5,
            UserId = 40,
            ApplicantName = "অ্যাডভোকেট প্রিয়া দাস (Adv. Priya Das)",
            BarRegistrationNumber = "SYL-0190",
            VerificationStatus = "Pending",
            Specialization = "তথ্য অধিকার (RTI)",
            AppliedAt = new DateTime(2026, 8, 25)
        }
    };

    public static List<AdminActViewModel> SampleActs => new()
    {
        new()
        {
            ActId = 1,
            Title = "বাংলাদেশ শ্রম আইন, ২০০৬ (The Bangladesh Labour Act, 2006)",
            Year = 2006,
            SectionCount = 353,
            ImportedAt = new DateTime(2026, 8, 18),
            IsVectorIndexed = true
        },
        new()
        {
            ActId = 2,
            Title = "তথ্য অধিকার আইন, ২০০৯ (The Right to Information Act, 2009)",
            Year = 2009,
            SectionCount = 40,
            ImportedAt = new DateTime(2026, 8, 18),
            IsVectorIndexed = true
        },
        new()
        {
            ActId = 3,
            Title = "ভোক্তা অধিকার সংরক্ষণ আইন, ২০০৯ (The Consumer Rights Protection Act, 2009)",
            Year = 2009,
            SectionCount = 58,
            ImportedAt = new DateTime(2026, 8, 19),
            IsVectorIndexed = true
        },
        new()
        {
            ActId = 4,
            Title = "দণ্ডবিধি, ১৮৬০ (The Penal Code, 1860)",
            Year = 1860,
            SectionCount = 511,
            ImportedAt = new DateTime(2026, 8, 20),
            IsVectorIndexed = false
        },
        new()
        {
            ActId = 5,
            Title = "নারী ও শিশু নির্যাতন দমন আইন, ২০০০ (The Prevention of Oppression Against Women and Children Act, 2000)",
            Year = 2000,
            SectionCount = 60,
            ImportedAt = new DateTime(2026, 8, 21),
            IsVectorIndexed = false
        }
    };

    /// <summary>
    /// Mock section dropdown for ScenarioMapping add-form. When Tultul wires
    /// ScenarioMappingService, this becomes IActSectionRepository.GetAllAsync().
    /// </summary>
    public static List<SelectListItem> SampleScenarioSections => new()
    {
        new("বাংলাদেশ শ্রম আইন, ২০০৬ — ধারা ১২৩ (Section 123) · মজুরি পরিশোধের সময়সীমা", "123"),
        new("বাংলাদেশ শ্রম আইন, ২০০৬ — ধারা ২৮৯ (Section 289) · ধারা ১২৩ লঙ্ঘনের দণ্ড", "289"),
        new("তথ্য অধিকার আইন, ২০০৯ — ধারা ৪ (Section 4) · তথ্য প্রদানের বাধ্যবাধকতা", "44"),
        new("ভোক্তা অধিকার সংরক্ষণ আইন, ২০০৯ — ধারা ৪৫ (Section 45) · ভোক্তা ক্ষতিপূরণ", "301"),
        new("দণ্ডবিধি, ১৮৬০ — ধারা ৪২০ (Section 420) · প্রতারণা", "489")
    };

    public static List<AdminScenarioMappingViewModel> SampleMappings => new()
    {
        new()
        {
            MappingId = 1,
            SectionId = 123,
            ScenarioKeyword = "বেতন না দেওয়া (unpaid wage)",
            ActTitle = "বাংলাদেশ শ্রম আইন, ২০০৬",
            SectionNumber = "ধারা ১২৩ (Section 123)",
            Notes = "বকেয়া মজুরি ও বেতন সংক্রান্ত অভিযোগে প্রাথমিক বুস্ট।"
        },
        new()
        {
            MappingId = 2,
            SectionId = 289,
            ScenarioKeyword = "শ্রমিক হয়রানি (worker harassment)",
            ActTitle = "বাংলাদেশ শ্রম আইন, ২০০৬",
            SectionNumber = "ধারা ২৮৯ (Section 289)",
            Notes = "মজুরি বকেয়ার শাস্তি সংক্রান্ত ধারা — ডিফল্ট সেকেন্ডারি সাইটেশন।"
        },
        new()
        {
            MappingId = 3,
            SectionId = 44,
            ScenarioKeyword = "তথ্য চাওয়া (RTI request)",
            ActTitle = "তথ্য অধিকার আইন, ২০০৯",
            SectionNumber = "ধারা ৪ (Section 4)",
            Notes = "সরকারি তথ্য প্রাপ্তির মূল ধারা, RTI আবেদনে পিন করা।"
        },
        new()
        {
            MappingId = 4,
            SectionId = 301,
            ScenarioKeyword = "ভোক্তা প্রতারণা (consumer fraud)",
            ActTitle = "ভোক্তা অধিকার সংরক্ষণ আইন, ২০০৯",
            SectionNumber = "ধারা ৪৫ (Section 45)",
            Notes = "মেয়াদোত্তীর্ণ/নকল পণ্য সংক্রান্ত ভোক্তা অভিযোগের বুস্ট।"
        }
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
        },
        AuditLogs = new()
        {
            new() { Timestamp = "10 mins ago", Action = "Advocate Bar Verification Approved", Actor = "Admin (Shads)", Status = "Success", Details = "Adv. Kazi Farhan (DHA-4412) verified & review permissions granted." },
            new() { Timestamp = "32 mins ago", Action = "Corpus Re-Index Triggered", Actor = "System Worker", Status = "Info", Details = "SHA256 checksum matched across all 1,484 Acts in Qdrant." },
            new() { Timestamp = "1 hour ago", Action = "Suspicious Intake Submission Blocked", Actor = "Moderation Filter", Status = "Warning", Details = "Toxic keyword pattern intercepted on anonymous case submission." },
            new() { Timestamp = "3 hours ago", Action = "New Citizen Account Registered", Actor = "Citizen Intake", Status = "Success", Details = "User ID #418 registered with 2FA enabled." },
            new() { Timestamp = "5 hours ago", Action = "AI Fallback Invocation", Actor = "Polly Retry Policy", Status = "Info", Details = "Transient 503 from Gemini Studio resolved on second exponential retry." }
        }
    };
}
