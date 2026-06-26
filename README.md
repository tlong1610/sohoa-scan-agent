# Sohoa Scan Agent

Windows desktop agent for TWAIN document scanning (Plustek PS4080U and compatible scanners).

- Runs locally on scan workstations (`http://127.0.0.1:18612`)
- Staging: scan → edit → export PDF → commit to Sohoa data-lake via web app
- Stack: .NET 8, NTwain, SkiaSharp, PDFsharp

## Install (scan workstation)

1. Install Plustek PS4080U TWAIN driver
2. Download `SohoaScanAgent-Setup.exe` from [Releases](https://github.com/tlong1610/sohoa-scan-agent/releases) (when available)
3. Run installer — agent starts in system tray
4. Open Sohoa web app on the same PC and use Scan Intake

## Build from source

```powershell
dotnet build src\Sohoa.ScanAgent\Sohoa.ScanAgent.csproj -c Release
```

Output: `src\Sohoa.ScanAgent\bin\Release\net8.0-windows\SohoaScanAgent.exe`

## API

See [docs/openapi.yaml](docs/openapi.yaml) for the local HTTP API contract used by the React frontend.
