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
    IReadOnlyList<string> LinkedVariables,
    bool ActivationRequested = false,
    bool Activated = false);

public sealed record TwinCatProjectPreparationRequest(
    string RepositoryRoot,
    string ProgId = "TcXaeShell.DTE.15.0",
    string? OutputRoot = null,
    bool Activate = false);

public interface ITwinCatProjectPreparer
{
    TwinCatProjectProbeReport Prepare(TwinCatProjectPreparationRequest request);

    TwinCatProjectProbeReport RefreshLatest(TwinCatProjectPreparationRequest request);
}

public sealed class TwinCatProjectProbe : ITwinCatProjectPreparer
{
    private const string TwinCatProjectTemplate = @"C:\TwinCAT\3.1\Components\Base\PrjTemplate\TwinCAT Project.tsproj";

    public TwinCatProjectProbeReport Prepare(TwinCatProjectPreparationRequest request) =>
        CreateProjectWithPlcTemplates(request.RepositoryRoot, request.ProgId, request.OutputRoot, request.Activate);

    public TwinCatProjectProbeReport RefreshLatest(TwinCatProjectPreparationRequest request)
    {
        var latestRoot = FindLatestGeneratedProjectRoot();
        if (latestRoot is null)
        {
            return new TwinCatProjectProbeReport(
                false,
                "No previous JointBench TwinCAT project was found to refresh. Run Prepare TwinCAT when online scan is available.",
                request.OutputRoot ?? string.Empty,
                "JointBenchProjectProbe",
                "JointBenchPlc",
                [],
                false,
                null,
                [],
                request.Activate,
                false);
        }

        return RefreshExistingProjectWithPlcTemplates(latestRoot, request.RepositoryRoot, request.ProgId, request.Activate);
    }

    public TwinCatProjectProbeReport CreateProjectWithPlcTemplates(
        string repositoryRoot,
        string progId = "TcXaeShell.DTE.15.0",
        string? outputRoot = null,
        bool activate = false)
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

            ImportPlcTemplates(sysManager, templates);

            var plcBuildSucceeded = BuildSolution(dte, sysManager, tempRoot);
            WriteTreeDiagnostics(sysManager, tempRoot);
            if (!plcBuildSucceeded)
            {
                throw new InvalidOperationException($"PLC build failed. See {Path.Combine(tempRoot, "build-errors.txt")}.");
            }

            var (linkPlan, linkedVariables) = ScanTi5AndLinkVariables(sysManager, tempRoot);
            var activated = false;
            if (activate)
            {
                var activationStart = DateTimeOffset.UtcNow.AddSeconds(-2);
                ActivateConfiguration(dte, sysManager);
                var runtimeErrors = TwinCatRuntimeDiagnostics.ReadRecentStartupErrors(activationStart);
                if (runtimeErrors.Count > 0)
                {
                    throw new InvalidOperationException($"TwinCAT restart reported errors: {string.Join(" | ", runtimeErrors)}");
                }

                activated = true;
            }

