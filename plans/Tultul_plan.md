# Tultul — Data Foundation & Search Infrastructure Plan

> ponytail: full — every step earns its place. No boilerplate tasks. Options are real choices.

**Role:** Tultul owns everything from raw schema to searchable index — the 14 entities, the data import pipeline, and the retrieval infrastructure that other teammates build on top of. She is the critical path: Shads, Arpita, and Erin are all blocked until her entities and interfaces land.

---

## Checkpoint 1: Schema, Data Pipeline, Qdrant Store

### Step 1.1: Initialize the .NET 8 Solution

> **Depends on**: Shads completing repo setup (Step 1-3 of Shads_plan.md — repo exists, branches created).

1. Create the solution with 4 projects:
   ```
   dotnet new sln -n MuktoAin -o src
   dotnet new classlib -n MuktoAin.Domain -o src/MuktoAin.Domain
   dotnet new classlib -n MuktoAin.Application -o src/MuktoAin.Application
   dotnet new classlib -n MuktoAin.Infrastructure -o src/MuktoAin.Infrastructure
   dotnet new mvc -n MuktoAin.Web -o src/MuktoAin.Web
   ```
2. Add projects to the solution:
   ```
   dotnet sln src/MuktoAin.sln add src/MuktoAin.Domain src/MuktoAin.Application src/MuktoAin.Infrastructure src/MuktoAin.Web
   ```
3. Set up project references enforcing the Clean Architecture dependency rule:
   ```
   MuktoAin.Application → references MuktoAin.Domain
   MuktoAin.Infrastructure → references MuktoAin.Domain
   MuktoAin.Web → references MuktoAin.Application, MuktoAin.Infrastructure
   ```
   ```
   dotnet add src/MuktoAin.Application reference src/MuktoAin.Domain
   dotnet add src/MuktoAin.Infrastructure reference src/MuktoAin.Domain
   dotnet add src/MuktoAin.Web reference src/MuktoAin.Application src/MuktoAin.Infrastructure
   ```
   **Critical rule**: `MuktoAin.Domain` references NOTHING. Zero NuGet packages, zero project references. If you need to add a dependency to Domain, you're doing it wrong.

4. Add test projects:
   ```
   dotnet new xunit -n MuktoAin.UnitTests -o tests/MuktoAin.UnitTests
   dotnet new xunit -n MuktoAin.IntegrationTests -o tests/MuktoAin.IntegrationTests
   dotnet sln src/MuktoAin.sln add tests/MuktoAin.UnitTests tests/MuktoAin.IntegrationTests
   ```

5. Verify it builds:
   ```
   dotnet build src/MuktoAin.sln
   ```

### Step 1.2: Implement All 9 Enums

> **Depends on**: Step 1.1 (solution must exist).

Create in `MuktoAin.Domain/Enums/`. Each enum is a single file:

```csharp
// 1. UserRole.cs
public enum UserRole { Citizen = 0, Lawyer = 1, Admin = 2 }

// 2. AccountStatus.cs
public enum AccountStatus { Active = 0, Suspended = 1 }

// 3. VerificationStatus.cs
public enum VerificationStatus { Pending = 0, Approved = 1, Rejected = 2 }

// 4. CaseStatus.cs
public enum CaseStatus { Submitted = 0, UnderReview = 1, Finalized = 2 }

// 5. DocumentType.cs
public enum DocumentType { GeneralDiary = 0, RtiRequest = 1, LabourComplaint = 2, ConsumerComplaint = 3 }

// 6. DocumentStatus.cs
public enum DocumentStatus { Draft = 0, UnderReview = 1, Approved = 2, Rejected = 3 }

// 7. ReviewDecision.cs
public enum ReviewDecision { Approved = 0, EditedApproved = 1, Rejected = 2 }

// 8. RetrievalMethod.cs
public enum RetrievalMethod { Keyword = 0, Vector = 1 }

// 9. AiRequestType.cs
public enum AiRequestType { LawIdentification = 0, RightsExplanation = 1, Drafting = 2 }
```

- ponytail: One file per enum in a flat `Enums/` folder. No sub-folders, no base classes, no enum helpers. Ceiling: flat enum files; upgrade path: if enums need display names or localized labels later, add extension methods — don't convert to classes.

### Step 1.3: Implement All 14 Domain Entities

> **Depends on**: Step 1.2 (enums must exist since entities reference them).

Create in `MuktoAin.Domain/Entities/`. One file per entity. Follow the exact field specs from `.agent/spec/design.md` §2.

**Entity list (build in this order — dependencies flow top-down):**

#### Tier 1 — No foreign keys to other entities:
1. **`District.cs`** — `DistrictId` (PK, byte since only 64 rows), `Name`
2. **`CaseCategory.cs`** — `CategoryId` (PK), `Name`, `Description`

#### Tier 2 — References Tier 1:
3. **`User.cs`** — All fields from spec. The `CreatedByAdminId` is a self-referencing FK.
   - **Decision point**: Shads needs this to inherit `IdentityUser<int>` for ASP.NET Identity. Two ways to handle:
     - **Option A**: Add `: IdentityUser<int>` now. Means `MuktoAin.Domain` needs a NuGet reference to `Microsoft.Extensions.Identity.Stores`. This technically violates the "zero dependencies" rule for Domain.
     - **Option B**: Keep `User.cs` as a plain POCO. Shads creates a separate `ApplicationUser : IdentityUser<int>` in Infrastructure that maps to the same table. Cleaner but more wiring.
     - **Recommendation**: Option A with a pragmatic exception. The `Microsoft.Extensions.Identity.Stores` package is a thin abstractions package (just interfaces and base classes, no EF dependency). For a student project, one small dependency in Domain is better than the complexity of dual-user mapping. Coordinate with Shads.

4. **`Act.cs`** — `ActId`, `Title`, `ActNumber`, `Year`, `PublicationDate`, `Language`, `IsRepealed`, `TokenCount`, `SourceUrl`, `ImportedAt`. Add a navigation property: `ICollection<ActSection> Sections`, `ICollection<ActFootnote> Footnotes`.

#### Tier 3 — References Tier 2:
5. **`LawyerProfile.cs`** — `LawyerProfileId`, `UserId` (FK → User), `BarRegistrationNumber`, `VerificationStatus`, `VerifiedByAdminId` (FK → User), `Specialization`, `VerifiedAt`. Nav property: `User User`, `User VerifiedByAdmin`.
6. **`Case.cs`** — `CaseId`, `UserId` (nullable FK → User), `CategoryId` (FK → CaseCategory), `DistrictId` (FK → District), `Title`, `Description`, `Language`, `Status`, `IsAnonymous`, `CreatedAt`, `UpdatedAt`. Nav properties: `User? User`, `CaseCategory Category`, `District District`.
7. **`ActSection.cs`** — `SectionId`, `ActId` (FK → Act), `OrdinalPosition`, `SectionNumber` (nullable), `SectionTitle` (nullable), `SectionText`. Nav properties: `Act Act`, `ICollection<ActSectionChunk> Chunks`.
8. **`ActFootnote.cs`** — `FootnoteId`, `ActId` (FK → Act), `FootnoteOrder`, `FootnoteText`.

