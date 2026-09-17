namespace SiLogg.Reader;

public sealed class ReaderOptions
{
    public string LicenseName { get; set; } = "";
    public string LicenseType { get; set; } = "NonCommercial";
    public string LicenseKey { get; set; } = "";
    public string UploadApiUrl { get; set; } = "";
    public string UploadApiKey { get; set; } = "";
    public string LocalOutputFolder { get; set; } = "";
    public int PollIntervalMs { get; set; } = 1000;
    public int DedupeWindowMinutes { get; set; } = 30;

    /// <summary>Default read mode at startup: "Remote" (default) or "Direct". Can be toggled at runtime in the UI.</summary>
    public string DefaultTargetMode { get; set; } = "Remote";

    /// <summary>When true, simulates a USB master + remote station instead of using the real SDK. For testing without hardware.</summary>
    public bool UseMockDevice { get; set; }
}
