using DbClipExporter;
using Microsoft.Data.Sqlite;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

if (args.Length < 2)
{
    Console.WriteLine($"Usage: {Assembly.GetExecutingAssembly().GetName().Name} <dbPath> <imagesDir> [outputDir]");
    return;
}

var dbPath = Path.GetFullPath(args[0]);
var imagesDir = Path.GetFullPath(args[1]);
var outputDir = args.Length > 2 ? args[2] : Directory.GetCurrentDirectory();
Directory.CreateDirectory(outputDir);

if (!File.Exists(dbPath))
{
    Console.Error.WriteLine($"Database not found: {dbPath}");
    return;
}

if (!Directory.Exists(imagesDir))
{
    Console.Error.WriteLine($"Image directory not found: {imagesDir}");
    return;
}

Console.WriteLine("Computing source hash...");
var sourceHash = HashHelper.ComputeSourceHash(dbPath, imagesDir);

Console.WriteLine("Creating manifest...");
var manifest = new ClipboardManifest
{
    Version = "1.0",
    ExportedByUser = Environment.UserName,
    Workstation = Environment.MachineName,
    ExportedAtUtc = DateTime.UtcNow,
    ImagesFolder = "images",
    DatabaseFile = HashHelper.ExportDbName,
    EntryCount = CountEntries(dbPath, out var imagePaths),
    Notes = "Clipboard export created automatically",
    SourceHash = sourceHash
};



// force SQLite to release any file lock
GC.Collect();
GC.WaitForPendingFinalizers();

var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
var manifestPath = Path.Combine(outputDir, "manifest.json");
File.WriteAllText(manifestPath, manifestJson);

var exportPath = Path.Combine(outputDir, $"clipboard_export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip");
Console.WriteLine($"Creating archive: {exportPath}");

using (var zip = ZipFile.Open(exportPath, ZipArchiveMode.Create))
{
    zip.CreateEntryFromFile(dbPath, manifest.DatabaseFile);
    zip.CreateEntryFromFile(manifestPath, "manifest.json");
    //foreach (var img in Directory.EnumerateFiles(imagesDir))
    //{
    //    zip.CreateEntryFromFile(img, Path.Combine("images", Path.GetFileName(img)));
    //}
    foreach (var relPath in imagePaths.Distinct())
    {
        var weekInPath = HashHelper.TryExtractWeekFromPath(relPath, out var weekPath);
        zip.CreateEntryFromFile(relPath,
            weekInPath
                ? Path.Combine("images", weekPath, Path.GetFileName(relPath))
                : Path.Combine("images", Path.GetFileName(relPath)));
    }
}

Console.WriteLine($"Export complete: {exportPath}");
File.Delete(manifestPath);
return;

static int CountEntries(string dbPath, out List<string> imagePaths)
{
    imagePaths = [];
    try
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False;");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT image_path FROM clip;";
        var reader = cmd.ExecuteReader();
        var count = 0;
        while (reader.Read())
        {
            var imagePath = reader["image_path"] as string;
            if (!string.IsNullOrEmpty(imagePath)) imagePaths.Add(imagePath);
            count++;
        }
        return count;
    }
    catch
    {
        return 0;
    }
}