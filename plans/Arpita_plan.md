# Arpita — Document Generation + Lawyer Review + Admin Plan

> ponytail: full — every step earns its place. No boilerplate tasks. Options are real choices.

**Role:** Arpita owns the output pipeline — everything from "the AI generated a draft" to "a lawyer approved it" to "the citizen downloads a PDF." She also owns the case lifecycle, admin analytics, and all DTOs.

---

## Checkpoint 1: DTOs

### Step 1.1: All DTOs in MuktoAin.Application/DTOs

> **Depends on**: Tultul completing all 14 entities and 9 enums (Step 1.2 + 1.3 of Tultul_plan.md). You need entity field names and enum types to define matching DTO shapes.

DTOs are the data shapes that flow between layers. Controllers receive and return DTOs — never raw entities.

Create in `MuktoAin.Application/DTOs/`. Use C# `record` types for immutability:

```csharp
// CaseSubmissionDto.cs — citizen intake form data
public record CaseSubmissionDto(
    int CategoryId,
    byte DistrictId,
    string Title,
    string Description,
    string Language,       // "bn", "en", "banglish"
    bool IsAnonymous
);

// RightsExplanationDto.cs — AI output for "Explain My Rights"
public record RightsExplanationDto(
    string Explanation,                    // plain-language rights text
    IReadOnlyList<CitedSectionDto> CitedSections,  // grounded citations
    string Disclaimer
);

public record CitedSectionDto(
    int SectionId,
    string ActTitle,
    string SectionNumber,
    string SectionText,
    float RelevanceScore,
    string RetrievalMethod    // "Vector" or "Keyword"
);

// DraftDocumentDto.cs — generated document preview
public record DraftDocumentDto(
    int DocumentId,
    int CaseId,
    string DocumentType,
    string ContentDraft,
    string Status,
    DateTime CreatedAt
);

// SearchResultDto.cs — Acts search results
public record SearchResultDto(
    string Query,
    int TotalResults,
    int Page,
    IReadOnlyList<CitedSectionDto> Results
);

// ReviewDto.cs — lawyer review submission
public record ReviewDto(
    int DocumentId,
    string Decision,          // "Approved", "EditedApproved", "Rejected"
    string? EditedContent,    // only if Decision = EditedApproved
    string Comments
);

// AnalyticsDto.cs — admin dashboard aggregates
public record AnalyticsSummaryDto(
    int TotalCases,
    int PendingReviews,
    int ApprovedDocuments,
    IReadOnlyList<CategoryCountDto> CasesByCategory,
    IReadOnlyList<DistrictCountDto> CasesByDistrict
);

public record CategoryCountDto(string CategoryName, int Count);
public record DistrictCountDto(string DistrictName, int Count);

// LawyerApplicationDto.cs — lawyer verification request
public record LawyerApplicationDto(
    string BarRegistrationNumber,
    string? Specialization
);

// CaseDetailDto.cs — full case view for tracking
public record CaseDetailDto(
    int CaseId,
    string Title,
    string Description,
    string CategoryName,
    string DistrictName,
    string Status,
    bool IsAnonymous,
    DateTime CreatedAt,
    IReadOnlyList<DraftDocumentDto>? Documents
);
```

- ponytail: Use `record` not `class`. Records give you value equality, immutability, and `with` expressions for free. Don't add validation attributes here — validation belongs in services or FluentValidation. Don't create a DTO for every entity — only for data that crosses a layer boundary. Ceiling: flat records; upgrade path: add FluentValidation validators per DTO if input validation gets complex.

**Decision point — mapping strategy:**
- **Option A**: Manual mapping in services (`new DraftDocumentDto(entity.DocumentId, ...)`). Zero dependencies, explicit, slightly verbose.
- **Option B**: AutoMapper. Convention-based, less code, adds a NuGet dependency and magic.
- **Option C**: Mapster. Lighter than AutoMapper, source-generated.
- **Recommendation**: Option A. With ~8 DTOs and simple flat mappings, manual mapping is readable and debuggable. AutoMapper adds indirection that's not worth it for this scale. Ceiling: manual mapping; upgrade path: Mapster if DTO count exceeds 20.

---

## Checkpoint 2: Document Generation + Lawyer Review + Case Lifecycle

### Step 2.1: CaseService.cs — Case Lifecycle

> **Depends on**: Tultul completing `ICaseRepository`, `AppDbContext`, repository implementations, and seed data (Steps 1.4, 1.6, 1.7, 1.12 of Tultul_plan.md).

The case is the central entity everything hangs off. CaseService manages creation, status transitions, and querying.

1. Create `Application/Services/CaseService.cs`.

