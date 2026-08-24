# Shads — Project Lead Plan

> ponytail: full — every step earns its place. No boilerplate tasks. Options are real choices.

---

## Part 1: Initial Repository Setup (Project Lead Duties)

You're the lead. Before anyone writes a line of service code, the repo needs to exist, the tooling needs to work, and each teammate needs a clean surface to build on.

### Step 1: Create the GitHub Repository

1. Create `muktoAin-ISD` on GitHub (private, no template).
2. Clone it locally. You already have the `.agent/` spec folder and `AGENTS.md` — commit those as the initial commit.
3. Add a `.gitignore` for .NET:
   ```
   dotnet new gitignore
   ```
4. Add `.editorconfig` for consistent formatting across 4 developers:
   ```
   dotnet new editorconfig
   ```
5. Push the initial commit to `main`.

### Step 2: Set Up Branch Protection

On GitHub → Settings → Branches → `main`:
- Require pull request reviews before merging (1 reviewer minimum).
- Require status checks to pass (add this after CI is set up in CP3; skip for now).
- No direct pushes to `main`.

### Step 3: Create Feature Branches for Each Teammate

Create and push these branches so teammates can start immediately:
```
git branch shads/identity-ai-core
git branch tultul/schema-data-pipeline
git branch arpita/document-pipeline
git branch erin/frontend-views
git push origin --all
```

### Step 4: Assign Teammate Starting Points

Send each teammate their section of the team split doc and tell them:

- **Tultul**: "Start with the solution scaffold. Create the 4-project .sln, all 14 entities, all 9 enums, all interfaces, AppDbContext, and the manual MSSQL DDL scripts in `scripts/`. Push a PR when the SQL scripts execute cleanly in SSMS. Everyone else is blocked until your entities + interfaces land."

- **Arpita**: "Start with all DTOs in `MuktoAin.Application/DTOs/`. You can write these from the entity specs in `.agent/spec/design.md` without waiting for Tultul's entities to compile — just match the field names. Once Tultul's interfaces land, start `CaseService` and `DocumentService`."

- **Erin**: "Start with `_Layout.cshtml`, `_DisclaimerBanner.cshtml`, `_LanguageToggle.cshtml`, and the `wwwroot/` folder (CSS, JS, Bangla fonts). I'll give you the UI/UX design drafts. You can build all shared partials and the Home views without waiting for anyone."

### Step 5: Prepare Seed Data Files

Before Tultul's import pipeline can run, these JSON files need to exist in `data/`:

1. **`data/districts.json`** — 64 Bangladesh administrative districts. Source: Wikipedia "Districts of Bangladesh" article. Format:
   ```json
   [
     { "districtId": 1, "name": "Bagerhat" },
     { "districtId": 2, "name": "Bandarban" },
     ...
   ]
   ```
   Decision: Tultul owns this file, but you should verify the list is complete (64 rows, no duplicates, correct Romanized spellings).

2. **`data/categories.json`** — legal case categories. These are your design decision. Start with 4 matching the document templates:
   ```json
   [
     { "categoryId": 1, "name": "Labour Complaint", "description": "Unpaid wages, wrongful termination, unsafe working conditions" },
     { "categoryId": 2, "name": "General Diary (GD)", "description": "Police station report for theft, assault, threats, missing persons" },
     { "categoryId": 3, "name": "RTI Request", "description": "Right to Information application to government bodies" },
     { "categoryId": 4, "name": "Consumer Complaint", "description": "Defective products, fraudulent services, consumer rights violations" }
    ]
    ```

3. **`data/scenario-mappings.json`** — hand-curated keyword-to-section mappings. Start with ~20-30 entries for the Labour Act, targeting common citizen complaint phrases (Bangla + English) mapped to specific `ActSection.SectionId` values. Format:
   ```json
   [
     { "sectionId": 123, "scenarioKeyword": "unpaid wages", "notes": "Labour Act s.123 — wage payment timeline" },
     { "sectionId": 123, "scenarioKeyword": "বেতন দেয়নি", "notes": "Same section, Bangla keyword" }
   ]
   ```
   Expand to other Acts once the Labour vertical is validated.

4. **`data/bangladesh-acts-dataset.json`** — download from Kaggle (`sakhadib/bangladesh-legal-acts-dataset`). This is a ~50-100MB file. Add it to `.gitignore` and distribute via shared drive or Git LFS.
   - **Option A**: Git LFS (cleaner for CI/CD, costs storage).
   - **Option B**: `.gitignore` the file, add a `data/README.md` with the Kaggle download link and SHA256 hash for verification.
   - **Recommendation**: Option B for a student project — simpler, free.

### Step 6: Set Up Multi-Key Gemini API Key Rotation

The Gemini free tier quota (~1,000–1,500 RPD, 5–15 RPM) is per **Google Cloud project**, not per API key. With 4 teammates, each creates their own free Google AI Studio project — that's 4 independent quota buckets. You wire them all into a rotator so the app automatically switches to the next key when one is exhausted.

