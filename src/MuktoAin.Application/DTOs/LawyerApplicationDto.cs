namespace MuktoAin.Application.DTOs;

// Lawyer verification request
public record LawyerApplicationDto(
    string BarRegistrationNumber,
    string? Specialization
);
