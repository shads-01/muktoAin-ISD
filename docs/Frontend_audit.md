# Frontend Audit — Bilingual Switch, Static Content & Inconsistencies

**Date:** 2026-09-07
**Scope:** All 34 Razor views, `_Layout.cshtml`, `main.js` (1673 lines), `chat.js` (466 lines), `main.css` (1043 lines), and the controllers that feed toast messages.
**Method:** Static read-through + grep verification (counts and code excerpts below are from the actual files, not assumptions). No code was changed to produce this document.

---

## Root cause of the "buggy" language switch

There is **no server-side localization** — no `RequestLocalization` middleware, no resource files, no cookie read in `Program.cs`. Translation is 100% client-side: `main.js` writes the chosen language to `localStorage`/a cookie (`mkt-lang`), but **nothing ever reads that cookie server-side**. Every full page load re-renders in whatever language the `.cshtml` was hardcoded in, then JS tries to retranslate after `DOMContentLoaded`.

Two competing translation techniques coexist in the same codebase:

1. **Legacy approach** (most of `main.js`): ~700 lines of per-route blocks that grab elements by fragile positional CSS selectors — `.grid-3 .card` index 0/1/2, `labels[0..3]`, `stepCards[0].querySelector("b")`, `section:nth-of-type(2) .kicker`, `table thead th` by index. Any markup change silently desyncs translation, with no error.
2. **Modern approach** (`data-bn`/`data-en` attributes + one generic handler at `main.js:432`): robust, markup-order-independent. Already used correctly on `Home/Index`, `Admin/Dashboard`, `Admin/Analytics`, most of `Case/Result`, `Lawyer/Queue`.

The migration from (1) to (2) is **half-finished**, and the old JS blocks were never deleted after the views they targeted were rebuilt — that mismatch is the actual source of the bugginess.

---

## Confirmed active bugs

| # | Where | Problem |
|---|---|---|
| 1 | `main.js:947-957` (`/lawyer` branch) vs `Lawyer/Queue.cshtml:15` | JS overwrites the real `.page-head .kicker` ("FR-13 · FR-23") with a fabricated, stale label ("Verified Advocate Portal · FR-13" / "সনদপ্রাপ্ত আইনজীবী পোর্টাল") that doesn't match current markup at all — visible on every language toggle. |
| 2 | `_Layout.cshtml:146-155` — `.nav-adminbar` (Dashboard/Users/Lawyers/Corpus/Scenario Mappings/Categories/AI Logs/Transactions) | Hardcoded English, zero `data-bn`, zero JS handler. **Never changes language**, ever. |
| 3 | `_Layout.cshtml` — all `aria-label`s (`প্রধান মেনু`, `মুক্ত আইন হোম`, `থিম পরিবর্তন`, `ব্যবহারকারী মেনু`, `মেনু খুলুন`, `মেনু বন্ধ করুন`, `মোবাইল মেনু`) | Never translated — screen-reader users in English mode still hear Bangla labels. |
| 4 | `_Layout.cshtml:43` `<html lang="bn">` + nav links rendered server-side in Bangla (`রিভিউ কিউ`, `আইন খুঁজুন`, `সাইন ইন`…) | Hardcoded default is always Bangla; JS rewrites via `innerHTML` after `DOMContentLoaded`. Users with English saved as their preference see a **flash of Bangla nav on every single page load** before JS fixes it. |
| 5 | `AdminController.cs:312,322,337,424,433,443` | `TempData["Success"/"Error"]` set to **English-only** text ("Mapping added.", "Order refunded (sandbox)…") with no matching `*En` key, unlike every other controller. In Bangla mode, admins get English toast popups mid-Bangla UI. |
| 6 | `chat.js:262,270` | `btn.textContent = "নথি তৈরি করুন";` — plain Bangla, run after the button was correctly set up with a `data-bn/data-en` span at line 124. After a draft-generation error/completion, the button is **locked to Bangla permanently**, even in English mode. |
| 7 | `chat.js` — quick-reply chips (`নথি বানাতে চাই`/`আরও প্রশ্ন আছে`/`না, ধন্যবাদ`, line 95), quota-exceeded action links (149-151), cached-answer note (66-68), keyword-fallback note (76), "conversation not found" toast (322), search-mode hint toast (378) | All hardcoded Bangla-only, injected dynamically — **never respect the language toggle** regardless of what the rest of the page shows. |
| 8 | `chat.js:212,408,442` etc. | Error/status strings hardcoded as `"...বাংলা... / ...English..."` (both languages glued together with a slash) — a third, inconsistent bilingual style that ignores the toggle entirely and just always shows both. |
| 9 | `main.js:1375,1384` | Stray **U+3002 IDEOGRAPHIC FULL STOP ("。")** (CJK character) instead of a period/দাঁড়ি in two FAQ answers on the About page — an encoding/copy-paste artifact. |
| 10 | `main.js:1454-1467` ("Slashed Labels" rule) | Generic rule that splits any `<label>` text containing `/` and shows one half per language — only runs on plain-text labels with no children, mutates `dataset.original` once. Combined with the other inconsistent patterns above, label text ends up inconsistent across pages that look superficially similar. |

