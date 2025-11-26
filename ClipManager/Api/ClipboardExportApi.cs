using ClipManager.Data;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClipManager.Api;
public static class ClipboardExportApi
{
    private const string ExportDbName = "clipboard-history.db";

    public static void MapClipboardExportsApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/exports");

        group.MapGet("/create", CreateExport);
    }

    private static async Task<IResult> CreateExport(
        string? q,
        string? username,
        string? workstation,
        string? week,
        DateTime? startDate,
        DateTime? endDate,
        ClipboardDbContext mainDb,
        ClipboardContextFactory contextFactory,
        IConfiguration config,
        IWebHostEnvironment env
    )
    {
        // create export folders
        var dataRoot = Path.Combine(env.ContentRootPath, "db");
        var imagesDir = Path.Combine(dataRoot, "main", "images");
        var exportId = Guid.NewGuid().ToString("N");
        var exportDir = Path.Combine(dataRoot, "exports", exportId);
        Directory.CreateDirectory(exportDir);

        List<string> imagePaths = [];
        var entryCount = 0;
        // filter main data
        var query = mainDb.ClipboardEntries.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(c => EF.Functions.Like(c.Data ?? "", $"%{q}%") || EF.Functions.Like(c.ImagePath ?? "", $"%{q}%"));
        if (!string.IsNullOrWhiteSpace(username))
            query = query.Where(c => EF.Functions.Like(c.Username ?? "", $"%{username}%"));
        if (!string.IsNullOrWhiteSpace(week))
            query = query.Where(c => EF.Functions.Like(c.Week ?? "", $"%{week}%"));
        if (!string.IsNullOrWhiteSpace(workstation))
            query = query.Where(c => EF.Functions.Like(c.Workstation ?? "", $"%{workstation}%"));
        if (startDate.HasValue)
            query = query.Where(c => c.Timestamp >= startDate.Value);
        if (endDate.HasValue)
            query = query.Where(c => c.Timestamp <= endDate.Value);

        var records = await query.ToListAsync();
        entryCount = records.Count;
        imagePaths.AddRange(
            records.Select(item => item.ImagePath)
                .Where(imagePath => !string.IsNullOrEmpty(imagePath))!
            );

        // create new database in export dir
        var exportDbPath = Path.Combine(exportDir, ExportDbName);
        await using (var exportContext = contextFactory.Create(exportDbPath, config, false))
        {
            await exportContext.Database.EnsureCreatedAsync();

            //insert filtered rows into the new DB
            await exportContext.ClipboardEntries.AddRangeAsync(records);
            await exportContext.SaveChangesAsync();
            await exportContext.Database.CloseConnectionAsync();
        }

        // copy images
        var exportImagesDir = Path.Combine(exportDir, "images");
        Directory.CreateDirectory(exportImagesDir);

        foreach (var relPath in imagePaths.Distinct())
        {
            var weekInPath = ClipboardImportExportHelpers.TryExtractWeekFromPath(relPath, out var weekPath);
            var srcPath = weekInPath
                ? Path.Combine(imagesDir, weekPath, Path.GetFileName(relPath))
                : Path.Combine(imagesDir, Path.GetFileName(relPath));
            if (!File.Exists(srcPath)) continue;
            if (weekInPath) Directory.CreateDirectory(Path.Combine(exportImagesDir, weekPath));
            var destPath = weekInPath
                ? Path.Combine(exportImagesDir, weekPath, Path.GetFileName(srcPath))
                : Path.Combine(exportImagesDir, Path.GetFileName(srcPath));
            File.Copy(srcPath, destPath, overwrite: true);
        }

        // create manifest
        var manifest = new Manifest
        {
            Version = "1.0",
            ExportedByUser = Environment.UserName,
            Workstation = Environment.MachineName,
            ExportedAtUtc = DateTime.UtcNow,
            ImagesFolder = "images",
            DatabaseFile = ExportDbName,
            EntryCount = entryCount,
            Notes = "Clipboard export created automatically",
            SourceHash = ComputeSourceHash(exportDbPath, exportImagesDir)
        };

        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(exportDir, "manifest.json"), manifestJson);

        // force SQLite to release any file lock
        GC.Collect();
        GC.WaitForPendingFinalizers();

        // create zip
        var zipName = $"clipboard_export_{exportId}.zip";
        var zipPath = Path.Combine(dataRoot, "exports", zipName);
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        ZipFile.CreateFromDirectory(exportDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

        // clean up folder
        try
        {
            Directory.Delete(exportDir, recursive: true);
        }
        catch
        {
            // ignored
        }

        // return downloadable file
        //var stream = File.OpenRead(zipPath);
        //return Results.File(stream, "application/zip", zipName);
        return Results.File(zipPath, "application/zip", zipName);
    }

    public static string ComputeSourceHash(string dbPath, string imagesDir)
    {
        using var sha = SHA256.Create();

        // hash the DB file first
        AppendFileHash(sha, dbPath, relativeName: ExportDbName);

        if (Directory.Exists(imagesDir))
        {
            foreach (var file in Directory.EnumerateFiles(imagesDir).OrderBy(f => f))
            {
                var relative = Path.GetFileName(file);
                AppendFileHash(sha, file, relative);
            }
        }

        // finalize and return hex
        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    private static void AppendFileHash(SHA256 sha, string filePath, string relativeName)
    {
        // include filename in hash for determinism
        var nameBytes = Encoding.UTF8.GetBytes(relativeName);
        sha.TransformBlock(nameBytes, 0, nameBytes.Length, nameBytes, 0);

        // include file content
        using var stream = File.OpenRead(filePath);
        var buffer = new byte[8192];
        int bytesRead;
        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            sha.TransformBlock(buffer, 0, bytesRead, buffer, 0);
        }

        // finish one block
        sha.TransformBlock([], 0, 0, null, 0);
    }
}
