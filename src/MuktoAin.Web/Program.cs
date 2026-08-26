using Microsoft.EntityFrameworkCore;
using MuktoAin.Application.Services;
using MuktoAin.Domain.Interfaces.Repositories;
using MuktoAin.Infrastructure.Data;
using MuktoAin.Infrastructure.Data.Seeding;
using MuktoAin.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Schema is authored and controlled directly in SSMS via scripts/*.sql (T-1.6) --
// this context only maps onto that predefined schema. No EF migrations by design.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddScoped<ICaseRepository, CaseRepository>();
builder.Services.AddScoped<CaseService>();

// Shads will add: Identity, GeminiClient, AI services (S-1.x)
// Tultul will add: repositories, IVectorStore/QdrantVectorStore (T-1.11 to T-1.13)
// Arpita will add: DocumentService, ReviewService, etc.

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/ServerError");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseStatusCodePagesWithReExecute("/Home/Error", "?statusCode={0}");

app.UseSession();

// Do NOT call Database.MigrateAsync() -- see the "No EF migrations" note above.
// Seeders assume the SSMS scripts have already been executed.
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await SeedDistricts.SeedAsync(context, app.Environment.ContentRootPath);
    await SeedCategories.SeedAsync(context, app.Environment.ContentRootPath);
    await ActImportService.SeedAsync(context, app.Environment.ContentRootPath, logger);
    await LegalChunkingService.ChunkAsync(context, logger);
    await SeedScenarioMappings.SeedAsync(context, app.Environment.ContentRootPath, logger);
    // TODO: [Shads] SeedAdminUser will be added here.
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