#### Tier 4 — References Tier 3:
9. **`ActSectionChunk.cs`** — `ChunkId`, `SectionId` (FK → ActSection), `ChunkOrder`, `ChunkText`, `TokenCount`, `VectorId` (nullable), `ContentHash` (nullable), `LastEmbeddedAt` (nullable). Nav property: `ActSection Section`.
10. **`ScenarioMapping.cs`** — `MappingId`, `SectionId` (FK → ActSection), `ScenarioKeyword`, `Notes`.
11. **`GeneratedDocument.cs`** — `DocumentId`, `CaseId` (FK → Case), `DocumentType`, `ContentDraft`, `ContentFinal` (nullable), `Status`, `PdfPath` (nullable), `CreatedAt`.
12. **`CaseActReference.cs`** — `CaseActReferenceId`, `CaseId` (FK → Case), `SectionId` (FK → ActSection), `RelevanceScore`, `RetrievalMethod`.

#### Tier 5 — References Tier 4:
13. **`LawyerReview.cs`** — `ReviewId`, `DocumentId` (FK → GeneratedDocument), `LawyerProfileId` (FK → LawyerProfile), `Decision`, `Comments`, `ReviewedAt`.
14. **`AiLog.cs`** — `LogId` (BIGINT/long), `CaseId` (nullable FK → Case), `RequestType`, `PromptText`, `ResponseText`, `ModelUsed`, `TokensUsed`, `LatencyMs` (INT — round-trip API call duration in milliseconds, required by FR-12), `CreatedAt`.

**General rules for all entities:**
- Use `int` for most PKs. Use `byte` for `District.DistrictId` (only 64 rows). Use `long` for `AiLog.LogId` (high-volume).
- Use C# nullable reference types (`string?`) for nullable fields.
- Navigation properties use `virtual` for lazy loading (optional — decide whether to enable lazy loading or always use `.Include()`).
  - **Option A**: Eager loading only (`.Include()` in every query). Explicit, no surprises.
  - **Option B**: Enable lazy loading proxies (`UseLazyLoadingProxies()` in DbContext config). Less boilerplate, risk of N+1 queries.
  - **Recommendation**: Option A. Lazy loading hides performance problems. In a student project where query patterns aren't well-understood yet, explicit `.Include()` is safer.

### Step 1.4: Implement Repository Interfaces

> **Depends on**: Step 1.3 (entities must exist to be referenced in interface signatures).

Create in `MuktoAin.Domain/Interfaces/Repositories/`.

Start with a generic base + entity-specific interfaces:

```csharp
// IRepository.cs — generic base
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(int id);
    Task<IEnumerable<T>> GetAllAsync();
    Task AddAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(T entity);
    Task SaveChangesAsync();
}
```

Then entity-specific interfaces only when they need methods beyond CRUD:

```csharp
// IActRepository.cs
public interface IActRepository : IRepository<Act>
{
    Task<Act?> GetWithSectionsAsync(int actId);
    Task<IEnumerable<Act>> SearchByTitleAsync(string query);
}

// IActSectionRepository.cs
public interface IActSectionRepository : IRepository<ActSection>
{
    Task<IEnumerable<ActSection>> GetBySectionIdsAsync(IEnumerable<int> sectionIds);
    Task<IEnumerable<ActSection>> FullTextSearchAsync(string query, int maxResults);
}

// ICaseRepository.cs
public interface ICaseRepository : IRepository<Case>
{
    Task<IEnumerable<Case>> GetByUserIdAsync(int userId);
    Task<Case?> GetWithDocumentsAsync(int caseId);
}

// IActSectionChunkRepository.cs
public interface IActSectionChunkRepository : IRepository<ActSectionChunk>
{
    Task<IEnumerable<ActSectionChunk>> GetUnembeddedChunksAsync(int batchSize);
    Task UpdateEmbeddingInfoAsync(int chunkId, string vectorId, string contentHash);
}

// IScenarioMappingRepository.cs — consumed by Shads's PromptAssembler (eng review)
public interface IScenarioMappingRepository : IRepository<ScenarioMapping>
{
    Task<IEnumerable<ScenarioMapping>> SearchByKeywordAsync(string keywordFragment);
}
```

- ponytail: Only create specific interfaces for entities that actually need custom query methods. `District`, `CaseCategory`, `ActFootnote`, `ScenarioMapping` — just use `IRepository<T>` directly. Don't create `IDistrictRepository` with zero custom methods. Ceiling: 4-5 specific interfaces; upgrade path: add new interfaces when a service method can't express its query through `IRepository<T>`.

### Step 1.5: Implement Service Interfaces

> **Depends on**: Step 1.3 (entities/DTOs referenced in signatures).

Create in `MuktoAin.Domain/Interfaces/Services/`. These are the contracts other teammates code against:

```csharp
// IAiService.cs — Shads implements
public interface IAiService
{
    Task<string> GenerateContentAsync(string prompt);
}

// IEmbeddingService.cs — Shads implements
public interface IEmbeddingService
{
    Task<float[]> GetEmbeddingAsync(string text);
}

// IVectorStore.cs — you implement
public interface IVectorStore
{
    Task UpsertAsync(string vectorId, float[] embedding, Dictionary<string, string> payload);
    Task<IEnumerable<VectorSearchResult>> SearchAsync(float[] queryVector, int topK);
    Task DeleteAsync(string vectorId);
}

// IRagContextBuilder.cs — you implement
public interface IRagContextBuilder
{
    Task<IEnumerable<RetrievedSection>> RetrieveContextAsync(string query, int topK = 8);
}
```

Add DTOs/result types as simple records in `Domain/Interfaces/Services/` or a shared `Domain/Models/` folder:
```csharp
public record VectorSearchResult(string VectorId, float Score, Dictionary<string, string> Payload);
public record RetrievedSection(int SectionId, string ActTitle, string SectionNumber, string SectionText, float RelevanceScore, RetrievalMethod Method);
```

