namespace OpenAudioOrchestrator.Web.Middleware;

/// <summary>
/// Serves HTTP-01 ACME challenge responses for Let's Encrypt domain validation.
/// Intercepts requests to /.well-known/acme-challenge/{token} and returns the
/// key authorization string from the AcmeCertificateService.
/// </summary>
public class AcmeChallengeMiddleware
{
    private readonly RequestDelegate _next;

    public AcmeChallengeMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/.well-known/acme-challenge", out var remaining)
            && remaining.HasValue
            && remaining.Value.Length > 1)
        {
            var token = remaining.Value.TrimStart('/');
            var acmeService = context.RequestServices.GetService<Services.AcmeCertificateService>();
            var response = acmeService?.GetChallengeResponse(token);

            if (response is not null)
            {
                context.Response.ContentType = "application/octet-stream";
                await context.Response.WriteAsync(response);
                return;
            }

            context.Response.StatusCode = 404;
            return;
        }

        await _next(context);
    }
}
