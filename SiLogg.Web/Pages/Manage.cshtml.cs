using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SiLogg.Core;

namespace SiLogg.Web.Pages;

public class ManageModel(SiLoggService service, IWebHostEnvironment environment) : PageModel
{
    [BindProperty]
    public string ImportFolder { get; set; } = string.Empty;

    public string? StatusMessage { get; private set; }

    public void OnGet()
    {
        ImportFolder = DefaultImportFolder();
        StatusMessage = TempData["StatusMessage"] as string;
    }

    public IActionResult OnPostImport()
    {
        try
        {
            var result = service.ImportFolder(ImportFolder);
            TempData["StatusMessage"] = $"Importerade {result.ImportedRows:N0} rader från {result.FilesRead} CSV-filer.";
            return RedirectToPage();
        }
        catch (DirectoryNotFoundException exception)
        {
            ModelState.AddModelError(nameof(ImportFolder), exception.Message);
            return Page();
        }
    }

    private string DefaultImportFolder() =>
        environment.IsDevelopment()
            ? Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "controlpost-dump"))
            : Path.Combine(AppContext.BaseDirectory, "controlpost-dump");
}
