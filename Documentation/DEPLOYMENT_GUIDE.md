# eCommerce.Inventory - Deployment Guide

## Overview

This guide covers the deployment of the eCommerce.Inventory application to a local IIS server with the backend API running as a Windows Service.

## Architecture

- **Frontend (Angular)**: Hosted on IIS at `http://inventory.local` (port 80)
- **Backend (API)**: Running as Windows Service on `http://localhost:5152`

## Prerequisites

- Windows Server or Windows 10/11 with IIS installed
- .NET 8.0 Runtime (or later)
- SQL Server (local or remote)
- Administrator privileges

## Deployment Scripts

### 1. `publish.ps1` - Automated Build and Deployment

**Purpose**: Builds and deploys both frontend and backend, managing IIS and Windows Service.

**Usage**:
```powershell
# Run as Administrator
.\publish.ps1
```

**What it does**:
1. Stops IIS site and Windows Service
2. Cleans `Publish` directory
3. Builds backend (Release configuration)
4. Builds frontend (Production configuration)
5. Preserves `appsettings.Production.json`
6. Creates logs directory with proper permissions
7. Configures/updates Windows Service
8. Starts IIS and Windows Service

**Important**: Always run as Administrator!

### 2. `setup-iis.ps1` - Initial IIS Configuration

**Purpose**: One-time IIS setup for the application.

**Usage**:
```powershell
# Run as Administrator (only needed once)
.\setup-iis.ps1
```

**What it does**:
1. Creates IIS Application Pool `InventoryAppPool`
2. Creates IIS Site `InventorySite` pointing to `Publish/ui`
3. Creates `web.config` for Angular routing
4. Adds `inventory.local` entry to hosts file
5. Configures site bindings (port 80)

## Configuration Files

### appsettings.Production.json

**Location**: `eCommerce.Inventory.Api/appsettings.Production.json`

**Important**: This file contains your production secrets and is **preserved** during deployment.

**Required settings**:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_SERVER;Database=ECommerceInventory;..."
  },
  "CardTraderApi": {
    "BaseUrl": "https://api.cardtrader.com/api/v2",
    "BearerToken": "YOUR_CARDTRADER_API_TOKEN_HERE",
    "SharedSecret": "YOUR_CARDTRADER_SHARED_SECRET_HERE"
  }
}
```

### web.config (Auto-generated)

**Location**: `Publish/ui/web.config`

This file is automatically created by `setup-iis.ps1` and handles Angular routing.

## Windows Service Configuration

### Service Details

- **Name**: `eCommerce.Inventory`
- **Display Name**: eCommerce Inventory API
- **Account**: NetworkService
- **Startup Type**: Automatic
- **Port**: 5152

### Manual Service Management

```powershell
# Check service status
Get-Service -Name "eCommerce.Inventory"

# Start service
Start-Service -Name "eCommerce.Inventory"

# Stop service
Stop-Service -Name "eCommerce.Inventory"

# Restart service
Restart-Service -Name "eCommerce.Inventory"
```

### Logs Location

Service logs are written to: `C:\OSL\Sorgenti\Mio\eCommerce.Inventory\Publish\api\logs\`

## Troubleshooting

### Frontend shows IIS default page

**Solution**: Run `setup-iis.ps1` as Administrator to configure IIS correctly.

### API returns 404 errors

**Possible causes**:
1. Windows Service not running: `Start-Service -Name "eCommerce.Inventory"`
2. Service not listening on port 5152: Check Event Viewer
3. Firewall blocking port 5152

### Service won't start

**Check**:
1. Event Viewer → Windows Logs → Application
2. Service logs in `Publish/api/logs/`
3. Verify `appsettings.Production.json` exists and is valid
4. Ensure NetworkService has permissions on `Publish/api` folder

### Logs not being written

**Solution**: Ensure NetworkService has write permissions on logs folder:
```powershell
icacls "C:\OSL\Sorgenti\Mio\eCommerce.Inventory\Publish\api\logs" /grant "*S-1-5-20:(OI)(CI)M"
```

### Database connection errors

**Check**:
1. SQL Server is running and accessible
2. Connection string in `appsettings.Production.json` is correct
3. NetworkService has access to SQL Server database

## Post-Deployment Verification

1. **Frontend**: Navigate to `http://inventory.local` - should show Angular app
2. **Backend**: Navigate to `http://localhost:5152` - should show API response
3. **Service**: Check service is running: `Get-Service -Name "eCommerce.Inventory"`
4. **Logs**: Verify logs are being written to `Publish/api/logs/`

## Updating the Application

To deploy updates:

1. Make your code changes
2. Run `.\publish.ps1` as Administrator
3. The script will automatically:
   - Stop services
   - Build new versions
   - Deploy updates
   - Restart services

**Note**: `appsettings.Production.json` is preserved during updates.

## Security Considerations

1. **Never commit** `appsettings.Production.json` with real credentials
2. Use strong passwords for database connections
3. Keep Card Trader API tokens secure
4. Consider using HTTPS in production
5. Regularly update .NET runtime and dependencies

## Backup Recommendations

Before deploying updates, backup:
1. Database: `ECommerceInventory`
2. Configuration: `appsettings.Production.json`
3. Logs: `Publish/api/logs/` (if needed)

## Support

For issues or questions, check:
1. Application logs in `Publish/api/logs/`
2. Windows Event Viewer
3. IIS logs in `C:\inetpub\logs\LogFiles\`
