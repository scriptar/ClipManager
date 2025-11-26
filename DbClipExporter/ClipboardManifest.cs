
namespace DbClipExporter
{
    public class ClipboardManifest
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
}
