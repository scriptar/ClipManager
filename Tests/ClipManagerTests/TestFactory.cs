using ClipManager.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClipManagerTests;

public class TestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Program.ConfigureDatabaseOverride = services =>
        {
            var descriptor =
                services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ClipboardDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            // create and register shared in-memory connection
            //var conn = new SqliteConnection("DataSource=file::memory:?cache=shared");
            var conn = new SqliteConnection("DataSource=:memory:");
            conn.Open();
            services.AddSingleton(conn);
            services.AddDbContext<ClipboardDbContext>(options => options.UseSqlite(conn));
        };
    }
}
