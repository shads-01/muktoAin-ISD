using MuktoAin.Application.DTOs;
using MuktoAin.Domain.Enums;

namespace MuktoAin.Web.Controllers;

/// <summary>
/// Bilingual display text + deep-link URL for a Notification, by type.
/// Presentation mapping only — mirrors StatusText's shape and purpose.
/// </summary>
public static class NotificationTextFormatter
{
    public static (string TextBn, string TextEn, string Url) Format(NotificationDto dto) => dto.Type switch
    {
        NotificationType.CaseSubmitted => (
            "আপনার মামলা সফলভাবে জমা হয়েছে।",
            "Your case was submitted successfully.",
            $"/Case/Result?id={dto.RelatedCaseId}"),

        NotificationType.DocumentDecided => (
            "আপনার নথিতে আইনজীবী সিদ্ধান্ত দিয়েছেন।",
            "A lawyer made a decision on your document.",
            $"/Case/Result?id={dto.RelatedCaseId}"),

        NotificationType.LawyerVerified => (
            "আপনার আইনজীবী যাচাইয়ের সিদ্ধান্ত হয়েছে।",
            "Your lawyer verification decision is ready.",
            "/Lawyer/Status"),

        NotificationType.PaymentReceived => (
            "আপনার হিসাবে সম্মানী জমা হয়েছে।",
            "A payment was credited to your balance.",
            "/Lawyer/Payments"),

        NotificationType.NewLawyerApplication => (
            "নতুন আইনজীবী আবেদন যাচাইয়ের অপেক্ষায়।",
            "A new lawyer application is awaiting verification.",
            "/Admin/Lawyers"),

        NotificationType.NewCaseInQueue => (
            "আইনজীবী সারিতে একটি নতুন মামলা পর্যালোচনার অপেক্ষায়।",
            "A new case is waiting for review in the lawyer queue.",
            "/Lawyer/Queue?filter=Unclaimed"),

        NotificationType.DocumentResubmitted => (
            "আপনার পর্যালোচনাধীন একটি নথি নাগরিক আবার জমা দিয়েছেন।",
            "A citizen resubmitted a document you are reviewing.",
            $"/Lawyer/Review/{dto.RelatedDocumentId}"),

        _ => ("বিজ্ঞপ্তি", "Notification", "/")
    };
}
