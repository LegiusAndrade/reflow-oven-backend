using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ReflowOven.Api.Auth;
using ReflowOven.Api.Realtime;
using ReflowOven.Application.Abstractions;
using ReflowOven.Domain.Abstractions;
using ReflowOven.Domain.Entities;
using ReflowOven.Domain.Enums;
using ReflowOven.Infrastructure.Auth;
using ReflowOven.Infrastructure.Persistence;
using Xunit;

namespace ReflowOven.Tests;

/// <summary>
/// D6: exercises the revocation PIPELINE seam, not just the list. A JWT is minted by the real
/// <see cref="JwtTokenService"/> and validated exactly as <c>Program.cs</c> does — MapInboundClaims=false,
/// so the raw <c>sub</c>/<c>iat</c> claims survive — then run through <see cref="TokenRevocationCheck"/>
/// (the shared decision the bearer <c>OnTokenValidated</c> event and the SignalR filter both call) and
/// through <see cref="RevocationHubFilter"/> (the SignalR handshake + invocation guard).
/// </summary>
public sealed class RevocationSeamTests
{
    [Fact]
    public async Task Real_validated_jwt_is_rejected_when_revoked_and_a_fresh_one_is_accepted()
    {
        var opts = Options.Create(new JwtOptions());
        var clock = new TestClock(new DateTimeOffset(2026, 7, 3, 12, 0, 0, TimeSpan.Zero));
        var jwt = new JwtTokenService(opts, clock);
        var user = new User { Id = Guid.NewGuid(), Name = "operador", Type = UserType.Regular };

        // A real token minted, then validated back into a principal (raw sub/iat, MapInboundClaims=false).
        var oldPrincipal = Validate(jwt.CreateForUser(user).Token, opts.Value);

        await using var sp = BuildProvider();
        var revocations = NewList(sp, clock);

        Assert.False(TokenRevocationCheck.IsRevoked(oldPrincipal, revocations)); // nothing revoked yet

        clock.Advance(TimeSpan.FromSeconds(5));
        await revocations.RevokeAsync(user.Id);
        Assert.True(TokenRevocationCheck.IsRevoked(oldPrincipal, revocations)); // the real old token is now rejected

        var freshPrincipal = Validate(jwt.CreateForUser(user).Token, opts.Value);
        Assert.False(TokenRevocationCheck.IsRevoked(freshPrincipal, revocations)); // a re-login passes the seam
    }

    [Fact]
    public async Task Calibration_session_token_is_never_revoked_even_by_RevokeAll()
    {
        // The config-backed technician session has sub="calibration" (not a Guid), so a global revoke
        // (factory reset) must never collaterally kill it — the seam simply ignores a non-Guid subject.
        var opts = Options.Create(new JwtOptions());
        var clock = new TestClock(new DateTimeOffset(2026, 7, 3, 12, 0, 0, TimeSpan.Zero));
        var jwt = new JwtTokenService(opts, clock);
        var principal = Validate(jwt.CreateForCalibration().Token, opts.Value);

        await using var sp = BuildProvider();
        var revocations = NewList(sp, clock);
        clock.Advance(TimeSpan.FromSeconds(5));
        await revocations.RevokeAllAsync();

        Assert.False(TokenRevocationCheck.IsRevoked(principal, revocations));
    }

    [Fact]
    public async Task HubFilter_refuses_a_revoked_handshake_and_allows_a_fresh_one()
    {
        var opts = Options.Create(new JwtOptions());
        var clock = new TestClock(new DateTimeOffset(2026, 7, 3, 12, 0, 0, TimeSpan.Zero));
        var jwt = new JwtTokenService(opts, clock);
        var user = new User { Id = Guid.NewGuid(), Name = "operador", Type = UserType.Regular };
        var principal = Validate(jwt.CreateForUser(user).Token, opts.Value);

        await using var sp = BuildProvider();
        var revocations = NewList(sp, clock);
        var filter = new RevocationHubFilter(revocations);
        var hub = new TestHub();
        await using var empty = new ServiceCollection().BuildServiceProvider();

        // Fresh handshake: OnConnectedAsync calls next().
        var called = false;
        await filter.OnConnectedAsync(new HubLifetimeContext(new FakeCallerContext(principal), empty, hub),
            _ => { called = true; return Task.CompletedTask; });
        Assert.True(called);

        // Revoked handshake: OnConnectedAsync throws before next() runs.
        clock.Advance(TimeSpan.FromSeconds(5));
        await revocations.RevokeAsync(user.Id);
        await Assert.ThrowsAsync<HubException>(() =>
            filter.OnConnectedAsync(new HubLifetimeContext(new FakeCallerContext(principal), empty, hub),
                _ => Task.CompletedTask));
    }

    [Fact]
    public async Task HubFilter_rejects_a_revoked_method_invocation()
    {
        var opts = Options.Create(new JwtOptions());
        var clock = new TestClock(new DateTimeOffset(2026, 7, 3, 12, 0, 0, TimeSpan.Zero));
        var jwt = new JwtTokenService(opts, clock);
        var user = new User { Id = Guid.NewGuid(), Name = "operador", Type = UserType.Regular };
        var principal = Validate(jwt.CreateForUser(user).Token, opts.Value);

        await using var sp = BuildProvider();
        var revocations = NewList(sp, clock);
        var filter = new RevocationHubFilter(revocations);
        var hub = new TestHub();
        await using var empty = new ServiceCollection().BuildServiceProvider();
        var method = typeof(TestHub).GetMethod(nameof(TestHub.Ping))!;
        var inv = new HubInvocationContext(new FakeCallerContext(principal), empty, hub, method, []);

        clock.Advance(TimeSpan.FromSeconds(5));
        await revocations.RevokeAsync(user.Id);

        await Assert.ThrowsAsync<HubException>(() =>
            filter.InvokeMethodAsync(inv, _ => ValueTask.FromResult<object?>(null)).AsTask());
    }

    // ---- helpers ---------------------------------------------------------

    /// <summary>Validate a token exactly as Program.cs's JwtBearer does — MapInboundClaims=false so the raw
    /// sub/iat claims survive. Lifetime validation is off here (it is orthogonal to revocation and would tie
    /// the test to the real wall clock); the signature/issuer/audience are still enforced, so the token is genuine.</summary>
    private static ClaimsPrincipal Validate(string token, JwtOptions o)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var parms = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = o.Issuer,
            ValidateAudience = true,
            ValidAudience = o.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.SigningKey)),
            ValidateLifetime = false,
        };
        return handler.ValidateToken(token, parms, out _);
    }

    private static ServiceProvider BuildProvider()
    {
        var dbName = $"revoke-seam-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ReflowDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IAppDbContext>(s => s.GetRequiredService<ReflowDbContext>());
        return services.BuildServiceProvider();
    }

    private static TokenRevocationList NewList(IServiceProvider sp, IClock clock) =>
        new(sp.GetRequiredService<IServiceScopeFactory>(), Options.Create(new JwtOptions()), clock,
            NullLogger<TokenRevocationList>.Instance);

    private sealed class TestClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = now;
        public void Advance(TimeSpan by) => UtcNow += by;
    }

    private sealed class TestHub : Hub
    {
        public void Ping() { }
    }

    private sealed class FakeCallerContext(ClaimsPrincipal? user) : HubCallerContext
    {
        public override string ConnectionId => "c1";
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User { get; } = user;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }
}
