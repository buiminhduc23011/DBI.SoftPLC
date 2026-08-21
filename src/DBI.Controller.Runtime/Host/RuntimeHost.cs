using DBI.Controller.Core.Interfaces;
using DBI.Controller.Core.Models;
using DBI.Controller.Protocol;
using DBI.Controller.Runtime.Drivers;
using DBI.Controller.Runtime.Engine;
using DBI.Controller.Runtime.Safety;

namespace DBI.Controller.Runtime.Host;

/// <summary>
/// Gom toàn bộ vòng đời Runtime vào một chỗ: nạp chương trình, cấu hình driver, chạy scan,
/// phục vụ lệnh từ Studio.
/// </summary>
/// <remarks>
/// Trước đây <c>Program.Main</c> dựng <see cref="ScanEngine"/> rồi <b>chờ Enter</b> — không bao giờ
/// gọi <c>SetProgram()</c> hay <c>Start()</c>, nên Runtime chưa từng chạy một chu kỳ scan nào (B-4).
/// </remarks>
public class RuntimeHost : IDisposable
{
    private readonly object _stateLock = new();

    private readonly MemorySnapshot _memory = new();
    private readonly SafetyCatchManager _safety = new();
    private readonly DriverManager _driverManager = new();
    private readonly ScanEngine _engine;
    private readonly IProgramSwapper _swapper;
    private readonly DeploymentStore _store;
    private readonly DriverFactory _driverFactory;

    private IControllerProgramContract? _program;
    private RuntimeState _state = RuntimeState.NoProgram;
    private bool _disposed;

    public RuntimeHost(
        DeploymentStore? store = null,
        IProgramSwapper? swapper = null,
        DriverFactory? driverFactory = null,
        int scanIntervalMs = 20)
    {
        _store = store ?? new DeploymentStore();
        _swapper = swapper ?? new ColdRestartSwapper();
        _driverFactory = driverFactory ?? new DriverFactory();

        _engine = new ScanEngine(_memory, _driverManager, _safety) { ScanIntervalMs = scanIntervalMs };
        _safety.FaultOccurred += OnFaultOccurred;
    }

    public RuntimeState State
    {
        get { lock (_stateLock) return _state; }
    }

    public DeploymentStore Store => _store;

    public SafetyCatchManager Safety => _safety;

    public DriverManager Drivers => _driverManager;

    /// <summary>Phát khi logic người dùng ném lỗi. <c>IpcServer</c> đẩy lên Studio qua kênh <c>fault</c>.</summary>
    public event EventHandler<FaultEvent>? FaultOccurred;

    // ── Deploy ───────────────────────────────────────────────────────────────────

    public async Task<DeployResponse> DeployAsync(DeployRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Dừng hẳn và xả output xuống THIẾT BỊ THẬT trước khi đụng vào chương trình.
        //    Bỏ bước này thì băng tải vẫn quay trong lúc nạp chương trình mới.
        await StopInternalAsync(ct).ConfigureAwait(false);
        _memory.ClearAllForces();

        // 2. Nạp assembly mới
        var swap = await _swapper.SwapAsync(request.AssemblyBytes, request.Mode, _memory, ct).ConfigureAwait(false);

        if (!swap.Ok || swap.Program is null)
            return FailDeploy(swap.Error, swap.ErrorCode);

        // 3. Dựng lại driver theo cấu hình thiết bị mới
        try
        {
            RebuildDrivers(request.Devices);
        }
        catch (UnknownDriverException ex)
        {
            return FailDeploy(ex.Message, ErrorCodes.UnknownDriver);
        }

        _program = swap.Program;
        _driverManager.RoutingTable.Load(request.TagRoutes);
        _engine.SetProgram(swap.Program);
        _engine.ResetMetrics();
        _safety.ResetFault();

        // 4. Lưu để tự dựng lại sau khi khởi động lại (ADR-005)
        bool autoStart = _store.Load()?.Manifest.AutoStart ?? true;
        _store.Save(request.AssemblyBytes, request.TagRoutes, request.Devices, autoStart);

        SetState(RuntimeState.Stopped);

        // 5. Kết nối driver rồi mới OnStart — OnStart của người dùng thường đọc đầu vào,
        //    chưa kết nối thì đầu vào toàn 0.
        await ConnectDriversAsync(ct).ConfigureAwait(false);

        Start();

        return new DeployResponse(true, null);
    }

