using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data;

// Schema is authored and controlled directly in SSMS via scripts/*.sql (Step 1.6) --
// this context maps entity properties onto that predefined schema. No EF migrations
// are used by design. Table name mappings (e.g. ToTable("USER", "dbo") for reserved
// words) and column-level constraints (max lengths, precision, uniqueness) land in
// Step 1.6 alongside the SQL DDL, via IEntityTypeConfiguration<T> classes applied
// below, so the two stay in lockstep.
//
// S-1.1: inherits IdentityDbContext so ASP.NET Core Identity's store entities
// (claims, logins, tokens) are part of the model for UserStore to function.
// IMPORTANT: only [dbo].[USER] physically exists -- scripts/02_schema.sql
// deliberately has no role/claim/login tables. Role authorization runs off the
// User.Role enum column (see UserRoleClaimsTransformation in MuktoAin.Web), never
// against IdentityRole tables. Nothing must query those entity types at runtime.
public class AppDbContext : IdentityDbContext<User, IdentityRole<int>, int>
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
