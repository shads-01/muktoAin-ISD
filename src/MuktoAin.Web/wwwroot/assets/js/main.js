/* ============================================================
   MuktoAin — shared UI behavior (v2, Parchment Sepia)
   Theme · language · drawer · popovers · tabs · modals · toasts
   No frameworks. Progressive enhancement only.
   ============================================================ */
(function () {
  "use strict";

  /* ---------- Theme (light / night library) ---------- */
  var savedTheme = null;
  try { savedTheme = localStorage.getItem("mkt-theme"); } catch (e) {}
  var theme = savedTheme || (matchMedia("(prefers-color-scheme:dark)").matches ? "dark" : "light");
  document.documentElement.dataset.theme = theme;

  function setTheme(t) {
    document.documentElement.dataset.theme = t;
    try { localStorage.setItem("mkt-theme", t); } catch (e) {}
    document.querySelectorAll(".theme-toggle").forEach(function (b) {
      b.innerHTML = icon(t === "dark" ? "sun" : "moon");
    });
    renderIcons();
  }
  function icon(name) {
    return '<i data-lucide="' + name + '"></i>';
  }

  /* Converts a string of Latin digits to Bengali digits (display only --
     mirrors the server-side ToBn() helper used for pagination labels etc). */
  function toBengaliDigits(s) {
    return String(s).replace(/[0-9]/g, function (d) {
      return String.fromCharCode(d.charCodeAt(0) - 48 + "০".charCodeAt(0));
    });
  }

  /* ---------- Lucide render helper ---------- */
  function renderIcons(root) {
    if (window.lucide) window.lucide.createIcons(root ? { nameAttr: "data-lucide", attrs: {}, root: root } : undefined);
  }

  /* ---------- Comprehensive Localization Engine (i18n) ---------- */
  var savedLang = null;
  try { savedLang = localStorage.getItem("mkt-lang"); } catch (e) {}
  var currentLang = savedLang || "bn";
  document.documentElement.lang = currentLang;

  var translations = {
    bn: {
      "skip-link": "সরাসরি কন্টেন্টে যান / Skip to content",
      "nav-legal-aid": "আইনি সেবা",
      "nav-submit": "সমস্যা জমা দিন",
      "nav-tracking": "মামলা ট্র্যাকিং",
      "nav-search": "আইন খুঁজুন",
      "nav-corpus": "আইন ও করপাস",
      "nav-categories": "বিভাগসমূহ",
      "nav-about": "পরিচিতি",
      "nav-signin": "সাইন ইন",
      "nav-register": "নিবন্ধন",
      "nav-mycases": "আমার মামলাসমূহ",
      "nav-lawyerqueue": "রিভিউ কিউ",
      "nav-admindash": "ড্যাশবোর্ড",
      "nav-analytics": "অ্যানালিটিক্স",
      "nav-profile": "আমার প্রোফাইল ও সেটিংস",
      "nav-logout": "লগআউট",
      "disclaimer-tag": "দাবিত্যাগ:",
      "disclaimer-text": "মুক্ত আইন সাধারণ আইনি তথ্যসেবা দেয় — এটি আনুষ্ঠানিক আইনি পরামর্শ নয়। প্রতিটি দলিল ব্যবহারের পূর্বে সনদপ্রাপ্ত আইনজীবী দ্বারা পর্যালোচনা আবশ্যক।",
      "footer-tagline": "বাংলাদেশের নাগরিকদের জন্য বিনামূল্যে AI-সহায়ক আইনি তথ্য — প্রতিটি দলিল যাচাইকৃত আইনজীবী দ্বারা পর্যালোচিত।",
      "footer-nav-h": "মেনু",
      "footer-legal-h": "আইনি",
      "footer-roles-h": "ভূমিকা",
      "footer-nav-1": "আইনি সেবা ও চ্যাট",
      "footer-nav-2": "আইনি বিষয়সমূহ",
      "footer-nav-3": "আইন অনুসন্ধান",
      "footer-nav-4": "মামলা ট্র্যাকিং",
      "footer-nav-5": "আমাদের সম্পর্কে",
      "footer-leg-1": "পূর্ণাঙ্গ দাবিত্যাগ",
      "footer-leg-2": "সাধারণ জিজ্ঞাসা (FAQ)",
      "footer-leg-3": "ডেটাসেট কৃতজ্ঞতা",
      "footer-role-1": "নাগরিক সাইন ইন",
      "footer-role-2": "নতুন নিবন্ধন",
      "footer-role-3": "আইনজীবী রিভিউ পোর্টাল",
      "footer-role-4": "অ্যাডমিন ওভারভিউ",
      "footer-copyright": "© 2026 মুক্ত আইন MuktoAin · AI-Augmented Legal Aid for Bangladesh · Academic Project · Not formal legal advice",

      // Home
      "home-kicker": "AI-সহায়ক আইনি তথ্য ও পরামর্শ",
      "home-title": "সহজ ভাষায় আপনার অধিকার জানুন,<br />আইনি দলিলের খসড়া তৈরি করুন",
      "home-sub": "বাংলা, English বা Banglish-এ আপনার আইনি সমস্যা বলুন — আমরা প্রয়োজনীয় ধারা উদ্ধৃতিসহ অধিকার ব্যাখ্যা করব এবং আইনজীবী-যাচাইকৃত খসড়া প্রস্তুত করব।",
      "home-tab-rights": "আইনি অধিকার জানুন",
      "home-tab-search": "ধারা খুঁজুন",
      "home-composer-placeholder": "আপনার সমস্যা লিখুন... (যেমন: ৩ মাস বেতন পাইনি, জিডি করব কীভাবে?)",
      "home-search-placeholder": "আইনের কীওয়ার্ড লিখুন... (যেমন: বেতন, ধারা ১২৩)",
      "home-ex-label": "উদাহরণ:",
      "home-ex-1": "বকেয়া মজুরি দাবি",
      "home-ex-2": "হারানো দলিলের জিডি",
      "home-ex-3": "তথ্য অধিকার আবেদন (RTI)",
      "home-ex-4": "ভোক্তা অধিকার প্রতারণা",
      "home-workflow-kicker": "কার্যপ্রণালী · Workflow",
      "home-workflow-h": "এটি যেভাবে কাজ করে",
      "home-workflow-link": "পদ্ধতি সম্পর্কে বিস্তারিত →",
      "home-step1-t": "সমস্যা বর্ণনা করুন",
      "home-step1-sub": "Describe Problem",
      "home-step1-d": "চ্যাট বা ফর্মের মাধ্যমে আপনার আইনি সমস্যার কথা খুলে বলুন — সম্পূর্ণ বাংলায় বা মিশ্র ইংরেজিতে।",
      "home-step2-t": "অধিকার ও ধারা জানুন",
      "home-step2-sub": "Know Your Rights",
      "home-step2-d": "বাংলাদেশের সংকলিত আইনসমূহ (১,৪৮৪টি আইন · ৩৫,০০০+ ধারা) থেকে প্রাসঙ্গিক ধারা উদ্ধৃতিসহ সহজ ভাষায় আপনার অধিকার দেখানো হয়।",
      "home-step3-t": "আইনজীবী-যাচাইকৃত দলিল পান",
      "home-step3-sub": "Lawyer-Reviewed Document",
      "home-step3-d": "AI আবেদনের উপযুক্ত খসড়া তৈরি করে; সনদপ্রাপ্ত আইনজীবী পর্যালোচনা ও অনুমোদন করার পরই সুরক্ষিত PDF ডাউনলোড আনলক হয়।",
      "home-cats-kicker": "বিভাগসমূহ · Categories",
      "home-cats-h": "আইনি বিভাগসমূহ ও সেবাসমূহ",
      "home-cats-all": "সকল বিভাগ দেখুন →",
      "home-cat1-t": "শ্রম অধিকার ও অভিযোগ",
      "home-cat1-d": "বকেয়া বেতন, অন্যায় বরখাস্ত, মাতৃত্বকালীন সুবিধা ও ক্ষতিপূরণ দাবি",
      "home-cat1-b": "শ্রম আইন ২০০৬",
      "home-cat2-t": "সাধারণ ডায়েরি (GD)",
      "home-cat2-d": "ডকুমেন্ট হারানো, নিরাপত্তা হুমকি, অপরাধ ও থানায় জিডি সংক্রান্ত আবেদন",
      "home-cat2-b": "দণ্ডবিধি ১৮৬০",
      "home-cat3-t": "তথ্য অধিকার আবেদন",
      "home-cat3-d": "সরকারি ও স্বায়ত্তশাসিত প্রতিষ্ঠানের জনগুরুত্বপূর্ণ তথ্য চেয়ে ফর্ম ‘ক’ আবেদন",
      "home-cat3-b": "তথ্য অধিকার ২০০৯",
      "home-cat4-t": "ভোক্তা অধিকার সংরক্ষণ",
      "home-cat4-d": "ত্রুটিপূর্ণ পণ্য, অতিরিক্ত মূল্য, নকল ঔষধ ও সেবায় প্রতারণার ক্ষতিপূরণ",
      "home-cat4-b": "ভোক্তা অধিকার ২০০৯",
      "home-sec-kicker": "Enterprise Security · FR-17",
      "home-sec-h": "সম্পূর্ণ বিনামূল্যে, নাগরিক সুরক্ষায় নিবেদিত",
      "home-sec-p": "নাগরিক তথ্যের গোপনীয়তা রক্ষা আমাদের সর্বোচ্চ অগ্রাধিকার। আপনার ব্যক্তিগত তথ্য Data Protection API দ্বারা সুরক্ষিত থাকে এবং যেকোনো সময় বেনামে মামলা দায়ের ও ট্র্যাক করা সম্ভব।",
      "home-sec-btn1": "নতুন সমস্যা জমা দিন →",
      "home-sec-btn2": "আইনজীবীদের তালিকা",

      // Submit Case
      "submit-kicker": "Citizen Intake · FR-2",
      "submit-title": "আইনি সমস্যা জমা দিন",
      "submit-sub": "বাংলা, English বা Banglish-এ আপনার সমস্যার বিবরণ লিখুন — AI প্রয়োজনীয় আইন খুঁজবে ও দলিল প্রস্তুত করবে।",
      "submit-cat-label": "আইনি বিভাগ",
      "submit-cat-placeholder": "— বিভাগ নির্বাচন করুন —",
      "submit-cat-hint": "আপনার সমস্যার ধরন অনুযায়ী সঠিক বিভাগ বাছাই করুন (যেমন: মজুরি সমস্যা হলে শ্রম অভিযোগ)",
      "submit-dist-label": "জেলা",
      "submit-dist-placeholder": "— জেলা নির্বাচন করুন —",
      "submit-dist-hint": "যে জেলায় ঘটনাটি ঘটেছে বা সংশ্লিষ্ট কারখানা/প্রতিষ্ঠান অবস্থিত",
      "submit-title-label": "সংক্ষিপ্ত শিরোনাম",
      "submit-title-placeholder": "যেমন: ৩ মাস ধরে বকেয়া বেতন পরিশোধ না করা",
      "submit-title-hint": "এক লাইনে মূল অভিযোগ বা সমস্যার সারসংক্ষেপ",
      "submit-desc-label": "সমস্যার পূর্ণ বিবরণ",
      "submit-desc-placeholder": "ঘটনাটি বিস্তারিত লিখুন — কখন ঘটেছে, কী অন্যায় হয়েছে, আপনার কী ক্ষতি হয়েছে এবং আপনি কী আইনি প্রতিকার চান...",
      "submit-desc-langhint": "বাংলা, English বা Banglish যেকোনো ভাষায় লিখতে পারেন",
      "submit-anon-title": "বেনামে জমা দিন (Anonymous Submission · FR-2)",
      "submit-anon-desc": "আপনার নাম বা ব্যক্তিগত পরিচয় সংরক্ষিত থাকবে না; একটি গোপনীয় ট্র্যাকিং কোডের মাধ্যমে যেকোনো সময় ফলাফল ও খসড়া দেখতে পারবেন।",
      "submit-btn": "সমস্যা জমা দিন ও অধিকার জানুন →",

      // Track Case
      "track-kicker": "Citizen Dashboard · FR-8",
      "track-title": "আপনার জমাকৃত মামলাসমূহ",
      "track-sub": "আপনার সকল আইনি সমস্যা, ধারা বিশ্লেষণ ও খসড়া দলিলের অগ্রগতি পর্যবেক্ষণ করুন",
      "track-new-btn": "+ নতুন সমস্যা জমা দিন",
      "track-filter-label": "ফিল্টার:",
      "track-filter-all": "সকল মামলা",
      "track-filter-review": "পর্যালোচনাধীন",
      "track-filter-final": "অনুমোদিত",
      "track-filter-submitted": "দাখিলকৃত",
      "track-th-code": "ট্র্যাকিং কোড",
      "track-th-title": "শিরোনাম",
      "track-th-cat": "আইনি বিভাগ",
      "track-th-date": "দাখিলের তারিখ",
      "track-th-status": "বর্তমান স্ট্যাটাস",
      "track-th-action": "পদক্ষেপ",

      // Search Laws
      "search-kicker": "Statutory Full-Text Search · FR-7",
      "search-title": "বাংলাদেশের আইন সংকলনে খুঁজুন",
      "search-sub": "১,৪৮৪টি পূর্ণাঙ্গ আইন ও ৩৫,০০০+ ধারা থেকে সরাসরি কীওয়ার্ড, ধারা নম্বর বা বিষয়বস্তু খুঁজুন",
      "search-placeholder": "কীওয়ার্ড লিখুন... (যেমন: মজুরি, ক্ষতিপূরণ, ধারা ১২৩, ট্রেড ইউনিয়ন)",
      "search-btn": "খুঁজুন",
      "search-popular-label": "জনপ্রিয় সার্চ:",
      "search-pop-1": "মজুরি পরিশোধ",
      "search-pop-2": "ধারা ১২৩",
      "search-pop-3": "তথ্য অধিকার",
      "search-pop-4": "দণ্ডবিধি ৪২০",
      "search-empty-t": "আইন ও ধারার নাম লিখে অনুসন্ধান শুরু করুন",
      "search-empty-d": "বাংলা বা ইংরেজিতে সার্চ করুন। নির্দিষ্ট ধারা নম্বর যেমন 'ধারা ৩৩' বা 'section 33' দিয়েও সার্চ করতে পারেন।",
      "search-noresults-t": "কোনো ধারা খুঁজে পাওয়া যায়নি",
      "search-noresults-d": "অনুগ্রহ করে ভিন্ন কোনো শব্দ বা আইনের নাম দিয়ে আবার অনুসন্ধান করুন।",

      // Categories
      "cat-kicker": "Legal Scenarios · FR-6",
      "cat-title": "আইনি সেবাসমূহ ও বিভাগসমূহ",
      "cat-sub": "আপনার আইনি সমস্যাটি কোন বিভাগের অন্তর্ভুক্ত তা বেছে নিয়ে বিস্তারিত জানুন ও সরাসরি মামলা দাখিল করুন।",
      "cat-badge-laws": "সংশ্লিষ্ট আইন ও বিধিমালা",
      "cat-badge-draft": "স্বয়ংক্রিয় খসড়া ফরম্যাট",

      // Lawyer Queue
      "lawyer-kicker": "Lawyer Review Gate · FR-11",
      "lawyer-title": "আইনজীবী পর্যালোচনা ও অনুমোদন কিউ",
      "lawyer-sub": "নাগরিকের প্রস্তুতকৃত AI খসড়া দলিল পর্যালোচনা, পরিমার্জন ও আইনগত বৈধতা নিশ্চিত করুন।",
      "lawyer-filter-pending": "অপেক্ষমাণ খসড়া",
      "lawyer-filter-claimed": "আমার গৃহীত",
      "lawyer-filter-approved": "অনুমোদিত খসড়া",
      "lawyer-filter-rejected": "প্রত্যাখ্যাত",

      // Login & Register
      "login-kicker": "Secure Legal Portal",
      "login-title": "আপনার একাউন্টে প্রবেশ করুন",
      "login-sub": "মামলার অগ্রগতি দেখতে বা আইনজীবী রিভিউ করতে লগইন করুন",
      "login-sidebar-h": "নাগরিকের আইনি ক্ষমতায়নে মুক্ত আইন",
      "login-sidebar-1": "সহজ ভাষায় আপনার যেকোনো আইনি সমস্যার তাৎক্ষণিক পর্যালোচনা ও অধিকারের ব্যাখ্যা।",
      "login-sidebar-2": "স্বয়ংক্রিয় জিডি, আরটিআই এবং শ্রম-ভোক্তা অভিযোগের খসড়া তৈরি।",
      "login-sidebar-3": "বাংলাদেশ বার কাউন্সিলের সনদপ্রাপ্ত আইনজীবী কর্তৃক প্রতিটি দলিলের বাধ্যতামূলক সত্যায়ন।",
      "login-sidebar-4": "সম্পূর্ণ বিনামূল্যে, আধুনিক এনক্রিপশন ও নাগরিক তথ্যের গোপনীয়তা সুরক্ষা।",
      "login-email-label": "ইমেইল ঠিকানা",
      "login-pass-label": "পাসওয়ার্ড",
      "login-forgot": "পাসওয়ার্ড ভুলে গেছেন?",
      "login-remember": "আমাকে মনে রাখুন",
      "login-btn": "লগইন করুন →",
      "login-noacc": "নতুন ব্যবহারকারী?",
      "login-reglink": "নতুন একাউন্ট নিবন্ধন করুন",

      // About
      "about-kicker": "Mission & Vision",
      "about-title": "মুক্ত আইন কী এবং কেন?",
      "about-sub": "বাংলাদেশের নাগরিকদের জন্য AI-সহায়ক আইনি তথ্য প্ল্যাটফর্ম — সহজ ভাষায় অধিকার, আইনজীবী-যাচাইকৃত দলিল।"
    },
    en: {
      // Common
      "skip-link": "Skip to content",
      "nav-legal-aid": "Legal Aid",
      "nav-submit": "Submit Issue",
      "nav-tracking": "Case Tracking",
      "nav-search": "Search Laws",
      "nav-corpus": "Corpus & Acts",
      "nav-categories": "Categories",
      "nav-about": "About",
      "nav-signin": "Sign In",
      "nav-register": "Register",
      "nav-mycases": "My Cases",
      "nav-lawyerqueue": "Review Queue",
      "nav-admindash": "Dashboard",
      "nav-analytics": "Analytics",
      "nav-profile": "Profile & Settings",
      "nav-logout": "Sign Out",
      "disclaimer-tag": "Disclaimer:",
      "disclaimer-text": "MuktoAin provides general legal information and document drafting assistance. This is NOT formal legal advice. Every document must be reviewed by a verified lawyer before use.",
      "footer-tagline": "Free AI-augmented legal aid platform for citizens of Bangladesh — every document reviewed by verified advocates.",
      "footer-nav-h": "Navigation",
      "footer-legal-h": "Legal & Terms",
      "footer-roles-h": "Portals",
      "footer-nav-1": "Legal Aid & Chat",
      "footer-nav-2": "Legal Categories",
      "footer-nav-3": "Statute Search",
      "footer-nav-4": "Case Tracking",
      "footer-nav-5": "About Us",
      "footer-leg-1": "Full Legal Disclaimer",
      "footer-leg-2": "Frequently Asked Questions (FAQ)",
      "footer-leg-3": "Dataset Attribution",
      "footer-role-1": "Citizen Sign In",
      "footer-role-2": "New Registration",
      "footer-role-3": "Lawyer Review Portal",
      "footer-role-4": "Admin Overview",
      "footer-copyright": "© 2026 MuktoAin · AI-Augmented Legal Aid for Bangladesh · Academic Project · Not formal legal advice",

      // Home
      "home-kicker": "AI-Augmented Legal Aid for Bangladesh",
      "home-title": "Understand Your Rights in Plain Language,<br />Draft Legal Documents with AI",
      "home-sub": "Describe your legal issue in Bangla, English, or Banglish — we retrieve relevant statutes, explain applicable rights, and auto-draft lawyer-reviewed documents.",
      "home-tab-rights": "Explain My Rights",
      "home-tab-search": "Search Sections",
      "home-composer-placeholder": "Describe your problem... (e.g. Haven't received salary for 3 months, how to file a GD?)",
      "home-search-placeholder": "Search legal keywords... (e.g. wages, section 123)",
      "home-ex-label": "Examples:",
      "home-ex-1": "Unpaid Wage Claim",
      "home-ex-2": "Lost Document GD",
      "home-ex-3": "Right to Information (RTI)",
      "home-ex-4": "Consumer Protection Fraud",
      "home-workflow-kicker": "Workflow & Process",
      "home-workflow-h": "How It Works",
      "home-workflow-link": "Learn more about the process →",
      "home-step1-t": "Describe Your Problem",
      "home-step1-sub": "Describe Problem",
      "home-step1-d": "Describe your legal dispute via chat or intake form — in plain Bengali, English, or mixed Banglish.",
      "home-step2-t": "Know Rights & Sections",
      "home-step2-sub": "Know Your Rights",
      "home-step2-d": "Instant plain-language rights explanation citing statutory sections from 1,484 Bangladesh Acts.",
      "home-step3-t": "Lawyer-Certified Document",
      "home-step3-sub": "Lawyer-Reviewed Document",
      "home-step3-d": "AI prepares an application draft; a verified Bar Council advocate reviews and approves it before secure PDF download.",
      "home-cats-kicker": "Legal Categories",
      "home-cats-h": "Legal Practice Areas & Services",
      "home-cats-all": "Browse all categories →",
      "home-cat1-t": "Labour Rights & Complaints",
      "home-cat1-d": "Unpaid wages, unlawful termination, maternity benefits, and compensation claims",
      "home-cat1-b": "Labour Act 2006",
      "home-cat2-t": "General Diary (GD)",
      "home-cat2-d": "Lost documents, safety threats, offenses, and police GD applications",
      "home-cat2-b": "Penal Code 1860",
      "home-cat3-t": "Right to Information (RTI)",
      "home-cat3-d": "Request public importance information from government and autonomous bodies via Form 'Ka'",
      "home-cat3-b": "RTI Act 2009",
      "home-cat4-t": "Consumer Protection",
      "home-cat4-d": "Defective goods, overpricing, counterfeit medicine, and service fraud compensation",
      "home-cat4-b": "Consumer Rights 2009",
      "home-sec-kicker": "Enterprise Security · FR-17",
      "home-sec-h": "Completely Free, Dedicated to Citizen Privacy",
      "home-sec-p": "Citizen data privacy is our highest priority. All personal information is protected with ASP.NET Data Protection encryption, with full support for anonymous case submissions.",
      "home-sec-btn1": "Submit New Case →",
      "home-sec-btn2": "Verified Advocates",

      // Submit Case
      "submit-kicker": "Citizen Intake · FR-2",
      "submit-title": "Submit Legal Issue",
      "submit-sub": "Describe your situation in Bangla, English, or Banglish — AI will analyze statutes and draft appropriate documents.",
      "submit-cat-label": "Legal Category",
      "submit-cat-placeholder": "— Select Category —",
      "submit-cat-hint": "Choose the appropriate category matching your dispute (e.g. Labour Complaint for unpaid wages)",
      "submit-dist-label": "District",
      "submit-dist-placeholder": "— Select District —",
      "submit-dist-hint": "The administrative district where the incident occurred or where the institution is located",
      "submit-title-label": "Brief Title",
      "submit-title-placeholder": "e.g. Unpaid wages for 3 consecutive months",
      "submit-title-hint": "One-line summary of the core complaint",
      "submit-desc-label": "Detailed Problem Description",
      "submit-desc-placeholder": "Describe what happened in detail — when it occurred, the violations committed, damages suffered, and the legal remedy sought...",
      "submit-desc-langhint": "You can write in Bangla, English, or Banglish freely",
      "submit-anon-title": "Anonymous Submission (FR-2)",
      "submit-anon-desc": "Your identity will not be stored; you will receive a confidential tracking GUID code to check progress and download drafts.",
      "submit-btn": "Submit Case & View Rights Analysis →",

      // Track Case
      "track-kicker": "Citizen Dashboard · FR-8",
      "track-title": "Your Submitted Cases",
      "track-sub": "Track legal analysis, case status, and progress toward lawyer review approval",
      "track-new-btn": "+ Submit New Issue",
      "track-filter-label": "Filter:",
      "track-filter-all": "All Cases",
      "track-filter-review": "Under Review",
      "track-filter-final": "Approved",
      "track-filter-submitted": "Submitted",
      "track-th-code": "Tracking Code",
      "track-th-title": "Title",
      "track-th-cat": "Category",
      "track-th-date": "Submission Date",
      "track-th-status": "Status",
      "track-th-action": "Action",

      // Search Laws
      "search-kicker": "Statutory Full-Text Search · FR-7",
      "search-title": "Search Bangladesh Statutory Code",
      "search-sub": "Query sections and sub-sections directly across 1,484 Bangladesh Acts and 35,000+ legal sections",
      "search-placeholder": "Enter keywords... (e.g. wages, compensation, section 123, trade union)",
      "search-btn": "Search",
      "search-popular-label": "Popular Searches:",
      "search-pop-1": "Wage Payment",
      "search-pop-2": "Section 123",
      "search-pop-3": "Right to Information",
      "search-pop-4": "Penal Code 420",
      "search-empty-t": "Search Across All Bangladesh Statutes",
      "search-empty-d": "Search in English or Bangla. You can also look up specific sections directly (e.g. 'section 33' or 'ধারা ৩৩').",
      "search-noresults-t": "No Matching Sections Found",
      "search-noresults-d": "Please try again with a different keyword or Act name.",

      // Categories
      "cat-kicker": "Legal Scenarios · FR-6",
      "cat-title": "Legal Practice Areas & Categories",
      "cat-sub": "Explore categorized legal domains, review applicable statutes, and start a guided complaint filing.",
      "cat-badge-laws": "Applicable Statutes & Rules",
      "cat-badge-draft": "Standard Legal Templates",

      // Lawyer Queue
      "lawyer-kicker": "Lawyer Review Gate · FR-11",
      "lawyer-title": "Lawyer Review & Certification Queue",
      "lawyer-sub": "Review AI-generated legal drafts, make statutory edits, and certify documents for citizen download.",
      "lawyer-filter-pending": "Pending Drafts",
      "lawyer-filter-claimed": "My Claimed Cases",
      "lawyer-filter-approved": "Approved Drafts",
      "lawyer-filter-rejected": "Rejected",

      // Login & Register
      "login-kicker": "Secure Legal Portal",
      "login-title": "Sign In to MuktoAin",
      "login-sub": "Access your cases or review citizen document drafts",
      "login-sidebar-h": "Empowering Citizens with MuktoAin",
      "login-sidebar-1": "Instant analysis of your legal disputes with statutory section references in plain language.",
      "login-sidebar-2": "Automated drafting of General Diary applications, RTI requests, and Labour complaints.",
      "login-sidebar-3": "Mandatory review and certification of every legal document by verified advocates.",
      "login-sidebar-4": "Completely free, modern field-level encryption, and citizen privacy protection.",
      "login-email-label": "Email Address",
      "login-pass-label": "Password",
      "login-forgot": "Forgot Password?",
      "login-remember": "Remember me on this device",
      "login-btn": "Sign In →",
      "login-noacc": "New user?",
      "login-reglink": "Register a new account",

      // About
      "about-kicker": "Mission & Vision",
      "about-title": "What is MuktoAin and Why?",
      "about-sub": "An AI-augmented legal-aid platform for Bangladesh — plain-language rights and lawyer-verified drafting."
    }
  };

  function applyLanguage(l) {
    currentLang = l === "en" ? "en" : "bn";
    document.documentElement.lang = currentLang;
    try {
      localStorage.setItem("mkt-lang", currentLang);
      document.cookie = "mkt-lang=" + currentLang + ";path=/;max-age=31536000;SameSite=Lax";
    } catch (e) {}

    // Update active class on all language toggle buttons
    document.querySelectorAll(".lang-toggle").forEach(function (group) {
      group.querySelectorAll("button").forEach(function (btn) {
        var btnLang = btn.getAttribute("data-lang") || (btn.textContent.trim().toLowerCase().indexOf("en") !== -1 ? "en" : "bn");
        btn.classList.toggle("active", btnLang === currentLang);
      });
    });

    var dict = translations[currentLang];
    if (!dict) return;

    // 1. Skip link
    var skip = document.querySelector(".skip-link");
    if (skip) skip.textContent = dict["skip-link"];

    // 2. Surface 1 Disclaimer banner
    var disclaimerEl = document.querySelector("aside.disclaimer");
    if (disclaimerEl) {
      disclaimerEl.innerHTML = "<b>" + dict["disclaimer-tag"] + "</b> " + dict["disclaimer-text"];
    }

    // 3. Navbar navigation links (Preserving logo brand!)
    var navMap = [
      { sel: '.nav-links a[href="/"], .nav-links a[href=""]', text: dict["nav-legal-aid"], icon: "message-square" },
      { sel: '.nav-links a[href*="/Case/Submit"]', text: dict["nav-submit"], icon: "edit-3" },
      { sel: '.nav-links a[href*="/Case/Track"]', text: dict["nav-tracking"], icon: "folder-clock" },
      { sel: '.nav-links a[href*="/Admin/Dashboard"], .nav-links a[href="/Admin"]', text: dict["nav-admindash"], icon: "shield" },
      { sel: '.nav-links a[href*="/Admin/Analytics"]', text: dict["nav-analytics"], icon: "bar-chart-3" },
      { sel: '.nav-links a[href*="/Lawyer/Queue"]', text: dict["nav-lawyerqueue"], icon: "file-check-2" },
      { sel: '.nav-links a[href*="/Search"]', text: dict["nav-corpus"] || dict["nav-search"], icon: "search" },
      { sel: '.nav-links a[href*="/Category"]', text: dict["nav-categories"], icon: "layout-grid" },
      { sel: '.nav-links a[href*="/Home/About"]', text: dict["nav-about"], icon: "info" },
      { sel: '.nav-desktop-auth a[href*="/Account/Login"]', text: dict["nav-signin"] },
      { sel: '.nav-desktop-auth a[href*="/Account/Register"]', text: dict["nav-register"] }
    ];
    navMap.forEach(function (item) {
      document.querySelectorAll(item.sel).forEach(function (el) {
        if (item.icon) {
          el.innerHTML = '<i data-lucide="' + item.icon + '"></i>' + item.text;
        } else {
          el.textContent = item.text;
        }
      });
    });

    // 4. User menu popover
    var userPopMap = [
      { sel: '#user-pop a[href*="/Account/Profile"]', text: dict["nav-profile"], icon: "user" },
      { sel: '#user-pop .user-logout-btn', text: dict["nav-logout"], icon: "log-out" }
    ];
    userPopMap.forEach(function (item) {
      document.querySelectorAll(item.sel).forEach(function (el) {
        el.innerHTML = '<i data-lucide="' + item.icon + '"></i>' + item.text;
      });
    });

    // 5. Mobile drawer links
    var drawerMap = [
      { sel: 'aside.drawer nav a[href="/"]', text: currentLang === "en" ? "Legal Aid & Chat" : "আইনি সেবা ও চ্যাট", icon: "message-square" },
      { sel: 'aside.drawer nav a[href*="/Case/Submit"]', text: currentLang === "en" ? "Submit Problem (Intake)" : "সমস্যা জমা দিন (Intake)", icon: "edit-3" },
      { sel: 'aside.drawer nav a[href*="/Case/Track"]', text: currentLang === "en" ? "Case Tracking" : "মামলা ট্র্যাকিং", icon: "folder-clock" },
      { sel: 'aside.drawer nav a[href*="/Admin/Dashboard"], aside.drawer nav a[href="/Admin"]', text: currentLang === "en" ? "Admin Dashboard" : "অ্যাডমিন ড্যাশবোর্ড", icon: "shield" },
      { sel: 'aside.drawer nav a[href*="/Admin/Analytics"]', text: currentLang === "en" ? "Analytics & Reports" : "অ্যানালিটিক্স ও রিপোর্ট", icon: "bar-chart-3" },
      { sel: 'aside.drawer nav a[href*="/Lawyer/Queue"]', text: currentLang === "en" ? "Lawyer Review Queue" : "আইনজীবী রিভিউ কিউ", icon: "file-check-2" },
      { sel: 'aside.drawer nav a[href*="/Search"]', text: currentLang === "en" ? "Statutes & Corpus" : "আইন ও করপাস", icon: "search" },
      { sel: 'aside.drawer nav a[href*="/Category"]', text: currentLang === "en" ? "Legal Categories" : "আইনি বিভাগসমূহ", icon: "layout-grid" },
      { sel: 'aside.drawer nav a[href*="/Home/About"]', text: currentLang === "en" ? "About & Disclaimer" : "পরিচিতি ও দাবিত্যাগ", icon: "info" },
      { sel: 'aside.drawer nav a[href*="/Account/Login"]', text: currentLang === "en" ? "Sign In" : "সাইন ইন", icon: "log-in" },
      { sel: 'aside.drawer nav a[href*="/Account/Register"]', text: currentLang === "en" ? "Register New Account" : "নতুন একাউন্ট নিবন্ধন", icon: "user-plus" },
      { sel: 'aside.drawer nav a[href*="/Account/Profile"]', text: currentLang === "en" ? "My Profile & Settings" : "আমার প্রোফাইল ও সেটিংস", icon: "user" }
    ];
    drawerMap.forEach(function (item) {
      document.querySelectorAll(item.sel).forEach(function (el) {
        el.innerHTML = '<i data-lucide="' + item.icon + '"></i> ' + item.text;
      });
    });

    // 6. Breadcrumbs auto-translator
    document.querySelectorAll(".breadcrumbs a, .breadcrumbs span").forEach(function (b) {
      if (b.dataset.en && b.dataset.bn) {
        b.textContent = currentLang === "en" ? b.dataset.en : b.dataset.bn;
        return;
      }
      var t = b.textContent.trim();
      if (currentLang === "en") {
        if (t === "হোম" || t === "নীড়") b.textContent = "Home";
        else if (t === "মামলাসমূহ" || t === "মামলা") b.textContent = "Cases";
        else if (t === "নতুন দাখিল") b.textContent = "New Submission";
        else if (t === "মামলা ট্র্যাকিং") b.textContent = "Case Tracking";
        else if (t === "আইন অনুসন্ধান" || t === "আইন খুঁজুন" || t === "আইন ও করপাস") b.textContent = "Search Statutes";
        else if (t === "আইনি বিভাগসমূহ" || t === "বিভাগসমূহ") b.textContent = "Categories";
        else if (t === "পরিচিতি ও দাবিত্যাগ" || t === "পরিচিতি") b.textContent = "About & Disclaimer";
        else if (t === "সাইন ইন") b.textContent = "Sign In";
        else if (t === "নিবন্ধন") b.textContent = "Register";
        else if (t === "আইনজীবী কিউ") b.textContent = "Lawyer Queue";
        else if (t.indexOf("অ্যাডমিন") !== -1 || t.indexOf("মিশন কন্ট্রোল") !== -1) b.textContent = "Admin Console";
        else if (t.indexOf("অ্যানালিটিক্স") !== -1) b.textContent = "Analytics";
        else if (t.indexOf("প্রোফাইল") !== -1) b.textContent = "Account Profile";
      } else {
        if (t === "Home") b.textContent = "হোম";
        else if (t === "Cases") b.textContent = "মামলাসমূহ";
        else if (t === "New Submission") b.textContent = "নতুন দাখিল";
        else if (t === "Case Tracking") b.textContent = "মামলা ট্র্যাকিং";
        else if (t === "Search Statutes") b.textContent = "আইন অনুসন্ধান";
        else if (t === "Categories") b.textContent = "আইনি বিভাগসমূহ";
        else if (t === "About & Disclaimer") b.textContent = "পরিচিতি ও দাবিত্যাগ";
        else if (t === "Sign In") b.textContent = "সাইন ইন";
        else if (t === "Register") b.textContent = "নিবন্ধন";
        else if (t === "Lawyer Queue") b.textContent = "আইনজীবী কিউ";
        else if (t === "Admin Console") b.textContent = "অ্যাডমিন কনসোল";
        else if (t === "Analytics") b.textContent = "অ্যানালিটিক্স";
        else if (t === "Account Profile") b.textContent = "অ্যাকাউন্ট প্রোফাইল";
      }
    });

    // 6b. Generic bilingual block pairs -- used where the Bangla/English phrasing
    // differs enough (word order, nested inline tags) that swapping a single
    // text node via data-bn/data-en isn't enough. Markup ships both variants;
    // this just shows the one matching currentLang and hides the other.
    document.querySelectorAll(".i18n-bn").forEach(function (el) { el.style.display = currentLang === "en" ? "none" : ""; });
    document.querySelectorAll(".i18n-en").forEach(function (el) { el.style.display = currentLang === "en" ? "" : "none"; });

    // 7. Route-Scoped Page Content Translation
    var path = window.location.pathname.toLowerCase();

    if (path === "/" || path === "/home" || path === "/home/index" || path === "") {
      // Home Page
      var kickerEl = document.querySelector(".page-head .kicker");
      if (kickerEl) kickerEl.innerHTML = '<i data-lucide="scale" aria-hidden="true"></i> ' + dict["home-kicker"];
      var titleEl = document.querySelector(".page-head .page-title");
      if (titleEl) titleEl.innerHTML = dict["home-title"];
      var subEl = document.querySelector(".page-head .page-sub");
      if (subEl) subEl.innerHTML = dict["home-sub"];

      var composerBtn1 = document.querySelector("#composer-mode button:first-child");
      if (composerBtn1) composerBtn1.innerHTML = '<i data-lucide="message-square"></i> ' + dict["home-tab-rights"];
      var composerBtn2 = document.querySelector("#composer-mode button:last-child");
      if (composerBtn2) composerBtn2.innerHTML = '<i data-lucide="search"></i> ' + dict["home-tab-search"];

      var textarea = document.querySelector(".composer textarea");
      if (textarea) {
        var isSearch = composerBtn2 && composerBtn2.classList.contains("active");
        textarea.placeholder = isSearch ? dict["home-search-placeholder"] : dict["home-composer-placeholder"];
      }

      var exLabel = document.querySelector(".page-head .tiny.muted");
      if (exLabel) exLabel.textContent = dict["home-ex-label"];

      var exChips = document.querySelectorAll(".page-head a.chip.chip-sm");
      if (exChips.length >= 4) {
        exChips[0].textContent = dict["home-ex-1"];
        exChips[1].textContent = dict["home-ex-2"];
        exChips[2].textContent = dict["home-ex-3"];
        exChips[3].textContent = dict["home-ex-4"];
      }

      var workflowKicker = document.querySelector("section:nth-of-type(2) .kicker");
      if (workflowKicker) workflowKicker.innerHTML = '<i data-lucide="workflow"></i> ' + dict["home-workflow-kicker"];

      var workflowH = document.querySelector(".section-h");
      if (workflowH) workflowH.textContent = dict["home-workflow-h"];
      var workflowLink = document.querySelector('a[href*="/Home/About"].tiny.muted');
      if (workflowLink) workflowLink.textContent = dict["home-workflow-link"];

      var stepCards = document.querySelectorAll(".grid-3 .card");
      if (stepCards.length >= 3) {
        var num1 = stepCards[0].querySelector(".step-num");
        if (num1) num1.textContent = currentLang === "en" ? "1" : "১";
        var b1 = stepCards[0].querySelector("b");
        if (b1) b1.textContent = dict["home-step1-t"];
        var sub1 = stepCards[0].querySelector(".cat-en");
        if (sub1) {
          sub1.textContent = dict["home-step1-sub"];
          sub1.style.display = currentLang === "en" ? "none" : "";
        }
        var p1 = stepCards[0].querySelector("p");
        if (p1) p1.textContent = dict["home-step1-d"];

        var num2 = stepCards[1].querySelector(".step-num");
        if (num2) num2.textContent = currentLang === "en" ? "2" : "২";
        var b2 = stepCards[1].querySelector("b");
        if (b2) b2.textContent = dict["home-step2-t"];
        var sub2 = stepCards[1].querySelector(".cat-en");
        if (sub2) {
          sub2.textContent = dict["home-step2-sub"];
          sub2.style.display = currentLang === "en" ? "none" : "";
        }
        var p2 = stepCards[1].querySelector("p");
        if (p2) p2.textContent = dict["home-step2-d"];

        var num3 = stepCards[2].querySelector(".step-num");
        if (num3) num3.textContent = currentLang === "en" ? "3" : "৩";
        var b3 = stepCards[2].querySelector("b");
        if (b3) b3.textContent = dict["home-step3-t"];
        var sub3 = stepCards[2].querySelector(".cat-en");
        if (sub3) {
          sub3.textContent = dict["home-step3-sub"];
          sub3.style.display = currentLang === "en" ? "none" : "";
        }
        var p3 = stepCards[2].querySelector("p");
        if (p3) p3.textContent = dict["home-step3-d"];
      }

      var catSectionKicker = document.querySelector("#categories .kicker, section:nth-of-type(3) .kicker");
      if (catSectionKicker) catSectionKicker.innerHTML = '<i data-lucide="layers"></i> ' + dict["home-cats-kicker"];
      var catSectionH = document.querySelector("#categories .section-h");
      if (catSectionH) catSectionH.textContent = dict["home-cats-h"];
      var catSectionAll = document.querySelector('#categories a.btn-quiet');
      if (catSectionAll) catSectionAll.textContent = dict["home-cats-all"];

      // Category cards on home page
      var catCards = document.querySelectorAll("#categories .grid-4 a.cat-card");
      if (catCards.length >= 4) {
        var c1_b = catCards[0].querySelector("b");
        var c1_p = catCards[0].querySelector("p");
        var c1_bdg = catCards[0].querySelector(".badge");
        var c1_en = catCards[0].querySelector(".cat-en");
        if (c1_b) c1_b.textContent = dict["home-cat1-t"];
        if (c1_p) c1_p.textContent = dict["home-cat1-d"];
        if (c1_bdg) c1_bdg.textContent = dict["home-cat1-b"];
        if (c1_en) c1_en.style.display = currentLang === "en" ? "none" : "";

        var c2_b = catCards[1].querySelector("b");
        var c2_p = catCards[1].querySelector("p");
        var c2_bdg = catCards[1].querySelector(".badge");
        var c2_en = catCards[1].querySelector(".cat-en");
        if (c2_b) c2_b.textContent = dict["home-cat2-t"];
        if (c2_p) c2_p.textContent = dict["home-cat2-d"];
        if (c2_bdg) c2_bdg.textContent = dict["home-cat2-b"];
        if (c2_en) c2_en.style.display = currentLang === "en" ? "none" : "";

        var c3_b = catCards[2].querySelector("b");
        var c3_p = catCards[2].querySelector("p");
        var c3_bdg = catCards[2].querySelector(".badge");
        var c3_en = catCards[2].querySelector(".cat-en");
        if (c3_b) c3_b.textContent = dict["home-cat3-t"];
        if (c3_p) c3_p.textContent = dict["home-cat3-d"];
        if (c3_bdg) c3_bdg.textContent = dict["home-cat3-b"];
        if (c3_en) c3_en.style.display = currentLang === "en" ? "none" : "";

        var c4_b = catCards[3].querySelector("b");
        var c4_p = catCards[3].querySelector("p");
        var c4_bdg = catCards[3].querySelector(".badge");
        var c4_en = catCards[3].querySelector(".cat-en");
        if (c4_b) c4_b.textContent = dict["home-cat4-t"];
        if (c4_p) c4_p.textContent = dict["home-cat4-d"];
        if (c4_bdg) c4_bdg.textContent = dict["home-cat4-b"];
        if (c4_en) c4_en.style.display = currentLang === "en" ? "none" : "";
      }

      // Security section
      var secKicker = document.querySelector("section.card .kicker");
      if (secKicker) secKicker.innerHTML = '<i data-lucide="shield-check"></i> ' + dict["home-sec-kicker"];
      var secH = document.querySelector("section.card h3");
      if (secH) secH.textContent = dict["home-sec-h"];
      var secP = document.querySelector("section.card p.muted");
      if (secP) secP.textContent = dict["home-sec-p"];
      var secBtn1 = document.querySelector('section.card a.btn-primary');
      if (secBtn1) secBtn1.textContent = dict["home-sec-btn1"];
      var secBtn2 = document.querySelector('section.card a.btn-outline');
      if (secBtn2) secBtn2.textContent = dict["home-sec-btn2"];

    } else if (path.indexOf("/case/submit") !== -1) {
      // Case Submit Page
      var kicker = document.querySelector(".page-head .kicker");
      if (kicker) kicker.innerHTML = '<i data-lucide="edit-3"></i> ' + dict["submit-kicker"];
      var title = document.querySelector(".page-head .page-title");
      if (title) title.textContent = dict["submit-title"];
      var sub = document.querySelector(".page-head .page-sub");
      if (sub) sub.textContent = dict["submit-sub"];

      var labels = document.querySelectorAll("form .field label:not(.check-row)");
      if (labels.length >= 4) {
        labels[0].textContent = dict["submit-cat-label"];
        labels[1].textContent = dict["submit-dist-label"];
        labels[2].textContent = dict["submit-title-label"];
        labels[3].textContent = dict["submit-desc-label"];
      }

      var hints = document.querySelectorAll("form .field .hint");
      if (hints.length >= 3) {
        hints[0].textContent = dict["submit-cat-hint"];
        hints[1].textContent = dict["submit-dist-hint"];
        hints[2].textContent = dict["submit-title-hint"];
      }

      var descLangHint = document.querySelector(".field .row.spread .tiny.muted");
      if (descLangHint) descLangHint.textContent = dict["submit-desc-langhint"];

      var optCat = document.querySelector('select[name="CategoryId"] option[value=""]');
      if (optCat) optCat.textContent = dict["submit-cat-placeholder"];
      var optDist = document.querySelector('select[name="DistrictId"] option[value=""]');
      if (optDist) optDist.textContent = dict["submit-dist-placeholder"];

      var inpTitle = document.querySelector('input[name="Title"]');
      if (inpTitle) inpTitle.placeholder = dict["submit-title-placeholder"];
      var txtDesc = document.querySelector('textarea[name="Description"]');
      if (txtDesc) txtDesc.placeholder = dict["submit-desc-placeholder"];

      var anonB = document.querySelector(".check-row b");
      if (anonB) anonB.textContent = dict["submit-anon-title"];
      var anonSmall = document.querySelector(".check-row small");
      if (anonSmall) anonSmall.textContent = dict["submit-anon-desc"];

      var submitBtn = document.querySelector('form button[type="submit"]');
      if (submitBtn) submitBtn.innerHTML = '<i data-lucide="send"></i> ' + dict["submit-btn"];

    } else if (path.indexOf("/case/track") !== -1) {
      // Case Track Page
      var kicker = document.querySelector(".page-head .kicker");
      if (kicker) kicker.innerHTML = '<i data-lucide="folder-clock"></i> ' + dict["track-kicker"];
      var title = document.querySelector(".page-head .page-title");
      if (title) title.textContent = dict["track-title"];
      var sub = document.querySelector(".page-head .page-sub");
      if (sub) sub.textContent = dict["track-sub"];

      var newBtn = document.querySelector('.page-head a[href*="/Case/Submit"]');
      if (newBtn) newBtn.innerHTML = '<i data-lucide="plus-circle"></i> ' + dict["track-new-btn"];

      var filterLabel = document.querySelector(".row.wrap .tiny.muted");
      if (filterLabel) filterLabel.textContent = dict["track-filter-label"];

      var filterChips = document.querySelectorAll(".chip-row .chip");
      if (filterChips.length >= 4) {
        var countMatch = filterChips[0].textContent.match(/\(([^)]+)\)/);
        var countStr = countMatch ? " (" + countMatch[1] + ")" : "";
        filterChips[0].textContent = dict["track-filter-all"] + countStr;
        filterChips[1].textContent = dict["track-filter-review"];
        filterChips[2].textContent = dict["track-filter-final"];
        filterChips[3].textContent = dict["track-filter-submitted"];
      }

      var ths = document.querySelectorAll("table thead th");
      if (ths.length >= 6) {
        ths[0].textContent = dict["track-th-code"];
        ths[1].textContent = dict["track-th-title"];
        ths[2].textContent = dict["track-th-cat"];
        ths[3].textContent = dict["track-th-date"];
        ths[4].textContent = dict["track-th-status"];
        ths[5].textContent = dict["track-th-action"];
      }

    } else if (path.indexOf("/search") !== -1) {
      // Search Laws Page
      var kicker = document.querySelector(".search-hero .kicker");
      if (kicker) kicker.innerHTML = '<i data-lucide="search"></i> ' + dict["search-kicker"];
      var title = document.querySelector(".search-hero .page-title");
      if (title) title.textContent = dict["search-title"];
      var sub = document.querySelector(".search-hero .page-sub");
      if (sub) sub.textContent = dict["search-sub"];

      var searchInp = document.querySelector('.search-bar input[name="q"], .search-bar input[type="search"], .search-bar input[type="text"]');
      if (searchInp) searchInp.placeholder = dict["search-placeholder"];

      var searchBtn = document.querySelector(".search-bar button");
      if (searchBtn) searchBtn.innerHTML = '<i data-lucide="search"></i> ' + dict["search-btn"];

      var popLabel = document.querySelector(".search-bar .row.wrap .tiny.muted");
      if (popLabel) popLabel.textContent = dict["search-popular-label"];

      // Popular-search chips carry both a label and the actual query they submit
      // (via asp-route-q at render time) -- translating the label alone leaves the
      // href pointing at whichever language rendered the page, so a chip that now
      // *reads* in English still searched for the Bangla term underneath. Rebuild
      // the href from the same dict entry driving the label so they can't diverge.
      var popChips = document.querySelectorAll(".search-bar a.chip.chip-sm");
      if (popChips.length >= 4) {
        var popKeys = ["search-pop-1", "search-pop-2", "search-pop-3", "search-pop-4"];
        popKeys.forEach(function (key, i) {
          var term = dict[key];
          popChips[i].textContent = term;
          popChips[i].setAttribute("href", window.location.pathname + "?q=" + encodeURIComponent(term));
        });
      }

      // Empty state comes in two flavors sharing the same markup class: the
      // "start searching" prompt (no query yet) and "no results" (query ran, 0
      // hits). Distinguish by whether a query is present on the page so the
      // right copy is translated instead of always applying the prompt text.
      var emptyBox = document.querySelector(".search-empty, .empty-state");
      if (emptyBox) {
        var qInput = document.querySelector('.search-bar input[name="q"]');
        var hasQuery = !!(qInput && qInput.value);
        var h3 = emptyBox.querySelector("h3");
        var p = emptyBox.querySelector("p");
        if (hasQuery) {
          if (h3) h3.textContent = dict["search-noresults-t"];
          if (p) p.textContent = dict["search-noresults-d"];
        } else {
          if (h3) h3.textContent = dict["search-empty-t"];
          if (p) p.textContent = dict["search-empty-d"];
        }
      }

      // Result cards, reading modals, and the rail (filter checkboxes + search-tips
      // card) all render hardcoded Bangla in the view. Translate every element
      // carrying the data-bn/data-en pair the view now emits for these (icons,
      // where present, are untouched since they live outside the translated
      // span/text node).
      document.querySelectorAll(".result-item [data-bn][data-en], .reading-modal [data-bn][data-en], .rail [data-bn][data-en]").forEach(function (el) {
        el.textContent = currentLang === "en" ? el.dataset.en : el.dataset.bn;
      });

      // Pagination page numbers render as real digits carried in data-page --
      // reformat them into the active script (Bengali vs Latin) rather than
      // leaving them permanently Bengali regardless of language.
      document.querySelectorAll(".pagination [data-page]").forEach(function (el) {
        var n = el.getAttribute("data-page");
        el.textContent = currentLang === "en" ? n : toBengaliDigits(n);
      });

    } else if (path.indexOf("/category/details") !== -1 || path.indexOf("/category/") !== -1 && path.length > "/category/".length) {
      // Category Details Page
      var catCrumb = document.querySelector(".crumb-cat-name");
      if (catCrumb) {
        var bnCrumb = catCrumb.getAttribute("data-bn");
        var enCrumb = catCrumb.getAttribute("data-en");
        if (bnCrumb && enCrumb) {
          catCrumb.textContent = currentLang === "en" ? enCrumb : bnCrumb;
        }
      }

      var catTitle = document.querySelector(".card .page-title, .page-head .page-title");
      var catEn = document.querySelector(".card .cat-en");
      var catH3 = document.querySelector(".cat-details-h");
      var catDesc = document.querySelector(".cat-details-desc");
      var catClaimsH = document.querySelector(".cat-claims-h");
      var catClaimsList = document.querySelector(".cat-claims-list");
      var catSubmitBtn = document.querySelector('.card a.btn-primary[href*="/Case/Submit"], .card a.btn-primary');

      if (catTitle && catEn) {
        if (currentLang === "en") {
          if (!catTitle.dataset.bn) catTitle.dataset.bn = catTitle.textContent;
          catTitle.textContent = catEn.textContent;
          catEn.style.display = "none";
        } else {
          if (catTitle.dataset.bn) catTitle.textContent = catTitle.dataset.bn;
          catEn.style.display = "";
        }
      }

      if (catH3) catH3.textContent = currentLang === "en" ? "Category Description & Scope" : "বিভাগের বিবরণ ও পরিধি";
      if (catDesc) {
        var enDesc = catDesc.getAttribute("data-en");
        var bnDesc = catDesc.getAttribute("data-bn");
        if (enDesc && bnDesc) {
          catDesc.textContent = currentLang === "en" ? enDesc : bnDesc;
        }
      }
      if (catClaimsH) {
        catClaimsH.innerHTML = '<i data-lucide="check-square"></i> ' + (currentLang === "en" ? "Frequently Filed Issues in this Category:" : "এই বিভাগে সচরাচর দাখিলযোগ্য অভিযোগসমূহ:");
      }
      if (catClaimsList) {
        var items = catClaimsList.querySelectorAll("li");
        items.forEach(function(li) {
          var bnLi = li.getAttribute("data-bn");
          var enLi = li.getAttribute("data-en");
          if (bnLi && enLi) {
            li.textContent = currentLang === "en" ? enLi : bnLi;
          }
        });
      }
      if (catSubmitBtn) {
        catSubmitBtn.innerHTML = '<i data-lucide="edit-3"></i> ' + (currentLang === "en" ? "Submit Issue in this Category →" : "এই বিভাগে মামলা দাখিল করুন →");
      }

    } else if (path.indexOf("/category") !== -1) {
      // Category Grid Page
      var catsCrumb = document.querySelector(".crumb-cats");
      if (catsCrumb) {
        catsCrumb.textContent = currentLang === "en" ? "Categories" : "আইনি বিভাগসমূহ";
      }

      var kicker = document.querySelector(".page-head .kicker");
      if (kicker) kicker.innerHTML = '<i data-lucide="layout-grid"></i> ' + dict["cat-kicker"];
      var title = document.querySelector(".page-head .page-title");
      if (title) title.textContent = dict["cat-title"];
      var sub = document.querySelector(".page-head .page-sub");
      if (sub) sub.textContent = dict["cat-sub"];

      document.querySelectorAll(".cat-card").forEach(function (card) {
        var h2 = card.querySelector("h2");
        var enSpan = card.querySelector(".cat-en");
        var descP = card.querySelector(".cat-desc, p.muted");

        if (h2 && enSpan) {
          if (currentLang === "en") {
            if (!card.dataset.bnTitle) card.dataset.bnTitle = h2.textContent;
            h2.textContent = enSpan.textContent;
            enSpan.style.display = "none";
          } else {
            if (card.dataset.bnTitle) h2.textContent = card.dataset.bnTitle;
            enSpan.style.display = "";
          }
        }

        if (descP) {
          var enDesc = descP.getAttribute("data-en");
          var bnDesc = descP.getAttribute("data-bn");
          if (enDesc && bnDesc) {
            descP.textContent = currentLang === "en" ? enDesc : bnDesc;
          }
        }
      });

      var badges = document.querySelectorAll(".cat-meta .badge");
      badges.forEach(function (b) {
        if (b.textContent.indexOf("সংশ্লিষ্ট") !== -1 || b.textContent.indexOf("Statutes") !== -1) {
          b.innerHTML = '<i data-lucide="book-open"></i> ' + dict["cat-badge-laws"];
        } else if (b.textContent.indexOf("খসড়া") !== -1 || b.textContent.indexOf("Templates") !== -1) {
          b.innerHTML = '<i data-lucide="file-text"></i> ' + dict["cat-badge-draft"];
        }
      });

    } else if (path.indexOf("/lawyer") !== -1) {
      // Lawyer Portal
      var kicker = document.querySelector(".page-head .kicker");
      if (kicker) kicker.innerHTML = '<i data-lucide="award"></i> ' + (currentLang === "en" ? "Verified Advocate Portal · FR-13" : "সনদপ্রাপ্ত আইনজীবী পোর্টাল · FR-13");
      var title = document.querySelector(".page-head .page-title");
      if (title) title.textContent = currentLang === "en" ? "Document Review Queue" : "দলিল পর্যালোচনা কিউ (Review Queue)";
      var sub = document.querySelector(".page-head .page-sub");
      if (sub) sub.textContent = currentLang === "en" ? "Review and certify AI-generated drafts to approve final official documents for citizens." : "AI দ্বারা প্রস্তুতকৃত খসড়া দলিল পর্যালোচনা ও সত্যায়ন করে নাগরিকের জন্য চূড়ান্ত PDF অনুমোদন করুন।";

      var badgeBar = document.querySelector(".page-head .badge-final");
      if (badgeBar) badgeBar.innerHTML = '<i data-lucide="check-circle-2"></i> ' + (currentLang === "en" ? "Bar Verified: DHA-1187" : "বার সনদ যাচাইকৃত: DHA-1187");

      var kpiCards = document.querySelectorAll(".stat-strip .kpi");
      if (kpiCards.length >= 3) {
        var k1_lbl = kpiCards[0].querySelector(".k-label");
        var k1_num = kpiCards[0].querySelector(".k-num");
        var k1_sub = kpiCards[0].querySelector(".k-sub");
        if (k1_lbl) k1_lbl.innerHTML = '<i data-lucide="clock"></i> ' + (currentLang === "en" ? "Pending in Queue" : "অপেক্ষমাণ কিউ");
        if (k1_num) k1_num.textContent = currentLang === "en" ? "3 items" : "৩টি";
        if (k1_sub) k1_sub.textContent = currentLang === "en" ? "Avg Review Time: 2 hours" : "গড় পর্যালোচনা সময়: ২ ঘণ্টা";

        var k2_lbl = kpiCards[1].querySelector(".k-label");
        var k2_num = kpiCards[1].querySelector(".k-num");
        var k2_sub = kpiCards[1].querySelector(".k-sub");
        if (k2_lbl) k2_lbl.innerHTML = '<i data-lucide="check-check"></i> ' + (currentLang === "en" ? "Your Reviews" : "আপনার পর্যালোচনাসমূহ");
        if (k2_num) k2_num.textContent = currentLang === "en" ? "28 items" : "২৮টি";
        if (k2_sub) k2_sub.textContent = currentLang === "en" ? "Completed this month" : "এই মাসে সম্পন্ন";

        var k3_lbl = kpiCards[2].querySelector(".k-label");
        var k3_num = kpiCards[2].querySelector(".k-num");
        var k3_sub = kpiCards[2].querySelector(".k-sub");
        if (k3_lbl) k3_lbl.innerHTML = '<i data-lucide="star"></i> ' + (currentLang === "en" ? "Pro-Bono Hours" : "প্রো-বোনো ঘণ্টা");
        if (k3_num) k3_num.textContent = currentLang === "en" ? "14.5" : "১৪.৫";
        if (k3_sub) k3_sub.textContent = currentLang === "en" ? "Legal aid contribution" : "আইনি সহায়তা অবদান";
      }

      var lawyerThs = document.querySelectorAll("table thead th");
      if (lawyerThs.length >= 6) {
        lawyerThs[0].textContent = currentLang === "en" ? "Case Tracking" : "মামলা ট্র্যাকিং";
        lawyerThs[1].textContent = currentLang === "en" ? "Title & Description" : "শিরোনাম ও বিবরণ";
        lawyerThs[2].textContent = currentLang === "en" ? "Legal Category" : "আইনি বিভাগ";
        lawyerThs[3].textContent = currentLang === "en" ? "Submitted At" : "দাখিলের সময়";
        lawyerThs[4].textContent = currentLang === "en" ? "Status" : "বর্তমান অবস্থা";
        lawyerThs[5].textContent = currentLang === "en" ? "Action" : "পদক্ষেপ";
      }

      document.querySelectorAll("table tbody a.btn-primary").forEach(function(btn) {
        btn.textContent = currentLang === "en" ? "Review Draft →" : "পর্যালোচনা করুন →";
      });

    } else if (path.indexOf("/admin/dashboard") !== -1 || path === "/admin" || path === "/admin/") {
      // Admin Dashboard / Mission Control
      var kicker = document.querySelector(".page-head .kicker");
      if (kicker) kicker.innerHTML = '<i data-lucide="shield-check"></i> ' + (currentLang === "en" ? "FR-15 · Mission Control & Operations" : "FR-15 · মিশন কন্ট্রোল ও সিস্টেম অপারেশনস");
      var title = document.querySelector(".page-head .page-title");
      if (title) title.textContent = currentLang === "en" ? "Admin Control Hub" : "অ্যাডমিন কন্ট্রোল হাব";
      var sub = document.querySelector(".page-head .page-sub");
      if (sub) sub.textContent = currentLang === "en" ? "Platform infrastructure status, advocate verification triage, and system action console." : "প্ল্যাটফর্ম ইনফ্রাস্ট্রাকচার স্ট্যাটাস, আইনজীবী সনদ যাচাই ট্রায়াজ এবং সিস্টেম অ্যাকশন কনসোল।";

      var pulseHead = document.querySelector(".card strong i[data-lucide='server']");
      if (pulseHead && pulseHead.parentElement) {
        pulseHead.parentElement.innerHTML = '<i data-lucide="server" style="width:16px;height:16px;color:var(--gold);"></i> ' + (currentLang === "en" ? "Live Infrastructure & Service Pulse (System Health)" : "লাইভ ইনফ্রাস্ট্রাকচার ও সার্ভিস পালস (System Health)");
      }
      var pulseBadge = document.querySelector(".card span.badge-success");
      if (pulseBadge && (pulseBadge.textContent.indexOf("সচল") !== -1 || pulseBadge.textContent.indexOf("Operational") !== -1)) {
        pulseBadge.textContent = currentLang === "en" ? "All Services Operational" : "সকল সার্ভিস সচল (Operational)";
      }

      var kpis = document.querySelectorAll(".grid-4 .kpi");
      if (kpis.length >= 4) {
        var k1 = kpis[0].querySelector(".k-label");
        var k1_sub = kpis[0].querySelector(".k-sub");
        if (k1) k1.innerHTML = '<i data-lucide="user-check"></i> ' + (currentLang === "en" ? "Verifications Waiting" : "সনদ যাচাই অপেক্ষমান");
        if (k1_sub) k1_sub.textContent = currentLang === "en" ? "Bar Council Advocate Applications" : "বাংলাদেশ বার কাউন্সিল আইনজীবী আবেদন";

        var k2 = kpis[1].querySelector(".k-label");
        var k2_sub = kpis[1].querySelector(".k-sub");
        if (k2) k2.innerHTML = '<i data-lucide="clock"></i> ' + (currentLang === "en" ? "Review Queue Backlog" : "রিভিউ কিউ ব্যাকলগ");
        if (k2_sub) k2_sub.textContent = currentLang === "en" ? "Cases pending lawyer approval" : "আইনজীবী পর্যালোচনার অপেক্ষায়";

        var k3 = kpis[2].querySelector(".k-label");
        var k3_sub = kpis[2].querySelector(".k-sub");
        if (k3) k3.innerHTML = '<i data-lucide="book-marked"></i> ' + (currentLang === "en" ? "Statutes & Act Corpus" : "আইন ও স্ট্যাটিউট করপাস");
        if (k3_sub) k3_sub.textContent = currentLang === "en" ? "Fully digitalized legal repository" : "সম্পূর্ণ ডিজিটালাইজড আইন ভাণ্ডার";

        var k4 = kpis[3].querySelector(".k-label");
        var k4_sub = kpis[3].querySelector(".k-sub");
        if (k4) k4.innerHTML = '<i data-lucide="users"></i> ' + (currentLang === "en" ? "Total Platform Users" : "মোট প্ল্যাটফর্ম ব্যবহারকারী");
        if (k4_sub) k4_sub.textContent = currentLang === "en" ? "Citizen and advocate accounts" : "নাগরিক ও আইনজীবী একাউন্ট";
      }

      var triageTitle = document.querySelector(".card h2 i[data-lucide='award']");
      if (triageTitle && triageTitle.parentElement) {
        triageTitle.parentElement.innerHTML = '<i data-lucide="award" style="display:inline;vertical-align:middle;color:var(--gold);"></i> ' + (currentLang === "en" ? "Advocate Bar Verification Triage (FR-17)" : "আইনজীবী সনদ যাচাই ট্রায়াজ (FR-17)");
      }

      var triageThs = document.querySelectorAll("table thead th");
      if (triageThs.length >= 4) {
        triageThs[0].textContent = currentLang === "en" ? "Applicant Name" : "আবেদনকারীর নাম";
        triageThs[1].textContent = currentLang === "en" ? "Bar Reg No" : "বার রেজিস্ট্রেশন নং";
        triageThs[2].textContent = currentLang === "en" ? "Date" : "তারিখ";
        triageThs[3].textContent = currentLang === "en" ? "Decision" : "সিদ্ধান্ত";
      }

      var auditHead = document.querySelector(".card h2 i[data-lucide='history']");
      if (auditHead && auditHead.parentElement) {
        auditHead.parentElement.innerHTML = '<i data-lucide="history" style="display:inline;vertical-align:middle;color:var(--gold);"></i> ' + (currentLang === "en" ? "Recent System Audit & Security Logs" : "সাম্প্রতিক সিস্টেম অডিট ও সিকিউরিটি লগ");
      }

    } else if (path.indexOf("/admin/analytics") !== -1) {
      // Admin Analytics Page
      var kicker = document.querySelector(".page-head .kicker");
      if (kicker) kicker.innerHTML = '<i data-lucide="bar-chart-3"></i> ' + (currentLang === "en" ? "FR-16 · Anonymized Analytics & Observability" : "FR-16 · Anonymized Analytics & Observability");
      var title = document.querySelector(".page-head .page-title");
      if (title) title.textContent = currentLang === "en" ? "System Analytics & Impact Report" : "সিস্টেম অ্যানালিটিক্স ও কার্যক্ষমতা রিপোর্ট";
      var sub = document.querySelector(".page-head .page-sub");
      if (sub) sub.textContent = currentLang === "en" ? "Platform utilization, geographic legal demand, AI processing latency, and lawyer review turnaround metrics." : "প্ল্যাটফর্ম ব্যবহার, আইনি বিভাগের ভৌগোলিক বণ্টন, এআই প্রসেসিং লেটেন্সি ও আইনজীবী রিভিউ টার্নঅ্যারাউন্ড মেট্রিক্স।";

      var kpis = document.querySelectorAll(".grid-4 .kpi");
      if (kpis.length >= 4) {
        var k1 = kpis[0].querySelector(".k-label");
        var k1_sub = kpis[0].querySelector(".k-sub");
        if (k1) k1.innerHTML = '<i data-lucide="folder-check"></i> ' + (currentLang === "en" ? "Total Resolved Cases" : "সর্বমোট সমাধানকৃত মামলা");
        if (k1_sub) k1_sub.textContent = currentLang === "en" ? "New cases this week" : "এই সপ্তাহে নতুন";

        var k2 = kpis[1].querySelector(".k-label");
        var k2_num = kpis[1].querySelector(".k-num");
        var k2_sub = kpis[1].querySelector(".k-sub");
        if (k2) k2.innerHTML = '<i data-lucide="clock"></i> ' + (currentLang === "en" ? "Avg Lawyer Review Time" : "গড় আইনজীবী রিভিউ সময়");
        if (k2_num) k2_num.textContent = currentLang === "en" ? "3.4 Hours" : "৩.৪ ঘণ্টা";
        if (k2_sub) k2_sub.textContent = currentLang === "en" ? "Target: < 6 hours" : "টার্গেট: < ৬ ঘণ্টা";

        var k3 = kpis[2].querySelector(".k-label");
        var k3_sub = kpis[2].querySelector(".k-sub");
        if (k3) k3.innerHTML = '<i data-lucide="cpu"></i> ' + (currentLang === "en" ? "AI Calls Today" : "AI কল ভলিউম (আজ)");
        if (k3_sub) k3_sub.textContent = currentLang === "en" ? "Failure rate: 2.1%" : "ব্যর্থতার হার: ২.১%";

        var k4 = kpis[3].querySelector(".k-label");
        var k4_sub = kpis[3].querySelector(".k-sub");
        if (k4) k4.innerHTML = '<i data-lucide="zap"></i> ' + (currentLang === "en" ? "Avg RAG Response Latency" : "গড় RAG রেসপন্স লেটেন্সি");
        if (k4_sub) k4_sub.textContent = currentLang === "en" ? "Google Gemini + Qdrant" : "Google Gemini + Qdrant";
      }

      var chartH1 = document.querySelector(".card h3");
      if (chartH1 && (chartH1.textContent.indexOf("মামলা বিভাজন") !== -1 || chartH1.textContent.indexOf("Case Share") !== -1)) {
        chartH1.textContent = currentLang === "en" ? "Case Distribution by Legal Category (Case Share)" : "আইনি বিষয়ভিত্তিক মামলা বিভাজন (Case Share)";
      }

    } else if (path.indexOf("/account/profile") !== -1) {
      // User Profile Page
      var crumbProfile = document.querySelector(".breadcrumbs span:last-child");
      if (crumbProfile) crumbProfile.textContent = currentLang === "en" ? "Account Profile" : "অ্যাকাউন্ট প্রোফাইল";

      var formTitle = document.querySelector(".card h2 i[data-lucide='user-cog']");
      if (formTitle && formTitle.parentElement) {
        formTitle.parentElement.innerHTML = '<i data-lucide="user-cog" style="display:inline;vertical-align:middle;color:var(--gold);"></i> ' + (currentLang === "en" ? "Profile & Account Details" : "প্রোফাইল ও অ্যাকাউন্ট তথ্য");
      }

      var passTitle = document.querySelector(".card h2 i[data-lucide='key-round']");
      if (passTitle && passTitle.parentElement) {
        passTitle.parentElement.innerHTML = '<i data-lucide="key-round" style="display:inline;vertical-align:middle;color:var(--gold);"></i> ' + (currentLang === "en" ? "Security & Password" : "পাসওয়ার্ড ও নিরাপত্তা");
      }

    } else if (path.indexOf("/account/login") !== -1) {
      // Login Page
      var kicker = document.querySelector(".auth-card .page-head .kicker");
      if (kicker) kicker.innerHTML = '<i data-lucide="log-in"></i> ' + (currentLang === "en" ? "Authentication" : "নিরাপদ লগইন");
      var title = document.querySelector(".auth-card .page-head .page-title");
      if (title) title.textContent = dict["login-title"];
      var sub = document.querySelector(".auth-card .page-head .page-sub");
      if (sub) sub.textContent = dict["login-sub"];

      var sideKicker = document.querySelector(".auth-side .kicker");
      if (sideKicker) sideKicker.innerHTML = '<i data-lucide="shield-check"></i> ' + dict["login-kicker"];
      var sideH = document.querySelector(".auth-side h2");
      if (sideH) sideH.textContent = dict["login-sidebar-h"];
      var sideList = document.querySelectorAll(".auth-side ul li span");
      if (sideList.length >= 4) {
        sideList[0].textContent = dict["login-sidebar-1"];
        sideList[1].textContent = dict["login-sidebar-2"];
        sideList[2].textContent = dict["login-sidebar-3"];
        sideList[3].textContent = dict["login-sidebar-4"];
      }

      var sideFoot = document.querySelector(".auth-side .tiny.muted");
      if (sideFoot) sideFoot.textContent = currentLang === "en" ? "© 2026 MuktoAin · Not formal legal advice" : "© ২০২৬ মুক্ত আইন · আনুষ্ঠানিক আইনি পরামর্শ নয়";

      var lblEmail = document.querySelector('label[for="Email"], label[asp-for="Email"]');
      if (lblEmail) lblEmail.textContent = dict["login-email-label"];
      var lblPass = document.querySelector('label[for="Password"], label[asp-for="Password"]');
      if (lblPass) lblPass.textContent = dict["login-pass-label"];

      var forgotLink = document.querySelector('.auth-card a[href*="Forgot"], .auth-card a[href*="forgot"], .auth-card a.tiny.muted');
      if (forgotLink && (forgotLink.textContent.indexOf("পাসওয়ার্ড") !== -1 || forgotLink.textContent.indexOf("Password") !== -1)) {
        forgotLink.textContent = dict["login-forgot"];
      }

      var rememberLabel = document.querySelector('.check-row span');
      if (rememberLabel) rememberLabel.textContent = dict["login-remember"];

      var loginBtn = document.querySelector('form button[type="submit"]');
      if (loginBtn) loginBtn.innerHTML = '<i data-lucide="log-in"></i> ' + dict["login-btn"];

      var demoQuickTitle = document.querySelector(".demo-quick-title");
      if (demoQuickTitle) {
        demoQuickTitle.textContent = currentLang === "en" ? "Demo Accounts · 1-Click Autofill" : "ডেমো একাউন্ট টেস্ট করুন · ১-ক্লিকে পূরণ";
      }
      var demoCitizen = document.querySelector(".demo-role-citizen");
      if (demoCitizen) demoCitizen.textContent = currentLang === "en" ? "Citizen" : "নাগরিক";
      var demoLawyer = document.querySelector(".demo-role-lawyer");
      if (demoLawyer) demoLawyer.textContent = currentLang === "en" ? "Lawyer" : "আইনজীবী";
      var demoAdmin = document.querySelector(".demo-role-admin");
      if (demoAdmin) demoAdmin.textContent = currentLang === "en" ? "Admin" : "অ্যাডমিন";

      var regPrompts = document.querySelectorAll('.auth-card .text-center, .auth-card p.muted.tiny, .auth-card p:last-of-type');
      regPrompts.forEach(function (el) {
        if (el.textContent.indexOf("ব্যবহারকারী") !== -1 || el.textContent.indexOf("user") !== -1 || el.textContent.indexOf("নিবন্ধন") !== -1 || el.textContent.indexOf("Register") !== -1) {
          el.innerHTML = dict["login-noacc"] + ' <a href="/Account/Register" style="font-weight: 700;">' + dict["login-reglink"] + ' →</a>';
        }
      });

    } else if (path.indexOf("/account/register") !== -1) {
      // Register Page
      var regKicker = document.querySelector(".auth-card .page-head .kicker");
      if (regKicker) regKicker.innerHTML = '<i data-lucide="sparkles"></i> ' + (currentLang === "en" ? "Get Started" : "শুরু করুন");
      var regTitle = document.querySelector(".auth-card .page-head .page-title");
      if (regTitle) regTitle.textContent = currentLang === "en" ? "Create New Account" : "নতুন একাউন্ট তৈরি করুন";
      var regSub = document.querySelector(".auth-card .page-head .page-sub");
      if (regSub) regSub.textContent = currentLang === "en" ? "Enter your information to access legal aid or join as an advocate" : "আইনি অধিকার জানতে বা আইনজীবী হিসেবে যুক্ত হতে তথ্য দিন";

      var regSideKicker = document.querySelector(".auth-side .kicker");
      if (regSideKicker) regSideKicker.innerHTML = '<i data-lucide="user-plus"></i> ' + (currentLang === "en" ? "Join MuktoAin" : "মুক্ত আইনে যোগ দিন");
      var regSideH = document.querySelector(".auth-side h2");
      if (regSideH) regSideH.textContent = currentLang === "en" ? "Connecting Citizens & Legal Experts" : "নাগরিক ও আইনজীবীদের মেলবন্ধন";

      var regSideList = document.querySelectorAll(".auth-side ul li span");
      if (regSideList.length >= 3) {
        regSideList[0].innerHTML = currentLang === "en"
          ? "<b>As a Citizen:</b> Instant legal analysis and structured document drafting."
          : "<b>নাগরিক হিসেবে:</b> আইনি সমস্যার তাৎক্ষণিক উত্তর জানুন এবং আবেদন দলিলের নির্ভুল খসড়া তৈরি করুন।";
        regSideList[1].innerHTML = currentLang === "en"
          ? "<b>As a Lawyer:</b> Review pro-bono & legal-aid drafts upon Bar verification."
          : "<b>আইনজীবী হিসেবে:</b> বার নম্বর যাচাইকরণ শেষে প্রো-বোনো ও লিগ্যাল এইড খসড়া পর্যালোচনায় অবদান রাখুন।";
        regSideList[2].innerHTML = currentLang === "en"
          ? "<b>Completely Free & Secure:</b> Modern field-level encryption and citizen privacy protection."
          : "<b>সম্পূর্ণ উন্মুক্ত ও নিরাপদ:</b> আধুনিক এনক্রিপশন ও গোপনীয়তা সুরক্ষার প্রতিশ্রুতি।";
      }

      var roleMainLbl = document.querySelector(".auth-card .field > label:first-child");
      if (roleMainLbl) roleMainLbl.textContent = currentLang === "en" ? "Select Your Role" : "আপনার ভূমিকা নির্বাচন করুন";

      var roleCards = document.querySelectorAll(".role-card");
      if (roleCards.length >= 2) {
        var r1_b = roleCards[0].querySelector("b");
        var r1_sm = roleCards[0].querySelector("small");
        if (r1_b) r1_b.textContent = currentLang === "en" ? "Citizen" : "নাগরিক";
        if (r1_sm) r1_sm.textContent = currentLang === "en" ? "Legal aid & document drafting" : "আইনি পরামর্শ ও সহায়তা";

        var r2_b = roleCards[1].querySelector("b");
        var r2_sm = roleCards[1].querySelector("small");
        if (r2_b) r2_b.textContent = currentLang === "en" ? "Lawyer" : "আইনজীবী";
        if (r2_sm) r2_sm.textContent = currentLang === "en" ? "Review & verify drafts" : "দলিল পর্যালোচনা ও যাচাই";
      }

      var inpName = document.querySelector('input[name="FullName"]');
      if (inpName) inpName.placeholder = currentLang === "en" ? "e.g., Md. Rafiqul Islam" : "e.g., মোঃ রফিকুল ইসলাম";
      var inpPhone = document.querySelector('input[name="PhoneNumber"]');
      if (inpPhone) inpPhone.placeholder = currentLang === "en" ? "01712-XXXXXX" : "০১৭১২-XXXXXX";
      var inpPass = document.querySelector('input[name="Password"]');
      if (inpPass) inpPass.placeholder = currentLang === "en" ? "At least 6 characters" : "কমপক্ষে ৬ অক্ষর";
      var inpConf = document.querySelector('input[name="ConfirmPassword"]');
      if (inpConf) inpConf.placeholder = currentLang === "en" ? "Re-enter password" : "পুনরায় পাসওয়ার্ড লিখুন";
      var inpBar = document.querySelector('input[name="BarRegistrationNumber"]');
      if (inpBar) inpBar.placeholder = currentLang === "en" ? "e.g., DHA-1234 or Bar Certificate No." : "e.g., DHA-1234 বা বার সনদ নম্বর";

      var barHint = document.querySelector('#lawyerBarSection p.tiny.muted');
      if (barHint) barHint.textContent = currentLang === "en"
        ? "For lawyer accounts, review queue access will be activated upon bar number verification by an admin."
        : "আইনজীবী একাউন্টের ক্ষেত্রে অ্যাডমিন কর্তৃক বার নম্বর যাচাইয়ের পর রিভিউ কিউতে প্রবেশাধিকার সক্রিয় হবে।";

      var regSubmitBtn = document.querySelector('.auth-card form button[type="submit"]');
      if (regSubmitBtn) {
        regSubmitBtn.innerHTML = '<i data-lucide="check-circle"></i> ' + (currentLang === "en" ? "Create Account →" : "নিবন্ধন সম্পন্ন করুন →");
      }

      var regLoginPrompt = document.querySelector('.auth-card .text-center');
      if (regLoginPrompt) {
        regLoginPrompt.innerHTML = (currentLang === "en" ? "Already have an account?" : "ইতিমধ্যে একাউন্ট আছে?") + ' <a href="/Account/Login" style="font-weight: 700;">' + (currentLang === "en" ? "Sign In Here →" : "এখানে সাইন ইন করুন →") + '</a>';
      }

    } else if (path.indexOf("/home/about") !== -1) {
      // About Page
      var kicker = document.querySelector(".page-head .kicker");
      if (kicker) kicker.innerHTML = '<i data-lucide="scale" aria-hidden="true"></i> ' + dict["about-kicker"];
      var title = document.querySelector(".page-head .page-title");
      if (title) title.textContent = dict["about-title"];
      var sub = document.querySelector(".page-head .page-sub");
      if (sub) sub.textContent = dict["about-sub"];

      var aboutPs = document.querySelectorAll(".stack > p");
      if (aboutPs.length >= 2) {
        aboutPs[0].innerHTML = currentLang === "en"
          ? "<b>MuktoAin</b> is an open and free AI-augmented legal information platform. When you describe your dispute in plain Bengali, English, or Banglish, we instantly retrieve statutory sections from the Bangladesh Code and explain your rights in accessible terms, while auto-drafting official legal documents (GD, RTI applications, Labour complaints)."
          : "<b>মুক্ত আইন (MuktoAin)</b> একটি উন্মুক্ত ও বিনামূল্যের AI-সহায়ক আইনি তথ্যসেবা প্ল্যাটফর্ম। আপনি সাধারণ বাংলা, English বা Banglish-এ নিজের সমস্যার কথা জানালে আমরা সরকারি সংকলিত আইনসমূহ (Bangladesh Code) থেকে তাৎক্ষণিকভাবে প্রাসঙ্গিক ধারা খুঁজে এনে আপনার অধিকার সহজ ভাষায় ব্যাখ্যা করি। একই সাথে থানা, আদালত বা কর্তৃপক্ষের নিকট জমা দেওয়ার উপযুক্ত আবেদনপত্র (জিডি, আরটিআই, শ্রম অভিযোগ) স্বয়ংক্রিয়ভাবে খসড়া তৈরি করি।";

        aboutPs[1].innerHTML = currentLang === "en"
          ? "<b>Human-in-the-Loop Safeguard:</b> AI can never make final legal determinations. Every draft prepared by MuktoAin must be thoroughly reviewed, verified, and certified by an advocate registered with the Bangladesh Bar Council before final official PDF download is unlocked for the citizen."
          : "<b>মানবীয় সুরক্ষা গেট (Human-in-the-Loop Safeguard):</b> AI কোনো অবস্থাতেই সরাসরি চূড়ান্ত আইনি সিদ্ধান্ত নিতে পারে না। তাই মুক্ত আইনে প্রস্তুতকৃত প্রতিটি খসড়া বাংলাদেশ বার কাউন্সিলে নিবন্ধিত এবং আমাদের অ্যাডমিন কর্তৃক যাচাইকৃত একজন অভিজ্ঞ আইনজীবী পুঙ্খানুপুঙ্খভাবে পর্যালোচনা ও অনুমোদন করার পরেই কেবল নাগরিকের জন্য চূড়ান্ত PDF ডাউনলোড উন্মুক্ত করা হয়।";
      }

      var sectionHs = document.querySelectorAll(".section-h");
      if (sectionHs.length >= 4) {
        sectionHs[0].innerHTML = '<i data-lucide="workflow"></i> ' + (currentLang === "en" ? "How It Works" : "কার্যপ্রণালী · How It Works");
        sectionHs[1].innerHTML = '<i data-lucide="help-circle"></i> ' + (currentLang === "en" ? "Frequently Asked Questions (FAQ)" : "সাধারণ জিজ্ঞাসা (FAQ)");
        sectionHs[2].innerHTML = '<i data-lucide="shield-alert"></i> ' + (currentLang === "en" ? "Full Legal Disclaimer (FR-11)" : "পূর্ণাঙ্গ আইনি দাবিত্যাগ");
        sectionHs[3].innerHTML = '<i data-lucide="database"></i> ' + (currentLang === "en" ? "Dataset & Technology Attribution" : "ডেটাসেট ও প্রযুক্তি কৃতজ্ঞতা");
      }

      var aboutSteps = document.querySelectorAll(".grid-3 .card");
      if (aboutSteps.length >= 3) {
        var s1_num = aboutSteps[0].querySelector(".step-num");
        var s1_b = aboutSteps[0].querySelector("b");
        var s1_p = aboutSteps[0].querySelector("p");
        if (s1_num) s1_num.textContent = currentLang === "en" ? "1" : "১";
        if (s1_b) s1_b.textContent = currentLang === "en" ? "Describe Your Issue" : "সমস্যা বলুন";
        if (s1_p) s1_p.textContent = currentLang === "en" ? "Describe your complaint via chat or intake form — with full support for anonymous submissions with private tracking GUIDs." : "চ্যাটে বা ফর্মে সাধারণ ভাষায় আপনার অভিযোগ বা আইনি ঘটনাটি লিখুন — চাইলে সম্পূর্ণ বেনামে (Anonymous) ট্র্যাকিং কোড দিয়ে জমা দিতে পারেন।";

        var s2_num = aboutSteps[1].querySelector(".step-num");
        var s2_b = aboutSteps[1].querySelector("b");
        var s2_p = aboutSteps[1].querySelector("p");
        if (s2_num) s2_num.textContent = currentLang === "en" ? "2" : "২";
        if (s2_b) s2_b.textContent = currentLang === "en" ? "Understand Your Rights" : "আইনি অধিকার জানুন";
        if (s2_p) s2_p.textContent = currentLang === "en" ? "Statutory sections and sub-sections are retrieved via vector search to explain legal rights and remedies in plain language." : "ভেক্টর সার্চের মাধ্যমে সুনির্দিষ্ট ধারা ও উপধারা উদ্ধার করে আপনার অধিকার, করণীয় ও আইনি সীমাবদ্ধতা সহজ বাংলায় তুলে ধরা হয়।";

        var s3_num = aboutSteps[2].querySelector(".step-num");
        var s3_b = aboutSteps[2].querySelector("b");
        var s3_p = aboutSteps[2].querySelector("p");
        if (s3_num) s3_num.textContent = currentLang === "en" ? "3" : "৩";
        if (s3_b) s3_b.textContent = currentLang === "en" ? "Verified Document Download" : "যাচাইকৃত দলিল সংগ্রহ";
        if (s3_p) s3_p.textContent = currentLang === "en" ? "AI creates the formal legal draft; a verified advocate reviews and certifies it before unlocking the final PDF download." : "AI আবেদনের আনুষ্ঠানিক খসড়া তৈরি করে; আইনজীবী ধারা মিলিয়ে সত্যায়ন ও সংশোধন করার পর নিখুঁত অফিসিয়াল ফরম্যাটে PDF ডাউনলোড করুন।";
      }

      var faqs = document.querySelectorAll("#faq .acc");
      if (faqs.length >= 5) {
        var f1_s = faqs[0].querySelector("summary");
        var f1_b = faqs[0].querySelector(".acc-body");
        if (f1_s) f1_s.innerHTML = (currentLang === "en" ? "Is MuktoAin completely free for everyone?" : "মুক্ত আইন কি সাধারণ মানুষের জন্য সম্পূর্ণ বিনামূল্যে?") + '<i data-lucide="chevron-down" class="chev"></i>';
        if (f1_b) {
          f1_b.innerHTML = currentLang === "en"
            ? "Yes, MuktoAin is a 100% free and open non-commercial academic initiative. No subscription fees or financial charges are levied on citizens or advocates.<div class=\"tiny\" style=\"margin-top:6px;\"><em>MuktoAin is 100% free and open for academic & public legal empowerment. No ads or subscription fees.</em></div>"
            : "হ্যাঁ, মুক্ত আইন সম্পূর্ণ অবাণিজ্যিক ও বিনামূল্যে ব্যবহারযোগ্য একটি একাডেমিক উদ্ভাবন। নাগরিক বা আইনজীবী কারো কাছ থেকেই কোনো প্রকার সাবস্ক্রিপশন ফি বা আর্থিক চার্জ নেওয়া হয় না。<div class=\"tiny\" style=\"margin-top:6px;\"><em>MuktoAin is 100% free and open for academic & public legal empowerment. No ads or subscription fees.</em></div>";
        }

        var f2_s = faqs[1].querySelector("summary");
        var f2_b = faqs[1].querySelector(".acc-body");
        if (f2_s) f2_s.innerHTML = (currentLang === "en" ? "Can AI responses be used directly in court as legal advice?" : "প্ল্যাটফর্মের প্রদত্ত উত্তর কি আইনি পরামর্শ হিসেবে আদালতে উপস্থাপন করা যাবে?") + '<i data-lucide="chevron-down" class="chev"></i>';
        if (f2_b) {
          f2_b.innerHTML = currentLang === "en"
            ? "No. MuktoAin provides general legal informational guidance and statutory references. It is not a law firm or official legal counsel. Always consult a qualified advocate of the Bangladesh Bar Council directly before court litigation.<div class=\"tiny\" style=\"margin-top:6px;\"><em>Provides general legal informational guidance, not formal advocate representation.</em></div>"
            : "না। মুক্ত আইন কেবল সাধারণ আইনি তথ্য ও সংশ্লিষ্ট ধারার সন্ধান দেয়। এটি কোনো আইন ফার্ম বা আনুষ্ঠানিক পরামর্শদাতা নয়। যেকোনো আদালতের মামলা পরিচালনার পূর্বে অবশ্যই সরাসরি একজন অভিজ্ঞ আইনজীবীর পরামর্শ নেওয়া আবশ্যক。<div class=\"tiny\" style=\"margin-top:6px;\"><em>Provides general legal informational guidance, not formal advocate representation.</em></div>";
        }

        var f3_s = faqs[2].querySelector("summary");
        var f3_b = faqs[2].querySelector(".acc-body");
        if (f3_s) f3_s.innerHTML = (currentLang === "en" ? "Who reviews and certifies the legal drafts?" : "খসড়া দলিলগুলো কারা এবং কীভাবে পর্যালোচনা করেন?") + '<i data-lucide="chevron-down" class="chev"></i>';
        if (f3_b) {
          f3_b.innerHTML = currentLang === "en"
            ? "Advocates registered with the Bangladesh Bar Council who have verified their Bar registration certificates on our portal review these drafts. They inspect the accuracy of statutory sections, factual clarity, and approve or edit the document before final release."
            : "বাংলাদেশ বার কাউন্সিলে নিবন্ধিত যেসকল আইনজীবী আমাদের পোর্টালে তাদের বার সনদ যাচাই করিয়েছেন, তারা এই খসড়াগুলো পর্যালোচনা করেন। আইনের ধারা ঠিক আছে কিনা, ঘটনার বিবরণ পরিষ্কার কিনা তা দেখে আইনজীবী খসড়া অনুমোদন বা প্রয়োজনীয় সম্পাদনা করেন।";
        }

        var f4_s = faqs[3].querySelector("summary");
        var f4_b = faqs[3].querySelector(".acc-body");
        if (f4_s) f4_s.innerHTML = (currentLang === "en" ? "How is citizen privacy and anonymity protected?" : "আমার গোপনীয়তা ও ব্যক্তিগত তথ্যের নিরাপত্তা কীভাবে নিশ্চিত হয়?") + '<i data-lucide="chevron-down" class="chev"></i>';
        if (f4_b) {
          f4_b.innerHTML = currentLang === "en"
            ? "You can submit complaints completely anonymously without providing your name or phone number. Each submission receives a unique cryptographic tracking code allowing only you to view results and drafts."
            : "আপনি নাম, মোবাইল বা পরিচয় গোপন রেখে বেনামে (Anonymous) মামলা জমা দিতে পারেন। প্রতিটি মামলার জন্য ইউনিক ক্রিপ্টোগ্রাফিক ট্র্যাকিং কোড দেওয়া হয় যার মাধ্যমে কেবল আপনি ফলাফল দেখতে পারবেন।";
        }

        var f5_s = faqs[4].querySelector("summary");
        var f5_b = faqs[4].querySelector(".acc-body");
        if (f5_s) f5_s.innerHTML = (currentLang === "en" ? "Which laws and statutes are currently indexed?" : "বর্তমানে কোন কোন আইন অন্তর্ভুক্ত রয়েছে?") + '<i data-lucide="chevron-down" class="chev"></i>';
        if (f5_b) {
          f5_b.innerHTML = currentLang === "en"
            ? "Over 1,484 full Bangladesh Acts and 35,000+ legal sections from the official Bangladesh Code are indexed, including the Labour Act 2006, Penal Code 1860, RTI Act 2009, and Consumer Rights Protection Act 2009."
            : "বাংলাদেশ কোড (Bangladesh Code) থেকে শ্রম আইন ২০০৬, দণ্ডবিধি ১৮৬০, তথ্য অধিকার আইন ২০০৯, ভোক্তা অধিকার সংরক্ষণ আইন ২০০৯-সহ গুরুত্বপূর্ণ ১,৪৮৪টি পূর্ণাঙ্গ আইন এবং ৩৫,০০০+ ধারা ইনডেক্স করা রয়েছে।";
        }
      }
    }

    // 8. Footer section links & headers
    var footerBrandP = document.querySelector(".footer-brand p");
    if (footerBrandP) footerBrandP.textContent = dict["footer-tagline"];

    var footerHeaders = document.querySelectorAll(".footer-grid h4");
    if (footerHeaders.length >= 3) {
      footerHeaders[0].textContent = dict["footer-nav-h"];
      footerHeaders[1].textContent = dict["footer-legal-h"];
      footerHeaders[2].textContent = dict["footer-roles-h"];
    }

    var footNavLinks = document.querySelectorAll(".footer-grid > div:nth-child(2) ul li a");
    if (footNavLinks.length >= 5) {
      footNavLinks[0].textContent = dict["footer-nav-1"];
      footNavLinks[1].textContent = dict["footer-nav-2"];
      footNavLinks[2].textContent = dict["footer-nav-3"];
      footNavLinks[3].textContent = dict["footer-nav-4"];
      footNavLinks[4].textContent = dict["footer-nav-5"];
    }

    var footLegLinks = document.querySelectorAll(".footer-grid > div:nth-child(3) ul li a");
    if (footLegLinks.length >= 3) {
      footLegLinks[0].textContent = dict["footer-leg-1"];
      footLegLinks[1].textContent = dict["footer-leg-2"];
      footLegLinks[2].textContent = dict["footer-leg-3"];
    }

    var footRoleLinks = document.querySelectorAll(".footer-grid > div:nth-child(4) ul li a");
    if (footRoleLinks.length >= 4) {
      footRoleLinks[0].textContent = dict["footer-role-1"];
      footRoleLinks[1].textContent = dict["footer-role-2"];
      footRoleLinks[2].textContent = dict["footer-role-3"];
      footRoleLinks[3].textContent = dict["footer-role-4"];
    }

    var footerBottom = document.querySelector(".footer-bottom");
    if (footerBottom) footerBottom.textContent = dict["footer-copyright"];

    // 9. Slashed Labels (e.g. "পূর্ণ নাম / Full Name", "ইমেইল / Email", "পাসওয়ার্ড / Password")
    document.querySelectorAll("label").forEach(function(lbl) {
      if (lbl.children.length === 0) {
        if (!lbl.dataset.original && lbl.textContent.indexOf("/") !== -1) {
          lbl.dataset.original = lbl.textContent;
        }
        if (lbl.dataset.original) {
          var parts = lbl.dataset.original.split("/");
          if (parts.length >= 2) {
            lbl.textContent = currentLang === "en" ? parts[1].trim() : parts[0].trim();
          }
        }
      }
    });

    renderIcons();
    window.dispatchEvent(new CustomEvent("languagechange", { detail: { lang: currentLang } }));
  }

  document.addEventListener("DOMContentLoaded", function () {
    /* theme toggle buttons */
    document.querySelectorAll(".theme-toggle").forEach(function (b) {
      b.setAttribute("aria-label", b.getAttribute("aria-label") || "Toggle theme");
      if (!b.innerHTML.trim()) b.innerHTML = icon(theme === "dark" ? "sun" : "moon");
      b.addEventListener("click", function () {
        setTheme(document.documentElement.dataset.theme === "dark" ? "light" : "dark");
      });
    });

    /* language toggle (active switching) */
    document.querySelectorAll(".lang-toggle").forEach(function (group) {
      group.querySelectorAll("button").forEach(function (btn) {
        var btnLang = btn.getAttribute("data-lang") || (btn.textContent.trim().toLowerCase().indexOf("en") !== -1 ? "en" : "bn");
        btn.setAttribute("data-lang", btnLang);
        btn.classList.toggle("active", btnLang === currentLang);

        btn.addEventListener("click", function () {
          applyLanguage(btnLang);
        });
      });
    });

    // Apply language on initial load
    applyLanguage(currentLang);

    /* filter chips */
    document.querySelectorAll("[data-chip-group]").forEach(function (group) {
      group.querySelectorAll(".chip").forEach(function (chip) {
        chip.addEventListener("click", function () {
          if (group.hasAttribute("data-chip-multi")) {
            chip.classList.toggle("active");
          } else {
            group.querySelectorAll(".chip").forEach(function (c) { c.classList.remove("active"); });
            chip.classList.add("active");
          }
          var ev = new CustomEvent("chipchange", { detail: chip, bubbles: true });
          group.dispatchEvent(ev);
        });
      });
    });

    /* underline tabs */
    document.querySelectorAll("[data-tabs]").forEach(function (tabsEl) {
      var buttons = tabsEl.querySelectorAll("button");
      buttons.forEach(function (btn) {
        btn.addEventListener("click", function () {
          buttons.forEach(function (b) { b.classList.remove("active"); });
          btn.classList.add("active");
          var scope = document.querySelector(tabsEl.dataset.tabs) || document;
          scope.querySelectorAll(":scope > .tab-panel, :scope .tab-panel").forEach(function (p) {
            p.classList.toggle("active", p.id === btn.dataset.panel);
          });
          renderIcons();
        });
      });
    });

    /* mobile drawer */
    var drawer = document.getElementById("drawer");
    var backdrop = document.getElementById("drawer-backdrop");
    function closeDrawer() {
      if (!drawer) return;
      drawer.classList.remove("open");
      if (backdrop) backdrop.classList.remove("open");
      var burger = document.querySelector(".nav-burger");
      if (burger) burger.setAttribute("aria-expanded", "false");
    }
    document.querySelectorAll(".nav-burger").forEach(function (b) {
      b.addEventListener("click", function () {
        drawer.classList.add("open");
        if (backdrop) backdrop.classList.open ? backdrop.classList.remove("open") : backdrop.classList.add("open");
        b.setAttribute("aria-expanded", "true");
      });
    });
    if (backdrop) backdrop.addEventListener("click", closeDrawer);
    document.querySelectorAll("[data-close-drawer]").forEach(function (b) {
      b.addEventListener("click", closeDrawer);
    });
    document.addEventListener("keydown", function (e) {
      if (e.key === "Escape") { closeDrawer(); closeAllModals(); closePops(); }
    });

    /* avatar / popover menus */
    document.querySelectorAll("[data-pop]").forEach(function (trigger) {
      trigger.addEventListener("click", function (e) {
        e.stopPropagation();
        var pop = document.getElementById(trigger.dataset.pop);
        var isOpen = pop && pop.classList.contains("open");
        closePops();
        if (pop && !isOpen) pop.classList.add("open");
      });
    });
    function closePops() {
      document.querySelectorAll(".menu-pop.open").forEach(function (p) { p.classList.remove("open"); });
    }
    document.addEventListener("click", function (e) {
      if (!e.target.closest(".pop-wrap")) closePops();
    });

    /* modals & bottom sheets */
    document.querySelectorAll("[data-open-modal]").forEach(function (t) {
      t.addEventListener("click", function (e) {
        e.preventDefault();
        var m = document.querySelector(t.dataset.openModal);
        if (m) { m.classList.add("open"); var f = m.querySelector("input,textarea,select,button"); if (f) f.focus({ preventScroll: true }); }
      });
    });
    document.querySelectorAll("[data-close-modal]").forEach(function (t) {
      t.addEventListener("click", function () {
        var bd = t.closest(".modal-backdrop");
        if (bd) bd.classList.remove("open");
      });
    });
    document.querySelectorAll(".modal-backdrop").forEach(function (bd) {
      bd.addEventListener("click", function (e) {
        if (e.target === bd) bd.classList.remove("open");
      });
    });
    function closeAllModals() {
      document.querySelectorAll(".modal-backdrop.open").forEach(function (m) { m.classList.remove("open"); });
    }

    /* toast: showToast(msg, type) global */
    window.showToast = function (msg, type) {
      var old = document.querySelector(".toast");
      if (old) old.remove();
      var el = document.createElement("div");
      el.className = "toast " + (type || "");
      el.innerHTML = icon(type === "error" ? "alert-circle" : "check-circle-2") + "<span></span>";
      el.querySelector("span").textContent = msg;
      document.body.appendChild(el);
      renderIcons(el);
      setTimeout(function () { el.remove(); }, 4000);
    };

    /* copy-to-clipboard buttons [data-copy] */
    document.querySelectorAll("[data-copy]").forEach(function (btn) {
      btn.addEventListener("click", function () {
        var text = btn.dataset.copy || "";
        function done() {
          var msg = currentLang === "en"
            ? (btn.dataset.copyMsgEn || btn.dataset.copyMsg || "Copied ✓")
            : (btn.dataset.copyMsgBn || btn.dataset.copyMsg || "কপি হয়েছে ✓");
          window.showToast(msg);
        }
        if (navigator.clipboard && navigator.clipboard.writeText) {
          navigator.clipboard.writeText(text).then(done, done);
        } else { done(); }
      });
    });

    /* char counters: textarea[data-counter="#id"] */
    document.querySelectorAll("textarea[data-counter]").forEach(function (ta) {
      var counter = document.querySelector(ta.dataset.counter);
      if (!counter) return;
      var max = parseInt(ta.getAttribute("maxlength") || "5000", 10);
      function update() {
        var n = ta.value.length;
        counter.textContent = currentLang === "en"
          ? n.toLocaleString("en-US") + " / " + max.toLocaleString("en-US")
          : n.toLocaleString("bn-BD") + " / " + max.toLocaleString("bn-BD");
        counter.classList.toggle("warn", n > max * 0.9);
      }
      ta.addEventListener("input", update);
      update();
    });

    /* demo confirm dialogs [data-confirm] */
    document.querySelectorAll("[data-confirm]").forEach(function (el) {
      el.addEventListener("click", function (e) {
        if (!window.confirm(el.dataset.confirm)) e.preventDefault();
      });
    });

    /* composer autogrow */
    document.querySelectorAll(".composer textarea, textarea.autogrow").forEach(function (ta) {
      ta.addEventListener("input", function () {
        ta.style.height = "auto";
        ta.style.height = Math.min(ta.scrollHeight, 130) + "px";
      });
    });

    /* chat mode switch (Ask vs Search) */
    var composerMode = document.getElementById("composer-mode");
    if (composerMode) {
      composerMode.addEventListener("chipchange", function (e) {
        var ta = document.querySelector(".composer textarea");
        if (!ta) return;
        var dict = translations[currentLang];
        var isSearch = e.detail.textContent.indexOf("খুঁজ") !== -1 || e.detail.textContent.indexOf("Search") !== -1;
        ta.placeholder = isSearch
          ? dict["home-search-placeholder"]
          : dict["home-composer-placeholder"];
      });
    }

    renderIcons();
  });
})();
