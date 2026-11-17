using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace RigForge.Host.Daemon.Hosting;

/// <summary>Fields read back from <c>schtasks /Query /TN RigForge /XML</c>.</summary>
public readonly record struct ParsedTaskInfo(
    bool SettingsEnabled,
    bool LogonTriggerEnabled,
    string? UserId,
    string? ExecutablePath,
    string? WorkingDirectory)
{
    /// <summary>Sign-in autostart is on only when the task and its logon trigger are both enabled.</summary>
    public bool AutostartEnabled => SettingsEnabled && LogonTriggerEnabled;
}

/// <summary>
/// Builds and reads the Task Scheduler document for <c>\RigForge</c>.
/// The task stays enabled and startable on demand; "start with Windows" flips only the logon trigger.
/// </summary>
public static class WindowsLogonTaskXml
{
    public const string TaskName = "RigForge";
    public const string NamespaceUri = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    public const string Description = "RigForge: RGB, fan/pump and AIO screen daemon";

    /// <summary>
    /// Interactive users (IU) and authenticated users (AU) may read and manually start the task
    /// (<c>FRFX</c>) at medium integrity, so <c>schtasks /Run /TN RigForge</c> does not prompt for UAC.
    /// </summary>
    public const string SecurityDescriptor = "D:P(A;;FA;;;BA)(A;;FA;;;SY)(A;;FRFX;;;IU)(A;;FRFX;;;AU)";

    public static string BuildXml(string executablePath, string workingDirectory, string userId, bool logonTriggerEnabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var builder = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = false,
            Encoding = Encoding.Unicode,
            Indent = false,
            NewLineHandling = NewLineHandling.None,
        };

        using (var writer = XmlWriter.Create(builder, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("Task", NamespaceUri);
            writer.WriteAttributeString("version", "1.2");

            writer.WriteStartElement("RegistrationInfo", NamespaceUri);
            WriteText(writer, "Description", Description);
            WriteText(writer, "SecurityDescriptor", SecurityDescriptor);
            writer.WriteEndElement();

            writer.WriteStartElement("Triggers", NamespaceUri);
            writer.WriteStartElement("LogonTrigger", NamespaceUri);
            WriteText(writer, "Enabled", Flag(logonTriggerEnabled));
            WriteText(writer, "UserId", userId);
            writer.WriteEndElement();
            writer.WriteEndElement();

            writer.WriteStartElement("Principals", NamespaceUri);
            writer.WriteStartElement("Principal", NamespaceUri);
            writer.WriteAttributeString("id", "Author");
            WriteText(writer, "UserId", userId);
            WriteText(writer, "LogonType", "InteractiveToken");
            WriteText(writer, "RunLevel", "HighestAvailable");
            writer.WriteEndElement();
            writer.WriteEndElement();

            writer.WriteStartElement("Settings", NamespaceUri);
            WriteText(writer, "MultipleInstancesPolicy", "IgnoreNew");
            WriteText(writer, "DisallowStartIfOnBatteries", "false");
            WriteText(writer, "StopIfGoingOnBatteries", "false");
            WriteText(writer, "AllowHardTerminate", "true");
            WriteText(writer, "StartWhenAvailable", "true");
            WriteText(writer, "RunOnlyIfNetworkAvailable", "false");
            WriteText(writer, "AllowStartOnDemand", "true");
            WriteText(writer, "Enabled", "true");
            WriteText(writer, "Hidden", "false");
            WriteText(writer, "RunOnlyIfIdle", "false");
            WriteText(writer, "WakeToRun", "false");
            WriteText(writer, "ExecutionTimeLimit", "PT0S");
            WriteText(writer, "Priority", "7");
            writer.WriteStartElement("RestartOnFailure", NamespaceUri);
            WriteText(writer, "Interval", "PT1M");
            WriteText(writer, "Count", "3");
            writer.WriteEndElement();
            writer.WriteEndElement();

            writer.WriteStartElement("Actions", NamespaceUri);
            writer.WriteAttributeString("Context", "Author");
            writer.WriteStartElement("Exec", NamespaceUri);
            WriteText(writer, "Command", executablePath);
            WriteText(writer, "WorkingDirectory", workingDirectory);
            writer.WriteEndElement();
            writer.WriteEndElement();

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return builder.ToString();
    }

    /// <summary>
    /// <paramref name="autostartMode"/> is <c>true</c>, <c>false</c>, or <c>preserve</c>.
    /// Preserve keeps the current logon trigger when the task already exists, and enables it otherwise.
    /// </summary>
    public static bool ResolveLogonTrigger(string autostartMode, bool taskExists, bool currentLogonTriggerEnabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(autostartMode);
        if (autostartMode.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (autostartMode.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (autostartMode.Equals("preserve", StringComparison.OrdinalIgnoreCase))
        {
            return taskExists ? currentLogonTriggerEnabled : true;
        }

        throw new ArgumentException($"Autostart mode '{autostartMode}' must be true, false, or preserve.", nameof(autostartMode));
    }

    public static bool TryParse(string xml, out ParsedTaskInfo info)
    {
        info = default;
        if (string.IsNullOrWhiteSpace(xml))
        {
            return false;
        }

        var start = xml.IndexOf('<', StringComparison.Ordinal);
        var end = xml.LastIndexOf('>');
        if (start < 0 || end <= start)
        {
            return false;
        }

        try
        {
            var document = XDocument.Parse(xml[start..(end + 1)], LoadOptions.None);
            var root = document.Root;
            if (root is null || !root.Name.LocalName.Equals("Task", StringComparison.Ordinal))
            {
                return false;
            }

            var logon = Child(Child(root, "Triggers"), "LogonTrigger");
            var settings = Child(root, "Settings");
            var exec = Child(Child(root, "Actions"), "Exec");
            info = new ParsedTaskInfo(
                SettingsEnabled: FlagValue(settings, "Enabled"),
                LogonTriggerEnabled: FlagValue(logon, "Enabled"),
                UserId: Text(logon, "UserId"),
                ExecutablePath: Text(exec, "Command"),
                WorkingDirectory: Text(exec, "WorkingDirectory"));
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static void WriteText(XmlWriter writer, string name, string value)
    {
        writer.WriteStartElement(name, NamespaceUri);
        writer.WriteString(value);
        writer.WriteEndElement();
    }

    private static string Flag(bool value) => value ? "true" : "false";

    private static XElement? Child(XElement? parent, string localName) =>
        parent?.Elements().FirstOrDefault(element => element.Name.LocalName.Equals(localName, StringComparison.Ordinal));

    private static string? Text(XElement? parent, string localName)
    {
        var value = Child(parent, localName)?.Value?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static bool FlagValue(XElement? parent, string localName) =>
        bool.TryParse(Text(parent, localName), out var enabled) && enabled;
}
