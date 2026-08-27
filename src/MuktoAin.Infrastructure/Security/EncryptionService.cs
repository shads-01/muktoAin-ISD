using Microsoft.AspNetCore.DataProtection;
using MuktoAin.Domain.Interfaces;

namespace MuktoAin.Infrastructure.Security;

/// <summary>
/// Field-level PII encryption using the ASP.NET Data Protection API.
/// Framework-managed key rotation and persistence; no custom crypto.
/// </summary>
public class EncryptionService : IEncryptionService
{
    private readonly IDataProtector _protector;

    public EncryptionService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("MuktoAin.PII");
    }

    public string Encrypt(string plaintext) =>
        string.IsNullOrEmpty(plaintext) ? plaintext : _protector.Protect(plaintext);

    public string Decrypt(string ciphertext) =>
        string.IsNullOrEmpty(ciphertext) ? ciphertext : _protector.Unprotect(ciphertext);
}
