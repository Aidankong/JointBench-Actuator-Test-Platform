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
    private readonly HardStoneStateProbe hardStoneStateProbe = new();

    private ProductionGateState productionGate = ProductionGateState.Locked;
    private StationReadinessReport? lastReadinessReport;
    private string? lastReportDirectory;
    private CancellationTokenSource? productionTestCancellation;
    private bool testRunning;
    private bool uiBusy;

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
        SubtitleText.Text = IsChinese ? "Ti5 硬石 EtherCAT 主站工位" : "Ti5 HardStone EtherCAT master station";
        WorkflowHeader.Text = IsChinese ? "流程" : "Workflow";
        Workflow1.Text = IsChinese ? "1  一键工位检查" : "1  Station check";
        Workflow2.Text = IsChinese ? "2  主站固件" : "2  Master firmware";
        Workflow3.Text = IsChinese ? "3  SWD 通信" : "3  SWD link";
        Workflow4.Text = IsChinese ? "4  Ti5 EtherCAT OP" : "4  Ti5 EtherCAT OP";
        Workflow5.Text = IsChinese ? "5  控制链路" : "5  Control link";
        Workflow6.Text = IsChinese ? "Enable only 使能不运动" : "Enable only";
        Workflow7.Text = IsChinese ? "1deg 阶跃" : "1deg step";
        Workflow8.Text = IsChinese ? "低速两圈正反转" : "Low-speed two-turn";
        ReadinessHeader.Text = IsChinese ? "工位就绪" : "Station Readiness";
        EsiHeader.Text = IsChinese ? "工程工具" : "Engineering Tools";
        TestHeader.Text = IsChinese ? "产线测试" : "Production Test";
        EventHeader.Text = IsChinese ? "事件输出" : "Event Output";
        RunPreflightButton.Content = IsChinese ? "一键工位检查" : "Check Station";
        ReadHardStoneStateButton.Content = IsChinese ? "读取状态" : "Read State";
        AutomationSmokeButton.Content = IsChinese ? "自动化诊断" : "Automation Smoke";
        ScanSpikeButton.Content = IsChinese ? "工程扫描" : "Engineering Scan";
        ScanSpikeButton.ToolTip = IsChinese
            ? "仅用于工程诊断；产线启动门禁以一键工位检查为准。"
            : "Engineering diagnostic only; production readiness uses Check Station.";
        ImportEsiButton.Content = IsChinese ? "导入 ESI" : "Import ESI";
        ImportLastEsiButton.Content = IsChinese ? "导入上次 ESI" : "Import Last";
        PrepareTwinCatButton.Content = IsChinese ? "旧 TwinCAT 准备" : "Legacy TwinCAT";
        ActivateTwinCatCheckBox.Content = IsChinese ? "激活旧配置" : "Activate legacy";
        AmsLabel.Text = "AMS Net ID";
        VerifyOneDegButton.Content = IsChinese ? "1deg 验证" : "1deg Verify";
        PortLabel.Text = IsChinese ? "端口" : "Port";
        PrefixLabel.Text = IsChinese ? "符号前缀" : "Symbol Prefix";
        CheckAdsSymbolsButton.Content = IsChinese ? "检查符号" : "Check Symbols";
        StartTestButton.Content = IsChinese ? "开始测试" : "Start Test";
        StopTestButton.Content = IsChinese ? "停止测试" : "Stop Test";
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

    private async void ReadHardStoneStateButton_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        WriteOutput("HardStone state read started.");
        try
        {
            var config = StationConfigLoader.Load(FullStationPath());
            var snapshot = await Task.Run(() => hardStoneStateProbe.Read(config));
            WriteHardStoneState(snapshot);
            LiveStatusText.Text = snapshot.Ok
                ? (IsChinese ? "硬石状态 OK" : "HardStone state OK")
                : (IsChinese ? "硬石状态异常" : "HardStone state issue");
        }
        catch (Exception exc)
        {
            WriteOutput($"HardStone state read error: {exc.Message}");
            LiveStatusText.Text = IsChinese ? "读取状态失败" : "State read failed";
            MessageBox.Show(this, exc.Message, IsChinese ? "读取状态失败" : "Read State Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
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
            lastReadinessReport = report;
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

        AdsBadge.Text = productionGate.AdsOk ? (IsChinese ? "控制链路 OK" : "Control OK") : (IsChinese ? "控制链路待检查" : "Control pending");
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

    private async void VerifyOneDegButton_Click(object sender, RoutedEventArgs e)
    {
        await RunProductionTestAsync(ProductionRunProfile.OneDegreeVerification);
    }

    private async void StartTestButton_Click(object sender, RoutedEventArgs e)
    {
        await RunProductionTestAsync(ProductionRunProfile.FullAcceptance);
    }

    private void SafetyCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateMotionControls();
    }

    private async Task RunProductionTestAsync(ProductionRunProfile profile)
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

        productionTestCancellation = new CancellationTokenSource();
        testRunning = true;
        SetBusy(true);
        LiveStatusText.Text = IsChinese ? "测试中..." : "Testing...";
        MotionBadge.Text = IsChinese ? "运动中" : "Motion running";
        MotionBadge.Foreground = Brush("#6A4A00");
        try
        {
            var config = StationConfigLoader.Load(FullStationPath());
            var preRunState = string.Equals(config.Protocol, "hardstone_swd", StringComparison.OrdinalIgnoreCase)
                ? await Task.Run(() => hardStoneStateProbe.Read(config), productionTestCancellation.Token)
                : null;
            using var adapter = MotionAdapterFactory.Create(config);
            var runner = new ProductionTestSequenceRunner(
                adapter,
                new TestReportWriter(),
                line => Dispatcher.Invoke(() =>
                {
                    WriteOutput(line);
                    LiveStatusText.Text = line;
                }));
            var result = await runner.RunAsync(
                new ProductionSequenceRequest(
                    Path.Combine(Environment.CurrentDirectory, "reports"),
                    CurrentLanguage,
                    config.Ads,
                    config.Safety,
                    config.Tests)
                {
                    Scaling = config.Scaling,
                    Protocol = config.Protocol,
                    HardStone = config.HardStone,
                    PreRunChecks = lastReadinessReport?.Checks ?? [],
                    PreRunState = preRunState,
                }.WithProfile(profile),
                productionTestCancellation.Token);
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
            productionTestCancellation?.Dispose();
            productionTestCancellation = null;
            testRunning = false;
            SetBusy(false);
        }
    }

    private void StopTestButton_Click(object sender, RoutedEventArgs e)
    {
        if (productionTestCancellation is null || productionTestCancellation.IsCancellationRequested)
        {
            return;
        }

        StopTestButton.IsEnabled = false;
        LiveStatusText.Text = IsChinese ? "正在停止..." : "Stopping...";
        MotionBadge.Text = IsChinese ? "停止中" : "Stopping";
        MotionBadge.Foreground = Brush("#6A4A00");
        WriteOutput("Operator stop requested.");
        productionTestCancellation.Cancel();
    }

    private void WriteHardStoneState(HardStoneStateSnapshot snapshot)
    {
        WriteOutput($"HardStone state: {(snapshot.Ok ? "OK" : "FAILED")}");
        WriteOutput(snapshot.Message);
        WriteOutput($"Ti5: index={snapshot.Ti5SlaveIndex}, op={snapshot.EtherCatOperational}, vendor=0x{snapshot.VendorId:X8}, product=0x{snapshot.ProductCode:X8}, revision=0x{snapshot.RevisionNumber:X8}");
        WriteOutput($"Drive: statusword=0x{snapshot.Statusword:X4}, controlword=0x{snapshot.Controlword:X4}, enabled={snapshot.Enabled}, watchdog={snapshot.WatchdogOk}, error={snapshot.CommandError}");
        WriteOutput($"Mode: command={snapshot.ModeOfOperationCommand}, display={snapshot.ModeOfOperationDisplay}");
        WriteOutput($"Diagnosis: {CiA402StateDiagnosis.Describe(snapshot.Statusword, snapshot.Controlword, snapshot.CommandError, snapshot.Enabled, snapshot.ModeOfOperationCommand, snapshot.ModeOfOperationDisplay)}");
        WriteOutput($"Mailbox: command_code={snapshot.CommandCode}, command_sequence={snapshot.CommandSequence}, command_ack={snapshot.CommandAck}, heartbeat_sequence={snapshot.HeartbeatSequence}, heartbeat_ack={snapshot.HeartbeatAck}");
        WriteOutput($"Counts: zero={snapshot.ZeroPositionCounts}, actual={snapshot.ActualPositionCounts}, target={snapshot.TargetPositionCounts}, relative={snapshot.TargetRelativeCounts}");
        WriteOutput($"Position: actual={snapshot.ActualPositionDegrees:F6}deg, target={snapshot.TargetPositionDegrees:F6}deg, following_error={snapshot.FollowingErrorDegrees:F6}deg, velocity_counts={snapshot.ActualVelocityCounts}, torque={snapshot.TorqueActual}");
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
        uiBusy = busy;
        RunPreflightButton.IsEnabled = !busy;
        ReadHardStoneStateButton.IsEnabled = !busy;
        ImportEsiButton.IsEnabled = !busy;
        ImportLastEsiButton.IsEnabled = !busy;
        PrepareTwinCatButton.IsEnabled = !busy;
        AutomationSmokeButton.IsEnabled = !busy;
        ScanSpikeButton.IsEnabled = !busy;
        CheckAdsSymbolsButton.IsEnabled = !busy;
        UpdateMotionControls();
    }

    private void UpdateMotionControls()
    {
        var motionAllowed = !uiBusy && productionGate.ReadyForMotion && SafetyConfirmed();
        VerifyOneDegButton.IsEnabled = motionAllowed;
        StartTestButton.IsEnabled = motionAllowed;
        StopTestButton.IsEnabled = uiBusy && testRunning && productionTestCancellation is not null && !productionTestCancellation.IsCancellationRequested;
    }

    private void WriteOutput(string line)
    {
        OutputBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        OutputBox.ScrollToEnd();
    }

    private static SolidColorBrush Brush(string color) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
}
