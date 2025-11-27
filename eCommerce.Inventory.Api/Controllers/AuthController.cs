using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Api.Models; // For ApiResponse
using Microsoft.AspNetCore.Mvc;

namespace eCommerce.Inventory.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

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

    [HttpPost("register")]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Register([FromBody] RegisterDto registerDto)
    {
        try
        {
            var result = await _authService.RegisterAsync(registerDto);
            return Ok(new ApiResponse<AuthResponseDto>
            {
                Success = true,
                Data = result,
                Message = "Registration successful"
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<AuthResponseDto>
            {
                Success = false,
                Message = ex.Message
            });
        }
    }
}
