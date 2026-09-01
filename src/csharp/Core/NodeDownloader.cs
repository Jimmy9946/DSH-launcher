using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;

namespace DSHLauncher.Core;

public sealed class DownloadProgressEventArgs(long received, long total) : EventArgs
{
    public long Received { get; } = received;
    public long Total { get; } = total;
    public double Percent => Total > 0 ? Received * 100.0 / Total : 0;
}

/// <summary>
/// Node 免安装包下载器：官方源 / npmmirror 双通道。
/// 服务器支持 Range 时用 8 线程并行分块下载，否则回退单流下载。
/// </summary>
public static class NodeDownloader
{
    private const int ChunkCount = 8;
    private static readonly HttpClient Head = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static string NodeZipUrl(string version, bool mirror) => mirror
        ? $"https://registry.npmmirror.com/-/binary/node/v{version}/node-v{version}-win-x64.zip"
        : $"https://nodejs.org/dist/v{version}/node-v{version}-win-x64.zip";

    public static async Task DownloadAsync(string url, string zipPath, IProgress<DownloadProgressEventArgs>? progress, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        if (await HeadSupportsRangeAsync(url, ct).ConfigureAwait(false))
            await ParallelChunkDownloadAsync(url, zipPath, progress, ct).ConfigureAwait(false);
        else
            await StreamDownloadAsync(url, zipPath, progress, ct).ConfigureAwait(false);
    }

    private static async Task<bool> HeadSupportsRangeAsync(string url, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var resp = await Head.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return false;
            return resp.Headers.AcceptRanges.Contains("bytes") && resp.Content.Headers.ContentLength is > 0;
        }
        catch { return false; }
    }

    private static async Task ParallelChunkDownloadAsync(string url, string zipPath, IProgress<DownloadProgressEventArgs>? progress, CancellationToken ct)
    {
        using var probe = await Head.SendAsync(new HttpRequestMessage(HttpMethod.Head, url), ct).ConfigureAwait(false);
        var total = probe.Content.Headers.ContentLength!.Value;
        var chunk = (total + ChunkCount - 1) / ChunkCount;
        var parts = new string[ChunkCount];
        long received = 0;
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        var tasks = new List<Task>(ChunkCount);
        for (int i = 0; i < ChunkCount; i++)
        {
            var idx = i;
            var start = idx * chunk;
            var end = Math.Min(total - 1, start + chunk - 1);
            parts[idx] = zipPath + $".part{idx}";
            tasks.Add(Task.Run(async () =>
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(start, end);
                using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                await using var fs = new FileStream(parts[idx], FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var buf = new byte[1 << 20];
                int n;
                while ((n = await stream.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    await fs.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    var got = Interlocked.Add(ref received, n);
                    progress?.Report(new DownloadProgressEventArgs(got, total));
                }
            }, ct));
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
        await using (var outFs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        {
            foreach (var p in parts)
            {
                await using var part = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
                await part.CopyToAsync(outFs, ct).ConfigureAwait(false);
            }
        }
        foreach (var p in parts) File.Delete(p);
    }

    private static async Task StreamDownloadAsync(string url, string zipPath, IProgress<DownloadProgressEventArgs>? progress, CancellationToken ct)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? 0;
        await using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buf = new byte[1 << 20];
        long received = 0;
        int n;
        while ((n = await stream.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
        {
            await fs.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
            received += n;
            progress?.Report(new DownloadProgressEventArgs(received, total));
        }
    }
}
