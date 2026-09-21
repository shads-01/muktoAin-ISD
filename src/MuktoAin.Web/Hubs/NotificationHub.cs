using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MuktoAin.Web.Hubs;

// Real-time notification channel. Server -> client only: the server sends a
// content-free "notificationsChanged" signal to one user (SignalR's default
// user id = the ClaimTypes.NameIdentifier claim, i.e. User.Id), and the
// browser re-fetches /Notification/Unread. No notification text travels over
// the socket, so ownership checks and bilingual formatting stay in one place.
[Authorize]
public class NotificationHub : Hub
{
    public const string Path = "/hubs/notifications";
    public const string ChangedEvent = "notificationsChanged";
}