2. Core methods:
   ```csharp
   public class CaseService
   {
       private readonly ICaseRepository _caseRepo;
       private readonly IRepository<CaseCategory> _categoryRepo;
   
       // Create a new case from citizen submission.
       // Returns the CaseId AND, for anonymous submissions, an AnonymousTrackingCode
       // (GUID shown once to the submitter — their only way to use FR-8 tracking).
       // NOTE (eng review): encrypt Title/Description here — see Shads_plan.md Step 2.8.
       // Case.Description = _encryptionService.Encrypt(dto.Description), same for Title,
       // and decrypt in the read paths below. Coordinate once; don't duplicate.
       public async Task<int> SubmitCaseAsync(CaseSubmissionDto dto, int? userId)
       {
           var caseEntity = new Case
           {
               UserId = dto.IsAnonymous ? null : userId,
               CategoryId = dto.CategoryId,
               DistrictId = dto.DistrictId,
               Title = dto.Title,
               Description = dto.Description,
               Language = dto.Language,
               Status = CaseStatus.Submitted,
               IsAnonymous = dto.IsAnonymous,
               CreatedAt = DateTime.UtcNow,
               UpdatedAt = DateTime.UtcNow
           };
           await _caseRepo.AddAsync(caseEntity);
           await _caseRepo.SaveChangesAsync();
           return caseEntity.CaseId;
       }
   
        // Get case with documents for tracking dashboard
        public async Task<CaseDetailDto?> GetCaseDetailAsync(int caseId, int? userId, UserRole callerRole, string? trackingCode = null)
        {
            var c = await _caseRepo.GetWithDocumentsAsync(caseId);
            if (c == null) return null;
            // Access rules (eng review round 2):
            // - Lawyers/Admins: full access.
            // - Authenticated citizens: only their OWN non-anonymous cases
            //   (c.UserId != userId is NOT enough — when both sides are null,
            //   null != null evaluates FALSE in C# and would grant guests access
            //   to every guest case. Compare explicitly against null first).
            // - Guests (userId == null): may read an anonymous case ONLY with its
            //   AnonymousTrackingCode (issued at submit, stored on CASE).
            switch (callerRole)
            {
                case UserRole.Admin:
                case UserRole.Lawyer:
                    break;
                case UserRole.Citizen:
                    if (userId == null)
                    {
                        if (!c.IsAnonymous || c.AnonymousTrackingCode != trackingCode) return null;
                    }
                    else if (c.IsAnonymous || c.UserId != userId) return null;
                    break;
            }
            return MapToCaseDetailDto(c);
       }
   
       // Get all cases for a user (tracking dashboard)
       public async Task<IEnumerable<CaseDetailDto>> GetUserCasesAsync(int userId)
       {
           var cases = await _caseRepo.GetByUserIdAsync(userId);
           return cases.Select(MapToCaseDetailDto);
       }
   }
   ```

3. **Status state machine** — enforce valid transitions:
   ```
   Submitted → UnderReview   (triggered when a lawyer picks up a document for review)
   UnderReview → Finalized   (triggered when lawyer approves)
   UnderReview → Submitted   (triggered when lawyer rejects — goes back for re-drafting)
   ```
   Implement as a simple method:
   ```csharp
   public async Task<bool> TransitionStatusAsync(int caseId, CaseStatus newStatus)
   {
       var c = await _caseRepo.GetByIdAsync(caseId);
       if (c == null) return false;

        bool valid = (c.Status, newStatus) switch
        {
            (CaseStatus.Submitted, CaseStatus.UnderReview) => true,
            (CaseStatus.UnderReview, CaseStatus.Finalized) => true,
            (CaseStatus.UnderReview, CaseStatus.Submitted) => true,  // rejection
            (CaseStatus.Finalized, CaseStatus.Submitted) => true,    // rejection of a replacement draft re-opens a finalized case (multi-doc reconciliation)
            _ => false
        };

       if (!valid) return false;
       c.Status = newStatus;
       c.UpdatedAt = DateTime.UtcNow;
       await _caseRepo.SaveChangesAsync();
       return true;
   }
   ```
   - ponytail: A tuple switch is a state machine. Don't build a `StateMachine<T>` class or pull in a library like Stateless. Three transitions, three lines. Ceiling: switch expression; upgrade path: if transitions need side effects (email notifications, audit logs), extract to a `CaseWorkflow` class.

### Step 2.2: DocumentGenerator.cs — Template Selection Engine

> **Depends on**: Tultul completing entities + Shads completing `AiOrchestrationService` (Step 2.4 of Shads_plan.md). The generator needs the AI-produced rights explanation and cited sections to assemble the document.

This is the core of Arpita's assignment. The DocumentGenerator takes a case + AI context and produces a structured legal document using the appropriate template.

1. Create `Application/Documents/DocumentGenerator.cs`.

   > **Layer note (eng review):** the generator does pure string assembly — no I/O — so it
   > belongs in **Application**, not Infrastructure. Infrastructure placement would create a
   > reverse dependency (Infrastructure consuming Application DTOs). Only `PdfExportService`
   > (which performs file/IO work via QuestPDF) stays in Infrastructure.

2. Design:
   ```csharp
   public class DocumentGenerator
   {
       private readonly Dictionary<DocumentType, IDocumentTemplate> _templates;
   
       public DocumentGenerator(IEnumerable<IDocumentTemplate> templates)
       {
           _templates = templates.ToDictionary(t => t.DocumentType);
       }
   
       public async Task<string> GenerateAsync(Case caseEntity, RightsExplanationDto explanation)
       {
           // Map CaseCategory to DocumentType
           var docType = MapCategoryToDocumentType(caseEntity.CategoryId);
   
           if (!_templates.TryGetValue(docType, out var template))
               throw new InvalidOperationException($"No template found for document type {docType}");
   
           return await template.RenderAsync(caseEntity, explanation);
       }
   
       private DocumentType MapCategoryToDocumentType(int categoryId) => categoryId switch
       {
           1 => DocumentType.LabourComplaint,
           2 => DocumentType.GeneralDiary,
           3 => DocumentType.RtiRequest,
           4 => DocumentType.ConsumerComplaint,
           _ => throw new ArgumentException($"Unknown category: {categoryId}")
       };
   }
   ```

