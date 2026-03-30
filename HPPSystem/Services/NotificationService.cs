using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HPPSystem.Services;

public sealed partial class ToastMessage : ObservableObject
{
    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _isError;
}

public sealed class NotificationService
{
    public ObservableCollection<ToastMessage> Toasts { get; } = new();

    public void ShowSuccess(string message) => Show(message, false);

    public void ShowError(string message) => Show(message, true);

    private void Show(string message, bool isError)
    {
        var toast = new ToastMessage
        {
            Message = message,
            IsError = isError
        };

        Toasts.Add(toast);
        _ = RemoveLaterAsync(toast);
    }

    private async Task RemoveLaterAsync(ToastMessage toast)
    {
        await Task.Delay(3500);
        await Dispatcher.UIThread.InvokeAsync(() => Toasts.Remove(toast));
    }
}
