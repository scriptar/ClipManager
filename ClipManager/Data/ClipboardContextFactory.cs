using Microsoft.EntityFrameworkCore;

namespace ClipManager.Data;

public class ClipboardContextFactory
{
    public ClipboardDbContext Create(string databasePath, IConfiguration config, bool usePooling = true)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ClipboardDbContext>();
        optionsBuilder.UseSqlite($"Data Source={databasePath};Pooling={(usePooling ? "True" : "False")};");
        return new ClipboardDbContext(optionsBuilder.Options, config);
    }
}