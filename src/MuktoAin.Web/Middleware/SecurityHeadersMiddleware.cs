namespace MuktoAin.Web.Middleware;

// AUD-6: clickjacking / MIME-sniffing / referrer hardening on EVERY response
// (via Response.OnStarting, so error pages and static files are covered too).
// The CSP allowlist mirrors the app's actual third-party surface:
//   - scripts: wwwroot/lib + https://unpkg.com (lucide icons) +
//     https://cdn.jsdelivr.net (marked + dompurify, Home/Index.cshtml) +
//     inline theme init scripts (hence 'unsafe-inline' on script-src)
//   - styles: wwwroot/assets/css + inline style="" attributes + Google Fonts CSS
//   - fonts: Google Fonts (fonts.gstatic.com)
public class SecurityHeadersMiddleware
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://unpkg.com https://cdn.jsdelivr.net; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data:; " +
        "connect-src 'self'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Content-Security-Policy"] = ContentSecurityPolicy;
            return Task.CompletedTask;
        });

        await _next(context);
    }
}
