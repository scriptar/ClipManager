namespace ClipManagerTests.TestHelpers;

public static class FileHelpers
{
    public static string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        return dir;
    }

    public static void SaveTempImage(string path)
    {
        File.WriteAllBytes(path, new byte[4096]); // placeholder image data
    }
}