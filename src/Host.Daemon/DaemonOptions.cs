namespace RigForge.Host.Daemon;

public sealed class DaemonOptions
{
    public const string SectionName = "Daemon";

    /// <summary>
    /// Browser origins allowed to talk to the daemon (besides the daemon's own origin, which serves the built UI).
    /// Everything else is rejected so that a random web page cannot drive your fans through localhost.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = ["http://localhost:5173", "http://127.0.0.1:5173"];
}
