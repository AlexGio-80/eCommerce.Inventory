namespace eCommerce.Inventory.Application.Settings;

public class BackupSettings
{
    public bool Enabled { get; set; } = true;
    public string Schedule { get; set; } = "0 2 * * *"; // Daily at 02:00 AM
    public int RetentionDays { get; set; } = 3;
    public string BackupPath { get; set; } = "Backups"; // Relative path (local backups)
    public string? BackupDestinationPath { get; set; } = null; // Optional: absolute path for network/external drive (e.g., "D:\\Backups" or "\\\\NAS\\Backups")
}
