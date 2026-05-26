using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;

namespace JointBench.TwinCat;

public sealed class SystemProbe
{
    private const string TwinCatEsiDirectory = @"C:\TwinCAT\3.1\Config\Io\EtherCAT";
    private const string TwinCatAdsNet48Dll = @"C:\TwinCAT\AdsApi\.NET\v4.0.30319\TwinCAT.Ads.dll";
    private const string TwinCatXaeShell = @"C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\TcXaeShell.exe";
    private const string TwinCatXaeBase = @"C:\TwinCAT\3.1\Components\Base\TwinCAT XAE Base.dll";
    private const string TCatSysManagerTypeLib = @"C:\TwinCAT\3.1\Components\Base\TCatSysManager.tlb";

    public PreflightReport CheckPrerequisites()
    {
        var checks = new List<CheckItem>
        {
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Ok("windows", $"Windows {Environment.OSVersion.VersionString}")
                : Error("windows", "JointBench production helper requires Windows."),
            Ok("dotnet", $".NET runtime {Environment.Version}"),
            FileExists("twincat-esi-dir", TwinCatEsiDirectory, directory: true),
            FileExists("twincat-ads-net48", TwinCatAdsNet48Dll),
            FileExists("twincat-xae-shell", TwinCatXaeShell),
            FileExists("twincat-xae-base", TwinCatXaeBase),
            FileExists("twincat-automation-typelib", TCatSysManagerTypeLib),
            ComRegistered("TcXaeShell.DTE.15.0"),
            ComRegistered("VisualStudio.DTE.18.0"),
            VisualStudioCheck(),
            MSBuildCheck(),
            ServiceCheck("TcSysSrv", required: true),
            ServiceCheck("TcEventLogger", required: false),
            IsAdministrator()
                ? Ok("administrator", "Current process has administrator rights.")
                : Warning("administrator", "Current process is not elevated. ESI install or TwinCAT activation may require administrator rights."),
        };

        return new PreflightReport(DateTimeOffset.UtcNow, checks);
    }

    private static CheckItem VisualStudioCheck()
    {
        var installPath = VsWhere("-latest", "-products", "*", "-property", "installationPath");
        return string.IsNullOrWhiteSpace(installPath)
            ? Error("visual-studio", "Visual Studio installation was not found by vswhere.")
            : Ok("visual-studio", installPath);
    }

    private static CheckItem MSBuildCheck()
    {
        var msbuild = VsWhere("-latest", "-products", "*", "-find", @"MSBuild\Current\Bin\amd64\MSBuild.exe");
        return string.IsNullOrWhiteSpace(msbuild) || !File.Exists(msbuild)
            ? Error("msbuild", "Visual Studio MSBuild.exe was not found.")
            : Ok("msbuild", msbuild);
    }

    private static CheckItem FileExists(string name, string path, bool directory = false)
    {
        var exists = directory ? Directory.Exists(path) : File.Exists(path);
        return exists ? Ok(name, path) : Error(name, "Missing required path.", path);
    }

    private static CheckItem ComRegistered(string progId)
    {
        try
        {
            return Type.GetTypeFromProgID(progId) is null
                ? Error($"com-{progId}", "COM ProgID is not registered.")
                : Ok($"com-{progId}", "COM ProgID is registered.");
        }
        catch (Exception exc)
        {
            return Error($"com-{progId}", "COM registration check failed.", exc.Message);
        }
    }

    private static CheckItem ServiceCheck(string serviceName, bool required)
    {
        try
        {
            using var controller = new ServiceController(serviceName);
            var status = controller.Status;
            if (status == ServiceControllerStatus.Running)
            {
                return Ok($"service-{serviceName}", "Running");
            }

            return required
                ? Error($"service-{serviceName}", $"Service is {status}.")
                : Warning($"service-{serviceName}", $"Service is {status}.");
        }
        catch (Exception exc)
        {
            return required
                ? Error($"service-{serviceName}", "Required service was not found.", exc.Message)
                : Warning($"service-{serviceName}", "Optional service was not found.", exc.Message);
        }
    }

    private static string? VsWhere(params string[] args)
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var vswhere = Path.Combine(programFilesX86, "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswhere))
        {
            return null;
        }

        var startInfo = new ProcessStartInfo(vswhere)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(10_000);
        return output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static CheckItem Ok(string name, string message, string? detail = null) => new(name, "ok", message, detail);

    private static CheckItem Warning(string name, string message, string? detail = null) => new(name, "warning", message, detail);

    private static CheckItem Error(string name, string message, string? detail = null) => new(name, "error", message, detail);
}
