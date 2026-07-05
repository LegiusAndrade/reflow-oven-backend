using Microsoft.AspNetCore.SignalR;
using ReflowOven.Api.Auth;

namespace ReflowOven.Api.Realtime;

/// <summary>
/// Re-checks JWT revocation on the SignalR seam (D3). The bearer <c>OnTokenValidated</c> event only runs
/// once, at the handshake — so without this filter a user revoked AFTER connecting would keep a live
/// stream. This rejects a revoked token both when a NEW connection is established and on EVERY subsequent
/// hub method invocation (e.g. <c>SubscribeRun</c>).
///
/// Residual (documented, not yet closed): an already-established server→client PUSH stream (telemetry /
/// diagnostics) keeps flowing until the client next invokes a hub method, the token expires, or the
/// connection drops — SignalR has no built-in mid-stream re-auth. These hubs are read-only, so the residual
/// is a bounded confidentiality window, never a control path. Forcing an immediate disconnect of a live
/// push stream (tracking connectionIds per user and aborting them on revoke) is possible future work.
/// </summary>
public sealed class RevocationHubFilter(ITokenRevocationList revocations) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext ctx, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (TokenRevocationCheck.IsRevoked(ctx.Context.User, revocations))
            throw new HubException("Sessão revogada: o usuário foi alterado ou removido após a emissão do token.");
        return await next(ctx);
    }

    public async Task OnConnectedAsync(HubLifetimeContext ctx, Func<HubLifetimeContext, Task> next)
    {
        if (TokenRevocationCheck.IsRevoked(ctx.Context.User, revocations))
            throw new HubException("Sessão revogada: o usuário foi alterado ou removido após a emissão do token.");
        await next(ctx);
    }
}
