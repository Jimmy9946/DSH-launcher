using System;
using System.IO;
using System.Text.Json;

namespace DSHLauncher;

/// <summary>启动器设置：持久化在启动器目录 settings.json，环境变量可覆盖。</summary>
public sealed class Settings
{
    public string NodeVersion { get; set; } = "22.23.2";
    public int Port { get; set; } = 3080;
    public bool ForceMirror { get; set; }
    public string DshHome { get; set; } = "";
    public string Workspace { get; set; } = "";

    public static string AppDir => AppContext.BaseDirectory;

    public string RuntimeDir => Path.Combine(AppDir, "runtime");
    public string NodeDirName => $"node-v{NodeVersion}-win-x64";
    public string NodeHome => Path.Combine(RuntimeDir, NodeDirName);
    public string NodeExe => Path.Combine(NodeHome, "node.exe");
    public string NpmCliJs => Path.Combine(NodeHome, "node_modules", "npm", "bin", "npm-cli.js");
    public string DshBinJs => Path.Combine(NodeHome, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
    public string LogDir => Path.Combine(AppDir, "logs");
    public string LogFile => Path.Combine(LogDir, "launcher.log");
    public string SettingsFile => Path.Combine(AppDir, "settings.json");

    public string EffectiveDshHome =>
        string.IsNullOrEmpty(DshHome) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh") : DshHome;

    public string EffectiveWorkspace =>
        string.IsNullOrEmpty(Workspace) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DSH-Workspace") : Workspace;

    public string Url => $"http://127.0.0.1:{Port}";

    public static Settings Load()
    {
        var s = new Settings();
        try
        {
            var f = Path.Combine(AppDir, "settings.json");
            if (File.Exists(f))
                s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(f)) ?? s;
        }
        catch { }
        if (Environment.GetEnvironmentVariable("DSH_LAUNCHER_PORT") is { Length: > 0 } p && int.TryParse(p, out var port)) s.Port = port;
        if (Environment.GetEnvironmentVariable("DSH_LAUNCHER_NODE_VERSION") is { Length: > 0 } nv) s.NodeVersion = nv;
        if (Environment.GetEnvironmentVariable("DSH_LAUNCHER_FORCE_MIRROR") == "1") s.ForceMirror = true;
        if (Environment.GetEnvironmentVariable("DSH_HOME") is { Length: > 0 } dh) s.DshHome = dh;
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppDir);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
