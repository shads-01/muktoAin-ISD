using Microsoft.AspNetCore.DataProtection;
using MuktoAin.Infrastructure.Security;

namespace MuktoAin.UnitTests.Services;

public class EncryptionServiceTests
{
    private readonly EncryptionService _encryptionService;

    public EncryptionServiceTests()
    {
        var provider = new EphemeralDataProtectionProvider();
        _encryptionService = new EncryptionService(provider);
    }

    [Fact]
    public void EncryptDecrypt_EnglishString_RoundtripsSuccessfully()
    {
        var plaintext = "Citizen National ID: 19901234567890";
        var ciphertext = _encryptionService.Encrypt(plaintext);
        var decrypted = _encryptionService.Decrypt(ciphertext);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void EncryptDecrypt_BanglaString_RoundtripsSuccessfully()
    {
        var plaintext = "আমার জাতীয় পরিচয়পত্র নম্বর: ১৯০১১২৩৪৫৬৭৮৯০";
        var ciphertext = _encryptionService.Encrypt(plaintext);
        var decrypted = _encryptionService.Decrypt(ciphertext);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_CiphertextDiffersFromPlaintext()
    {
        var plaintext = "Sensitive Legal Complaint Details";
        var ciphertext = _encryptionService.Encrypt(plaintext);

        Assert.NotEqual(plaintext, ciphertext);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Encrypt_EmptyOrNull_ReturnsOriginalInput(string? input)
    {
        var encrypted = _encryptionService.Encrypt(input!);
        var decrypted = _encryptionService.Decrypt(input!);

        Assert.Equal(input, encrypted);
        Assert.Equal(input, decrypted);
    }
}
