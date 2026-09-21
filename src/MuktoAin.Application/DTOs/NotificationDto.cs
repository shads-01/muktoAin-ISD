using MuktoAin.Domain.Enums;

namespace MuktoAin.Application.DTOs;

public record NotificationDto(
    int NotificationId,
    NotificationType Type,
    int? RelatedCaseId,
    int? RelatedDocumentId,
    int? RelatedLawyerProfileId,
    bool IsRead,
    DateTime CreatedAt
);
