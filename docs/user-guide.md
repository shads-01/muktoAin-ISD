# MuktoAin (মুক্ত আইন) — End-User Guide

> **Legal Disclaimer / আইনি দাবিত্যাগ:**
> MuktoAin provides general legal information and document drafting assistance based on Bangladeshi statutes. It does NOT provide formal legal advice, representation, or an advocate-client relationship. Every AI-generated draft must be reviewed and approved by a verified Bangladeshi advocate before it can be downloaded or used in court.
> 
> মুক্ত আইন বাংলাদেশী আইনের উপর ভিত্তি করে সাধারণ আইনি তথ্য এবং খসড়া সহায়তা প্রদান করে। এটি কোনো আনুষ্ঠানিক আইনি পরামর্শ বা প্রতিনিধিত্ব নয়। ব্যবহারের পূর্বে প্রতিটি নথি যাচাইকৃত আইনজীবী দ্বারা পর্যালোচনা ও অনুমোদন করা বাধ্যতামূলক।
> 
> The platform enforces a **strict 3-surface disclaimer policy**:
> 1. A persistent warning banner displayed across all web pages (`_DisclaimerBanner.cshtml`).
> 2. Mandatory disclaimer injection into every AI explanation and chat response (`DisclaimerInjector.cs`).
> 3. Permanent statutory disclaimers stamped on all generated document previews and finalized QuestPDF exports.

---

## Table of Contents