**Each teammate does this:**
1. Go to [aistudio.google.com](https://aistudio.google.com) — sign in with their own Google account.
2. Create a new project (**do NOT enable billing** — enabling billing removes the free tier for that project).
3. Go to API Keys → Create API Key.
4. Send you the key via secure channel (Signal DM, not GitHub, not Discord).

**You do this once you have all 4 keys:**

Store the keys as an array in `appsettings.Development.json` (this file is `.gitignore`d — never commit it):
```json
{
  "Gemini": {
    "ApiKeys": [
      "key-from-shads-project",
      "key-from-tultul-project",
      "key-from-arpita-project",
      "key-from-erin-project"
    ],
    "EmbeddingModel": "text-embedding-004",
    "GenerationModel": "gemini-2.5-flash"
  }
}
```

**How rotation works:** `GeminiClient` keeps an index into the key list. On every request, it uses the current key. If it gets a `429 RESOURCE_EXHAUSTED` response, it increments the index (mod 4) and retries immediately with the next key. If all 4 keys are exhausted (very unlikely in dev), it falls back to exponential backoff.

**Practical impact:** ~1,200 RPD × 4 keys = ~4,800 requests/day shared across all teammates. The embedding batch job (~10,000 chunks) will use most of this on the first run — rotate all 4 keys during the batch. After that, the dev traffic (manual testing) is light enough that 1 key is fine.

> **Note on fairness:** Each teammate's key draws from their own project quota. No one teammate's dev usage burns another's budget. The rotator uses all keys roughly equally (round-robin for normal traffic, failover on 429).

### Step 7: Set Up Qdrant Cloud (Primary for CP1 & CP2)

We use **Qdrant Cloud Free Tier** initially for Checkpoints 1 and 2 to eliminate local Docker setup friction across teammates. Docker containerization (`Dockerfile` and `docker-compose.yml`) will be added in **Checkpoint 3 (Step 3.3)**.

1. Sign up at [cloud.qdrant.io](https://cloud.qdrant.io) and create a free 1GB cluster.
2. In the Qdrant Cloud console, generate an API key with read/write access.
3. Copy the cluster endpoint URL (e.g., `https://xxxxxx.us-east-1.cloud.qdrant.io:6333`) and API key.
4. Share the URL and API key with teammates via the secure channel for their `appsettings.Development.json`.
5. In `appsettings.Development.json.template`, provide the format:
   ```json
   {
     "Qdrant": {
       "Endpoint": "https://your-cluster-id.cloud.qdrant.io:6333",
       "ApiKey": "your-qdrant-api-key"
     }
   }
   ```

### Step 8: Share Configuration Template

Create `appsettings.Development.json.template` (committed to repo) so teammates know exactly what to fill in:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=MuktoAin;Trusted_Connection=True;MultipleActiveResultSets=true"
  },
  "Gemini": {
    "ApiKeys": [
      "REPLACE_WITH_KEY_1",
      "REPLACE_WITH_KEY_2",
      "REPLACE_WITH_KEY_3",
      "REPLACE_WITH_KEY_4"
    ],
    "EmbeddingModel": "text-embedding-004",
    "GenerationModel": "gemini-2.5-flash"
  },
  "Qdrant": {
    "Endpoint": "http://localhost:6333",
    "ApiKey": ""
  }
}
```

Each teammate copies this to `appsettings.Development.json`, adds the keys you send them, and the rotator handles the rest. They don't need to know which index their key is at.

---

## Part 2: Shads's Checkpoint Tasks (Serialized)

### Checkpoint 1: Identity + Gemini Client + Embedding Job

#### Step 1.1: ASP.NET Core Identity Configuration

> **Depends on**: Tultul completing `User.cs` entity, `AppDbContext.cs`, and the manual MSSQL schema scripts in SSMS.

Once Tultul's schema PR is merged and scripts executed in SSMS:

1. Install Identity packages in `MuktoAin.Infrastructure`:
   ```
   dotnet add package Microsoft.AspNetCore.Identity.EntityFrameworkCore
   ```
2. Make `AppDbContext` inherit from `IdentityDbContext<User, IdentityRole<int>, int>` (or configure Identity to use Tultul's existing `User` entity).
   - **Option A**: Use ASP.NET Identity's built-in `IdentityUser` as a base class for `User.cs`. Simpler, but adds Identity columns Tultul didn't design.
   - **Option B**: Map Identity onto Tultul's existing `User` entity using a custom `IUserStore<User>`. Cleaner schema, more code.
   - **Recommendation**: Option A — inherit `IdentityUser<int>` in the `User` entity. Coordinate with Tultul to add `: IdentityUser<int>` to `User.cs` before his PR merges. The extra Identity columns are harmless and save 200+ lines of custom store code.
   - **Documented exception**: this adds `Microsoft.Extensions.Identity.Stores` as a NuGet dependency in `MuktoAin.Domain`, which contradicts the AGENTS.md rule "Domain: zero dependencies on external libraries." We accept this knowingly (pragmatism > purity for a student project). **Update AGENTS.md §3.1 in the same PR** to record the exception, so the written rule matches reality. Revisit only if Domain grows more dependencies.
3. Configure Identity in `Program.cs` (Tultul owns this file — either he adds the Identity registration or you add it in your PR):
   ```csharp
   builder.Services.AddIdentity<User, IdentityRole<int>>(options =>
   {
       options.Password.RequireDigit = true;
       options.Password.RequiredLength = 8;
   })
   .AddEntityFrameworkStores<AppDbContext>()
   .AddDefaultTokenProviders();
   ```
4. Configure cookie authentication (redirect to `/Account/Login`).
5. Add role seeding: create `Citizen`, `Lawyer`, `Admin` roles on startup.

#### Step 1.2: SeedAdminUser.cs

> **Depends on**: Step 1.1 (Identity must be configured first).

1. Create `SeedAdminUser.cs` in `Infrastructure/Data/Seeding/`.
2. On app startup, check if an admin user exists. If not, create one:
   ```csharp
   var adminEmail = "admin@muktoain.bd";
   var adminUser = await userManager.FindByEmailAsync(adminEmail);
   if (adminUser == null)
   {
       adminUser = new User
       {
           FullName = "System Administrator",
           Email = adminEmail,
           UserName = adminEmail,
           Role = UserRole.Admin,
           AccountStatus = AccountStatus.Active
       };
       await userManager.CreateAsync(adminUser, "Admin@123!");
       await userManager.AddToRoleAsync(adminUser, "Admin");
   }
   ```
3. Make the admin password configurable via `appsettings` for production (don't hardcode in prod).

#### Step 1.3: GeminiClient.cs (with key rotation)

> **Depends on**: Nothing — can start immediately, only needs the Gemini API keys from teammates.

1. Create `Infrastructure/Ai/GeminiClient.cs`.
2. Thin HTTP wrapper around Gemini's REST API. Use `HttpClient` via `IHttpClientFactory`.
3. Two public methods:
   - `Task<string> GenerateContentAsync(string prompt)` — calls `/v1beta/models/{model}:generateContent`
   - `Task<float[]> EmbedContentAsync(string text)` — calls `/v1beta/models/{model}:embedContent`

4. **Key rotation implementation** — the core addition. The client holds the list of API keys and an `_currentKeyIndex` counter:
   ```csharp
   public class GeminiClient : IAiService
   {
       private readonly string[] _apiKeys;
       private int _currentKeyIndex = 0;
       private readonly object _lock = new();
   
       public GeminiClient(IOptions<GeminiOptions> options, IHttpClientFactory factory)
       {
           _apiKeys = options.Value.ApiKeys;  // array from config
           // ...
       }
   
       private string GetCurrentKey()
       {
           lock (_lock) { return _apiKeys[_currentKeyIndex % _apiKeys.Length]; }
       }
   
       private void RotateKey()
       {
           lock (_lock) { _currentKeyIndex = (_currentKeyIndex + 1) % _apiKeys.Length; }
       }
   
       private async Task<HttpResponseMessage> SendWithRotationAsync(HttpRequestMessage request, CancellationToken ct)
       {
           int attempts = 0;
           while (attempts < _apiKeys.Length)
           {
               // Attach the current key as a query param: ?key={apiKey}
               var uriWithKey = new UriBuilder(request.RequestUri!);
               uriWithKey.Query = $"key={GetCurrentKey()}";
               request.RequestUri = uriWithKey.Uri;
   
                var response = await _httpClient.SendAsync(request, ct);
    
                if ((int)response.StatusCode == 429)   // RESOURCE_EXHAUSTED
                {
                    RotateKey();
                    attempts++;
                    await Task.Delay(200, ct);  // brief pause before retry
                    // Re-clone the request (HttpRequestMessage can't be re-sent)
                    request = CloneRequest(request);
                    continue;
                }
    
                return response;   // success or non-429 error — caller handles
            }
            throw new Exception("All Gemini API keys exhausted. Wait for quota reset.");
        }
    }
    ```
    - The `CloneRequest` helper is necessary because `HttpRequestMessage` is single-use. 5 lines. Don't abstract it. Ceiling: in-process round-robin, no persistent key-state across restarts; upgrade path: Redis-backed key index so multiple app instances share rotation state.
    - **Query-param rule**: when attaching the key, PRESERVE any existing query parameters and append `key` — do not overwrite the whole querystring:
      ```csharp
      var qb = new UriBuilder(request.RequestUri!);
      var query = $"key={Uri.EscapeDataString(GetCurrentKey())}";
      qb.Query = string.IsNullOrEmpty(qb.Query.TrimStart('?'))
          ? query
          : $"{qb.Query.TrimStart('?')}&{query}";
      request.RequestUri = qb.Uri;
      ```

5. **Polly for transient errors** (network timeouts, 5xx — NOT 429, which is handled by rotation):
   - **Option A**: Polly v7 extension on `IHttpClientBuilder` — `AddTransientHttpErrorPolicy()`.
   - **Option B**: Polly v8 `ResiliencePipeline` — the .NET 8 idiomatic path.
   - **Recommendation**: Polly v8. Register it in DI, keep the pipeline simple: 3 retries with exponential backoff on `HttpRequestException` and 5xx only. Do NOT add 429 to Polly's retry — the rotator handles it.

6. Parse the JSON response. Gemini returns:
   - Generation: `candidates[0].content.parts[0].text`
   - Embedding: `embedding.values` (array of floats)

7. Create a `GeminiOptions` POCO bound to config:
   ```csharp
   public class GeminiOptions
   {
       public string[] ApiKeys { get; set; } = [];
       public string EmbeddingModel { get; set; } = "text-embedding-004";
       public string GenerationModel { get; set; } = "gemini-2.5-flash";
   }
   ```
   Register in `Program.cs`: `builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection("Gemini"));`

8. Implement `IAiService` (defined by Tultul in Domain).
   - ponytail: The interface IS the abstraction. `GeminiClient` is the only implementation you'll write. If providers change, write a new class — don't add abstraction layers now. Ceiling: single concrete client; upgrade path: new class per provider, switch via config.

#### Step 1.4: GeminiEmbeddingService.cs

> **Depends on**: Step 1.3 (`GeminiClient` must exist to call the embedding API).

1. Create `Infrastructure/Ai/GeminiEmbeddingService.cs` implementing `IEmbeddingService`.
2. Single method: `Task<float[]> GetEmbeddingAsync(string text)`.
3. This is a thin wrapper that calls `GeminiClient.EmbedContentAsync()`. It exists as a separate service because `IEmbeddingService` is the interface other services depend on (not `IAiService`).
   - ponytail: If this feels like unnecessary indirection, it is for now. But the split matters when the embedding model and generation model are different API endpoints or even different providers. Keep it. Ceiling: one-method wrapper; upgrade path: batch embedding, caching.

#### Step 1.5: EmbeddingBatchJob.cs (GPU/Compute Task)

> **Depends on**: Tultul completing the batch import pipeline (all `ActSectionChunk` rows must exist in SQL Server) AND Tultul completing `QdrantVectorStore.cs` (to upsert vectors).

This is the heaviest compute task in the project. It reads every `ActSectionChunk` from SQL, embeds it via Gemini, and upserts the vector into Qdrant.

1. Create `Infrastructure/VectorStore/EmbeddingBatchJob.cs` implementing `IHostedService` or as a console command.
   - **Option A**: `IHostedService` that runs on app startup if a flag is set (`--seed-embeddings`).
   - **Option B**: Separate console app / CLI command (`dotnet run --project MuktoAin.Web -- embed`).
   - **Option C**: A controller endpoint (`POST /admin/embed`) gated behind admin auth.
   - **Recommendation**: Option A with a config flag. Keeps it in-process, no extra project. Run it once, then flip the flag off.

2. Implementation logic:
   ```
   1. Query all ActSectionChunk rows where VectorId IS NULL or LastEmbeddedAt < some threshold
   2. For each chunk (batched, not one-by-one):
      a. Compute SHA-256 hash of ChunkText → compare with ContentHash
      b. If hash matches and VectorId exists → skip (already embedded)
      c. Call GeminiEmbeddingService.GetEmbeddingAsync(chunk.ChunkText)
      d. Call QdrantVectorStore.UpsertAsync(vectorId, embedding, payload)
      e. Update chunk row: VectorId, ContentHash, LastEmbeddedAt
   3. Log progress every 100 chunks
   ```

3. **Rate limiting**: Gemini free tier is ~5-15 RPM for embeddings. For ~10,000 chunks at 10 RPM, that's ~17 hours if you do one-at-a-time. Batch the embedding API calls (Gemini supports batch embedding — multiple texts in one request). With batches of 10-20, this drops to 1-2 hours.
   - ponytail: Don't build a queue system. A `for` loop with `Task.Delay` between batches and a try-catch-retry is enough. Ceiling: sequential batching; upgrade path: parallel workers with rate limiter.

4. **Resumability**: If the job crashes halfway, it should pick up where it left off. The `WHERE VectorId IS NULL` query handles this naturally.

#### Step 1.6: Integration Test — RAG Retrieval Smoke Test

> **Depends on**: Steps 1.3, 1.4, 1.5 (Gemini client + embeddings must work), AND Tultul's `QdrantVectorStore.cs` + `SimilaritySearchService.cs`.

1. Write one integration test in `tests/MuktoAin.IntegrationTests/AiPipeline/`:
   ```csharp
   [Fact]
   public async Task Labour_Query_Returns_Labour_Act_Sections()
   {
       var query = "আমার বেতন ৩ মাস দেয়নি";  // "Haven't been paid for 3 months"
       var embedding = await embeddingService.GetEmbeddingAsync(query);
       var results = await vectorStore.SearchAsync(embedding, topK: 5);
       
       Assert.NotEmpty(results);
       Assert.Contains(results, r => r.Payload["ActTitle"].Contains("Labour"));
   }
   ```
2. This test validates the entire CP1 pipeline: data imported → chunks created → embedded → Qdrant indexed → retrieval works.
3. Additional required tests (from eng review):
   - `DisclaimerInjector` unit test: appends the correct language disclaimer exactly once, never mutates original text.
   - `EncryptionService` roundtrip test: encrypt→decrypt returns original for Bangla + English strings; ciphertext ≠ plaintext.
   - Key-exhaustion behavior test: all keys return 429 → exception surfaces a clear user-facing error message (not a raw stack trace).

---

### Checkpoint 2: Prompt Engineering + AI Services + Disclaimer

#### Step 2.1: Constants — PromptTemplates.cs and Disclaimers.cs

> **Depends on**: Nothing — pure constants, can be written anytime.

1. `Domain/Constants/Disclaimers.cs`:
   ```csharp
   public static class Disclaimers
   {
       public const string Legal = "⚠️ MuktoAin provides general legal information and document drafting assistance. This is NOT formal legal advice. Every document must be reviewed by a verified lawyer before use. For urgent legal matters, consult a qualified advocate.";
       
       public const string LegalBangla = "⚠️ মুক্ত আইন সাধারণ আইনি তথ্য ও নথি প্রণয়নে সহায়তা প্রদান করে। এটি আনুষ্ঠানিক আইনি পরামর্শ নয়। প্রতিটি নথি ব্যবহারের পূর্বে একজন যাচাইকৃত আইনজীবী দ্বারা পর্যালোচনা করা আবশ্যক।";
   }
   ```

2. `Domain/Constants/PromptTemplates.cs`:
   ```csharp
   public static class PromptTemplates
   {
       public const string RightsExplanation = """
           You are a legal information assistant for Bangladesh. 
           A citizen has described this problem: {problem}
           
           Based ONLY on the following statutory sections, explain their rights 
           in plain {language}. Cite specific Act names and Section numbers.
           
           Relevant statutory text:
           {context}
           
           Rules:
           - Only cite sections provided above. Never fabricate citations.
           - Use simple language a non-lawyer can understand.
           - If the provided sections don't cover the problem, say so explicitly.
           - End with: {disclaimer}
           """;
       
       // Add more templates as needed for drafting, etc.
   }
   ```
   - ponytail: Start with 2 templates (rights explanation + document drafting). Add more only when a real use case demands it. Ceiling: string constants; upgrade path: template files on disk or database-stored prompts.

#### Step 2.2: DisclaimerInjector.cs

> **Depends on**: Step 2.1 (needs `Disclaimers.cs` constants).

1. Create `Application/Services/DisclaimerInjector.cs`.
   - **Note**: This goes in Application, not Infrastructure — it performs no external I/O (pure string concatenation with a constant). Placing it in Application keeps the dependency arrow correct since `AiOrchestrationService` (also Application) consumes it.
2. Single method: `string InjectDisclaimer(string aiResponse, string language)`.
3. Appends the appropriate disclaimer (English or Bangla) to the end of every AI response.
4. This is surface 2 of 3 in the disclaimer policy. Erin handles surface 1 (`_DisclaimerBanner.cshtml`), Arpita handles surface 3 (PDF stamp).
   - ponytail: This is literally string concatenation with a newline. Don't over-engineer it. Ceiling: append-only; upgrade path: structured response wrapper if the frontend needs to render the disclaimer separately.

#### Step 2.3: PromptAssembler.cs

> **Depends on**: Step 2.1 (templates) + Tultul completing `RagContextBuilder.cs` (to get retrieved sections as context).

1. Create `Application/Services/PromptAssembler.cs`.
   - **Note**: Like DisclaimerInjector, this is pure string manipulation — no external I/O. It belongs in Application so `AiOrchestrationService` can consume it without a reverse dependency.
2. Takes: citizen's problem description, retrieved statutory sections (from RAG), scenario mappings, language preference.
3. Returns: a fully assembled prompt string ready to send to Gemini.
4. Logic:
   ```
   1. Select the right PromptTemplate based on request type (rights explanation vs drafting)
   2. Substitute {problem}, {language}, {disclaimer}
