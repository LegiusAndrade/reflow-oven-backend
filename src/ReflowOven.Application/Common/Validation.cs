using System.Text.RegularExpressions;

namespace ReflowOven.Application.Common;

/// <summary>Field validators mirroring the frontend (limits.ts ranges + the username/email regexes).</summary>
public static class Validation
{
    private static readonly Regex UserNameRe = new(DomainConstants.UserNameRegex, RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EmailRe = new(DomainConstants.EmailRegex, RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static void ValidateUserName(string name)
    {
        if (name.Length < DomainConstants.UserNameMinLength || name.Length > DomainConstants.UserNameMaxLength)
            throw new ValidationAppException($"Usuário deve ter entre {DomainConstants.UserNameMinLength} e {DomainConstants.UserNameMaxLength} caracteres.");
        if (!UserNameRe.IsMatch(name))
            throw new ValidationAppException("Usuário contém caracteres inválidos.");
    }

    public static void ValidateEmail(string email)
    {
        if (email.Length > DomainConstants.EmailMaxLength || !EmailRe.IsMatch(email))
            throw new ValidationAppException("E-mail inválido.");
    }
}
