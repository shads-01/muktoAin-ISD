using System.Net;
using MuktoAin.IntegrationTests.Helpers;

namespace MuktoAin.IntegrationTests.Api;

public class WebHostSmokeTests : IClassFixture<MuktoAinWebApplicationFactory>
{
    private readonly MuktoAinWebApplicationFactory _factory;

    public WebHostSmokeTests(MuktoAinWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Get_Home_Index_Returns_Success()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/");
        Assert.True(response.IsSuccessStatusCode,
            $"Expected success from GET / but got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task Get_UnknownRoute_Returns_404_Via_StatusCodePages()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/DefinitelyNotARealRoute");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Home_NotFound_Returns_404()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/Home/NotFound");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Home_ServerError_Returns_500()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/Home/ServerError");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Get_Home_AccessDenied_Returns_403()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/Home/AccessDenied");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Error_Pages_Render_Bilingual_Copy()
    {
        var client = _factory.CreateClient();
        var notFound = await client.GetAsync("/Home/NotFound");
        var html = await notFound.Content.ReadAsStringAsync();
        Assert.Contains("data-bn", html);
        Assert.Contains("data-en", html);
    }
}
