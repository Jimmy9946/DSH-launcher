using System.Windows;
using System.Windows.Input;
using DSHLauncher.ViewModels;

namespace DSHLauncher;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Log.Init(System.IO.Path.Combine(Settings.AppDir, "logs", "launcher.log"));
        _vm.Logs.CollectionChanged += (_, _) =>
        {
            Dispatcher.BeginInvoke(() => LogScroller.ScrollToEnd());
        };
        Log.Line += line => Dispatcher.BeginInvoke(() => _vm.Logs.Add(new LogItem($"[{DateTime.Now:HH:mm:ss}] {line}")));
        Log.Info("GUI started");

        // PCL 式体验：打开窗口即自动部署（已就绪则直接进入启动流程）
        Loaded += async (_, _) => await _vm.DeployAsync();
    }

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

    private async void Deploy_Click(object sender, RoutedEventArgs e) => await _vm.DeployAsync();

    private void StartOnly_Click(object sender, RoutedEventArgs e) => _vm.StartOnly();

    private void OpenData_Click(object sender, RoutedEventArgs e) => _vm.OpenDataDir();

    protected override void OnClosed(EventArgs e)
    {
        _vm.Cancel();
        base.OnClosed(e);
    }
}
