using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel;

namespace Perigon.MiniDb.Client.Services;

public sealed class DesktopFilePickerService
{
    public async Task<string?> PickMiniDbFileAsync(CancellationToken cancellationToken = default)
    {
        var result = await MainThread.InvokeOnMainThreadAsync(() =>
            FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select MiniDB file",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.WinUI] = [".mds"],
                    [DevicePlatform.MacCatalyst] = ["mds"]
                })
            }));

        return result?.FullPath;
    }
}
