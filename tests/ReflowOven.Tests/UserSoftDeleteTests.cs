using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ReflowOven.Application.Abstractions;
using ReflowOven.Application.Common;
using ReflowOven.Application.Dtos;
using ReflowOven.Application.Services;
using ReflowOven.Domain.Abstractions;
using ReflowOven.Domain.Enums;
using ReflowOven.Infrastructure.Auth;
using ReflowOven.Infrastructure.Email;
using ReflowOven.Infrastructure.Persistence;
using Xunit;

namespace ReflowOven.Tests;

/// <summary>
/// User soft-delete + the Master "trash": a deleted user is hidden from the grid but visible to the Master
/// (with who/when), its name stays reserved, and restore/purge round-trip. EF Core InMemory provider.
/// </summary>
public sealed class UserSoftDeleteTests
{
    [Fact]
    public async Task Delete_hides_the_user_keeps_it_in_the_trash_and_reserves_the_name()
    {
        await using var db = NewContext();
        var svc = NewService(db);
        var created = await svc.CreateAsync(new CreateUserRequest("joao.teste", "joao@x.com"));

        await svc.DeleteAsync(Guid.Parse(created.Id));

        // Hidden from the normal grid…
        Assert.DoesNotContain(await svc.ListAsync(), u => u.Id == created.Id);
        // …present in the Master's trash, with who/when…
        var row = Assert.Single(await svc.ListDeletedAsync());
        Assert.Equal("joao.teste", row.Name);
        Assert.NotNull(row.DeletedAt);
        Assert.Equal("master.dev", row.DeletedBy);
        // …and the name stays reserved (cannot recreate while soft-deleted).
        await Assert.ThrowsAsync<ConflictException>(() =>
            svc.CreateAsync(new CreateUserRequest("joao.teste", "outro@x.com")));
    }

    [Fact]
    public async Task Restore_brings_the_user_back_and_purge_removes_it_for_good()
    {
        await using var db = NewContext();
        var svc = NewService(db);
        var created = await svc.CreateAsync(new CreateUserRequest("maria.teste", "maria@x.com"));
        var id = Guid.Parse(created.Id);

        await svc.DeleteAsync(id);
        await svc.RestoreAsync(id);

        Assert.Contains(await svc.ListAsync(), u => u.Id == created.Id); // back in the grid
        Assert.Empty(await svc.ListDeletedAsync());                       // no longer in the trash

        await svc.DeleteAsync(id);
        await svc.PurgeAsync(id);
        Assert.Empty(await svc.ListDeletedAsync());                       // gone for good
        // The name is free only after a hard purge.
        var again = await svc.CreateAsync(new CreateUserRequest("maria.teste", "maria2@x.com"));
        Assert.Equal("maria.teste", again.Name);
    }

    // ---- helpers ---------------------------------------------------------

    private static ReflowDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ReflowDbContext>()
            .UseInMemoryDatabase($"softdelete-{Guid.NewGuid():N}")
            .Options;
        return new ReflowDbContext(options);
    }

    private static UserService NewService(ReflowDbContext db)
    {
        var clock = new FixedClock();
        var current = new MasterUser();
        var audit = new AuditService(db, clock, current, NullLogger<AuditService>.Instance);
        return new UserService(db, new BcryptPasswordHasher(), clock, audit, current,
            new StubEmailSender(NullLogger<StubEmailSender>.Instance), NullLogger<UserService>.Instance);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    }

    // Acts as the dev Master performing the trash operations; UserId is null so no activity counter is bumped.
    private sealed class MasterUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? Id => null;
        public Guid? UserId => null;
        public string? Name => "master.dev";
        public UserType? Role => UserType.Master;
        public bool IsCalibration => false;
    }
}