## Dead code (currently harmless, but a landmine + wasted effort)

Once a view is migrated to `data-bn`/`data-en`, the matching legacy block in `main.js` still runs on every toggle and **re-sets the same nodes from separately-typed hardcoded strings**. Today the two copies happen to agree; the moment either copy is edited without the other, translations silently diverge with no build check to catch it.

- `main.js:558-693` — entire "Home Page" block (kicker/title/composer tabs/step cards/category cards/security section) targets `.page-head`, `.grid-3 .card`, `#categories .grid-4 a.cat-card`, `section.card` — **none of this markup exists anymore**; `Home/Index.cshtml` was fully rebuilt as a chat-first page (`chat-shell`, `#chat-thread`, `#composer-mode`) and correctly uses `data-bn/data-en` throughout. ~135 lines, 100% unreachable.
- `main.js:997-1132` — Admin Dashboard block duplicates what `Admin/Dashboard.cshtml` (61 `data-bn` attrs) already handles natively.
- `main.js:1134-1171` — Admin Analytics block duplicates what `Admin/Analytics.cshtml` (52 `data-bn` attrs) already handles natively.
- `main.js:947-996` — most of the `/lawyer` block duplicates or conflicts with `Lawyer/Queue.cshtml`'s own `data-bn` markup (see bug #1).

**Recommendation:** finish the migration — convert the remaining legacy-style blocks to `data-bn`/`data-en` in the views, then delete the corresponding JS blocks. This removes the entire class of "translation drifted from markup" bugs at once.

## Pages with zero translation support

Checked by counting `data-bn=` occurrences per view and cross-checking against `main.js` route branches:

| Page | `data-bn` count | Status |
|---|---|---|
| `Account/ForgotPassword.cshtml` | 0 | 100% hardcoded Bangla, no JS branch — confirmed via full read. |
| `Account/ResetPassword.cshtml` | 0 | Same — fully hardcoded Bangla, no JS branch. |
| `Admin/AiLogs.cshtml` | 0 | Fully English-hardcoded, no JS branch. |
| `Admin/Categories.cshtml` | 0 | Same. |
| `Admin/Corpus.cshtml` | 0 | Same. |
| `Admin/Lawyers.cshtml` | 0 | Same. |
| `Admin/Scenarios.cshtml` | 0 | Same. |
| `Admin/Transactions.cshtml` | 0 | Same. |
| `Admin/Users.cshtml` | 0 | Confirmed fully English-hardcoded (`User Management`, `Suspend`, `Activate`, `data-confirm="Suspend this account?..."`). |

**Net effect: 7 of 9 Admin pages, plus both password-recovery pages, never change language at all.** The language toggle is still shown on all of them (inherited from `_Layout`), which is actively misleading — it implies the page will translate and it won't.

