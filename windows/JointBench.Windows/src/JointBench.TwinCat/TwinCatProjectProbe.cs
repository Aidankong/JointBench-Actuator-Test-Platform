using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace JointBench.TwinCat;

public sealed record TwinCatProjectTemplateSet(string PlcTemplateName, IReadOnlyList<string> PouTemplatePaths)
{
    public static TwinCatProjectTemplateSet FromRepositoryRoot(string repositoryRoot)
    {
        var root = ResolveRepositoryRoot(repositoryRoot);
        var sourceRoot = Path.Combine(root, "twincat", "src");
        return new TwinCatProjectTemplateSet(
            "Standard PLC Template",
            [
                Path.Combine(sourceRoot, "ST_JointBenchAds.TcDUT"),
                Path.Combine(sourceRoot, "ST_Ti5CiA402PdoInput.TcDUT"),
                Path.Combine(sourceRoot, "ST_Ti5CiA402PdoOutput.TcDUT"),
                Path.Combine(sourceRoot, "FB_JointBenchAxis.TcPOU"),
                Path.Combine(sourceRoot, "MAIN.TcPOU"),
            ]);
    }

    private static string ResolveRepositoryRoot(string start)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(start));
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "twincat", "src")) &&
                Directory.Exists(Path.Combine(directory.FullName, "windows", "JointBench.Windows")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Path.GetFullPath(start);
    }
}

public sealed record TwinCatProjectProbeReport(
    bool Ok,
    string Error,
    string TempRoot,
    string SolutionName,
    string PlcProjectName,
    IReadOnlyList<string> ImportedPouTemplates,
    bool PlcBuildSucceeded,
    Ti5PdoLinkPlan? LinkPlan,
    IReadOnlyList<string> LinkedVariables);

public sealed class TwinCatProjectProbe
{
    private const string TwinCatProjectTemplate = @"C:\TwinCAT\3.1\Components\Base\PrjTemplate\TwinCAT Project.tsproj";

    public TwinCatProjectProbeReport CreateProjectWithPlcTemplates(
        string repositoryRoot,
        string progId = "TcXaeShell.DTE.15.0",
        string? outputRoot = null)
    {
        var tempRoot = outputRoot ?? Path.Combine(Path.GetTempPath(), $"jointbench-tc-project-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var templates = TwinCatProjectTemplateSet.FromRepositoryRoot(repositoryRoot);
        object? dte = null;
        try
        {
            dte = ComAutomation.Create(progId);
            ComAutomation.TrySet(dte, "SuppressUI", true);
            ComAutomation.TrySet(dte, "UserControl", false);
            Thread.Sleep(TimeSpan.FromSeconds(5));

            var solution = ComAutomation.Get(dte, "Solution")
                ?? throw new InvalidOperationException("DTE Solution object is not available.");
            ComAutomation.Retry(() => ComAutomation.Invoke(solution, "Create", tempRoot, "JointBenchProjectProbe"));
            var project = ComAutomation.Retry(() => ComAutomation.Invoke(
                solution,
                "AddFromTemplate",
                TwinCatProjectTemplate,
                tempRoot,
                "JointBenchProjectProbe.tsproj"));
            Thread.Sleep(TimeSpan.FromSeconds(6));

            var sysManager = ComAutomation.Get(project, "Object")
                ?? throw new InvalidOperationException("TwinCAT project object is not available.");
            var plcNode = ComAutomation.Invoke(sysManager, "LookupTreeItem", "TIPC")
                ?? throw new InvalidOperationException("TwinCAT PLC tree item TIPC was not found.");
            ComAutomation.Invoke(plcNode, "CreateChild", "JointBenchPlc", 0, string.Empty, templates.PlcTemplateName);
            Thread.Sleep(TimeSpan.FromSeconds(4));

            var plcProject = ComAutomation.Invoke(sysManager, "LookupTreeItem", "TIPC^JointBenchPlc^JointBenchPlc Project")
                ?? throw new InvalidOperationException("JointBenchPlc project node was not found after creation.");
            var pousNode = ComAutomation.Invoke(sysManager, "LookupTreeItem", "TIPC^JointBenchPlc^JointBenchPlc Project^POUs")
                ?? throw new InvalidOperationException("JointBenchPlc POUs folder was not found after creation.");
            var dutsNode = ComAutomation.Invoke(sysManager, "LookupTreeItem", "TIPC^JointBenchPlc^JointBenchPlc Project^DUTs")
                ?? throw new InvalidOperationException("JointBenchPlc DUTs folder was not found after creation.");
            ComAutomation.Invoke(pousNode, "DeleteChild", "MAIN");
            foreach (var path in templates.PouTemplatePaths)
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException($"PLC template file not found: {path}", path);
                }

                var parent = IsDutTemplate(path) ? dutsNode : pousNode;
                ComAutomation.Invoke(parent, "CreateChild", null, 58, null, path);
            }

            var plcBuildSucceeded = BuildSolution(dte, sysManager, tempRoot);
            WriteTreeDiagnostics(sysManager, tempRoot);
            if (!plcBuildSucceeded)
            {
                throw new InvalidOperationException($"PLC build failed. See {Path.Combine(tempRoot, "build-errors.txt")}.");
            }

            var (linkPlan, linkedVariables) = ScanTi5AndLinkVariables(sysManager, tempRoot);

            return new TwinCatProjectProbeReport(
                true,
                string.Empty,
                tempRoot,
                "JointBenchProjectProbe",
                "JointBenchPlc",
                templates.PouTemplatePaths,
                plcBuildSucceeded,
                linkPlan,
                linkedVariables);
        }
        catch (Exception exc)
        {
            return new TwinCatProjectProbeReport(false, ExceptionChain(exc), tempRoot, "JointBenchProjectProbe", "JointBenchPlc", [], false, null, []);
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
                }

