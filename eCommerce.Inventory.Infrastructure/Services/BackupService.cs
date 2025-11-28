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
                // Should not happen if GetNextRunTime is correct, but safety check
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
        // Simple daily schedule parser
        // Assumes format "m h * * *" (Cron-like but simplified for daily)
        // Or just hardcoded to run at specific time if parsing fails

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
        _logger.LogInformation("Starting scheduled backup...");

        var backupFolder = Path.Combine(AppContext.BaseDirectory, _settings.BackupPath);
        Directory.CreateDirectory(backupFolder);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var dbBackupPath = Path.Combine(backupFolder, $"InventoryDB_{timestamp}.bak");
        var appDataBackupPath = Path.Combine(backupFolder, $"AppData_{timestamp}.zip");

        // 1. Database Backup
        try
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var connectionString = dbContext.Database.GetConnectionString();

                // Extract database name from connection string
                var builder = new SqlConnectionStringBuilder(connectionString);
                var dbName = builder.InitialCatalog;

                _logger.LogInformation("Backing up database {DbName} to {Path}", dbName, dbBackupPath);

                // Execute BACKUP DATABASE command
                // Note: COMPRESSION is not supported in SQL Server Express Edition
                // Removed COMPRESSION option for compatibility

                var sql = $"BACKUP DATABASE [{dbName}] TO DISK = @path WITH FORMAT, INIT";
                await dbContext.Database.ExecuteSqlRawAsync(sql, new SqlParameter("@path", dbBackupPath));

                _logger.LogInformation("Database backup completed successfully.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database backup failed");
            // Continue to file backup even if DB fails? Yes.
        }

        // 2. Application Data Backup (Frontend UI + Logs)
        try
        {
            // When running as Windows Service, AppContext.BaseDirectory points to Publish/api
            // Frontend is in Publish/ui (sibling directory)
            var publishRoot = Path.GetDirectoryName(AppContext.BaseDirectory); // Go up to Publish folder
            var uiPath = Path.Combine(publishRoot!, "ui");
            var logsDir = Path.Combine(AppContext.BaseDirectory, "logs");

            // Create a temporary folder to collect files
            var tempDir = Path.Combine(Path.GetTempPath(), $"InventoryBackup_{timestamp}");
            Directory.CreateDirectory(tempDir);

            // Backup frontend (UI)
            if (Directory.Exists(uiPath))
            {
                _logger.LogInformation("Backing up frontend from {Path}", uiPath);
                CopyDirectory(uiPath, Path.Combine(tempDir, "ui"));
            }
            else
            {
                _logger.LogWarning("Frontend directory not found at {Path}", uiPath);
            }

            // Backup logs
            if (Directory.Exists(logsDir))
            {
                _logger.LogInformation("Backing up logs from {Path}", logsDir);
                CopyDirectory(logsDir, Path.Combine(tempDir, "logs"));
            }

            _logger.LogInformation("Zipping application data to {Path}", appDataBackupPath);
            ZipFile.CreateFromDirectory(tempDir, appDataBackupPath);

            // Cleanup temp
            Directory.Delete(tempDir, true);

            _logger.LogInformation("Application data backup completed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Application data backup failed");
        }

        // 3. Retention Policy
        await ApplyRetentionPolicyAsync(backupFolder);
    }

    private async Task ApplyRetentionPolicyAsync(string backupFolder)
    {
        try
        {
            _logger.LogInformation("Applying retention policy. Keeping backups for {Days} days.", _settings.RetentionDays);

            var retentionDate = DateTime.Now.AddDays(-_settings.RetentionDays);
            var files = Directory.GetFiles(backupFolder);

            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.CreationTime < retentionDate)
                {
                    _logger.LogInformation("Deleting old backup file: {File}", fileInfo.Name);
                    fileInfo.Delete();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying retention policy");
        }
    }

    private void CopyDirectory(string sourceDir, string destinationDir)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

        Directory.CreateDirectory(destinationDir);

        foreach (FileInfo file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath);
        }

        foreach (DirectoryInfo subDir in dir.GetDirectories())
        {
            string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
            CopyDirectory(subDir.FullName, newDestinationDir);
        }
    }
}