    private DeployResponse FailDeploy(string? error, string? errorCode)
    {
        _program = null;
        _engine.SetProgram(null);
        SetState(RuntimeState.NoProgram);

        return new DeployResponse(false, error, errorCode);
    }

    private void RebuildDrivers(IEnumerable<DeviceSpec> devices)
    {
        // Dựng hết trước: loại driver lạ phải ném ra TRƯỚC khi ta gỡ bộ driver đang chạy được.
        var built = devices.Select(_driverFactory.Create).ToList();

        _driverManager.DisconnectAllAsync().GetAwaiter().GetResult();
        _driverManager.ClearDrivers();

        foreach (var driver in built)
            _driverManager.RegisterDriver(driver);
    }

    private async Task ConnectDriversAsync(CancellationToken ct)
    {
        foreach (var driver in _driverManager.Drivers)
        {
            try
            {
                await driver.ConnectAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Thiết bị chưa lên không được chặn deploy — trạng thái Faulted của driver hiện ở
                // Device view, và ScanEngine bỏ qua driver chưa kết nối.
                Console.Error.WriteLine($"[driver] '{driver.DriverId}' kết nối thất bại: {ex.Message}");
            }
        }
    }

    // ── Start / Stop / Reset ─────────────────────────────────────────────────────

    public void Start()
    {
        lock (_stateLock)
        {
            if (_state == RuntimeState.NoProgram || _program is null)
                throw new InvalidOperationException("Chưa có chương trình nào được nạp.");

            if (_state == RuntimeState.Faulted)
                throw new InvalidOperationException("Runtime đang ở trạng thái Fault. Gọi Reset trước khi chạy lại.");

            if (_state == RuntimeState.Running) return;
        }

        InvokeLifecycle(p => p.OnStart(), nameof(IControllerProgramContract.OnStart));
        _engine.Start();
        SetState(RuntimeState.Running);
    }

    public void Stop() => StopInternalAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    /// Dừng scan và đưa máy về trạng thái an toàn.
    /// </summary>
    /// <remarks>
    /// ⚠️ Thứ tự bắt buộc: dừng scan → <c>ClearAllOutputs</c> → <c>SwapOutputBuffers</c> →
    /// <c>WriteOutputsAsync</c>. Phải xả output xuống <b>thiết bị thật</b>, không chỉ trong bộ nhớ,
    /// nếu không băng tải vẫn quay sau lệnh Stop.
    /// </remarks>
    private async Task StopInternalAsync(CancellationToken ct)
    {
        bool wasRunning;
        lock (_stateLock) wasRunning = _state == RuntimeState.Running;

        if (wasRunning)
        {
            _engine.Stop();
            InvokeLifecycle(p => p.OnStop(), nameof(IControllerProgramContract.OnStop));
        }

        _memory.ClearAllOutputs();
        _memory.SwapOutputBuffers();

        try
        {
            await _driverManager.WriteOutputsAsync(_memory, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[stop] Xả output xuống thiết bị thất bại: {ex.Message}");
        }

        lock (_stateLock)
        {
            if (_state == RuntimeState.Running) SetStateUnsafe(RuntimeState.Stopped);
        }
    }

    /// <summary>
    /// Xoá fault của SafetyCatch. Máy về <c>Stopped</c> — người vận hành tự quyết định chạy lại,
    /// Reset không tự khởi động máy.
    /// </summary>
    public void Reset()
    {
        _safety.ResetFault();
        _memory.ClearAllOutputs();
        _memory.SwapOutputBuffers();

        SetState(_program is null ? RuntimeState.NoProgram : RuntimeState.Stopped);
    }

    // ── Truy vấn ─────────────────────────────────────────────────────────────────

    public StatusResponse GetStatus()
    {
        var metrics = _engine.Metrics;

        return new StatusResponse(
            State,
            metrics.CycleCount,
            metrics.LastScanMs,
            metrics.MaxScanMs,
            metrics.JitterMs,
            _safety.LastFault?.Message);
    }

    public ScanMetrics Metrics => _engine.Metrics;

    public DeviceStatesResponse GetDeviceStates() => new(
        _driverManager.GetDeviceStates()
            .Select(d => new DeviceStateInfo(d.DriverId, d.State.ToString(), d.LastError))
            .ToList());

    /// <summary>
    /// Thử kết nối thiết bị bằng driver tạm — không thay thế bộ driver đang chạy.
    /// </summary>
    /// <remarks>
    /// Lỗi kết nối KHÔNG ném ra ngoài: trả về trong <see cref="TestConnectionResponse.Error"/> để
    /// Studio hiển thị đúng thông điệp của driver thay vì lỗi IPC chung chung.
    /// </remarks>
    public async Task<TestConnectionResponse> TestDeviceConnectionAsync(TestConnectionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        IDriver driver;
        try
        {
            driver = _driverFactory.Create(request.Device);
        }
        catch (UnknownDriverException ex)
        {
            return new TestConnectionResponse(false, ex.Message);
        }

        try
        {
            await driver.ConnectAsync(ct).ConfigureAwait(false);
            return new TestConnectionResponse(true, null);
        }
        catch (OperationCanceledException)
        {
            return new TestConnectionResponse(false, "Hết thời gian chờ kết nối.");
        }
        catch (Exception ex)
        {
            return new TestConnectionResponse(false, ex.Message);
        }
        finally
        {
            try { await driver.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); }
            catch { /* dọn dẹp tốt nhất có thể — kết quả test đã quyết rồi */ }
        }
    }

