using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Playwright;
using MuktoAin.Infrastructure.Data;
using MuktoAin.IntegrationTests.Helpers;

namespace MuktoAin.IntegrationTests.Browser;

// Runs the app on real Kestrel (a browser can't reach WebApplicationFactory's
// in-memory TestServer) with the same test wiring as the HTTP tests: EF
// InMemory, stub AI services, and TestAuthHandler, so a browser signs in by
// sending the X-Test-UserId / X-Test-Role headers. Also starts one headless
// Chromium for the whole class.
//
// When Chromium is not installed the tests skip instead of failing. Install it
// once after building:
//   pwsh tests/MuktoAin.IntegrationTests/bin/Debug/net8.0/playwright.ps1 install chromium
public class BrowserAppFixture : IAsyncLifetime
{
    public KestrelAppFactory App { get; } = new();
    public IBrowser? Browser { get; private set; }
    public string? SkipReason { get; private set; }
    private IPlaywright? _playwright;

    public async Task InitializeAsync()
    {
        _ = App.Services; // builds and starts both hosts

        try
        {
            _playwright = await Playwright.CreateAsync();
            Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }
        catch (PlaywrightException ex)
        {
            SkipReason = "Playwright Chromium is not available (run playwright.ps1 install chromium): "
                         + ex.Message.Split('\n')[0];
        }
    }

    public async Task DisposeAsync()
    {
        if (Browser != null) await Browser.DisposeAsync();
        _playwright?.Dispose();
        await App.DisposeAsync();
    }
}

public class KestrelAppFactory : MuktoAinWebApplicationFactory
{
    private IHost? _kestrelHost;

    public string BaseAddress { get; private set; } = "";

    // Services of the Kestrel host, the one the browser talks to.
    public IServiceProvider AppServices => _kestrelHost!.Services;

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // WebApplicationFactory needs its TestServer host; the browser needs a
        // real socket. Build both from the same configured builder.
        var testHost = builder.Build();

        builder.ConfigureWebHost(web => web.UseKestrel().UseUrls("http://127.0.0.1:0"));
        // Own InMemory database: startup seeding would otherwise run twice
        // against the one the TestServer host already seeded.
        var dbName = $"MuktoAin-Browser-{Guid.NewGuid():N}";
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        });
        _kestrelHost = builder.Build();
        _kestrelHost.Start();
        BaseAddress = _kestrelHost.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();

        testHost.Start();
        return testHost;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _kestrelHost?.Dispose();
        base.Dispose(disposing);
    }
}
