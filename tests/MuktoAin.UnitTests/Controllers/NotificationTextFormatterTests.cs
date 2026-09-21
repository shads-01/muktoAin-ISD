using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Enums;
using MuktoAin.Web.Controllers;
using Xunit;

namespace MuktoAin.UnitTests.Controllers;

public class NotificationTextFormatterTests
{
    [Fact]
    public void Format_CaseSubmitted_LinksToCaseResult()
    {
        var dto = new NotificationDto(1, NotificationType.CaseSubmitted, RelatedCaseId: 42,
            RelatedDocumentId: null, RelatedLawyerProfileId: null, IsRead: false, CreatedAt: DateTime.UtcNow);

        var (textBn, textEn, url) = NotificationTextFormatter.Format(dto);

        Assert.Equal("/Case/Result?id=42", url);
        Assert.False(string.IsNullOrWhiteSpace(textBn));
        Assert.False(string.IsNullOrWhiteSpace(textEn));
    }

    [Fact]
    public void Format_LawyerVerified_LinksToLawyerStatus()
    {
        var dto = new NotificationDto(2, NotificationType.LawyerVerified, RelatedCaseId: null,
            RelatedDocumentId: null, RelatedLawyerProfileId: 7, IsRead: false, CreatedAt: DateTime.UtcNow);

        var (_, textEn, url) = NotificationTextFormatter.Format(dto);

        Assert.Equal("/Lawyer/Status", url);
        Assert.False(string.IsNullOrWhiteSpace(textEn));
    }
}
