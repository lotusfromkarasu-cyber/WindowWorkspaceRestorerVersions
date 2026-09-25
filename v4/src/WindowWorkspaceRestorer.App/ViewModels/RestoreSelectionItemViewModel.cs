using System.ComponentModel;
using System.Runtime.CompilerServices;
using WindowWorkspaceRestorer.Models;

namespace WindowWorkspaceRestorer.ViewModels;

public sealed class RestoreSelectionItemViewModel : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public RestoreSelectionItemViewModel(WorkspaceItem item)
    {
        Item = item;
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

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
