#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;

namespace NMBHLabSys.LabManagerSubsystem.Services;

public interface ILabManagerPasswordService
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string storedHash);

    /// <summary>A random password that always meets the complexity rules (AC-06-02 / AC-07-02).</summary>
    string GenerateTemporaryPassword(int length = 12);

    /// <summary>A random, URL-safe token for the reset link, plus the hash to store (AC-09-03).</summary>
    (string Token, string TokenHash) GenerateResetToken();
    string HashResetToken(string token);
}

/// <summary>
/// Passwords are hashed THE SAME WAY AS THE GROUP'S LOGIN (NMBDHL.Auth AuthRepository.HashPassword):
/// SHA-256 of the UTF-8 password, written as 64 lowercase hex characters, stored in [User].PasswaordHash.
/// That way the doctors and technicians this subsystem creates can log in on the group's login page,
/// and a manager's Change Password checks the current password correctly.
///
/// VerifyPassword also still accepts the older PBKDF2 hashes (ASP.NET PasswordHasher) that earlier
/// versions of this backend and its test scripts wrote, so nothing already in a database stops working.
/// If the group changes its hashing, change HashPassword and VerifyPassword here to match.
/// </summary>
public class LabManagerPasswordService : ILabManagerPasswordService
{
    // PasswordHasher needs a "user" type; it doesn't use it for anything.
    private sealed class HashUser { }

    private static readonly PasswordHasher<HashUser> Hasher = new();
    private static readonly HashUser User = new();

    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";   // no I or O (look like 1 and 0)
    private const string Lower = "abcdefghjkmnpqrstuvwxyz";    // no l
    private const string Digits = "23456789";                  // no 0 or 1
    private const string Special = "@#$!%&*?";

    // Same as the group's login: SHA-256, lowercase hex
    public string HashPassword(string password) => Sha256Hex(password);

    public bool VerifyPassword(string password, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash) || password is null) return false;
        string stored = storedHash.Trim();

        // The group's format: 64 hex characters (compared in constant time, ignoring case)
        if (stored.Length == 64 && IsHex(stored))
        {
            byte[] expected = Convert.FromHexString(stored);
            byte[] actual = SHA256.HashData(Encoding.UTF8.GetBytes(password));
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }

        // Older PBKDF2 hashes from earlier versions of this backend
        try
        {
            var result = Hasher.VerifyHashedPassword(User, stored, password);
            return result != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            // A hash in another format (or garbage) means "wrong password", not a crash.
            return false;
        }
    }

    private static string Sha256Hex(string password) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant();

    private static bool IsHex(string s)
    {
        foreach (char c in s)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    public string GenerateTemporaryPassword(int length = 12)
    {
        if (length < 8) length = 8;
        string all = Upper + Lower + Digits + Special;

        var chars = new char[length];
        chars[0] = Pick(Upper);
        chars[1] = Pick(Lower);
        chars[2] = Pick(Digits);
        chars[3] = Pick(Special);
        for (int i = 4; i < length; i++) chars[i] = Pick(all);

        // Shuffle so the required characters aren't always at the front
        for (int i = chars.Length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars);
    }

    public (string Token, string TokenHash) GenerateResetToken()
    {
        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (token, HashResetToken(token));
    }

    // Only the hash is stored, so someone who can read the database can't use a pending link.
    public string HashResetToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    // RandomNumberGenerator, not System.Random: these passwords are emailed out, so they must not be guessable.
    private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
}
