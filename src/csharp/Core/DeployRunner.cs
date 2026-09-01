using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Compression;

namespace DSHLauncher.Core;

public enum DeployStep
{
    DetectEnv,
    DownloadNode,
    ExtractNode,
    InstallDsh,
    Ready,
}

/// <summary>
/// 部署编排（v1.2）：先检查环境（系统 Node 优先复用），按需下载/安装，
/// 然后由用户手动启动服务。事件驱动进度（GUI 订阅），核心与界面完全解耦。
/// </summary>
public sealed class DeployRunner
{
    private readonly Settings _s;
    public bool Mirror { get; set; }
    public bool AutoUpdateVersion { get; set; } = true;

    /// <summary>Prepare 后选定的 Node 环境（system 或 builtin）。</summary>
    public NodeEnv? CurrentNode { get; private set; }

    public event Action<string>? LogLine;
    public event Action<DeployStep>? StepChanged;
    public event Action<DownloadProgressEventArgs>? DownloadProgress;

    public DeployRunner(Settings s, bool mirror)
    {
        _s = s;
        Mirror = mirror;
    }

    /// <summary>第一步：检查环境 + 按需下载/安装（不启动服务）。</summary>
    public async Task<bool> PrepareAsync(CancellationToken ct = default)
    {
        // 0. 联网查询最新版本（仅用于下载时的版本选择与提示）
        if (AutoUpdateVersion)
        {
            LogLine?.Invoke("正在查询最新版本...");
            try
            {
                var node = await VersionChecker.LatestNodeLtsAsync(Mirror, ct).ConfigureAwait(false);
                var dsh = await VersionChecker.LatestDshAsync(Mirror, ct).ConfigureAwait(false);
                LogLine?.Invoke($"最新版本: Node {node} / DSH {dsh}");
                _s.NodeVersion = node;
            }
            catch
            {
                LogLine?.Invoke("版本查询失败, 使用默认版本");
            }
        }

        // 1. 环境检测：系统 Node 优先，其次内置，最后下载
        StepChanged?.Invoke(DeployStep.DetectEnv);
        var sys = NodeEnv.FindSystemNode();
        if (sys != null)
        {
            NodeEnv.TryGetVersion(sys.NodeExe, out var major);
            LogLine?.Invoke($"检测到系统 Node v{major}：直接复用，无需下载");
            CurrentNode = sys;
        }
        else if (File.Exists(_s.NodeExe) && NodeEnv.TryGetVersion(_s.NodeExe, out var builtinMajor) && builtinMajor >= 22)
        {
            LogLine?.Invoke($"使用内置 Node v{builtinMajor}");
            CurrentNode = NodeEnv.FromBuiltin(_s.NodeExe);
        }
        else
        {
            LogLine?.Invoke("未检测到可用 Node（需要 v22 以上），下载内置版本...");
            StepChanged?.Invoke(DeployStep.DownloadNode);
            var zip = Path.Combine(_s.RuntimeDir, $"node-v{_s.NodeVersion}-win-x64.zip");
            var prog = new Progress<DownloadProgressEventArgs>(e => DownloadProgress?.Invoke(e));
            var ok = false;
            var attempts = new[] { Mirror, !Mirror };
            foreach (var mirror in attempts)
            {
                var url = NodeDownloader.NodeZipUrl(_s.NodeVersion, mirror);
                LogLine?.Invoke($"下载 Node v{_s.NodeVersion} ({(mirror ? "国内镜像" : "官方源")})...");
                try
                {
                    await NodeDownloader.DownloadAsync(url, zip, prog, ct).ConfigureAwait(false);
                    ok = true;
                    break;
                }
                catch (Exception ex)
                {
                    LogLine?.Invoke($"下载失败: {ex.Message}");
                    try { File.Delete(zip); } catch { }
                }
            }
            if (!ok) return false;

            StepChanged?.Invoke(DeployStep.ExtractNode);
            LogLine?.Invoke("解压 Node...");
            Directory.CreateDirectory(_s.RuntimeDir);
            ZipFile.ExtractToDirectory(zip, _s.RuntimeDir);
            File.Delete(zip);
            if (!File.Exists(_s.NodeExe))
            {
                LogLine?.Invoke("解压后未找到 node.exe");
                return false;
            }
            LogLine?.Invoke($"Node {_s.NodeVersion} 就绪");
            CurrentNode = NodeEnv.FromBuiltin(_s.NodeExe);
        }

        // 2. DSH：统一安装到启动器目录 runtime\npm-global（免管理员）
        StepChanged?.Invoke(DeployStep.InstallDsh);
        if (File.Exists(_s.DshBinJsGlobal))
        {
            LogLine?.Invoke("DSH 已安装, 跳过");
        }
        else
        {
            LogLine?.Invoke($"安装 DSH ({(Mirror ? "镜像" : "官方")}源)...");
            var code = await DshInstaller.InstallAsync(_s, CurrentNode, Mirror, LogLine, ct).ConfigureAwait(false);
            if (code != 0 && !Mirror)
            {
                LogLine?.Invoke("官方源安装失败, 切换国内镜像重试...");
                code = await DshInstaller.InstallAsync(_s, CurrentNode, true, LogLine, ct).ConfigureAwait(false);
            }
            if (code != 0 || !File.Exists(_s.DshBinJsGlobal))
            {
                LogLine?.Invoke("DSH 安装失败");
                return false;
            }
            LogLine?.Invoke("DSH 安装完成");
        }

        StepChanged?.Invoke(DeployStep.Ready);
        return true;
    }

    /// <summary>第二步：启动 DSH Web 并等待就绪（用户手动触发）。</summary>
    public async Task<bool> StartWebAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_s.DshBinJsGlobal))
        {
            LogLine?.Invoke("环境未就绪，请先完成部署");
            return false;
        }
        var node = NodeEnv.FindSystemNode() ?? (File.Exists(_s.NodeExe) ? NodeEnv.FromBuiltin(_s.NodeExe) : null);
        if (node == null)
        {
            LogLine?.Invoke("找不到可用的 Node，请先完成部署");
            return false;
        }

        Directory.CreateDirectory(_s.EffectiveWorkspace);
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(6);
        System.Diagnostics.Process? proc = null;
        var restartCount = 0;
        while (DateTime.UtcNow < deadline)
        {
            if (WebServer.IsPortOpen(_s.Port)) goto ready;
            if (proc == null || proc.HasExited)
            {
                if (restartCount >= 3) break;
                restartCount++;
                if (proc != null) LogLine?.Invoke("DSH 进程退出, 正在重新启动...");
                else LogLine?.Invoke($"启动 DSH Web (端口 {_s.Port})...");
                proc = WebServer.Launch(_s, node);
                if (proc == null) LogLine?.Invoke("启动 DSH 失败");
            }
            await Task.Delay(2000, ct).ConfigureAwait(false);
        }
        LogLine?.Invoke("等待服务超时, 请检查日志");
        return false;

        ready:
        LogLine?.Invoke($"服务已就绪: {_s.Url}");
        return true;
    }
}
