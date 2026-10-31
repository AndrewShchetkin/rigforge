using System.CommandLine;
using RigForge.Tools.HardwareScanner;

var root = new RootCommand("rigforge-scan: hardware diagnostics for RigForge (USB HID devices, sensors, fan controllers).");

var vendor = new Option<string?>("--vid") { Description = "Only devices of this vendor id (hex), e.g. 1e71" };
var allVendors = new Option<bool>("--all") { Description = "List every HID device, not only NZXT (1e71) and ASUS (0b05)" };
var hid = new Command("hid", "List HID devices with report sizes, usage pages and support status (read-only).") { vendor, allVendors };
hid.SetAction(result => HidCommand.Run(result.GetValue(vendor), result.GetValue(allVendors)));

var dumpVendor = new Option<string>("--vid") { Description = "Vendor id (hex)", Required = true };
var dumpProduct = new Option<string>("--pid") { Description = "Product id (hex)", Required = true };
var reportCount = new Option<int>("--count", "-n") { Description = "Number of reports to print", DefaultValueFactory = _ => 8 };
var reportTimeout = new Option<int>("--timeout") { Description = "Milliseconds to wait for each report", DefaultValueFactory = _ => 1500 };
var hidDump = new Command("hid-dump", "Print raw input reports a device streams on its own (read-only, for protocol work).") { dumpVendor, dumpProduct, reportCount, reportTimeout };
hidDump.SetAction(result => HidDumpCommand.Run(result.GetValue(dumpVendor)!, result.GetValue(dumpProduct)!, result.GetValue(reportCount), result.GetValue(reportTimeout)));

var probeVerbose = Verbose();
var probe = new Command("probe", "Detect and initialize supported devices in dry-run mode (no lighting, fan or LCD writes).") { probeVerbose };
probe.SetAction((result, cancellationToken) => ProbeCommand.RunAsync(result.GetValue(probeVerbose), cancellationToken));

var watch = new Option<bool>("--watch", "-w") { Description = "Refresh every second until Ctrl+C" };
var sensorsVerbose = Verbose();
var sensors = new Command("sensors", "Read every sensor once, or continuously with --watch.") { watch, sensorsVerbose };
sensors.SetAction((result, cancellationToken) => SensorsCommand.RunAsync(result.GetValue(watch), result.GetValue(sensorsVerbose), cancellationToken));

var udevOutput = new Option<FileInfo?>("--output", "-o") { Description = "Write the rules to a file instead of stdout" };
var udev = new Command("udev", "Generate udev rules that grant the logged-in user access to supported controllers (Linux).") { udevOutput };
udev.SetAction(result => UdevCommand.Run(result.GetValue(udevOutput)));

var conflicts = new Command("conflicts", "List running software that would fight over the same hardware.");
conflicts.SetAction(_ => ConflictsCommand.Run());

var dumpOutput = new Option<FileInfo?>("--output", "-o") { Description = "Write the report to a file instead of stdout" };
var dumpVerbose = Verbose();
var dump = new Command("dump", "Full diagnostic report as JSON (HID devices, detected devices, sensors, conflicts).") { dumpOutput, dumpVerbose };
dump.SetAction((result, cancellationToken) => DumpCommand.RunAsync(result.GetValue(dumpOutput), result.GetValue(dumpVerbose), cancellationToken));

root.Add(hid);
root.Add(hidDump);
root.Add(probe);
root.Add(sensors);
root.Add(udev);
root.Add(conflicts);
root.Add(dump);

return await root.Parse(args).InvokeAsync();

static Option<bool> Verbose() => new("--verbose", "-v") { Description = "Show driver log output" };
