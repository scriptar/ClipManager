using ClipManager.Api;
using ClipManager.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// configs
var configuration = builder.Configuration;
var imageBasePath = configuration.GetValue<string>("Clipboard:ImageBasePath") ?? "./images";
if (Program.ConfigureDatabaseOverride is null)
{
    var connection = configuration.GetConnectionString("ClipboardDb") ?? "Data Source=./clipboard-history.db";
    builder.Services.AddDbContext<ClipboardDbContext>(options => options.UseSqlite(connection));
}
else
{
    // Test override: let the test project configure the database
    Program.ConfigureDatabaseOverride(builder.Services);
}

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ClipboardContextFactory>();
builder.Services.AddScoped(sp =>
{
    var navigation = sp.GetRequiredService<NavigationManager>();
    return new HttpClient { BaseAddress = new Uri(navigation.BaseUri) };
});

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// allow UI to call the API
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

// ensure the main database is created
using (var scope = app.Services.CreateScope())
{
    var mainDbContext = scope.ServiceProvider.GetRequiredService<ClipboardDbContext>();
    await mainDbContext.Database.EnsureCreatedAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapClipboardApi();
app.MapClipboardExportsApi();
app.MapClipboardImportsApi();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

// static file serving for images (exposes imageBasePath).
if (Directory.Exists(imageBasePath))
{
    var provider = new FileExtensionContentTypeProvider
    {
        Mappings =
        {
            [".svg"] = "image/svg+xml"
        }
    };
    var contentRoot = builder.Environment.ContentRootPath;
    imageBasePath = Path.GetFullPath(Path.Combine(contentRoot, imageBasePath));
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(imageBasePath),
        RequestPath = "/images",
        ContentTypeProvider = provider
    });
}

app.Run();

// Required for WebApplicationFactory
public partial class Program { }