namespace Auraly.Platform.Application.Auth.DTOs;

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
