using SiLogg.Core;
using System.Globalization;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
var swedishCulture = CultureInfo.GetCultureInfo("sv-SE");
CultureInfo.DefaultThreadCurrentCulture = swedishCulture;
CultureInfo.DefaultThreadCurrentUICulture = swedishCulture;

// Add services to the container.
builder.Services.AddRazorPages();
var databasePath = builder.Environment.IsDevelopment()
    ? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "data", "si-logg.db"))
    : Path.Combine(AppContext.BaseDirectory, "data", "si-logg.db");
builder.Services.AddSingleton(new SiLoggService(databasePath));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

if (app.Environment.IsDevelopment())
{
    app.UseStaticFiles();
}
else
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(Path.Combine(AppContext.BaseDirectory, "wwwroot"))
    });
}

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();

app.Run();