## Static / mock content & general inconsistency

- **Raw hex colors bypassing the design-token system**: `main.css` defines a full token set (`--border-strong: #92743a`, `--primary: #5c4715`, etc. at lines 12-47) but ~30+ component rules hardcode the same or *near*-same values directly instead of referencing the variable — e.g. `.nav-links a[aria-current="page"] { color: #4a3812; }` (`main.css:240`) is a color that exists nowhere in `:root`, distinct from `--primary`. If the brand color token is ever changed, buttons update but nav highlight/brand text/avatar background silently don't — pinned to an undocumented one-off hex. Same pattern in `.avatar`, `.menu-pop`, `.drawer`, `.nav-adminbar` (`main.css:220-346`).
- **Confirm dialogs never translate**: `[data-confirm]` (`main.js:1642`) calls `window.confirm(el.dataset.confirm)` verbatim — whatever language the view author hardcoded into `data-confirm` is what shows, forever (e.g. `Admin/Users.cshtml:60` is English-only).
- **`<meta name="description">` and page `<title>`** (`_Layout.cshtml:47-48`) are static Bangla, never localized.
- **Dead/unused dictionary key**: `main.js:58` defines `"nav-mycases": "আমার মামলাসমূহ"/"My Cases"` but no code path ever applies it — the actual case-tracking nav link uses `"nav-tracking"` ("Case Tracking"/"মামলা ট্র্যাকিং") instead, while the server-rendered label is a third variant, "আমার মামলা" (`_Layout.cshtml:100`). Three different labels for the same link depending on where you look.
- **Translation drift, not just missing**: `"cat-badge-draft"` (`main.js:188/363`) is "স্বয়ংক্রিয় খসড়া ফরম্যাট" (auto-generated draft format) in Bangla but "Standard Legal Templates" in English — not the same claim, just loosely paired.

## Suggested pages / product decisions

No functional gap needs a brand-new controller/feature — `PaymentController` and `ChatController` are intentionally API-only (no views needed, confirmed). Two things are worth a deliberate decision rather than leaving as an accident:

1. **No public lawyer directory/roster.** The product story ("verified advocates", a dead-code JS key literally named `home-sec-btn2: "Verified Advocates"`) implies citizens might want to see who's reviewing cases, but there's no page for it — only the lawyer-facing `Lawyer/Status` (their own verification state). Decide: add one, or drop the copy that implies it exists.
2. **The Admin-section language toggle should probably be hidden**, not "fixed" — 7/9 admin pages are deliberately English-only (internal tool). Showing a working-looking language toggle there is itself the inconsistency, not the missing translations underneath it.

## Recommended fix order

1. Real, currently-visible bugs first: items **#2, #3, #4, #5, #6** in the table above.
2. The two fully-untranslated password-recovery pages (`ForgotPassword`, `ResetPassword`).
3. Dead-code cleanup as its own pass (delete the `main.js` blocks listed above) — keep separate from (1)/(2) so it's easy to review in isolation.
4. Decide and act on the two product-decision items (lawyer directory, admin toggle visibility).

---

## Fix checklist

One box per distinct change called out above. Checked = verified fixed in the current tree as of 2026-09-09 (re-verify before trusting a checked box if more time has passed).

### Confirmed active bugs (table, "Confirmed active bugs")

