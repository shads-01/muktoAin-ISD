using Microsoft.EntityFrameworkCore;
using MuktoAin.Domain.Entities;

namespace MuktoAin.UnitTests.Repositories;

public class ChatSessionConfigurationTests
{
    [Fact]
    public void GuestKeyIndex_IsFilteredAndNonUnique()
    {
        using var db = TestDbContextFactory.Create();
        var index = Assert.Single(db.Model.FindEntityType(typeof(ChatSession))!
            .GetIndexes().Where(i => i.Properties.Count == 1 &&
                i.Properties[0].Name == nameof(ChatSession.SessionKey)));
        Assert.False(index.IsUnique);
        Assert.Equal("IX_CHAT_SESSION_SessionKey", index.GetDatabaseName());
        Assert.Equal("[SessionKey] IS NOT NULL", index.GetFilter());
    }
}
