namespace MuktoAin.Application.DTOs;

// S-3.6: read model for the admin user-management list view (/Admin/Users).
public record UserListDto(
    int UserId,
    string FullName,
    string Email,
    string Role,
    string Status);
