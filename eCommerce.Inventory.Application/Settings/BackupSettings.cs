namespace eCommerce.Inventory.Application.Settings;

public class BackupSettings
{
    public bool Enabled { get; set; } = true;
    public string Schedule { get; set; } = "0 2 * * *"; // Daily at 02:00 AM
    public int RetentionDays { get; set; } = 3;
    public string BackupPath { get; set; } = "Backups";
}
