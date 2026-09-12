using MuktoAin.Application.Services;
using Xunit;

namespace MuktoAin.UnitTests.Services;

public class ModerationServiceTests
{
    private readonly ModerationService _service;

    public ModerationServiceTests()
    {
        _service = new ModerationService();
    }

    [Theory]
    [InlineData("আমার ৩ মাসের বকেয়া বেতন কোম্পানি পরিশোধ করছে না।")]
    [InlineData("বাসা ছাড়ার পর বাড়িওয়ালা সিকিউরিটি ডিপোজিট ফেরত দিচ্ছেন না।")]
    [InlineData("Yesterday my national identity card was lost at Motijheel area.")]
    [InlineData("I need to file a consumer complaint against an e-commerce seller.")]
    [InlineData("তথ্য অধিকার আইন ২০০৯ অনুযায়ী সরকারি উন্নয়ন প্রকল্পের বাজেট বিবরণী পেতে চাই।")]
    public void IsContentAppropriate_ValidLegalQueries_ReturnsTrue(string validText)
    {
        var result = _service.IsContentAppropriate(validText);

        Assert.True(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsContentAppropriate_NullOrEmptyOrWhitespace_ReturnsTrue(string? emptyText)
    {
        var result = _service.IsContentAppropriate(emptyText);

        Assert.True(result);
    }

    [Theory]
    [InlineData("How to hire killer for contract work")]
    [InlineData("I want to know about assassination")]
    [InlineData("Methods for credit card fraud online")]
    [InlineData("Distributing exploit kit and ransomware payout")]
    [InlineData("Where to buy weapons online")]
    public void IsContentAppropriate_EnglishBlockedTerms_ReturnsFalse(string prohibitedEnglish)
    {
        var result = _service.IsContentAppropriate(prohibitedEnglish);

        Assert.False(result);
    }

    [Theory]
    [InlineData("কীভাবে ঘরে বসে বোমা তৈরি করা যায়?")]
    [InlineData("সীমান্ত দিয়ে অবৈধ অস্ত্র পাচার সংক্রান্ত তথ্য")]
    [InlineData("আন্তর্জাতিক মাদক চোরাচালান চক্রের খবর")]
    [InlineData("গুপ্ত হামলা এবং সন্ত্রাসী হামলা চালানোর নির্দেশিকা")]
    public void IsContentAppropriate_BanglaBlockedTerms_ReturnsFalse(string prohibitedBangla)
    {
        var result = _service.IsContentAppropriate(prohibitedBangla);

        Assert.False(result);
    }

    [Theory]
    [InlineData("HITMAN service required")]
    [InlineData("PhIsHiNg AtTaCk campaign")]
    [InlineData("ASSASSINATION plot")]
    public void IsContentAppropriate_CaseInsensitiveMatching_ReturnsFalse(string mixedCaseBlocked)
    {
        var result = _service.IsContentAppropriate(mixedCaseBlocked);

        Assert.False(result);
    }
}
