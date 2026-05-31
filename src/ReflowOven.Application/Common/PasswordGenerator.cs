using System.Security.Cryptography;

namespace ReflowOven.Application.Common;

/// <summary>
/// Generates the strong, system-issued initial password emailed to a new user. Uses a cryptographic
/// RNG and an unambiguous alphabet (no 0/O/1/l/I) so the password is easy to read in the email.
/// </summary>
public static class PasswordGenerator
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%&*?";
    private const string All = Upper + Lower + Digits + Symbols;

    public static string Generate(int length = DomainConstants.GeneratedPasswordLength)
    {
        if (length < 4) length = 4;

        // Guarantee one char from each class, fill the rest from the full set, then shuffle.
        var chars = new List<char>(length)
        {
            Upper[RandomNumberGenerator.GetInt32(Upper.Length)],
            Lower[RandomNumberGenerator.GetInt32(Lower.Length)],
            Digits[RandomNumberGenerator.GetInt32(Digits.Length)],
            Symbols[RandomNumberGenerator.GetInt32(Symbols.Length)],
        };
        while (chars.Count < length)
            chars.Add(All[RandomNumberGenerator.GetInt32(All.Length)]);

        for (var i = chars.Count - 1; i > 0; i--) // Fisher–Yates with a cryptographic RNG
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string([.. chars]);
    }
}
