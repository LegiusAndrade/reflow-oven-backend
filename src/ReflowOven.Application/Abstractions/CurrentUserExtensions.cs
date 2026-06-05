namespace ReflowOven.Application.Abstractions;

/// <summary>Shared authority helpers derived from the current principal.</summary>
public static class CurrentUserExtensions
{
    /// <summary>Per-item delete authority: the Admin manages everything; the dev Master may only remove
    /// items IT created (others don't even show a delete control); nobody else deletes. Shared by
    /// ProgramService/UserService so the front's <c>CanDelete</c> flag and the server-side guard stay in
    /// lock-step.</summary>
    public static bool CanDeleteOwnedBy(this ICurrentUser current, Guid? createdById) => current.Role switch
    {
        UserType.Admin => true,
        UserType.Master => createdById is not null && createdById == current.UserId,
        _ => false,
    };
}