    /// <summary>
    /// Đọc giá trị tag cho vòng push monitoring.
    /// </summary>
    /// <remarks>
    /// ⚠️ Gọi từ <b>task riêng</b>, tuyệt đối không phải scan thread. Đọc ảnh chụp đã chốt nên
    /// không cản trở chu kỳ 20ms — đây chính là lý do tồn tại của ADR-001.
    /// </remarks>
    public IReadOnlyDictionary<string, object> ReadTags() => _memory.SnapshotAll();

    public IReadOnlyCollection<string> KnownTagNames => _driverManager.RoutingTable.TagNames;
    public IReadOnlyDictionary<string, object> GetForces() => _memory.GetAllForces();
    public bool SetForce(string tagName, object value, bool enable)
    {
        if (enable) _memory.SetForce(tagName, value); else _memory.ClearForce(tagName);
        return true;
    }

    /// <summary>
    /// Ghi một lần vào tag từ Watch Table (phase-10 Task 10.4).
    /// </summary>
    /// <remarks>
    /// Ghi thẳng vào OutputState — cùng chỗ logic người dùng ghi, nên chu kỳ sau
    /// <see cref="MemorySnapshot.SwapOutputBuffers"/> tự công bố xuống driver. Tag Input bị chặn vì
    /// driver ghi đè mỗi chu kỳ — giá trị sẽ biến mất ngay và gây hiểu nhầm "ghi được mà không có tác dụng".
    /// </remarks>
    public WriteTagResponse WriteTag(WriteTagRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var route = _driverManager.RoutingTable.All.FirstOrDefault(
            r => r.TagName.Equals(request.TagName, StringComparison.OrdinalIgnoreCase));

        if (route is null)
            return new WriteTagResponse(false, $"Tag '{request.TagName}' chưa được khai báo trong Tag Table.");

        if (route.Direction == TagDirection.Input)
            return new WriteTagResponse(false,
                $"Tag '{request.TagName}' là Input — driver ghi đè mỗi chu kỳ nên không sửa được từ Studio. Dùng Force nếu cần ép giá trị.");

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(request.ValueJson);
            switch (route.DataType)
            {
                case TagDataType.Bool:
                    if (doc.RootElement.ValueKind is not (System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False))
                        return new WriteTagResponse(false, $"Tag '{route.TagName}' kiểu Bool cần giá trị true/false.");
                    _memory.SetBool(route.TagName, doc.RootElement.GetBoolean());
                    break;
                case TagDataType.Int:
                    _memory.SetInt(route.TagName, doc.RootElement.GetInt32());
                    break;
                case TagDataType.Real:
                    _memory.SetFloat(route.TagName, (float)doc.RootElement.GetDouble());
                    break;
            }
        }
        catch (FormatException)
        {
            return new WriteTagResponse(false, $"Giá trị '{request.ValueJson}' không hợp lệ cho tag '{route.TagName}' kiểu {route.DataType}.");
        }

