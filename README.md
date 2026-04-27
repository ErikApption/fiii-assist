# QFX Watcher

A **WinUI 3** desktop application that automatically monitors the Microsoft Edge downloads folder for `.qfx` (OFX/QFX bank statement) files and uploads them to a self-hosted [Actual Budget](https://actualbudget.org/) instance.

---

## Features

| Feature | Details |
|---|---|
| **Auto-detect Edge downloads folder** | Reads the Edge browser profile preferences to find the configured download directory; falls back to the default Windows `%USERPROFILE%\Downloads` folder. |
| **Live file watching** | Uses `FileSystemWatcher` to react immediately when a new `.qfx` file appears — no polling. |
| **QFX / OFX parsing** | Pure .NET parser handles both legacy SGML (OFX 1.x) and XML (OFX 2.x / QFX) formats. |
| **Actual Budget integration** | Authenticates with the server, retrieves your accounts, and posts parsed transactions using the Actual Budget REST API. |
| **Confirmation dialog** | Optional prompt lets you choose which account to import into before uploading. |
| **Archive imported files** | Optionally moves the imported `.qfx` file to a `imported/` sub-folder after a successful upload. |
| **Settings persistence** | All configuration is stored via `ApplicationData.LocalSettings` (no external config files). |

---

## Requirements

- Windows 10 version 2004 (build 19041) or later
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Windows App SDK 1.5](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)
- A running [Actual Budget server](https://actualbudget.org/docs/install/)

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
| **Server URL** | Base URL of your Actual Budget server, e.g. `http://localhost:5006` |
| **Password** | The password you set when setting up Actual Budget |
| **Custom watch folder** | Leave blank to use the auto-detected Edge downloads folder |
| **Default account** | Pre-select an Actual Budget account for quick imports |
| **Confirm before importing** | Show a dialog for each detected file (recommended) |
| **Archive after import** | Move the `.qfx` file to a `imported/` sub-folder after upload |

Click **Test Connection** to verify the server URL and password, then **Save Settings**.

---

## Usage

1. Configure your Actual Budget server on the **Settings** page and save.
2. Switch to the **Dashboard** page and click **▶ Start Watching**.
3. Download a `.qfx` file from your bank in Edge — the app will detect it automatically.
4. A dialog appears asking which account to import into. Select the account and click **Import**.
5. Transactions are posted to Actual Budget. The import is logged in the dashboard.

---

## Project Structure

```
QfxWatcher/
├── Models/
│   ├── AppSettings.cs          # Persisted user settings
│   ├── QfxTransaction.cs       # Parsed transaction from QFX file
│   ├── ActualAccount.cs        # Account from Actual Budget API
│   └── ImportLogEntry.cs       # Dashboard log entry
├── Services/
│   ├── QfxParserService.cs     # OFX/QFX SGML+XML parser
│   ├── ActualBudgetService.cs  # HTTP client for Actual Budget REST API
│   ├── FileWatcherService.cs   # FileSystemWatcher + Edge folder detection
│   └── SettingsService.cs      # ApplicationData.LocalSettings wrapper
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

## Actual Budget API

The app communicates with the Actual Budget server's REST API:

| Operation | Endpoint |
|---|---|
| Authenticate | `POST /account/login` |
| List accounts | `GET /api/accounts` |
| Import transactions | `POST /api/accounts/{id}/import-transactions` |

Ensure your Actual Budget server version supports these endpoints (v23.x or later recommended).

---

## License

MIT – see [LICENSE](LICENSE).