            return new TwinCatProjectProbeReport(
                true,
                string.Empty,
                tempRoot,
                "JointBenchProjectProbe",
                "JointBenchPlc",
                templates.PouTemplatePaths,
                plcBuildSucceeded,
                linkPlan,
                linkedVariables,
                activate,
                activated);
        }
        catch (Exception exc)
        {
            return new TwinCatProjectProbeReport(false, ExceptionChain(exc), tempRoot, "JointBenchProjectProbe", "JointBenchPlc", [], false, null, [], activate, false);
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

    private TwinCatProjectProbeReport RefreshExistingProjectWithPlcTemplates(
        string projectRoot,
        string repositoryRoot,
        string progId,
        bool activate)
    {
        var templates = TwinCatProjectTemplateSet.FromRepositoryRoot(repositoryRoot);
        object? dte = null;
        var step = "copy-template-files";
        try
        {
            CopyTemplateFiles(projectRoot, templates);
            step = "create-dte";
            dte = ComAutomation.Create(progId);
            ComAutomation.TrySet(dte, "SuppressUI", true);
            ComAutomation.TrySet(dte, "UserControl", false);
            Thread.Sleep(TimeSpan.FromSeconds(5));

            step = "open-solution";
            var solutionPath = Path.Combine(projectRoot, "JointBenchProjectProbe.sln");
            if (!File.Exists(solutionPath))
            {
                throw new FileNotFoundException($"JointBench TwinCAT solution was not found: {solutionPath}", solutionPath);
            }

            var solution = ComAutomation.Get(dte, "Solution")
                ?? throw new InvalidOperationException("DTE Solution object is not available.");
            ComAutomation.Retry(() => ComAutomation.Invoke(solution, "Open", solutionPath));
            Thread.Sleep(TimeSpan.FromSeconds(6));
            step = "get-project";
            var project = OpenedSolutionProject(solution, Path.Combine(projectRoot, "JointBenchProjectProbe.tsproj"))
                ?? throw new InvalidOperationException("TwinCAT project was not found in the existing solution.");
            var sysManager = ComAutomation.Get(project, "Object")
                ?? throw new InvalidOperationException("TwinCAT project object is not available.");

            step = "import-plc-templates";
            ImportPlcTemplates(sysManager, templates);
            step = "build-solution";
            var plcBuildSucceeded = BuildSolution(dte, sysManager, projectRoot);
            step = "write-tree-diagnostics";
            WriteTreeDiagnostics(sysManager, projectRoot);
            if (!plcBuildSucceeded)
            {
                throw new InvalidOperationException($"PLC build failed. See {Path.Combine(projectRoot, "build-errors.txt")}.");
            }

            var activated = false;
            if (activate)
            {
                step = "activate-configuration";
                var activationStart = DateTimeOffset.UtcNow.AddSeconds(-2);
                ActivateConfiguration(dte, sysManager);
                var runtimeErrors = TwinCatRuntimeDiagnostics.ReadRecentStartupErrors(activationStart);
                if (runtimeErrors.Count > 0)
                {
                    throw new InvalidOperationException($"TwinCAT restart reported errors: {string.Join(" | ", runtimeErrors)}");
                }

                activated = true;
            }

            return new TwinCatProjectProbeReport(
                true,
                string.Empty,
                projectRoot,
                "JointBenchProjectProbe",
                "JointBenchPlc",
                templates.PouTemplatePaths,
                plcBuildSucceeded,
                null,
                ["Existing EtherCAT PDO links preserved."],
                activate,
                activated);
        }
        catch (Exception exc)
        {
            return new TwinCatProjectProbeReport(false, $"{step}: {ExceptionChain(exc)}", projectRoot, "JointBenchProjectProbe", "JointBenchPlc", [], false, null, [], activate, false);
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

    private static void ActivateConfiguration(object dte, object sysManager)
    {
        try
        {
            ComAutomation.Invoke(dte, "ExecuteCommand", "File.SaveAll");
        }
        catch
        {
            // Saving through DTE is best effort; ActivateConfiguration reports the authoritative result.
        }

        ComAutomation.Invoke(sysManager, "ActivateConfiguration");
        Thread.Sleep(TimeSpan.FromSeconds(2));
        ComAutomation.Invoke(sysManager, "StartRestartTwinCAT");
        Thread.Sleep(TimeSpan.FromSeconds(10));
        TwinCatPlcRuntimeController.EnsureLocalPortRun(851);
    }

    private static bool BuildSolution(object dte, object sysManager, string tempRoot)
    {
        var solution = ComAutomation.Get(dte, "Solution")
            ?? throw new InvalidOperationException("DTE Solution object is not available.");
        var solutionBuild = ComAutomation.Get(solution, "SolutionBuild")
            ?? throw new InvalidOperationException("DTE SolutionBuild object is not available.");

        try
        {
            ComAutomation.Invoke(solutionBuild, "Build", true);
        }
        catch
        {
        }

        try
        {
            ComAutomation.Invoke(dte, "ExecuteCommand", "Build.RebuildSolution");
        }
        catch
        {
        }
        Thread.Sleep(TimeSpan.FromSeconds(8));
        var plcRoot = ComAutomation.Invoke(sysManager, "LookupTreeItem", "TIPC^JointBenchPlc")
            ?? throw new InvalidOperationException("JointBenchPlc root node was not found.");
        ComAutomation.Invoke(plcRoot, "GenerateBootProject", true);
        Thread.Sleep(TimeSpan.FromSeconds(2));
        File.WriteAllLines(Path.Combine(tempRoot, "build-errors.txt"), ReadErrorList(dte));
        var lastBuildInfo = ComAutomation.Get(solutionBuild, "LastBuildInfo");
        return lastBuildInfo is null || Convert.ToInt32(lastBuildInfo) == 0 || File.Exists(Path.Combine(tempRoot, "JointBenchPlc", "JointBenchPlc.tmc"));
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

    private static void ImportPlcTemplates(object sysManager, TwinCatProjectTemplateSet templates)
    {
        _ = ComAutomation.Invoke(sysManager, "LookupTreeItem", "TIPC^JointBenchPlc^JointBenchPlc Project")
            ?? throw new InvalidOperationException("JointBenchPlc project node was not found.");
        var pousNode = ComAutomation.Invoke(sysManager, "LookupTreeItem", "TIPC^JointBenchPlc^JointBenchPlc Project^POUs")
            ?? throw new InvalidOperationException("JointBenchPlc POUs folder was not found.");
        var dutsNode = ComAutomation.Invoke(sysManager, "LookupTreeItem", "TIPC^JointBenchPlc^JointBenchPlc Project^DUTs")
            ?? throw new InvalidOperationException("JointBenchPlc DUTs folder was not found.");

        foreach (var childName in new[] { "MAIN", "FB_JointBenchAxis" })
        {
            TryDeleteChild(pousNode, childName);
        }

        foreach (var childName in new[] { "ST_JointBenchAds", "ST_Ti5CiA402PdoInput", "ST_Ti5CiA402PdoOutput", "JointBenchTypes" })
        {
            TryDeleteChild(dutsNode, childName);
        }

        foreach (var path in templates.PouTemplatePaths)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"PLC template file not found: {path}", path);
            }

            var parent = IsDutTemplate(path) ? dutsNode : pousNode;
            ComAutomation.Invoke(parent, "CreateChild", null, 58, null, path);
        }
    }

    private static void TryDeleteChild(object parent, string childName)
    {
        try
        {
            ComAutomation.Invoke(parent, "DeleteChild", childName);
        }
        catch
        {
        }
    }

    private static object? OpenedSolutionProject(object solution, string projectPath)
    {
        try
        {
            var projects = ComAutomation.Get(solution, "Projects");
            var project = ComAutomation.GetIndexed(projects, "Item", 1);
            if (project is not null)
            {
                return project;
            }
        }
        catch
        {
        }

        try
        {
            var activeProjects = ComAutomation.Get(solution, "ActiveSolutionProjects");
            if (activeProjects is Array { Length: > 0 } array)
            {
                return array.GetValue(0);
            }
        }
        catch
        {
        }

        try
        {
            return ComAutomation.GetIndexed(solution, "Item", 1);
        }
        catch
        {
        }

        try
        {
            return ComAutomation.Invoke(solution, "AddFromFile", projectPath);
        }
        catch
        {
            return null;
        }
    }

    private static void CopyTemplateFiles(string projectRoot, TwinCatProjectTemplateSet templates)
    {
        foreach (var sourcePath in templates.PouTemplatePaths)
        {
            var targetDirectory = IsDutTemplate(sourcePath)
                ? Path.Combine(projectRoot, "JointBenchPlc", "DUTs")
                : Path.Combine(projectRoot, "JointBenchPlc", "POUs");
            Directory.CreateDirectory(targetDirectory);
            File.Copy(sourcePath, Path.Combine(targetDirectory, Path.GetFileName(sourcePath)), overwrite: true);
        }
    }

    private static string? FindLatestGeneratedProjectRoot()
    {
        var temp = new DirectoryInfo(Path.GetTempPath());
        return temp.EnumerateDirectories("jointbench-tc-project-*")
            .Where(directory => File.Exists(Path.Combine(directory.FullName, "JointBenchProjectProbe.sln")) &&
                                Directory.Exists(Path.Combine(directory.FullName, "JointBenchPlc")))
            .OrderByDescending(directory => directory.LastWriteTimeUtc)
            .Select(directory => directory.FullName)
            .FirstOrDefault();
    }

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
