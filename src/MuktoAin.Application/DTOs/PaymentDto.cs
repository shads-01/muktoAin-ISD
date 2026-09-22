using MuktoAin.Domain.Enums;

namespace MuktoAin.Application.DTOs;

public record PaymentOrderDto(
    int PaymentOrderId,
    int? CaseId,
    string Purpose,       // TopUp | Honorarium
    string Status,        // Pending | Paid | Failed | Refunded
    decimal Amount,
    decimal Commission,
    decimal NetToLawyer,
    string? GatewayRef,
    DateTime CreatedAt,
    DateTime? PaidAt,
    DateTime? RefundedAt,
    string? UserEmail,    // anonymized citizen (email domain only where needed; admin sees full)
    string? LawyerName
);

public record LawyerEarningsDto(
    decimal Balance,
    List<EarningRowDto> History
);

public record EarningRowDto(
    int PaymentOrderId,
    int CaseId,
    decimal Gross,
    decimal Commission,
    decimal Net,
    DateTime PaidAt
);
