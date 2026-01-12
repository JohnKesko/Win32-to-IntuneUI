# Win32 to Intune Package Creator

A Windows app for creating `.intunewin` packages and uploading them directly to Microsoft Intune.

![Win32 to Intune](img/screenshot.png)

## Download

Get the latest release from [GitHub Releases](https://github.com/JohnKesko/Win32-to-IntuneUI/releases):

- **Windows**: `Win32-to-IntuneUI-Setup.exe` (includes auto-updates)

> **Note**: The app automatically downloads `IntuneWinAppUtil.exe` from Microsoft on first launch.

## Features

### Single Package

Create a single `.intunewin` package:

1. Select **Source Folder** (folder containing your app files)
2. Select **Setup File** (the installer: .exe, .msi, .cmd, .bat)
3. Select **Output Folder** (where to save the package)
4. Click **Create Package**

### Batch Processing

Process multiple applications at once:

1. Select a **Parent Folder** containing subfolders (one per app)
2. Click **Scan Folders** - the app auto-detects installers
3. Review the list and fix any that need attention
4. Select **Output Folder**
5. Click **Process Batch**

### Upload to Intune

Upload packages directly to Microsoft Intune:

1. Enter your Azure AD credentials (Tenant ID, Client ID, Client Secret)
2. Click **Connect** to authenticate
3. Browse for `.intunewin` files or use packages from Single/Batch modes
4. Edit app metadata (name, install/uninstall commands, publisher, description)
5. Click **Start Upload**

### Settings

View consolidated logs from all operations. Export logs for troubleshooting.

---

## Configuration File (Optional)

For advanced control during batch processing, place an `intuneconfig.json` file in any app folder:

```json
{
  "installer": "setup.exe",
  "displayName": "My Application",
  "publisher": "Contoso Ltd",
  "description": "Productivity tool for enterprise",
  "installCommand": "setup.exe /S /ALLUSERS",
  "uninstallCommand": "setup.exe /S /uninstall",
  "skip": false
}
```

| Field | Description |
|-------|-------------|
| `installer` | Relative path to the setup file |
| `displayName` | App name (defaults to folder name) |
| `publisher` | Publisher name |
| `description` | App description |
| `installCommand` | Silent install command |
| `uninstallCommand` | Silent uninstall command |
| `skip` | Set `true` to skip this folder during batch |

**All fields are optional.** Without a config file, the app:
- Auto-detects the installer (.msi, setup.exe, etc.)
- Uses the folder name as the display name
- Generates default install/uninstall commands

### Example Folder Structure

```
📁 Apps/
├── 📁 7-Zip/
│   ├── 📄 7z2301-x64.msi
│   └── 📄 intuneconfig.json   ← specify display name, publisher
├── 📁 Chrome/
│   ├── 📄 GoogleChromeEnterprise64.msi
│   └── 📄 intuneconfig.json   ← custom uninstall command
└── 📁 _Templates/
    └── 📄 intuneconfig.json   ← "skip": true (folder ignored)
```

---

## Azure AD App Setup

To upload to Intune, create an Azure AD App Registration:

1. Go to [Azure Portal](https://portal.azure.com) → Azure Active Directory → App registrations
2. Click **New registration**, give it a name
3. Under **API permissions**, add:
   - `DeviceManagementApps.ReadWrite.All` (Application permission)
4. Grant admin consent
5. Under **Certificates & secrets**, create a new client secret
6. Copy the **Tenant ID**, **Application (client) ID**, and **Secret value**

---

## Building from Source

```bash
git clone https://github.com/JohnKesko/Win32-to-IntuneUI.git
cd Win32-to-IntuneUI
dotnet run --project Win32-to-IntuneUI/Win32-to-IntuneUI.csproj
```

**Requirements**: .NET 10.0 SDK

---

## License

Open source under MIT. Microsoft Win32 Content Prep Tool is licensed separately by Microsoft.
