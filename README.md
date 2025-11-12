# Win32 to Intune Package Creator

An app built with Avalonia for creating `.intunewin` packages for Microsoft Intune deployment.

## Requirements

.NET 10 for the UI   
https://dotnet.microsoft.com/en-us/download   
Just download the SDK and install it. Nothing else needed.

.NET Framework >= 4.7.2 for MS Prep Tool   
https://github.com/microsoft/Microsoft-Win32-Content-Prep-Tool



## How It Works

The application automatically downloads `IntuneWinAppUtil.exe` from Microsoft's repo on first launch. The tool is stored locally and does not need to be downloaded again.

**There is two modes:**

### Single Package Mode

<img src="./img/img01.png" width="550" height="">

Create individual `.intunewin` packages one at a time:

1. Launch the application
2. Select the source folder containing your application files
3. Select the setup file (installer executable or MSI)
4. Select the output folder where the package will be saved
5. Optionally, select a catalog folder
6. Click "Create Package"

### Batch Processing Mode

<img src="./img/img02.png" width="550" height="">

Process multiple applications at once:

1. Select a parent folder containing subfolders with different applications
2. Click "Scan Folders" to detect applications
3. A dialog will appear showing all detected applications - review and select installers for apps that need attention
4. Select an output folder for all packages
5. Click "Process Batch" to create all packages
6. Optionally, click "Upload to Intune" to upload packages directly to Microsoft Intune

The application will create `.intunewin` files in the specified output folder.

## Upload to Microsoft Intune

After creating packages in batch mode, you can upload them directly to Intune:

1. Click "Upload to Intune" button after batch processing
2. Paste your Microsoft Graph access token
3. Click "Test Connection" to verify authentication
4. Review and edit application names in the grid
5. Click "Start Upload" to upload all packages

### Getting a Microsoft Graph Access Token

To upload to Intune, you need a Microsoft Graph access token with appropriate permissions. Here's how to get one:

#### Option 1: Using Graph Explorer (Quick Testing)
1. Visit [Graph Explorer](https://developer.microsoft.com/en-us/graph/graph-explorer)
2. Sign in with your Microsoft account
3. Click "Modify permissions" and consent to: `DeviceManagementApps.ReadWrite.All`
4. Copy the access token from the "Access token" tab

#### Option 2: Using Azure AD App Registration (Production)
1. Go to [Azure Portal](https://portal.azure.com) > Azure Active Directory > App registrations
2. Click "New registration"
   - Name: `Win32-to-IntuneUI`
   - Supported account types: Choose appropriate option
   - Redirect URI: Leave blank for now
3. After creation, note the "Application (client) ID" and "Directory (tenant) ID"
4. Go to "API permissions" > "Add a permission" > "Microsoft Graph" > "Application permissions"
5. Add: `DeviceManagementApps.ReadWrite.All`
6. Click "Grant admin consent" (requires admin privileges)
7. Go to "Certificates & secrets" > "New client secret" > Create and copy the secret value
8. Use a tool or script to get an access token:

```powershell
$tenantId = "YOUR_TENANT_ID"
$clientId = "YOUR_CLIENT_ID"
$clientSecret = "YOUR_CLIENT_SECRET"

$body = @{
    grant_type    = "client_credentials"
    client_id     = $clientId
    client_secret = $clientSecret
    scope         = "https://graph.microsoft.com/.default"
}

$response = Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$tenantId/oauth2/v2.0/token" -Body $body
$response.access_token
```

**Note:** Access tokens expire after 1 hour. You'll need to generate a new one when it expires.

## Tool Storage

The downloaded tool is stored at:  
`%LocalAppData%\Win32-to-IntuneUI\tools\IntuneWinAppUtil.exe`

## Building

```
git clone https://github.com/JohnKesko/Win32-to-IntuneUI.git
cd Win32-to-IntuneUI
dotnet run --project Win32-to-IntuneUI/Win32-to-IntuneUI.csproj
```

## Release Build

To create a self-contained executable for distribution:

```bash
dotnet publish -c Release
```

The executable will be located at:  
`Win32-to-IntuneUI/bin/Release/net10.0/win-x64/publish/Win32-to-IntuneUI.exe`

## License

This project is open source. The Microsoft Win32 Content Prep Tool is licensed separately by Microsoft. See the [official repository](https://github.com/Microsoft/Microsoft-Win32-Content-Prep-Tool) for license terms.
