using System.IO.Compression;
using eCommerce.Inventory.Application.Settings;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace eCommerce.Inventory.Infrastructure.Services;

public class BackupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BackupService> _logger;
    private readonly BackupSettings _settings;
    private readonly IConfiguration _configuration;

    public BackupService(
        IServiceProvider serviceProvider,
        ILogger<BackupService> logger,
        IOptions<BackupSettings> settings,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _settings = settings.Value;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Backup Service is disabled.");
            return;
        }

        _logger.LogInformation("Backup Service started. Schedule: {Schedule}, Retention: {Retention} days",
            _settings.Schedule, _settings.RetentionDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            var nextRun = GetNextRunTime();
            var delay = nextRun - DateTime.Now;

            if (delay.TotalMilliseconds <= 0)
            {
                delay = TimeSpan.FromMinutes(1);
            }

            _logger.LogInformation("Next backup scheduled for: {NextRun}", nextRun);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            try
            {
                await PerformBackupAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during backup process");
            }
        }
    }

    private DateTime GetNextRunTime()
    {
        var now = DateTime.Now;
        var parts = _settings.Schedule.Split(' ');

        int hour = 2;
        int minute = 0;

        if (parts.Length >= 2 && int.TryParse(parts[1], out int h) && int.TryParse(parts[0], out int m))
        {
            hour = h;
            minute = m;
        }

        var nextRun = new DateTime(now.Year, now.Month, now.Day, hour, minute, 0);

        if (nextRun <= now)
        {
            nextRun = nextRun.AddDays(1);
        }

        return nextRun;
    }

    private async Task PerformBackupAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting comprehensive backup...");

        // Determine backup destination (network/external drive or local)
        var backupFolder = string.IsNullOrWhiteSpace(_settings.BackupDestinationPath)
            ? Path.Combine(AppContext.BaseDirectory, _settings.BackupPath)
            : _settings.BackupDestinationPath;

        Directory.CreateDirectory(backupFolder);
        _logger.LogInformation("Backup destination: {Path}", backupFolder);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var tempBackupDir = Path.Combine(Path.GetTempPath(), $"InventoryBackup_{timestamp}");
        Directory.CreateDirectory(tempBackupDir);

        try
        {
            // 1. Database Backup
            var dbBackupPath = Path.Combine(tempBackupDir, $"Database_InventoryDB_{timestamp}.bak");
            await BackupDatabaseAsync(dbBackupPath);

            // 2. Backup Frontend (UI)
            var publishRoot = Path.GetDirectoryName(AppContext.BaseDirectory)!;
            var uiPath = Path.Combine(publishRoot, "ui");
            if (Directory.Exists(uiPath))
            {
                _logger.LogInformation("Backing up frontend from {Path}", uiPath);
                CopyDirectory(uiPath, Path.Combine(tempBackupDir, "Frontend_UI"));
            }
            else
            {
                _logger.LogWarning("Frontend directory not found at {Path}", uiPath);
            }

            // 3. Backup Backend (API) - Complete application
            var apiPath = AppContext.BaseDirectory;
            _logger.LogInformation("Backing up backend from {Path}", apiPath);
            CopyDirectory(apiPath, Path.Combine(tempBackupDir, "Backend_API"), excludePatterns: new[] { "Backups", "logs" });

            // 4. Backup Logs separately
            var logsDir = Path.Combine(apiPath, "logs");
            if (Directory.Exists(logsDir))
            {
                _logger.LogInformation("Backing up logs from {Path}", logsDir);
                CopyDirectory(logsDir, Path.Combine(tempBackupDir, "Logs"));
            }

            // 5. Create comprehensive ZIP
            var finalZipPath = Path.Combine(backupFolder, $"InventoryBackup_Complete_{timestamp}.zip");
            _logger.LogInformation("Creating comprehensive backup ZIP: {Path}", finalZipPath);
            ZipFile.CreateFromDirectory(tempBackupDir, finalZipPath, CompressionLevel.Optimal, false);

            _logger.LogInformation("✅ Comprehensive backup completed successfully: {Size} MB",
                new FileInfo(finalZipPath).Length / 1024 / 1024);

            // 6. Cleanup temp directory
            Directory.Delete(tempBackupDir, true);

            // 7. Apply retention policy
            await ApplyRetentionPolicyAsync(backupFolder);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup process failed");

            // Cleanup temp on failure
            if (Directory.Exists(tempBackupDir))
            {
                try { Directory.Delete(tempBackupDir, true); } catch { }
            }

            throw;
        }
    }

    private async Task BackupDatabaseAsync(string backupPath)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var connectionString = dbContext.Database.GetConnectionString();

            var builder = new SqlConnectionStringBuilder(connectionString);
            var dbName = builder.InitialCatalog;

            _logger.LogInformation("Backing up database {DbName} to {Path}", dbName, backupPath);

            // Note: COMPRESSION not supported in SQL Express
            var sql = $"BACKUP DATABASE [{dbName}] TO DISK = @path WITH FORMAT, INIT";
            await dbContext.Database.ExecuteSqlRawAsync(sql, new SqlParameter("@path", backupPath));

            _logger.LogInformation("✅ Database backup completed: {Size} MB",
                new FileInfo(backupPath).Length / 1024 / 1024);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database backup failed");
            throw;
        }
    }

    private async Task ApplyRetentionPolicyAsync(string backupFolder)
    {
        try
        {
            _logger.LogInformation("Applying retention policy. Keeping backups for {Days} days.", _settings.RetentionDays);

            var retentionDate = DateTime.Now.AddDays(-_settings.RetentionDays);
            var files = Directory.GetFiles(backupFolder, "InventoryBackup_Complete_*.zip");

            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.CreationTime < retentionDate)
                {
                    _logger.LogInformation("Deleting old backup: {File} ({Size} MB)",
                        fileInfo.Name, fileInfo.Length / 1024 / 1024);
                    fileInfo.Delete();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying retention policy");
        }
    }

    private void CopyDirectory(string sourceDir, string destinationDir, string[]? excludePatterns = null)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists)
        {
            _logger.LogWarning("Source directory not found: {Path}", dir.FullName);
            return;
        }

        Directory.CreateDirectory(destinationDir);

        foreach (FileInfo file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath, overwrite: true);
        }

        foreach (DirectoryInfo subDir in dir.GetDirectories())
        {
            // Skip excluded directories
            if (excludePatterns != null && excludePatterns.Any(pattern =>
                subDir.Name.Equals(pattern, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
            CopyDirectory(subDir.FullName, newDestinationDir, excludePatterns);
        }
    }
}
