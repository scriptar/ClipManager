using ClipManager.Data;
using ClipManager.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Text.Json;

namespace ClipManager.Api;
public static class ClipboardImportApi
{
    private const bool PerformManifestValidation = true;

    public static void MapClipboardImportsApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/imports");

        group.MapGet("/", GetImports);
        group.MapGet("/{name}/entries", GetEntries);
        group.MapPost("/{name}/merge", MergeImport);
        group.MapDelete("/{name}", DeleteImport);
        group.MapPost("/upload", UploadImport);
        group.MapGet("/{importId}/images/{week}/{fileName}", UploadedImages);
    }

    private static string ImportDbPath(string name, IWebHostEnvironment env) => Path.Combine(env.ContentRootPath, "db", "imports", name);

    private static async Task<Manifest> LoadManifestJsonFileAsync(string importFolder)
    {
        var manifestPath = Path.Combine(importFolder, "manifest.json");

        Manifest manifest;
        if (File.Exists(manifestPath))
        {
            var json = await File.ReadAllTextAsync(manifestPath);
            manifest = JsonSerializer.Deserialize<Manifest>(json) ?? new Manifest();
        }
        else
        {
            manifest = new Manifest { ExportedByUser = "Unknown", Workstation = "Unknown" };
        }

        return manifest;
    }

    private static async Task<IResult> GetImports(ClipboardDbContext mainDb)
    {
        var query = await mainDb.ImportEntries.AsQueryable()
            .OrderByDescending(e => e.ImportedAt)
            .Select(i => new ImportRecord
            {
                Name = i.Name ?? "",
                ImportedAt = i.ImportedAt ?? DateTime.MinValue,
                ImportedBy = i.ImportedBy ?? "",
                Path = i.Path ?? "",
                EntryCount = i.EntryCount,
                Workstation = i.Workstation ?? ""
            }).ToListAsync();

        return Results.Ok(query);
    }

    private static async Task<IResult> GetEntries(string name, ClipboardContextFactory contextFactory,
        IConfiguration config, IWebHostEnvironment env)
    {
        var importFolder = ImportDbPath(name, env);

        var manifest = await LoadManifestJsonFileAsync(importFolder);
        var importedDbPath = Path.Combine(importFolder, manifest.DatabaseFile);
        if (!File.Exists(importedDbPath))
            return Results.NotFound($"Import '{name}' not found.");

        await using var importContext = contextFactory.Create(importedDbPath, config, false);
        var query = importContext.ClipboardEntries.AsQueryable();
        var entries = await query.Take(100).Select(c => new ClipboardRecord
        {
            Data = c.Data,
            ImagePath = !string.IsNullOrEmpty(c.ImagePath)
                ? Path.Combine("/api/imports/", name, c.ImagePath).Replace("\\", "/")
                : null,
            Username = c.Username,
            Workstation = c.Workstation,
            Week = c.Week,
            Timestamp = c.Timestamp.ToString("u")
        }).ToListAsync();
        await importContext.Database.CloseConnectionAsync();

        return Results.Ok(entries);
    }

    private static async Task<IResult> MergeImport(
        string name,
        HttpRequest request,
        ClipboardDbContext mainDb,
        ClipboardContextFactory contextFactory,
        IConfiguration config,
        IWebHostEnvironment env)
    {
        var importFolder = ImportDbPath(name, env);

        var manifest = await LoadManifestJsonFileAsync(importFolder);
        var importedDbPath = Path.Combine(importFolder, manifest.DatabaseFile);
        if (!File.Exists(importedDbPath))
            return Results.NotFound($"Import '{name}' not found.");

        var summary = await ClipboardImportExportHelpers.MergeIntoMainDbAsync(importedDbPath, manifest, mainDb, contextFactory, config, env);
        return Results.Ok(new
        {
            message = $"'{name}' merged into main database: {summary.Inserted} inserted, {summary.Skipped} skipped, {summary.Failed} failed."
        });
    }

    private static async Task<IResult> DeleteImport(string name, ClipboardDbContext mainDb, IWebHostEnvironment env)
    {
        var importFolder = ImportDbPath(name, env);
        if (!Directory.Exists(importFolder))
            return Results.NotFound($"Import '{name}' not found.");

        // force SQLite to release any file lock
        GC.Collect();
        GC.WaitForPendingFinalizers();

        Directory.Delete(importFolder, recursive: true);

        // remove from imports table
        await mainDb.ImportEntries.Where(i => i.Name == name).ExecuteDeleteAsync();
        await mainDb.SaveChangesAsync();

        return Results.Ok(new { message = $"Deleted import '{name}'." });
    }

    private static async Task<IResult> UploadImport(HttpRequest request, ClipboardDbContext mainDb,
        IWebHostEnvironment env)
    {
        if (!request.HasFormContentType)
            return Results.BadRequest("Expected multipart/form-data.");

        var form = await request.ReadFormAsync();
        var file = form.Files.FirstOrDefault();
        if (file is null || !file.FileName.EndsWith(".zip"))
            return Results.BadRequest("Missing or invalid file.");

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHHmmZ");
        var baseName = Path.GetFileNameWithoutExtension(file.FileName)
            .Replace(' ', '_')
            .Replace('.', '_')
            .Replace('/', '_')
            .Replace('\\', '_');

        var folderName = $"{timestamp}_{baseName}";
        var importPath = ImportDbPath(folderName, env);
        Directory.CreateDirectory(importPath);

        // save uploaded zip file (temporarily)
        var tmpPath = Path.Combine(importPath, file.FileName);
        await using (var stream = File.Create(tmpPath))
            await file.CopyToAsync(stream);

        try
        {
            ZipFile.ExtractToDirectory(tmpPath, importPath);
        }
        catch (Exception ex)
        {
            Directory.Delete(importPath, recursive: true);
            return Results.BadRequest($"Failed to extract archive: {ex.Message}");
        }

        // attempt to load manifest
        var manifest = await LoadManifestJsonFileAsync(importPath);
        var importedDbPath = Path.Combine(importPath, manifest.DatabaseFile);
        if (!File.Exists(importedDbPath))
            return Results.BadRequest("Imported database missing.");

        if (PerformManifestValidation)
        {
            var computed = ClipboardExportApi.ComputeSourceHash(importedDbPath,
                Path.Combine(importPath, manifest.ImagesFolder ?? "images"));
            if (computed != manifest.SourceHash)
                return Results.BadRequest("Export contents have been modified or corrupted.");
        }

        // verify integrity
        await using (var conn = new SqliteConnection($"Data Source={importedDbPath};Pooling=False;"))
        {
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check";
            var result = (string?)await cmd.ExecuteScalarAsync();
            await conn.CloseAsync();
            if (result != "ok") return Results.BadRequest("Corrupt database.");
        }

        // register in imports table
        await mainDb.ImportEntries.AddAsync(new ImportEntry
        {
            Name = folderName,
            ImportedAt = DateTime.UtcNow,
            ImportedBy = manifest.ExportedByUser ?? "Unknown",
            Path = Path.GetRelativePath(env.ContentRootPath, importPath),
            EntryCount = manifest.EntryCount,
            Workstation = manifest.Workstation ?? ""
        });
        await mainDb.SaveChangesAsync();

        // cleanup the uploaded zip
        File.Delete(tmpPath);

        return Results.Ok(new
        {
            message = $"Imported archive '{file.FileName}'",
            folder = folderName,
            manifest
        });
    }

    private static Task<IResult> UploadedImages(string importId, string week, string fileName, IWebHostEnvironment env)
    {
        var baseDir = Path.Combine(env.ContentRootPath, "db", "imports", importId, "images", week);
        var filePath = Path.Combine(baseDir, fileName);

        if (!File.Exists(filePath))
            return Task.FromResult(Results.NotFound());

        var contentType = fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                ? "image/jpeg"
                : "application/octet-stream";

        var stream = File.OpenRead(filePath);
        return Task.FromResult(Results.File(stream, contentType));
    }
}