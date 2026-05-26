using System.Xml.Linq;

namespace JointBench.TwinCatHelper;

public sealed class EsiService
{
    public const string DefaultTwinCatEsiDirectory = @"C:\TwinCAT\3.1\Config\Io\EtherCAT";

    public EsiSummary ReadSummary(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("ESI XML path is required.", nameof(sourcePath));
        }

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("ESI XML file not found.", sourcePath);
        }

        if (!sourcePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"ESI file must be an XML file: {sourcePath}");
        }

        XDocument document;
        try
        {
            document = XDocument.Load(sourcePath, LoadOptions.PreserveWhitespace);
        }
        catch (Exception exc) when (exc is System.Xml.XmlException or IOException)
        {
            throw new InvalidOperationException($"ESI XML parse failed: {exc.Message}", exc);
        }

        var root = document.Root;
        if (root is null || root.Name.LocalName != "EtherCATInfo")
        {
            throw new InvalidOperationException("Selected XML is not an EtherCAT ESI file; root element must be EtherCATInfo.");
        }

        var vendor = Child(root, "Vendor");
        var descriptions = Child(root, "Descriptions");
        var device = descriptions?.Descendants().FirstOrDefault(element => element.Name.LocalName == "Device");
        var deviceType = Child(device, "Type");

        if (vendor is null || device is null || deviceType is null)
        {
            throw new InvalidOperationException("ESI XML is missing Vendor or Device/Type metadata.");
        }

        return new EsiSummary(
            Text(Child(vendor, "Name"), "UnknownVendor"),
            Text(Child(vendor, "Id"), "unknown"),
            Text(deviceType, "UnknownDevice"),
            Attribute(deviceType, "ProductCode", "unknown"),
            Attribute(deviceType, "RevisionNo", "unknown"));
    }

    public EsiInstallResult Install(string sourcePath, string? targetDirectory = null, bool dryRun = false)
    {
        var summary = ReadSummary(sourcePath);
        var targetRoot = targetDirectory
            ?? Environment.GetEnvironmentVariable("JOINTBENCH_TWINCAT_ESI_DIR")
            ?? DefaultTwinCatEsiDirectory;

        if (!Directory.Exists(targetRoot))
        {
            throw new DirectoryNotFoundException($"TwinCAT ESI directory does not exist: {targetRoot}");
        }

        var targetPath = Path.Combine(targetRoot, Path.GetFileName(sourcePath));
        if (!dryRun)
        {
            try
            {
                if (!Path.GetFullPath(sourcePath).Equals(Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(sourcePath, targetPath, overwrite: true);
                }
            }
            catch (UnauthorizedAccessException exc)
            {
                throw new InvalidOperationException(
                    $"Permission denied while installing ESI to {targetRoot}. Run the helper as administrator.",
                    exc);
            }
        }

        return new EsiInstallResult(sourcePath, targetPath, dryRun, summary);
    }

    private static XElement? Child(XContainer? element, string localName) =>
        element?.Elements().FirstOrDefault(child => child.Name.LocalName == localName);

    private static string Text(XElement? element, string defaultValue) =>
        string.IsNullOrWhiteSpace(element?.Value) ? defaultValue : element.Value.Trim();

    private static string Attribute(XElement element, string localName, string defaultValue)
    {
        var attribute = element.Attributes().FirstOrDefault(item => item.Name.LocalName == localName);
        return string.IsNullOrWhiteSpace(attribute?.Value) ? defaultValue : attribute.Value.Trim();
    }
}
