using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace DSHLauncher.Core;

/// <summary>用选定的 Node 启动 dsh web 服务、等待端口就绪、打开浏览器。</summary>
public static class WebServer
{
    public static bool IsPortOpen(int port)
    {
        try
        {
            using var c = new TcpClient();
            c.Connect("127.0.0.1", port);
            return true;
        }
        catch { return false; }
    }

    public static async Task<bool> WaitReadyAsync(int port, TimeSpan timeout, CancellationToken ct = default)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (IsPortOpen(port)) return true;
            await Task.Delay(2000, ct).ConfigureAwait(false);
        }
        return IsPortOpen(port);
    }

    public static Process? Launch(Settings s, NodeEnv node)
    {
        var psi = new ProcessStartInfo(node.NodeExe)
        {
            UseShellExecute = false,
            WorkingDirectory = s.EffectiveWorkspace,
        };
        psi.ArgumentList.Add(s.DshBinJsGlobal);
        psi.ArgumentList.Add("web");
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(s.Port.ToString());
        psi.EnvironmentVariables["DSH_HOME"] = s.EffectiveDshHome;
        if (s.ForceMirror)
            psi.EnvironmentVariables["npm_config_registry"] = "https://registry.npmmirror.com";
        try { return Process.Start(psi); }
        catch { return null; }
    }

    public static void OpenBrowser(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
