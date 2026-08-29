using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Api.Models; // For ApiResponse
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace eCommerce.Inventory.Api.Controllers;

/// <summary>
/// Login e cambio password. La registrazione non esiste: l'applicazione ha un solo utente e
/// un endpoint aperto per crearne altri era un varco, non una funzionalità. Account nuovi si
/// aggiungono a mano sul database.
/// </summary>
[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Login([FromBody] LoginDto loginDto)
    {
        try
        {
            var result = await _authService.LoginAsync(loginDto);
            return Ok(new ApiResponse<AuthResponseDto>
            {
                Success = true,
                Data = result,
                Message = "Login successful"
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new ApiResponse<AuthResponseDto>
            {
                Success = false,
                Message = "Invalid username or password"
            });
        }
    }

    /// <summary>
    /// Cambia la password dell'utente che sta chiamando. Serve la password attuale, così un
    /// token rubato da solo non basta a chiudere fuori il proprietario dell'account.
    /// </summary>
    [HttpPost("change-password")]
    public async Task<ActionResult<ApiResponse<object>>> ChangePassword([FromBody] ChangePasswordDto changePasswordDto)
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
        {
            return Unauthorized(new ApiResponse<object>
            {
                Success = false,
                Message = "Token privo del nome utente"
            });
        }

        try
        {
            await _authService.ChangePasswordAsync(username, changePasswordDto);
            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Password aggiornata"
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new ApiResponse<object>
            {
                Success = false,
                Message = "Password attuale non corretta"
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<object>
            {
                Success = false,
                Message = ex.Message
            });
        }
    }
}
