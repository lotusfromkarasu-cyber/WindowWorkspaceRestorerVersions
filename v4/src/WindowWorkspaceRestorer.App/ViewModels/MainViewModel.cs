using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using WindowWorkspaceRestorer.Models;
using WindowWorkspaceRestorer.Services;

namespace WindowWorkspaceRestorer.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly PersistenceService _persistenceService;
    private readonly StartupService _startupService;
    private readonly WorkspaceCaptureService _captureService;
    private readonly WorkspaceRestoreService _restoreService;
    private WorkspaceRecord? _selectedRecord;
    private string _newRecordName = string.Empty;
    private string _selectedRecordName = string.Empty;
    private string _statusText = "准备就绪。";
    private string _restoreDetails = string.Empty;
    private bool _isBusy;
    private bool _isStartupEnabled;
    private readonly SettingsService _settingsService;
    private bool _closeToTray;
    private int _workspacePanelIndex;

    public MainViewModel(
        PersistenceService persistenceService,
        StartupService startupService,
        WorkspaceCaptureService captureService,
        WorkspaceRestoreService restoreService,
        SettingsService? settingsService = null)
    {
        _persistenceService = persistenceService;
        _startupService = startupService;
        _captureService = captureService;
        _restoreService = restoreService;
        _settingsService = settingsService ?? new SettingsService();
        _closeToTray = _settingsService.Load().CloseToTray;

        Records = new ObservableCollection<WorkspaceRecord>();
        Candidates = new ObservableCollection<CaptureCandidate>();
        RestoreSelections = new ObservableCollection<RestoreSelectionItemViewModel>();
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy && Candidates.Any(x => x.IsSelected && x.CanPersist));
        RestoreCommand = new AsyncRelayCommand(RestoreAsync, () => !IsBusy && SelectedRecord is not null && RestoreSelections.Any(x => x.IsSelected));
        SelectAllRestoreItemsCommand = new AsyncRelayCommand(() => SetRestoreSelectionAsync(true), () => !IsBusy && RestoreSelections.Count > 0);
        ClearRestoreItemsCommand = new AsyncRelayCommand(() => SetRestoreSelectionAsync(false), () => !IsBusy && RestoreSelections.Any(x => x.IsSelected));
        RenameCommand = new AsyncRelayCommand(RenameAsync, () => !IsBusy && SelectedRecord is not null && !string.IsNullOrWhiteSpace(SelectedRecordName));
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => !IsBusy && SelectedRecord is not null);
    }

    public ObservableCollection<WorkspaceRecord> Records { get; }

    public ObservableCollection<CaptureCandidate> Candidates { get; }

    public ObservableCollection<RestoreSelectionItemViewModel> RestoreSelections { get; }

    public string RecordFilePath => _persistenceService.FilePath;

    public bool CloseToTray
    {
        get => _closeToTray;
        set
        {
            if (_closeToTray == value) return;
            try
            {
                _settingsService.Save(new AppSettings(value));
                _closeToTray = value;
                StatusText = value ? "关闭窗口时将收起到系统托盘。" : "关闭窗口时将退出程序。";
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            { StatusText = $"无法保存设置：{ex.Message}"; }
            OnPropertyChanged();
        }
    }

    public WorkspaceRecord? SelectedRecord
    {
        get => _selectedRecord;
        set
        {
            if (Equals(_selectedRecord, value))
            {
                return;
            }

            _selectedRecord = value;
            OnPropertyChanged();
            var selectedName = value?.Name ?? string.Empty;
            if (_selectedRecordName != selectedName)
            {
                _selectedRecordName = selectedName;
                OnPropertyChanged(nameof(SelectedRecordName));
            }
            RebuildRestoreSelections(value);
            if (value is not null)
            {
                WorkspacePanelIndex = 1;
            }
            RaiseCommandStates();
        }
    }

    public int WorkspacePanelIndex
    {
        get => _workspacePanelIndex;
        set
        {
            if (_workspacePanelIndex == value) return;
            _workspacePanelIndex = value;
            OnPropertyChanged();
        }
    }

    public int SelectedRestoreCount => RestoreSelections.Count(x => x.IsSelected);

    public string RestoreSelectionSummary => $"已选择 {SelectedRestoreCount} / {RestoreSelections.Count} 项";

    public string NewRecordName
    {
        get => _newRecordName;
        set
        {
            if (_newRecordName == value)
            {
                return;
            }

            _newRecordName = value;
            OnPropertyChanged();
            RaiseCommandStates();
        }
    }

    public string SelectedRecordName
    {
        get => _selectedRecordName;
        set
        {
            if (_selectedRecordName == value)
            {
                return;
            }

            _selectedRecordName = value;
            OnPropertyChanged();
            RaiseCommandStates();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            OnPropertyChanged();
        }
    }

    public string RestoreDetails
    {
        get => _restoreDetails;
        private set
        {
            if (_restoreDetails == value)
            {
                return;
            }

            _restoreDetails = value;
            OnPropertyChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value)
            {
                return;
            }

            _isBusy = value;
            OnPropertyChanged();
            RaiseCommandStates();
        }
    }

    public bool IsStartupEnabled
    {
        get => _isStartupEnabled;
        set
        {
            if (_isStartupEnabled == value)
            {
                return;
            }

            try
            {
                _startupService.SetEnabled(value);
                _isStartupEnabled = value;
                OnPropertyChanged();
                StatusText = value ? "已启用登录后自动启动。" : "已关闭登录后自动启动。";
            }
            catch (Exception ex)
            {
                StatusText = $"修改启动设置失败：{ex.Message}";
                OnPropertyChanged();
            }
        }
    }

    public ICommand ScanCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand RestoreCommand { get; }

    public ICommand SelectAllRestoreItemsCommand { get; }

    public ICommand ClearRestoreItemsCommand { get; }

    public ICommand RenameCommand { get; }

    public ICommand DeleteCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            var loadResult = await _persistenceService.LoadAsync();
            Records.Clear();
            foreach (var record in loadResult.Records.OrderByDescending(x => x.UpdatedAt))
            {
                Records.Add(record);
            }

            SelectedRecord = Records.FirstOrDefault();
            _isStartupEnabled = _startupService.IsEnabled();
            OnPropertyChanged(nameof(IsStartupEnabled));
            StatusText = loadResult.Warning ?? $"已加载 {Records.Count} 条记录。";
        }
        catch (Exception ex)
        {
            StatusText = $"初始化失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ScanAsync()
    {
        IsBusy = true;
        WorkspacePanelIndex = 0;
        try
        {
            var candidates = await _captureService.ScanAsync();
            Candidates.Clear();
            foreach (var candidate in candidates)
            {
                Candidates.Add(candidate);
                candidate.PropertyChanged += CandidateOnPropertyChanged;
            }

            StatusText = Candidates.Count == 0
                ? "没有找到可记录的 Office、WPS、资源管理器或 COMSOL 项目。"
                : $"找到 {Candidates.Count} 个项目，请勾选后保存。";
            RestoreDetails = string.Empty;
        }
        catch (OperationCanceledException)
        {
            StatusText = "扫描已取消。";
        }
        catch (Exception ex)
        {
            StatusText = $"扫描失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
            RaiseCommandStates();
        }
    }

    private async Task SaveAsync()
    {
        var items = Candidates
            .Where(x => x.IsSelected && x.CanPersist)
            .Select(x => x.Item)
            .GroupBy(CreateSaveKey, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();
        if (items.Length == 0)
        {
            StatusText = "请至少勾选一个可保存项目。";
            return;
        }

        var now = DateTimeOffset.Now;
        var name = string.IsNullOrWhiteSpace(NewRecordName)
            ? CreateAutomaticRecordName(now)
            : NewRecordName.Trim();
        var record = new WorkspaceRecord(Guid.NewGuid(), name, now, now, items);

        IsBusy = true;
        try
        {
            Records.Insert(0, record);
            SelectedRecord = record;
            await _persistenceService.SaveAsync(Records);
            NewRecordName = string.Empty;
            StatusText = $"已保存记录“{record.Name}”，包含 {items.Length} 个项目。";
        }
        catch (Exception ex)
        {
            Records.Remove(record);
            StatusText = $"保存失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RestoreAsync()
    {
        if (SelectedRecord is null)
        {
            return;
        }

        var selectedRecord = CreateSelectedRestoreRecord(SelectedRecord, RestoreSelections);
        if (selectedRecord.Items.Count == 0)
        {
            StatusText = "请至少勾选一个要恢复的项目。";
            return;
        }

        IsBusy = true;
        try
        {
            var summary = await _restoreService.RestoreAsync(selectedRecord);
            RestoreDetails = string.Join(
                Environment.NewLine,
                summary.Results.Select(result => $"{StatusLabel(result.Status)}：{result.Item.DisplayName} - {result.Message}"));
            StatusText = $"恢复完成：成功或已存在 {summary.SucceededCount} 项，失败 {summary.FailedCount} 项，需要处理 {summary.RequiresActionCount} 项。";
        }
        catch (OperationCanceledException)
        {
            StatusText = "恢复已取消。";
        }
        catch (Exception ex)
        {
            StatusText = $"恢复失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteAsync()
    {
        if (SelectedRecord is null)
        {
            return;
        }

        var record = SelectedRecord;
        Records.Remove(record);
        SelectedRecord = Records.FirstOrDefault();
        try
        {
            await _persistenceService.SaveAsync(Records);
            StatusText = $"已删除记录“{record.Name}”。";
        }
        catch (Exception ex)
        {
            Records.Insert(0, record);
            SelectedRecord = record;
            StatusText = $"删除后保存失败：{ex.Message}";
        }
    }

    private async Task RenameAsync()
    {
        if (SelectedRecord is null || string.IsNullOrWhiteSpace(SelectedRecordName))
        {
            return;
        }

        var original = SelectedRecord;
        var renamed = original with
        {
            Name = SelectedRecordName.Trim(),
            UpdatedAt = DateTimeOffset.Now
        };
        var index = Records.IndexOf(original);
        if (index < 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Records[index] = renamed;
            SelectedRecord = renamed;
            await _persistenceService.SaveAsync(Records);
            StatusText = $"已将记录重命名为“{renamed.Name}”。";
        }
        catch (Exception ex)
        {
            Records[index] = original;
            SelectedRecord = original;
            StatusText = $"重命名失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CandidateOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CaptureCandidate.IsSelected))
        {
            RaiseCommandStates();
        }
    }

    private void RebuildRestoreSelections(WorkspaceRecord? record)
    {
        foreach (var selection in RestoreSelections)
        {
            selection.PropertyChanged -= RestoreSelectionOnPropertyChanged;
        }

        RestoreSelections.Clear();
        if (record is not null)
        {
            foreach (var item in record.Items)
            {
                var selection = new RestoreSelectionItemViewModel(item);
                selection.PropertyChanged += RestoreSelectionOnPropertyChanged;
                RestoreSelections.Add(selection);
            }
        }

        OnPropertyChanged(nameof(SelectedRestoreCount));
        OnPropertyChanged(nameof(RestoreSelectionSummary));
    }

    private void RestoreSelectionOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RestoreSelectionItemViewModel.IsSelected)) return;
        OnPropertyChanged(nameof(SelectedRestoreCount));
        OnPropertyChanged(nameof(RestoreSelectionSummary));
        RaiseCommandStates();
    }

    private Task SetRestoreSelectionAsync(bool isSelected)
    {
        foreach (var item in RestoreSelections)
        {
            item.IsSelected = isSelected;
        }

        return Task.CompletedTask;
    }

    private void RaiseCommandStates()
    {
        (SaveCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (RestoreCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SelectAllRestoreItemsCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ClearRestoreItemsCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (RenameCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (DeleteCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (ScanCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private static string StatusLabel(RestoreItemStatus status)
    {
        return status switch
        {
            RestoreItemStatus.AlreadyOpen => "已存在",
            RestoreItemStatus.Opened => "已打开",
            RestoreItemStatus.Missing => "不存在",
            RestoreItemStatus.Unsupported => "不支持",
            RestoreItemStatus.ApplicationUnavailable => "应用不可用",
            RestoreItemStatus.RequiresUserAction => "需要处理",
            _ => "失败"
        };
    }

    private static string CreateSaveKey(WorkspaceItem item)
    {
        var group = item.Type == WorkspaceItemType.ExplorerFolder
            ? $":{item.GroupKey ?? "legacy"}"
            : string.Empty;
        return $"{item.Type}:{PathNormalizer.Normalize(item.Path)}{group}";
    }

    private static string CreateAutomaticRecordName(DateTimeOffset timestamp)
    {
        return $"工作区 {timestamp:yyyy-MM-dd HH-mm-ss}";
    }

    internal static WorkspaceRecord CreateSelectedRestoreRecord(
        WorkspaceRecord source,
        IEnumerable<RestoreSelectionItemViewModel> selections)
    {
        return source with
        {
            Items = selections.Where(x => x.IsSelected).Select(x => x.Item).ToArray()
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
