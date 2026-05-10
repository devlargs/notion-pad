# Notion Pad

Your everyday notepad — but everything saves to Notion automatically. A lightweight Windows desktop app built with WPF on .NET 8.

## Why

Notion is a great place to keep things, but its editor is heavy and the desktop app eats RAM. Notion Pad gives you a fast, no-friction text window that mirrors every keystroke (debounced) into a Notion database in the background. Your notes live locally as JSON first; Notion is the mirror.

## Features

- One Notion database, one page per note (uses the database's `Name` title property)
- First non-empty line of the note becomes the page title
- 1.5 s debounced autosave with a per-note serial sync queue
- Offline-friendly: local JSON is the source of truth; failed syncs queue and retry
- Per-note sync indicator (Synced / Pending / Syncing / Error) with inline error banner
- Settings dialog with **Test connection** so you know the integration works before saving
- Dark, minimal UI
- Tiny footprint compared to Electron-based notepads (no Chromium runtime)

## Requirements

- Windows 10/11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (for prebuilt binaries)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (only needed if you build from source)
- A Notion account + an internal integration

## Notion setup (one-time)

1. Go to <https://www.notion.so/my-integrations> and click **+ New integration**.
2. Name it (e.g. "Notion Pad"), select the workspace, and create it.
3. Copy the **Internal Integration Secret** — that's your token.
4. In Notion, create a database. The screenshot used while designing the app had a single `Name` (title) column — that's all you need. If you add more columns, Notion Pad just ignores them.
5. Open the database, click the **•••** menu → **Connections** → add the integration you just created. (Without this step, Notion will return `object_not_found`.)
6. Copy the database ID from its URL. It's the 32-character string between the workspace slug and the `?v=` query:
   ```
   https://www.notion.so/<workspace>/<DATABASE_ID>?v=...
   ```

## Run from source

```powershell
dotnet run
```

That's it — the first build restores NuGet packages and launches the window. On first launch, the Settings dialog blocks until you paste your integration token and database ID.

Hot-reload during development:

```powershell
dotnet watch run
```

## Build a distributable

Framework-dependent (small, requires .NET 8 Desktop Runtime on the target machine):

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

Self-contained (larger, runs on any Win10/11 box):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

Output lands in `bin/Release/net8.0-windows/win-x64/publish/NotionPad.exe`.

## How it works

```
+------------------ WPF UI ------------------+
|  MainWindow: sidebar (notes) + editor pane |
|  SettingsWindow (first-run + edit)         |
+--------------------------------------------+
                |
                v
+------------------ Services ----------------+
|  LocalStore   atomic JSON in %AppData%     |
|  NotionClient HttpClient -> api.notion.com |
|  SyncQueue    per-note serial Task chain   |
+--------------------------------------------+
```

- **Local store** lives at `%AppData%\NotionPad\notion-pad.json` and is rewritten atomically (tmp + rename) on a 200 ms debounce.
- **Title derivation**: first non-empty line of the body, trimmed and truncated to 200 chars.
- **Body → blocks**: split by blank lines; each paragraph becomes a Notion `paragraph` block (chunked at 2000 chars to respect Notion's per-block limit).
- **Sync queue**: each note has its own serial chain of `Task`s — a new save waits for the previous one to finish, so the most recent body always lands last and you don't race `delete children` against `append children`.
- **Updates** patch the title property, delete all existing children blocks, then append fresh ones. (Simple and correct; not the most efficient for very long notes.)
- **Delete** archives the Notion page (sets `archived: true`) and removes the local entry.

## Project layout

```
NotionPad.csproj
App.xaml / App.xaml.cs
app.manifest
Models/
  Note.cs        Settings.cs
Services/
  LocalStore.cs  NotionClient.cs  SyncQueue.cs
Views/
  Theme.xaml
  MainWindow.xaml(.cs)
  SettingsWindow.xaml(.cs)
  SyncStateConverters.cs
```

## Troubleshooting

- **`name is not a property that exists`** — your database's title column isn't called `Name`. Rename it in Notion or update `TitleProperties` in `Services/NotionClient.cs`.
- **`object_not_found`** — the integration hasn't been shared with the database. Open the DB → ••• → Connections → add it.
- **`API token is invalid`** — re-copy the integration secret; tokens start with `secret_` or `ntn_`.
- **The build can't replace `NotionPad.exe`** — close the running window first; the executable is locked while the app is open.

## License

MIT
