# Win32 to Intune Package Creator

An app built with Avalonia for creating `.intunewin` packages for Microsoft Intune deployment.

## Requirements

.NET 10  
https://dotnet.microsoft.com/en-us/download

Just download the SDK and install it. Nothing else needed.

## How It Works

The application automatically downloads `IntuneWinAppUtil.exe` from Microsoft's repo on first launch. The tool is stored locally and does not need to be downloaded again.

<img src="./img/img01.png" width="550" height="">

## Usage

1. Launch the application
2. Select the source folder containing your application files
3. Select the setup file (installer executable or MSI)
4. Select the output folder where the package will be saved
5. Click "Create Package"

The application will create a `.intunewin` file in the output folder.

## Tool Storage

The downloaded tool is stored at:  
`%LocalAppData%\Win32-to-IntuneUI\tools\IntuneWinAppUtil.exe`

## Building

```bash
dotnet restore
dotnet build
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
