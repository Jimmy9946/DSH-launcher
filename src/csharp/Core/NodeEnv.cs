using System;
using System.Diagnostics;
using System.IO;

namespace DSHLauncher.Core;

/// <summary>
/// Node 运行环境：优先复用系统已安装的 Node（不重复下载），
/// 没有或版本不足时才用启动器内置的免安装 Node。
/// DSH 一律安装到启动器目录 runtime\npm-global（--prefix），
/// 无论用哪个 Node 都不需要管理员权限、不污染系统。
/// </summary>
public sealed class NodeEnv(string nodeExe, string npmCliJs, string source)
{
    public string NodeExe { get; } = nodeExe;
    public string NpmCliJs { get; } = npmCliJs;
    /// <summary>"system" 或 "builtin"。</summary>
    public string Source { get; } = source;

    public static NodeEnv FromSystem(string nodeExe) =>
        new(nodeExe, SystemNpmCliJs(nodeExe), "system");

    public static NodeEnv FromBuiltin(string nodeExe) =>
        new(nodeExe, Path.Combine(Path.GetDirectoryName(nodeExe)!, "node_modules", "npm", "bin", "npm-cli.js"), "builtin");

    /// <summary>系统 PATH 里找 Node：存在且主版本 ≥ 22 才返回（含配套 npm-cli.js）。</summary>
    public static NodeEnv? FindSystemNode()
    {
        try
        {
            var psi = new ProcessStartInfo("where.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("node");
            using var p = Process.Start(psi);
            if (p == null) return null;
            var path = p.StandardOutput.ReadLine()?.Trim();
            p.WaitForExit(3000);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            if (!TryGetVersion(path, out var major) || major < 22) return null;
            if (!File.Exists(SystemNpmCliJs(path))) return null;
            return FromSystem(path);
        }
        catch { return null; }
    }

    /// <summary>解析 node 主版本号；失败返回 false。</summary>
    public static bool TryGetVersion(string nodeExe, out int major)
    {
        major = 0;
        try
        {
            var psi = new ProcessStartInfo(nodeExe)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("--version");
            using var p = Process.Start(psi);
            if (p == null) return false;
            var v = p.StandardOutput.ReadToEnd().Trim().TrimStart('v');
            p.WaitForExit(3000);
            var dot = v.IndexOf('.');
            return int.TryParse(dot > 0 ? v[..dot] : v, out major);
        }
        catch { return false; }
    }

    private static string SystemNpmCliJs(string nodeExe) =>
        Path.Combine(Path.GetDirectoryName(nodeExe)!, "node_modules", "npm", "bin", "npm-cli.js");
}
