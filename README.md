# Win32 to Intune Package Creator

A cross-platform app for creating `.intunewin` packages and uploading them to Microsoft Intune.

## Download

Get the latest release from [GitHub Releases](https://github.com/JohnKesko/Win32-to-IntuneUI/releases):

- **Windows**: `Win32-to-IntuneUI-Setup.exe` (auto-updates included)
- **macOS**: `Win32-to-IntuneUI-x.x.x-osx-arm64.zip` or `osx-x64.zip`
- **Linux**: `Win32-to-IntuneUI-x.x.x-linux-x64.tar.gz`

## Requirements

- .NET Framework >= 4.7.2 (Windows only, for MS Prep Tool)

The app downloads `IntuneWinAppUtil.exe` from Microsoft's repo on first launch.

## Features

### Single Package Mode

Create individual `.intunewin` packages:

1. Select source folder, setup file, and output folder
2. Click "Create Package"

### Batch Processing Mode

Process multiple applications at once:

1. Select a parent folder containing app subfolders
2. Click "Scan Folders" to detect applications
3. Review detected apps and select installers
4. Click "Process Batch" to create all packages
5. Click "Upload to Intune" to upload directly

## Intune Upload

Upload packages directly to Intune using a Microsoft Graph access token.

### Required Permission

`DeviceManagementApps.ReadWrite.All`

### Getting a Token

**Graph Explorer** (testing): Visit [Graph Explorer](https://developer.microsoft.com/en-us/graph/graph-explorer), sign in, consent to permission, copy token.

**Azure AD App** (production):
```powershell
$body = @{
    grant_type    = "client_credentials"
    client_id     = "YOUR_CLIENT_ID"
    client_secret = "YOUR_CLIENT_SECRET"
    scope         = "https://graph.microsoft.com/.default"
}
$response = Invoke-RestMethod -Method Post `
    -Uri "https://login.microsoftonline.com/YOUR_TENANT_ID/oauth2/v2.0/token" `
    -Body $body
$response.access_token
```

## Building from Source

```bash
git clone https://github.com/JohnKesko/Win32-to-IntuneUI.git
cd Win32-to-IntuneUI
dotnet run --project Win32-to-IntuneUI/Win32-to-IntuneUI.csproj
```

## License

Open source. Microsoft Win32 Content Prep Tool is licensed separately by Microsoft.
