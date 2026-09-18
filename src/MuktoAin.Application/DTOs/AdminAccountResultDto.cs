using MuktoAin.Domain.Entities;

namespace MuktoAin.Application.DTOs;

public record AdminAccountResultDto(User CreatedUser, string PasswordResetUrl);
