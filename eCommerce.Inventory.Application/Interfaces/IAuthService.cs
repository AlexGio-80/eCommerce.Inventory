using eCommerce.Inventory.Application.DTOs;

namespace eCommerce.Inventory.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponseDto> LoginAsync(LoginDto loginDto);
    Task ChangePasswordAsync(string username, ChangePasswordDto changePasswordDto);
}
