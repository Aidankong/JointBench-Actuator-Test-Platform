using System.IO.Compression;
using System.Xml.Linq;

namespace JointBench.TwinCat;

public sealed record ActiveTwinCatConfigReport(
    bool Ok,
    bool Ti5Found,
    string Message,
    string SourcePath);

public static class TwinCatActiveConfigProbe
{
    private const int Ti5VendorId = 0x00522227;
    private const int Ti5ProductCode = 0x00009253;
    private const int Ti5Revision = 0x00010005;

    public static ActiveTwinCatConfigReport Inspect(string? archivePath = null)
    {
        var path = archivePath ?? Path.Combine(@"C:\TwinCAT\3.1\Boot", "CurrentConfig.tszip");
        return InspectArchive(path);
    }

    public static ActiveTwinCatConfigReport InspectArchive(string archivePath)
    {
        if (!File.Exists(archivePath))
        {
            return new ActiveTwinCatConfigReport(false, false, $"Active TwinCAT configuration archive was not found: {archivePath}", archivePath);
        }

        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            foreach (var entry in archive.Entries.Where(entry => entry.FullName.EndsWith(".tsproj", StringComparison.OrdinalIgnoreCase)))
            {
                using var reader = new StreamReader(entry.Open());
                var xml = reader.ReadToEnd();
                if (ContainsTi5(xml))
                {
                    return new ActiveTwinCatConfigReport(true, true, "Active TwinCAT configuration contains Ti5.", archivePath);
                }
            }

            return new ActiveTwinCatConfigReport(true, false, "Active TwinCAT configuration does not contain Ti5.", archivePath);
        }
        catch (Exception exc) when (exc is IOException or InvalidDataException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return new ActiveTwinCatConfigReport(false, false, $"Active TwinCAT configuration could not be inspected: {exc.Message}", archivePath);
        }
    }

    private static bool ContainsTi5(string xml)
    {
        var doc = XDocument.Parse(xml);
        return doc
            .Descendants()
            .Where(element => element.Name.LocalName.Equals("EtherCAT", StringComparison.OrdinalIgnoreCase))
            .Any(element =>
                IntAttribute(element, "VendorId") == Ti5VendorId &&
                IntAttribute(element, "ProductCode") == Ti5ProductCode &&
                IntAttribute(element, "RevisionNo") == Ti5Revision);
    }

    private static int IntAttribute(XElement element, string name)
    {
        var text = element.Attribute(name)?.Value.Trim() ?? string.Empty;
        if (text.StartsWith("#x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, null, out var value) ? value : 0;
        }

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, null, out var value) ? value : 0;
        }

        return int.TryParse(text, out var decimalValue) ? decimalValue : 0;
    }
}
