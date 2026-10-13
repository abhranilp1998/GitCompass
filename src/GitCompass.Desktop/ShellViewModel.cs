using CommunityToolkit.Mvvm.ComponentModel;
using GitCompass.Domain;

namespace GitCompass.Desktop;

public sealed partial class ShellViewModel : ObservableObject
{
    [ObservableProperty] private RepositorySnapshot? snapshot;
    [ObservableProperty] private HistoryPage? history;
    [ObservableProperty] private string page = "Overview";
    [ObservableProperty] private bool busy;
    [ObservableProperty] private UserGoal goal = UserGoal.Understand;
}
