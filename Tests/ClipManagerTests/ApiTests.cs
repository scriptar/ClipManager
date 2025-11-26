using ClipManager.Data;
using ClipManager.Models;
using ClipManagerTests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Dynamic;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ClipManagerTests;

[TestFixture]
public class ApiTests
{
    private readonly TestFactory _factory = new();
    private HttpClient _client = null!;

    [SetUp]
    public void Setup()
    {
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void Teardown()
    {
        Program.ConfigureDatabaseOverride = null;
        _client.Dispose();
    }

    [Test]
    public async Task GetClipboardHistory_ReturnsSuccess()
    {
        const int pageSize = 10;
        var response = await _client.GetAsync($"/api/clipboard?pageSize={pageSize}");
        Assert.That(response.IsSuccessStatusCode, Is.True);

        dynamic? result = await response.Content.ReadFromJsonAsync<ExpandoObject>();
        Assert.That(result, Is.Not.Null);
        Assert.That(int.Parse(result?.pageSize.ToString()), Is.EqualTo(pageSize));
    }

    [Test]
    public async Task ExportApi_Returns_Valid_Zip_Stream()
    {
        const string username = "Tester";
        const string week = "2025-W22";
        // save a record into an in-memory database (this does not change the real production database)
        using (var scope = _factory.Services.CreateScope())
        {
            await using var db = scope.ServiceProvider.GetRequiredService<ClipboardDbContext>();
            db.ClipboardEntries.Add(new ClipboardEntry
            {
                Data = "Hello World",
                Username = username,
                Workstation = "WS",
                Week = week,
                Timestamp = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        // create a temp directory to hold the exported database
        var tempDir = FileHelpers.CreateTempDirectory();
        var debugZipPath = Path.Combine(tempDir, "export.test");
        var shouldDelete = true;

        try
        {
            // create an export database ZIP file from the main in-memory database
            var response = await _client.GetAsync("/api/exports/create");
            Assert.That(response.IsSuccessStatusCode, Is.True);
            var mediaType = response.Content.Headers.ContentType?.MediaType;

            await using var zipStream = await response.Content.ReadAsStreamAsync();
            using var mem = new MemoryStream();
            await zipStream.CopyToAsync(mem);

            // if the data returned is not a ZIP file, fail
            var bytes = mem.ToArray();
            if (mediaType != "application/zip" ||
                bytes.Length > 0 && (bytes[0] == '\r' || bytes[0] == '\n' || bytes[0] == '<'))
            {
                var text = Encoding.UTF8.GetString(bytes);
                Console.WriteLine($"Server returned {mediaType}, not application/zip:");
                Console.WriteLine(text);
                Assert.Fail($"Server returned {mediaType} instead of ZIP");
            }

            // write the ZIP file from the service to disk for investigation
            await File.WriteAllBytesAsync(debugZipPath, mem.ToArray());
            mem.Position = 0;

            // if the ZIP file does not have a manifest or a database, fail (but don't delete the ZIP file for further investigation)
            shouldDelete = false;
            using var archive = new ZipArchive(mem, ZipArchiveMode.Read);
            Assert.That(archive.Entries.Any(e => e.FullName == "manifest.json"));
            Assert.That(archive.Entries.Any(e => e.FullName.EndsWith(".db")));
            shouldDelete = true;

            // check that username and week are in "distinct" lookup (used for datalist controls)
            var lookupResponse = await _client.GetAsync("/api/clipboard/distinct");
            Assert.That(lookupResponse.IsSuccessStatusCode, Is.True);
            dynamic? result = await lookupResponse.Content.ReadFromJsonAsync<ExpandoObject>();
            Assert.That(result?.usernames, Is.Not.Null);
            if (result.usernames.ValueKind == JsonValueKind.Array)
            {
                var items = result.usernames.GetRawText();
                Assert.That(items, Is.Not.EqualTo("[]"));
                Assert.That(items.Contains(username), Is.True);
            }
            Assert.That(result?.weeks, Is.Not.Null);
            if (result.weeks.ValueKind == JsonValueKind.Array)
            {
                var items = result.weeks.GetRawText();
                Assert.That(items, Is.Not.EqualTo("[]"));
                Assert.That(items.Contains(week), Is.True);
            }
        }
        catch
        {
            // do not delete the zip file on failure
            Console.WriteLine($"Test failed - keeping debug ZIP at: {debugZipPath}");
            throw;
        }
        finally
        {
            // if the ZIP file passed all tests, delete the directory containing it (and its ZIP file)
            if (shouldDelete && Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public async Task ImportApi_UploadImportMergeAndFetch_Success()
    {
        // ensure the master (in-memory) database is empty (this does not change the real production database)
        using var scope = _factory.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<ClipboardDbContext>();
        db.ClipboardEntries.RemoveRange(db.ClipboardEntries);
        db.ImportEntries.RemoveRange(db.ImportEntries);
        await db.SaveChangesAsync();
        Assert.That(await db.ClipboardEntries.CountAsync(), Is.Zero);

        // upload an exported database and add to the import table
        using var content = new MultipartFormDataContent();
        var filePath = Path.Combine("Resources", "export.test");
        var fileStream = new FileStream(filePath, FileMode.Open);
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(fileContent, "file", "export.zip");
        var uploadResponse = await _client.PostAsync("/api/imports/upload", content);
        Assert.That(uploadResponse.IsSuccessStatusCode, Is.True);

        // get the name of the uploaded database from the import table
        var importResult = await _client.GetFromJsonAsync<List<ImportRecord>>("/api/imports");
        var imports = importResult ?? [];
        Assert.That(imports.Count, Is.EqualTo(1));
        var name = imports.First().Name;

        // get the entries in the uploaded database
        var entriesResult = await _client.GetFromJsonAsync<List<ClipboardRecord>>($"/api/imports/{Uri.EscapeDataString(name)}/entries");
        var entries = entriesResult ?? [];
        Assert.That(entries.Count, Is.EqualTo(1));
        Assert.That(entries.First().Data, Is.EqualTo("Hello World"));

        // import the uploaded/imported database by name into the master database
        var mergeResponse = await _client.PostAsync($"/api/imports/{Uri.EscapeDataString(name)}/merge", null);
        Assert.That(mergeResponse.IsSuccessStatusCode, Is.True);
        //var mergeText = await mergeResponse.Content.ReadAsStringAsync();

        // get record(s) from the previously empty master database
        var mainDbResponse = await _client.GetAsync($"/api/clipboard");
        Assert.That(mainDbResponse.IsSuccessStatusCode, Is.True);
        Assert.That(await db.ClipboardEntries.CountAsync(), Is.Not.Zero);

        dynamic? result = await mainDbResponse.Content.ReadFromJsonAsync<ExpandoObject>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result?.items, Is.Not.Null);
        if (result.items.ValueKind == JsonValueKind.Array)
        {
            var items = result.items.GetRawText();
            Assert.That(items, Is.Not.EqualTo("[]"));
        }

        // delete import record
        var deleteResult = await _client.DeleteAsync($"/api/imports/{Uri.EscapeDataString(name)}");
        Assert.That(deleteResult.IsSuccessStatusCode, Is.True);
    }
}
