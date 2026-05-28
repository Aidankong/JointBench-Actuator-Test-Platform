using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JointBench.TwinCat;
using Microsoft.Win32;

namespace JointBench.ProductionApp;

public partial class MainWindow : Window
{
    private readonly AutomationProbe automationProbe = new();
    private readonly EtherCatScanProbe etherCatScanProbe = new();
    private readonly AdsSymbolValidator adsSymbolValidator = new();
    private readonly TwinCatPreparationService preparationService = new();
    private readonly EsiAutoImportService esiAutoImportService = new();
    private readonly StationReadinessService stationReadinessService = new();

    private ProductionGateState productionGate = ProductionGateState.Locked;
    private string? lastReportDirectory;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        ApplyLanguage();
        WriteOutput("JointBench production shell ready.");
    }

    private ReportLanguage CurrentLanguage
    {
        get
        {
            var tag = (LanguageCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            return string.Equals(tag, "zh-CN", StringComparison.OrdinalIgnoreCase)
                ? ReportLanguage.SimplifiedChinese
                : ReportLanguage.English;
        }
    }

    private bool IsChinese => CurrentLanguage == ReportLanguage.SimplifiedChinese;

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RunStationReadinessAsync(autoStarted: true);
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        TitleText.Text = IsChinese ? "JointBench 产线测试" : "JointBench Production";
        SubtitleText.Text = IsChinese ? "Ti5 TwinCAT ADS 工位" : "Ti5 TwinCAT ADS station";
        WorkflowHeader.Text = IsChinese ? "流程" : "Workflow";
        Workflow1.Text = IsChinese ? "1  一键工位检查" : "1  Station check";
        Workflow2.Text = IsChinese ? "2  ESI 导入" : "2  ESI import";
        Workflow3.Text = IsChinese ? "3  TwinCAT 准备" : "3  Prepare TwinCAT";
        Workflow4.Text = IsChinese ? "4  Ti5 就绪" : "4  Ti5 ready";
        Workflow5.Text = IsChinese ? "5  ADS 符号" : "5  ADS symbols";
        Workflow6.Text = IsChinese ? "Enable only 使能不运动" : "Enable only";
        Workflow7.Text = IsChinese ? "1deg 阶跃" : "1deg step";
        Workflow8.Text = IsChinese ? "5deg 验收" : "5deg acceptance";
        ReadinessHeader.Text = IsChinese ? "工位就绪" : "Station Readiness";
        EsiHeader.Text = "ESI";
        TestHeader.Text = IsChinese ? "产线测试" : "Production Test";
        EventHeader.Text = IsChinese ? "事件输出" : "Event Output";
        RunPreflightButton.Content = IsChinese ? "一键工位检查" : "Check Station";
        AutomationSmokeButton.Content = IsChinese ? "自动化诊断" : "Automation Smoke";
        ScanSpikeButton.Content = IsChinese ? "工程扫描" : "Engineering Scan";
        ScanSpikeButton.ToolTip = IsChinese
            ? "仅用于工程诊断；产线启动门禁以一键工位检查为准。"
            : "Engineering diagnostic only; production readiness uses Check Station.";
        ImportEsiButton.Content = IsChinese ? "导入 ESI" : "Import ESI";
        ImportLastEsiButton.Content = IsChinese ? "导入上次 ESI" : "Import Last";
        PrepareTwinCatButton.Content = IsChinese ? "准备 TwinCAT" : "Prepare TwinCAT";
        ActivateTwinCatCheckBox.Content = IsChinese ? "激活配置" : "Activate";
        AmsLabel.Text = "AMS Net ID";
        PortLabel.Text = IsChinese ? "端口" : "Port";
        PrefixLabel.Text = IsChinese ? "符号前缀" : "Symbol Prefix";
        CheckAdsSymbolsButton.Content = IsChinese ? "检查符号" : "Check Symbols";
        StartTestButton.Content = IsChinese ? "开始测试" : "Start Test";
        OpenReportButton.Content = IsChinese ? "打开报告" : "Open Report";
        EStopCheckBox.Content = IsChinese ? "急停已确认" : "E-stop ready";
        FixtureCheckBox.Content = IsChinese ? "治具已确认" : "Fixture ready";
        PowerLimitCheckBox.Content = IsChinese ? "限流电源已确认" : "Current-limited power";
        LiveStatusText.Text = IsChinese ? "空闲" : "Idle";
        UpdateBadges();
    }

    private async void RunPreflightButton_Click(object sender, RoutedEventArgs e)
    {
        await RunStationReadinessAsync(autoStarted: false);
    }

    private async Task RunStationReadinessAsync(bool autoStarted)
    {
        var station = FullStationPath();
        SetBusy(true);
        ReadinessProgressBar.Visibility = Visibility.Visible;
        PreflightSummary.Text = autoStarted
            ? (IsChinese ? "软件启动，正在自动检查工位..." : "Application started; checking station...")
            : (IsChinese ? "正在执行一键工位检查..." : "Running station readiness check...");
        WriteOutput(autoStarted ? "Startup station readiness check started." : "Manual station readiness check started.");

        try
        {
            var report = await Task.Run(() => stationReadinessService.Check(station));
            ApplyReadinessReport(report);
            WriteReadinessReport(report);
        }
        catch (Exception exc)
        {
            productionGate = ProductionGateState.Locked;
            PreflightSummary.Text = IsChinese ? "工位检查失败" : "Station check failed";
            UpdateBadges();
            WriteOutput($"Station readiness error: {exc.Message}");
        }
        finally
        {
            ReadinessProgressBar.Visibility = Visibility.Collapsed;
            SetBusy(false);
        }
    }

    private void ApplyReadinessReport(StationReadinessReport report)
    {
        productionGate = ProductionGateState.FromReadiness(report);
        PreflightSummary.Text = report.Ready
            ? (IsChinese ? "工位就绪检查通过，仍需操作员确认安全项后才能运动。" : "Station checks passed; operator safety confirmation is still required before motion.")
            : (IsChinese ? "工位检查发现问题，请查看事件输出。" : "Station checks found issues; review event output.");

        if (report.EsiAutoImport?.InstallResult is { } installResult)
        {
            EsiSummary.Text = installResult.Summary.Label;
        }
        else if (report.EsiAutoImport is not null)
        {
            EsiSummary.Text = report.EsiAutoImport.Message;
        }

        UpdateBadges();
    }

    private void WriteReadinessReport(StationReadinessReport report)
    {
        WriteOutput($"Station readiness: {(report.Ready ? "OK" : "FAILED")}");
        foreach (var check in report.Checks)
        {
            WriteOutput($"[{check.Status}] {check.Name}: {check.Message}");
            if (!string.IsNullOrWhiteSpace(check.Detail))
            {
                WriteOutput($"    {check.Detail}");
            }
        }

        if (report.Preparation?.LinkPlan is not null)
        {
            WriteOutput($"Ti5: vendor 0x{report.Preparation.LinkPlan.VendorId:X8}, product 0x{report.Preparation.LinkPlan.ProductCode:X8}, revision 0x{report.Preparation.LinkPlan.RevisionNumber:X8}");
            foreach (var warning in report.Preparation.LinkPlan.Warnings)
            {
                WriteOutput($"Warning: {warning}");
            }
        }

        if (report.Preflight is not null && !report.Preflight.Ok)
        {
            foreach (var check in report.Preflight.Checks)
            {
                WriteOutput($"Preflight [{check.Status}] {check.Name}: {check.Message}");
            }
        }

        if (report.Preparation?.ScanReport is { } scanReport)
        {
            foreach (var box in scanReport.Boxes)
            {
                WriteOutput($"Scan box {box.MasterIndex}.{box.BoxIndex}: {box.Name}, vendor 0x{box.VendorId:X8}, product 0x{box.ProductCode:X8}, revision 0x{box.RevisionNo:X8}");
            }
        }

        if (report.AdsSymbols is { Ok: false } adsReport)
        {
            foreach (var symbol in adsReport.Symbols.Where(symbol => !symbol.Ok))
            {
                WriteOutput($"ADS [{symbol.ExpectedType}] {symbol.Name}: {symbol.Message}");
            }
        }
    }

    private void UpdateBadges()
    {
        EnvironmentBadge.Text = productionGate.EnvironmentOk
            ? (IsChinese ? "环境 OK" : "Environment OK")
            : (IsChinese ? "环境待检查" : "Environment");
        EnvironmentBadge.Foreground = Brush(productionGate.EnvironmentOk ? "#244C2A" : "#6A4A00");

        AdsBadge.Text = productionGate.AdsOk ? "ADS OK" : (IsChinese ? "ADS 待检查" : "ADS pending");
        AdsBadge.Foreground = Brush(productionGate.AdsOk ? "#244C2A" : "#6A4A00");

        MotionBadge.Text = productionGate.ReadyForMotion
            ? (IsChinese ? "就绪待安全确认" : "Ready gated")
            : (IsChinese ? "运动锁定" : "Motion locked");
        MotionBadge.Foreground = Brush(productionGate.ReadyForMotion ? "#244C2A" : "#682D2D");
    }

    private void ImportEsiButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = IsChinese ? "选择 EtherCAT ESI XML" : "Select EtherCAT ESI XML",
            Filter = "EtherCAT ESI XML (*.xml)|*.xml|All Files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var result = esiAutoImportService.ImportAndRemember(dialog.FileName);
            EsiSummary.Text = result.Summary.Label;
            WriteOutput("ESI installed and remembered.");
            WriteOutput(result.Summary.Label);
            WriteOutput($"Source: {result.SourcePath}");
            WriteOutput($"Target: {result.TargetPath}");
        }
        catch (Exception exc)
        {
            EsiSummary.Text = IsChinese ? "ESI 导入失败" : "ESI import failed";
            WriteOutput($"ESI error: {exc.Message}");
            MessageBox.Show(this, exc.Message, IsChinese ? "ESI 导入失败" : "ESI Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportLastEsiButton_Click(object sender, RoutedEventArgs e)
    {
        var report = esiAutoImportService.ImportLastUsed();
        EsiSummary.Text = report.InstallResult?.Summary.Label ?? report.Message;
        WriteOutput($"Last ESI import: {(report.Ok ? "OK" : "FAILED")}");
        WriteOutput(report.Message);
        if (report.InstallResult is not null)
        {
            WriteOutput($"Source: {report.InstallResult.SourcePath}");
            WriteOutput($"Target: {report.InstallResult.TargetPath}");
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

    private void ScanSpikeButton_Click(object sender, RoutedEventArgs e)
    {
        var report = etherCatScanProbe.Scan();
        productionGate = productionGate.WithEngineeringScan(report);
        UpdateBadges();

        WriteOutput($"Engineering EtherCAT scan: {(report.Ok ? "OK" : "FAILED")}");
        WriteOutput($"Ti5 found: {report.Ti5Found}");
        WriteOutput($"Temp root: {report.TempRoot}");
        if (!report.Ok)
        {
            WriteOutput(report.Error);
            WriteOutput("Engineering scan did not change production readiness. Use Check Station as the production gate.");
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

    private void CheckAdsSymbolsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(AdsPortBox.Text, out var port))
        {
            WriteOutput("ADS error: port must be an integer.");
            return;
        }

        var report = adsSymbolValidator.Check(new AdsConnectionOptions(AmsNetIdBox.Text.Trim(), port, SymbolPrefixBox.Text.Trim()));
        productionGate = productionGate.WithAdsSymbolCheck(report);
        UpdateBadges();

        WriteOutput($"ADS symbol check: {(report.Ok ? "OK" : "FAILED")}");
        WriteOutput($"Target: {report.AmsNetId}:{report.Port} {report.SymbolPrefix}");
        foreach (var symbol in report.Symbols)
        {
            WriteOutput($"[{(symbol.Ok ? "ok" : "error")}] {symbol.Name} ({symbol.ExpectedType}): {symbol.Message}");
        }
    }

    private async void PrepareTwinCatButton_Click(object sender, RoutedEventArgs e)
    {
        var activate = ActivateTwinCatCheckBox.IsChecked == true;
        if (activate)
        {
            var answer = MessageBox.Show(
                this,
                IsChinese ? "激活 TwinCAT 配置可能重启运行时。请确认只在工程模式下继续。" : "Activating TwinCAT may restart the runtime. Continue in engineering mode?",
                IsChinese ? "确认激活" : "Confirm Activation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        SetBusy(true);
        try
        {
            var station = FullStationPath();
            var report = await Task.Run(() => preparationService.Prepare(new TwinCatPreparationRequest(station, activate)));
            WriteOutput($"TwinCAT preparation: {(report.Ok ? "OK" : "FAILED")}");
            WriteOutput(report.Message);
            if (!activate)
            {
                WriteOutput("Dry-run only: ADS symbols will remain unavailable until Activate is checked and TwinCAT configuration is activated.");
            }
            else if (report.Activated)
            {
                WriteOutput("Activation requested TwinCAT restart. Wait for TwinCAT to return to Run, then run station readiness again.");
            }

            if (report.LinkPlan is not null)
            {
                foreach (var link in report.LinkPlan.Links)
                {
                    WriteOutput($"Link: {link.PlcVariablePath} <= {link.EtherCatVariablePath}");
                }
            }
        }
        catch (Exception exc)
        {
            WriteOutput($"TwinCAT preparation error: {exc.Message}");
            MessageBox.Show(this, exc.Message, IsChinese ? "准备 TwinCAT 失败" : "Prepare TwinCAT Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void StartTestButton_Click(object sender, RoutedEventArgs e)
    {
        if (!productionGate.ReadyForMotion)
        {
            MessageBox.Show(
                this,
                IsChinese ? "请先完成一键工位检查，并确认工位就绪和 ADS 符号全部通过。" : "Run Check Station first and make sure station readiness and ADS symbols pass before motion.",
                IsChinese ? "测试未就绪" : "Test Not Ready",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!SafetyConfirmed())
        {
            MessageBox.Show(
                this,
                IsChinese ? "请先确认急停、治具和限流电源。" : "Confirm E-stop, fixture, and current-limited power first.",
                IsChinese ? "运动被锁定" : "Motion Locked",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SetBusy(true);
        LiveStatusText.Text = IsChinese ? "测试中..." : "Testing...";
        MotionBadge.Text = IsChinese ? "运动中" : "Motion running";
        MotionBadge.Foreground = Brush("#6A4A00");
        try
        {
            var config = StationConfigLoader.Load(FullStationPath());
            using var client = new BeckhoffAdsSymbolClient();
            var runner = new ProductionTestSequenceRunner(
                new AdsMotionAdapter(client, config.Ads),
                new TestReportWriter());
            var result = await runner.RunAsync(
                new ProductionSequenceRequest(
                    Path.Combine(Environment.CurrentDirectory, "reports"),
                    CurrentLanguage,
                    config.Ads,
                    config.Safety,
                    config.Tests)
                {
                    Scaling = config.Scaling,
                },
                CancellationToken.None);
            lastReportDirectory = result.OutputDirectory;
            LiveStatusText.Text = $"{result.OverallResult}: {result.TestId}";
            MotionBadge.Text = result.OverallResult;
            MotionBadge.Foreground = Brush(result.OverallResult == "PASS" ? "#244C2A" : "#682D2D");
            WriteOutput($"Production sequence: {result.OverallResult}");
            WriteOutput($"Report: {result.OutputDirectory}");
            foreach (var stage in result.StageResults)
            {
                WriteOutput($"[{stage.Result}] {stage.StageName}: {string.Join("; ", stage.FailureReasons)}");
            }
        }
        catch (Exception exc)
        {
            LiveStatusText.Text = IsChinese ? "测试失败" : "Test failed";
            MotionBadge.Text = IsChinese ? "运动异常" : "Motion issue";
            MotionBadge.Foreground = Brush("#682D2D");
            WriteOutput($"Production test error: {exc.Message}");
            MessageBox.Show(this, exc.Message, IsChinese ? "测试失败" : "Test Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OpenReportButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = lastReportDirectory ?? Path.Combine(Environment.CurrentDirectory, "reports");
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
    }

    private bool SafetyConfirmed() =>
        EStopCheckBox.IsChecked == true &&
        FixtureCheckBox.IsChecked == true &&
        PowerLimitCheckBox.IsChecked == true;

    private string FullStationPath()
    {
        var text = StationDirBox.Text.Trim();
        return Path.IsPathRooted(text)
            ? text
            : Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, text));
    }

    private void SetBusy(bool busy)
    {
        RunPreflightButton.IsEnabled = !busy;
        ImportEsiButton.IsEnabled = !busy;
        ImportLastEsiButton.IsEnabled = !busy;
        PrepareTwinCatButton.IsEnabled = !busy;
        AutomationSmokeButton.IsEnabled = !busy;
        ScanSpikeButton.IsEnabled = !busy;
        CheckAdsSymbolsButton.IsEnabled = !busy;
        StartTestButton.IsEnabled = !busy;
    }

    private void WriteOutput(string line)
    {
        OutputBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        OutputBox.ScrollToEnd();
    }

    private static SolidColorBrush Brush(string color) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
}
