using System.Text.Json;

namespace JointBench.TwinCat;

public sealed record JointBenchAppState(string? LastEsiPath = null);

public sealed class JointBenchAppStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public JointBenchAppStateStore(string? filePath = null)
    {
        FilePath = filePath ?? DefaultFilePath();
    }

    public string FilePath { get; }

    public JointBenchAppState Load()
    {
        if (!File.Exists(FilePath))
        {
            return new JointBenchAppState();
        }

        try
        {
            using var stream = File.OpenRead(FilePath);
            return JsonSerializer.Deserialize<JointBenchAppState>(stream) ?? new JointBenchAppState();
        }
        catch (Exception exc) when (exc is JsonException or IOException or UnauthorizedAccessException)
        {
            return new JointBenchAppState();
        }
    }

    public void Save(JointBenchAppState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? ".");
        using var stream = File.Create(FilePath);
        JsonSerializer.Serialize(stream, state, JsonOptions);
    }

    private static string DefaultFilePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "JointBench", "ProductionApp", "state.json");
    }
}

public sealed record EsiAutoImportReport(
    bool Attempted,
    bool Ok,
    string Message,
    EsiInstallResult? InstallResult,
    string? SourcePath);

public sealed class EsiAutoImportService
{
    private readonly EsiService esiService;
    private readonly JointBenchAppStateStore stateStore;

    public EsiAutoImportService()
        : this(new EsiService(), new JointBenchAppStateStore())
    {
    }

    public EsiAutoImportService(EsiService esiService, JointBenchAppStateStore stateStore)
    {
        this.esiService = esiService;
        this.stateStore = stateStore;
    }

    public EsiInstallResult ImportAndRemember(string sourcePath, string? targetDirectory = null)
    {
        var result = esiService.Install(sourcePath, targetDirectory);
        stateStore.Save(stateStore.Load() with { LastEsiPath = result.SourcePath });
        return result;
    }

    public EsiAutoImportReport ImportLastUsed(string? targetDirectory = null)
    {
        var sourcePath = stateStore.Load().LastEsiPath;
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return new EsiAutoImportReport(false, true, "No last ESI file has been selected.", null, null);
        }

        if (!File.Exists(sourcePath))
        {
            return new EsiAutoImportReport(true, false, $"Last ESI file no longer exists: {sourcePath}", null, sourcePath);
        }

        try
        {
            var result = esiService.Install(sourcePath, targetDirectory);
            return new EsiAutoImportReport(true, true, "Last ESI file imported.", result, sourcePath);
        }
        catch (Exception exc)
        {
            return new EsiAutoImportReport(true, false, $"Last ESI import failed: {exc.Message}", null, sourcePath);
        }
    }
}

public sealed record StationReadinessReport(
    DateTimeOffset GeneratedAtUtc,
    bool Ready,
    string Summary,
    IReadOnlyList<CheckItem> Checks,
    PreflightReport? Preflight,
    EsiAutoImportReport? EsiAutoImport,
    TwinCatPreparationReport? Preparation,
    AdsSymbolCheckReport? AdsSymbols,
    AdsRuntimeStateReport? RuntimeState = null);

public sealed class StationReadinessService
{
    private readonly Func<PreflightReport> preflight;
    private readonly Func<EsiAutoImportReport> autoImportEsi;
    private readonly Func<TwinCatPreparationRequest, TwinCatPreparationReport> prepareTwinCat;
    private readonly Func<StationConfig, AdsRuntimeConfigurationReport> applyAdsRuntimeConfig;
    private readonly Func<AdsConnectionOptions, AdsSymbolCheckReport> checkAdsSymbols;
    private readonly Func<AdsConnectionOptions, AdsRuntimeStateReport> checkAdsRuntimeState;
    private readonly Func<ActiveTwinCatConfigReport> inspectActiveConfig;
    private readonly Func<int, IHardStoneDebugTransport> hardStoneTransportFactory;

    public StationReadinessService()
        : this(
            () => new SystemProbe().CheckPrerequisites(),
            () => new EsiAutoImportService().ImportLastUsed(),
            request => new TwinCatPreparationService().Prepare(request),
            options => new AdsSymbolValidator().Check(options),
            config => new AdsRuntimeConfigurator().ApplyAsync(config, CancellationToken.None).GetAwaiter().GetResult(),
            options => new AdsRuntimeStateProbe().Check(options),
            () => TwinCatActiveConfigProbe.Inspect(),
            speed => new HardStoneOpenOcdTransport(speed))
    {
    }