- [x] **#1** Lawyer-portal kicker overwrite — `main.js:947-957` clobbering `Lawyer/Queue.cshtml`'s real kicker with a fabricated label
- [ ] **#2** `.nav-adminbar` links never translate — *partially changed, not fixed*: admin links were folded into the primary nav (`8c12c1a`) so the old `.nav-adminbar` block is gone, but the new admin links in `_Layout.cshtml:77-89,175-182` are still hardcoded English with zero `data-bn`/`data-en` — same bug, new location
- [ ] **#3** `_Layout.cshtml` `aria-label`s never translated (`প্রধান মেনু`, `মুক্ত আইন হোম`, `থিম পরিবর্তন`, `ব্যবহারকারী মেনু`, `মেনু খুলুন`, `মেনু বন্ধ করুন`, `মোবাইল মেনু`) — still Bangla-only, confirmed at `_Layout.cshtml:69,74,122,134,151,163,168,232`
- [ ] **#4** `<html lang="bn">` + hardcoded-Bangla nav flash before JS retranslates — `_Layout.cshtml:43` unchanged
- [ ] **#5** `AdminController` English-only toasts (no `*En`/`*Bn` pairing) — still present: `Mapping added.`, `Mapping deleted.`, `Order refunded (sandbox)…`, `Payout marked paid (sandbox).`, `Order marked Paid (sandbox gateway).`, and `Keyword and section are required.` (`AdminController.cs:346,356,371,458,467,477`)
- [x] **#6** `chat.js` draft-submit button locked to Bangla after error/completion — fixed in `535df3e` (button now re-renders via `curLang()` + `data-bn`/`data-en` span, `chat.js:145,301-302`)
- [x] **#7** Chat dynamic strings ignoring the toggle (quick-reply chips, quota-wall links, cached/retrieval-only notes, category chips) — fixed in `535df3e` ("make the language toggle reach dynamic chat content")
- [x] **#8** Glued `"বাংলা / English"` bilingual strings in `chat.js` — no remaining matches; resolved as part of `535df3e`
- [ ] **#9** Stray U+3002 ("。") CJK full stops in About-page FAQ answers — still present at `main.js:1387,1396`
- [ ] **#10** "Slashed Labels" generic-rule inconsistency — rule still in place unchanged at `main.js:1466-1473`

### Dead code cleanup

- [ ] Delete unreachable "Home Page" block (`main.js:558-693`) — targets markup that no longer exists
- [ ] Delete Admin Dashboard duplicate block (`main.js:997-1132`)
- [ ] Delete Admin Analytics duplicate block (`main.js:1134-1171`)
- [ ] Delete/reconcile the `/lawyer` block (`main.js:947-996`) now that bug #1's kicker overwrite is fixed — the rest of the block still duplicates `Lawyer/Queue.cshtml`'s own `data-bn` markup

### Pages with zero translation support

- [ ] `Account/ForgotPassword.cshtml` — still 0 `data-bn`
- [ ] `Account/ResetPassword.cshtml` — still 0 `data-bn`
- [ ] `Admin/AiLogs.cshtml` — still 0 `data-bn`
- [ ] `Admin/Categories.cshtml` — still 0 `data-bn`
- [ ] `Admin/Corpus.cshtml` — still 0 `data-bn`
- [ ] `Admin/Lawyers.cshtml` — still 0 `data-bn`
- [ ] `Admin/Scenarios.cshtml` — still 0 `data-bn`
- [ ] `Admin/Transactions.cshtml` — still 0 `data-bn`
- [ ] `Admin/Users.cshtml` — still 0 `data-bn` (confirmed hardcoded English + English-only `data-confirm` at line 60)

### Static / mock content & general inconsistency

- [ ] Raw hex colors bypassing the design-token system (`main.css` ~30+ rules, e.g. `.nav-links a[aria-current="page"]` at `main.css:240`)
- [ ] `[data-confirm]` dialogs never translate (`main.js:1642`, e.g. `Admin/Users.cshtml:60`)
- [ ] `<meta name="description">` / page `<title>` static Bangla, never localized (`_Layout.cshtml:47-48`)
- [ ] Dead/unused `nav-mycases` dictionary key vs. three divergent labels for the same link (`nav-tracking` in `main.js`, `আমার মামলা` server-rendered in `_Layout.cshtml:114,205`) — all three still present, unreconciled
- [ ] Translation drift on `"cat-badge-draft"` (Bangla ≠ English meaning) — unresolved

### Product decisions (not bugs — deliberate calls)

- [ ] Decide: add a public lawyer directory/roster, or drop the copy implying one exists
- [ ] Decide: hide the language toggle on the 7 admin-only pages instead of "fixing" their translation
