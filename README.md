# QFX Watcher

A **WinUI 3** desktop application that automatically monitors the Microsoft Edge downloads folder for `.qfx` (OFX/QFX bank statement) files and uploads them to a self-hosted [Firefly III](https://www.firefly-iii.org/) instance.

---

## Features

| Feature | Details |
|---|---|
| **Auto-detect Edge downloads folder** | Reads the Edge browser profile preferences to find the configured download directory; falls back to the default Windows `%USERPROFILE%\Downloads` folder. |
| **Live file watching** | Uses `FileSystemWatcher` to react immediately when a new `.qfx` file appears — no polling. |
| **QFX / OFX parsing** | Pure .NET parser handles both legacy SGML (OFX 1.x) and XML (OFX 2.x / QFX) formats. |
| **Firefly III integration** | Authenticates with a Personal Access Token, retrieves your asset accounts, and posts parsed transactions using the Firefly III REST API. |
| **Confirmation dialog** | Optional prompt lets you choose which account to import into before uploading. |
| **Archive imported files** | Optionally moves the imported `.qfx` file to a `imported/` sub-folder after a successful upload. |
| **Account cache** | Fetched accounts are saved to a local JSON file (`%LOCALAPPDATA%\QfxWatcher\accounts.json`) so they are available without an active connection. |
| **Settings persistence** | All configuration is stored via `ApplicationData.LocalSettings` (no external config files). |

---

## Requirements

- Windows 10 version 2004 (build 19041) or later
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Windows App SDK 2.0](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)
- A running [Firefly III server](https://docs.firefly-iii.org/how-to/firefly-iii/installation/docker/)

---

## Getting Started

### 1. Build

```bash
git clone https://github.com/ErikApption/qfx-watcher.git
cd qfx-watcher
dotnet build QfxWatcher/QfxWatcher.csproj -c Release
```

### 2. Run

```bash
dotnet run --project QfxWatcher/QfxWatcher.csproj
```

Or open `QfxWatcher.sln` in Visual Studio 2022 and press **F5**.

---

## Configuration

Open the **Settings** page inside the app:

| Setting | Description |
|---|---|
| **Server URL** | Base URL of your Firefly III server, e.g. `https://firefly.example.com` |
| **Personal Access Token (API Key)** | A Firefly III Personal Access Token (Profile → OAuth → Personal Access Tokens) |
| **Custom watch folder** | Leave blank to use the auto-detected Edge downloads folder |
| **Default account** | Pre-select a Firefly III asset account for quick imports |
| **Confirm before importing** | Show a dialog for each detected file (recommended) |
| **Archive after import** | Move the `.qfx` file to a `imported/` sub-folder after upload |

Click **Test Connection** to verify the server URL and API key. Accounts are fetched and saved to the local cache, then **Save Settings**.

---

## Usage

1. Configure your Firefly III server on the **Settings** page and save.
2. Switch to the **Dashboard** page and click **▶ Start Watching**.
3. Download a `.qfx` file from your bank in Edge — the app will detect it automatically.
4. A dialog appears asking which account to import into. Select the account and click **Import**.
5. Transactions are posted to Firefly III. The import is logged in the dashboard.

---

## Project Structure

```
QfxWatcher/
├── Models/
│   ├── AppSettings.cs          # Persisted user settings
│   ├── QfxTransaction.cs       # Parsed transaction from QFX file
│   ├── FireflyAccount.cs       # Account from Firefly III API
│   └── ImportLogEntry.cs       # Dashboard log entry
├── Services/
│   ├── QfxParserService.cs     # OFX/QFX SGML+XML parser
│   ├── FireflyIIIService.cs    # HTTP client for Firefly III REST API
│   ├── FileWatcherService.cs   # FileSystemWatcher + Edge folder detection
│   └── SettingsService.cs      # LocalSettings wrapper + accounts JSON cache
├── ViewModels/
│   ├── DashboardViewModel.cs   # Dashboard state and import orchestration
│   └── SettingsViewModel.cs    # Settings form state
├── Pages/
│   ├── DashboardPage.xaml      # Main monitoring view
│   └── SettingsPage.xaml       # Configuration form
├── Converters/
│   └── BoolToVisibilityConverter.cs
├── App.xaml / App.xaml.cs      # Application bootstrap
└── MainWindow.xaml             # NavigationView shell
```

---

## Firefly III API

The app communicates with the Firefly III server's REST API:

| Operation | Endpoint |
|---|---|
| List asset accounts | `GET /api/v1/accounts?type=asset` |
| Import a transaction | `POST /api/v1/transactions` |

Authentication uses a **Personal Access Token** sent as a `Bearer` token in the `Authorization` header. Duplicate detection is handled by the `error_if_duplicate_hash` flag.

---

## License

MIT – see [LICENSE](LICENSE).
