using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MuktoAin.Domain.Entities;
using MuktoAin.Domain.Enums;
using MuktoAin.Infrastructure.Data;
using Xunit.Sdk;

namespace MuktoAin.IntegrationTests.Support;

// Owns the dedicated MuktoAin_IntegrationTest database for the whole test run.
// When no SQL Server instance is reachable, DatabaseAvailable stays false and
// every test Skips itself — CI runners without a database still pass. When a
// server IS reachable, the database is dropped and recreated and the
// hand-authored schema scripts are replayed from scripts/, then shared seed
// helpers (FK parents, acts/sections/chunks) are offered to the test classes.
public class SqlDatabaseFixture : IAsyncLifetime
{
    public const string DatabaseName = "MuktoAin_IntegrationTest";

    // 02 first: the full 14-table schema, already containing the column
    // migrations folded into it by scripts 04-06/10. 08/09_* are additive,
    // idempotent redesign scripts. 03 (full-text) is attempted separately
    // because SQL Express without Advanced Services lacks the feature.
    private static readonly string[] MandatoryScripts =
    {
        "02_schema.sql",
        "08_redesign_tables.sql",
        "09_part_b_tables.sql",
        "09_fix_chat_session_sessionkey_unique_index.sql",
        "11_notifications_table.sql",
        "12_add_user_issuperadmin.sql",
        "13_chat_conversational_columns.sql",
        "14_add_admin_audit_log.sql",
        "14_chat_sidebar_history.sql",
        "15_chat_blocked_streak.sql",
        "16_notification_is_seen.sql",
        "17_add_rowversion_columns.sql",
        "18_payment_gateway.sql",
        "19_payment_gateway_routing.sql",
        "20_ai_chat_credits.sql",
    };
    private const string FullTextScript = "03_fulltext.sql";

    private int? _districtId;
    private int? _categoryId;
    private int? _userId;

    public bool DatabaseAvailable { get; private set; }
    public bool FullTextAvailable { get; private set; }
    public string DbConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        var master = await ProbeAsync();
        if (master is null)
        {
            return; // DatabaseAvailable stays false -> all tests skip
        }
        DatabaseAvailable = true;

        var builder = new SqlConnectionStringBuilder(master) { InitialCatalog = DatabaseName };
        DbConnectionString = builder.ConnectionString;

        await RecreateDatabaseAsync(master);

        var scriptsRoot = FindScriptsRoot();
        foreach (var script in MandatoryScripts)
        {
            var text = File.ReadAllText(Path.Combine(scriptsRoot, script));
            await ExecuteBatchesAsync(SqlScriptRunner.SplitIntoBatches(text));
        }

