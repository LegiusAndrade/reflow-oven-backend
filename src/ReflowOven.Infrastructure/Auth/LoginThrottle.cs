using Microsoft.Extensions.Caching.Memory;

namespace ReflowOven.Infrastructure.Auth;

/// <summary>
/// In-process login throttle backed by <see cref="IMemoryCache"/>. Counts consecutive failures per
/// username inside a sliding window; once <see cref="MaxFailures"/> is reached the name is locked for a
/// fixed <see cref="Cooldown"/>. The lock entry stores its own expiry timestamp so the remaining time can
/// be surfaced to the client (a countdown). A single OrangePi instance has no need for a distributed store.
/// </summary>
public sealed class LoginThrottle(IMemoryCache cache, IClock clock) : ILoginThrottle
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);

    private static string FailKey(string u) => $"login:fail:{u}";
    private static string LockKey(string u) => $"login:lock:{u}";

    public TimeSpan? LockRemaining(string usernameLower)
    {
        if (cache.TryGetValue<DateTimeOffset>(LockKey(usernameLower), out var until))
        {
            var remaining = until - clock.UtcNow;
            if (remaining > TimeSpan.Zero) return remaining;
        }
        return null;
    }

    public void RecordFailure(string usernameLower)
    {
        var next = (cache.TryGetValue<int>(FailKey(usernameLower), out var c) ? c : 0) + 1;
        if (next >= MaxFailures)
        {
            cache.Remove(FailKey(usernameLower));
            // Store WHEN the lock expires (not just a flag), so LockRemaining can report the countdown.
            cache.Set(LockKey(usernameLower), clock.UtcNow + Cooldown, Cooldown);
        }
        else
        {
            cache.Set(FailKey(usernameLower), next, Window);
        }
    }

    public void Reset(string usernameLower)
    {
        cache.Remove(FailKey(usernameLower));
        cache.Remove(LockKey(usernameLower));
    }
}
