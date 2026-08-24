using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using eCommerce.Inventory.Infrastructure.Persistence;
using System.Diagnostics;

namespace eCommerce.Inventory.Api.HealthChecks;

/// <summary>
/// Health check for SQL Server database connectivity
/// </summary>
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<DatabaseHealthCheck> _logger;

    public DatabaseHealthCheck(ApplicationDbContext dbContext, ILogger<DatabaseHealthCheck> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Simple query to verify database connectivity
            await _dbContext.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);

            stopwatch.Stop();

            _logger.LogDebug("Database health check passed in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

            return HealthCheckResult.Healthy(
                "Database connection is healthy",
                new Dictionary<string, object>
                {
                    ["responseTimeMs"] = stopwatch.ElapsedMilliseconds,
                    ["provider"] = "SQL Server"
                });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(ex, "Database health check failed after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

            return HealthCheckResult.Unhealthy(
                "Database connection failed",
                ex,
                new Dictionary<string, object>
                {
                    ["responseTimeMs"] = stopwatch.ElapsedMilliseconds,
                    ["error"] = ex.Message
                });
        }
    }
}