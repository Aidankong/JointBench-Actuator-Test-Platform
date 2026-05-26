using System.Windows;
using System.Windows.Media;
using JointBench.TwinCat;
using Microsoft.Win32;

namespace JointBench.ProductionApp;

public partial class MainWindow : Window
{
    private readonly SystemProbe systemProbe = new();
    private readonly EsiService esiService = new();
    private readonly AutomationProbe automationProbe = new();
    private readonly EtherCatScanProbe etherCatScanProbe = new();
    private readonly AdsSymbolValidator adsSymbolValidator = new();

    public MainWindow()
    {
        InitializeComponent();
        WriteOutput("JointBench production shell ready.");
    }

    private void RunPreflightButton_Click(object sender, RoutedEventArgs e)
    {
        var report = systemProbe.CheckPrerequisites();
        EnvironmentBadge.Text = report.Ok ? "Environment OK" : "Environment issue";
        EnvironmentBadge.Foreground = Brush(report.Ok ? "#244C2A" : "#682D2D");
        PreflightSummary.Text = report.Ok ? "All prerequisite checks passed" : "Review prerequisite output";

        WriteOutput($"Preflight: {(report.Ok ? "OK" : "FAILED")}");
        foreach (var check in report.Checks)
        {
            WriteOutput($"[{check.Status}] {check.Name}: {check.Message}");
            if (!string.IsNullOrWhiteSpace(check.Detail))
            {
                WriteOutput($"    {check.Detail}");
            }
        }
    }

    private void ImportEsiButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select EtherCAT ESI XML",
            Filter = "EtherCAT ESI XML (*.xml)|*.xml|All Files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var result = esiService.Install(dialog.FileName);
            EsiSummary.Text = result.Summary.Label;
            WriteOutput("ESI installed.");
            WriteOutput(result.Summary.Label);
            WriteOutput($"Source: {result.SourcePath}");
            WriteOutput($"Target: {result.TargetPath}");
        }
        catch (Exception exc)
        {
            EsiSummary.Text = "ESI import failed";
            WriteOutput($"ESI error: {exc.Message}");
            MessageBox.Show(this, exc.Message, "ESI Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AutomationSmokeButton_Click(object sender, RoutedEventArgs e)
    {
        var result = automationProbe.Smoke();
        WriteOutput($"Automation smoke: {(result.Ok ? "OK" : "FAILED")}");
        WriteOutput($"ProgID: {result.ProgId}");
        if (result.Ok)
        {
            WriteOutput($"Name: {result.Name}");
            WriteOutput($"Version: {result.Version}");
            return;
        }

        WriteOutput(result.Error);
    }

    private void CheckAdsSymbolsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(AdsPortBox.Text, out var port))
        {
            WriteOutput("ADS error: port must be an integer.");
            return;
        }

        var report = adsSymbolValidator.Check(new AdsConnectionOptions(AmsNetIdBox.Text.Trim(), port, SymbolPrefixBox.Text.Trim()));
        AdsBadge.Text = report.Ok ? "ADS symbols OK" : "ADS issue";
        AdsBadge.Foreground = Brush(report.Ok ? "#244C2A" : "#682D2D");

        WriteOutput($"ADS symbol check: {(report.Ok ? "OK" : "FAILED")}");
        WriteOutput($"Target: {report.AmsNetId}:{report.Port} {report.SymbolPrefix}");
        foreach (var symbol in report.Symbols)
        {
            WriteOutput($"[{(symbol.Ok ? "ok" : "error")}] {symbol.Name} ({symbol.ExpectedType}): {symbol.Message}");
        }
    }

    private void ScanSpikeButton_Click(object sender, RoutedEventArgs e)
    {
        var report = etherCatScanProbe.Scan();
        WriteOutput($"EtherCAT scan spike: {(report.Ok ? "OK" : "FAILED")}");
        WriteOutput($"Ti5 found: {report.Ti5Found}");
        WriteOutput($"Temp root: {report.TempRoot}");
        if (!report.Ok)
        {
            WriteOutput(report.Error);
            return;
        }

        foreach (var master in report.Masters)
        {
            WriteOutput($"Master {master.Index}: {master.ItemSubTypeName} {master.DeviceDescription} {master.DeviceData}");
        }

        foreach (var box in report.Boxes)
        {
            WriteOutput($"Box {box.MasterIndex}.{box.BoxIndex}: {box.Name}, vendor 0x{box.VendorId:X8}, product 0x{box.ProductCode:X8}, revision 0x{box.RevisionNo:X8}");
        }
    }

    private void WriteOutput(string line)
    {
        OutputBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        OutputBox.ScrollToEnd();
    }

    private static SolidColorBrush Brush(string color) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
}
