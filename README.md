# InspectorAPI

A cross-platform desktop HTTP client built with [Avalonia UI](https://avaloniaui.net/). Organise requests into collections, send them, inspect responses, and save everything locally — no account required.

---

## Features

- **Tabbed requests** — open multiple requests side by side, duplicate tabs, quick-save
- **Collections & folders** — organise saved requests, rename, delete, import/export
- **Query params ↔ URL sync** — edit either the URL bar or the params panel; both stay in sync
- **Request headers** — autocomplete on common HTTP header names, pre-populated defaults
- **Response viewer** — formatted JSON, raw view, response headers, response time & size
- **Import / Export** — native JSON format and Postman v2.1 collections
- **Light & dark themes** — follows the system preference via Avalonia FluentTheme

---

## Dependencies

### Runtime

| Dependency | Version | Purpose |
|---|---|---|
| [.NET](https://dotnet.microsoft.com/download) | **10.0** | Runtime and SDK |
| [Avalonia](https://avaloniaui.net/) | 11.2.3 | Cross-platform UI framework |
| [Avalonia.Themes.Fluent](https://www.nuget.org/packages/Avalonia.Themes.Fluent) | 11.2.3 | Fluent design theme (light & dark) |
| [Avalonia.Fonts.Inter](https://www.nuget.org/packages/Avalonia.Fonts.Inter) | 11.2.3 | Inter font family |
| [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) | 8.3.2 | MVVM source generators (`ObservableProperty`, `RelayCommand`, …) |

NuGet packages are restored automatically on build — no manual installation is needed beyond the .NET SDK.

### Platform requirements

| Platform | Notes |
|---|---|
| **Windows** 10 / 11 | Native Win32 backend |
| **macOS** 12+ | Native AppKit backend |
| **Linux** | X11 or Wayland; requires a working desktop environment |

---

## Building from the command line

### 1 — Install the .NET 10 SDK

Download and install the SDK for your platform from <https://dotnet.microsoft.com/download/dotnet/10.0>.

Verify the installation:

```bash
dotnet --version
# should print 10.x.x
```

### 2 — Clone the repository

```bash
git clone https://github.com/jsiulian/InspectorAPI.git
cd InspectorAPI
```

### 3 — Restore NuGet packages

```bash
dotnet restore
```

### 4 — Run in development mode

```bash
dotnet run --project InspectorAPI.Desktop
```

This builds in Debug configuration (includes the Avalonia visual debugger overlay on `F12`).

### 5 — Build a release binary

```bash
dotnet build -c Release
```

### 6 — Publish a self-contained executable

Replace `<RID>` with the [runtime identifier](https://learn.microsoft.com/dotnet/core/rid-catalog) for your target platform:

| Platform | RID |
|---|---|
| Windows x64 | `win-x64` |
| Windows ARM64 | `win-arm64` |
| macOS Apple Silicon | `osx-arm64` |
| macOS Intel | `osx-x64` |
| Linux x64 | `linux-x64` |
| Linux ARM64 | `linux-arm64` |

```bash
dotnet publish InspectorAPI.Desktop -c Release -r <RID> --self-contained true
```

The output lands in:

```
InspectorAPI.Desktop/bin/Release/net10.0/<RID>/publish/
```

---

## Project structure

```
InspectorAPI/
├── InspectorAPI.sln
├── InspectorAPI.Core/          # Platform-independent logic
│   ├── Models/                 # Data models (Collection, SavedRequest, …)
│   ├── Services/               # HTTP client, collection persistence, Postman converter
│   └── ViewModels/             # MVVM view models (MainViewModel, RequestTabViewModel, …)
└── InspectorAPI.Desktop/       # Avalonia desktop application
    ├── Assets/                 # Application icon
    ├── Converters/             # Value converters (method badge colours, …)
    └── Views/
        ├── MainWindow.axaml    # Single-window UI — layout, styles, theme resources
        └── MainWindow.axaml.cs # Code-behind (file picker, keyboard shortcuts, focus)
```

---

## Data storage

Collections are saved as JSON files in the platform application-data directory:

| Platform | Path |
|---|---|
| Windows | `%APPDATA%\InspectorAPI\collections\` |
| macOS | `~/Library/Application Support/InspectorAPI/collections/` |
| Linux | `~/.config/InspectorAPI/collections/` (or `$XDG_CONFIG_HOME/InspectorAPI/collections/`) |
