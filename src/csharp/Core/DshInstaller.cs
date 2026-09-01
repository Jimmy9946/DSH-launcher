using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Diagnostics;

namespace DSHLauncher.Core;

/// <summary>用内置 Node 直接跑 npm-cli.js 安装 DSH（不依赖 cmd/系统 npm，自包含）。</summary>
public static class DshInstaller
{
    public static async Task<int> InstallAsync(Settings s, bool mirror, Action<string>? output, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(s.NodeExe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Settings.AppDir,
        };
        psi.ArgumentList.Add(s.NpmCliJs);
        psi.ArgumentList.Add("install");
        psi.ArgumentList.Add("-g");
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
