using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace NetTrace.Services;

/// <summary>confirmation dialogs and file pickers, bound to the main window.</summary>
public sealed class DialogService
{
    private Window? _window;

    public void Attach(Window window) => _window = window;

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        if (_window?.Content is not FrameworkElement root)
        {
            return false;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = confirmText,
            CloseButtonText = "cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = root.XamlRoot,
            RequestedTheme = root.ActualTheme,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>shows a save picker and returns the chosen path, or null when cancelled.</summary>
    public async Task<string?> PickSaveFileAsync(string suggestedName, string typeLabel, string extension)
    {
        if (_window is null)
        {
            return null;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedName,
        };
        picker.FileTypeChoices.Add(typeLabel, [extension]);

        // unpackaged desktop apps must associate pickers with a window handle
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_window));

        Windows.Storage.StorageFile? file = await picker.PickSaveFileAsync();
        return file?.Path;
    }
}
