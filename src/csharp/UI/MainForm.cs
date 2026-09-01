using DSHLauncher.Core;

namespace DSHLauncher.UI;

/// <summary>主窗口：步骤状态、下载进度、实时日志、设置项、操作按钮。</summary>
public sealed class MainForm : Form
{
    private readonly Settings _s;
    private readonly bool _mirror;
    private readonly Label _status = new();
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1000 };
    private readonly RichTextBox _log = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Value = 3080, Width = 90 };
    private readonly CheckBox _mirrorBox = new() { Text = "使用国内镜像" };
    private readonly Button _deployBtn = new() { Text = "部署并启动", Width = 110 };
    private readonly Button _openBtn = new() { Text = "打开数据目录", Width = 110 };
    private readonly Button _stopBtn = new() { Text = "仅启动服务", Width = 110 };
    private CancellationTokenSource? _cts;
    private bool _running;

    public MainForm(Settings s, bool mirror)
    {
        _s = s;
        _mirror = mirror || s.ForceMirror;
        Text = "DSH 一键部署启动器 v1.0";
        Width = 720;
        Height = 540;
        StartPosition = FormStartPosition.CenterScreen;

        _status.Text = "就绪";
        _status.Dock = DockStyle.Top;
        _status.Height = 34;
        _status.Font = new Font(_status.Font.FontFamily, 11f, FontStyle.Bold);
        _status.Padding = new Padding(8, 8, 0, 0);

        _progress.Dock = DockStyle.Top;
        _progress.Height = 20;

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8, 10, 8, 8),
        };
        bottom.Controls.Add(new Label { Text = "端口:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        bottom.Controls.Add(_port);
        bottom.Controls.Add(_mirrorBox);
        bottom.Controls.Add(_deployBtn);
        bottom.Controls.Add(_stopBtn);
        bottom.Controls.Add(_openBtn);

        Controls.Add(_log);
        Controls.Add(_progress);
        Controls.Add(_status);
        Controls.Add(bottom);

        _mirrorBox.Checked = _mirror;
        _port.Value = Math.Clamp(s.Port, 1, 65535);

        _deployBtn.Click += async (_, _) => await StartDeployAsync();
        _stopBtn.Click += (_, _) => StartOnlyAsync();
        _openBtn.Click += (_, _) =>
        {
            Directory.CreateDirectory(s.EffectiveDshHome);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(s.EffectiveDshHome) { UseShellExecute = true });
        };
        FormClosing += (_, _) => _cts?.Cancel();

        Log.Line += AppendLog;
    }

    private void AppendLog(string line)
    {
        if (IsDisposed) return;
        try
        {
            if (InvokeRequired) { BeginInvoke(() => AppendLog(line)); return; }
            _log.AppendText(line + "\n");
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }
        catch { }
    }

    private void SetStatus(string text)
    {
        if (IsDisposed) return;
        try
        {
            if (InvokeRequired) { BeginInvoke(() => SetStatus(text)); return; }
            _status.Text = text;
        }
        catch { }
    }

    private void SetProgress(double percent)
    {
        if (IsDisposed) return;
        try
        {
            if (InvokeRequired) { BeginInvoke(() => SetProgress(percent)); return; }
            _progress.Value = (int)Math.Clamp(percent * 10, 0, 1000);
        }
        catch { }
    }

    private async Task StartDeployAsync()
    {
        if (_running) return;
        _running = true;
        _deployBtn.Enabled = false;
        _stopBtn.Enabled = false;
        _s.Port = (int)_port.Value;
        _s.ForceMirror = _mirrorBox.Checked;
        _s.Save();
        _cts = new CancellationTokenSource();

        var runner = new DeployRunner(_s, _s.ForceMirror);
        runner.LogLine += AppendLog;
        runner.DownloadProgress += e => SetProgress(e.Percent);
        runner.StepChanged += step =>
        {
            var name = step switch
            {
                DeployStep.DetectNode => "检测 Node",
                DeployStep.DownloadNode => "下载 Node",
                DeployStep.ExtractNode => "解压 Node",
                DeployStep.InstallDsh => "安装 DSH",
                DeployStep.StartWeb => "启动服务",
                DeployStep.WaitReady => "等待就绪",
                _ => "完成",
            };
            SetStatus(name);
        };

        AppendLog($"===== 开始部署 (端口 {_s.Port}) =====");
        var ok = await runner.RunAsync(_cts.Token);
        SetProgress(ok ? 100 : 0);
        SetStatus(ok ? "部署完成" : "部署失败");
        AppendLog(ok ? "===== 部署完成 =====" : "===== 部署失败 =====");
        if (ok)
        {
            WebServer.OpenBrowser(_s.Url);
            AppendLog($"已打开浏览器: {_s.Url}");
        }
        _running = false;
        _deployBtn.Enabled = true;
        _stopBtn.Enabled = true;
    }

    private void StartOnlyAsync()
    {
        if (_running) return;
        _s.Port = (int)_port.Value;
        _s.ForceMirror = _mirrorBox.Checked;
        _s.Save();
        if (!WebServer.IsPortOpen(_s.Port))
        {
            Directory.CreateDirectory(_s.EffectiveWorkspace);
            WebServer.Launch(_s);
            AppendLog($"服务启动中 (端口 {_s.Port})...");
            _ = Task.Run(async () =>
            {
                if (await WebServer.WaitReadyAsync(_s.Port, TimeSpan.FromMinutes(6)).ConfigureAwait(false))
                {
                    AppendLog($"服务已就绪: {_s.Url}");
                    WebServer.OpenBrowser(_s.Url);
                }
                else AppendLog("等待服务超时");
            });
        }
        else
        {
            AppendLog("服务已在运行, 直接打开页面");
            WebServer.OpenBrowser(_s.Url);
        }
    }
}
