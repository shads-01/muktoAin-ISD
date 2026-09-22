# MuktoAin Frontend Redesign — Design Spec

**Date:** 2026-09-05
**Status:** Approved by user (brainstorm complete; awaiting implementation plan)
**Scope:** Full end-to-end UI/UX overhaul of every page + additive backend services to support new flows. Theme/visual system (CSS, tokens, fonts) unchanged — this spec covers **pages, content, and flows only**.

---

## 1. Vision

Transform MuktoAin from a form-based legal-aid site into a **chat-first citizen experience** (like ChatGPT/Gemini/Claude): a citizen describes their legal problem conversationally in বাংলা/English/Banglish, goes back and forth with the AI (grounded in RAG-retrieved Bangladeshi statutes), and only when satisfied presses **[নথি তৈরি করুন / Generate Draft]** — which packages the full conversation, cited acts, and AI context into a case with an AI-drafted legal document. The citizen reviews/edits the draft, sends it to a **specialization-matched lawyer pool** (claim-based), tracks progress, receives rejection feedback through the chat itself (AI-assisted salvage), and downloads the finalized lawyer-approved PDF.

Everything currently working (backend case pipeline, RAG, auth, search) stays intact. All changes are additive or page-level redesigns.

---

## 2. The New Case Lifecycle (backbone)

```
CHAT (ephemeral, auto-saved, resumable)
  ↓ citizen satisfied → [Generate Draft] → confirm modal
CASE: DraftReady (citizen editing)          ← tracking code issued here (guests)
  ↓ [Send to Lawyer]
CASE: UnderReview → specialization pool, oldest-first, lawyer claims (optimistic lock)
  ↓ lawyer decides
APPROVED → lawyer's final version shown → PDF unlocks (FR-9, Surface-3 stamp)
REJECTED → mandatory reason → [Return to Chat] w/ reason injected into AI context
  → salvageable: AI improves draft → regenerate → resubmit (version++)
  → unsalvageable: AI says so honestly → citizen [Withdraw] → Withdrawn (viewable forever)
```

**Statuses:** `DraftReady → UnderReview → Approved | Rejected (loop) | Withdrawn`

**Unification rule:** the structured form (FR-2, kept as secondary intake) synthesizes its answers into the transcript as the opening user message — **every case = chat transcript + draft version chain + review events**, regardless of entry path.

**Key decisions (locked during brainstorm):**
- Case created **at [Generate Draft]** press (chat stays ephemeral until commit)
- Lawyer assignment: **specialization pool + claim** (no citizen picking, no auto-assign, no public directory)
- Citizen sees lawyer name + bar no **after claim**
- Guest chat: unlimited-in-quota, tracking code issued at draft-gen; **optional notification email** at draft-gen ("no account created")
- Rejection loop: reason injected into chat context; **AI honestly assesses salvageability**; citizen withdraws if dead
- Full **draft version chain**: v1 = AI original (immutable), v2+ = citizen/lawyer edits; lawyer sees original-vs-citizen diff
- Unfinished chats: **auto-save + resumable** (session for guests, DB for logged-in) via recent-chats strip
- Unlimited parallel cases per citizen
- Email notifications for signed-in citizens + guests who left an email (SMTP; part of this build)
- In-app unread dot on My Cases for lawyer activity

---

## 3. AI Budget Controller (Gemini free-tier viability)

**Problem:** Free-tier `gemini-2.5-flash` ≈ 250 RPD; `flash-lite` ≈ 1,000 RPD. Every chat turn ≈ 1 embed (RAG) + 1 generate. "Unlimited free chat" is not viable platform-wide.

**Solution — `AiBudgetService` + degradation ladder:**