    public StationReadinessService(
        Func<PreflightReport> preflight,
        Func<EsiAutoImportReport> autoImportEsi,
        Func<TwinCatPreparationRequest, TwinCatPreparationReport> prepareTwinCat,
        Func<AdsConnectionOptions, AdsSymbolCheckReport> checkAdsSymbols,
        Func<StationConfig, AdsRuntimeConfigurationReport>? applyAdsRuntimeConfig = null,
        Func<AdsConnectionOptions, AdsRuntimeStateReport>? checkAdsRuntimeState = null,
        Func<ActiveTwinCatConfigReport>? inspectActiveConfig = null,
        Func<int, IHardStoneDebugTransport>? hardStoneTransportFactory = null)
    {
        this.preflight = preflight;
        this.autoImportEsi = autoImportEsi;
        this.prepareTwinCat = prepareTwinCat;
        this.applyAdsRuntimeConfig = applyAdsRuntimeConfig ?? (config => new AdsRuntimeConfigurator().ApplyAsync(config, CancellationToken.None).GetAwaiter().GetResult());
        this.checkAdsSymbols = checkAdsSymbols;
        this.checkAdsRuntimeState = checkAdsRuntimeState ?? (options => new AdsRuntimeStateProbe().Check(options));
        this.inspectActiveConfig = inspectActiveConfig ?? (() => TwinCatActiveConfigProbe.Inspect());
        this.hardStoneTransportFactory = hardStoneTransportFactory ?? (speed => new HardStoneOpenOcdTransport(speed));
    }

