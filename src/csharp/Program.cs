using System.Runtime.InteropServices;

namespace DSHLauncher;

public static class Program
{
    public static bool ConsoleAttached;

    [STAThread]
    public static int Main(string[] args)
    {
        var s = Settings.Load();
        var mirror = s.ForceMirror;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--mirror") { mirror = true; continue; }
            if (a == "--port" && i + 1 < args.Length && int.TryParse(args[++i], out var p2)) { s.Port = p2; continue; }
            if (a.StartsWith("--port=", StringComparison.OrdinalIgnoreCase) && int.TryParse(a["--port=".Length..], out var p))
            { s.Port = p; continue; }
            if (a.StartsWith("--node-version=", StringComparison.OrdinalIgnoreCase) && a.Length > "--node-version=".Length)
            { s.NodeVersion = a["--node-version=".Length..]; continue; }
        }

        Log.Init(s.LogFile);

        if (args.Contains("--auto"))
        {
            AttachConsole();
            Log.Info("auto mode start");
            var ok = RunAutoAsync(s, mirror).GetAwaiter().GetResult();
            Log.Info(ok ? "auto mode done: OK" : "auto mode done: FAILED");
            return ok ? 0 : 1;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new UI.MainForm(s, mirror));
        return 0;
    }

    private static async Task<bool> RunAutoAsync(Settings s, bool mirror)
    {
        var runner = new Core.DeployRunner(s, mirror);
        runner.LogLine += m => Console.WriteLine(m);
        runner.DownloadProgress += e =>
        {
            if (e.Total > 0)
                Console.Write($"\r下载: {e.Percent:F1}% ({e.Received / 1048576.0:F1}/{e.Total / 1048576.0:F1} MB)   ");
        };
        var ok = await runner.RunAsync();
        Console.WriteLine();
        if (ok)
        {
            Console.WriteLine();
            Console.WriteLine($"部署完成! DSH Web: {s.Url}");
            Console.WriteLine($"  - DSH 数据目录: {s.EffectiveDshHome}");
            Console.WriteLine($"  - 关闭 DSH Web 进程即停止服务");
        }
        return ok;
    }

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    private static void AttachConsole()
    {
        try
        {
            if (AllocConsole())
            {
                Log.ConsoleAttached = true;
                Console.OutputEncoding = System.Text.Encoding.UTF8;
            }
        }
        catch { }
    }
}