        return new WriteTagResponse(true, null);
    }

    // ── Tự khởi động sau khi bật máy (ADR-005) ───────────────────────────────────

    /// <summary>
    /// Dựng lại lần deploy gần nhất và quyết định có tự chạy hay không.
    /// </summary>
    /// <returns><c>true</c> nếu máy tự chạy.</returns>
    public async Task<bool> TryRestoreLastDeploymentAsync(CancellationToken ct = default)
    {
        var deployment = _store.Load();
        var target = _store.DecideStartupState(deployment);

        if (deployment is null)
        {
            SetState(RuntimeState.NoProgram);
            return false;
        }

        var swap = await _swapper.SwapAsync(deployment.AssemblyBytes, SwapMode.ColdRestart, _memory, ct)
            .ConfigureAwait(false);

        if (!swap.Ok || swap.Program is null)
        {
            Console.Error.WriteLine($"[autostart] Nạp lại chương trình thất bại: {swap.Error}");
            SetState(RuntimeState.NoProgram);
            return false;
        }

        try
        {
            RebuildDrivers(deployment.Manifest.Devices);
        }
        catch (UnknownDriverException ex)
        {
            Console.Error.WriteLine($"[autostart] {ex.Message}");
            SetState(RuntimeState.NoProgram);
            return false;
        }

        _program = swap.Program;
        _driverManager.RoutingTable.Load(deployment.Manifest.TagRoutes);
        _engine.SetProgram(swap.Program);
        SetState(RuntimeState.Stopped);

        if (target != RuntimeState.Running)
        {
            Console.WriteLine(ExplainWhyNotAutoStarted(deployment));
            return false;
        }

        await ConnectDriversAsync(ct).ConfigureAwait(false);
        Start();

        return true;
    }

    private string ExplainWhyNotAutoStarted(StoredDeployment deployment)
    {
        if (_store.HasNoRunFile)
            return $"[autostart] Không tự chạy: có tệp phanh tay '{_store.NoRunFilePath}'.";

        if (deployment.Manifest.LastCleanState == RuntimeState.Faulted)
            return "[autostart] Không tự chạy: lần chạy trước kết thúc ở trạng thái Fault.";

        if (!deployment.Manifest.AutoStart)
            return "[autostart] Không tự chạy: project tắt AutoStart.";

        return "[autostart] Không tự chạy: lần trước máy đang dừng.";
    }

    // ── Nội bộ ───────────────────────────────────────────────────────────────────

    private void OnFaultOccurred(object? sender, FaultEvent fault)
    {
        _memory.ClearAllForces();
        SetState(RuntimeState.Faulted);
        FaultOccurred?.Invoke(this, fault);
    }

    private void SetState(RuntimeState state)
    {
        lock (_stateLock) SetStateUnsafe(state);
    }

    private void SetStateUnsafe(RuntimeState state)
    {
        _state = state;

        // Ghi MỖI LẦN đổi trạng thái, không phải lúc tắt — mất điện thì không có "lúc tắt" nào cả.
        try { _store.UpdateLastCleanState(state); }
        catch (IOException) { /* đĩa đầy hoặc bị khoá: không được làm sập Runtime */ }
    }

    private void InvokeLifecycle(Action<IControllerProgramContract> action, string name)
    {
        if (_program is null) return;

        try
        {
            action(_program);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[program] {name}() ném lỗi: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { StopInternalAsync(CancellationToken.None).GetAwaiter().GetResult(); }
        catch { /* đang tắt máy, không còn gì để cứu */ }

        _driverManager.DisconnectAllAsync().GetAwaiter().GetResult();
        _swapper.Unload();
        _safety.FaultOccurred -= OnFaultOccurred;

        GC.SuppressFinalize(this);
    }
}