- ponytail: Interfaces go in Domain. Implementations go in Application (for business logic) or Infrastructure (for external integrations). Don't create interfaces for things no one will swap. `ISearchService` exists because the search could be FTS or vector. `IDistrictService` probably doesn't need to exist — just inject the repository. Ceiling: ~6-8 service interfaces; upgrade path: add when a second implementation becomes plausible.

### Step 1.6: EF Core Packages + Manual MSSQL DDL Scripts in SSMS & DbContext Mapping

> **Depends on**: Step 1.3 (entity definitions).

1. Install EF Core packages in `MuktoAin.Infrastructure`:
   ```
   dotnet add src/MuktoAin.Infrastructure package Microsoft.EntityFrameworkCore.SqlServer
   dotnet add src/MuktoAin.Infrastructure package Microsoft.EntityFrameworkCore.Tools
   ```

2. All database schema creation, keys, constraints, and indexes are authored as **manual MSSQL scripts** and executed via **SQL Server Management Studio (SSMS)**.

1. Create `scripts/01_init_database.sql`:
   ```sql
   IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'MuktoAin')
   BEGIN
       CREATE DATABASE MuktoAin;
   END
   GO
   USE MuktoAin;
   GO
   ```

2. Create `scripts/02_schema.sql` with complete manual T-SQL DDL for all 14 tables:
   - `dbo.USER` (with `UserId INT IDENTITY PRIMARY KEY`, unique email, `AccountStatus`, `Role`)
   - `dbo.LAWYER_PROFILE` (`LawyerProfileId INT IDENTITY PRIMARY KEY`, `UserId INT UNIQUE FK`, bar reg number)
   - `dbo.CASE_CATEGORY` (`CategoryId INT IDENTITY PRIMARY KEY`, `Name`, `Description`)
   - `dbo.DISTRICT` (`DistrictId TINYINT PRIMARY KEY`, `NameEn`, `NameBn`, `Division`)
   - `dbo.ACT` (`ActId INT IDENTITY PRIMARY KEY`, `ActNumber`, `Year`, `Title`, `ActType`, `Status`)
   - `dbo.ACT_SECTION` (`SectionId INT IDENTITY PRIMARY KEY`, `ActId FK`, `SectionNumber`, `SectionText`, `IsAmended`)
   - `dbo.ACT_SECTION_CHUNK` (`ChunkId INT IDENTITY PRIMARY KEY`, `SectionId FK`, `ChunkIndex`, `ChunkText`, `VectorId`, `TokenCount`, `ContentHash`)
   - `dbo.ACT_FOOTNOTE` (`FootnoteId INT IDENTITY PRIMARY KEY`, `ActId FK`, `NoteText`, `ReferenceSection`)
   - `dbo.CASE` (`CaseId INT IDENTITY PRIMARY KEY`, `UserId NULLABLE FK`, `CategoryId FK`, `DistrictId FK`, `Title`, `Description`, `Language`, `Status`, `AnonymousTrackingCode NVARCHAR(36) NULL` (guest tracking for FR-8, see Arpita_plan.md Step 2.1), `CreatedAt`)
   - `dbo.SCENARIO_MAPPING` (`MappingId INT IDENTITY PRIMARY KEY`, `SectionId FK`, `ScenarioKeyword`, `Notes`)
   - `dbo.GENERATED_DOCUMENT` (`DocumentId INT IDENTITY PRIMARY KEY`, `CaseId FK`, `DocumentType`, `ContentDraft`, `ContentFinal`, `Status`, `PdfPath`, `AssignedLawyerProfileId INT NULL FK → LAWYER_PROFILE` (review-claim guard, see Arpita_plan.md Step 2.7), `CreatedAt`)
   - `dbo.CASE_ACT_REFERENCE` (`CaseActReferenceId INT IDENTITY PRIMARY KEY`, `CaseId FK`, `SectionId FK`, `RelevanceScore`, `RetrievalMethod`)
   - `dbo.LAWYER_REVIEW` (`ReviewId INT IDENTITY PRIMARY KEY`, `DocumentId FK`, `LawyerProfileId FK`, `Decision`, `Comments`, `ReviewedAt`)
   - `dbo.AI_LOG` (`LogId BIGINT IDENTITY PRIMARY KEY`, `CaseId NULLABLE FK`, `RequestType`, `PromptText`, `ResponseText`, `ModelUsed`, `TokensUsed`, `LatencyMs`, `CreatedAt`)

3. Execute both scripts in **SSMS** against your SQL Server instance. Verify all 14 tables and foreign keys are created cleanly in Object Explorer.

   > **Naming & reserved-word rules (applies to every script and raw SQL query in this repo):**
   > - Table names follow `design.md` §2 exactly, UPPERCASE with underscores: `[dbo].[USER]`, `[dbo].[CASE]`, `[dbo].[ACT_SECTION]`, etc.
   > - `USER` and `CASE` are T-SQL reserved words — they MUST be bracket-delimited (`[dbo].[USER]`) in every script, view, and raw SQL string. Unbracketed `dbo.USER` / `dbo.CASE` will not parse in SSMS.
   > - EF Core maps entities to these tables via explicit `ToTable("USER", "dbo")` configuration — do NOT rely on DbSet property-name pluralization.
   > - Raw SQL (e.g. `FromSqlRaw`) must reference the same bracketed physical names.

4. Create `Infrastructure/Data/AppDbContext.cs` to map to the SQL tables:
   ```csharp
   public class AppDbContext : DbContext
   {
       public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

       public DbSet<Act> Acts => Set<Act>();
       public DbSet<ActSection> ActSections => Set<ActSection>();
       public DbSet<ActSectionChunk> ActSectionChunks => Set<ActSectionChunk>();
       public DbSet<ActFootnote> ActFootnotes => Set<ActFootnote>();
       public DbSet<Case> Cases => Set<Case>();
       public DbSet<CaseCategory> CaseCategories => Set<CaseCategory>();
       public DbSet<CaseActReference> CaseActReferences => Set<CaseActReference>();
       public DbSet<District> Districts => Set<District>();
       public DbSet<GeneratedDocument> GeneratedDocuments => Set<GeneratedDocument>();
       public DbSet<LawyerProfile> LawyerProfiles => Set<LawyerProfile>();
       public DbSet<LawyerReview> LawyerReviews => Set<LawyerReview>();
       public DbSet<ScenarioMapping> ScenarioMappings => Set<ScenarioMapping>();
       public DbSet<AiLog> AiLogs => Set<AiLog>();

       protected override void OnModelCreating(ModelBuilder modelBuilder)
       {
           base.OnModelCreating(modelBuilder);
           modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
       }
   }
   ```
   **Important**: The database schema is controlled and managed directly in SSMS via the SQL scripts. EF Core configurations map entity properties to these predefined MSSQL tables.

