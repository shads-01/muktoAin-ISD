# Document Styling & Language Toggle — Design

Date: 2026-09-10
Status: Approved for implementation planning

## Problem

Generated legal documents (currently only `LabourComplaintTemplate`) read as
inconsistent:

1. **Mismatched language** — section headers are hardcoded in English
   (`"FACTS OF THE CASE:"`, `"APPLICABLE LEGAL PROVISIONS:"`,
   `"RELIEF SOUGHT:"`, `"DECLARATION:"`) while the surrounding legal prose
   (citizen's case description, cited statute text, Gemini's rights
   explanation) is in Bangla. English labels inside a Bangla legal letter.
2. **Stray markdown/random lines** — the AI-authored rights explanation
   (`explanation.Explanation`) is embedded verbatim into the plain-text
   document. If Gemini emits `**bold**`, `# heading`, or `- bullet` syntax,
   those characters print literally in a document meant to be plain text.
   Section dividers are `─` × 40 / `═` × 60 ASCII art, which reads as noise
   outside a true monospace render (web preview, PDF, narrow screens).
3. **No language toggle** — a document is only ever available in the
   language it was generated in (`Case.Language`), with no way for a viewer
   to see it in the other language.

This spec covers two related changes: fixing the styling/consistency
problems in the existing render path, and adding an on-demand,
AI-translated language toggle to document preview.

## Scope

In scope:
- Restructuring template output into ordered `{heading, body}` sections
  with bilingual (Bangla/English) static headers.
- Sanitizing AI-authored text embedded into a document (strip markdown
  syntax, collapse blank lines) plus a prompt-level instruction not to
  emit markdown in the first place.
- Replacing ASCII-art dividers with real CSS rules (web) / QuestPDF rules
  (PDF).
- An on-demand, cached, AI-translated language toggle in the document
  preview view (`Preview.cshtml`), covering the currently-implemented
  `LabourComplaint` document type.

Out of scope (explicitly deferred, not silently decided):
- Implementing the three missing document templates (`GeneralDiary`,
  `RtiRequest`, `ConsumerComplaint`) — separate task, tracked already as an
  open gap in `DocumentGenerator`.
- Translating the **PDF export**. The downloaded/approved PDF stays in the
  document's original generation language only — it is the
  lawyer-reviewed, authoritative artifact, and shipping an AI-translated
  version as if it carries the same approval status is a legal-risk
  decision this spec does not make. PDF export is unchanged by this work.
- A general i18n framework for the whole app. This is scoped to the
  document preview surface only.

## Architecture & components

**Template output shape**

`IDocumentTemplate.RenderAsync` changes from returning `Task<string>` to
`Task<IReadOnlyList<DocumentSection>>`, where:

```csharp
public record DocumentSection(string HeadingBn, string HeadingEn, string Body);
```

Headers are static strings the template already owns in both languages —
no AI or translation involved for headers. `DocumentGenerator.GenerateAsync`
joins the sections into the single string still persisted to
`GeneratedDocument.ContentDraft` (DB shape unchanged), rendered in whatever
language the case was generated in (`Case.Language`). `DocumentGenerator`
is also where `AiTextSanitizer` runs, over any section body sourced from AI
output, before the string is assembled.

**Sanitizer**

`AiTextSanitizer` (new, `MuktoAin.Application.Documents`): a small static
utility — strips leading `**`/`##`/`- `/`* ` markdown markers and collapses
3+ consecutive blank lines to one. Applied only to AI-authored spans
(e.g. `explanation.Explanation`), never to citizen-authored free text or
statute text, since sanitizing user input isn't this problem's cause.

