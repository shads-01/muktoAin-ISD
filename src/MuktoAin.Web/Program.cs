using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MuktoAin.Domain.Entities;
using MuktoAin.Infrastructure.Data;
using MuktoAin.Infrastructure.Data.Seeding;
using MuktoAin.Web.Auth;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Schema is authored and controlled directly in SSMS via scripts/*.sql (T-1.6) --
// this context only maps onto that predefined schema. No EF migrations by design.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// S-1.1: ASP.NET Core Identity against the manually-authored [dbo].[USER] table.
// Role tables do not exist in the SSMS schema by design -- authorization runs off
// the User.Role enum via UserRoleClaimsTransformation (see Auth/ folder).
builder.Services.AddIdentityCore<User>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = true;

    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddSignInManager<SignInManager<User>>()
.AddDefaultTokenProviders();

// AddIdentityCore does NOT wire cookie authentication -- done explicitly here.
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "MuktoAin.Auth";
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Home/Forbidden";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.AddTransient<Microsoft.AspNetCore.Authentication.IClaimsTransformation, UserRoleClaimsTransformation>();

// Shads will add: GeminiClient, AI services (S-1.x / S-2.x)
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

// S-1.1: authentication must run before authorization.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