                try
                {
                    ComAutomation.Invoke(dte, "Quit");
                }
                catch
                {
                }

                try
                {
                    Marshal.FinalReleaseComObject(dte);
                }
                catch
                {
                }
            }
        }
    }

    private static bool BuildSolution(object dte, object sysManager, string tempRoot)
    {
        var solution = ComAutomation.Get(dte, "Solution")
            ?? throw new InvalidOperationException("DTE Solution object is not available.");
        var solutionBuild = ComAutomation.Get(solution, "SolutionBuild")
            ?? throw new InvalidOperationException("DTE SolutionBuild object is not available.");

        ComAutomation.Invoke(solutionBuild, "Build", true);
        ComAutomation.Invoke(dte, "ExecuteCommand", "Build.RebuildSolution");
        Thread.Sleep(TimeSpan.FromSeconds(8));
        var plcRoot = ComAutomation.Invoke(sysManager, "LookupTreeItem", "TIPC^JointBenchPlc")
            ?? throw new InvalidOperationException("JointBenchPlc root node was not found.");
        ComAutomation.Invoke(plcRoot, "GenerateBootProject", true);
        Thread.Sleep(TimeSpan.FromSeconds(2));
        File.WriteAllLines(Path.Combine(tempRoot, "build-errors.txt"), ReadErrorList(dte));
        var lastBuildInfo = ComAutomation.Get(solutionBuild, "LastBuildInfo");
        return Convert.ToInt32(lastBuildInfo) == 0 || File.Exists(Path.Combine(tempRoot, "JointBenchPlc", "JointBenchPlc.tmc"));
    }

    private static IReadOnlyList<string> ReadErrorList(object dte)
    {
        try
        {
            var toolWindows = ComAutomation.Get(dte, "ToolWindows");
            var errorList = ComAutomation.Get(toolWindows, "ErrorList");
            var errorItems = ComAutomation.Get(errorList, "ErrorItems");
            var count = Convert.ToInt32(ComAutomation.Get(errorItems, "Count"));
            var messages = new List<string>();
            for (var index = 1; index <= count; index++)
            {
                var item = ComAutomation.Invoke(errorItems, "Item", index);
                var level = Convert.ToString(ComAutomation.Get(item, "ErrorLevel")) ?? string.Empty;
                var description = Convert.ToString(ComAutomation.Get(item, "Description")) ?? string.Empty;
                var fileName = Convert.ToString(ComAutomation.Get(item, "FileName")) ?? string.Empty;
                var line = Convert.ToString(ComAutomation.Get(item, "Line")) ?? string.Empty;
                var column = Convert.ToString(ComAutomation.Get(item, "Column")) ?? string.Empty;
                messages.Add($"{level}: {description} ({fileName}:{line}:{column})");
            }

            return messages.Count > 0 ? messages : ["No Visual Studio error-list items were reported."];
        }
        catch (Exception exc)
        {
            return [$"Failed to read Visual Studio error list: {ExceptionChain(exc)}"];
        }
    }

    private static bool IsDutTemplate(string path) =>
        Path.GetFileName(path).Equals("JointBenchTypes.TcPOU", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".TcDUT", StringComparison.OrdinalIgnoreCase);

    private static void WriteTreeDiagnostics(object sysManager, string tempRoot)
    {
        WriteTreeDiagnostic(sysManager, tempRoot, "plc-tree.xml", "TIPC");
        WriteTreeDiagnostic(sysManager, tempRoot, "plc-project.xml", "TIPC^JointBenchPlc^JointBenchPlc Project");
        WriteTreeDiagnostic(sysManager, tempRoot, "plc-instance.xml", "TIPC^JointBenchPlc^JointBenchPlc Instance");
        WriteTreeDiagnostic(sysManager, tempRoot, "io-tree.xml", "TIID");
    }

    private static void WriteTreeDiagnostic(object sysManager, string tempRoot, string fileName, string treePath)
    {
        try
        {
            var item = ComAutomation.Invoke(sysManager, "LookupTreeItem", treePath);
            var xml = Convert.ToString(ComAutomation.Invoke(item, "ProduceXml", true)) ?? string.Empty;
            File.WriteAllText(Path.Combine(tempRoot, fileName), xml);
        }
        catch (Exception exc)
        {
            File.WriteAllText(Path.Combine(tempRoot, fileName), $"Failed to read {treePath}: {ExceptionChain(exc)}");
        }
    }

    private static (Ti5PdoLinkPlan LinkPlan, IReadOnlyList<string> LinkedVariables) ScanTi5AndLinkVariables(object sysManager, string tempRoot)
    {
        var ioDevices = ComAutomation.Invoke(sysManager, "LookupTreeItem", "TIID")
            ?? throw new InvalidOperationException("TwinCAT I/O Devices tree item TIID was not found.");
        var foundXml = Convert.ToString(ComAutomation.Invoke(ioDevices, "ProduceXml", false)) ?? string.Empty;
        File.WriteAllText(Path.Combine(tempRoot, "found-devices.xml"), foundXml);
        var foundDoc = XDocument.Parse(foundXml);
        var masterIndex = 0;
        foreach (var deviceNode in foundDoc.Descendants().Where(element => element.Name.LocalName == "Device"))
        {
            var itemSubType = IntValue(deviceNode, "ItemSubType");
            var itemSubTypeName = Text(deviceNode, "ItemSubTypeName");
            if (itemSubType != 111 && !itemSubTypeName.Contains("EtherCAT Master", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            masterIndex++;
            var addressInfo = deviceNode.Elements().FirstOrDefault(element => element.Name.LocalName == "AddressInfo")
                ?? throw new InvalidOperationException("Found EtherCAT master is missing AddressInfo.");
            var masterItem = ComAutomation.Invoke(ioDevices, "CreateChild", $"Device_{masterIndex}_EtherCAT", itemSubType, string.Empty, null)
                ?? throw new InvalidOperationException("Failed to create temporary EtherCAT master item.");
            ComAutomation.Invoke(masterItem, "ConsumeXml", $"<TreeItem><DeviceDef>{addressInfo}</DeviceDef></TreeItem>");
            ComAutomation.Invoke(masterItem, "ConsumeXml", "<TreeItem><DeviceDef><ScanBoxes>1</ScanBoxes></DeviceDef></TreeItem>");
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
                var box = new EtherCatBoxInfo(
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
                    childPath);
                if (!box.IsTi5)
                {
                    continue;
                }

                WriteBoxVariableDiagnostics(child, tempRoot, masterIndex, childIndex);
                var linkPlan = TwinCatPdoLinkPlanner.BuildTi5Plan(box);
                var linked = new List<string>();
                foreach (var link in linkPlan.Links)
                {
                    ComAutomation.Invoke(sysManager, "LinkVariables", link.PlcVariablePath, link.EtherCatVariablePath);
                    linked.Add($"{link.PlcVariablePath} <= {link.EtherCatVariablePath}");
                }

                return (linkPlan, linked);
            }
        }

        throw new InvalidOperationException("No Ti5 EtherCAT slave was found while building the TwinCAT project.");
    }

    private static void WriteBoxVariableDiagnostics(object boxItem, string tempRoot, int masterIndex, int childIndex)
    {
        var lines = new List<string>();
        for (var direction = 0; direction <= 1; direction++)
        {
            try
            {
                var count = Convert.ToInt32(ComAutomation.GetIndexed(boxItem, "VarCount", direction));
                lines.Add($"{(direction == 0 ? "Inputs" : "Outputs")}: {count}");
                for (var variableIndex = 1; variableIndex <= count; variableIndex++)
                {
                    var variable = ComAutomation.GetIndexed(boxItem, "Var", direction, variableIndex);
                    var name = Convert.ToString(ComAutomation.Get(variable, "Name")) ?? string.Empty;
                    var path = Convert.ToString(ComAutomation.Get(variable, "PathName")) ?? string.Empty;
                    lines.Add($"{direction}:{variableIndex}: {name} => {path}");
                }
            }
            catch (Exception exc)
            {
                lines.Add($"{direction}: {ExceptionChain(exc)}");
            }
        }

        File.WriteAllLines(Path.Combine(tempRoot, $"master-{masterIndex}-box-{childIndex}-vars.txt"), lines);
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
}
