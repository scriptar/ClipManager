using ClipManager.Models;
using Microsoft.EntityFrameworkCore;

namespace ClipManager.Data
{
    public class ClipboardDbContext(DbContextOptions<ClipboardDbContext> options, IConfiguration config)
        : DbContext(options)
    {
        private readonly string _tableName = config["Clipboard:TableName"] ?? "clip";

        public DbSet<ClipboardEntry> ClipboardEntries => Set<ClipboardEntry>();
        public DbSet<ImportEntry> ImportEntries => Set<ImportEntry>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.Properties<string>().UseCollation("NOCASE");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ClipboardEntry>().ToTable(_tableName);
            modelBuilder.Entity<ClipboardEntry>()
                .Property(c => c.Timestamp)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        }
    }
}