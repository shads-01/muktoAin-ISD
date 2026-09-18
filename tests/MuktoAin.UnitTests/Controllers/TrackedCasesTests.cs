using Microsoft.AspNetCore.Http;
using MuktoAin.Web.Session;

namespace MuktoAin.UnitTests.Controllers;

public class TrackedCasesTests
{
    [Fact]
    public void Tracking_PreservesLegacyFormat_AndResolvesByCase()
    {
        var session = new TestSession();
        session.SetString("TrackedCases", "10:synthetic-a|bad|11:synthetic-b");
        TrackedCases.Remember(session, 12, "synthetic-c");
        TrackedCases.Remember(session, 12, "synthetic-c");
        Assert.Equal("10:synthetic-a|11:synthetic-b|12:synthetic-c", session.GetString("TrackedCases"));
        Assert.Equal("synthetic-b", TrackedCases.Resolve(session, 11));
        Assert.Null(TrackedCases.Resolve(session, 99));
    }
}
