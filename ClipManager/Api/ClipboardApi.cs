using ClipManager.Data;
using Microsoft.EntityFrameworkCore;

namespace ClipManager.Api
{
    public static class ClipboardApi
    {
        public static void MapClipboardApi(this WebApplication app)
        {
            var group = app.MapGroup("/api/clipboard");

            group.MapGet("/", async (ClipboardDbContext db, int? page, int? pageSize,
                string? q, string? username, string? week, string? workstation) =>
            {
                var query = db.ClipboardEntries.AsQueryable();

                if (!string.IsNullOrWhiteSpace(q))
                {
                    query = query.Where(e => (e.Data ?? "").Contains(q) || (e.ImagePath ?? "").Contains(q));
                }

                if (!string.IsNullOrWhiteSpace(username))
                    query = query.Where(e => (e.Username ?? "").Contains(username));
                if (!string.IsNullOrWhiteSpace(week))
                    query = query.Where(e => (e.Week ?? "").Contains(week));
                if (!string.IsNullOrWhiteSpace(workstation))
                    query = query.Where(e => (e.Workstation ?? "").Contains(workstation));
                query = query.OrderByDescending(e => e.Timestamp);

                var pg = page.GetValueOrDefault(1);
                var ps = Math.Clamp(pageSize.GetValueOrDefault(50), 1, 1000);
                var total = await query.CountAsync();
                var items = await query.Skip((pg - 1) * ps).Take(ps).ToListAsync();

                return Results.Ok(new { total, page = pg, pageSize = ps, items });
            });

            group.MapGet("/{id:int}", async (ClipboardDbContext db, int id) =>
            {
                var e = await db.ClipboardEntries.FindAsync(id);
                return e is not null ? Results.Ok(e) : Results.NotFound();
            });

            group.MapGet("/{id:int}/imageurl", async (ClipboardDbContext db, IConfiguration config, int id) =>
            {
                var e = await db.ClipboardEntries.FindAsync(id);
                if (e is null || string.IsNullOrEmpty(e.ImagePath)) return Results.NotFound();

                // if image path is inside configured imageBasePath, return a normalized relative /images/... URL
                var full = Path.GetFullPath(e.ImagePath);
                var imageBasePath = config.GetValue<string>("Clipboard:ImageBasePath") ?? "./images";
                var baseFull = Path.GetFullPath(imageBasePath);

                if (full.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
                {
                    var relative = full.Substring(baseFull.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    //var url = $"/images/{relative.Replace(Path.DirectorySeparatorChar, '/')}";
                    var url =
                        $"/images/{Uri.EscapeDataString(relative.Replace(Path.DirectorySeparatorChar, '/')).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase)}";

                    return Results.Ok(new { url });
                }

                // otherwise, return the raw path (client won't be able to access it via /images)
                return Results.Ok(new { path = e.ImagePath });
            });

            group.MapGet("/distinct", async (ClipboardDbContext db) =>
            {
                var usernames = await db.ClipboardEntries.AsQueryable()
                    .Select(e => e.Username)
                    .Where(u => !string.IsNullOrEmpty(u))
                    .Distinct()
                    .OrderBy(u => u)
                    .ToListAsync();

                var weeks = await db.ClipboardEntries.AsQueryable()
                    .Select(e => e.Week)
                    .Where(w => !string.IsNullOrEmpty(w))
                    .Distinct()
                    .OrderBy(w => w)
                    .ToListAsync();

                return Results.Ok(new { Usernames = usernames, Weeks = weeks });
            });
        }
    }
}