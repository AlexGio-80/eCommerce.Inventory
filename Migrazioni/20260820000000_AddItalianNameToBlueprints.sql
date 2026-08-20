-- Migration: Add ItalianName to Blueprints
-- Apply via SQL directo on production DB (ECommerceInventory on .\SQLEXPRESS)
-- After running, register in __EFMigrationsHistory to keep EF in sync

ALTER TABLE Blueprints ADD ItalianName nvarchar(max) NULL;

-- Register migration in history (match the C# migration filename without extension)
IF NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = '20260820000000_AddItalianNameToBlueprints')
BEGIN
    INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
    VALUES ('20260820000000_AddItalianNameToBlueprints', '8.0.0');
END