### Step 1.7: Seed Data Loaders

> **Depends on**: Step 1.6 (DbContext + SSMS scripts executed so tables exist).

Create in `Infrastructure/Data/Seeding/`:

1. **`SeedDistricts.cs`** — Load `data/districts.json` → insert 64 `District` rows.
   ```csharp
   public static async Task SeedAsync(AppDbContext context)
   {
       if (await context.Districts.AnyAsync()) return;  // idempotent
       var json = await File.ReadAllTextAsync("data/districts.json");
       var districts = JsonSerializer.Deserialize<List<District>>(json);
       context.Districts.AddRange(districts!);
       await context.SaveChangesAsync();
   }
   ```

2. **`SeedCategories.cs`** — Load `data/categories.json` → insert 4 `CaseCategory` rows. Same pattern.

3. **`SeedScenarioMappings.cs`** — Load `data/scenario-mappings.json` → insert `ScenarioMapping` rows. These are hand-curated keyword-to-section mappings. Start with a small set (~20-30 mappings for the Labour Act) and expand later.
   - ponytail: Don't over-invest in scenario mappings at this stage. The RAG pipeline should work WITHOUT them — they're a boost signal, not a requirement. Ship with 20 mappings. Ceiling: static JSON file; upgrade path: admin UI for managing mappings (FR-18).

4. **Wire seeders into `Program.cs`:**
   ```csharp
   using (var scope = app.Services.CreateScope())
   {
       var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
       // NOTE: schema comes from manual SSMS scripts (scripts/*.sql), NOT EF migrations.
       // Do NOT call Database.MigrateAsync() — there are no migrations by design.
       // Seeders assume the SSMS scripts have already been executed.
       await SeedDistricts.SeedAsync(context);
       await SeedCategories.SeedAsync(context);
       // SeedAdminUser is Shads's responsibility — he'll add it here
   }
   ```

### Step 1.8: Batch Import Pipeline (Acts → Sections → Footnotes)

> **Depends on**: Step 1.6 (tables must exist) + Shads placing `data/bangladesh-acts-dataset.json` in the repo or shared location.

This is the biggest data task. The Kaggle dataset (`sakhadib/bangladesh-legal-acts-dataset`) contains ~1,484 Acts in a JSON structure. You need to parse it into the normalized `Act` → `ActSection` → `ActFootnote` hierarchy.

1. Create `Infrastructure/Data/Seeding/SeedActsFromJson.cs`.

2. **First: understand the dataset structure.** Download the dataset and examine the JSON. The fields will map to your entities:
   - Top-level object → `Act` (title, number, year, etc.)
   - Nested sections array → `ActSection` (section number, title, text)
   - Nested footnotes → `ActFootnote` (amendment records)

3. Implementation:
   ```
   1. Read and deserialize the JSON file
   2. For each Act object:
      a. Check if Act with same Title+Year already exists → skip (idempotent)
      b. Create Act entity, save to get ActId
      c. For each section in the Act:
         i.  Create ActSection with OrdinalPosition set sequentially
         ii. Save to get SectionId
      d. For each footnote:
         i.  Create ActFootnote with FootnoteOrder set sequentially
   3. Save all via batch insert (use AddRange, not Add in a loop)
   ```

4. **Performance consideration**: ~1,484 Acts with potentially 50,000+ sections. Don't insert one-by-one.
   - **Option A**: `AddRange()` per Act (insert all sections for one Act at once). Reasonable — ~1,484 SaveChanges calls.
   - **Option B**: Bulk insert in batches of 500 using `context.BulkInsertAsync()` from EFCore.BulkExtensions. Fastest, adds a NuGet dependency.
   - **Option C**: Use raw SQL `BULK INSERT` via `context.Database.ExecuteSqlRawAsync()`. Fastest, most fragile.
   - **Recommendation**: Option A. 1,484 SaveChanges calls takes maybe 2-5 minutes on LocalDB. That's a one-time import. Don't add a library for a one-time job.
   - ponytail: This runs once. It doesn't need to be fast. It needs to be correct and idempotent. Ceiling: per-Act SaveChanges; upgrade path: EFCore.BulkExtensions if re-import time becomes a problem.

### Step 1.9: Sub-Chunking Logic

> **Depends on**: Step 1.8 (ActSection rows must exist to chunk).

Long statutory sections can be thousands of tokens. Embedding models have context limits, and shorter chunks give more precise retrieval. Split long sections into ~300-500 token sub-chunks.

1. Create a utility: `Infrastructure/Data/Seeding/ChunkingService.cs` (or inline in the seeder).

2. Logic:
   ```
   For each ActSection:
     if TokenCount(SectionText) <= 500:
       Create 1 ActSectionChunk with ChunkOrder=1, ChunkText=SectionText
     else:
       Split SectionText into chunks of ~400 tokens with ~50 token overlap
       For each chunk, create ActSectionChunk with sequential ChunkOrder
   ```

3. **Token counting**: You need a rough token count. Options:
   - **Option A**: Use the actual Gemini tokenizer API (`countTokens` endpoint). Accurate but slow (API call per section).
   - **Option B**: Approximate: `text.Length / 4` for English, `text.Length / 2` for Bangla. Fast, rough.
   - **Option C**: Use a local tokenizer library (e.g., `SharpToken` for tiktoken). Accurate, no API calls, but adds a dependency.
   - **Recommendation**: Option B for the chunking decision. The exact token count doesn't matter for splitting — you just need "is this section long enough to need splitting?" `Length / 3` is a safe middle ground for mixed Bangla/English text.

4. **Splitting strategy**: Split on paragraph boundaries first (`\n\n`), then sentence boundaries (`. `), then at the word nearest to the target size. Overlap ensures retrieval doesn't miss context that spans a split point.
   - ponytail: Don't build a recursive text splitter. Split on `\n\n`, fall back to `. `, fall back to word boundary. Three `if` statements. Ceiling: paragraph-first splitter; upgrade path: semantic chunking (split by meaning, not by length) when you have a local embedding model to detect topic shifts.

5. Set `VectorId = null` and `ContentHash = null` — Shads's `EmbeddingBatchJob` will fill these in.

6. Compute `TokenCount` for each chunk (using the same heuristic) and store it.

### Step 1.10: SQL Server Full-Text Index Script (`scripts/03_fulltext.sql`)

> **Depends on**: Step 1.6 (`ActSections` table must exist in SSMS).

We configure SQL Server Full-Text Search via a manual MSSQL script executed in SSMS.

