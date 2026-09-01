using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Compression;
using System.Text;

namespace DSHLauncher.Core;

public enum DeployStep
{
    DetectNode,
    DownloadNode,
    ExtractNode,
    InstallDsh,
    StartWeb,
    WaitReady,
    Done,
}

/// <summary>
/// 部署编排：查询最新版本 → 检测/下载/解压 Node → 安装 DSH → 启动 Web → 等待就绪。
/// 事件驱动进度（GUI 订阅），核心与界面完全解耦。
/// </summary>
public sealed class DeployRunner
{
    private readonly Settings _s;
    public bool Mirror { get; set; }
    public bool AutoUpdateVersion { get; set; } = true;

    public event Action<string>? LogLine;
    public event Action<DeployStep>? StepChanged;
    public event Action<DownloadProgressEventArgs>? DownloadProgress;

    public DeployRunner(Settings s, bool mirror)
    {
        _s = s;
        Mirror = mirror;
    }

    public async Task<bool> RunAsync(CancellationToken ct = default)
    {
        // 0. 联网查询最新版本
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

        // 1. 检测 Node（内置 runtime）
        StepChanged?.Invoke(DeployStep.DetectNode);
        if (File.Exists(_s.NodeExe))
        {
            var v = await GetNodeVersionAsync(_s.NodeExe, ct).ConfigureAwait(false);
            LogLine?.Invoke($"使用内置 Node: {v}");
        }
        else
        {
            // 2. 下载（首选通道失败自动切另一通道）
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

            // 3. 解压
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
        }

        // 4. 安装 / 检查 DSH
        StepChanged?.Invoke(DeployStep.InstallDsh);
        if (File.Exists(_s.DshBinJs))
        {
            LogLine?.Invoke("DSH 已安装, 跳过");
        }
        else
        {
            LogLine?.Invoke($"安装 DSH ({(Mirror ? "镜像" : "官方")}源)...");
            var code = await DshInstaller.InstallAsync(_s, Mirror, LogLine, ct).ConfigureAwait(false);
            if (code != 0 && !Mirror)
            {
                LogLine?.Invoke("官方源安装失败, 切换国内镜像重试...");
                code = await DshInstaller.InstallAsync(_s, true, LogLine, ct).ConfigureAwait(false);
            }
            if (code != 0 || !File.Exists(_s.DshBinJs))
            {
                LogLine?.Invoke("DSH 安装失败");
                return false;
            }
            LogLine?.Invoke("DSH 安装完成");
        }

        // 5. 启动 + 等待就绪（进程早退自动重启，最多 3 次，防御首启瞬态失败）
        StepChanged?.Invoke(DeployStep.StartWeb);
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
                proc = WebServer.Launch(_s);
                if (proc == null) LogLine?.Invoke("启动 DSH 失败");
            }
            await Task.Delay(2000, ct).ConfigureAwait(false);
        }
        LogLine?.Invoke("等待服务超时, 请检查日志");
        return false;

        ready:
        StepChanged?.Invoke(DeployStep.WaitReady);
        LogLine?.Invoke($"服务已就绪: {_s.Url}");
        StepChanged?.Invoke(DeployStep.Done);
        return true;
    }

    private static async Task<string> GetNodeVersionAsync(string nodeExe, CancellationToken ct)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(nodeExe)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("--version");
            using var p = System.Diagnostics.Process.Start(psi)!;
            var outp = p.StandardOutput.ReadToEndAsync(ct);
            var err = p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            return (await outp.ConfigureAwait(false)).Trim().TrimStart('v');
        }
        catch { return "?"; }
    }
}
