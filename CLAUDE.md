# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

KQL Pad is a Windows WPF desktop app (.NET 10, `net10.0-windows`) for keeping a library of parameterized KQL queries and running them against Azure Log Analytics workspaces belonging to multiple clients/tenants. There are no tests and no solution file; the only project is `KqlPad/KqlPad.csproj`.

- `KqlPad/` — source
- `dist/` — local publish output (gitignored); the exe is distributed as a GitHub release asset
- `preview/` — screenshots of the UI

## Commands

Run from `KqlPad/`:

```powershell
dotnet build
dotnet run
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o ..\dist
```

The publish line produces the ~66 MB `dist/KqlPad.exe` attached to GitHub releases; without `EnableCompressionInSingleFile` it comes out around 143 MB. There is no publish profile in the repo. `WPF0001` (experimental Fluent theming) is suppressed in the csproj.

## Architecture

**Composition.** `App.xaml.cs` holds three static singletons — `App.Library` (`LibraryStore`), `App.Profiles` (`ProfileStore`), `App.Client` (`LogAnalyticsClient`) — loaded in `OnStartup`. Views and `MainViewModel` reach them directly; there is no DI container. `MainWindow.xaml.cs` is a code-behind that handles menu/tree commands (new/edit/duplicate/delete query, categories, run/cancel, CSV export, portal link) and calls into `MainViewModel` (MVVM-lite with `Infrastructure/Observable`).

**On-disk state** lives under `%APPDATA%\KqlPad` (`Infrastructure/AppPaths.cs`):
- `queries/*.json` — the query library, one file per category. This folder is the single source of truth.
- `profiles.json` — client profiles (tenant ID, workspace ID, optional workspace resource ID, login hint, app client ID).
- `auth/` — Azure.Identity `AuthenticationRecord` files, one per profile.
- `.seeded` — marker that the starter library has been copied once.

**Query library (`Services/LibraryStore.cs`).** `Starter/*.json` are embedded resources; on first run (no `.seeded` marker) they're written into `queries/`. After that, deleting a starter query does not bring it back unless the user runs "Restore starters", which merges missing categories/queries by category name and query `id`. File names are slugs of the category name; renaming a category rewrites the file and deletes the old one. Load errors per file are collected in `LoadErrors` rather than thrown.

**Query JSON format (`Models/Models.cs`).** `QueryCategory { category, description, queries[] }`, each `QueryDef { id, name, description, tags, parameters[], kql }`. `kql` is serialized as an **array of lines** via `KqlLinesConverter` (a plain string is also accepted on read) to keep files diff-friendly. All JSON goes through `AppJson.Options` (camelCase, comments/trailing commas allowed, defaults omitted).

**Templating (`Services/Templating.cs`).** Placeholders are `{{name}}` (square brackets are avoided because they're real KQL). Values are escaped for safe use inside `'...'` or `"..."` literals unless the `ParamDef` has `raw: true` (for numbers, lists, timespans like `ago({{days}}d)`). `MainViewModel` remembers values by parameter name across queries, so `{{upn}}` carries over. The query editor's "Detect" adds `ParamDef`s for tokens found in the KQL.

**Running queries (`Services/LogAnalyticsClient.cs`).** Uses `InteractiveBrowserCredential` per profile with persistent token cache name `KqlPad`, POSTs to `https://api.loganalytics.io/v1/workspaces/{workspaceId}/query`, and parses only the first result table into a `DataTable` whose columns are named `c0, c1, …` (real names kept in `QueryResult.ColumnNames`) so arbitrary KQL column names can't break WPF bindings. Datetimes are formatted as UTC strings.
- Credentials are cached in memory by `profileId|AuthKey`, where `AuthKey` = tenant + clientId + loginHint. Changing any of those invalidates the saved sign-in for that profile only.
- Auth record files are named `hash(profileId)-hash(AuthKey).json`. Older versions used `hash(AuthKey).json` shared across profiles; that legacy file is migrated on read and only deleted on "Forget sign-in" if no other profile shares the same `AuthKey`. Preserve this per-profile isolation when touching auth.
- 401/403 errors get an explanatory hint about Global Admin not granting Azure RBAC (needs Log Analytics Reader).

**Profiles (`Services/ProfileStore.cs`).** Auto-saves on every collection or property change (atomic temp-file + move). A corrupt `profiles.json` is copied to `profiles.json.bad` rather than overwritten.

**Portal link (`Services/PortalLink.cs`).** Builds an Azure portal Logs deep link using the gzip+base64 encoding of the portal's "Share link to query"; requires the profile's `WorkspaceResourceId`.

**UI/theme.** `Theme.xaml` holds a dark neon/retro style; `Views/RetroBackdrop.cs` draws the background, `Infrastructure/ConsoleChrome.cs` colors the title bar via DWM attributes, and `Views/KqlHighlighter.cs` does simple KQL syntax coloring in a `RichTextBox`.
