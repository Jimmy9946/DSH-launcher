# DSH One-Click Launcher

A Windows launcher that deploys **DeepSeek Harness (DSH)** with one click:
downloads a portable Node.js runtime, installs `@deepseek-ai/dsh`, starts the
Web UI, and opens the browser — no admin rights, no system pollution.

## Features

- **WPF GUI (PCL-style skin)**: borderless dark-blue theme, step list with
  per-step states, slim progress bar, live log panel, port & mirror settings.
  Deployment starts automatically when the window opens.
- **Headless mode** (WinForms build): `DSHLauncher.exe --auto [--port=3098]
  [--mirror] [--node-version=24.20.0]` — exit code 0 means success.
- **Latest-version lookup**: Node LTS & DSH versions from official sources,
  falling back to Chinese mirrors (npmmirror) automatically.
- **8-thread chunked download** over HTTP Range when the server supports it,
  single-stream otherwise.
- **Self-contained**: portable Node lives under `runtime\` next to the exe;
  delete the folder to uninstall.
- **Resilient**: auto-restarts DSH (up to 3×) on early exit; idempotent when
  components are already installed or the service is already running.

## Layout

```
src/
  wpf/       WPF skin launcher (current, v1.1)
  csharp/    WinForms launcher + shared core (v1.0) — the Core/ modules are
             shared by both UIs
  *.bat      Command-line launcher (v1.0)
```

The shared core (`Core/VersionChecker.cs`, `NodeDownloader.cs`,
`DshInstaller.cs`, `WebServer.cs`, `DeployRunner.cs`, `Settings.cs`,
`Log.cs`) is UI-agnostic and referenced by both frontends.

## Build

Prereq: .NET SDK 10+ (cross-build from Linux works with
`EnableWindowsTargeting`).

```bash
# WPF build (current)
cd src/wpf
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o publish/win-x64

# WinForms build (legacy)
cd src/csharp
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o publish/win-x64
```

Note: on Linux hosts run the SDK with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`
(the published Windows binaries do not need it — do not set
`InvariantGlobalization` in the WPF project, it breaks XAML culture parsing).

## Environment variables

| Variable | Purpose | Default |
|---|---|---|
| `DSH_LAUNCHER_NODE_VERSION` | Pin Node version | latest LTS from network |
| `DSH_LAUNCHER_FORCE_MIRROR` | `1` = force Chinese mirrors | official first |
| `DSH_LAUNCHER_PORT` | Web port (not 0) | `3080` |
| `DSH_HOME` | DSH data directory | `%USERPROFILE%\.dsh` |

## License

MIT — see [LICENSE](LICENSE).
