using System.Windows;
using WindowWorkspaceRestorer.Services;
using WindowWorkspaceRestorer.ViewModels;

namespace WindowWorkspaceRestorer;

public partial class App : Application
{
    private Mutex? _instance;
    private EventWaitHandle? _activation;
    private RegisteredWaitHandle? _activationWait;
    private TrayService? _tray;
    private bool _ownsInstance;
    internal bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\WindowWorkspaceRestorer-v4-Show");
        _instance = new Mutex(true, @"Local\WindowWorkspaceRestorer-v4", out _ownsInstance);
        if (!_ownsInstance)
        {
            _activation.Set();
            Shutdown();
            return;
        }
        var adapters = new IWorkspaceAdapter[]
        {
            new WordAdapter(), new ExcelAdapter(), new PowerPointAdapter(),
            new ExplorerAdapter(), new ComsolAdapter(),
            new WpsAdapter(Models.WorkspaceItemType.WpsWriterDocument),
            new WpsAdapter(Models.WorkspaceItemType.WpsSpreadsheet),
            new WpsAdapter(Models.WorkspaceItemType.WpsPresentation)
        };
        var viewModel = new MainViewModel(new PersistenceService(), new StartupService(),
            new WorkspaceCaptureService(adapters), new WorkspaceRestoreService(adapters));
        var window = new MainWindow { DataContext = viewModel };
        MainWindow = window;
        _tray = new TrayService(() => Dispatcher.Invoke(ShowMainWindow), () => Dispatcher.Invoke(ExitApplication));
        _activationWait = ThreadPool.RegisterWaitForSingleObject(_activation,
            (_, _) => Dispatcher.BeginInvoke(ShowMainWindow), null, Timeout.Infinite, false);
        window.Show();
        _ = viewModel.InitializeAsync();
    }

    internal void ShowMainWindow()
    {
        if (IsExiting || MainWindow is null) return;
        MainWindow.Show();
        if (MainWindow.WindowState == WindowState.Minimized) MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    internal void ExitApplication()
    {
        IsExiting = true;
        Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        IsExiting = true;
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsExiting = true;
        _activationWait?.Unregister(null);
        _tray?.Dispose();
        _activation?.Dispose();
        if (_ownsInstance) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