3. Define the template interface in `MuktoAin.Domain/Interfaces/Services/`:
   ```csharp
   public interface IDocumentTemplate
   {
       DocumentType DocumentType { get; }
       Task<string> RenderAsync(Case caseEntity, RightsExplanationDto explanation);
   }
   ```
   Template implementations live beside the generator in `Application/Documents/Templates/`.

   - ponytail: The `Dictionary<DocumentType, IDocumentTemplate>` with DI auto-registration is the entire "strategy pattern." Don't call it that. It's a dictionary lookup. Ceiling: in-memory template dictionary; upgrade path: load templates from database or files if non-developers need to edit them.

### Step 2.3: LabourComplaintTemplate.cs — The First Template

> **Depends on**: Step 2.2 (DocumentGenerator + IDocumentTemplate interface).

This is the vertical slice template — it must work end-to-end for the Checkpoint 2 demo.

1. Create `Application/Documents/Templates/LabourComplaintTemplate.cs`.

2. A Labour complaint has a specific structure. Research the format used in Bangladesh District Labour Courts:
   ```
   TO
   The Inspector General / District Labour Court
   [District], Bangladesh

   Subject: Complaint Under Section [X] of the Bangladesh Labour Act, 2006

   Respected Sir/Madam,

   I, [Complainant Name], resident of [Address], [District], do hereby submit
   this complaint against [Employer/Company Name] for the following violation(s)
   of the Bangladesh Labour Act, 2006:

   FACTS OF THE CASE:
   [Case description from citizen input]

   APPLICABLE LEGAL PROVISIONS:
   [Cited Act sections from RAG retrieval, with section numbers]

   RELIEF SOUGHT:
   [Generated based on cited sections — unpaid wages, reinstatement, compensation, etc.]

   DECLARATION:
   I hereby declare that the information provided above is true and correct to
   the best of my knowledge.

   Date: [Generated Date]
   Complainant: [Name]

   [DISCLAIMER STAMP — Surface 3 of 3]
   ```

3. Implementation:
   ```csharp
   public class LabourComplaintTemplate : IDocumentTemplate
   {
       public DocumentType DocumentType => DocumentType.LabourComplaint;
   
       public Task<string> RenderAsync(Case caseEntity, RightsExplanationDto explanation)
       {
           var sb = new StringBuilder();
   
           sb.AppendLine("TO");
           sb.AppendLine($"The Inspector General / District Labour Court");
           sb.AppendLine($"{caseEntity.District.Name}, Bangladesh");
           sb.AppendLine();
           // ... build the full document structure
   
           // Inject cited sections
           sb.AppendLine("APPLICABLE LEGAL PROVISIONS:");
           foreach (var section in explanation.CitedSections)
           {
               sb.AppendLine($"• {section.ActTitle}, Section {section.SectionNumber}:");
               sb.AppendLine($"  {section.SectionText}");
               sb.AppendLine();
           }
   
           // Disclaimer stamp (Surface 3 of 3)
           sb.AppendLine(Disclaimers.Legal);
   
           return Task.FromResult(sb.ToString());
       }
   }
   ```

4. **Decision point — template rendering approach:**
   - **Option A**: `StringBuilder` (shown above). Simple, full control, no dependencies. Good for 4 templates.
   - **Option B**: Razor template engine (`RazorLight`). Templates as `.cshtml` files, familiar syntax, adds a NuGet package.
   - **Option C**: String interpolation with raw string literals (`"""`). Even simpler than StringBuilder for short templates.
   - **Recommendation**: Option A. The templates have conditional logic (different relief sections based on cited Acts) that makes raw string interpolation messy. StringBuilder handles conditionals naturally. RazorLight is overkill for 4 templates.
   - ponytail: StringBuilder. The template is a string with holes in it. Don't pull in a template engine for 4 templates. Ceiling: StringBuilder; upgrade path: RazorLight or Scriban if templates exceed 10 or non-developers need to edit them.

### Step 2.4: DocumentService.cs — Document Lifecycle

> **Depends on**: Steps 2.2 + 2.3 (DocumentGenerator must exist) + Tultul's repositories.

Manages the document from creation through to finalization.

1. Create `Application/Services/DocumentService.cs`.

