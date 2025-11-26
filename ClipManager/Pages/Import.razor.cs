using ClipManager.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Dynamic;
using System.Net.Http.Headers;

namespace ClipManager.Pages;

public partial class Import : ComponentBase
{
    private IBrowserFile? _file;
    private bool _uploading;
    private string? _status;
    private List<ImportRecord>? _imports = [];
    private ImportRecord? _selectedImport;
    private List<ClipboardEntry>? _selectedEntries;
    private Manifest? _manifestPreview;

    protected override async Task OnInitializedAsync()
    {
        await RefreshImportsAsync();
    }

    private async Task RefreshImportsAsync()
    {
        var result = await Http.GetFromJsonAsync<List<ImportRecord>>("/api/imports");
        _imports = result ?? [];
        StateHasChanged();
    }

    private async Task HandleFileSelected(InputFileChangeEventArgs e)
    {
        _file = e.File;
        _status = null;
        _manifestPreview = null;
        if (_file == null) return;

        try
        {
            var manifest = await JsRuntime.InvokeAsync<Manifest?>("clipboardViewer.readManifest", "fileInput");
            if (manifest != null)
            {
                _manifestPreview = manifest;
                StateHasChanged();
            }
            else
            {
                _status = "No manifest.json found in the archive.";
            }
        }
        catch (Exception ex)
        {
            _status = "Error reading manifest: " + ex.Message;
        }
    }

    private async Task UploadFile()
    {
        if (_file == null) return;
        _uploading = true;
        _status = "Uploading...";

        try
        {
            using var content = new MultipartFormDataContent();
            var fileContent = new StreamContent(_file.OpenReadStream(maxAllowedSize: 1024 * 1024 * 200)); // 200 MB
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(_file.ContentType);
            content.Add(fileContent, "file", _file.Name);

            var response = await Http.PostAsync("/api/imports/upload", content);
            if (response.IsSuccessStatusCode)
            {
                _status = "Upload complete";
                await RefreshImportsAsync();
                _manifestPreview = null;
            }
            else
            {
                _status = "Upload failed: " + response.ReasonPhrase;
            }
        }
        catch (Exception ex)
        {
            _status = "Error uploading: " + ex.Message;
        }
        finally
        {
            _uploading = false;
        }
    }

    private void SelectImport(ImportRecord imp)
    {
        _selectedImport = imp;
        _selectedEntries = null;
    }

    private async Task PreviewImport(ImportRecord imp)
    {
        _selectedImport = imp;
        _selectedEntries =
            await Http.GetFromJsonAsync<List<ClipboardEntry>>($"api/imports/{Uri.EscapeDataString(imp.Name)}/entries");
    }

    private async Task MergeImport(ImportRecord imp)
    {
        var confirmed = await JsRuntime.InvokeAsync<bool>("confirm", $"Merge '{imp.Name}' in main database?");
        if (!confirmed) return;

        var response = await Http.PostAsync($"api/imports/{Uri.EscapeDataString(imp.Name)}/merge", null);
        if (response.IsSuccessStatusCode)
        {
            dynamic? responseObj = await response.Content.ReadFromJsonAsync<ExpandoObject>();
            await JsRuntime.InvokeVoidAsync("alert", $"{responseObj?.message ?? $"'{imp.Name}' merged successfully!"}");
        }
        else
        {
            await JsRuntime.InvokeVoidAsync("alert", $"Failed to merge '{imp.Name}'.");
        }
    }

    private async Task DeleteImport(ImportRecord imp)
    {
        var confirmed = await JsRuntime.InvokeAsync<bool>("confirm", $"Delete '{imp.Name}'?");
        if (!confirmed) return;

        var response = await Http.DeleteAsync($"api/imports/{Uri.EscapeDataString(imp.Name)}");
        if (response.IsSuccessStatusCode)
        {
            _imports!.Remove(imp);
            _selectedEntries = null;
            StateHasChanged();
        }
        else
        {
            await JsRuntime.InvokeVoidAsync("alert", $"Failed to delete '{imp.Name}'.");
        }
    }

    private void HandleRowKeyDown(KeyboardEventArgs e, ImportRecord imp)
    {
        int idx;
        switch (e.Key)
        {
            case "Enter" or " ":
                SelectImport(imp);
                break;
            case "ArrowUp":
                idx = _imports!.IndexOf(imp);
                if (idx > 0) SelectImport(_imports[idx - 1]);
                break;
            case "ArrowDown":
                idx = _imports!.IndexOf(imp);
                if (idx < _imports.Count - 1) SelectImport(_imports[idx + 1]);
                break;
        }
    }

    private class ClipboardEntry
    {
        public string? Data { get; set; }
        public string? ImagePath { get; set; }
        public string? Username { get; set; }
        public string? Workstation { get; set; }
        public string? Week { get; set; }
        public string? Timestamp { get; set; }
    }
}
