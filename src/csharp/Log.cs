using System;
using System.IO;
namespace DSHLauncher;

/// <summary>轻量日志：文件 + 事件订阅（GUI 面板）+ 控制台三路输出。</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _file;
    public static event Action<string>? Line;

    /// <summary>--auto 模式时由入口程序置为 true，日志同时输出到控制台。</summary>
    public static bool ConsoleAttached { get; set; }

    public static void Init(string file)
    {
        _file = file;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.AppendAllText(file, $"\n===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====\n");
        }
        catch { }
    }

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg) => Write("ERROR", msg);

    private static void Write(string level, string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {level} {msg}";
        lock (Gate)
        {
            try { if (_file != null) File.AppendAllText(_file, line + "\n"); } catch { }
            Line?.Invoke(line);
            if (ConsoleAttached) Console.WriteLine(line);
        }
    }
}