2. Core methods:
   ```csharp
   public class DocumentService
   {
       private readonly DocumentGenerator _generator;
       private readonly IRepository<GeneratedDocument> _docRepo;
       private readonly IRepository<Case> _caseRepo;
   
       // Generate a new document for a case
       public async Task<DraftDocumentDto> GenerateDocumentAsync(int caseId, RightsExplanationDto explanation)
       {
           var caseEntity = await _caseRepo.GetByIdAsync(caseId);
           if (caseEntity == null) throw new ArgumentException("Case not found");
   
           var content = await _generator.GenerateAsync(caseEntity, explanation);
   
           var doc = new GeneratedDocument
           {
               CaseId = caseId,
               DocumentType = _generator.GetDocumentType(caseEntity.CategoryId),
               ContentDraft = content,      // immutable AI original
               ContentFinal = null,          // filled after lawyer review
               Status = DocumentStatus.Draft,
               CreatedAt = DateTime.UtcNow
           };
   
           await _docRepo.AddAsync(doc);
           await _docRepo.SaveChangesAsync();
   
           return MapToDto(doc);
       }
   
       // Get document for preview (citizens see ContentDraft, lawyers see both)
       public async Task<DraftDocumentDto?> GetDocumentAsync(int documentId)
       {
           var doc = await _docRepo.GetByIdAsync(documentId);
           return doc == null ? null : MapToDto(doc);
       }
   
       // Update document status (called by LawyerReviewService)
       public async Task UpdateStatusAsync(int documentId, DocumentStatus newStatus, string? editedContent = null)
       {
           var doc = await _docRepo.GetByIdAsync(documentId);
           if (doc == null) throw new ArgumentException("Document not found");
   
           doc.Status = newStatus;
           if (editedContent != null)
               doc.ContentFinal = editedContent;
           else if (newStatus == DocumentStatus.Approved)
               doc.ContentFinal = doc.ContentDraft;  // approved without edits
   
           await _docRepo.SaveChangesAsync();
       }
   }
   ```

3. **Key rule**: `ContentDraft` is NEVER modified after initial generation. It's the immutable AI original. `ContentFinal` is what the lawyer produces. This preserves the audit trail.

### Step 2.5: PdfExportService.cs — QuestPDF Integration

> **Depends on**: Step 2.4 (DocumentService — needs to retrieve the finalized content to render).

1. Install QuestPDF:
   ```
   dotnet add src/MuktoAin.Infrastructure package QuestPDF
   ```

2. Create `Infrastructure/Documents/PdfExportService.cs`.

3. Implementation:
   ```csharp
   public class PdfExportService
   {
       public byte[] GeneratePdf(GeneratedDocument document, Case caseEntity)
       {
           // Use ContentFinal if available (lawyer-reviewed), otherwise ContentDraft
           var content = document.ContentFinal ?? document.ContentDraft;
   
           var pdfDoc = Document.Create(container =>
           {
               container.Page(page =>
               {
                   page.Size(PageSizes.A4);
                   page.Margin(2, Unit.Centimetre);
                   page.DefaultTextStyle(x => x.FontSize(11));
   
                   page.Header().Text("MuktoAin — মুক্ত আইন")
                       .SemiBold().FontSize(16).FontColor(Colors.Blue.Darken2);
   
                   page.Content().PaddingVertical(10).Column(col =>
                   {
                       col.Item().Text(content).LineHeight(1.5f);
   
                       // Disclaimer stamp — Surface 3 of 3 (permanent, in the PDF itself)
                       col.Item().PaddingTop(20)
                           .Border(1).BorderColor(Colors.Red.Darken1)
                           .Padding(10)
                           .Text(Disclaimers.Legal)
                           .FontSize(9).FontColor(Colors.Red.Darken1);
                   });
   
                   page.Footer().AlignCenter()
                       .Text(x =>
                       {
                           x.Span("Generated by MuktoAin — ");
                           x.Span(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm UTC"));
                       });
               });
           });
   
           return pdfDoc.GeneratePdf();
       }
   }
   ```

4. **Bangla font rendering** — this is the critical risk. QuestPDF uses Lato by default, which doesn't render Bangla characters. You must register a Bangla font:
   ```csharp
   // In Program.cs or a startup helper
   QuestPDF.Settings.License = LicenseType.Community;  // MIT, free for < $1M revenue

   // Register Bangla font
   FontManager.RegisterFont(File.OpenRead("wwwroot/fonts/NotoSansBengali-Regular.ttf"));
   ```
   Then use it in templates:
   ```csharp
   page.DefaultTextStyle(x => x.FontFamily("Noto Sans Bengali").FontSize(11));
   ```
   - **Option A**: Noto Sans Bengali (Google Fonts, open source, excellent coverage).
   - **Option B**: SolaimanLipi (popular in Bangladesh, GPL licensed — check compatibility).
   - **Option C**: Kalpurush (widely used for Bangla printing).
   - **Recommendation**: Option A (Noto Sans Bengali) — broadest Unicode coverage, guaranteed to render all Bangla characters, clearly MIT-compatible with QuestPDF.

5. **Test rendering early.** Generate a test PDF with Bangla text in week 1. If fonts don't render, you want to know immediately, not in the CP2 demo.
   - ponytail: The first thing you do after installing QuestPDF is generate a "hello world" PDF with Bangla text. If that works, everything else is layout tweaking. If it doesn't, you have a font problem to solve before writing any template logic. Ceiling: single font, A4 layout; upgrade path: multiple font weights, letterhead template, QR code linking back to the case.

### Step 2.6: LawyerVerificationService.cs (FR-15)

> **Depends on**: Tultul completing `LawyerProfile` entity, `User` entity, and repositories.

Lawyers must be verified by an admin before they can review documents. This service manages the application and approval workflow.

1. Create `Application/Services/LawyerVerificationService.cs`.

