using System.ComponentModel;
using System.Windows;
using WindowWorkspaceRestorer.ViewModels;

namespace WindowWorkspaceRestorer;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (Application.Current is App { IsExiting: false } && DataContext is MainViewModel { CloseToTray: true })
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (Application.Current is App { IsExiting: false } app) app.ExitApplication();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current is App app) app.ExitApplication();
    }
}
