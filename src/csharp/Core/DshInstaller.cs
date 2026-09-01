using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace DSHLauncher.Core;

/// <summary>
/// 用选定的 Node（系统或内置）直接跑 npm-cli.js，把 DSH 安装到
/// 启动器目录（--prefix runtime\npm-global）：免管理员、不污染系统。
/// </summary>
public static class DshInstaller
{
    public static async Task<int> InstallAsync(Settings s, NodeEnv node, bool mirror, Action<string>? output, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(node.NodeExe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Settings.AppDir,
        };
        psi.ArgumentList.Add(node.NpmCliJs);
        psi.ArgumentList.Add("install");
        psi.ArgumentList.Add("-g");
        psi.ArgumentList.Add("--prefix");
        psi.ArgumentList.Add(s.NpmGlobalDir);
        psi.ArgumentList.Add("@deepseek-ai/dsh");
        psi.ArgumentList.Add("--registry");
        psi.ArgumentList.Add(mirror ? "https://registry.npmmirror.com" : "https://registry.npmjs.org");
        psi.ArgumentList.Add("--no-fund");
        psi.ArgumentList.Add("--no-audit");
        // npm 12+ 默认阻止 install scripts；原生依赖(node-pty/koffi 等)必须放行才能构建
        psi.ArgumentList.Add("--allow-scripts=@deepseek-ai/dsh-subprocess-local,koffi,node-pty,@google/genai,protobufjs");

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data is { Length: > 0 } d) output?.Invoke(d); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 } d) output?.Invoke(d); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
        return p.ExitCode;
    }
}