    public StationReadinessReport Check(string stationDirectory)
    {
        var checks = new List<CheckItem>();
        PreflightReport? preflightReport = null;
        EsiAutoImportReport? esiReport = null;
        TwinCatPreparationReport? preparationReport = null;
        AdsSymbolCheckReport? adsReport = null;
        AdsRuntimeConfigurationReport? runtimeConfigReport = null;
        AdsRuntimeStateReport? runtimeStateReport = null;
        ActiveTwinCatConfigReport? activeConfigReport = null;

        StationConfig config;
        try
        {
            config = StationConfigLoader.Load(stationDirectory);
            checks.Add(new CheckItem(
                "station-config",
                config.MotionAllowed ? "ok" : "error",
                config.MotionAllowed ? "Station config is motion-ready." : "Station config is missing required 1deg/two-turn ramp tests or safety limits."));
        }
        catch (Exception exc)
        {
            checks.Add(new CheckItem("station-config", "error", "Station config failed to load.", exc.Message));
            return Finish(checks, preflightReport, esiReport, preparationReport, adsReport);
        }

        if (string.Equals(config.Protocol, "hardstone_swd", StringComparison.OrdinalIgnoreCase))
        {
            return CheckHardStoneStation(config, checks, preflightReport, esiReport, preparationReport, adsReport);
        }

        try
        {
            preflightReport = preflight();
            checks.Add(new CheckItem("preflight", preflightReport.Ok ? "ok" : "error", preflightReport.Ok ? "Prerequisites passed." : "One or more prerequisite checks failed."));
        }
        catch (Exception exc)
        {
            checks.Add(new CheckItem("preflight", "error", "Prerequisite check failed.", exc.Message));
        }

        try
        {
            esiReport = autoImportEsi();
            checks.Add(new CheckItem("esi-auto-import", esiReport.Ok ? "ok" : "error", esiReport.Message, esiReport.SourcePath));
        }
        catch (Exception exc)
        {
            checks.Add(new CheckItem("esi-auto-import", "error", "ESI auto import failed.", exc.Message));
        }

        try
        {
            preparationReport = prepareTwinCat(new TwinCatPreparationRequest(stationDirectory, Activate: false));
            activeConfigReport = inspectActiveConfig();
            checks.Add(new CheckItem(
                "twincat-active-config",
                activeConfigReport.Ti5Found ? "ok" : "warning",
                activeConfigReport.Message,
                activeConfigReport.SourcePath));
            var scannedTi5 = preparationReport.ScanReport?.Ti5Found == true;
            var ti5Found = scannedTi5 || activeConfigReport.Ti5Found;
            var preparationOk = preparationReport.Ok || activeConfigReport.Ti5Found;
            checks.Add(new CheckItem(
                "twincat-prepare",
                preparationOk && ti5Found ? "ok" : "error",
                preparationReport.Ok ? preparationReport.Message : activeConfigReport.Ti5Found ? "Active TwinCAT configuration is already prepared with Ti5." : preparationReport.Message));
            checks.Add(new CheckItem(
                "ti5-scan",
                ti5Found ? "ok" : "error",
                scannedTi5 ? "Ti5 slave found." : activeConfigReport.Ti5Found ? "Ti5 found in active TwinCAT configuration." : "Ti5 slave was not found."));
        }
        catch (Exception exc)
        {
            checks.Add(new CheckItem("twincat-prepare", "error", "TwinCAT dry-run preparation failed.", exc.Message));
        }

        try
        {
            adsReport = checkAdsSymbols(config.Ads);
            var adsFailure = adsReport.Symbols.FirstOrDefault(symbol => !symbol.Ok);
            checks.Add(new CheckItem(
                "ads-symbols",
                adsReport.Ok ? "ok" : "error",
                adsReport.Ok ? "ADS symbols are available." : adsFailure?.Name == "<ads-state>" ? adsFailure.Message : "ADS symbol check failed.",
                $"{adsReport.AmsNetId}:{adsReport.Port} {adsReport.SymbolPrefix}"));
        }
        catch (Exception exc)
        {
            checks.Add(new CheckItem("ads-symbols", "error", "ADS symbol check failed.", exc.Message));
        }

        if (adsReport?.Ok == true)
        {
            try
            {
                runtimeConfigReport = applyAdsRuntimeConfig(config);
                checks.Add(new CheckItem(
                    "runtime-config",
                    runtimeConfigReport.Ok ? "ok" : "error",
                    runtimeConfigReport.Message,
                    runtimeConfigReport.Detail));
            }
            catch (Exception exc)
            {
                checks.Add(new CheckItem("runtime-config", "error", "PLC runtime configuration failed.", exc.Message));
            }
        }

        if (adsReport?.Ok == true && runtimeConfigReport?.Ok == true)
        {
            try
            {
                runtimeStateReport = checkAdsRuntimeState(config.Ads);
                checks.Add(new CheckItem(
                    "drive-state",
                    runtimeStateReport.Ok ? "ok" : "error",
                    runtimeStateReport.Message,
                    runtimeStateReport.Detail));
            }
            catch (Exception exc)
            {
                checks.Add(new CheckItem("drive-state", "error", "Ti5 runtime state check failed.", exc.Message));
            }
        }

        return Finish(checks, preflightReport, esiReport, preparationReport, adsReport, runtimeStateReport);
    }