2. Core methods:
   ```csharp
   public class LawyerVerificationService
   {
       private readonly IRepository<LawyerProfile> _profileRepo;
   
       // Lawyer submits verification application
       public async Task<int> ApplyAsync(int userId, LawyerApplicationDto dto)
       {
           // Check if this user already has a profile
           var existing = await _profileRepo.GetAllAsync();
           if (existing.Any(p => p.UserId == userId))
               throw new InvalidOperationException("Verification already submitted");
   
           var profile = new LawyerProfile
           {
               UserId = userId,
               BarRegistrationNumber = dto.BarRegistrationNumber,
               Specialization = dto.Specialization,
               VerificationStatus = VerificationStatus.Pending
           };
   
           await _profileRepo.AddAsync(profile);
           await _profileRepo.SaveChangesAsync();
           return profile.LawyerProfileId;
       }
   
       // Admin approves or rejects
       public async Task VerifyAsync(int lawyerProfileId, int adminUserId, bool approve)
       {
           var profile = await _profileRepo.GetByIdAsync(lawyerProfileId);
           if (profile == null) throw new ArgumentException("Profile not found");
   
           profile.VerificationStatus = approve
               ? VerificationStatus.Approved
               : VerificationStatus.Rejected;
           profile.VerifiedByAdminId = adminUserId;
           profile.VerifiedAt = DateTime.UtcNow;
   
           await _profileRepo.SaveChangesAsync();
       }
   
       // Get pending applications for admin review
       public async Task<IEnumerable<LawyerProfile>> GetPendingApplicationsAsync()
       {
           var all = await _profileRepo.GetAllAsync();
           return all.Where(p => p.VerificationStatus == VerificationStatus.Pending);
       }
   }
   ```

   - ponytail: The "check for existing profile" query using `GetAllAsync().Any()` is fine for a small user base. For scale, you'd add `GetByUserIdAsync` to the repo. Ceiling: in-memory filter; upgrade path: specific repository method.

### Step 2.7: LawyerReviewService.cs (FR-13, FR-14)

> **Depends on**: Steps 2.4 + 2.6 (DocumentService + LawyerVerificationService — a lawyer must be verified AND a document must exist).

This is the review gate — the feature that makes MuktoAin different from AdalotBD.

1. Create `Application/Services/LawyerReviewService.cs`.

2. Core methods:
   ```csharp
   public class LawyerReviewService
   {
       private readonly IRepository<LawyerReview> _reviewRepo;
       private readonly IRepository<GeneratedDocument> _docRepo;
       private readonly IRepository<LawyerProfile> _profileRepo;
       private readonly DocumentService _docService;
       private readonly CaseService _caseService;
   
        // Get documents awaiting review (the review queue — FR-13).
        // Shows UNCLAIMED drafts for everyone + documents claimed by the CALLING lawyer.
        // Lawyers must not see (or submit reviews on) each other's claimed work —
        // ownership is enforced here AND again in SubmitReviewAsync.
        public async Task<IEnumerable<DraftDocumentDto>> GetReviewQueueAsync(int callerLawyerProfileId)
        {
            var docs = await _docRepo.GetByStatusesAsync(DocumentStatus.Draft, DocumentStatus.UnderReview);
            return docs
                .Where(d => d.AssignedLawyerProfileId == null
                            || d.AssignedLawyerProfileId.Value == callerLawyerProfileId)
                .Select(MapToDto);
        }
    
        // Lawyer picks up a document for review
        public async Task ClaimForReviewAsync(int documentId, int lawyerProfileId)
        {
            // Verify the lawyer is approved
            var profile = await _profileRepo.GetByIdAsync(lawyerProfileId);
            if (profile?.VerificationStatus != VerificationStatus.Approved)
                throw new UnauthorizedAccessException("Lawyer not verified");
    
            var doc = await _docRepo.GetByIdAsync(documentId);
            if (doc == null) throw new ArgumentException("Document not found");
    
            // Assignment guard (eng review A5): persist WHO claimed the doc so two verified
            // lawyers cannot silently claim the same document. Requires a nullable
            // AssignedLawyerProfileId column on GENERATED_DOCUMENT (schema addition to
            // scripts/02_schema.sql — a field, not a new entity) and an optimistic check:
            if (doc.AssignedLawyerProfileId.HasValue && doc.AssignedLawyerProfileId.Value != lawyerProfileId)
                throw new InvalidOperationException("Document already claimed by another lawyer");
            doc.AssignedLawyerProfileId = lawyerProfileId;
    
            doc.Status = DocumentStatus.UnderReview;
            await _docRepo.SaveChangesAsync();
    
            // Transition the parent case status
            await _caseService.TransitionStatusAsync(doc.CaseId, CaseStatus.UnderReview);
        }
   
        // Lawyer submits their review (FR-14)
        public async Task SubmitReviewAsync(int lawyerProfileId, ReviewDto dto)
        {
            var doc = await _docRepo.GetByIdAsync(dto.DocumentId);
            if (doc == null) throw new ArgumentException("Document not found");
            if (doc.Status != DocumentStatus.UnderReview)
                throw new InvalidOperationException("Document not under review");

            // Ownership guard (eng review round 2): only the lawyer who CLAIMED the
            // document may submit a review on it. Status alone is not authorization.
            if (doc.AssignedLawyerProfileId != lawyerProfileId)
                throw new UnauthorizedAccessException("Document claimed by another lawyer");
   
            // Parse decision — TryParse, never Enum.Parse (bad input = 400, not 500)
            if (!Enum.TryParse<ReviewDecision>(dto.Decision, ignoreCase: true, out var decision))
                throw new ArgumentException($"Unknown decision: {dto.Decision}");
   
           // Create the review record
           var review = new LawyerReview
           {
               DocumentId = dto.DocumentId,
               LawyerProfileId = lawyerProfileId,
               Decision = decision,
               Comments = dto.Comments,
               ReviewedAt = DateTime.UtcNow
           };
           await _reviewRepo.AddAsync(review);
   
            // Update document based on decision
            switch (decision)
            {
                case ReviewDecision.Approved:
                    // ContentFinal = ContentDraft (no edits)
                    await _docService.UpdateStatusAsync(dto.DocumentId, DocumentStatus.Approved);
                    // Multi-doc rule (eng review round 2): a case can hold several drafts
                    // (rejection spawns replacements). Finalize ONLY when no sibling doc
                    // is still Draft/UnderReview — otherwise the case stays UnderReview.
                    if (!await _docRepo.HasDocsInStatusesAsync(doc.CaseId,
                            DocumentStatus.Draft, DocumentStatus.UnderReview))
                    {
                        await _caseService.TransitionStatusAsync(doc.CaseId, CaseStatus.Finalized);
                    }
                    break;
    
                case ReviewDecision.EditedApproved:
                    // ContentFinal = lawyer's edited version, ContentDraft preserved
                    if (string.IsNullOrWhiteSpace(dto.EditedContent))
                        throw new ArgumentException("Edited content required for EditedApproved");
                    await _docService.UpdateStatusAsync(dto.DocumentId, DocumentStatus.Approved, dto.EditedContent);
                    if (!await _docRepo.HasDocsInStatusesAsync(doc.CaseId,
                            DocumentStatus.Draft, DocumentStatus.UnderReview))
                    {
                        await _caseService.TransitionStatusAsync(doc.CaseId, CaseStatus.Finalized);
                    }
                    break;
    
                case ReviewDecision.Rejected:
                    await _docService.UpdateStatusAsync(dto.DocumentId, DocumentStatus.Rejected);
                    // Re-open the case so the citizen can regenerate a replacement draft.
                    await _caseService.TransitionStatusAsync(doc.CaseId, CaseStatus.Submitted);
                    break;
            }
   
           await _reviewRepo.SaveChangesAsync();
       }
   }
   ```

