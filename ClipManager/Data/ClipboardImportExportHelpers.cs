using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ClipManager.Data;

public record MergeSummary(bool Success, int Inserted, int Skipped, int Failed);

public record ClipboardRecord
{
    public string? Data { get; set; }
    public string? ImagePath { get; set; }
    public string? Username { get; set; }
    public string? Workstation { get; set; }
    public string? Week { get; set; }
    public string? Timestamp { get; set; }
}

public record ImportRecord
{
    public string Name { get; set; } = "";
    public DateTime ImportedAt { get; set; }
    public string? ImportedBy { get; set; } = "";
    public string Path { get; set; } = "";
    public int EntryCount { get; set; }
    public string? Workstation { get; set; } = "";
}

public class Manifest
{
    public string Version { get; set; } = "1.0";
    public string ExportedByUser { get; set; } = "";
    public string Workstation { get; set; } = "";
    public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;
    public string? ImagesFolder { get; set; } = "images";
    public string DatabaseFile { get; set; } = "clipboard-history.db";
    public int EntryCount { get; set; }
    public string? Notes { get; set; }

    // for merge tracking
    public string? SourceHash { get; set; }
}

public static class ClipboardImportExportHelpers
{

    public static string ComputeHash(string? data, string? imagePath, string? timestamp)
    {
        using var sha = SHA256.Create();
        var input = $"{data ?? ""}|{imagePath ?? ""}|{timestamp ?? ""}";
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    private static readonly Regex WeekPathRegEx = new(@"^images[\\/](?<Week>\d+\-W\d+)[\\/]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    public static bool TryExtractWeekFromPath(string relativeImagePath, out string week)
    {
        var match = WeekPathRegEx.Match(relativeImagePath);
        week = match.Success ? match.Groups["Week"].Value : string.Empty;
        return match.Success;
    }

    public static async Task<MergeSummary> MergeIntoMainDbAsync(
        string uploadedDbPath,
        Manifest manifest,
        ClipboardDbContext mainDb,
        ClipboardContextFactory contextFactory,
        IConfiguration config,
        IWebHostEnvironment env
    )
    {
        var mainImagesPath = Path.Combine(env.ContentRootPath, "db", "main", "images");
        Directory.CreateDirectory(mainImagesPath);
        var uploadedImagesPath =
            Path.Combine(Path.GetDirectoryName(uploadedDbPath)!, manifest.ImagesFolder ?? "images");

        await using var uploadContext = contextFactory.Create(uploadedDbPath, config, false);
        await using var transaction = await mainDb.Database.BeginTransactionAsync();

        var inserted = 0;
        var skipped = 0;
        var failed = 0;

        var uploadData = uploadContext.ClipboardEntries.Select(c => c);
        foreach (var entry in uploadData)
        {
            var imagePath = entry.ImagePath;
            entry.ContentHash = ComputeHash(
                entry.Data,
                imagePath,
                ((DateTime)entry.Timestamp).ToString("yyyy-MM-dd HH:mm:ss")
            );

            // skip duplicates
            if (await mainDb.ClipboardEntries.AnyAsync(c => c.ContentHash.Equals(entry.ContentHash)))
            {
                skipped++;
                continue;
            }

            if (!string.IsNullOrEmpty(imagePath))
            {
                var weekInPath = TryExtractWeekFromPath(imagePath, out var weekPath);
                var oldPath = weekInPath
                    ? Path.Combine(uploadedImagesPath, weekPath, Path.GetFileName(imagePath))
                    : Path.Combine(uploadedImagesPath, Path.GetFileName(imagePath));
                if (File.Exists(oldPath))
                {
                    // Maybe overkill? It will just fail upon insert due to unique ContentHash...
                    //var newFileName = $"{Guid.NewGuid()}{Path.GetExtension(oldPath)}";
                    var newFileName = Path.GetFileName(oldPath);
                    var newPath = weekInPath
                        ? Path.Combine(mainImagesPath, weekPath, newFileName)
                        : Path.Combine(mainImagesPath, newFileName);
                    if (!File.Exists(newPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
                        File.Copy(oldPath, newPath, overwrite: false);
                    }
                    var newImageRelPath = Path.Combine("images", newFileName).Replace("\\", "/");
                    entry.ImagePath = newImageRelPath;
                    entry.ContentHash = ComputeHash(
                        entry.Data,
                        newImageRelPath,
                        ((DateTime)entry.Timestamp).ToString("yyyy-MM-dd HH:mm:ss")
                    );
                }
                else
                {
                    failed++;
                    continue;
                }
            }

            try
            {
                await mainDb.ClipboardEntries.AddAsync(entry);
                await mainDb.SaveChangesAsync();
                inserted++;
            }
            catch
            {
                failed++;
            }
        }

        await transaction.CommitAsync();
        return new MergeSummary(true, inserted, skipped, failed);
    }
}