using System.Drawing;
using Forms = System.Windows.Forms;

namespace WindowWorkspaceRestorer.Services;

internal sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Icon _image;
    public TrayService(Action show, Action exit)
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"));
        using var stream = resource!.Stream;
        _image = new Icon(stream);
        _menu = new Forms.ContextMenuStrip();
        _menu.Items.Add("显示主窗口", null, (_, _) => show());
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("退出程序", null, (_, _) => exit());
        _icon = new Forms.NotifyIcon { Text = "Workspace · v4", Icon = _image, ContextMenuStrip = _menu, Visible = true };
        _icon.DoubleClick += (_, _) => show();
    }
    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _image.Dispose();
    }
}