3. **Redline tracking**: The `ContentDraft` vs `ContentFinal` split IS the redline. When a lawyer chooses `EditedApproved`:
   - `ContentDraft` stays untouched (immutable AI original)
   - `ContentFinal` contains the lawyer's edits
   - A future diff view can compare the two (string diff, not your problem for CP2 — just preserve both)
   - ponytail: The redline is two columns in a database. Don't build a diff engine, a revision history table, or operational transforms. Two strings. Compare them in the UI if you want. Ceiling: two-column before/after; upgrade path: diff library like `DiffPlex` for visual redline display.

4. **PDF download gate**: In `DocumentService` or a separate check, enforce:
   ```csharp
    public async Task<byte[]?> GetPdfIfApprovedAsync(int documentId)
    {
        // Include the Case nav — GetByIdAsync alone leaves doc.Case null (NRE in GeneratePdf)
        var doc = await _docRepo.GetWithCaseAsync(documentId);
        if (doc?.Status != DocumentStatus.Approved) return null;
        return _pdfService.GeneratePdf(doc, doc.Case);
    }
   ```
   Citizens cannot download PDFs for documents that haven't been approved. This is non-negotiable.

---

## Checkpoint 3: Additional Templates + Admin + Tests

### Step 3.1: GeneralDiaryTemplate.cs

> **Depends on**: Step 2.2 (IDocumentTemplate interface + DocumentGenerator dictionary registration).

A General Diary (GD) application is filed at a police station. The format follows Bangladesh Police procedures.

1. Create `Application/Documents/Templates/GeneralDiaryTemplate.cs` implementing `IDocumentTemplate`.

2. Structure:
   ```
   TO
   The Officer-in-Charge
   [Police Station Name], [District]

   Subject: General Diary Entry Application

   Sir,

   I, [Name], son/daughter of [Parent], residing at [Address], [District],
   most respectfully submit this General Diary entry for the following matter:

   STATEMENT OF FACTS:
   [Case description]

   APPLICABLE LEGAL PROVISIONS:
   [Cited sections — e.g., Code of Criminal Procedure, Section 154]

   PRAYER:
   I most humbly pray that the above facts be recorded in the General Diary
   register of your police station and necessary action be taken.

   Date: [Date]
   Applicant: [Name]

   [DISCLAIMER]
   ```

3. Register in DI so `DocumentGenerator` discovers it automatically:
   ```csharp
   builder.Services.AddScoped<IDocumentTemplate, GeneralDiaryTemplate>();
   ```

### Step 3.2: RtiRequestTemplate.cs

> **Depends on**: Same as Step 3.1.

Right to Information application under the Right to Information Act, 2009.