3. Build {context} from the retrieved sections:
   - For each section: "Act: {ActTitle}, Section {SectionNumber}: {SectionText}"
   - Truncate if total context exceeds model context window (~100K tokens for Flash, so this won't be a problem for 5-8 sections)
4. Retrieve matching `SCENARIO_MAPPING` rows for the query keywords and inject them as boost hints (design.md §3 step 5 requires this; Tultul seeds the data, admin manages it via FR-18 — this step is where it is actually CONSUMED). Query via `IScenarioMappingRepository.SearchByKeywordAsync()` (add to Tultul's repo interface batch).
5. Optionally inject few-shot examples (CP3 task)
6. Return assembled prompt
   ```
   - ponytail: String interpolation. Don't build a template engine. Ceiling: string.Replace placeholders; upgrade path: Scriban or Liquid templates if prompts get complex.

#### Step 2.4: AiOrchestrationService.cs

> **Depends on**: Steps 2.2 + 2.3 + Tultul's `RagContextBuilder.cs` + `GeminiClient.cs` from CP1.

This is the central coordinator. It receives a citizen's request and orchestrates the full AI pipeline.

1. Create `Application/Services/AiOrchestrationService.cs`.
2. Main method: `Task<AiResponse> ProcessCaseAsync(Case case, AiRequestType requestType)`.
3. Pipeline:
   ```
   0. CACHE CHECK (eng review round 2): for RightsExplanation requests, query AI_LOG
      for an existing RightsExplanation row for this case. If found, return the stored
      response (with citations from CASE_ACT_REFERENCE) instead of calling Gemini.
      Rationale: regenerating per page view burns free-tier quota, returns
      nondeterministic text inconsistent with the stored citations, and adds LLM
      latency for mobile users. Regenerate ONLY when no log row exists.
   1. Call RagContextBuilder.RetrieveContextAsync(case.Description, topK: 8)
      → Returns list of retrieved ActSection chunks with relevance scores
   2. Call PromptAssembler.AssemblePrompt(case, retrievedSections, requestType)
      → Returns the full prompt string
   3. Start a Stopwatch, call GeminiClient.GenerateContentAsync(prompt)
      → Returns raw AI response; stop the Stopwatch
   4. Call DisclaimerInjector.InjectDisclaimer(response, case.Language)
      → Returns response with disclaimer appended
   5. Call AiLogService.LogAsync(..., latencyMs: stopwatch.ElapsedMilliseconds) to record the call
   6. Save CaseActReference records for each cited section
   7. Return the processed response
   ```

#### Step 2.5: RightsExplanationService.cs

> **Depends on**: Step 2.4 (uses `AiOrchestrationService` internally).

1. Create `Application/Services/RightsExplanationService.cs`.
2. This is a thin facade over `AiOrchestrationService` with `requestType = AiRequestType.RightsExplanation`.
3. Returns a `RightsExplanationDto` containing:
   - The plain-language explanation
   - List of cited Act sections (with Act name, section number, relevance score)
   - The disclaimer
   - ponytail: This could be a method on `AiOrchestrationService` instead of a separate class. Separate class is warranted because the interface `IRightsExplanationService` is used directly by the controller — keeps the controller thin. Ceiling: orchestration wrapper; upgrade path: add caching for identical queries.

#### Step 2.6: AiLogService.cs

> **Depends on**: Tultul's `AiLog` entity and `IAiLogRepository`.

1. Create `Application/Services/AiLogService.cs`.
2. Single method: `Task LogAsync(int? caseId, AiRequestType type, string prompt, string response, string model, int tokensUsed, int latencyMs)`.
3. Creates an `AiLog` entity and saves via `IAiLogRepository`. The `latencyMs` parameter records the round-trip API call duration (FR-12 requirement).
4. This is called by `AiOrchestrationService` after every Gemini call.
   - ponytail: Fire-and-forget is tempting but wrong. If logging fails silently, you lose your audit trail. Log synchronously within the same transaction. If perf becomes an issue, move to a background queue later. Ceiling: sync logging; upgrade path: `Channel<AiLog>` with a background consumer.

#### Step 2.7: EncryptionService.cs

> **Depends on**: Nothing — standalone utility.

1. Create `Infrastructure/Security/EncryptionService.cs` implementing `IEncryptionService`.
2. Two methods: `string Encrypt(string plaintext)` and `string Decrypt(string ciphertext)`.
3. Use `Aes` from `System.Security.Cryptography`. Key stored in `appsettings` (or better, Azure Key Vault in production).
   - **Option A**: AES-256-CBC with a static key from config. Simple, works.
   - **Option B**: ASP.NET Data Protection API (`IDataProtector`). Framework-managed key rotation, more robust.
   - **Recommendation**: Option B — it's built into ASP.NET, handles key management, and is one line to set up:
     ```csharp
     var protector = dataProtectionProvider.CreateProtector("MuktoAin.PII");
     ```
   - ponytail: Don't build a custom encryption layer. ASP.NET Data Protection exists for this. Ceiling: single-purpose protector; upgrade path: per-field protectors with different purposes.

#### Step 2.8: Wire EncryptionService into CaseService

> **Depends on**: Step 2.7 (EncryptionService) + Arpita completing `CaseService.cs` (Step 2.1 of Arpita_plan.md).

The NFR requires field-level encryption for "sensitive citizen PII in cases." The `EncryptionService` exists but nothing calls it yet.

1. Coordinate with Arpita: in `CaseService.SubmitCaseAsync()`, encrypt `Case.Description` and `Case.Title` before saving:
   ```csharp
   caseEntity.Description = _encryptionService.Encrypt(dto.Description);
   caseEntity.Title = _encryptionService.Encrypt(dto.Title);
   ```
2. In `CaseService.GetCaseDetailAsync()`, decrypt before returning:
   ```csharp
   var decryptedTitle = _encryptionService.Decrypt(c.Title);
   var decryptedDesc = _encryptionService.Decrypt(c.Description);
   ```
3. **Encryption scope (eng review round 2)** — encrypting the CASE columns alone does NOT satisfy the PII NFR, because `Case.Description` plaintext flows to three other places:
   - **Embedding call**: decrypt BEFORE `RagContextBuilder`/embedding (Gemini must see plaintext; encryption is at-rest only).
   - **Document templates**: templates render decrypted text — decrypt in the read path before `DocumentGenerator`.
   - **AI_LOG.PromptText**: logging the full prompt persists the citizen's description as plaintext in a second table, defeating field-level encryption. Log a REDACTED prompt instead: replace the problem-description block with `[REDACTED: case description, N chars]`, keep the statutory context + template skeleton for debugging. Full fidelity stays ONLY in the encrypted CASE columns.
4. **Decision**: Shads adds `IEncryptionService` injection to Arpita's `CaseService`, or Arpita adds it when she builds the service. Coordinate — don't duplicate.
   - ponytail: Encrypt on write, decrypt on read. Two calls each. Don't build a transparent encryption layer or EF value converters. Ceiling: manual encrypt/decrypt in CaseService; upgrade path: EF Core value converters for automatic transparent encryption.

---

### Checkpoint 3: QA Evaluation + Deployment

#### Step 3.1: QA Evaluation Harness

> **Depends on**: The full RAG pipeline working end-to-end (Steps 1.3-1.6 + 2.3-2.5 + Tultul's retrieval infrastructure).

1. Download the QA benchmark dataset: `momahadi/bangladesh-legal-qa-dataset` (2,165 questions).
2. Create `tests/MuktoAin.IntegrationTests/AiPipeline/QaBenchmarkTests.cs`.
3. For each question in the dataset:
   ```
   a. Run the question through RagContextBuilder → get retrieved sections
   b. Check: does the retrieved set contain the correct section labeled in the dataset?
      → Retrieval accuracy (precision@k, recall@k)
   c. Run the question through AiOrchestrationService → get generated answer
   d. Compare generated answer to the gold-standard answer in the dataset
      → Answer accuracy (exact match or semantic similarity)
   ```
4. Output: a summary report with precision, recall, and accuracy percentages.
   - **Option A**: Run all 2,165 questions. Thorough, but will take hours with Gemini free tier rate limits.
   - **Option B**: Run a stratified sample (e.g., 200 questions, proportional across the 6 Acts). Faster, statistically meaningful.
   - **Recommendation**: Option B for development iteration, Option A for the final report. Keep both as test configurations.
   - ponytail: The harness is a `for` loop that calls the real pipeline and counts hits/misses. Don't build a test framework. Ceiling: sequential test runner with CSV output; upgrade path: parallel execution with rate limiter.

#### Step 3.2: Few-Shot IRAC Prompt Injection

> **Depends on**: Step 3.1 (you need the baseline zero-shot score first).

1. Select 5-10 high-quality QA examples from the dataset that include IRAC reasoning.
2. Add them to `PromptTemplates.cs` as few-shot examples:
   ```
   Example 1:
   Question: {question}
   Relevant Section: {act}, Section {number}
   Answer: {answer with IRAC reasoning}
   ```
3. Modify `PromptAssembler.cs` to include these examples in the prompt.
4. Re-run the QA harness (Step 3.1) with few-shot enabled.
5. Compare: zero-shot accuracy vs few-shot accuracy → the delta is your result for the academic report.

#### Step 3.3: Dockerfile

> **Depends on**: Nothing — can be done anytime, but best done after the app runs locally.

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/MuktoAin.Web/MuktoAin.Web.csproj", "MuktoAin.Web/"]
COPY ["src/MuktoAin.Application/MuktoAin.Application.csproj", "MuktoAin.Application/"]
COPY ["src/MuktoAin.Infrastructure/MuktoAin.Infrastructure.csproj", "MuktoAin.Infrastructure/"]
COPY ["src/MuktoAin.Domain/MuktoAin.Domain.csproj", "MuktoAin.Domain/"]
RUN dotnet restore "MuktoAin.Web/MuktoAin.Web.csproj"
COPY src/ .
RUN dotnet publish "MuktoAin.Web/MuktoAin.Web.csproj" -c Release -o /app/publish

FROM base AS final
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MuktoAin.Web.dll"]
```

- ponytail: Multi-stage build, nothing fancy. Don't add health checks, optimization layers, or Alpine base images until deployment proves they're needed. Ceiling: basic multi-stage; upgrade path: distroless base, health probes, non-root user.

#### Step 3.4: GitHub Actions CI/CD

> **Depends on**: Step 3.3 (Dockerfile) + the project building successfully.

Create `.github/workflows/ci.yml`:
```yaml
name: CI
on:
  push:
    branches: [main]
  pull_request:
    branches: [main]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: dotnet restore src/MuktoAin.sln
      - run: dotnet build src/MuktoAin.sln --no-restore
  unit-tests:
    runs-on: ubuntu-latest
    needs: build
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      # Unit tests only — no SQL Server / Qdrant / Gemini needed.
      # InMemory-provider tests CANNOT cover FromSqlRaw methods (they throw);
      # those live in the integration job below.
      - run: dotnet test tests/MuktoAin.UnitTests/ --verbosity normal
  integration-tests:
    runs-on: windows-latest   # LocalDB-style SQL available via service container below
    needs: build
    services:
      sqlserver:
        image: mcr.microsoft.com/mssql/server:2022-latest
        env:
          SA_PASSWORD: YourStrong!Passw0rd
          ACCEPT_EULA: Y
        ports:
          - 1433:1433
        options: >-
          --health-cmd "opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P 'YourStrong!Passw0rd' -Q 'SELECT 1' || exit 1"
          --health-interval 10s --health-timeout 5s --health-retries 5
    env:
      ConnectionStrings__DefaultConnection: "Server=localhost,1433;Database=MuktoAin_Test;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True"
      Qdrant__Endpoint: ${{ secrets.QDRANT_ENDPOINT }}
      Qdrant__ApiKey: ${{ secrets.QDRANT_API_KEY }}
      Gemini__ApiKeys__0: ${{ secrets.GEMINI_API_KEY_1 }}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: dotnet test tests/MuktoAin.IntegrationTests/ --verbosity normal
```
- **Note (eng review round 2)**: a bare `ubuntu-latest` + `dotnet test` would be red by construction — no SQL Server on the runner, and the RAG smoke/QA benchmark tests need real Qdrant + Gemini credentials. Integration job runs only when repo secrets are configured; until then it stays as the documented shape. FTS features need the container image WITH full-text (`mcr.microsoft.com/mssql/server` does NOT include FTS — for the CI FTS test, use the `mssql-full` community image or mark those tests `[Trait("Category","RequiresFts")]` to skip in CI).

- **Option A**: CI only (build + test on PR). Deploy manually.
- **Option B**: CI + CD to Azure App Service (auto-deploy on merge to main).
- **Recommendation**: Option A for now. Add CD in the final week if Azure credits are set up.

#### Step 3.5: docs/deployment-guide.md

> **Depends on**: Steps 3.3 + 3.4.

Write a deployment guide covering:
1. Prerequisites (Docker, .NET 8 SDK, SQL Server, Qdrant)
2. Local development setup (clone, restore, migrate, seed, run)
3. Docker build and run
4. Azure deployment (App Service + Azure SQL + Qdrant Cloud)
5. Environment variables / secrets configuration

#### Step 3.6: RequestLocalizationMiddleware Configuration

> **Depends on**: Erin completing `.resx` files (Step 2.8 of Erin_plan.md) + Identity (Step 1.1).

Erin creates the `.resx` resource files and expects Shads to wire the middleware. This is the server-side half of the localization story.

1. In `Program.cs`, add localization services:
   ```csharp
   builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
   builder.Services.AddControllersWithViews()
       .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
       .AddDataAnnotationsLocalization();
   ```
2. Configure supported cultures:
   ```csharp
   var supportedCultures = new[] { "bn", "en" };
   app.UseRequestLocalization(options =>
   {
       options.SetDefaultCulture("bn")
           .AddSupportedCultures(supportedCultures)
           .AddSupportedUICultures(supportedCultures);
   });
   ```
3. The language toggle (Erin's `_LanguageToggle.cshtml`) will set a cookie or query parameter that the middleware reads. Use `CookieRequestCultureProvider` — it persists the user's choice across page loads.
   - ponytail: Three method calls in Program.cs. Don't build a custom culture provider. Ceiling: cookie-based culture switching; upgrade path: store preference in `User.PreferredLanguage` and set on login.

#### Step 3.7: TLS 1.2+ and Secure Cookie Configuration

> **Depends on**: Step 1.1 (Identity/Cookie auth must be configured first).

The NFR requires enforced TLS 1.2+ and secure HTTP-only cookies.

1. In `Program.cs`, configure cookie security:
   ```csharp
   builder.Services.ConfigureApplicationCookie(options =>
   {
       options.Cookie.HttpOnly = true;
       options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
       options.Cookie.SameSite = SameSiteMode.Strict;
       options.ExpireTimeSpan = TimeSpan.FromHours(8);
       options.SlidingExpiration = true;
   });
   ```
2. Enforce HTTPS redirection and HSTS:
   ```csharp
   app.UseHttpsRedirection();
   app.UseHsts();
   ```
3. In production, configure Kestrel to require TLS 1.2:
   ```csharp
   builder.WebHost.ConfigureKestrel(options =>
   {
       options.ConfigureHttpsDefaults(h =>
           h.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                            System.Security.Authentication.SslProtocols.Tls13);
   });
   ```
   - ponytail: Standard ASP.NET security hardening. Ship with HTTPS redirect + secure cookies. Kestrel TLS config is production-only. Ceiling: framework defaults; upgrade path: N/A — this is table stakes.

#### Step 3.8: UserManagementService.cs (FR-18)

> **Depends on**: Step 1.1 (Identity — needs `UserManager<User>`).

The admin management console (FR-18) includes user management with account suspension. Erin builds the view (`Views/Admin/Users.cshtml`), but no service logic exists.

1. Create `Application/Services/UserManagementService.cs`.
2. Core methods:
   ```csharp
   public class UserManagementService
   {
       private readonly UserManager<User> _userManager;

       // Get all users (for admin list view)
       public async Task<IEnumerable<UserListDto>> GetAllUsersAsync()
       {
           var users = _userManager.Users.ToList();
           return users.Select(u => new UserListDto(
               u.Id, u.FullName, u.Email!, u.Role.ToString(),
               u.AccountStatus.ToString()));
       }

       // Suspend or activate a user account
       public async Task<bool> SetAccountStatusAsync(int userId, AccountStatus status, int adminUserId)
       {
           var user = await _userManager.FindByIdAsync(userId.ToString());
           if (user == null) return false;
           if (user.Role == UserRole.Admin) return false; // can't suspend admins

           user.AccountStatus = status;
           await _userManager.UpdateAsync(user);
           return true;
       }
   }
   ```
3. Add `UserListDto` to Arpita's DTOs (or define inline):
   ```csharp
   public record UserListDto(int UserId, string FullName, string Email, string Role, string Status);
   ```
   - ponytail: Two methods wrapping UserManager. Don't build a full user CRUD service — Identity handles creation via registration. This just covers the admin-list + suspend/activate gap. Ceiling: list + status toggle; upgrade path: add role reassignment, password reset.

#### Step 3.9: README.md

> **Depends on**: Steps 3.3 + 3.5 (Dockerfile + deployment guide should exist first).

As project lead, write the root `README.md` covering:
1. Project name, mission statement, and team members
2. Technology stack summary (link to AGENTS.md §2)
3. Quick start instructions (clone, restore, migrate, seed, run)
4. Architecture overview (link to `docs/architecture.md`)
5. Dataset attribution (link to `docs/attribution-CC-BY-SA-4.0.md`)
6. Legal disclaimer notice

---

## Part 3: Dependency Map

Every task Shads can't start until a specific teammate delivers something:

| Shads's Task | Blocked By | Teammate | Their Specific Task |
|---|---|---|---|
| **1.1** Identity Configuration | `User.cs` entity + `AppDbContext.cs` + manual SSMS SQL scripts | **Tultul** | All 14 entities, AppDbContext mapping, manual SQL schema in SSMS |
| **1.5** EmbeddingBatchJob | `ActSectionChunk` rows populated in SQL Server | **Tultul** | Batch import pipeline + sub-chunking logic |
| **1.5** EmbeddingBatchJob | `QdrantVectorStore.cs` (upsert method) | **Tultul** | QdrantVectorStore implementation |
| **1.6** Integration Test | `SimilaritySearchService.cs` (search method) | **Tultul** | SimilaritySearchService implementation |
| **2.3** PromptAssembler | `RagContextBuilder.cs` (provides retrieved sections as input) | **Tultul** | RagContextBuilder with vector-primary + FTS fallback |
| **2.4** AiOrchestrationService | `RagContextBuilder.cs` | **Tultul** | Same as above |
| **2.6** AiLogService | `AiLog` entity + `IAiLogRepository` | **Tultul** | Entity + interface (part of the 14-entity batch) |
| **3.1** QA Evaluation Harness | Full RAG pipeline working + at least 1 document template | **Tultul** (retrieval) + **Arpita** (document generator) | RagContextBuilder + DocumentGenerator working |

### What Shads Can Start Immediately (No Dependencies)

These tasks have zero blockers — start them on day 1:

1. ✅ Repository setup (Part 1, all steps)
2. ✅ `GeminiClient.cs` (only needs the API key)
3. ✅ `GeminiEmbeddingService.cs` (wraps GeminiClient)
4. ✅ `Constants/Disclaimers.cs`
5. ✅ `Constants/PromptTemplates.cs`
6. ✅ `DisclaimerInjector.cs` (Application layer, depends only on Disclaimers.cs)
7. ✅ `PromptAssembler.cs` (Application layer, can write the template-substitution logic immediately)
8. ✅ `EncryptionService.cs` (standalone)
9. ✅ `Dockerfile` (can write the structure even before the app compiles)
10. ✅ `appsettings.Development.json.template`

### Parallel Work Strategy

While waiting for Tultul's entities + SSMS SQL schema (the critical path):
1. Build `GeminiClient` + `GeminiEmbeddingService` + write manual tests against the real API
2. Write all constants (`PromptTemplates`, `Disclaimers`)
3. Write `DisclaimerInjector` + `EncryptionService`
4. Draft the `Dockerfile`
5. Set up Google AI Studio, Qdrant, share keys with team

Once Tultul's PR lands → execute the SQL scripts in SSMS, wire Identity, start the embedding batch job, build the integration test.
