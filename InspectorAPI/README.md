# REST Client

A Postman-like HTTP client built with Avalonia UI and .NET 10. Works on Windows, macOS, and Linux.

## Project Structure

```
RestClient/
├── RestClient.sln
├── InspectorAPI.Core/          # Platform-agnostic models, services, viewmodels
│   ├── Models/               # Data models (Collection, Request, Response, etc.)
│   ├── Services/             # HTTP sending + JSON-file collection persistence
│   └── ViewModels/           # MVVM ViewModels (CommunityToolkit.Mvvm)
└── InspectorAPI.Desktop/       # Avalonia 11 desktop application
    ├── Converters/           # AXAML value converters
    └── Views/                # MainWindow.axaml + code-behind
```

## Features

- **Send HTTP Requests**: GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS
- **Request Configuration**:
  - URL bar with method selector
  - Headers editor (key/value pairs with enable/disable toggles)
  - Query parameters editor
  - Request body editor with content-type selector
- **Response Viewer**:
  - Status code with color-coded success/error indicator
  - Response time and size
  - Body (JSON auto-formatted) and Headers tabs
- **Collections**:
  - Create collections and nested folders
  - Save requests to any collection/folder
  - Open saved requests in tabs
  - Delete requests and collections
  - Collections persisted as JSON in your AppData directory
- **Multi-tab interface**: Open multiple requests simultaneously

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Running

```bash
cd RestClient
dotnet run --project InspectorAPI.Desktop
```

## Building for release

```bash
# Windows
dotnet publish InspectorAPI.Desktop -c Release -r win-x64 --self-contained

# macOS
dotnet publish InspectorAPI.Desktop -c Release -r osx-x64 --self-contained

# Linux
dotnet publish InspectorAPI.Desktop -c Release -r linux-x64 --self-contained
```

## Collections storage

Collections are stored as JSON files in:
- **Windows**: `%APPDATA%\RestClient\collections\`
- **macOS**: `~/Library/Application Support/RestClient/collections/`
- **Linux**: `~/.config/RestClient/collections/`

## WebAssembly (optional)

To add browser/WASM support, create a third project `RestClient.Browser` targeting `net10.0-browser` with the `Avalonia.Browser` package and a browser-compatible `Program.cs`:

```csharp
// RestClient.Browser/Program.cs
using Avalonia;
using InspectorAPI.Desktop;  // or a shared App class

await BuildAvaloniaApp().StartBrowserAppAsync("out");

static AppBuilder BuildAvaloniaApp()
    => AppBuilder.Configure<App>().WithInterFont();
```

> Note: The `HttpRequestService` uses `HttpClientHandler` with `ServerCertificateCustomValidationCallback` which is not available in the browser runtime. Override with a browser-compatible service implementation.
