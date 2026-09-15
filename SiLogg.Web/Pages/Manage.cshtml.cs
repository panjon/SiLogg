using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SiLogg.Core;

namespace SiLogg.Web.Pages;

public class ManageModel(SiLoggService service, IWebHostEnvironment environment) : PageModel
{
    [BindProperty]
    public string ImportFolder { get; set; } = string.Empty;

    [BindProperty]
    public List<IFormFile> Files { get; set; } = [];

    public bool ShowFolderImport => environment.IsDevelopment();

    public IReadOnlyList<ImportedFileResult> ImportedFiles { get; private set; } = [];

    public string? StatusMessage { get; private set; }

    public void OnGet()
    {
        ImportFolder = DefaultImportFolder();
        ImportedFiles = service.ListImportedFiles();
        StatusMessage = TempData["StatusMessage"] as string;
    }

    public IActionResult OnPostClear()
    {
        service.ClearDatabase();
        TempData["StatusMessage"] = "Databasen är tömd.";
        return RedirectToPage();
    }

    public IActionResult OnPostDeleteFile(string sourceFile)
    {
        service.DeleteFile(sourceFile);
        TempData["StatusMessage"] = $"Tog bort data för {sourceFile}.";
        return RedirectToPage();
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
            ImportedFiles = service.ListImportedFiles();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostUploadAsync()
    {
        var csvFiles = Files.Where(file => file.Length > 0).ToList();
        if (csvFiles.Count == 0)
        {
            ModelState.AddModelError(nameof(Files), "Välj minst en CSV-fil.");
            ImportFolder = DefaultImportFolder();
            ImportedFiles = service.ListImportedFiles();
            return Page();
        }

        // Uploaded files only need to exist while ImportFolder reads them into SQLite.
        var tempFolder = Path.Combine(Path.GetTempPath(), "silogg-upload-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempFolder);
        try
        {
            foreach (var file in csvFiles)
            {
                var fileName = Path.GetFileName(file.FileName);
                if (!fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var destination = Path.Combine(tempFolder, fileName);
                await using var stream = System.IO.File.Create(destination);
                await file.CopyToAsync(stream);
            }

            var result = service.ImportFolder(tempFolder);
            TempData["StatusMessage"] = $"Importerade {result.ImportedRows:N0} rader från {result.FilesRead} CSV-filer.";
            return RedirectToPage();
        }
        finally
        {
            System.IO.Directory.Delete(tempFolder, recursive: true);
        }
    }

    private string DefaultImportFolder() =>
        environment.IsDevelopment()
            ? Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "controlpost-dump"))
            : Path.Combine(AppContext.BaseDirectory, "controlpost-dump");
}
