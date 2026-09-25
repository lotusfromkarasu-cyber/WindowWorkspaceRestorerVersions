using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WindowWorkspaceRestorer.Models;

public sealed class CaptureCandidate : INotifyPropertyChanged
{
    private bool _isSelected;

    public CaptureCandidate(
        WorkspaceItem item,
        bool canPersist = true,
        string? warning = null,
        nint windowHandle = default)
    {
        Item = item;
        CanPersist = canPersist;
        Warning = warning;
        WindowHandle = windowHandle;
        _isSelected = canPersist;
    }

    public WorkspaceItem Item { get; }

    public string TypeLabel => Item.Type switch
    {
        WorkspaceItemType.WordDocument => "Word",
        WorkspaceItemType.ExcelWorkbook => "Excel",
        WorkspaceItemType.PowerPointPresentation => "PowerPoint",
        WorkspaceItemType.ExplorerFolder => "文件夹",
        WorkspaceItemType.ComsolModel => "COMSOL",
        WorkspaceItemType.WpsWriterDocument => "WPS 文字",
        WorkspaceItemType.WpsSpreadsheet => "WPS 表格",
        WorkspaceItemType.WpsPresentation => "WPS 演示",
        _ => "文件"
    };

    public bool CanPersist { get; }

    public string? Warning { get; }

    public nint WindowHandle { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