Structure:
```
TO
The Designated Officer
[Government Body Name]
[Address]

Subject: Application Under Section 8 of the Right to Information Act, 2009

Sir/Madam,

Under the provisions of the Right to Information Act, 2009, I request
the following information:

INFORMATION REQUESTED:
[Generated from case description]

JUSTIFICATION:
[Cited sections from RTI Act]

PREFERRED FORMAT: [Printed copy / Electronic copy]

Applicant Details:
Name: [Name]
Address: [Address]
Date: [Date]

[DISCLAIMER]
```

### Step 3.3: ConsumerComplaintTemplate.cs

> **Depends on**: Same as Step 3.1.

Filed under the Consumer Rights Protection Act, 2009.

Structure:
```
TO
The Director General
National Consumer Rights Protection Directorate
[Or: District Consumer Rights Protection Committee, District]

Subject: Complaint Under the Consumer Rights Protection Act, 2009

Complainant: [Name, Address, Contact]
Respondent: [Business/Seller Name, Address]

FACTS OF THE COMPLAINT:
[Case description]

APPLICABLE PROVISIONS:
[Cited sections from Consumer Rights Protection Act]

COMPENSATION/RELIEF SOUGHT:
[Generated based on violation type]

SUPPORTING EVIDENCE:
[Placeholder — receipt, warranty, photographs]

Date: [Date]
Complainant Signature: [Name]

[DISCLAIMER]
```

- ponytail: All 3 additional templates follow the exact same pattern as LabourComplaintTemplate — implement `IDocumentTemplate`, use StringBuilder, inject cited sections and disclaimer. Copy-paste the structure, change the content. Don't abstract a "BaseTemplate" class for 4 templates. Ceiling: 4 independent template files; upgrade path: shared base if templates exceed 8.

### Step 3.4: AdminAnalyticsService.cs (FR-16)

> **Depends on**: Tultul completing repositories + seed data (Cases must exist for aggregation to be meaningful).

1. Create `Application/Services/AdminAnalyticsService.cs`.

2. Implementation:
   ```csharp
   public class AdminAnalyticsService
   {
       private readonly AppDbContext _context;
    
        public async Task<AnalyticsSummaryDto> GetSummaryAsync()
        {
            // Explicit manual MSSQL aggregate queries.
            // NOTE: table names follow the repo naming rule (Tultul_plan.md Step 1.6):
            // design.md UPPERCASE names, bracketed because USER/CASE are reserved words.
            var totalCases = await _context.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM [dbo].[CASE]")
                .SingleAsync();

            var pendingReviews = await _context.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM [dbo].[GENERATED_DOCUMENT] WHERE Status = 1") // 1 = UnderReview
                .SingleAsync();

            var approvedDocs = await _context.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM [dbo].[GENERATED_DOCUMENT] WHERE Status = 2") // 2 = Approved
                .SingleAsync();
     
            var byCategory = await _context.Database
                .SqlQueryRaw<CategoryCountDto>(@"
                    SELECT cat.Name AS CategoryName, COUNT(c.CaseId) AS [Count]
                    FROM [dbo].[CASE_CATEGORY] cat
                    LEFT JOIN [dbo].[CASE] c ON cat.CategoryId = c.CategoryId
                    GROUP BY cat.Name")
                .ToListAsync();
     
            var byDistrict = await _context.Database
                .SqlQueryRaw<DistrictCountDto>(@"
                    SELECT d.NameEn AS DistrictName, COUNT(c.CaseId) AS [Count]
                    FROM [dbo].[DISTRICT] d
                    LEFT JOIN [dbo].[CASE] c ON d.DistrictId = c.DistrictId
                    GROUP BY d.NameEn")
                .ToListAsync();
    
           return new AnalyticsSummaryDto(totalCases, pendingReviews, approvedDocs, byCategory, byDistrict);
       }
   }
   ```

   - **Manual SQL Queries**: Using explicit T-SQL aggregation queries (`COUNT`, `LEFT JOIN`, `GROUP BY`) provides full transparency and alignment with SSMS query management.
   - ponytail: Direct parameterized SQL queries. Don't build a reporting framework or CQRS read model. Ceiling: direct SQL aggregation; upgrade path: indexed views in SSMS if dataset grows large.

3. **Anonymization**: The spec says "anonymized." This means:
   - Never return user names, emails, or case descriptions in analytics.
   - Only return aggregate counts by category and district.
   - The DTO already enforces this — `AnalyticsSummaryDto` contains only counts, not case details.

### Step 3.5: ModerationService.cs

> **Depends on**: Tultul completing repositories.

1. Create `Application/Services/ModerationService.cs`.
2. Content moderation for case submissions — check for obviously inappropriate content before processing.

```csharp
public class ModerationService
{
    // Basic keyword filter — not AI-based, just a blocklist
    private static readonly HashSet<string> BlockedTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        // Add terms that indicate the submission isn't a legal query
    };

    public bool IsContentAppropriate(string content)
    {
        return !BlockedTerms.Any(term => content.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
```

- ponytail: This is a blocklist check. Don't build an AI moderation pipeline or integrate a content safety API for a student project. Ceiling: keyword blocklist; upgrade path: Gemini safety filters or Azure Content Safety API.

### Step 3.6: Unit Tests for Services

> **Depends on**: Steps 2.1-2.7 (services must exist to test).

