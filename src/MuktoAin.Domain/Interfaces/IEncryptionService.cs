namespace MuktoAin.Domain.Interfaces;

/// <summary>
/// Field-level protection for sensitive citizen PII at rest.
/// </summary>
public interface IEncryptionService
{
    string Encrypt(string plaintext);

    string Decrypt(string ciphertext);
}
