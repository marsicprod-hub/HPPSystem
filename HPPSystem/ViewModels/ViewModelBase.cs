using CommunityToolkit.Mvvm.ComponentModel;
using HPPSystem.Services;

namespace HPPSystem.ViewModels;

public class ViewModelBase : ObservableObject
{
}

public abstract class PageViewModelBase : ViewModelBase
{
    protected readonly IDataService DataService;
    protected readonly NotificationService Notifications;

    protected PageViewModelBase(IDataService dataService, NotificationService notifications)
    {
        DataService = dataService;
        Notifications = notifications;
        DataService.StateChanged += (_, _) => Refresh();
    }

    public abstract string Title { get; }

    public abstract void Refresh();

    protected void Success(string message) => Notifications.ShowSuccess(message);

    protected void Error(string message) => Notifications.ShowError(message);
}