1. Create tests in `tests/MuktoAin.UnitTests/Services/`.
2. Use Moq for mocking repository interfaces.
3. Test at minimum:
   - **CaseService**: `SubmitCaseAsync` creates a case with correct status. `TransitionStatusAsync` rejects invalid transitions (e.g., `Submitted → Finalized` should fail).
   - **DocumentService**: `GenerateDocumentAsync` sets `ContentDraft`, `Status = Draft`, `ContentFinal = null`.
   - **LawyerReviewService**: `SubmitReviewAsync` with `Approved` sets `ContentFinal = ContentDraft`. With `EditedApproved` sets `ContentFinal = edited text`. With `Rejected` sets status back to `Rejected`.
   - **LawyerVerificationService**: `ApplyAsync` rejects duplicate applications. `VerifyAsync` sets `VerifiedAt` timestamp.
   - **Claim race** (eng review): second lawyer claiming an already-claimed document throws `InvalidOperationException`.
   - **Unverified lawyer** calling `ClaimForReviewAsync` gets `UnauthorizedAccessException`.
   - **Anonymous-case authorization**: citizen requesting another user's anonymous case gets `null`; lawyer/admin can retrieve it.

```csharp
[Fact]
public async Task TransitionStatus_Submitted_To_Finalized_Should_Fail()
{
    // Arrange
    var mockRepo = new Mock<ICaseRepository>();
    mockRepo.Setup(r => r.GetByIdAsync(1))
        .ReturnsAsync(new Case { CaseId = 1, Status = CaseStatus.Submitted });
    var service = new CaseService(mockRepo.Object, ...);

    // Act
    var result = await service.TransitionStatusAsync(1, CaseStatus.Finalized);

    // Assert
    Assert.False(result);  // invalid transition
}
```

### Step 3.7: docs/attribution-CC-BY-SA-4.0.md

> **Depends on**: Nothing.

Document the dataset attribution as required by the CC BY-SA 4.0 license:

```markdown
# Dataset Attribution

## Bangladesh Legal Acts Dataset
- **Source**: sakhadib/bangladesh-legal-acts-dataset (Kaggle)
- **License**: CC BY-SA 4.0
- **Usage**: Batch-imported into MuktoAin's statutory corpus for RAG retrieval
- **Modifications**: Normalized into Act → Section → Chunk hierarchy;
  sub-chunked for embedding; Full-Text indexed

## Bangladesh Legal QA Dataset
- **Source**: momahadi/bangladesh-legal-qa-dataset (Hugging Face)
- **License**: CC BY 4.0
- **Usage**: Evaluation benchmark for QA harness (2,165 annotated QA pairs)
```

---

## Dependency Map

Every task Arpita can't start until someone else delivers something:

| Arpita's Task | Blocked By | Teammate | Their Specific Task |
|---|---|---|---|
| **1.1** All DTOs | All 14 entities + 9 enums | **Tultul** | Step 1.2 + 1.3 (entities and enums) |
| **2.1** CaseService | `ICaseRepository` + `AppDbContext` + repositories | **Tultul** | Steps 1.4, 1.6, 1.12 (interfaces, DbContext, repo implementations) |
| **2.2** DocumentGenerator | `AiOrchestrationService` (produces the `RightsExplanationDto` that templates consume) | **Shads** | Step 2.4 of Shads_plan.md |
| **2.4** DocumentService | `DocumentGenerator` (her own Step 2.2) + Tultul's repos | **Tultul** | Repository implementations |
| **2.5** PdfExportService | Bangla font file in `wwwroot/fonts/` | **Erin** | Erin places Noto Sans Bengali font in wwwroot |
| **2.6** LawyerVerificationService | `LawyerProfile` entity + repository | **Tultul** | Entity + repo |
| **2.7** LawyerReviewService | Steps 2.4 + 2.6 (DocumentService + LawyerVerificationService — her own prior steps) | Self | — |
| **3.4** AdminAnalyticsService | Cases must exist in DB (seed data or test data) | **Tultul** | Seed data loaders |

### What Arpita Can Start Immediately (After Tultul's Entities Land)

1. ✅ All DTOs (Step 1.1) — can draft from design.md field specs even before entities compile
2. ✅ `IDocumentTemplate` interface definition (needed for Step 2.2)
3. ✅ Template content research — look up actual Bangladesh legal document formats
4. ✅ QuestPDF "hello world" with Bangla text — test font rendering immediately
5. ✅ `docs/attribution-CC-BY-SA-4.0.md` (Step 3.7)

### Parallel Work Strategy

While waiting for Tultul's entities (Day 1-2):
1. Draft all DTOs from `design.md` field specs (record type shapes, won't compile until entities exist)
2. Research Bangladesh legal document formats for all 4 templates
3. Install QuestPDF, test Bangla font rendering with a standalone console test
4. Write `docs/attribution-CC-BY-SA-4.0.md`

Once Tultul's PR lands (Day 2-3):
1. Finalize and compile DTOs
2. Start CaseService immediately

Once Shads's AiOrchestrationService lands (Day ~5):
1. Wire DocumentGenerator to consume RightsExplanationDto
2. Build LabourComplaintTemplate (vertical slice)
3. Build DocumentService + PdfExportService
4. Build LawyerVerification + LawyerReview

CP3 (Day 7+):
1. Remaining 3 templates (copy pattern from Labour)
2. AdminAnalyticsService
3. Unit tests
