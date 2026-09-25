using System.Collections.ObjectModel;
using WindowWorkspaceRestorer.Models;

namespace WindowWorkspaceRestorer.ViewModels;

public sealed class RestoreProgressViewModel
{
    public RestoreProgressViewModel(RestoreSummary summary)
    {
        Summary = summary;
        Results = new ObservableCollection<RestoreItemResult>(summary.Results);
    }

    public RestoreSummary Summary { get; }

    public ObservableCollection<RestoreItemResult> Results { get; }
}
