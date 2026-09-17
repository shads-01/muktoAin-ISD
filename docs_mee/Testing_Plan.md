# MuktoAin — Lab 6 Testing Plan (Unit → Integration → System → Acceptance)

**Purpose:** Execution plan for the Lab 6 deliverable — "test your system thoroughly using normal, boundary, exceptional, and invalid cases" across the four SDLC testing levels, then submit a report with test cases, results, bugs found, and fixes/improvements (with screenshots).

**Scope:** All features currently implemented and runnable end-to-end (see §2 for what's in/out of scope). Manual testing is explicitly accepted by the instructor — this plan is manual-first for System/Acceptance level, and leans on the existing xUnit suites for Unit/Integration level.

**Non-goal:** This plan does not write production code or fix bugs. It tells you exactly what to click, what to type, what to run, and what to write down. Bugs found get *documented with a fix recommendation* per the assignment, not necessarily fixed in-session (see §7 for two bugs already known and root-caused).

---

## 1. How the four levels map onto this codebase

| SDLC Level | What it means here | Who/what does it | Where results go |
|---|---|---|---|
| **Unit** | Individual service/class methods in isolation (mocked dependencies) | Already-written xUnit tests in `tests/MuktoAin.UnitTests/` (23 test classes, ~190 tests as of the last recorded run) | `dotnet test` console output → §4 |
| **Integration** | Two or more real components wired together: Controller→Service→Repository→DB, or the RAG pipeline (Gemini + Qdrant + SQL FTS fallback) | Existing `tests/MuktoAin.IntegrationTests/AiPipeline/RagRetrievalSmokeTests.cs` **plus** manual checks against a real running app + local SQL Server/Qdrant | `dotnet test` output + manual notes → §4, §5 |
| **System** | The whole app tested as a black box through the browser — full user journeys across the Razor MVC UI | Manual, click-through testing against `dotnet run` | Screenshots + result tables → §5 |
| **Acceptance** | Does it satisfy the functional requirements (FR-1…FR-18, FR-24) from an end-user's point of view? | Manual walkthrough per persona (Citizen / Lawyer / Admin), checked against `.agent/spec/requirements.md` | Pass/Fail matrix → §6 |

---

## 2. Scope — what to test

Test only what is actually implemented and wired up (checking `plans/Dependency_plan.md` first, per this repo's tracking rule). As of this plan:

**In scope (implemented, wired to real services):**
- Auth & accounts: `AccountController` (Login, Register, Profile, ChangePassword, ForgotPassword, RequestPayout)
- Case lifecycle: `CaseController` (Submit, Result, SaveDraft, SendToLawyer, Withdraw, Track)
- Chat / RAG / AI: `ChatController` (New, Ask, Messages, Recent, Quota, Commit)
- Standalone Acts search (FR-7): `SearchController`
- Category browsing (FR-6): `CategoryController`
- Document preview/download + PDF gate + real PDF rendering (FR-9): `DocumentController` / `PdfExportService` (QuestPDF, Bengali font — `A-2.5` is done, no longer a stub)
- Lawyer flow: `LawyerController` (Status, Resubmit, Queue, Claim, Review, SubmitReview)
- Admin console: `AdminController` (Dashboard, Analytics, HealthStatus, EmbeddingProgress, Users, Suspend, Lawyers, VerifyLawyer, Corpus, Scenarios)
- Sandbox payments (FR-24): `PaymentController` (Honorarium, TopUp, Status)
- Cross-cutting: 3-surface disclaimer policy, bn/en localization, guest/anonymous access

**Out of scope for this lab pass (not yet implemented per `plans/Dependency_plan.md` Checkpoint 3 — don't write test cases you can't execute):**
- `ActsManagementService` admin CRUD re-indexing (`T-3.1`)
- `ScenarioMappingService` admin boosts (`T-3.2`)
- `ModerationService` blocklist filter (`A-3.5`)
- QA benchmark runner against the 2,165-question dataset (`S-3.1`–`S-3.3`) — that's a separate NLP evaluation workstream, not this lab's testing report

If a feature above turns out partially wired when you sit down to test it, note that as a finding rather than skipping it silently.

---

## 3. Test environment setup (do this once, screenshot it for the report's "Test Environment" section)

1. `dotnet build` the solution — confirm 0 errors (screenshot).
2. Confirm `appsettings.Development.json` has a valid Gemini API key and Qdrant endpoint, and SQL Server (LocalDB/Express) is reachable — run `dotnet run --project src/MuktoAin.Web`.
3. Confirm seed data present: 1,484 Acts / 35,633 Sections / 64 Districts / categories (per `[T-1.15]` exit-gate note in `plans/Dependency_plan.md`).
4. Confirm embedding backfill state — the full corpus (42,858 chunks) is now embedded in Qdrant (backfill from `[[rag-pipeline-latent-bugs-2026-08-30]]` has completed), so Chat/Case queries should hit the **vector** path by default; still record which path (vector vs. FTS fallback) was actually exercised per test case (see CHAT-05, CASE-05 below), since a genuinely unmatched query can still fall back.
5. Record: OS, .NET SDK version, browser used for manual testing, DB name.

---

## 4. Automated tests (Unit + Integration level)

### 4.1 Run and capture

```bash
dotnet test tests/MuktoAin.UnitTests --logger "console;verbosity=detailed"
dotnet test tests/MuktoAin.IntegrationTests --logger "console;verbosity=detailed"
```

For the report: capture the pass/fail summary line (`Passed! - Failed: X, Passed: Y, Skipped: Z`) as a screenshot, plus the full list of test names (already effectively your Unit-level test case table — cite the file names, don't retype every assertion).

### 4.2 Existing coverage inventory (cite this table in the report as "Unit tests already in place")

| Area | Test file | Notable exceptional/boundary cases already covered |
|---|---|---|
| AI logging | `AiLogServiceTests.cs` | PII redaction before persisting |
| AI orchestration | `AiOrchestrationServiceTests.cs` | cache hit/miss, pipeline failure paths |
| Case lifecycle | `CaseServiceTests.cs` | guest vs owner access rules, state-machine transitions incl. `Finalized→Submitted` reopen |
| Categories | `CategoryServiceTests.cs` | CRUD passthrough |
| Disclaimer injection | `DisclaimerInjectorTests.cs` | bilingual injection |
| Document generation | `DocumentGeneratorTests.cs`, `DocumentServiceTests.cs` | template dictionary lookup, immutable `ContentDraft` |
| Embedding batch | `EmbeddingBatchJobTests.cs`, `EmbeddingQuotaStateTests.cs` | quota exhaustion handling |
| Encryption | `EncryptionServiceTests.cs` | Bangla/English roundtrip, empty/null safety |
| Gemini client | `GeminiClientTests.cs`, `GeminiQuotaRetryInfoParsingTests.cs`, `GeminiResiliencePoliciesTests.cs` | 429 quota exhaustion + key rotation, retry/circuit-breaker |
| Keyword search | `KeywordSearchServiceTests.cs`, `SearchServiceTests.cs` | quote-escaping, blank-query short-circuit, AND vs OR term matching |
| Lawyer verification | `LawyerVerificationServiceTests.cs` | duplicate-application guard |
| Prompt assembly | `PromptAssemblerTests.cs` | grounding from retrieved sections |
| Rights explanation | `RightsExplanationServiceTests.cs` | — |
| RAG context building | `RagContextBuilderTests.cs` | vector hit, empty fallback, throw fallback, topK propagation, blank query |
| Similarity search | `SimilaritySearchServiceTests.cs` | chunk-to-section dedupe |
| User management | `UserManagementServiceTests.cs` | suspension, admin-protection guardrails |
| Controllers | `AccountControllerTests.cs`, `DocumentControllerTests.cs` | — |
| RAG smoke (integration) | `RagRetrievalSmokeTests.cs` | vector-primary retrieval, FTS fallback trigger, key rotation on quota exhaustion |

### 4.3 Exceptional-input gap tests — done (2026-09-09), plug the coverage the instructor specifically penalizes for

| New test | File | Case type | Status |
|---|---|---|---|
| Register with duplicate email | `AccountControllerTests.cs` | Exceptional | ✅ Added — asserts field error, no duplicate profile created |
| Register with mismatched confirm password | `AccountControllerTests.cs` | Invalid | ✅ Added (weak-password half already existed as `Register_WhenIdentityPasswordPolicyFails_MapsErrorToPasswordField`) |
| `CaseService` submit with null/empty description | `CaseServiceTests.cs` | Boundary/Invalid | ✅ Added — **but documents a real gap, not a rejection**: `CaseSubmitViewModel.Description` has no `[Required]` and `CaseService` has no null/empty guard, so an empty description is silently accepted and persisted today. Cite this as a finding in §7/Bugs & Issues, not a passing exceptional-input case. |
| `CaseService` submit with description at/over max DB column length | — | Boundary | **Dropped** — `CASE.Description` is `NVARCHAR(MAX)`, no length boundary exists to hit |
| `SearchService` with page = 0 / negative page | — | Boundary | **Already covered** by pre-existing `SearchActsAsync_InvalidPage_FallsBackToPageOne` |
| `PaymentController`: negative or zero amount | `PaymentControllerTests.cs` (new file) | Invalid | ✅ Added — `Honorarium`/`TopUp` both reject `Amount <= 0` with 400, no order row written |
| Non-admin reaches `/Admin/*` (incl. `VerifyLawyer`) | `AdminControllerTests.cs` (new file) | Exceptional | ✅ Added as a declarative check — `LawyerVerificationService.VerifyAsync` takes no caller role, the real gate is `[Authorize(Roles="Admin")]` on `AdminController` itself; test confirms that attribute is present and scoped to Admin |
| `KeywordSearchService`/FTS with SQL-wildcard/special characters (`%`, `_`, `'`) | `KeywordSearchServiceTests.cs` | Invalid | ✅ Added — confirms these pass through as literal quoted terms (not FTS operators, so nothing to escape); double-quote handling was already covered |

6 of the original 8 gaps landed as real new tests (8 new `[Fact]`/`[Theory]` methods, 12 test-case executions); 2 were dropped because the underlying premise didn't hold once checked against the actual schema/service. Full suite after these additions: **222 unit tests, 221 passed, 1 pre-existing unrelated failure** (`GeminiClientTests.Snapshot_AfterRequests_CountsOnlyTheKeyThatWasActuallyCalled`, `ObjectDisposedException` on a reused `StringContent` across a Polly retry — reproduces in isolation, untouched by this pass). Integration: 2/2 passed.

---

## 5. Manual test case catalog (System level — click through `dotnet run` in a browser)

Legend for **Type**: **N** = Normal, **B** = Boundary, **E** = Exceptional (system-level failure/edge condition), **I** = Invalid input.

### 5.1 Authentication & Account (`AccountController`)

| ID | Type | Scenario | Steps | Expected result |
|---|---|---|---|---|
| ACC-01 | N | Register a new citizen | Fill valid name/email/password, submit | Account created, redirected to login/home, can log in |
| ACC-02 | I | Register with duplicate email | Register twice with same email | Friendly validation error, no crash, no duplicate account |
| ACC-03 | E | Register with script/HTML in name field | Name = `<script>alert(1)</script>` | Value is stored/rendered **encoded**, no script execution (XSS check) |
| ACC-04 | N | Login with correct credentials | Valid email/password | Redirect to home/dashboard, session established |
| ACC-05 | I | Login with wrong password / nonexistent email | Try both: correct email + wrong password, then a random email | Same generic "invalid credentials" message both times (must **not** reveal whether the email exists) |
| ACC-06 | B | Repeated failed logins (lockout boundary) | Fail login 5+ times rapidly | Check whether ASP.NET Identity lockout kicks in as configured; document actual vs expected behavior |
| ACC-07 | E | Access `/Account/Profile` while logged out | Navigate directly (unauthenticated) | Redirected to login, not a 500/exception |
| ACC-08 | E | Non-lawyer requests payout | Logged in as Citizen, POST `/Account/RequestPayout` (e.g. via browser devtools or direct URL) | Rejected — authorization/role check, not silently processed |

### 5.2 Case Submission & Lifecycle (`CaseController`)

| ID | Type | Scenario | Steps | Expected result |
|---|---|---|---|---|
| CASE-01 | N | Submit case (Bangla and/or English) | Realistic Bangla or English legal problem description | Case created, redirected to `/Case/Result` with rights explanation + cited sections |
| CASE-02 | I | Submit with empty description | Blank textarea, submit | **Known defect** — currently accepted with no rejection (see §7); confirm it's still the actual live behavior |
| CASE-03 | E | Submit with an invalid/nonexistent category id | Tamper `cat` query param to a nonexistent id | Handled gracefully (ignored or error page), not a 500 |
| CASE-04 | N | Guest (anonymous) submission | Submit without logging in | Case created with `IsAnonymous=true`, tracking code shown **once** |
| CASE-05 | E | Result page — cited legal provisions | Submit a labour-law-shaped complaint, view `/Case/Result` | Sections are actually cited (regression check — see §7, bug #1/#2); "FACTS OF THE CASE" shows **plain text**, not ciphertext |
| CASE-06 | I | Track case with a malformed or nonexistent code | Garbage string, then a random valid-format GUID | Friendly "not found" both times, never an unhandled exception |
| CASE-07 | E | Access another user's case `Result` by guessing the id | Log in as User A, browse to User B's case id | Access denied, not data leakage |
| CASE-08 | B | `/Case/Track` list pagination boundary | As a citizen with 10+ cases, request `?page=0` and `?page=9999` | Clamped to a valid page (never an exception or empty crash), 10 rows/page |

### 5.3 Chat / RAG / AI Pipeline (`ChatController`)

| ID | Type | Scenario | Steps | Expected result |
|---|---|---|---|---|
| CHAT-01 | N | Start new chat + ask a legal question | `POST /Chat/New`, then `Ask` with a real question | Grounded answer with citations + disclaimer text present |
| CHAT-02 | I | Ask with empty message body | `Ask` with `{}` or empty string | Validation error, no Gemini call, no crash |
| CHAT-03 | E | Ask against a nonexistent/foreign chat id | Use a chat id belonging to another user or that doesn't exist | Rejected, not cross-user data leakage |
| CHAT-04 | B | Hit the free-tier message quota (`/Chat/Quota` boundary) | Send messages until quota, then one more | Correct capped-tier behavior (per R-13, `Tier="full"` should still show correctly once capped=false is set — verify no regression) |
| CHAT-05 | E | Gemini/Qdrant unavailable simulation | Temporarily point config at a bad Qdrant URL or invalid API key, ask a question | Falls back to SQL FTS gracefully (per architecture rule 3) or fails with a user-visible error — not a raw exception page |
| CHAT-06 | N | `Commit` a chat into a formal Case | Complete a chat, then commit | Case created from chat content correctly |

### 5.4 Standalone Acts Search (`SearchController`, FR-7)

| ID | Type | Scenario | Steps | Expected result |
|---|---|---|---|---|
| SRCH-01 | N | Search a real legal keyword | Query = `শ্রম` or `labour` | Relevant sections returned |
| SRCH-02 | I | Empty query | Submit blank search | Renders blank/prompt template, **not** a 500 (this is the R-9 fix — regression-test it) |
| SRCH-03 | I | Query with SQL wildcard/special characters | `%`, `_`, `"`, `O'Brien`-style apostrophe | No exception, properly escaped, sane result set |
| SRCH-04 | B | Pagination: `page=0` and page far beyond last page | `?q=labour&page=0`, `?q=labour&page=9999` | Clamped to a valid page on the low end, friendly "no more results" on the high end — never an exception |

### 5.5 Category Browsing (`CategoryController`, FR-6)

| ID | Type | Scenario | Steps | Expected result |
|---|---|---|---|---|
| CAT-01 | N | Browse category index | `/Category` | All seeded categories listed |
| CAT-02 | N | View valid category details | `/Category/Details/{validId}` | Category detail + related acts shown |
| CAT-03 | I | View nonexistent/invalid category id | `/Category/Details/999999`, `/Category/Details/0` | 404/friendly not-found, not a 500 |

### 5.6 Document Preview/Download & PDF Gate (`DocumentController`, FR-9)

| ID | Type | Scenario | Steps | Expected result |
|---|---|---|---|---|
| DOC-01 | N | Preview a generated draft | `/Document/Preview/{id}` for an existing draft | Draft content + Surface-3 disclaimer stamp visible |
| DOC-02 | E | Download before lawyer approval | Attempt `/Document/Download/{id}` while status is `Draft` | Locked/blocked — download must require `Approved` status |
| DOC-03 | N | Download after approval | Approve via lawyer flow first, then download | Valid PDF file (`%PDF` header, opens cleanly), correct Bengali text rendering, disclaimer stamp present on every page |
| DOC-04 | I | Preview/download nonexistent document id | Random id | Friendly not-found, not a 500 |
| DOC-05 | E | Download with Bengali filename | Download a document whose title contains Bengali text | Filename renders correctly (UTF-8 `Content-Disposition`, per R-10 — regression-test it) |

### 5.7 Lawyer Flow (`LawyerController`)

| ID | Type | Scenario | Steps | Expected result |
|---|---|---|---|---|
| LAW-01 | N | View own verification status | Logged in as a lawyer, `/Lawyer/Status` | Correct status shown |
| LAW-02 | E | Unverified lawyer accesses the review queue | Log in as a lawyer whose `VerificationStatus != Approved`, browse `/Lawyer/Queue` | Denied or empty queue with explanation, not raw data exposure |
| LAW-03 | B/E | Two lawyers claim the same document near-simultaneously | Open the same doc in two sessions, claim both quickly | Only one claim should succeed — `A-2.7` is now marked complete per `plans/Dependency_plan.md` (`ClaimAsync` checks `AssignedLawyerProfileId` before assigning), but it's a check-then-set guard with no DB-level concurrency token (`RowVersion`), so two truly simultaneous requests could still both pass the check — document the actual behavior; if both succeed, that's a bug to report |
| LAW-04 | I | Submit review rejection without mandatory comment | Leave comment blank on reject | Blocked — comment is mandatory per FR-14 |
| LAW-05 | N | Edit-and-approve flow | Edit draft text, approve | Both `ContentDraft` (original) and `ContentFinal` (edited) preserved separately |
| LAW-06 | E | Non-lawyer accesses `/Lawyer/*` routes | Log in as Citizen, browse `/Lawyer/Queue` | Redirected/403, not exposed |

### 5.8 Admin Console (`AdminController`)

| ID | Type | Scenario | Steps | Expected result |
|---|---|---|---|---|
| ADM-01 | N | View dashboard KPIs | `/Admin/Dashboard` as Admin | Real counts shown (per R-3 — no more `MockData`) |
| ADM-02 | E | Non-admin accesses `/Admin/*` | Log in as Citizen or Lawyer, browse any admin route | 403/redirect, never the page content |
| ADM-03 | E | Admin suspends another admin / self-suspend | Attempt to suspend the currently logged-in admin account, or another admin | Blocked by admin-protection guardrail (per existing `UserManagementService` tests — verify it holds at the controller/UI level too) |
| ADM-04 | N | Approve a pending lawyer verification | `/Admin/Lawyers`, approve one | `VerificationStatus=Approved`, `VerifiedByAdminId`/`VerifiedAt` stamped |
| ADM-05 | N | View corpus stats | `/Admin/Corpus` | Correct aggregate counts (1,484 Acts / 35,633 Sections etc.), loads fast (per R-14 DB-side aggregation fix — regression-test that it doesn't regress to an in-memory 42K-entity load) |
| ADM-06 | I | `/Admin/VerifyLawyer` with a nonexistent `lawyerProfileId` | Tamper the id | Handled gracefully, not a 500 |

### 5.9 Sandbox Payments (`PaymentController`, FR-24)

| ID | Type | Scenario | Steps | Expected result |
|---|---|---|---|---|
| PAY-01 | N | Honorarium payment on an approved case | Trigger the honorarium modal on an approved case, submit valid sandbox payment | Payment recorded, status reflects success |
| PAY-02 | E | Honorarium payment on a non-approved case | Attempt the honorarium flow before the case is approved | Blocked — case must be approved first |
| PAY-03 | I | Negative or zero amount | POST `Honorarium`/`TopUp` with `amount=-100` or `0` | Rejected, no payment row written |
| PAY-04 | E | Simulated sandbox payment failure | Use whatever "fail" test path the sandbox provider exposes | Failure surfaced to the user clearly, no false "success" state |

### 5.10 Cross-cutting: Disclaimer, Localization, Error Pages

| ID | Type | Scenario | Steps | Expected result |
|---|---|---|---|---|
| GEN-01 | N | Disclaimer banner present + injected into AI output/documents | Load a few pages, then a Chat/Result response and a document preview | Non-dismissible banner on every page; disclaimer text present in the AI response and the drafted document |
| GEN-02 | N | Language toggle bn ↔ en | Switch language on a page with `_LanguageToggle.cshtml` | UI strings switch correctly, no missing-resource fallback text (`[[key]]`-style placeholders) |
| GEN-03 | E | Navigate to a nonexistent route | `/this-does-not-exist` | Custom `HomeController.NotFound`/404 page, not the default IIS/Kestrel error |
| GEN-04 | E | Force a server error | Trigger any known error path (e.g. malformed id causing an unhandled exception, if found) | Custom `ServerError`/`Error` page shown, no stack trace leaked to the browser in a non-dev environment |

---

## 6. Acceptance-level pass (map to FRs, one pass per persona)

Walk through as each persona **in one sitting** and fill in Pass/Fail + notes against `.agent/spec/requirements.md`:

| Persona | Journey | FRs exercised |
|---|---|---|
| **Guest citizen** | Submit a case anonymously → get rights explanation → track by code → (cannot download until approved) | FR-1 (guest), FR-2, FR-3, FR-4, FR-8, FR-9, FR-11 |
| **Registered citizen** | Register → submit case → chat follow-up → send to lawyer → view approved doc → download PDF → pay honorarium | FR-1, FR-2 through FR-5, FR-8, FR-9, FR-24 |
| **Citizen (search-only)** | Browse categories → search Acts directly without submitting a case | FR-6, FR-7 |
| **Lawyer** | Apply for verification → get approved (as admin) → claim a document → review/edit/approve → reject another | FR-13, FR-14, FR-15 |
| **Admin** | Review dashboard/analytics → manage users (suspend/unsuspend) → verify a lawyer → check corpus stats | FR-16, FR-18 |

Produce one Pass/Fail/Partial row per FR with a one-line justification — this table **is** your Acceptance Testing section in the report.

---

## 7. Known-bug regression cases (already root-caused — verify they stay fixed, and use them as worked examples in the report's "bugs found" section if you re-discover any)

From `[[rag-pipeline-latent-bugs-2026-08-30]]`, three stacked bugs previously made "Cited Legal Provisions" always empty on `/Case/Result`:

1. **Encryption bug** — `RightsExplanationService`/`DocumentService` were feeding raw ciphertext (instead of decrypted text) into the RAG query and into "FACTS OF THE CASE". *Regression case: CASE-05 above.* If ciphertext reappears anywhere in a rendered page, that's the bug back.
2. **Keyword-fallback AND-logic bug** — the FTS fallback ANDed every word of a full case description together, guaranteeing zero matches. *Regression case: CASE-01/CASE-05, submit a full-sentence case description and confirm sections come back even when the vector path is empty.*
3. **Gemini/Qdrant vector-dimension mismatch** — embeddings were generated at 3072-dim against a 768-dim Qdrant collection, so every embed upsert silently failed 100% of the time. *Regression case: CHAT-01/CASE-01 with a query matching any act now returns vector-path results, not just FTS fallback (check `/Admin/EmbeddingProgress` too if time allows, since it's this bug's most direct indicator — the full corpus, 42,858 chunks, is embedded as of the completed backfill).*

Since the full backfill has completed, most Chat/Case queries should land on the vector path. If a query still falls back to FTS (e.g., no close semantic match), that's expected, not a bug — document which path you exercised for each Chat/Case test case so the report doesn't misreport a fallback hit as a vector-path failure.

---

## 8. Report deliverable structure (map directly to the assignment's checklist)

1. **Introduction** — system overview (from `AGENTS.md` §1), scope of this test pass (§2 above).
2. **Test Environment** — §3 details + screenshot of successful build/run.
3. **Testing Strategy** — the level-mapping table from §1, and why manual testing was chosen for System/Acceptance (instructor-approved).
4. **Unit Test Results** — §4.1 summary screenshot + §4.2 table (cite files, don't retype every assertion) + §4.3 new tests added, with before/after pass counts.
5. **Integration Test Results** — `RagRetrievalSmokeTests` results + any manual integration checks (DB, Qdrant/FTS fallback trigger — CHAT-05).
6. **System Test Results** — every table in §5, each row filled with **actual result** + **screenshot** (one screenshot per row is overkill; group screenshots per feature area, but every *E*/*I* row that reveals unexpected behavior gets its own screenshot).
7. **Acceptance Test Results** — §6 Pass/Fail/Partial matrix.
8. **Bugs & Issues Found** — for each: symptom, exact repro steps, screenshot, root cause (if found), and a **fix/improvement recommendation** (this is explicitly graded — don't skip the recommendation even for bugs you don't fix).
9. **Exceptional-Input Coverage Summary** — a short explicit callout listing every E/I-type row tested, since the instructor penalizes heavily for missing this — make it easy for a grader to see coverage at a glance.
10. **Conclusion** — overall system quality assessment, what's not yet testable (§2 out-of-scope items) and why.

**Screenshot discipline:** for every row you screenshot, capture (a) the input you entered, (b) the resulting page/response, and (c) dev tools network tab response code when testing an API-shaped endpoint (Chat/Payment) — that trio is what makes a screenshot actually verify the claimed result rather than just showing "a page."

---

## 9. Suggested execution order (checklist)

- [ ] §3 environment setup + screenshot
- [x] §4.3 write the 8 gap-filling unit tests
- [x] §4.1 run full automated suite, capture summary
- [ ] §5.1–5.2 Account + Case (core citizen flow first — everything downstream depends on a case existing)
- [ ] §5.3 Chat
- [ ] §5.4–5.5 Search + Category
- [ ] §5.6–5.7 Document + Lawyer (needs an approved/rejected case from §5.2 flow)
- [ ] §5.8 Admin
- [ ] §5.9 Payments
- [ ] §5.10 Cross-cutting
- [ ] §6 Acceptance persona walkthroughs
- [ ] §7 regression checks
- [ ] Assemble report per §8
