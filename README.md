# DSH One-Click Launcher

An **unofficial** Windows launcher that sets up DeepSeek Harness (DSH) locally:
it downloads a portable Node.js runtime, installs the official
`@deepseek-ai/dsh` npm package, starts the Web UI, and opens your browser.
Self-contained — no administrator privileges required, nothing is written
outside the launcher folder (delete the folder to uninstall).

> **Disclaimer:** This is an independent community project. It is **not
> affiliated with, endorsed by, or sponsored by** DeepSeek or the DeepSeek
> Harness project. Product names are trademarks of their respective owners and
> are used here only to describe compatibility. See
> [Third-party notices](#third-party-notices) for bundled third-party material.

## Features

- **WPF GUI**: borderless light theme (blue-gradient title bar, glass cards,
  ambient glow), per-step status list, slim progress bar, live log panel, port
  and mirror settings, auto port detection, Windows 11 native rounded corners,
  screen-adaptive window size, and an embedded HarmonyOS Sans SC font so the UI
  looks identical on any PC without installing anything.
- **Headless mode** (WinForms build): `DSHLauncher.exe --auto [--port=3098]
  [--mirror] [--node-version=24.20.0]` — exit code 0 means success.
- **Latest-version lookup**: Node LTS & DSH versions from official sources,
  falling back to public mirrors automatically.
- **8-thread chunked download** over HTTP Range when the server supports it,
  single-stream otherwise.
- **Self-contained**: portable Node lives under `runtime\` next to the exe;
  delete the folder to uninstall.
- **Resilient**: auto-restarts DSH (up to 3×) on early exit; idempotent when
  components are already installed or the service is already running.

## Layout

```
src/
  wpf/       WPF GUI launcher (current, v1.4)
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
| `DSH_LAUNCHER_FORCE_MIRROR` | `1` = force mirror sources | official first |
| `DSH_LAUNCHER_PORT` | Web port (not 0) | `3080` |
| `DSH_HOME` | DSH data directory | `%USERPROFILE%\.dsh` |

## Third-party notices

- **DeepSeek Harness (DSH)** — installed from the official npm package
  `@deepseek-ai/dsh`; subject to its own license terms.
- **HarmonyOS Sans SC** — © Huawei Technologies Co., Ltd. Used under the
  [HarmonyOS Sans Font License](src/wpf/Fonts/LICENSE-SC.txt), which permits
  free commercial use, embedding and redistribution; see the license text for
  the full terms.

## License

MIT — see [LICENSE](LICENSE).
