# DXP Log Viewer

WPF desktop app for browsing and filtering Azure App Service (Optimizely DXP) diagnostic logs exported as `PT1H.json` files (JSON lines).

## Requirements

- Windows
- .NET 10 SDK

## Build and run

```powershell
dotnet run
```

Paths can be passed as command-line arguments to load them on startup:

```powershell
dotnet run -- "D:\path\to\Log_folder"
```

## Loading logs

- **Open Files** – select one or more `.json`, `.log` or `.txt` files.
- **Open Folder** – recursively loads every `*.json` file in the folder.
- **Drag and drop** – drop files or folders onto the window.
- **Clear** – removes all loaded entries.

Each line is parsed as a JSON object. The `resultDescription` field is split into severity (e.g. `[09:15:02 ERR]`), source (class name) and message.

## Filtering

- Dropdowns: severity, source, category, container, host.
- Time range: `From` / `To` accept full date-times or a time of day (e.g. `09:30`), applied to the earliest loaded day.
- Search box (case-insensitive):
  - `word` – match anywhere in the entry
  - `"exact phrase"` – quoted phrase
  - `-word` – exclude entries that match
  - `field:value` – match a specific field (`severity`/`sev`, `source`/`src`, `message`/`msg`, `time`, `file`, or any raw JSON field)
- Regex checkbox: treats the whole search text as a regular expression. Invalid patterns turn the box red.
- Reset restores all filters.

Terms are combined with AND.
