# Sohoa Scan Agent

Windows desktop agent for TWAIN document scanning (Plustek PS4080U and compatible scanners).

- Runs locally on scan workstations (`http://127.0.0.1:18612`)
- **v2:** Stateless TWAIN bridge — `POST /scan` uploads JPEG to MinIO presigned URL (no local staging)
- Stack: .NET 8, NTwain

## Install (scan workstation)

1. Install Plustek PS4080U TWAIN driver (32-bit TWAIN → use **win-x86** build)
2. Download **`SohoaScanAgent-2.0.0-win-x86.zip`** from [Releases](https://github.com/tlong1610/sohoa-scan-agent/releases)
3. Extract and run `SohoaScanAgent.exe` — agent appears in the system tray
4. (Recommended) Add a shortcut to `shell:startup` so it runs on login
5. Open the Sohoa web app on the **same PC** and use Scan Intake

See [docs/INSTALL.md](docs/INSTALL.md) for detailed Vietnamese instructions.

Verify agent: open `http://127.0.0.1:18612/health` in a browser on the scan PC.

## Create a new release (maintainers)

Push a version tag to trigger the GitHub Actions build:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

The workflow builds a self-contained Windows x64 exe and attaches it to a GitHub Release automatically.

## Build from source

```powershell
dotnet build src\Sohoa.ScanAgent\Sohoa.ScanAgent.csproj -c Release
```

Output: `src\Sohoa.ScanAgent\bin\Release\net8.0-windows\SohoaScanAgent.exe`

## API

See [docs/openapi.yaml](docs/openapi.yaml) for the local HTTP API contract used by the React frontend.