- [1. Introduction & Core Principles](#1-introduction--core-principles)
  - [1.1 Platform Mission](#11-platform-mission)
  - [1.2 User Roles & Access Matrix](#12-user-roles--access-matrix)
  - [1.3 Mandatory 3-Surface Safeguards](#13-mandatory-3-surface-safeguards)
  - [1.4 The Human-in-the-Loop Review Gate](#14-the-human-in-the-loop-review-gate)
- [2. Getting Started](#2-getting-started)
  - [2.1 Technical & Browser Requirements](#21-technical--browser-requirements)
  - [2.2 Accessing the Application](#22-accessing-the-application)
  - [2.3 Bilingual Interface & Language Toggle](#23-bilingual-interface--language-toggle)
  - [2.4 User Registration & Account Creation](#24-user-registration--account-creation)
  - [2.5 Authentication & Profile Management](#25-authentication--profile-management)
- [3. Citizen User Guide](#3-citizen-user-guide)
  - [3.1 Registering & Logging In as a Citizen](#31-registering--logging-in-as-a-citizen)
  - [3.2 Browsing Legal Categories](#32-browsing-legal-categories)
  - [3.3 Searching Bangladeshi Statutes](#33-searching-bangladeshi-statutes)
  - [3.4 Submitting a Legal Problem / Case](#34-submitting-a-legal-problem--case)
  - [3.5 Conversational Legal Intake Chat](#35-conversational-legal-intake-chat)
  - [3.6 Explaining Rights & Statutory Grounding](#36-explaining-rights--statutory-grounding)
  - [3.7 Generating Legal Document Drafts](#37-generating-legal-document-drafts)
  - [3.8 Submitting Drafts to the Lawyer Review Queue](#38-submitting-drafts-to-the-lawyer-review-queue)
  - [3.9 Tracking Case Status](#39-tracking-case-status)
  - [3.10 Final Legal PDF Download](#310-final-legal-pdf-download)
  - [3.11 Citizen Wallet, Top-Up & Honorarium Payments](#311-citizen-wallet-top-up--honorarium-payments)
- [4. Verified Lawyer User Guide](#4-verified-lawyer-user-guide)
  - [4.1 Applying for Lawyer Verification](#41-applying-for-lawyer-verification)
  - [4.2 Navigating the Review Queue](#42-navigating-the-review-queue)
  - [4.3 Claiming Cases & Concurrency Locking](#43-claiming-cases--concurrency-locking)
  - [4.4 Conducting Legal Document Reviews](#44-conducting-legal-document-reviews)
  - [4.5 Past Reviews & Review History](#45-past-reviews--review-history)
  - [4.6 Lawyer Earnings, Honorarium & Payout Requests](#46-lawyer-earnings-honorarium--payout-requests)
  - [4.7 Ethical Duties & Legal Representation Notice](#47-ethical-duties--legal-representation-notice)
- [5. System Administrator User Guide](#5-system-administrator-user-guide)
  - [5.1 Admin Authentication & Role Tiers](#51-admin-authentication--role-tiers)
  - [5.2 System Dashboard & Analytics](#52-system-dashboard--analytics)
  - [5.3 User Management & Security Controls](#53-user-management--security-controls)
  - [5.4 Lawyer Verification & Credential Auditing](#54-lawyer-verification--credential-auditing)
  - [5.5 Statutory Corpus Management](#55-statutory-corpus-management)
  - [5.6 Legal Categories & Curated Scenario Guidance](#56-legal-categories--curated-scenario-guidance)
  - [5.7 Financial Audits & Transaction Ledger](#57-financial-audits--transaction-ledger)
  - [5.8 AI Audit Logs & System Health](#58-ai-audit-logs--system-health)
- [6. Language & Localization Guidelines](#6-language--localization-guidelines)
  - [6.1 Multi-Script Input Support](#61-multi-script-input-support)
  - [6.2 UI Chrome Localization](#62-ui-chrome-localization)
  - [6.3 Bilingual Legal Document Templates](#63-bilingual-legal-document-templates)
- [7. Troubleshooting & Common Issues](#7-troubleshooting--common-issues)
  - [7.1 Search Returns No Statutory Sections](#71-search-returns-no-statutory-sections)
  - [7.2 AI Chat Rate Limits & Daily Quota Reached](#72-ai-chat-rate-limits--daily-quota-reached)
  - [7.3 PDF Download Button Inactive or Gated](#73-pdf-download-button-inactive-or-gated)
  - [7.4 Anonymous Tracking Code Lost](#74-anonymous-tracking-code-lost)
  - [7.5 Lawyer Application Pending Approval](#75-lawyer-application-pending-approval)
- [8. Frequently Asked Questions (FAQ)](#8-frequently-asked-questions-faq)
- [9. References & Further Reading](#9-references--further-reading)

---

## 1. Introduction & Core Principles

### 1.1 Platform Mission
**MuktoAin (মুক্ত আইন)** is an open-source, AI-augmented legal-aid platform purpose-built for the citizens of Bangladesh. Ordinary citizens frequently face insurmountable barriers when seeking justice: complex colonial legal language, lack of transparency regarding statutory rights, and prohibitive costs for preliminary document drafting.

MuktoAin bridges this justice gap by:
- Enabling citizens to express legal problems naturally in **Bangla, English, or romanized Banglish** (e.g., *"amar malik salary dicche na"*).
- Grounding AI responses exclusively in authentic Bangladeshi statutes (e.g., Bangladesh Labour Act 2006, Penal Code 1860, Code of Criminal Procedure 1898, Consumer Rights Protection Act 2009).
- Generating structured legal documents ready for police stations, government offices, or courts.
- Introducing a **mandatory human-in-the-loop review gate** where certified Bangladeshi advocates verify all drafts before citizen download.

---

### 1.2 User Roles & Access Matrix

The platform implements role-based access control (RBAC) across three primary user roles, with elevated administrative permissions:

| Capability | Guest / Anonymous | Citizen | Lawyer (Pending) | Lawyer (Verified) | Administrator | SuperAdmin |
|---|:---:|:---:|:---:|:---:|:---:|:---:|
| Search Bangladeshi Acts (`/Search`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Browse Categories (`/Category`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Conversational Chat Intake (`/Chat`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Submit Legal Case (`/Case/Submit`) | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| Track Case (`/Case/Track`) | ✅ (With GUID) | ✅ | ❌ | ❌ | ✅ | ✅ |
| View Unapproved Draft Preview | ❌ | ✅ (Watermarked) | ❌ | ❌ | ✅ | ✅ |
| Download Finalized PDF | ❌ | ✅ (Post-Approval) | ❌ | ❌ | ✅ | ✅ |
| Access Lawyer Queue (`/Lawyer/Queue`) | ❌ | ❌ | ❌ | ✅ | ❌ | ❌ |
| Claim & Review Cases | ❌ | ❌ | ❌ | ✅ | ❌ | ❌ |
| Request Payouts (`/Account/Profile`) | ❌ | ❌ | ❌ | ✅ | ❌ | ❌ |
| Access Admin Portal (`/Admin/*`) | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ |
| Verify Lawyers & Manage Acts | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ |
| Manage Admins & Financial Refunds | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ |

---

### 1.3 Mandatory 3-Surface Safeguards

To prevent the generation of unauthorized legal advice (hallucinated citations or unverified legal representation), MuktoAin strictly enforces safeguards across **three distinct surfaces**:

```
+--------------------------------------------------------------------------------+
| Surface 1: Persistent UI Banner (Every web page via _DisclaimerBanner.cshtml)  |
+--------------------------------------------------------------------------------+
                                       |
+--------------------------------------------------------------------------------+
| Surface 2: AI Response Injection (Injected directly by DisclaimerInjector.cs)  |
+--------------------------------------------------------------------------------+
                                       |
+--------------------------------------------------------------------------------+
| Surface 3: Document Stamping (Embedded in QuestPDF layout & draft previews)    |
+--------------------------------------------------------------------------------+
```

1. **Surface 1 — Persistent UI Banner:** A visible disclaimer banner appears permanently at the top of every screen (`_DisclaimerBanner.cshtml`), reminding users that the platform provides legal information, not formal counsel.
2. **Surface 2 — AI Response Injection:** Every AI explanation generated via `IAiService` automatically passes through `DisclaimerInjector.cs`, appending the statutory bilingual disclaimer directly to the output.
3. **Surface 3 — Document Stamping:** Generated legal documents and PDFs embed a permanent header and footer stating: *"Prepared via MuktoAin Legal Aid. Requires verified advocate endorsement."* Watermarks are stamped on any unverified draft.

---

### 1.4 The Human-in-the-Loop Review Gate

AI cannot replace certified legal advocates. Under Bangladeshi law, only enrolled advocates of the Bangladesh Bar Council may provide formal legal representation and file court petitions. 

```
[ Citizen Case Submission ] ──▶ [ RAG Statute Retrieval ] ──▶ [ AI Initial Draft ]
                                                                       │
                                                                       ▼
[ PDF Download Enabled ] ◀── [ Lawyer Approved / Edited ] ◀── [ Mandatory Review Gate ]
                                      │
                                      ▼
                             [ Lawyer Rejected ] ──▶ [ Case Returned for Re-drafting ]
```

- Every generated draft document is initialized with status `PendingReview`.
- The citizen **cannot** download the document as a clean PDF while in this state.
- A verified lawyer must inspect the draft, verify the cited sections, optionally edit errors inline, and explicitly approve the document.
- Only upon lawyer approval is the document marked `Approved`, enabling the official QuestPDF export.

---

## 2. Getting Started

### 2.1 Technical & Browser Requirements
MuktoAin is accessible via any modern standards-compliant web browser. No browser extensions or local plugins are needed.
- **Recommended Browsers:** Google Chrome (version 100+), Mozilla Firefox (version 100+), Microsoft Edge (Chromium, version 100+), Apple Safari (version 15+).
- **Display Resolution:** Optimized for desktop (1920x1080 and 1366x768), tablet, and mobile displays (responsive Bootstrap 5 design).
- **Fonts:** Bengali typography is rendered using **Noto Sans Bengali** (included locally via Open Font License).

---

### 2.2 Accessing the Application
For local development and testing, open your browser and navigate to:
```
http://localhost:5250
```
*(Or `http://localhost:5000` depending on your launch profile in [`launchSettings.json`](../src/MuktoAin.Web/Properties/launchSettings.json).)*

> 📷 *[SCREENSHOT: MuktoAin landing page with hero banner, search bar, and disclaimer banner — capture before release]*

---

### 2.3 Bilingual Interface & Language Toggle
MuktoAin supports seamless switching between **বাংলা (Bengali)** and **English**:
1. Locate the language switch button in the top navigation bar (`_LanguageToggle.cshtml`).
2. Click **বাংলা** to translate the navigation, buttons, and form labels to Bengali.
3. Click **English** to switch the UI chrome back to English.
4. The platform preserves your preference across sessions using the `mkt-lang` browser cookie (`RequestLocalizationMiddleware.cs`).

> 📷 *[SCREENSHOT: Top navigation bar showing bilingual language toggle dropdown — capture before release]*

---

### 2.4 User Registration & Account Creation
To register an account:
1. Click **Register** (`/Account/Register`) in the upper-right corner.
2. Complete the registration form:
   - **Full Name:** Your real legal name.
   - **Email Address:** Valid email address for notifications and tracking.
   - **District:** Select your home district from Bangladesh's 64 official administrative districts.
   - **Password & Confirmation:** Minimum 8 characters with at least one capital letter, one digit, and one non-alphanumeric character.
   - **Account Type:** Select **Citizen** (default) or **Lawyer**.
3. Click **Register**. You are immediately logged in to your new dashboard.

> 📷 *[SCREENSHOT: User registration form displaying district dropdown and role selection — capture before release]*

---

### 2.5 Authentication & Profile Management
- **Login (`/Account/Login`):** Enter your email and password. Remember Me preserves the session cookie.
- **Forgot Password (`/Account/ForgotPassword`):** If you forget your password, submit your registered email to receive a password reset link.
- **Profile Dashboard (`/Account/Profile`):** View your district, role, email, and wallet balance. Lawyers can also view verification badges and submit payout requests from here.

> 📷 *[SCREENSHOT: User profile screen displaying user metadata and wallet balance — capture before release]*

---

## 3. Citizen User Guide

Citizens can utilize MuktoAin to discover applicable rights, search the legal corpus, submit formal grievances, consult the AI intake chatbot, and obtain verified legal documents.

---

### 3.1 Registering & Logging In as a Citizen
Citizens may use the platform either with an authenticated account or anonymously:
- **Registered Citizen:** Cases are permanently linked to your profile, accessible from your dashboard at any time.
- **Guest / Anonymous User:** You receive a secure 32-character **Anonymous Tracking Code** upon submission. You must save this code to access your case later.

> 📷 *[SCREENSHOT: Citizen login screen with link to anonymous intake option — capture before release]*

---

### 3.2 Browsing Legal Categories
If you are unsure which law applies to your grievance, explore the pre-configured categories:
1. Navigate to **Categories** (`/Category`) from the top menu.
2. The platform lists major legal domains:
   - **General Diary (সাধারণ ডায়েরি / GD):** Lost items, extortion, theft, verbal threats, security concerns.
   - **Right to Information (তথ্য অধিকার / RTI):** Requests for official records from government departments.
   - **Labour Grievances (শ্রম আইন):** Unpaid salary, wrongful termination, maternity benefits, workplace accidents.
   - **Consumer Protection (ভোক্তা অধিকার):** Adulterated food, counterfeit goods, overcharging, refusal of service.
3. Click on any category to view standard guidelines, typical statutory provisions, and direct links to initiate a complaint.

> 📷 *[SCREENSHOT: Legal categories grid displaying icons for GD, RTI, Labour, and Consumer complaints — capture before release]*

---

### 3.3 Searching Bangladeshi Statutes
To look up specific provisions in Bangladeshi law:
1. Click **Search Acts** (`/Search`) in the navigation bar.
2. Enter keywords in Bangla or English (e.g., *"unpaid salary"*, *"মজুরি বকেয়া"*, *"Section 150"*).
3. The platform executes a full-text search against the indexed statutory database.
4. Results display:
   - Act Name (e.g., *Bangladesh Labour Act, 2006*)
   - Section Number and Header
   - Snippet of the statutory text with matching terms highlighted
5. Click **View Full Section** to read complete statutory text and historical amendments.

> 📷 *[SCREENSHOT: Full-text Acts search results page with highlighted statutory snippets — capture before release]*

---

### 3.4 Submitting a Legal Problem / Case
To submit a formal legal problem for automated analysis and document generation:
1. Navigate to **Submit Case** (`/Case/Submit`).
2. Fill out the case submission form:
   - **Category:** Select the relevant legal classification.
   - **District:** Select the incident district (e.g., *Dhaka*, *Chattogram*, *Sylhet*).
   - **Problem Title:** Brief summary (e.g., *"Employer refusing to pay 3 months of wages"*).
   - **Detailed Description:** Describe the incident in full. Include dates, names of opposing parties, financial amounts, and specific facts. You may write in **Bangla, English, or Banglish**.
   - **Preferred Document Language:** Select **বাংলা (Bangla)** for official Bengali legal formats or **English**.
   - **Anonymous Submission:** Check this box if you do not want the case linked to your user account.
3. Click **Analyze & Draft Document**.
4. The system validates all fields (enforcing length limits and preventing injection).

> 📷 *[SCREENSHOT: Citizen case submission form with detailed description field and language toggle — capture before release]*

---

### 3.5 Conversational Legal Intake Chat
If you prefer a conversational guide rather than filling out a structured form:
1. Navigate to **AI Legal Intake** (`/Chat/New`).
2. The AI assistant welcomes you and asks friendly, straightforward questions to identify missing details:
   - *"Where did the incident occur?"*
   - *"When was your last working day?"*
   - *"What was the agreed monthly salary?"*
3. Reply naturally. The intake assistant extracts facts into an underlying structured case file without declaring premature legal conclusions.
4. You can view your remaining daily token quota via the **Quota Tracker** (`/Chat/Quota`).
5. Once sufficient facts are gathered, click **Generate Case Draft from Chat** to transition directly into document generation.

> 📷 *[SCREENSHOT: Conversational AI intake chat interface showing dialogue turns and case file drawer — capture before release]*

---

### 3.6 Explaining Rights & Statutory Grounding
Upon case submission or intake completion, MuktoAin activates its RAG (Retrieval-Augmented Generation) pipeline:
1. The system converts your grievance into vector embeddings using Google's `gemini-embedding-001`.
2. Relevant sections from Bangladeshi statutes are retrieved via vector similarity search from Qdrant (with automatic fallback to SQL Server Full-Text Search).
3. The platform generates an **Explanation of Rights**:
   - Plain-language explanation of what rights you hold under Bangladeshi law.
   - Explicit citations to relevant Act numbers and Sections (e.g., *Section 123 of the Bangladesh Labour Act, 2006*).
   - Practical next steps (e.g., formal notice deadlines, required jurisdictional offices).
4. Every explanation includes the mandatory Surface-2 legal disclaimer.

> 📷 *[SCREENSHOT: Rights explanation view showing cited statutory sections and disclaimer block — capture before release]*

---

### 3.7 Generating Legal Document Drafts
Alongside the rights explanation, the system auto-drafts a formal legal application (`/Case/Result`):
- **General Diary (GD Application):** Formatted for the Officer-in-Charge (OC) of the relevant police station.
- **RTI Request (তথ্য প্রাপ্তির আবেদন):** Formatted in accordance with **Form 'Ka' (ফরম 'ক')** of the Right to Information Act 2009.
- **Labour Complaint (শ্রম অভিযোগ):** Structured complaint for the Labour Court or Department of Inspection for Factories and Establishments (DIFE).
- **Consumer Rights Complaint (ভোক্তা অধিকার সংরক্ষণ আবেদন):** Structured petition for the Directorate of National Consumer Rights Protection (DNCRP).

#### The Watermarked Preview Screen
When you click **Preview Draft** (`/Document/Preview/{id}`):
- The document draft is displayed with a diagonal **"UNAPPROVED DRAFT — NOT FOR SUBMISSION"** watermark.
- Citizen details are clearly formatted.
- The **Download PDF** button is disabled with an explanatory tooltip: *"Awaiting verified lawyer review."*

> 📷 *[SCREENSHOT: Watermarked draft document preview showing disabled download button and lawyer review callout — capture before release]*

---

### 3.8 Submitting Drafts to the Lawyer Review Queue
To submit your generated draft to the human-in-the-loop review pipeline:
1. On the Case Result screen, click **Send to Lawyer for Review** (`/Case/SendToLawyer`).
2. Review the small honorarium fee (if configured in sandbox payments).
3. Confirm submission.
4. Your case status changes to `PendingReview` and enters the pool for verified Bangladeshi advocates.

> 📷 *[SCREENSHOT: Modal dialog confirming draft submission to the lawyer verification queue — capture before release]*

---

### 3.9 Tracking Case Status
You can track the progress of your review at any time:
1. **For Registered Citizens:** Open **My Cases** (`/Case/Track`) from your user navigation menu.
2. **For Anonymous Guests:** Open `/Case/Track`, enter your **Case ID** and your secret **Anonymous Tracking Code**, and click **Lookup Case**.
3. Possible case statuses include:
   - `Submitted`: Case received, AI draft generated.
   - `PendingReview`: Placed in the lawyer review queue.
   - `UnderReview`: Claimed by a verified advocate who is currently reviewing the document.
   - `Approved / Finalized`: The advocate has approved the document. PDF download is unlocked.
   - `Rejected`: The advocate rejected the draft (e.g., due to contradictory facts). Comments explain how to re-draft.

> 📷 *[SCREENSHOT: Case tracking dashboard displaying status timeline and tracking code input field — capture before release]*

---

### 3.10 Final Legal PDF Download
Once your case is approved by a verified lawyer:
1. Navigate to `/Case/Details/{id}` or click the notification link.
2. The document preview updates to **"Lawyer Approved"** with the approving advocate's name and Bar Council registration number.
3. Click **Download Official PDF** (`/Document/DownloadPdf/{id}`).
4. The server generates an official PDF via QuestPDF featuring:
   - Formal typography (Noto Sans Bengali).
   - Advocate endorsement stamp with registration number.
   - Case reference number, date, and district seal.
   - Clean statutory disclaimer stamped permanently in the footer.
5. You can now print, sign, and submit this physical document to the relevant authorities.

> 📷 *[SCREENSHOT: Approved case view showing active Download PDF button and lawyer endorsement card — capture before release]*

---

### 3.11 Citizen Wallet, Top-Up & Honorarium Payments
Citizens can pay in two places. No real money moves in either payment mode.

- **Chat credit top-up (signed-in users):** buy extra AI chats. Open **Recharge** on your profile page (`/Account/Profile`), or press **Top Up** in the chat when your daily limit runs out. Enter an amount, pick a payment method and continue to checkout.
- **Lawyer honorarium (optional):** once a lawyer has reviewed your case, open the case result page and choose **Send Lawyer Honorarium**. Enter an amount, pick a method and continue to checkout. After the payment clears, the case shows the honorarium as paid and the lawyer is notified.

After checkout you land on `/Payment/Result`, which shows whether the payment succeeded, failed or was cancelled.

**Commission:** the platform keeps 10% of each honorarium and the lawyer gets the other 90% (a ৳500 honorarium pays the lawyer ৳450). Top-ups have no commission.

#### Chat limits and credits

- Every signed-in user gets **30 free AI chats a day**. Guests share one pool of **10 free chats a day** across all guests. Both reset at midnight Pacific time.
- A chat counts only when the AI model answers. Cached answers, section searches and blocked messages are free.
- **Price:** ৳5 = 1 chat credit. A top-up must be at least ৳50 and a multiple of ৳5, so ৳100 buys 20 credits.
- Credits are used only after your free chats for the day run out. They never expire.
- Your balance shows on your profile page and next to the chat counter.
- Only signed-in users can buy credits. Guests are asked to register or log in.
- If an administrator refunds a top-up, the credits from it that you have not used yet are removed. Credits you already spent stay spent.

#### Payment modes and test credentials

The payment mode is set by the operator (see the Deployment Guide). The default is the offline simulator.

**Simulator mode (default, works offline).** Every method opens MuktoAin's own checkout page at `/GatewaySim`. You can pay with bKash, Nagad, Rocket or a card, then enter an OTP.

| Method | Test values |
|---|---|
| bKash / Nagad / Rocket | Any `01XXXXXXXXX` number, PIN `12121` |
| Card | `4111 1111 1111 1111`, CVV `123`, any future `MM/YY` |
| OTP (all methods) | `123456` |
| Force a failure | Wallet `01700000099` (insufficient balance) or card `4000 0000 0000 0002` (declined) |

Three wrong PIN, card or OTP entries fail the payment. A checkout session expires after 30 minutes.

**Sandbox mode (needs internet).** bKash goes to the real bKash sandbox, and card goes to the SSLCommerz sandbox, which also offers net banking and other wallets.

| Method | Test values |
|---|---|
| bKash | Wallet `01619777282` or `01619777283`, OTP `123456`, PIN `12121` |
| Card | Use the test cards shown on the SSLCommerz sandbox checkout page |

> 📷 *[SCREENSHOT: Simulated checkout page with bKash selected — capture before release]*

---

## 4. Verified Lawyer User Guide

Certified advocates enrolled with the Bangladesh Bar Council use MuktoAin to review community legal drafts, correct citations, earn honorariums, and provide pro-bono aid.

---

### 4.1 Applying for Lawyer Verification
To register as an advocate on MuktoAin:
1. Register an account with role **Lawyer** (`/Account/Register`).
2. Upon first login, you are redirected to the **Lawyer Status & Verification** screen (`/Lawyer/Status`).
3. Complete the Bar Verification form:
   - **Bar Council Registration Number:** Your official enrollment number.
   - **Primary Bar Association:** (e.g., *Dhaka Bar Association*, *Supreme Court Bar Association*).
   - **Legal Specialization:** (e.g., *Labour Law*, *Criminal Defense*, *Civil Litigation*, *Consumer Law*).
   - **Certificate Document:** Upload a scanned copy of your Bar Council license or membership card (PDF, PNG, or JPG).
4. Click **Submit Verification Request**.
5. Your account enters status `PendingVerification`. An administrator will inspect your credentials before activating your review privileges.

> 📷 *[SCREENSHOT: Lawyer verification application form showing Bar Council registration inputs and document upload — capture before release]*

---

### 4.2 Navigating the Review Queue
Once verified by an administrator, the **Lawyer Review Queue** (`/Lawyer/Queue`) is unlocked:
1. Open **Review Queue** from your top navigation menu.
2. The queue displays unassigned documents awaiting review, ordered chronologically.
3. Filter cases by:
   - Legal Category (Labour, Consumer, GD, RTI).
   - Incident District.
   - Preferred Document Language.
4. Each entry displays the Case Title, Category, Submission Date, and Citizen Language.

> 📷 *[SCREENSHOT: Lawyer review queue interface showing unassigned case cards and category filter pills — capture before release]*

---

### 4.3 Claiming Cases & Concurrency Locking
To prevent multiple lawyers from reviewing the same document simultaneously:
1. Click **Claim Case** on any open queue item.
2. The platform applies an optimistic concurrency lock (`RowVersion` token in `GENERATED_DOCUMENT`).
3. If another advocate claimed the case a millisecond earlier, the system notifies you gracefully without race conditions.
4. Once claimed, the case status transitions to `UnderReview` and remains locked to your profile for 24 hours.

> 📷 *[SCREENSHOT: Case claim confirmation prompt with 24-hour review window notice — capture before release]*

---

### 4.4 Conducting Legal Document Reviews
Opening a claimed case presents the **Document Review Workspace** (`/Lawyer/Review/{id}`):
- **Left Panel — Citizen Problem & Grounding:** Displays the citizen's original problem description, incident facts, and the statutory sections retrieved by RAG.
- **Right Panel — Editable Legal Draft:** Displays the AI-generated document draft.

#### Review Actions
You have three review options:
1. **Approve As-Is:** If the AI draft is accurate, properly cited, and legally sound, select **Approve**, enter brief congratulatory/encouraging notes, and click **Finalize Review**.
2. **Edit & Approve (Inline Editor):** If the draft contains minor factual errors, formatting anomalies, or awkward phrasing:
   - Edit the text directly in the review textarea.
   - Add missing legal boilerplate or adjust section citations.
   - Select **Edit & Approve** and submit. The edited version becomes the authoritative draft.
3. **Reject Draft:** If the problem is fundamentally unviable under the cited law, fraudulent, or missing critical facts:
   - Select **Reject**.
   - Provide mandatory, constructive feedback in the **Rejection Comments** field explaining what the citizen must clarify.
   - The case is returned to the citizen's dashboard for rectification.

> 📷 *[SCREENSHOT: Lawyer review workspace with side-by-side citizen facts panel and editable draft document panel — capture before release]*

---

### 4.5 Past Reviews & Review History
To review cases you have previously handled:
1. Navigate to **Review History** (`/Lawyer/History`).
2. Displays a table of all documents you approved, edited, or rejected.
3. Review audit timestamps, citizen feedback, and honorarium records.

> 📷 *[SCREENSHOT: Lawyer review history table displaying past decisions, timestamps, and citizen feedback — capture before release]*

---

### 4.6 Lawyer Earnings, Honorarium & Payout Requests
Lawyers earn nominal honorariums for each completed review:
1. Navigate to **Earnings & Payouts** (`/Lawyer/Payments`).
2. View your ledger:
   - Total reviews conducted.
   - Total honorarium earned (BDT).
   - Available balance ready for payout.
3. To request a payout:
   - Open your **Profile** (`/Account/Profile`).
   - Click **Request Payout**.
   - Enter your bKash, Nagad, or Bank Account details and requested amount.
   - Payout requests are verified and settled by administrators.

> 📷 *[SCREENSHOT: Lawyer earnings dashboard showing balance card and payout request modal — capture before release]*

---

### 4.7 Ethical Duties & Legal Representation Notice
- Advocates reviewing drafts on MuktoAin act as independent legal verifiers.
- Reviewing a document does not automatically create an advocate-on-record appearance in court.
- Lawyers must never solicit illegal gratification or insert fraudulent statements into citizen drafts.
- The advocate's name and enrollment number are permanently stamped onto approved PDF exports.

---

## 5. System Administrator User Guide

System Administrators and SuperAdministrators oversee the health, integrity, safety, and legal corpus of MuktoAin.

---

### 5.1 Admin Authentication & Role Tiers
Administrators log in via the standard login screen (`/Account/Login`). Upon authentication, an **Admin Portal** link appears in the main navigation.

#### SuperAdmin vs Admin Role Tiers
MuktoAin employs a two-tiered administrative model:
- **Administrator:** Can manage the statutory corpus, curate keyword scenarios, inspect AI logs, review analytics, and verify lawyers.
- **SuperAdministrator (`IsSuperAdmin = true`):** Can additionally create new administrators, suspend administrative accounts, approve financial payouts, and process payment refunds.

> 📷 *[SCREENSHOT: Admin navigation bar highlighting SuperAdmin badge and system management menus — capture before release]*

---

### 5.2 System Dashboard & Analytics
The **Admin Dashboard** (`/Admin/Dashboard`) provides real-time visibility into platform operations:
- **KPI Metrics:** Total Cases, Pending Reviews, Verified Advocates, Total Documents Finalized.
- **System Health:** Live connection status of SQL Server, Qdrant Vector Store, and Gemini API keys.
- **Embedding Ingestion Progress:** Real-time progress bar showing the status of section chunk vectorization.
- **Analytics Charts (`/Admin/Analytics`):** Breakdown of complaints by district and legal category (with full PII anonymization).

> 📷 *[SCREENSHOT: Administrative dashboard displaying live health metrics, KPI cards, and district chart — capture before release]*

---

### 5.3 User Management & Security Controls
Navigate to **User Management** (`/Admin/Users`):
1. View a searchable, paginated list of all registered accounts.
2. Filter users by role: Citizen, Lawyer, Admin.
3. Actions:
   - **Suspend User:** Immediately revokes login access and terminates active sessions (e.g., for bad-faith actors or spam).
   - **Unsuspend User:** Restores account access.
   - **Promote to Admin (SuperAdmin only):** Grants administrative access to trusted team members.
   - **Mutual-Immutability Guard:** A SuperAdmin cannot be suspended or demoted by another admin.

> 📷 *[SCREENSHOT: Admin user management table with role badges, search filter, and Suspend action buttons — capture before release]*

---

### 5.4 Lawyer Verification & Credential Auditing
Navigate to **Lawyer Verifications** (`/Admin/Lawyers`):
1. Review all pending advocate verification applications.
2. Click **Inspect Credentials** to review:
   - Submitted Bar Council Registration Number.
   - Bar Association affiliation.
   - Uploaded license certificate scan.
3. Cross-reference the registration number with the official Bangladesh Bar Council enrollment register.
4. Click **Approve & Verify** to grant review queue access, or **Reject** with a clear explanation.

> 📷 *[SCREENSHOT: Lawyer verification audit screen showing applicant details and certificate inspection preview — capture before release]*

---

### 5.5 Statutory Corpus Management
Navigate to **Corpus Management** (`/Admin/Corpus`):
1. Inspect the statutory database status (1,484 Acts, 35,633 sections, 42,858 chunks).
2. View Qdrant vector collection health and embedding dimensions.
3. Trigger re-indexing or force vectorization for newly uploaded statutes.
4. Test search ranking between Vector similarity search and SQL Full-Text Search fallback.

> 📷 *[SCREENSHOT: Corpus management screen displaying indexed Acts count, vector collection health, and re-index controls — capture before release]*

---

### 5.6 Legal Categories & Curated Scenario Guidance
Navigate to **Scenario Guidance** (`/Admin/Scenarios`):
1. Maintain curated keyword-to-section mappings (FR-18).
2. For common citizen phrases (e.g., *"pregnant worker fired"*), define instant boosts to specific sections (e.g., *Maternity Benefit provisions, Section 46 of Labour Act*).
3. Add, edit, or delete scenario mappings to continuously enhance Gemini's citation accuracy without retraining the model.

> 📷 *[SCREENSHOT: Scenario mappings management table displaying citizen keywords, target section IDs, and notes — capture before release]*

---

### 5.7 Financial Audits & Transaction Ledger
Navigate to **Transactions** (`/Admin/Transactions`):
1. View complete financial audit trail of all citizen top-ups and lawyer honorariums.
2. Inspect individual order status: `Pending`, `Paid`, `Failed`, `Refunded`.
3. **Process Refund (SuperAdmin only):** Refund disputed or cancelled honorarium payments back to the citizen's balance.
4. **Approve Payout (SuperAdmin only):** Mark lawyer withdrawal requests as disbursed following manual bank or mobile transfer.

> 📷 *[SCREENSHOT: Administrative transaction ledger showing order status, amounts, and refund action buttons — capture before release]*

---

### 5.8 AI Audit Logs & System Health
Navigate to **AI Audit Logs** (`/Admin/AiLogs`):
1. Inspect every generation request handled by Gemini.
2. Review prompt tokens, completion tokens, latency, and temperature settings.
3. Inspect raw retrieved statutory sections versus final generated citations to monitor for hallucination.
4. Check Gemini API key rotation status across multiple configured keys.

> 📷 *[SCREENSHOT: AI audit log viewer showing prompt latency, token counts, and sanitized generation logs — capture before release]*

---

## 6. Language & Localization Guidelines

### 6.1 Multi-Script Input Support
MuktoAin is designed to understand natural citizen communication across Bangladesh:
- **Standard Bangla (বাংলা):** *"আমি তিন মাস ধরে বেতন পাইনি। কারখানা কর্তৃপক্ষ কোনো নোটিশ ছাড়া আমাকে চাকরিচ্যুত করেছে।"*
- **Standard English:** *"My employer terminated my contract without notice and refuses to pay 3 months of arrears."*
- **Romanized Banglish:** *"Amar factory boss kono notice chara amake ber kore dise, 3 masher beton baki."*

Gemini 2.5 Flash and `gemini-embedding-001` understand all three formats natively. The system maps Banglish concepts to official legal terminology during retrieval.

---

### 6.2 UI Chrome Localization
- All navigation links, button text, error notices, and validation tooltips are stored in resource dictionaries (`SharedResource.bn.resx` and `SharedResource.en.resx`).
- The user's language selection controls which resource file is read, providing an interface in pure Bangla or English.

---

### 6.3 Bilingual Legal Document Templates
When generating documents, the citizen selects their preferred document language:
- **Bangla Legal Mode (বাংলা):** Outputs the document using formal Bangladeshi legal Bengali terminology (e.g., *"বরাবর, অফিসার ইনচার্জ"*, *"বিষয়: সাধারণ ডায়েরি করার আবেদন"*).
- **English Legal Mode:** Outputs the document in formal High Court / English administrative format.

---

## 7. Troubleshooting & Common Issues

### 7.1 Search Returns No Statutory Sections
- **Cause:** The initial statutory dataset has not been imported into your database.
- **Solution:** Run the database seeding scripts in SSMS (`scripts/run-all.ps1`) or navigate to `/Admin/Corpus` to initiate seed ingestion from `data/bangladesh-acts-dataset.json`.

---

### 7.2 AI Chat Rate Limits & Daily Quota Reached
- **"Daily AI limit reached" in the chat:** you have used your free chats for today (30 for signed-in users, 10 shared by all guests) and have no chat credits left. Wait for the reset at midnight Pacific time, or buy chat credits with **Top Up** (signed-in users, see section 3.11). Guests can register for their own 30 chats a day.
- **Model errors despite remaining chats:** Google AI Studio free-tier quotas (15 requests/minute, 1500 requests/day) apply to the whole site. MuktoAin rotates across the configured Gemini API keys (`GEMINI_API_KEY_1`, `GEMINI_API_KEY_2`, etc.). If all keys are exhausted, wait for the reset. A chat that fails does not use up a free chat or a credit.

---

### 7.3 PDF Download Button Inactive or Gated
- **Cause:** The generated legal draft has not yet been approved by a verified lawyer.
- **Solution:** Click **Send to Lawyer for Review**. Once a verified advocate approves your draft, the download button unlocks immediately.

---

### 7.4 Anonymous Tracking Code Lost
- **Cause:** You submitted a case anonymously and did not record the 32-character tracking GUID.
- **Solution:** For privacy reasons, anonymous cases cannot be retrieved without the unique tracking code. If lost, submit a new case or register an account so all cases stay linked to your profile.

---

### 7.5 Lawyer Application Pending Approval
- **Cause:** Your lawyer verification request is awaiting administrative inspection.
- **Solution:** Verification typically takes 24–48 hours while administrators cross-check Bar Council records. Contact platform support if urgent.

---

## 8. Frequently Asked Questions (FAQ)

#### Q1: Is MuktoAin a law firm or a substitute for a lawyer?
**A:** No. MuktoAin is an educational and document-drafting technology platform. It does not provide legal representation. Our human-in-the-loop architecture connects your draft to independent verified advocates before you use it.

#### Q2: Can I download a generated document without lawyer review?
**A:** No. All unapproved drafts are watermarked and locked against PDF export. You must submit the draft through the lawyer review queue to obtain an official PDF.

#### Q3: Does MuktoAin share my private data with opposing parties?
**A:** Never. Case titles and problem descriptions are encrypted at rest using AES-256 (`EncryptionService.cs`). Anonymous submissions do not record your IP or user identity.

#### Q4: How much does it cost to use MuktoAin?
**A:** Searching statutes, browsing categories, and consulting the AI intake chatbot are 100% free. Lawyer reviews are facilitated with a nominal community honorarium via sandbox payments.

#### Q5: Can I submit a case on behalf of an illiterate family member?
**A:** Yes. Community volunteers, family members, or union representatives can submit a case on behalf of others using their preferred language (Bangla or English).

#### Q6: Which Bangladeshi courts accept MuktoAin-generated documents?
**A:** The generated formats follow standard Bangladeshi templates: General Diaries for Thana police stations, Form 'Ka' for government RTI officers, and formal complaints for the Labour Court and Consumer Rights Directorate.

#### Q7: What happens if a lawyer rejects my case draft?
**A:** The lawyer provides specific feedback explaining what information is missing or why the selected statute does not apply. You can review the feedback, edit your facts, and resubmit.

#### Q8: How can I become a verified lawyer on the platform?
**A:** Register an account as a Lawyer and upload your Bangladesh Bar Council enrollment certificate under `/Lawyer/Status`. Once an administrator audits your credentials, your review account is activated.

#### Q9: What should I do if I am facing an immediate emergency?
**A:** MuktoAin is not an emergency dispatch service. For active crimes, imminent violence, or physical danger, contact national emergency services immediately by dialing **999**.

#### Q10: How does MuktoAin ensure the AI does not fabricate legal statutes?
**A:** MuktoAin uses Retrieval-Augmented Generation (RAG). The AI is strictly instructed to cite only the authentic statutory text retrieved from our verified database. Any response that attempts to invent a non-existent law is flagged and blocked.

---

## 9. References & Further Reading

- [System Architecture Specification](architecture.md) — Architectural overview, ERD, and component diagrams.
- [Deployment & Setup Guide](deployment-guide.md) — Local installation, database configuration, Docker, and secrets.
- [Testing & Quality Assurance Report](testing-report.md) — Complete test coverage methodology and unit test results.
- [Attribution & Open Data Licenses](attribution-CC-BY-SA-4.0.md) — Licensing and attribution for Bangladeshi Acts and QA datasets.
- [Main Repository README](../README.md) — Project mission, quick start, and team overview.
