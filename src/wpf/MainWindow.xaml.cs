using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using DSHLauncher.ViewModels;

namespace DSHLauncher;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        // 初始尺寸按屏幕工作区自适应（WPF DIP + PerMonitorV2 处理高 DPI 缩放）
        var work = SystemParameters.WorkArea;
        Width = Math.Min(900, Math.Max(760, work.Width - 80));
        Height = Math.Min(620, Math.Max(520, work.Height - 80));
        InitializeComponent();
        DataContext = _vm;
        Log.Init(System.IO.Path.Combine(Settings.AppDir, "logs", "launcher.log"));
        _vm.Logs.CollectionChanged += (_, _) =>
        {
            Dispatcher.BeginInvoke(() => LogScroller.ScrollToEnd());
        };
        Log.Line += line => Dispatcher.BeginInvoke(() => _vm.Logs.Add(new LogItem($"[{DateTime.Now:HH:mm:ss}] {line}")));
        Log.Info("GUI started");

        // v1.2 流程：打开窗口自动「检查环境 + 按需下载」，启动由用户手动触发
        Loaded += async (_, _) => await _vm.PrepareAsync();
    }

    /// <summary>Windows 11 原生圆角（与 EDGE 一致）；Windows 10 自动忽略。</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int cornerPref = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33, ref cornerPref, sizeof(int));
        }
        catch { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _vm.Cancel();
        Close();
    }

    private async void Deploy_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsReady) await _vm.StartAsync();
        else await _vm.PrepareAsync();
    }

    private async void StartOnly_Click(object sender, RoutedEventArgs e) => await _vm.StartAsync();

    private async void AutoPickPort_Click(object sender, RoutedEventArgs e) => await _vm.AutoPickPortAsync();

    private void OpenData_Click(object sender, RoutedEventArgs e) => _vm.OpenDataDir();

    protected override void OnClosed(EventArgs e)
    {
        _vm.Cancel();
        base.OnClosed(e);
    }
}