`PromptTemplates.RightsExplanation` and `PromptTemplates.DocumentDrafting`
also gain a rule: "Return plain prose only — do not use markdown syntax
(no **, #, numbered/bulleted lists with -, or backticks)." The sanitizer is
the safety net, not the only fix.

**Translation**

`IDocumentTranslationService` (new, `MuktoAin.Application.Services`):

```csharp
Task<string> TranslateAsync(string content, string sourceLanguage, string targetLanguage, CancellationToken ct = default);
```

Implemented via the existing `IAiService`/Gemini path (same client used by
`AiOrchestrationService`), with a new `PromptTemplates.Translation`
template: literal translation only, explicitly forbidden from adding,
removing, or reinterpreting legal claims, must preserve Act names, section
numbers, and figures (money/dates) verbatim.

**Cache**

New `DocumentTranslation` entity/table:

| Column | Type | Notes |
|---|---|---|
| `DocumentTranslationId` | int, PK | |
| `DocumentId` | int, FK → `GeneratedDocument` | |
| `Language` | varchar(2) | `"bn"` / `"en"` |
| `TranslatedContent` | text | |
| `CreatedAt` | timestamp | |

Unique constraint on `(DocumentId, Language)`. New migration script
`scripts/11_document_translation.sql`, following the existing numbering
pattern.

**Endpoint**

`POST /Document/{id}/Translate?lang={bn|en}` on `DocumentController`:
1. Resolve the document via the same access check `Preview` already uses
   (404/403 on failure — no new access-control surface).
2. Check `DocumentTranslation` for `(id, lang)`.
   - Hit: return `{content, disclaimer}` from cache.
   - Miss: call `IDocumentTranslationService.TranslateAsync`, persist the
     result, return it.
3. On failure (AI error, empty/suspicious result), return an error JSON
   shape and do not cache.

**View**

`Preview.cshtml` gets a বাংলা/English toggle (pill switch, matches the
approved mockup). Switching to the document's original language is
instant (already in the DOM). Switching to the other language calls the
endpoint, shows a loading state, swaps the content in, and displays a
fixed disclaimer badge: *"AI-translated for convenience — the
[original-language] version is the authoritative document."* The response
is cached client-side for the rest of the page session so re-toggling
doesn't refetch.

## Data flow

1. Citizen views a document → `Preview` loads it in `Case.Language`
   (unchanged).
2. Citizen clicks the other-language pill → `fetch(POST /Document/{id}/Translate?lang=…)`.
3. Controller resolves access, checks cache, calls translation service on
   miss, persists, returns.
4. JS swaps content, shows disclaimer badge, caches response in-page.
5. Switching back to the original language restores the untouched
   original DOM — no network call.

## Error handling

- AI call fails/times out (reuses `GeminiClient`'s existing resilience —
  key rotation, retries) → endpoint returns an error JSON; JS shows an
  inline bilingual message ("Translation unavailable right now — showing
  the original.") and leaves the original content visible.
- Translation result is empty or implausibly short relative to source
  (cheap length-ratio heuristic) → treated as a failure, not cached,
  same error UI.
- Document not found / not owned by requester → 404/403, identical to
  `Preview`'s existing behavior.
- Double-click on the toggle → button disables while a request is in
  flight (UI concern, not backend).

## Testing

- `DocumentTranslationServiceTests` — mocks `IAiService`, verifies prompt
  assembly and pass-through of the translated text.
- `DocumentGeneratorTests` / `LabourComplaintTemplateTests` — updated for
  the new section-list return shape, Bangla headers, and sanitizer
  application (sanitizer behavior is verified here, not in a standalone
  sanitizer test class).
- `DocumentControllerTests` (`Translate` action) — cache-hit path (no AI
  call made), cache-miss path (AI called once, row persisted), error path
  (AI throws → graceful JSON error, nothing cached), access-control path
  (mirrors existing `Preview` tests).
- No new UI/E2E test infrastructure — this repo's suite is xUnit-based
  (`GeminiClientTests.cs`, `AiOrchestrationServiceTests.cs`); this work
  follows that pattern only.

## Open questions for implementation planning

None — approved as above. `docs/admin-tier-access-requirements.md` and
`plans/Dependency_plan.md` should be checked for any task-numbering
convention to fold this work under, per this repo's mandatory progress
tracking rule.
