using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DbClipExporter
{
    public static class HashHelper
    {
        public const string ExportDbName = "clipboard-history.db";

        private static readonly Regex WeekPathRegEx = new(@"^images[\\/](?<Week>\d+\-W\d+)[\\/]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public static bool TryExtractWeekFromPath(string relativeImagePath, out string week)
        {
            var match = WeekPathRegEx.Match(relativeImagePath);
            week = match.Success ? match.Groups["Week"].Value : string.Empty;
            return match.Success;
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
}
