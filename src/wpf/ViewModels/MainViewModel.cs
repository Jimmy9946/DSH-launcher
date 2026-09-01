using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using DSHLauncher.Core;

namespace DSHLauncher.ViewModels;

public enum StepState { Pending, Running, Done, Failed }

public sealed class StepItem(string title) : INotifyPropertyChanged
{
    public string Title { get; } = title;

    private StepState _state;
    public StepState State
    {
        get => _state;
        set { _state = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class LogItem(string text)
{
    public string Text { get; } = text;
}

/// <summary>
/// 主窗口数据模型（v1.2 流程）：打开窗口自动「检查环境 + 按需下载/安装」，
/// 完成后由用户手动点击「启动服务」。
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly Settings _s = Settings.Load();
    private DeployRunner? _runner;
    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _ready;

    public ObservableCollection<StepItem> Steps { get; } =
    [
        new("检测环境"), new("下载组件"), new("安装 DSH"), new("就绪"),
    ];

    public ObservableCollection<LogItem> Logs { get; } = [];

    public string AppVersion => $"v1.2  ·  Node {_s.NodeVersion}";

    private string _statusText = "正在检查环境...";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    private string _statusColor = "#1E88E5";
    public string StatusColor
    {
        get => _statusColor;
        set { _statusColor = value; OnPropertyChanged(); }
    }

    private double _progress;
    public double Progress
    {
        get => _progress;
        set { _progress = value; OnPropertyChanged(); }
    }

    private string _progressText = "";
    public string ProgressText
    {
        get => _progressText;
        set { _progressText = value; OnPropertyChanged(); }
    }

    public int Port
    {
        get => _s.Port;
        set { _s.Port = value; _s.Save(); OnPropertyChanged(); }
    }

    public bool IsMirror
    {
        get => _s.ForceMirror;
        set { _s.ForceMirror = value; _s.Save(); OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _busy;
        set { _busy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanAct)); }
    }

    public bool CanAct => !_busy;

    public bool IsReady
    {
        get => _ready;
        private set { _ready = value; OnPropertyChanged(); OnPropertyChanged(nameof(MainButtonText)); }
    }

    /// <summary>主按钮文案：未就绪=重新部署，就绪=启动服务。</summary>
    public string MainButtonText => _busy ? "处理中…" : _ready ? "启动服务" : "重新部署";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>自动执行：检查环境 + 按需下载/安装（不启动服务）。</summary>
    public async Task PrepareAsync()
    {
        if (_busy) return;
        BeginBusy();
        _cts = new CancellationTokenSource();
        _s.Save();

        foreach (var s in Steps) s.State = StepState.Pending;
        Logs.Clear();
        AppendLog($"===== 环境检查 (端口 {_s.Port}) =====");
        if (_s.LastPort > 0)
            AppendLog($"上一次运行端口: {_s.LastPort}");
        SetStatus("正在检查环境...", "#1E88E5");

        _runner = new DeployRunner(_s, _s.ForceMirror);
        WireRunner(_runner);

        var ok = await _runner.PrepareAsync(_cts.Token);
        if (ok)
        {
            foreach (var s in Steps) s.State = StepState.Done;
            IsReady = true;
            SetStatus("环境就绪，点击「启动服务」", "#43A047");
            AppendLog("===== 环境就绪 =====");
        }
        else
        {
            foreach (var s in Steps)
                if (s.State == StepState.Running) s.State = StepState.Failed;
            IsReady = false;
            SetStatus("部署失败，请查看日志", "#E53935");
            AppendLog("===== 部署失败 =====");
        }
        EndBusy();
    }

    /// <summary>用户手动触发：启动 DSH Web 并打开浏览器。</summary>
    public async Task StartAsync()
    {
        if (_busy) return;
        // 环境未就绪时先补部署
        if (!_ready && !File.Exists(_s.DshBinJsGlobal))
        {
            await PrepareAsync();
            if (!_ready) return;
        }
        BeginBusy();
        _cts = new CancellationTokenSource();
        _runner ??= new DeployRunner(_s, _s.ForceMirror);
        WireRunner(_runner);

        SetStatus("正在启动服务...", "#1E88E5");
        var ok = await _runner.StartWebAsync(_cts.Token);
        if (ok)
        {
            _s.LastPort = _s.Port;
            _s.Save();
            SetStatus("服务已就绪，浏览器已打开", "#43A047");
            AppendLog($"已打开浏览器: {_s.Url}");
            WebServer.OpenBrowser(_s.Url);
        }
        else
        {
            SetStatus("启动失败，请查看日志", "#E53935");
        }
        EndBusy();
    }

    /// <summary>自动检测空闲端口（从当前端口起向上扫描），填入后自动开始部署。</summary>
    public async Task AutoPickPortAsync()
    {
        if (_busy) return;
        var start = _s.Port;
        var picked = 0;
        for (int p = start; p <= start + 30; p++)
        {
            if (!WebServer.IsPortOpen(p)) { picked = p; break; }
        }
        if (picked == 0)
        {
            AppendLog("未找到可用端口（当前端口附近 30 个都被占用）");
            return;
        }
        Port = picked;
        AppendLog($"已自动选择空闲端口: {picked}");
        await PrepareAsync();
    }

    public void OpenDataDir()
    {
        Directory.CreateDirectory(_s.EffectiveDshHome);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_s.EffectiveDshHome) { UseShellExecute = true });
    }

    public void Cancel() => _cts?.Cancel();

    private void WireRunner(DeployRunner runner)
    {
        runner.LogLine += line => Ui(() => { AppendLog(line); Log.Info(line); });
        runner.DownloadProgress += e => Ui(() =>
        {
            Progress = e.Total > 0 ? e.Percent : 0;
            ProgressText = e.Total > 0 ? $"{e.Percent:F1}% ({e.Received / 1048576.0:F1}/{e.Total / 1048576.0:F1} MB)" : "";
        });
        runner.StepChanged += step => Ui(() =>
        {
            var (idx, title) = step switch
            {
                DeployStep.DetectEnv => (0, "检测环境"),
                DeployStep.DownloadNode => (1, "下载组件"),
                DeployStep.ExtractNode => (1, "解压组件"),
                DeployStep.InstallDsh => (2, "安装 DSH"),
                _ => (3, "就绪"),
            };
            for (int i = 0; i < Steps.Count; i++)
                Steps[i].State = i < idx ? StepState.Done : i == idx ? StepState.Running : StepState.Pending;
            SetStatus(title, "#1E88E5");
        });
    }

    private void BeginBusy()
    {
        _busy = true;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanAct));
        OnPropertyChanged(nameof(MainButtonText));
    }

    private void EndBusy()
    {
        _busy = false;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanAct));
        OnPropertyChanged(nameof(MainButtonText));
    }

    private void AppendLog(string line) => Logs.Add(new LogItem($"[{DateTime.Now:HH:mm:ss}] {line}"));

    private void SetStatus(string text, string color)
    {
        StatusText = text;
        StatusColor = color;
    }

    private static void Ui(Action action)
    {
        var app = Application.Current;
        if (app == null) { action(); return; }
        if (app.Dispatcher.CheckAccess()) action();
        else app.Dispatcher.BeginInvoke(action);
    }
}
