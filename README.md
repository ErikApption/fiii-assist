# Firefly iii Assistant

FiiiAssist is a desktop app for importing bank transactions into [Firefly III](https://www.firefly-iii.org/). It monitors your downloads folder for `.qfx` files, parses them, and guides you through importing transactions via a step-by-step wizard.

The solution also includes a CLI tool for migration an actual finance database to Firefly III with transactions, bank accounts, rules and categories.

---

## Why does this exist

- In Canada, the banks do not offer any sort of APIs, and services (like mx.com), that offer integration could breach your end user agreement because they do use web scraping which counts as password sharing. Basically all you can rely on is the download of transactions from your bank website.
- Most personal finance tools have a manual QFX import but it's often the extra step that keeps me from keeping my accounts up to date. Most QFX do include the account info so nothing prevents from just dropping all your files and automating the next steps
- A desktop app lets you watch the download folder, avoid the selection of the QFX, import it and do a lot more interactive automation than a web app.
- Once the transaction import was built with the same logic as the existing importer, there was not a big gap to automate the migration from Actual Finance (which is a great tool but lacks a legit API). 
- Final goal is to automate the generations of rules to classify my transactions with AI and LLM

---

## Usage

1. Configure your Firefly III connection on the **Settings** page.
2. Switch to the **Dashboard** — the file watcher starts automatically after a successful connection.
3. Download a `.qfx` file from your bank — the app detects it and opens the import wizard.
4. The wizard auto-matches the QFX account ID to your Firefly III accounts. If no match is found, select an account manually.
5. Preview the parsed transactions, then click **Import**.
6. Progress is shown in real-time. Results are logged in the dashboard.

---

## Features

| Feature | Details |
|---|---|
| **Live file watching** | React immediately when a new `.qfx` file appears |
| **Auto-detect downloads folder** | Reads Edge browser profile preferences to find the configured download directory; falls back to `%USERPROFILE%\Downloads`. |
| **Reworked Import** | This implements similar logic and workflow to the Fiii data importer |
| **QFX/OFX parsing** | Handles both legacy SGML (OFX 1.x) and XML (OFX 2.x / QFX) formats. |
| **Import wizard** | Multi-step wizard: file selection → account matching → transaction preview → import with progress. |
| **Auto account matching** | Matches QFX file ACCTID to Firefly III account numbers automatically; prompts to update Firefly III if the account number is missing. |
| **Duplicate detection** | Skips transactions whose external ID already exists; optionally checks by content (date, amount, accounts). |
| **Batch import mode** | Submits transactions in batches to Firefly III for better performance. |
| **Bank account mappings** | Configure regex patterns per account for filename-based auto-routing. |
| **Pending file detection** | On startup, scans the watch folder for unprocessed QFX files and prompts to import them. |
| **Archive imported files** | Optionally moves imported `.qfx` files to an `imported/` sub-folder. |
| **Settings persistence** | JSON-based config in `%LocalAppData%\FiiiAssist\` with atomic writes and automatic backup. |
| **CLI bulk importer** | Command-line tool to import from an Actual Budget SQLite export into Firefly III with resume support. |
| **CSV import pipeline** | Configurable CSV-to-Firefly III import with column role mapping and type converters. |

---

## Requirements

- Windows
- A running [Firefly III](https://www.firefly-iii.org/) instance with a Personal Access Token

---

## Developer Guide

### Build

```bash
git clone https://github.com/ErikApption/qfx-watcher.git
cd qfx-watcher
dotnet build FiiiAssist.sln
```

### Run the CLI to migrate from actual finance

```bash
dotnet run --project FiiiAssist.Cli/FiiiAssist.Cli.csproj -- --db path/to/db.sqlite --server https://firefly.example.com --token YOUR_TOKEN
```

---

## Configuration (Desktop App)

Open the **Settings** page inside the app:

| Setting | Description |
|---|---|
| **Server URL** | Base URL of your Firefly III server, e.g. `https://firefly.example.com` |
| **Personal Access Token** | Token generated in Firefly III under Profile → OAuth → Personal Access Tokens |
| **Custom watch folder** | Leave blank to use the auto-detected Edge downloads folder |
| **Default account** | Pre-select a Firefly III asset account for quick imports |
| **Confirm before importing** | Show the import wizard for each detected file |
| **Archive after import** | Move the `.qfx` file to an `imported/` sub-folder after upload |
| **Skip duplicate transactions** | Check external IDs to avoid re-importing |
| **Skip duplicates by content** | Also match by date + amount + accounts |
| **Use batch mode** | Submit transactions in bulk for performance |
| **Error on duplicate hash** | Treat Firefly III duplicate hash matches as errors |
| **Ignore SSL validation** | Bypass TLS certificate checks (for self-signed certs) |

Click **Test Connection** to verify your server and token, then settings auto-save on change.

---

## CLI Usage

The CLI imports transactions from an Actual Budget SQLite database export into Firefly III.

```
FiiiAssist.Cli --db <path> --server <url> [options]
```

| Option | Description |
|---|---|
| `--db` | Path to the Actual Budget SQLite database file (required) |
| `--server` | Firefly III server URL (required) |
| `--token` | Personal access token (or set `FIREFLY_III_TOKEN` env var) |
| `--map` | Account mapping: `ActualName=FireflyId` (repeatable). Auto-maps by name if omitted. |
| `--dry-run` | Preview what would be imported without making changes |
| `--error-on-duplicate` | Treat duplicate hashes as errors |
| `--import-rules` | Also import Actual Budget payee/category rules as Firefly III rules |
| `--ignore-ssl` | Bypass SSL certificate validation |

The CLI supports **resume** — if interrupted, re-run the same command and it will skip already-completed accounts.

---

## Projects

The solution is split into focused projects:

| Project | Type | Description |
|---|---|---|
| **FiiiAssist** | WinUI 3 (WinExe) | Desktop application — UI, view models, pages, and app-level services (file watcher, settings, QFX file tracking, bank account mappings). |
| **FiiiAssist.Core** | Class Library | Shared business logic — Firefly III service adapter, QFX parser, CSV import pipeline with configurable column converters. |
| **FiiiAssist.FireflyIII** | Class Library | NSwag-generated API client for the Firefly III REST API, plus extension methods. |
| **FiiiAssist.Cli** | Console App | Command-line importer for bulk-loading an Actual Budget SQLite export into Firefly III. |
| **FiiiAssist.Tests** | xUnit | Integration tests for the WinUI app layer (view model tests using NSubstitute). |
| **FiiiAssist.Core.Tests** | xUnit | Unit tests for core logic — QFX parser, Firefly III service, CSV converters. |

### Dependency graph

```
FiiiAssist (WinUI app)
 └── FiiiAssist.Core
      └── FiiiAssist.FireflyIII

FiiiAssist.Cli
 └── FiiiAssist.Core
      └── FiiiAssist.FireflyIII

FiiiAssist.Tests
 ├── FiiiAssist
 └── FiiiAssist.Core

FiiiAssist.Core.Tests
 └── FiiiAssist.Core
```

---

## Data Storage

All app data is stored in `%LocalAppData%\FiiiAssist\`:

| File | Purpose |
|---|---|
| `settings.json` | User configuration (server URL, token, preferences) |
| `settings.json.bak` | Automatic backup of last known-good settings |
| `bank-account-mappings.json` | Regex patterns mapping filenames to Firefly III accounts |
| `qfx-file-tracking.json` | Tracks which QFX files have been imported, skipped, or failed |

---

## License

MIT — see [LICENSE](LICENSE).
