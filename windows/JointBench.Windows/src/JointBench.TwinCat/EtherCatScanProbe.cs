using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace JointBench.TwinCat;

public sealed class EtherCatScanProbe
{
    private const string TwinCatProjectTemplate = @"C:\TwinCAT\3.1\Components\Base\PrjTemplate\TwinCAT Project.tsproj";

    public EtherCatScanReport Scan(string progId = "TcXaeShell.DTE.15.0")
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"jointbench-tc-scan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        object? dte = null;
        try
        {
            dte = ComAutomation.Create(progId);
            ComAutomation.TrySet(dte, "SuppressUI", true);
            ComAutomation.TrySet(dte, "UserControl", false);
            Thread.Sleep(TimeSpan.FromSeconds(5));

            var solution = ComAutomation.Get(dte, "Solution")
                ?? throw new InvalidOperationException("DTE Solution object is not available.");
            ComAutomation.Retry(() => ComAutomation.Invoke(solution, "Create", tempRoot, "JointBenchScanSpike"));
            var project = ComAutomation.Retry(() => ComAutomation.Invoke(
                solution,
                "AddFromTemplate",
                TwinCatProjectTemplate,
                tempRoot,
                "JointBenchScanSpike.tsproj"));
            Thread.Sleep(TimeSpan.FromSeconds(6));

            var sysManager = ComAutomation.Get(project, "Object")
                ?? throw new InvalidOperationException("TwinCAT project object is not available.");
            var ioDevices = ComAutomation.Invoke(sysManager, "LookupTreeItem", "TIID")
                ?? throw new InvalidOperationException("TwinCAT I/O Devices tree item TIID was not found.");

            var foundXml = Convert.ToString(ComAutomation.Invoke(ioDevices, "ProduceXml", false)) ?? string.Empty;
            var foundPath = Path.Combine(tempRoot, "found-devices.xml");
            File.WriteAllText(foundPath, foundXml);

            var foundDoc = XDocument.Parse(foundXml);
            var masters = new List<EtherCatMasterInfo>();
            var boxes = new List<EtherCatBoxInfo>();
            var masterIndex = 0;

            foreach (var deviceNode in foundDoc.Descendants().Where(element => element.Name.LocalName == "Device"))
            {
                masterIndex++;
                var itemSubType = IntValue(deviceNode, "ItemSubType");
                var itemSubTypeName = Text(deviceNode, "ItemSubTypeName");
                var pnp = deviceNode.Descendants().FirstOrDefault(element => element.Name.LocalName == "Pnp");
                var addressInfo = deviceNode.Elements().FirstOrDefault(element => element.Name.LocalName == "AddressInfo")
                    ?? throw new InvalidOperationException("Found EtherCAT device is missing AddressInfo.");

                masters.Add(new EtherCatMasterInfo(
                    masterIndex,
                    itemSubType,
                    itemSubTypeName,
                    Text(pnp, "DeviceDesc"),
                    Text(pnp, "DeviceName"),
                    Text(pnp, "DeviceData")));

                var masterItem = ComAutomation.Invoke(ioDevices, "CreateChild", $"Device_{masterIndex}_EtherCAT", itemSubType, string.Empty, null)
                    ?? throw new InvalidOperationException("Failed to create temporary EtherCAT master item.");
                ComAutomation.Invoke(
                    masterItem,
                    "ConsumeXml",
                    $"<TreeItem><DeviceDef>{addressInfo}</DeviceDef></TreeItem>");
                ComAutomation.Invoke(
                    masterItem,
                    "ConsumeXml",
                    "<TreeItem><DeviceDef><ScanBoxes>1</ScanBoxes></DeviceDef></TreeItem>");
                Thread.Sleep(TimeSpan.FromSeconds(2));

                var childCount = Convert.ToInt32(ComAutomation.Get(masterItem, "ChildCount"));
                for (var childIndex = 1; childIndex <= childCount; childIndex++)
                {
                    var child = ComAutomation.GetIndexed(masterItem, "Child", childIndex)
                        ?? throw new InvalidOperationException($"Failed to read EtherCAT child {childIndex}.");
                    var childXml = Convert.ToString(ComAutomation.Invoke(child, "ProduceXml", true)) ?? string.Empty;
                    var childPath = Path.Combine(tempRoot, $"master-{masterIndex}-box-{childIndex}.xml");
                    File.WriteAllText(childPath, childXml);

                    var childDoc = XDocument.Parse(childXml);
                    var treeItem = childDoc.Root ?? throw new InvalidOperationException("Box XML has no root element.");
                    var info = treeItem.Descendants().FirstOrDefault(element => element.Name.LocalName == "Info");
                    var slave = treeItem.Descendants().FirstOrDefault(element => element.Name.LocalName == "Slave");

                    boxes.Add(new EtherCatBoxInfo(
                        masterIndex,
                        childIndex,
                        Text(treeItem, "ItemName"),
                        Text(treeItem, "PathName"),
                        IntValue(treeItem, "ItemSubType"),
                        Text(treeItem, "ItemSubTypeName"),
                        IntValue(info, "VendorId"),
                        IntValue(info, "ProductCode"),
                        IntValue(info, "RevisionNo"),
                        IntValue(info, "SerialNo"),
                        IntValue(info, "PhysAddr"),
                        IntValue(info, "AutoIncAddr"),
                        Text(slave, "EsiFile"),
                        childPath));
                }
            }

            return new EtherCatScanReport(
                true,
                string.Empty,
                tempRoot,
                foundPath,
                masters,
                boxes,
                boxes.Any(box => box.IsTi5));
        }
        catch (Exception exc)
        {
            return new EtherCatScanReport(false, ExceptionChain(exc), tempRoot, null, [], [], false);
        }
        finally
        {
            if (dte is not null)
            {
                try
                {
                    var solution = ComAutomation.Get(dte, "Solution");
                    ComAutomation.Invoke(solution, "Close", false);
                }
                catch
                {
                    // Ignore cleanup errors after a failed scan.
                }

                try
                {
                    ComAutomation.Invoke(dte, "Quit");
                }
                catch
                {
                    // Ignore cleanup errors after a failed scan.
                }

                try
                {
                    Marshal.FinalReleaseComObject(dte);
                }
                catch
                {
                    // Already released.
                }
            }
        }
    }

    private static string Text(XContainer? container, string localName)
    {
        if (container is null)
        {
            return string.Empty;
        }

        return container
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == localName)
            ?.Value
            .Trim() ?? string.Empty;
    }

    private static int IntValue(XContainer? container, string localName)
    {
        var text = Text(container, localName);
        return int.TryParse(text, out var value) ? value : 0;
    }

    private static string ExceptionChain(Exception exc)
    {
        var messages = new List<string>();
        for (var current = exc; current is not null; current = current.InnerException)
        {
            messages.Add($"{current.GetType().Name}: {current.Message}");
        }

        return string.Join(" -> ", messages);
    }
}
