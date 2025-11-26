using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ClipManager.Pages;
public partial class Export : ComponentBase
{
    private bool _exporting;
    private string? _exportStatus;

    private async Task DownloadExport()
    {
        _exporting = true;
        _exportStatus = "Preparing export...";

        try
        {
            var response = await Http.GetAsync("api/exports/create");
            if (!response.IsSuccessStatusCode)
            {
                _exportStatus = "Failed to create export: " + response.ReasonPhrase;
                return;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            await using var ms = new MemoryStream(bytes, writable: false);
            using var streamRef = new DotNetStreamReference(ms);

            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar ??
                           response.Content.Headers.ContentDisposition?.FileName ??
                           "clipboard_export.zip";

            await JsRuntime.InvokeVoidAsync("clipboardViewer.downloadFileFromStream", fileName, streamRef);
            _exportStatus = "Export complete";
        }
        catch (Exception ex)
        {
            _exportStatus = "Error: " + ex.Message;
        }
        finally
        {
            _exporting = false;
        }
    }
}