        FullTextAvailable = await TryInstallFullTextAsync(scriptsRoot);
    }

    public Task DisposeAsync() => Task.CompletedTask; // keep DB for post-run inspection

    // ---- connectivity -------------------------------------------------------

    private static async Task<string?> ProbeAsync()
    {
        foreach (var candidate in CandidateConnectionStrings())
        {
            try
            {
                await using var conn = new SqlConnection(candidate);
                await conn.OpenAsync();
                return candidate;
            }
            catch (SqlException)
            {
                // try the next candidate; if all fail the run reports unavailable
            }
        }
        return null;
    }

    private static IEnumerable<string> CandidateConnectionStrings()
    {
        var env = Environment.GetEnvironmentVariable("MUKTOAIN_TEST_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(env))
        {
            yield return env;
            yield break;
        }
        // Same default instance the app itself uses (src/MuktoAin.Web/appsettings.json),
        // then the SQL Express instance (src/MuktoAin.Web/appsettings.Development.json.template).
        yield return "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5";
        yield return "Server=.\\SQLEXPRESS;Database=master;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5";
    }

    private static async Task ExecuteAsync(string connectionString, string batch)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 120;
        cmd.CommandText = batch;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task RecreateDatabaseAsync(string masterConnectionString)
    {
        await ExecuteAsync(masterConnectionString, $@"
IF DB_ID(N'{DatabaseName}') IS NOT NULL
BEGIN
    ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [{DatabaseName}];
END
CREATE DATABASE [{DatabaseName}];");
        // Mirror scripts/01_init_database.sql's isolation settings (fresh DB,
        // so the ROLLBACK IMMEDIATE clauses are no-ops but harmless).
        await ExecuteAsync(masterConnectionString, $@"
ALTER DATABASE [{DatabaseName}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
ALTER DATABASE [{DatabaseName}] SET ALLOW_SNAPSHOT_ISOLATION ON;
ALTER DATABASE [{DatabaseName}] SET AUTO_CLOSE OFF;");
    }

    private async Task<bool> TryInstallFullTextAsync(string scriptsRoot)
    {
        try
        {
            var text = File.ReadAllText(Path.Combine(scriptsRoot, FullTextScript));
            await ExecuteBatchesAsync(SqlScriptRunner.SplitIntoBatches(text));
            return true;
        }
        catch (SqlException)
        {
            // Instance without "Full-Text and Semantic Extractions for Search"
            // (plain SQL Express). CONTAINSTABLE tests skip on FullTextAvailable.
            return false;
        }
    }

    // ---- contexts & raw execution ------------------------------------------

    public AppDbContext CreateContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(DbConnectionString)
            .Options);

    public async Task ExecuteBatchesAsync(IEnumerable<string> batches)
    {
        await using var conn = new SqlConnection(DbConnectionString);
        await conn.OpenAsync();
        foreach (var batch in batches)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 120;
            cmd.CommandText = batch;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task<object?> ScalarAsync(string sql)
    {
        await using var conn = new SqlConnection(DbConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 120;
        cmd.CommandText = sql;
        return await cmd.ExecuteScalarAsync();
    }

    public static string FindScriptsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "scripts", "02_schema.sql")))
        {
            dir = dir.Parent;
        }
        return dir is null
            ? throw new InvalidOperationException(
                $"Could not locate scripts/02_schema.sql walking up from {AppContext.BaseDirectory}")
            : Path.Combine(dir.FullName, "scripts");
    }

    // ---- shared seed helpers ------------------------------------------------

    public async Task<(int ActId, int SectionId)> SeedActWithSectionAsync(
        string sectionText, string? sectionNumber = null)
    {
        await using var ctx = CreateContext();
        var marker = Guid.NewGuid().ToString("N")[..12];
        var act = new Act
        {
            Title = "Test Act " + marker,
            ActNumber = "T-" + marker,
            Year = 2026,
            PublicationDate = "12 September 2026",
            Language = "en",
            SourceUrl = "https://bdlaws.test/" + marker,
        };
        var section = new ActSection
        {
            OrdinalPosition = 1,
            SectionNumber = sectionNumber ?? "1",
            SectionText = sectionText,
        };
        act.Sections.Add(section);
        ctx.Acts.Add(act);
        await ctx.SaveChangesAsync();
        return (act.ActId, section.SectionId);
    }

    public async Task<int> SeedChunkAsync(
        int sectionId, string chunkText, string? vectorId = null, string? contentHash = null)
    {
        await using var ctx = CreateContext();
        var chunk = new ActSectionChunk
        {
            SectionId = sectionId,
            ChunkOrder = 1,
            ChunkText = chunkText,
            TokenCount = chunkText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
            VectorId = vectorId,
            ContentHash = contentHash,
        };
        ctx.ActSectionChunks.Add(chunk);
        await ctx.SaveChangesAsync();
        return chunk.ChunkId;
    }

    public async Task<int> GetDistrictIdAsync()
    {
        if (_districtId.HasValue)
        {
            return _districtId.Value;
        }
        await using var ctx = CreateContext();
        var district = new District { DistrictId = 250, Name = "IntegrationTest District" };
        ctx.Districts.Add(district);
        await ctx.SaveChangesAsync();
        _districtId = district.DistrictId;
        return _districtId.Value;
    }

    public async Task<int> GetCategoryIdAsync()
    {
        if (_categoryId.HasValue)
        {
            return _categoryId.Value;
        }
        await using var ctx = CreateContext();
        var category = new CaseCategory
        {
            Name = "IntegrationTest Category",
            Description = "Seeded by T-3.3 integration tests",
        };
        ctx.CaseCategories.Add(category);
        await ctx.SaveChangesAsync();
        _categoryId = category.CategoryId;
        return _categoryId.Value;
    }

    public async Task<int> GetUserIdAsync()
    {
        if (_userId.HasValue)
        {
            return _userId.Value;
        }
        await using var ctx = CreateContext();
        var user = new User
        {
            FullName = "Integration Test Admin",
            Email = $"it-admin-{Guid.NewGuid():N}@test.local",
            Role = UserRole.Admin,
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        _userId = user.Id;
        return _userId.Value;
    }

    // ---- full-text readiness ------------------------------------------------

    // CONTAINSTABLE only sees rows after the catalog crawls them (change
    // tracking AUTO picks new rows up asynchronously). Kick a full population
    // (ignore the "already in progress" error) and poll ItemCount until it
    // covers every ACT_SECTION row, so queries are deterministic.
    public async Task WaitForFullTextIndexAsync(int timeoutSeconds = 60)
    {
        try
        {
            await ExecuteAsync(
                DbConnectionString,
                "ALTER FULLTEXT INDEX ON [dbo].[ACT_SECTION] START FULL POPULATION;");
        }
        catch (SqlException)
        {
            // Population may already be running — the polling loop handles it.
        }

        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var itemCount = Convert.ToInt32(await ScalarAsync(
                "SELECT ISNULL(FULLTEXTCATALOGPROPERTY('MuktoAinCatalog', 'ItemCount'), 0);"));
            var expected = Convert.ToInt32(await ScalarAsync(
                "SELECT COUNT(*) FROM [dbo].[ACT_SECTION];"));
            if (itemCount >= expected)
            {
                return;
            }
            await Task.Delay(500);
        }
        throw new XunitException(
            $"Full-text index did not finish population within {timeoutSeconds}s " +
            $"(FULLTEXTCATALOGPROPERTY('MuktoAinCatalog', 'ItemCount') never reached " +
            $"the ACT_SECTION row count).");
    }
}
