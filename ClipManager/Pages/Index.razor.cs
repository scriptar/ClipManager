using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace ClipManager.Pages;
public partial class Index : ComponentBase
{
    [Inject] private IJSRuntime JsRuntime { get; set; } = default!;

    private DotNetObjectReference<Index>? _dotNetRef;
    private List<EntryView>? _entries = [];
    private List<string> _usernames = [];
    private List<string> _weeks = [];

    private EntryView? _selectedEntry;
    private bool _toastVisible;
    private int _currentPage = 1;
    private int _recordsPerPage = 5;
    private int _totalRecords;
    private int PageCount => (int)Math.Ceiling(_totalRecords / (double)_recordsPerPage);
    private bool CanGoPrev => _currentPage > 1;
    private bool CanGoNext => _currentPage < PageCount;
    private bool _loading = true;
    private string? _copyMessage;
    private ElementReference _toastElement;

    private string? _q;
    private string? _username;
    private string? _week;

    protected override async Task OnInitializedAsync()
    {
        var result = await Http.GetFromJsonAsync<FilterResult>("/api/clipboard/distinct");
        if (result is not null)
        {
            _usernames = result.Usernames;
            _weeks = result.Weeks;
        }
        await LoadEntriesAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _dotNetRef = DotNetObjectReference.Create(this);
            await JsRuntime.InvokeVoidAsync("clipboardViewer.registerEscapeHandler", _dotNetRef);

            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromMinutes(2));
                    var result = await Http.GetFromJsonAsync<FilterResult>("/api/clipboard/distinct");
                    if (result is not null)
                    {
                        _usernames = result.Usernames;
                        _weeks = result.Weeks;
                        await InvokeAsync(StateHasChanged);
                    }
                }
            });
        }
    }

    public void Dispose()
    {
        _ = JsRuntime.InvokeVoidAsync("clipboardViewer.unregisterEscapeHandler");
        _dotNetRef?.Dispose();
    }

    [JSInvokable]
    public void OnEscapePressed()
    {
        if (_toastVisible)
        {
            HideToast();
        }
    }

    private async Task LoadEntriesAsync()
    {
        _loading = true;
        try
        {
            await SearchEntriesAsync();
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    private async Task SearchEntriesAsync()
    {
        var url = $"/api/clipboard?page={_currentPage}&pageSize={_recordsPerPage}";
        if (!string.IsNullOrWhiteSpace(_q)) url += $"&q={Uri.EscapeDataString(_q)}";
        if (!string.IsNullOrWhiteSpace(_username)) url += $"&username={Uri.EscapeDataString(_username)}";
        if (!string.IsNullOrWhiteSpace(_week)) url += $"&week={Uri.EscapeDataString(_week)}";

        var resp = await Http.GetFromJsonAsync<ApiResult>(url);
        _totalRecords = resp?.Total ?? 0;
        _entries = resp?.Items.Select(i => new EntryView
        {
            Id = i.Id,
            Data = i.Data,
            ImagePath = i.ImagePath,
            Username = i.Username,
            Workstation = i.Workstation,
            Week = i.Week,
            Timestamp = i.Timestamp
        }).ToList();

        if (_entries != null)
        {
            foreach (var e in _entries.Where(x => !string.IsNullOrEmpty(x.ImagePath)))
            {
                try
                {
                    var r = await Http.GetFromJsonAsync<ImageUrlResult>($"/api/clipboard/{e.Id}/imageurl");
                    if (r != null)
                    {
                        e.ImageUrl = r.Url ?? null;
                    }
                }
                catch
                {
                    // ignore inaccessible images
                }
            }
        }
    }

    private async Task CopyTextToClipboardAsync(string text)
    {
        await JsRuntime.InvokeVoidAsync("clipboardViewer.copyText", text);
        _copyMessage = "Text copied!";
        StateHasChanged();
        await Task.Delay(1500);
        _copyMessage = null;
        StateHasChanged();
    }

    private async Task CopyImageToClipboardAsync(string imageUrl)
    {
        await JsRuntime.InvokeVoidAsync("clipboardViewer.copyImage", imageUrl);
        _copyMessage = "Image copied!";
        StateHasChanged();
        await Task.Delay(1500);
        _copyMessage = null;
        StateHasChanged();
    }

    private async Task OnFilterChangedAsync(ChangeEventArgs e)
    {
        _currentPage = 1;
        await LoadEntriesAsync();
    }

    private async Task ShowToastAsync(EntryView entry)
    {
        _selectedEntry = entry;
        _toastVisible = true;
        StateHasChanged();
        // wait until toast is rendered, then focus it
        await Task.Yield();
        await JsRuntime.InvokeVoidAsync("clipboardViewer.focusElement", _toastElement);
    }

    private void HideToast()
    {
        _toastVisible = false;
        _selectedEntry = null;
        StateHasChanged();
    }
    private async Task OnEntryKeyDownAsync(KeyboardEventArgs e, int index, EntryView entry)
    {
        switch (e.Key)
        {
            case "Enter" or " " or "Spacebar":
                await ShowToastAsync(entry);
                break;
            case "ArrowUp":
                await MoveFocusAsync(index - 1);
                break;
            case "ArrowDown":
                await MoveFocusAsync(index + 1);
                break;
            case "PageUp":
                HideToast();
                if (!CanGoPrev) return;
                await PrevPageAsync();
                await Task.Yield();
                await JsRuntime.InvokeVoidAsync("clipboardViewer.focusFirstRow");
                break;
            case "PageDown":
                HideToast();
                if (!CanGoNext) return;
                await NextPageAsync();
                await Task.Yield();
                await JsRuntime.InvokeVoidAsync("clipboardViewer.focusFirstRow");
                break;
        }
    }

    private async Task MoveFocusAsync(int targetIndex)
    {
        if (targetIndex < 0)
        {
            if (!CanGoPrev) return;
            await PrevPageAsync();
            await Task.Yield();
            await JsRuntime.InvokeVoidAsync("clipboardViewer.focusLastRow");
        }
        else if (targetIndex >= _entries!.Count)
        {
            if (!CanGoNext) return;
            await NextPageAsync();
            await Task.Yield();
            await JsRuntime.InvokeVoidAsync("clipboardViewer.focusFirstRow");
        }
        else
        {
            await JsRuntime.InvokeVoidAsync("clipboardViewer.focusRow", targetIndex);
        }
    }

    private async Task PrevPageAsync()
    {
        if (!CanGoPrev) return;
        _currentPage--;
        await LoadEntriesAsync();
    }

    private async Task NextPageAsync()
    {
        if (!CanGoNext) return;
        _currentPage++;
        await LoadEntriesAsync();
    }

    private void HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key != "Enter") return;
        _currentPage = 1;
        _ = LoadEntriesAsync();
    }

    private void Refresh()
    {
        _currentPage = 1;
        _ = LoadEntriesAsync();
    }

    private static string GetImageFileName(EntryView e)
    {
        if (string.IsNullOrEmpty(e.ImagePath) && string.IsNullOrEmpty(e.ImageUrl))
        {
            return "Clipboard image";
        }

        var path = e.ImagePath ?? e.ImageUrl ?? string.Empty;
        try
        {
            return path;
            //return Path.GetFileName(path);
        }
        catch
        {
            var lastSlash = path.LastIndexOfAny(['/', '\\']);
            return lastSlash >= 0 && lastSlash < path.Length - 1
                ? path[(lastSlash + 1)..]
                : "Clipboard image";
        }
    }

    private class EntryView
    {
        public int Id { get; set; }
        public string? Data { get; set; }
        public string? ImagePath { get; set; }
        public string? ImageUrl { get; set; }
        public string? Username { get; set; }
        public string? Workstation { get; set; }
        public string? Week { get; set; }
        public DateTime Timestamp { get; set; }
    }

    private class ApiResult
    {
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public List<ItemDto> Items { get; set; } = [];
    }

    private class FilterResult
    {
        public List<string> Usernames { get; set; } = [];
        public List<string> Weeks { get; set; } = [];
    }

    private class ItemDto
    {
        public int Id { get; set; }
        public string? Data { get; set; }
        public string? ImagePath { get; set; }
        public string? Username { get; set; }
        public string? Workstation { get; set; }
        public string? Week { get; set; }
        public DateTime Timestamp { get; set; }
    }

    private class ImageUrlResult
    {
        public string? Url { get; set; }
        public string? Path { get; set; }
    }
}