1. Create `scripts/03_fulltext.sql`:
   ```sql
   USE MuktoAin;
   GO

   -- 1. Create Full-Text Catalog if not exists
   IF NOT EXISTS (SELECT * FROM sys.fulltext_catalogs WHERE name = 'MuktoAinCatalog')
   BEGIN
       CREATE FULLTEXT CATALOG MuktoAinCatalog AS DEFAULT;
   END
   GO

    -- 2. Create Full-Text Index on ActSections
    -- NOTE: ACT_SECTION has NO ActTitle column (title lives on [dbo].[ACT]).
    -- Index SectionText only; filter/join by Act title at query time via
    -- IActRepository.GetWithSectionsAsync or a JOIN in the FTS query.
    IF NOT EXISTS (
        SELECT * FROM sys.fulltext_indexes 
        WHERE object_id = OBJECT_ID('[dbo].[ACT_SECTION]')
    )
    BEGIN
        CREATE FULLTEXT INDEX ON [dbo].[ACT_SECTION](SectionText)
            KEY INDEX [PK_ACT_SECTION]
            ON MuktoAinCatalog
            WITH STOPLIST = OFF;
    END
    GO
    ```
    `STOPLIST = OFF` is crucial — Bangla legal terms and domain vocabulary must not be dropped by SQL Server's English-centric stoplist.

2. Open and execute `scripts/03_fulltext.sql` in **SSMS**. Verify the index status:
   ```sql
   SELECT FULLTEXTCATALOGPROPERTY('MuktoAinCatalog', 'ItemCount') AS IndexedItemCount;
   ```

### Step 1.11: QdrantVectorStore.cs

> **Depends on**: Step 1.5 (IVectorStore interface) + Shads setting up Qdrant (Step 7 of Shads_plan.md).

1. Install the Qdrant .NET SDK:
   ```
   dotnet add src/MuktoAin.Infrastructure package Qdrant.Client
   ```

2. Create `Infrastructure/VectorStore/QdrantVectorStore.cs` implementing `IVectorStore`.

3. Core methods:
   ```csharp
   public class QdrantVectorStore : IVectorStore
   {
        private readonly QdrantClient _client;
        // Collection name MUST come from config (appsettings.Development.json is gitignored).
        // Every teammate's local SQL Server generates INDEPENDENT IDENTITY values —
        // teammate B's ChunkId 123 is a different chunk than teammate A's. Sharing one
        // canonical collection across four local DBs corrupts retrieval.
        // Convention: each developer sets Qdrant:Collection to "act_section_chunks_<name>"
        // locally; the shared/canonical "act_section_chunks" collection is written ONLY by
        // the single EmbeddingBatchJob run against a merged database (Shads runs it once
        // after Tultul's import PR merges).
        private string CollectionName => _options.Value.Collection ?? "act_section_chunks";
        private const int VectorSize = 768;  // text-embedding-004 output dimension

        public QdrantVectorStore(IOptions<QdrantOptions> options)
        {
            // NOTE: verify against the pinned Qdrant.Client version before coding.
            // The constructor takes (host, port, https, apiKey) — NOT a full URL string.
            // Parse options.Value.Endpoint into host/https/port. Recent SDK versions
            // deprecated SearchAsync in favor of QueryAsync — check the API surface
            // on day 1 with a 15-minute spike (see TODOS.md).
            var uri = new Uri(options.Value.Endpoint);
            _client = new QdrantClient(uri.Host, port: uri.Port, https: uri.Scheme == "https",
                apiKey: options.Value.ApiKey);
        }

       public async Task EnsureCollectionAsync()
       {
           // Create collection if it doesn't exist
           var collections = await _client.ListCollectionsAsync();
           if (!collections.Any(c => c == CollectionName))
           {
               await _client.CreateCollectionAsync(CollectionName, 
                   new VectorParams { Size = VectorSize, Distance = Distance.Cosine });
           }
       }

       public async Task UpsertAsync(string vectorId, float[] embedding, Dictionary<string, string> payload)
       {
           var point = new PointStruct
           {
               Id = new PointId { Uuid = vectorId },
               Vectors = embedding,
               Payload = { /* convert payload dict to Qdrant payload */ }
           };
           await _client.UpsertAsync(CollectionName, new[] { point });
       }

       public async Task<IEnumerable<VectorSearchResult>> SearchAsync(float[] queryVector, int topK)
       {
           var results = await _client.SearchAsync(CollectionName, queryVector, limit: (ulong)topK);
           return results.Select(r => new VectorSearchResult(
               r.Id.Uuid, r.Score, /* extract payload */));
       }
   }
   ```

4. **Important**: Store the `SectionId`, `ActTitle`, and `SectionNumber` in the Qdrant payload alongside the vector. This allows retrieval results to be mapped back to SQL entities without a separate DB query per result.

5. Call `EnsureCollectionAsync()` on app startup (in `Program.cs` next to the seed methods).

6. Create a `QdrantOptions` POCO:
   ```csharp
    public class QdrantOptions
    {
        public string Endpoint { get; set; } = "https://your-cluster-id.cloud.qdrant.io:6333";
        public string ApiKey { get; set; } = "";
        public string? Collection { get; set; }  // per-developer namespace, see above
    }
   ```
   - ponytail: The Qdrant SDK handles connection pooling and retries internally. Don't wrap it in Polly — that's for HTTP clients you control. Ceiling: direct SDK usage; upgrade path: add health check endpoint for monitoring.

### Step 1.12: Repository Implementations with Manual SQL Queries

> **Depends on**: Steps 1.4 + 1.6 (interfaces + DbContext / SQL connection).

Create in `Infrastructure/Repositories/`. Repositories write explicit, parameterized MSSQL queries (`SELECT`, `INSERT`, `UPDATE`, `DELETE`, and Full-Text queries) rather than relying on opaque ORM generation.

```csharp
public class Repository<T> : IRepository<T> where T : class
{
    protected readonly AppDbContext _context;
    protected readonly DbSet<T> _dbSet;

    public Repository(AppDbContext context)
    {
        _context = context;
        _dbSet = context.Set<T>();
    }

    public virtual async Task<T?> GetByIdAsync(int id) => await _dbSet.FindAsync(id);
    public virtual async Task<IEnumerable<T>> GetAllAsync() => await _dbSet.ToListAsync();
    public virtual async Task AddAsync(T entity) => await _dbSet.AddAsync(entity);
    public virtual Task UpdateAsync(T entity) { _dbSet.Update(entity); return Task.CompletedTask; }
    public virtual Task DeleteAsync(T entity) { _dbSet.Remove(entity); return Task.CompletedTask; }
    public virtual async Task SaveChangesAsync() => await _context.SaveChangesAsync();
}
```

Specific repositories with explicit, manual SQL queries:

