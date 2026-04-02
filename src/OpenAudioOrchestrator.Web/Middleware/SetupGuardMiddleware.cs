using OpenAudioOrchestrator.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace OpenAudioOrchestrator.Web.Middleware;

public class SetupGuardMiddleware
{
    private readonly RequestDelegate _next;
    // BUG-01: Use an int with Interlocked so the false→true transition is atomic.
    // 0 = setup not complete, 1 = setup complete.
    private int _setupComplete;

    public SetupGuardMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var isSetupPath = context.Request.Path.StartsWithSegments("/setup");
        var isStaticResource = context.Request.Path.StartsWithSegments("/_framework")
            || context.Request.Path.StartsWithSegments("/_blazor")
            || context.Request.Path.StartsWithSegments("/_content")
            || context.Request.Path.StartsWithSegments("/css")
            || context.Request.Path.Value?.Contains('.') == true;

        if (isStaticResource)
        {
            await _next(context);
            return;
        }

        // Once users exist, cache the result — users don't un-exist.
        // Interlocked.Exchange ensures the false→true write is atomic and
        // visible to all threads without a torn read.
        if (Volatile.Read(ref _setupComplete) == 0)
        {
            var db = context.RequestServices.GetRequiredService<AppDbContext>();
            if (await db.Users.AnyAsync())
                Interlocked.Exchange(ref _setupComplete, 1);
        }

        if (isSetupPath)
        {
            // Block setup page once users exist — prevents creating rogue admins
            if (Volatile.Read(ref _setupComplete) == 1)
            {
                context.Response.Redirect("/");
                return;
            }
            await _next(context);
            return;
        }

        if (Volatile.Read(ref _setupComplete) == 0)
        {
            context.Response.Redirect("/setup");
            return;
        }

        await _next(context);
    }
}
