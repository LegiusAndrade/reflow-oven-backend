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
            throw new ValidationAppException("Usuário aceita apenas letras, números e ponto (.) — sem espaços ou outros caracteres especiais.");
    }

    public static void ValidateEmail(string email)
    {
        if (email.Length > DomainConstants.EmailMaxLength || !EmailRe.IsMatch(email))
            throw new ValidationAppException("E-mail inválido.");
    }

    /// <summary>Any character is allowed; only the length is enforced (max = BCrypt's byte limit).</summary>
    public static void ValidatePassword(string password)
    {
        if (password.Length < DomainConstants.PasswordMinLength)
            throw new ValidationAppException($"A senha deve ter ao menos {DomainConstants.PasswordMinLength} caracteres.");
        if (password.Length > DomainConstants.PasswordMaxLength)
            throw new ValidationAppException($"A senha deve ter no máximo {DomainConstants.PasswordMaxLength} caracteres.");
    }
}
