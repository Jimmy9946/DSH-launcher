using System;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;

namespace DSHLauncher.Core;

/// <summary>联网查询最新版本：Node LTS（nodejs.org / npmmirror）与 DSH（npm registry / 镜像）。</summary>
public static class VersionChecker
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };

    /// <summary>最新 Node LTS 版本号，全部通道失败返回默认值。</summary>
    public static async Task<string> LatestNodeLtsAsync(bool mirror, CancellationToken ct = default)
    {
        var urls = mirror
            ? new[] { "https://registry.npmmirror.com/-/binary/node/index.json", "https://nodejs.org/dist/index.json" }
            : new[] { "https://nodejs.org/dist/index.json", "https://registry.npmmirror.com/-/binary/node/index.json" };
        foreach (var u in urls)
        {
            try
            {
                var json = await Http.GetStringAsync(u, ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.TryGetProperty("lts", out var lts) && lts.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(lts.GetString()))
                        return el.GetProperty("version").GetString()!.TrimStart('v');
                }
            }
            catch { }
        }
        return "22.23.2";
    }

    /// <summary>最新 DSH 版本号，全部通道失败返回默认值。</summary>
    public static async Task<string> LatestDshAsync(bool mirror, CancellationToken ct = default)
    {
        var urls = mirror
            ? new[] { "https://registry.npmmirror.com/@deepseek-ai/dsh/latest", "https://registry.npmjs.org/@deepseek-ai/dsh/latest" }
            : new[] { "https://registry.npmjs.org/@deepseek-ai/dsh/latest", "https://registry.npmmirror.com/@deepseek-ai/dsh/latest" };
        foreach (var u in urls)
        {
            try
            {
                var json = await Http.GetStringAsync(u, ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("version", out var v))
                    return v.GetString()!;
            }
            catch { }
        }
        return "0.1.1-rc.2";
    }
}