```csharp
public class ActSectionRepository : Repository<ActSection>, IActSectionRepository
{
    public ActSectionRepository(AppDbContext context) : base(context) { }

    public async Task<IEnumerable<ActSection>> GetBySectionIdsAsync(IEnumerable<int> sectionIds)
    {
        // Manual parameterized query
        var idsList = string.Join(",", sectionIds);
        return await _dbSet
            .FromSqlRaw("SELECT * FROM [dbo].[ACT_SECTION] WHERE SectionId IN (SELECT value FROM STRING_SPLIT({0}, ','))", idsList)
            .Include(s => s.Act)
            .ToListAsync();
    }

    public async Task<IEnumerable<ActSection>> FullTextSearchAsync(string query, int maxResults)
    {
        // Manual MSSQL FTS query using CONTAINSTABLE ranking.
        // Join [dbo].[ACT] so the Act title is available for filtering/display
        // (the full-text index covers SectionText only).
        return await _dbSet.FromSqlInterpolated($@"
            SELECT TOP({maxResults}) s.*
            FROM [dbo].[ACT_SECTION] s
            INNER JOIN CONTAINSTABLE([dbo].[ACT_SECTION], SectionText, {query}) AS ft
                ON s.SectionId = ft.[KEY]
            ORDER BY ft.[RANK] DESC")
            .Include(s => s.Act)
            .ToListAsync();
    }
}
```

### Step 1.13: Program.cs — DI Wiring

> **Depends on**: Steps 1.6, 1.11, 1.12 (all infrastructure services must exist to register).

Wire everything in `MuktoAin.Web/Program.cs`:

```csharp
// DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Repositories
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddScoped<IActRepository, ActRepository>();
builder.Services.AddScoped<IActSectionRepository, ActSectionRepository>();
builder.Services.AddScoped<ICaseRepository, CaseRepository>();
builder.Services.AddScoped<IActSectionChunkRepository, ActSectionChunkRepository>();

// Infrastructure services (your implementations)
builder.Services.Configure<QdrantOptions>(builder.Configuration.GetSection("Qdrant"));
builder.Services.AddSingleton<IVectorStore, QdrantVectorStore>();

// Shads will add: Identity, GeminiClient, AI services
// Arpita will add: DocumentService, ReviewService, etc.
```

Leave clear `// TODO: [Teammate] will register [service] here` comments so teammates know where to add their registrations.

---

## Checkpoint 2: Search Services + RAG Orchestration

### Step 2.1: SimilaritySearchService.cs

