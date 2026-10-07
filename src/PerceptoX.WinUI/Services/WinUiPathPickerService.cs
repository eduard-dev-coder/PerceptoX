using PerceptoX.Presentation.Services;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PerceptoX.WinUI.Services;

public sealed class WinUiPathPickerService(nint windowHandle, bool modernFormats = false) : IPathPickerService
{
    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FolderPicker picker = new();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, windowHandle);
        StorageFolder? selected = await picker.PickSingleFolderAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return selected?.Path;
    }

    public async Task<string?> PickImageAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileOpenPicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            ViewMode = PickerViewMode.Thumbnail
        };
        foreach (string extension in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff" })
        {
            picker.FileTypeFilter.Add(extension);
        }
        if (modernFormats)
            foreach (string extension in new[] { ".heic", ".heif", ".avif" }) picker.FileTypeFilter.Add(extension);

        InitializeWithWindow.Initialize(picker, windowHandle);
        StorageFile? selected = await picker.PickSingleFileAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return selected?.Path;
    }
}
