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

/// <summary>PCL 皮肤主窗口的数据模型：把 DeployRunner 的事件转成 UI 可绑定的状态。</summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly Settings _s = Settings.Load();
    private DeployRunner? _runner;
    private CancellationTokenSource? _cts;
    private bool _busy;

    public ObservableCollection<StepItem> Steps { get; } =
    [
        new("检测 Node"), new("下载 / 解压"), new("安装 DSH"), new("启动服务"), new("等待就绪"),
    ];

    public ObservableCollection<LogItem> Logs { get; } = [];

    public string AppVersion => $"v1.1.0  ·  Node {_s.NodeVersion}";

    private string _statusText = "就绪，点击部署开始";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    private string _statusColor = "#48CAE4";
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

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>完整部署：检测/下载 Node → 安装 DSH → 启动 Web → 打开浏览器。</summary>
    public async Task DeployAsync()
    {
        if (_busy) return;
        _busy = true;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanAct));
        _cts = new CancellationTokenSource();
        _s.Save();

        foreach (var s in Steps) s.State = StepState.Pending;
        Logs.Clear();
        AppendLog($"===== 开始部署 (端口 {_s.Port}) =====");
        SetStatus("准备中", "#48CAE4");

        _runner = new DeployRunner(_s, _s.ForceMirror);
        _runner.LogLine += line => Ui(() => { AppendLog(line); Log.Info(line); });
        _runner.DownloadProgress += e => Ui(() =>
        {
            Progress = e.Total > 0 ? e.Percent : 0;
            ProgressText = e.Total > 0 ? $"{e.Percent:F1}% ({e.Received / 1048576.0:F1}/{e.Total / 1048576.0:F1} MB)" : "";
        });
        _runner.StepChanged += step => Ui(() =>
        {
            var (idx, title) = step switch
            {
                DeployStep.DetectNode => (0, "检测 Node"),
                DeployStep.DownloadNode => (1, "下载 Node"),
                DeployStep.ExtractNode => (1, "解压 Node"),
                DeployStep.InstallDsh => (2, "安装 DSH"),
                DeployStep.StartWeb => (3, "启动服务"),
                DeployStep.WaitReady => (4, "等待就绪"),
                _ => (4, "完成"),
            };
            for (int i = 0; i < Steps.Count; i++)
                Steps[i].State = i < idx ? StepState.Done : i == idx ? StepState.Running : StepState.Pending;
            SetStatus(title, "#48CAE4");
        });

        var ok = await _runner.RunAsync(_cts.Token);
        Progress = ok ? 100 : 0;
        if (ok)
        {
            foreach (var s in Steps) s.State = StepState.Done;
            SetStatus("部署完成，已打开浏览器", "#2ECC71");
            AppendLog($"已打开浏览器: {_s.Url}");
            WebServer.OpenBrowser(_s.Url);
        }
        else
        {
            foreach (var s in Steps)
                if (s.State == StepState.Running) s.State = StepState.Failed;
            SetStatus("部署失败，请查看日志", "#E74C3C");
        }
        AppendLog(ok ? "===== 部署完成 =====" : "===== 部署失败 =====");
        _busy = false;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanAct));
    }

    /// <summary>仅启动服务（不检查组件）。</summary>
    public void StartOnly()
    {
        if (_busy) return;
        if (WebServer.IsPortOpen(_s.Port))
        {
            AppendLog("服务已在运行，直接打开页面");
            WebServer.OpenBrowser(_s.Url);
            return;
        }
        AppendLog($"启动服务 (端口 {_s.Port})...");
        _ = Task.Run(async () =>
        {
            Directory.CreateDirectory(_s.EffectiveWorkspace);
            if (await WebServer.WaitReadyAsync(_s.Port, TimeSpan.FromSeconds(1)).ConfigureAwait(false)) { }
            var proc = WebServer.Launch(_s);
            if (await WebServer.WaitReadyAsync(_s.Port, TimeSpan.FromMinutes(6)).ConfigureAwait(false))
            {
                Ui(() => { SetStatus("服务已就绪", "#2ECC71"); AppendLog($"服务已就绪: {_s.Url}"); WebServer.OpenBrowser(_s.Url); });
            }
            else
            {
                Ui(() => SetStatus("启动失败", "#E74C3C"));
            }
        });
    }

    public void OpenDataDir()
    {
        Directory.CreateDirectory(_s.EffectiveDshHome);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_s.EffectiveDshHome) { UseShellExecute = true });
    }

    public void Cancel() => _cts?.Cancel();

    private void AppendLog(string line)
    {
        Logs.Add(new LogItem($"[{DateTime.Now:HH:mm:ss}] {line}"));
    }

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