> **Depends on**: Step 1.11 (QdrantVectorStore) + Shads completing `IEmbeddingService` implementation (to embed the user's query).

1. Create `Infrastructure/VectorStore/SimilaritySearchService.cs`.
2. Takes a query string → embeds it → searches Qdrant → returns ranked results.

```csharp
public class SimilaritySearchService
{
    private readonly IEmbeddingService _embedding;
    private readonly IVectorStore _vectorStore;
    private readonly IActSectionRepository _sectionRepo;

    public async Task<IEnumerable<RetrievedSection>> SearchAsync(string query, int topK = 8)
    {
        var queryVector = await _embedding.GetEmbeddingAsync(query);
        var vectorResults = await _vectorStore.SearchAsync(queryVector, topK);

        // Map Qdrant results back to full ActSection entities
        var sectionIds = vectorResults.Select(r => int.Parse(r.Payload["SectionId"]));
        var sections = await _sectionRepo.GetBySectionIdsAsync(sectionIds);

        return vectorResults.Select(vr =>
        {
            var section = sections.First(s => s.SectionId == int.Parse(vr.Payload["SectionId"]));
            return new RetrievedSection(
                section.SectionId, section.Act.Title, section.SectionNumber ?? "",
                section.SectionText, vr.Score, RetrievalMethod.Vector);
        });
    }
}
```

### Step 2.2: KeywordSearchService.cs

> **Depends on**: Step 1.10 (FTS index) + Step 1.12 (ActSectionRepository with `FullTextSearchAsync`).

1. Create `Infrastructure/Search/KeywordSearchService.cs`.
2. Two use cases:
   - **FR-7 standalone search**: Citizen browses/searches the Acts corpus directly.
   - **FR-3 fallback**: RagContextBuilder calls this when Qdrant is down.

```csharp
public class KeywordSearchService
{
    private readonly IActSectionRepository _sectionRepo;

    public async Task<IEnumerable<RetrievedSection>> SearchAsync(string query, int maxResults = 20)
    {
        // Sanitize query for CONTAINS() syntax
        var sanitized = SanitizeForFts(query);
        var sections = await _sectionRepo.FullTextSearchAsync(sanitized, maxResults);

        return sections.Select(s => new RetrievedSection(
            s.SectionId, s.Act.Title, s.SectionNumber ?? "",
            s.SectionText, 0f, RetrievalMethod.Keyword));
    }

    private static string SanitizeForFts(string query)
    {
        // Escape special FTS characters, wrap terms in quotes for phrase search
        // or split into OR'd terms for broader results
        // Decision: Use FORMSOF(INFLECTIONAL, ...) for English terms
        // and exact match for Bangla terms (no stemmer for Bangla in SQL Server)
        return $"\"{query.Replace("\"", "")}\"";
    }
}
```

- **Option A**: Simple phrase search (`"exact query"` in CONTAINS). Precise, may miss relevant sections.
- **Option B**: Split words and OR them. Broader, noisier.
- **Option C**: Use `FREETEXT()` instead of `CONTAINS()`. Most permissive, SQL Server handles stemming.
- **Recommendation**: `FREETEXT()` for the fallback path (you want maximum recall when vector search is down). `CONTAINS()` for the standalone search (user expects keyword matching). Make the method take a `SearchMode` parameter.

### Step 2.3: RagContextBuilder.cs — The Orchestrator

> **Depends on**: Steps 2.1 + 2.2 (both search services must exist).

This is the service that Shads's `AiOrchestrationService` calls. It tries vector search first, falls back to FTS if Qdrant fails.

1. Create `Application/Services/RagContextBuilder.cs` implementing `IRagContextBuilder`.

```csharp
public class RagContextBuilder : IRagContextBuilder
{
    // IMPORTANT: depend on Domain interfaces, NOT concrete Infrastructure classes.
    // Application must never reference Infrastructure (Clean Architecture rule).
    // Define IVectorSectionSearch and IKeywordSectionSearch in Domain/Interfaces/Services/;
    // SimilaritySearchService and KeywordSearchService implement them in Infrastructure.
    private readonly IVectorSectionSearch _vectorSearch;
    private readonly IKeywordSectionSearch _keywordSearch;
    private readonly ILogger<RagContextBuilder> _logger;

    public async Task<IEnumerable<RetrievedSection>> RetrieveContextAsync(string query, int topK = 8)
    {
        try
        {
            var results = await _vectorSearch.SearchAsync(query, topK);
            if (results.Any()) return results;

            // Vector search returned nothing — fall back
            _logger.LogWarning("Vector search returned 0 results, falling back to FTS");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Vector search failed, falling back to FTS");
        }

        // Fallback: SQL Server Full-Text Search
        return await _keywordSearch.SearchAsync(query, topK);
    }
}
```

- ponytail: This is a try-catch with a fallback. That's all it is. Don't build a "retrieval strategy pattern" or a "search provider chain." Ceiling: try/catch fallback; upgrade path: circuit breaker on the vector path so repeated failures stop trying for 30 seconds.

### Step 2.4: SearchService.cs (Standalone Acts Search — FR-7)

> **Depends on**: Step 2.2 (KeywordSearchService).

1. Create `Application/Services/SearchService.cs`.
2. This wraps `KeywordSearchService` for the citizen-facing standalone search feature (FR-7). It adds pagination, filtering by Act, and result formatting.

```csharp
public class SearchService
{
    private readonly KeywordSearchService _fts;
    private readonly IActRepository _actRepo;

    // Note: Uses Arpita's SearchResultDto record from MuktoAin.Application/DTOs/
    public async Task<SearchResultDto> SearchActsAsync(string query, int page = 1, int pageSize = 20, int? actId = null)
    {
        var results = await _fts.SearchAsync(query, maxResults: 100);

        if (actId.HasValue)
            results = results.Where(r => /* filter by actId */);

        var paged = results.Skip((page - 1) * pageSize).Take(pageSize);

        return new SearchResultDto(
            Query: query,
            TotalResults: results.Count(),
            Page: page,
            Results: paged.Select(r => new CitedSectionDto(
                r.SectionId, r.ActTitle, r.SectionNumber, r.SectionText,
                r.RelevanceScore, r.Method.ToString())).ToList());
    }
}
```

### Step 2.5: CategoryService.cs (FR-6)

> **Depends on**: Step 1.12 (repository for CaseCategory).

1. Create `Application/Services/CategoryService.cs`.
2. Simple CRUD over categories. Citizens browse categories without submitting a case.

```csharp
public class CategoryService
{
    private readonly IRepository<CaseCategory> _categoryRepo;

    public async Task<IEnumerable<CaseCategory>> GetAllCategoriesAsync()
        => await _categoryRepo.GetAllAsync();

    public async Task<CaseCategory?> GetByIdAsync(int id)
        => await _categoryRepo.GetByIdAsync(id);
}
```

- ponytail: This is two methods that call the generic repo. It exists so controllers don't inject repositories directly (Application layer boundary). If this feels too thin, it is. But the layer boundary matters. Ceiling: CRUD passthrough; upgrade path: add category statistics, popular categories, etc.

---

## Checkpoint 3: Maintenance + Testing

### Step 3.1: ActsManagementService.cs (FR-17)

> **Depends on**: Step 1.8 (import pipeline) + Step 1.9 (chunking) + Shads completing `EmbeddingBatchJob`.

Incremental re-indexing: when a statute is amended, re-import only the changed sections.

1. Create `Application/Services/ActsManagementService.cs`.
2. Logic:
   ```
   1. Load updated JSON (or a single Act's data)
   2. For each section: compute SHA-256 hash of SectionText
   3. Compare with existing ActSection.ContentHash (via ActSectionChunk)
   4. If hash differs:
      a. Update SectionText
      b. Delete old chunks for this section
      c. Re-chunk the new text
      d. Set VectorId = null on new chunks (triggers re-embedding by Shads's batch job)
   5. If hash matches: skip — no change
   ```

- ponytail: This is a diff-and-update loop. The ContentHash on `ActSectionChunk` is the mechanism — if the hash matches, the embedding is still valid. Don't build a full versioning system. Ceiling: hash-based change detection; upgrade path: version history table if you need to track what changed when.

### Step 3.2: ScenarioMappingService.cs (FR-18 — Admin CRUD)

> **Depends on**: Step 1.4 (repository interfaces) + Step 1.12 (repository implementations).

FR-18 requires admin management of scenario mappings. Erin will build the admin view (`Views/Admin/ScenarioMappings.cshtml`); this service provides the backend.

1. Create `Application/Services/ScenarioMappingService.cs`.
2. Core methods:
   ```csharp
   public class ScenarioMappingService
   {
       private readonly IRepository<ScenarioMapping> _mappingRepo;
       private readonly IActSectionRepository _sectionRepo;

       public async Task<IEnumerable<ScenarioMapping>> GetAllAsync()
           => await _mappingRepo.GetAllAsync();

       public async Task<int> AddMappingAsync(int sectionId, string keyword, string? notes)
       {
           // Verify section exists
           var section = await _sectionRepo.GetByIdAsync(sectionId);
           if (section == null) throw new ArgumentException("Section not found");

           var mapping = new ScenarioMapping
           {
               SectionId = sectionId,
               ScenarioKeyword = keyword,
               Notes = notes
           };
           await _mappingRepo.AddAsync(mapping);
           await _mappingRepo.SaveChangesAsync();
           return mapping.MappingId;
       }

       public async Task<bool> DeleteMappingAsync(int mappingId)
       {
           var mapping = await _mappingRepo.GetByIdAsync(mappingId);
           if (mapping == null) return false;
           await _mappingRepo.DeleteAsync(mapping);
           await _mappingRepo.SaveChangesAsync();
           return true;
       }
   }
   ```
   - ponytail: Three methods — list, add, delete. No update (if a mapping is wrong, delete and re-add). Ceiling: flat CRUD; upgrade path: bulk import from JSON for batch updates.

### Step 3.3: Unit Tests for Repositories

> **Depends on**: Steps 1.6 + 1.12 (DbContext + repo implementations).

1. Create tests in `tests/MuktoAin.UnitTests/Repositories/`.
2. Use EF Core's `InMemoryDatabase` for isolation:
   ```csharp
   var options = new DbContextOptionsBuilder<AppDbContext>()
       .UseInMemoryDatabase("TestDb_" + Guid.NewGuid())
       .Options;
   using var context = new AppDbContext(options);
   ```

3. Test at minimum:
   - `ActRepository.GetWithSectionsAsync` — returns Act with loaded sections
   - `ActSectionRepository.GetSectionsByActIdAsync` — LINQ filtering works on the InMemory provider
   - ~~`ActSectionRepository.GetBySectionIdsAsync`~~ — **NOT testable here**: implemented via `FromSqlRaw`, which throws on the InMemory provider. Cover it in integration tests with real SQL Server (see CI integration job in Shads_plan.md Step 3.4).
   - `ActSectionChunkRepository.GetUnembeddedChunksAsync` — returns only chunks with null VectorId
   - `CaseRepository.GetByUserIdAsync` — returns only that user's cases
   - Generic `Repository<T>` CRUD — add, get, update, delete roundtrip

- ponytail: InMemoryDatabase doesn't support FTS or raw SQL. Don't test `FullTextSearchAsync` here — that goes in integration tests. Ceiling: InMemory provider for LINQ queries; upgrade path: Testcontainers with real SQL Server for integration.

### Step 3.4: Database Integration Tests

> **Depends on**: All data pipeline steps + a running SQL Server instance.

1. Create tests in `tests/MuktoAin.IntegrationTests/Database/`.
2. Use a real SQL Server (LocalDB) with a test database:
   ```csharp
   var options = new DbContextOptionsBuilder<AppDbContext>()
       .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=MuktoAin_Test;...")
       .Options;
   ```
3. Test:
   - Migration applies cleanly
   - Seed data loads (64 districts, 4 categories)
   - Act import produces expected row counts
   - Full-Text Search returns results for known queries
   - FK constraints prevent orphaned records

### Step 3.5: docs/architecture.md

> **Depends on**: Nothing — write whenever, update as architecture evolves.

Document:
- The 4-project Clean Architecture structure and dependency rules
- The 14-entity ERD (can reference `design.md` or include a simplified version)
- The retrieval pipeline (vector-primary + FTS fallback)
- Data flow: JSON → Act → Section → Chunk → Qdrant → Retrieval
- How to add a new entity or repository (for future contributors)

---

## Dependency Map

Every task Tultul can't start until someone else delivers something:

| Tultul's Task | Blocked By | Teammate | Their Specific Task |
|---|---|---|---|
| **1.1** Solution scaffold | Repo must exist on GitHub with branches | **Shads** | Steps 1-3 of Shads_plan.md (repo setup) |
| **2.1** SimilaritySearchService | `IEmbeddingService` implementation (to embed user's query) | **Shads** | `GeminiEmbeddingService.cs` (Step 1.4 of Shads_plan.md) |

That's it. **Tultul has only 2 external dependencies.** Almost everything she builds depends only on her own prior steps.

### What Tultul Can Start Immediately (After Shads Creates the Repo)

1. ✅ Solution scaffold (Step 1.1)
2. ✅ All 9 enums (Step 1.2)
3. ✅ All 14 entities (Step 1.3)
4. ✅ Repository interfaces (Step 1.4)
5. ✅ Service interfaces (Step 1.5)
6. ✅ Manual MSSQL DDL scripts in SSMS + AppDbContext (Step 1.6)
7. ✅ Seed data loaders (Step 1.7)
8. ✅ Batch import pipeline (Step 1.8)
9. ✅ Sub-chunking logic (Step 1.9)
10. ✅ FTS index script in SSMS (Step 1.10)
11. ✅ QdrantVectorStore (Step 1.11)
12. ✅ Repository implementations with SQL queries (Step 1.12)
13. ✅ Program.cs DI wiring (Step 1.13)
14. ✅ KeywordSearchService (Step 2.2) — depends only on her own repo
15. ✅ CategoryService (Step 2.5) — depends only on her own repo

### Blocked Task

- ❌ **SimilaritySearchService** (Step 2.1) — needs Shads's `IEmbeddingService` to embed the query vector. Can write the class structure but can't test it until Shads delivers.

### Parallel Work Strategy

Tultul's work is almost entirely self-contained. The critical insight: **she IS the critical path.** Everyone else is waiting on her entities and interfaces. Prioritize:

1. **Day 1-2**: Steps 1.1 → 1.5 (solution, enums, entities, interfaces). PR this immediately — Shads, Arpita, Erin are all blocked.
2. **Day 2-3**: Step 1.6 (SSMS SQL DDL scripts + DbContext mapping). PR this — Shads can start Identity.
3. **Day 3-5**: Steps 1.7 → 1.9 (seed data, import pipeline, chunking). PR this — Shads can start embedding batch job.
4. **Day 5-6**: Steps 1.10 → 1.13 (FTS script in SSMS, Qdrant store, SQL repos, DI wiring).
5. **Day 6+**: CP2 search services. By now Shads should have `GeminiEmbeddingService` ready, unblocking Step 2.1.

## GSTACK REVIEW REPORT

| Run | Scope | Status |
|---|---|---|
| Eng review r1 | Architecture A1-A6 · Code C1-C7 · Tests · Perf P1-P2 | complete — all findings applied in-file |
| Outside voice r2 | Claude subagent (Codex absent) — 8 new findings O1-O8 | complete — all applied in-file |

| Finding | Fix location |
|---|---|
| LocalDB lacks FTS; connection strings | _Initial_setup_plan.md (Express Advanced mandated) |
| FTS indexed nonexistent ActTitle; naming rules | _Initial_setup + Tultul Step 1.6 (SectionText only, bracketed names) |
| Clean Arch violations (DI concretes, DocumentGenerator layer) | Tultul RagContextBuilder → Domain interfaces; Arpita templates → Application/Documents |
| Identity-in-Domain exception (A4) | Shads Option A note + AGENTS.md §3.1 amendment instruction |
| Review claim race + ownership (A5/O2) | Arpita: AssignedLawyerProfileId, queue filter, SubmitReview guard |
| Guest authz null-compare + anonymous tracking (A6/O3) | Arpita GetCaseDetailAsync rewrite; AnonymousTrackingCode on CASE |
| XSS Html.Raw (C2) | Erin Result view encoded render |
| Gemini key clobber, Qdrant ctor, MigrateAsync, ScenarioMapping (C1/C5/C6/C7) | Shads + Tultul respective steps |
| Encryption scope, AI_LOG redaction, cached explanations (O4/O7) | Shads Steps 2.6/2.8 pipeline step 0; Erin contract note |
| Analytics SQL table names (O5) | Arpita Step 3.4 bracketed real names |
| CI red-by-construction; InMemory vs FromSqlRaw (O6) | Shads CI yaml split unit/integration + SQL container; Tultul test list |
| Multi-doc state machine (O8) | Arpita approve/reject sibling guards + Finalized→Submitted row |

**VERDICT:** CROSS-MODEL absorbed (Claude subagent standing in for Codex; zero contradictions between models).

Deferred items live in TODOS.md (Qdrant SDK spike, ScenarioMapping retrieval-boost depth, Program.cs merge convention, CI FTS-image choice).

NO UNRESOLVED DECISIONS
