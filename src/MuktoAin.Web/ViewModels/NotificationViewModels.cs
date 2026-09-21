namespace MuktoAin.Web.ViewModels;

public class NotificationListViewModel
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public List<NotificationItemViewModel> Items { get; set; } = new();
}

public class NotificationItemViewModel
{
    public int NotificationId { get; set; }
    public string TextBn { get; set; } = string.Empty;
    public string TextEn { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
