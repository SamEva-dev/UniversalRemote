using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Maui.Storage;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Maui.Media;

/// <summary>Stores a salted PBKDF2 PIN verifier in platform SecureStorage. Plaintext PINs are never persisted.</summary>
public sealed class SecureMediaProfilePinStore : IMediaProfilePinStore
{
    private const int Iterations = 120_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public async Task<bool> HasPinAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        ValidateProfile(profileId);
        cancellationToken.ThrowIfCancellationRequested();
        return !string.IsNullOrWhiteSpace(await SecureStorage.Default.GetAsync(Key(profileId)).ConfigureAwait(false));
    }

    public async Task SetPinAsync(Guid profileId, string pin, CancellationToken cancellationToken = default)
    {
        ValidateProfile(profileId);
        ValidatePin(pin);
        cancellationToken.ThrowIfCancellationRequested();

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        var verifier = string.Join('|', "v1", Iterations.ToString(CultureInfo.InvariantCulture), Convert.ToBase64String(salt), Convert.ToBase64String(hash));
        await SecureStorage.Default.SetAsync(Key(profileId), verifier).ConfigureAwait(false);
    }

    public async Task<bool> VerifyPinAsync(Guid profileId, string pin, CancellationToken cancellationToken = default)
    {
        ValidateProfile(profileId);
        if (string.IsNullOrWhiteSpace(pin)) return false;
        cancellationToken.ThrowIfCancellationRequested();
        var verifier = await SecureStorage.Default.GetAsync(Key(profileId)).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(verifier)) return false;

        try
        {
            var parts = verifier.Split('|');
            if (parts.Length != 4 || parts[0] != "v1") return false;
            var iterations = int.Parse(parts[1], CultureInfo.InvariantCulture);
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(pin, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
        catch (CryptographicException) { return false; }
    }

    public Task DeletePinAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        ValidateProfile(profileId);
        cancellationToken.ThrowIfCancellationRequested();
        SecureStorage.Default.Remove(Key(profileId));
        return Task.CompletedTask;
    }

    private static string Key(Guid profileId) => $"media.profile.pin.{profileId:N}";

    private static void ValidateProfile(Guid profileId)
    {
        if (profileId == Guid.Empty) throw new ArgumentException("Profile ID must not be empty.", nameof(profileId));
    }

    private static void ValidatePin(string pin)
    {
        if (pin.Length is < 4 or > 8 || pin.Any(c => !char.IsAsciiDigit(c)))
            throw new ArgumentException("Le PIN doit contenir 4 à 8 chiffres.", nameof(pin));
    }
}
