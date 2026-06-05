using Microsoft.Extensions.Caching.Memory;

namespace ReflowOven.Infrastructure.Auth;

/// <summary>
/// In-process login throttle backed by <see cref="IMemoryCache"/>. Counts consecutive failures per
/// username inside a sliding window; once <see cref="MaxFailures"/> is reached the name is locked for a
/// fixed <see cref="Cooldown"/>. The cache handles expiry, so there is no background sweeping. A single
/// OrangePi instance has no need for a distributed store.
/// </summary>
public sealed class LoginThrottle(IMemoryCache cache) : ILoginThrottle
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);

    private static string FailKey(string u) => $"login:fail:{u}";
    private static string LockKey(string u) => $"login:lock:{u}";

    public bool IsLocked(string usernameLower) => cache.TryGetValue(LockKey(usernameLower), out _);

    public void RecordFailure(string usernameLower)
    {
        var next = (cache.TryGetValue<int>(FailKey(usernameLower), out var c) ? c : 0) + 1;
        if (next >= MaxFailures)
        {
            cache.Remove(FailKey(usernameLower));
            cache.Set(LockKey(usernameLower), true, Cooldown);
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
