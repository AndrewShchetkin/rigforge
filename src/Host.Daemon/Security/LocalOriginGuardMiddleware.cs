using Microsoft.Extensions.Options;

namespace RigForge.Host.Daemon.Security;

/// <summary>
/// Rejects browser requests from foreign origins. WebSocket upgrades are not covered by CORS, so without this
/// check any web page could open the SignalR hub on 127.0.0.1 and change fan curves ("localhost CSRF").
/// Requests without an Origin header (CLI tools, Postman, native clients) are not affected.
/// </summary>
internal sealed partial class LocalOriginGuardMiddleware(RequestDelegate next, IOptions<DaemonOptions> options, ILogger<LocalOriginGuardMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && !IsAllowed(origin, context.Request))
        {
            LogRejected(origin, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(context);
    }

    private bool IsAllowed(string origin, HttpRequest request)
    {
        if (options.Value.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected request from foreign origin {Origin} to {Path}")]
    private partial void LogRejected(string origin, PathString path);
}
