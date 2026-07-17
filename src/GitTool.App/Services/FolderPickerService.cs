using Windows.Storage.Pickers;

namespace GitTool.App.Services;

public sealed class FolderPickerService
{
    public async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add("*");

        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.Current.MainWindowHandle);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