1. **Metering via FR-12's existing `AI_LOG`** — `COUNT(*)` by user/session per day. Zero new metering schema.
2. **Reserved pool:** ~30% of each model's daily RPD reserved for case-critical ops (draft generation, salvage regeneration). Chat can never starve the FR-5 pipeline.
3. **Model routing:** chat turns → `flash-lite`; draft-gen + complex analysis → `flash`.
4. **Tiers:** Guest ≈ 10 AI turns/day (per session) · Signed-in ≈ 30/day. Resets midnight PT (matches Gemini RPD reset).
5. **Degradation ladder (instead of a hard wall):** full AI answer → capped-output answer (~600 max tokens, trimmed context) → **retrieval-only answer** (scenario-mapping hits + FTS sections formatted with citations; zero model calls; genuinely useful) → quota wall.
6. **Quota wall = 5 escape routes:** (1) আগামীকাল আবার, (2) নিবন্ধন করুন → 3× quota, (3) আইন খুঁজুন (search always free), (4) retrieval-only mode, (5) **quota top-up pack (sandbox payment)**.
7. **Answer cache:** normalized-query hash → cached answer for repeat questions (especially the 4 category staples). Honest "answered before" label.
8. **Honest counter on composer:** "আজ বাকি: ৭" — real numbers, no dark patterns.
9. **BYOK (user's own Gemini key) — considered and rejected:** Google exposes no user-quota passthrough via OAuth. Pasting personal API keys burdens low-digital-literacy users and creates credential-custody risk. Documented as rejected.

---

## 4. Payments (sandbox + commission — course requirement)

**Mode:** SSLCommerz (or equivalent) **sandbox only** — fake money, clearly badged "ডেমো মোড — স্যান্ডবক্স পেমেন্ট" on every money surface. Commission system required by course teacher.

**Entities:**
- `PAYMENT_ORDER` — Purpose (TopUp | Honorarium), Amount, Status (Pending → Paid | Failed | Refunded), Gateway txn ref, sandbox flag
- `HONORARIUM_LEDGER` — gross / commission / net per honorarium order
- `PAYOUT_REQUEST` — lawyer payout requests → admin approves → mark paid

**Commission:** 10% platform / 90% lawyer (e.g., ৳200 → lawyer ৳180 · platform ৳20). **Configurable constant in appsettings.** Split shown transparently in the payment modal **before** paying.

**Two honest purchase moments (free tier never blocked):**
1. **Quota top-up at the wall:** ৳49 → +50 turns today (all free escape routes remain)
2. **Lawyer honorarium at approval:** "আইনজীবীকে শুকরিয়া জানাতে চান?" → citizen's chosen amount → split preview

**Lawyer surface:** Earnings card — sandbox balance, per-case honorarium history (case, anonymized citizen, gross, commission, net), [পরিশোধ চান] payout request.

**Admin surface:** dedicated **Transactions page** (8th admin page) — orders list (anonymized citizen, purpose, status), commission ledger, payout queue (approve → mark paid), refund action (order refunded + quota/ledger reversed).

**Lifecycle:** gateway failure/cancel → order Failed + retry prompt; **nothing in case/quota changes until sandbox IPN/webhook confirms success**. Refund = admin action → order Refunded + quota/ledger reversed.

**Copy impact:** About softens "সম্পূর্ণ বিনামূল্যে" → "প্রধান সব সেবা বিনামূল্যে — Free core service; optional sandbox payments for quota top-ups & lawyer honoraria." Free tier untouched.

---

## 5. Page Inventory (25 pages, 4 surfaces)

Theme/visuals per existing `template-frontend/` + `main.css` conventions. All pages follow PATTERNS.md skeleton (skip-link → role navbar → disclaimer banner → drawer → main → footer; chat home uses mini-footer).

### A. Citizen Surface (8)

**A1. Chat Home `/` (rebuilt from Home/Index)**
ChatGPT-style shell: welcome bubble ("আপনার সমস্যা বলুন — আইনটা আমরা খুঁজে দেব"), 4 category prefill chips (GD/RTI/Labour/Consumer) + example prompts, **recent-chats strip** (resume unfinished; guests via session), mode chips (আইনি অধিকার জানুন / ধারা খুঁজুন — act-search answers inline in chat), sticky composer with honest quota counter, answer cards w/ citation chips → **section modal** (verbatim text, amendment/footnote, source), quick-reply chips, inline AI disclaimer (Surface 2). No marketing content — About absorbs it.

**A2. Generate-Draft confirm modal**
AI-proposed doc type (editable — guarantees template match), District select (64, required FK), anonymous toggle w/ tracking-code note, optional notification email, [তৈরি করুন] → creates case → lands on Case page draft-first. Tracking code + copy shown prominently for guests. Case-critical quota (never charged to citizen's chat quota).

**A3. Case Page `Case/Result` (evolved — single stateful case home)**
Real status timeline (never hardcoded again — driven by actual status), embedded parchment paper view w/ **[Edit] toggle** (citizen edits by typing; version chain v1 immutable AI original, v2+ edits), [Send to Lawyer], lawyer block (name + bar no after claim, decision, comments), rejection reason inline + [Return to Chat], [Withdraw] action, PDF button locked→unlocked by approval. **No share URL, no print** (citizen opens case on tracking page to show someone in person). Document controller shrinks to PDF download endpoint only.

**A4. My Cases `Case/Track` (evolved)**
New-case button + mobile FAB, **guest tracking-code lookup card** (MKT-XXXXXX → opens case), working server-side status filter chips (fixes decorative filters), case rows w/ unread dot on lawyer activity, rejection reason inline, pagination.

**A5. Structured Submit form `Case/Submit` (kept, fixed)**
Secondary intake for form-preferrers/accessibility. Category pre-fill now actually works (from categories/search/chat deep-links). Right-rail "এরপর কী হবে" steps. Form answers synthesized into transcript as opening message → same lifecycle as chat path. Anonymous checkbox + tracking-code explainer.

**A6–A7. Categories (Index/Details) — topic launchers**
Index: 4-card grid w/ badges (guides, sections count) + "can't find your topic?" helper. Details: common-scenario cards that **deep-link into chat with prefilled prompt** (e.g. `/?prefill=আমার বেতন ৩ মাস বাকি...`), relevant-laws accordions (w/ amendment/source), dual CTAs (আলোচনা করুন → chat / ফর্মে জমা দিন → submit w/ category preselected). Browse-without-submitting preserved (FR-6).

**A8. Payment surfaces (modal flows)**
Quota top-up at wall + honorarium after approval, both w/ transparent split preview + sandbox badge.

### B. Lawyer Surface (3)

**B1. Lawyer/Status (NEW)**
Unverified lawyer landing: pending state w/ submitted bar no + typical review time, "while you wait" checklist (browse laws, categories, complete profile), rejection state w/ admin's reason + resubmit from this page. (Register flow covers application — no separate apply page.)

**B2. Queue (evolved — real data, no mocks)**
KPI strip (Pending / Completed today / Avg turnaround 7d), filter toolbar (All/Mine/Unassigned chips, category, sort), queue table w/ **SLA age** ("waiting 26h"), claim-on-open (optimistic lock, one active review per lawyer — others' buttons disable w/ tooltip), footer identity card.

**B3. Review (evolved)**
Case context accordion (citizen's quote, **PII-decrypted-for-this-session note**, citation chips, "citizen edited" flag), original-vs-editable compare (tabs mobile / side-by-side ≥900px), mandatory comments textarea, 3 decisions: Approve (confirm) / Approve-with-Edits / **Reject → mandatory-reason modal** (reason shown to citizen + injected into their chat context on return).

### C. Admin Console (8 pages, 100% real data — services built, no mocks)

**C1. Overview/Dashboard** — real KPIs (cases, pending reviews, verifications waiting, AI calls w/ failure rate), live health (MSSQL/Qdrant/Gemini), embedding progress (existing poll), category + district charts, verification mini-queue linking to full page. All dead buttons/placeholder links eliminated.
**C2. Users** — search + role filter, suspend/activate (login already checks suspended), admin rows protected (no actions), pagination.
**C3. Lawyers/Verification** — Pending/Approved/Rejected tabs w/ counts, approve btn, reject → mandatory reason modal (shown to lawyer on their Status page).
**C4. Corpus** — Acts tab (acts table w/ sections/chunks/last-embedded/status incl. Stale + per-act Re-embed) + Import tab (drop-zone, staged pipeline w/ progress, hash-dedup stats).
**C5. Scenarios** — keyword→section mapping CRUD (inline add form, delete w/ confirm, bulk CSV import).
**C6. Categories** — 4-category CRUD grid w/ **template badges** (`gd_application.v1` etc.), new/edit modal.
**C7. AI Logs** — FR-12 audit: filter by type/date/latency, log table w/ status dots, inspect accordion (PII-scrubbed prompt/response in mono), case cross-links, retention note.
**C8. Transactions (NEW)** — orders list (anonymized citizen, purpose, amount, status), commission ledger (gross/commission/net), payout queue (approve → mark paid), refund action. Where the commission system is demonstrated.

Admin is **not** in the review pool (review is lawyer-only; admin manages, doesn't review).

### D. Account & Public (9)

**D1. Login** — auth-split layout, demo-accounts panel, "account not needed to submit" note.
**D2. Register** — role radio-cards (Citizen/Lawyer); lawyer path inline: bar reg no + specialization + "verification required before queue" note.
**D3. ForgotPassword (NEW)** — real email reset flow via SMTP + Identity tokens.
**D4. ResetPassword (NEW)** — token-validated password set page.
**D5. Profile** — current form + password change + role links + **[সব ডিভাইস থেকে লগ আউট]** (logout everywhere). No session device list.
**D6. Search `Search/Index`** — working act filter (real submitted param — fixes decorative checkboxes), `<mark>` highlights, pagination, section modal for truncated text, **"AI-কে জিজ্ঞেস করুন" chip per result** → chat w/ section pre-cited. Skip recent-searches rail.
**D7. About** — absorbs home's marketing: how-it-works 3-step, trust/academic highlight, FAQ, canonical disclaimer (`#disclaimer`), dataset attribution (`#dataset`). Updated payment copy (§4).
**D8. Error pages 403/404/500** — template versions (CTAs per template).
**D9. Privacy** — updated for: chat data, notification emails, quota top-up records, payment sandbox data.

### Navigation (per role — template's 4 navbar variants)
- Guest/Citizen: চ্যাট · বিষয়সমূহ · আইন খুঁজুন · (আমার মামলা if logged in) · পরিচিতি
- Lawyer (verified): রিভিউ কিউ · আইন খুঁজুন · বিষয়সমূহ · পরিচিতি (+ Earnings card links); unverified → ভেরিফিকেশন status link instead of queue
- Admin: Overview + secondary adminbar (Dashboard · Users · Lawyers · Corpus · Scenarios · Categories · AI Logs · Transactions)

---

## 6. Backend Additions (additive only — nothing existing breaks)

**New services/endpoints:**
- Chat: `CHAT_SESSION`/`CHAT_MESSAGE` persistence, AJAX chat endpoint (send → RAG → budget-checked answer), resume-list queries, prefill param
- `GenerateDraftFromChat` (packages transcript + citations → Case + Document + context snapshot in one transaction; issues tracking code; optional notification email)
- `SendToLawyer` (pool entry w/ specialization match), `ClaimDocument` (optimistic lock), `ResubmitCase` (version++), `WithdrawCase`
- Citizen draft edit (version chain writes)
- Unread-activity tracking (dot data) + email notification service (SMTP): sent-to-lawyer / approved / rejected(reason)
- `AiBudgetService` (metering, tiers, routing, ladder, cache) + answer-cache table
- Payments: `PaymentService` (sandbox gateway init/IPN/verify), order lifecycle, commission ledger, payout requests/refunds
- Forgot/Reset password (Identity tokens + SMTP)
- Admin services for all 8 pages (real queries; suspend/activate; verification approve/reject w/ reason; corpus re-embed triggers; scenario CRUD; category CRUD; AI log filters)
- PDF export wiring (`PdfExportService` — currently a TODO stub) gated on Approved status

**Schema additions (additive tables/columns; zero renames of working schema):**
- `CHAT_SESSION` (Id, UserId?, SessionKey?, Title, Status: InProgress|Committed, CreatedAt, UpdatedAt)
- `CHAT_MESSAGE` (Id, ChatSessionId, Role: User|Assistant, Content, CitedJson, CreatedAt)
- `ANSWER_CACHE` (QueryHash, Question, Answer, CitedJson, HitCount, CreatedAt)
- `PAYMENT_ORDER` (Id, UserId?, CaseId?, Purpose: TopUp|Honorarium, Amount, Commission, NetToLawyer, Status, GatewayRef, CreatedAt, PaidAt)
- `HONORARIUM_LEDGER` / `PAYOUT_REQUEST`
- `GENERATED_DOCUMENT` additions: version columns (VersionNo, ParentVersionId), claim columns (ClaimedByLawyerId?, ClaimedAt), citizen-edit flag
- `CASE` additions: Withdrawn status, NotificationEmail?, UnreadFlag for citizen, rejection-reason plumbing (via review)
- `USER`/`LAWYER_PROFILE`: (already have suspend + verification status) — payout earnings derive from ledger

*(Exact DDL gets finalized in the implementation plan against the real 14-entity schema.)*

**Bugs fixed en route:** home composer dropping typed query, hardcoded result timeline, `CanDownloadPdf` force-false, mock lawyer/admin data, decorative search filters, dead admin buttons, fake forgot-password toast, `Document/Preview` ownership hole (page is replaced by embedded case view; controller shrinks to gated download).

**Explicitly out of scope (rejected):** TTS/শুনুন buttons, shareable case URLs, print stylesheet (per final decision: PDF-only + in-person case view), public lawyer directory, session device list, BYOK, live payments, dark-gallery variants beyond current theme.

---

## 7. Design Principles Applied (from skills)

- **Question test:** every screen asks one easy question — chat home: "what's your problem?"; case page: "happy with the draft?"; queue: "what do you review next?"
- **Value before ask:** full chat value before any account wall; tracking code at commit; registration = 3× quota (honest)
- **Specificity is trust:** SLA ages, real quota counters, transparent commission split, verbatim citations w/ amendment + source
- **No dark patterns:** no fake urgency/scarcity; honest quota; sandbox payments badged; AI honestly refuses unsalvageable cases
- **Mobile-first:** single-column stacking = priority; FAB on My Cases; sticky composer; bottom-sheet modals; mobile compare-tabs for lawyer review
- **State coverage:** loading (typing dots), empty (welcome, no cases, no results), error (quota wall w/ 5 escapes, payment failure + retry), rejection states everywhere

---

## 8. Success Criteria

1. A guest can: chat (quota'd) → generate draft → get tracking code → track → reopen case by code → see rejection → return to chat w/ reason → resubmit or withdraw → download PDF after approval — **without ever creating an account**.
2. A citizen can hold unlimited parallel cases; unfinished chats auto-resume.
3. Free tier never blocks rights: quota wall always offers ≥4 free escape routes.
4. Lawyers only see docs in their specialization; one active review; rejection reason always present.
5. Admin console: zero mock data, zero dead buttons; commission demonstrable end-to-end in sandbox (order → split ledger → payout).
6. Gemini budget: platform survives a busy day (reserved pool for FR-5; degradation ladder absorbs spikes).
7. All 3 disclaimer surfaces intact everywhere; PDF permanently stamped (FR-11).