    private StationReadinessReport CheckHardStoneStation(
        StationConfig config,
        List<CheckItem> checks,
        PreflightReport? preflightReport,
        EsiAutoImportReport? esiReport,
        TwinCatPreparationReport? preparationReport,
        AdsSymbolCheckReport? adsReport)
    {
        try
        {
            preflightReport = preflight();
            checks.Add(new CheckItem("preflight", preflightReport.Ok ? "ok" : "error", preflightReport.Ok ? "Prerequisites passed." : "One or more prerequisite checks failed."));
        }
        catch (Exception exc)
        {
            checks.Add(new CheckItem("preflight", "error", "Prerequisite check failed.", exc.Message));
        }

        var hardStone = config.HardStone;
        if (hardStone is null)
        {
            checks.Add(new CheckItem("hardstone-config", "error", "HardStone backend options are missing."));
            return Finish(checks, preflightReport, esiReport, preparationReport, adsReport);
        }

        checks.Add(new CheckItem(
            "hardstone-firmware",
            File.Exists(hardStone.FirmwareElfPath) ? "ok" : "error",
            File.Exists(hardStone.FirmwareElfPath) ? "HardStone firmware ELF is available." : "HardStone firmware ELF is missing.",
            hardStone.FirmwareElfPath));

        if (File.Exists(hardStone.FirmwareElfPath))
        {
            try
            {
                using var transport = hardStoneTransportFactory(hardStone.AdapterSpeedKHz);
                transport.ConnectAsync(
                    new HardStoneDebugOptions(
                        hardStone.FirmwareElfPath,
                        hardStone.CountsPerDegree,
                        Math.Max(Math.Abs(config.Safety.MinPositionDegrees), Math.Abs(config.Safety.MaxPositionDegrees))),
                    CancellationToken.None).GetAwaiter().GetResult();
                var ti5Index = transport.ReadInt32Async("g_ec_ti5_slave_index", CancellationToken.None).GetAwaiter().GetResult();
                var operational = transport.ReadInt32Async("g_ec_operational", CancellationToken.None).GetAwaiter().GetResult();
                var vendor = transport.ReadInt32Async("g_ec_last_vendor", CancellationToken.None).GetAwaiter().GetResult();
                var product = transport.ReadInt32Async("g_ec_last_product", CancellationToken.None).GetAwaiter().GetResult();
                var revision = transport.ReadInt32Async("g_ec_last_revision", CancellationToken.None).GetAwaiter().GetResult();
                var statusword = transport.ReadInt32Async("g_host_statusword", CancellationToken.None).GetAwaiter().GetResult();
                var controlword = transport.ReadInt32Async("g_host_controlword", CancellationToken.None).GetAwaiter().GetResult();
                var error = transport.ReadInt32Async("g_host_command_error", CancellationToken.None).GetAwaiter().GetResult();
                var enabled = transport.ReadInt32Async("g_host_enabled", CancellationToken.None).GetAwaiter().GetResult() != 0;
                var watchdog = transport.ReadInt32Async("g_host_watchdog_ok", CancellationToken.None).GetAwaiter().GetResult() != 0;
                var modeCommand = transport.ReadInt32Async("g_host_mode_of_operation", CancellationToken.None).GetAwaiter().GetResult();
                var modeDisplay = transport.ReadInt32Async("g_host_mode_display", CancellationToken.None).GetAwaiter().GetResult();

                var ok = ti5Index > 0 && operational > 0 && vendor == config.VendorId && product == config.ProductCode;
                checks.Add(new CheckItem(
                    "hardstone-ti5",
                    ok ? "ok" : "error",
                    ok ? "HardStone master reports Ti5 EtherCAT OP." : "HardStone master did not report Ti5 EtherCAT OP.",
                    $"index={ti5Index}, op={operational}, vendor=0x{vendor:X8}, product=0x{product:X8}, revision=0x{revision:X8}, statusword=0x{statusword:X4}, error={error}"));

                var diagnosis = CiA402StateDiagnosis.Describe(statusword, controlword, error, enabled, modeCommand, modeDisplay);
                var servoEnableGate = !enabled && (statusword & 0x006F) == 0x0023 && (controlword & 0x000F) == 0x000F;
                var driveStateOk = ok && watchdog && error == 0 && (statusword & 0x0008) == 0 && !servoEnableGate;
                checks.Add(new CheckItem(
                    "hardstone-drive-state",
                    driveStateOk ? "ok" : "error",
                    driveStateOk ? "HardStone Ti5 drive state is readable and not faulted." : diagnosis,
                    $"statusword=0x{statusword:X4}, controlword=0x{controlword:X4}, mode_command={modeCommand}, mode_display={modeDisplay}, enabled={enabled}, watchdog={watchdog}, error={error}; {diagnosis}"));
            }
            catch (Exception exc)
            {
                checks.Add(new CheckItem("hardstone-ti5", "error", "HardStone Ti5 check failed.", exc.Message));
            }
        }

        return Finish(checks, preflightReport, esiReport, preparationReport, adsReport);
    }

    private static StationReadinessReport Finish(
        IReadOnlyList<CheckItem> checks,
        PreflightReport? preflightReport,
        EsiAutoImportReport? esiReport,
        TwinCatPreparationReport? preparationReport,
        AdsSymbolCheckReport? adsReport,
        AdsRuntimeStateReport? runtimeStateReport = null)
    {
        var ready = checks.All(check => !check.IsError);
        var summary = ready ? "Station readiness checks passed." : "Station readiness checks found issues.";
        return new StationReadinessReport(DateTimeOffset.UtcNow, ready, summary, checks, preflightReport, esiReport, preparationReport, adsReport, runtimeStateReport);
    }
}
