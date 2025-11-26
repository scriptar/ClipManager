using ClipManager.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace ClipManagerTests.TestHelpers;

public static class TestDbConfig
{
    public static SqliteConnection CreateInMemoryConnection()
    {
        var conn = new SqliteConnection("DataSource=file::memory:?cache=shared");
        conn.Open();
        return conn;
    }

    public static void UseInMemoryDatabase()
    {
        // create and register shared in-memory connection
        Program.ConfigureDatabaseOverride = services =>
        {
            var conn = CreateInMemoryConnection();
            services.AddSingleton(conn);
            services.AddDbContext<ClipboardDbContext>(options => options.UseSqlite(conn));
        };
    }

    public static void Reset()
    {
        Program.ConfigureDatabaseOverride = null;
    }
}

