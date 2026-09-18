using Microsoft.Extensions.Configuration;

namespace SiLogg.Reader;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstanceMutex = new Mutex(true, "SiLogg.Reader.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "SiLogg Reader körs redan. Stäng den befintliga instansen innan du startar en ny.",
                "SiLogg Reader",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .Build();

        var options = new ReaderOptions();
        configuration.GetSection("Reader").Bind(options);
        options.LocalOutputFolder = Environment.ExpandEnvironmentVariables(
            string.IsNullOrWhiteSpace(options.LocalOutputFolder)
                ? Path.Combine(Path.GetTempPath(), "SiLogg", "readouts")
                : options.LocalOutputFolder);

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm(options));
    }
}
