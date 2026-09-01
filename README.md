# DSH 一键部署启动器

[![GitHub license](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](http://makeapullrequest.com)

**一键部署 · 绿色自包含 · 免管理员权限** — 让 DeepSeek Harness（DSH）在新手电脑上一键跑起来：自动下载便携版 Node.js、安装官方 `@deepseek-ai/dsh` 包、启动 Web 界面并打开浏览器。

> 非官方项目：本项目为独立社区项目，与 DeepSeek 及 DeepSeek Harness 项目无任何关联、背书或赞助关系；文中产品名称均为其各自所有者的商标，仅用于描述兼容性。

## ✨ 功能特性

1. **WPF 图形界面**：无边框浅色主题、步骤状态列表、细进度条、实时日志面板、端口与镜像设置、自动检测空闲端口、Windows 11 原生圆角、屏幕自适应尺寸。
2. **无人值守模式**：WinForms 版（`--auto` 参数，退出码 0 即成功）。
3. **联网查询最新版本**：Node LTS 与 DSH 版本，官方源失败自动切换公共镜像。
4. **8 线程分块下载**：服务器支持 HTTP Range 时并行下载，否则单流回退。
5. **自包含**：便携版 Node 位于 exe 旁的 `runtime\` 目录，删除文件夹即卸载。
6. **自动重试**：DSH 进程早退自动重启（最多 3 次）；已安装组件自动跳过、服务已在运行则直接打开页面。

## 📦 目录结构

```text
src/
  wpf/       WPF 图形界面版（当前版本，v1.4）
  csharp/    WinForms 版 + 共享核心模块（v1.0）—— Core/ 模块被两个界面共用
  *.bat      命令行版（v1.0）
```

共享核心（`Core/VersionChecker.cs`、`NodeDownloader.cs`、`DshInstaller.cs`、`WebServer.cs`、`DeployRunner.cs`、`Settings.cs`、`Log.cs`）与界面无关，两个前端共用。

## 🔧 构建

前置条件：.NET SDK 10+（Linux 下交叉构建需 `EnableWindowsTargeting`）。

```bash
# WPF 版（当前）
cd src/wpf
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o publish/win-x64

# WinForms 版（旧版）
cd src/csharp
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o publish/win-x64
```

注意：Linux 主机上构建时需设置 `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`（发布出的 Windows 程序不需要——不要在 WPF 工程里设置 `InvariantGlobalization`，会导致 XAML 文化解析出错）。

## ⚙️ 环境变量

| 变量 | 作用 | 默认值 |
|------|------|--------|
| `DSH_LAUNCHER_NODE_VERSION` | 指定 Node 版本 | 联网查询最新 LTS |
| `DSH_LAUNCHER_FORCE_MIRROR` | 设为 `1` 强制走镜像源 | 官方源优先 |
| `DSH_LAUNCHER_PORT` | Web 端口（勿设 0） | `3080` |
| `DSH_HOME` | DSH 数据目录 | `%USERPROFILE%\.dsh` |

## 📄 许可与致谢

本项目自写代码以 MIT 许可发布（详见 [LICENSE](LICENSE)）；第三方组件遵循其原始开源许可：

- **DeepSeek Harness（DSH）**：安装自官方 npm 包 `@deepseek-ai/dsh`，遵循其自身许可条款。
- **鸿蒙 Sans SC**：© 华为技术有限公司，依据 [HarmonyOS Sans Font License](src/wpf/Fonts/LICENSE-SC.txt) 使用（允许免费商用、嵌入与再分发，完整条款见许可文本）。

**致谢**：DeepSeek（DeepSeek Harness）、华为（HarmonyOS Sans）、.NET 开源社区。

## ⚠️ 免责声明

- 本项目为独立社区项目，非 DeepSeek 官方项目，与 DeepSeek 无任何关联；请勿将其包装为官方产品。
- 本启动器仅自动化安装官方 npm 包 `@deepseek-ai/dsh`，不修改、不附带任何 DeepSeek 专有资源。
- 内嵌鸿蒙 Sans SC 字体版权归华为所有，使用范围以 HarmonyOS Sans Font License 为准。

## 👤 作者

[Jimmy](https://github.com/Jimmy9946) · GitHub

欢迎提 Issue 和 Pull Request！
