using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;
using System.Security.Principal;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace RumiVtsController
{
    internal sealed class TrayAppContext : ApplicationContext, IRumiActionSink
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ToolStripMenuItem _statusItem;
        private readonly ToolStripMenuItem _connectItem;
        private readonly ToolStripMenuItem _hzItem;
        private readonly ToolStripMenuItem _calibrateItem;
        private readonly ToolStripMenuItem _deltaRadiusItem;
        private readonly ToolStripMenuItem _restartAdminItem;
        private readonly CancellationTokenSource _cts = new();
        private readonly VtsClient _vtsClient = new();
        private readonly ControllerLoop _controllerLoop;
        private readonly RawInputListener _rawInputListener;
        private readonly Control _uiInvoker;
        private Config _config;
        private Task? _expressionPipeTask;
        private bool _connectHotkeysTriggered;
        private readonly string _configPath;
        private readonly string _envPath;
        private bool _connecting;
        private int _windowWidth;
        private int _windowHeight;
        private bool _hasWindowSize;
        private Rectangle _windowClientBounds = Rectangle.Empty;
        private bool _hasWindowClientBounds;
        private FileSystemWatcher? _configWatcher;
        private System.Threading.Timer? _configReloadTimer;
        private System.Threading.Timer? _vtsWatchTimer;
        private IntPtr _foregroundHook;
        private WinEventDelegate? _foregroundHookCallback;
        private int _vtsWatchRunning;
        private bool _vtsProcessSeen;
        private readonly object _configReloadLock = new();
        private bool _configReloading;
        private System.Threading.Timer? _modelPollTimer;
        private int _modelPolling;
        private System.Threading.Timer? _calibrationNeutralTimer;
        private readonly System.Windows.Forms.Timer _calibrationTimer;
        private readonly ToolStripMenuItem _updateProfileItem;
        private bool _calibrationArmed;
        private bool _lastMouseDown;
        private int _calibrationRunning;
        private bool _calibrationOverrideApplied;
        private bool _calibrationPrevVirtualDesktop;
        private bool _calibrationPrevPrimaryMonitor;
        private bool _calibrationTrackingSuspended;
        private bool _hotkeyTrackingSuspended;
        private bool _hotkeyTrackingFrozen;
        private HotkeyWindow? _hotkeyWindow;
        private readonly SynchronizationContext? _uiContext;
        private readonly object _apiErrorLock = new();
        private DateTimeOffset _lastApiErrorAt = DateTimeOffset.MinValue;
        private static readonly int[] HzOptions = { 30, 60, 120 };
        private static readonly string[] VtsProcessNames = { "VTube Studio", "VTubeStudio" };
        private const int HotkeyToggleId = 1;
        private const int HotkeyDumpId = 2;
        private const int HotkeyCalibrateId = 3;
        private const int HotkeyFreezeId = 4;
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_NOREPEAT = 0x4000;
        private const uint VK_F1 = 0x70;
        private bool _hotkeyRegistered;
        private uint _hotkeyVk;
        private bool _hotkeyDumpRegistered;
        private uint _hotkeyDumpVk;
        private bool _hotkeyCalibrateRegistered;
        private uint _hotkeyCalibrateVk;
        private bool _hotkeyFreezeRegistered;
        private uint _hotkeyFreezeVk;
        private bool _debugHotkeysActive;
        private string? _activeProfileKey;
        private string? _activeModelId;
        private string? _activeModelName;
        private Dictionary<string, Config.HotkeyConfig> _activeHotkeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CancellationTokenSource> _hotkeyDurationTimers = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _hotkeyDurationLock = new();
        private readonly Dictionary<string, DateTimeOffset> _hotkeyLastTriggerAt = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _hotkeyCooldownLock = new();
        private readonly Dictionary<string, bool> _hotkeyToggleActive = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _hotkeyToggleLock = new();
        private bool _centerInside;
        private bool _modelInside;

        public TrayAppContext()
        {
            _configPath = ResolvePath("config.json");
            _envPath = ResolvePath(".env");
            EnvFile.Load(_envPath);
            _config = Config.Load(_configPath);
            _uiInvoker = new Control();
            _uiInvoker.CreateControl();
            _rawInputListener = new RawInputListener(_config.Model.DeltaMode.RawInput.Enabled);
            _controllerLoop = new ControllerLoop(_vtsClient, new MouseInput(), _rawInputListener, _config, _configPath);
            _controllerLoop.MonitorTransitioned += OnMonitorTransitioned;
            _controllerLoop.DeltaModeChanged += OnDeltaModeChanged;
            _controllerLoop.CenterHovered += OnCenterHovered;
            _controllerLoop.CenterExited += OnCenterExited;
            _controllerLoop.ModelHovered += OnModelHovered;
            _controllerLoop.ModelExited += OnModelExited;
            _controllerLoop.DizzyTriggered += OnDizzyTriggered;
            _controllerLoop.WakeJoltPanic += OnWakeJoltPanic;
            _controllerLoop.WakeJoltReturn += OnWakeJoltReturn;
            _controllerLoop.SmartIdleChanged += OnSmartIdleChanged;
            _controllerLoop.AfkChanged += OnAfkChanged;
            _uiContext = SynchronizationContext.Current;
            _vtsClient.ApiErrorReceived += OnApiErrorReceived;
            _vtsClient.ModelOutlineReceived += OnModelOutlineReceived;
            InitializeConfigWatcher();
            StartExpressionPipeServer();

            _connectItem = new ToolStripMenuItem("Reconnect");
            _connectItem.Click += async (_, __) => await ReconnectAsync();

            _hzItem = new ToolStripMenuItem(FormatHzLabel(_config.Vts.Inject.Hz));
            _hzItem.Click += (_, __) => CycleHz();

            _calibrateItem = new ToolStripMenuItem("Calibrate Head Offset (Next Click)");
            _calibrateItem.Click += async (_, __) => await ArmCalibrationAsync().ConfigureAwait(false);

            _deltaRadiusItem = new ToolStripMenuItem("Delta Radius Targets");
            _deltaRadiusItem.DropDownOpening += (_, __) => PopulateDeltaRadiusMenu();

            _restartAdminItem = new ToolStripMenuItem("Restart as Admin");
            _restartAdminItem.Click += (_, __) => RestartAsAdmin();

            _updateProfileItem = new ToolStripMenuItem("Update Model Profile");
            _updateProfileItem.Click += async (_, __) => await UpdateProfileFromVtsAsync();

            _statusItem = new ToolStripMenuItem("Status: Disconnected") { Enabled = false };

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (_, __) => Exit();

            var versionItem = new ToolStripMenuItem(AppVersion.Version) { Enabled = false };

            var menu = new ContextMenuStrip();
            menu.Items.AddRange(new ToolStripItem[]
            {
                versionItem,
                new ToolStripSeparator(),
                _connectItem,
                _hzItem,
                _calibrateItem,
                _deltaRadiusItem,
                _restartAdminItem,
                _updateProfileItem,
                new ToolStripSeparator(),
                _statusItem,
                new ToolStripSeparator(),
                exitItem
            });

            var iconPath = ResolvePath("icon.ico");
            var trayIcon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;

            _notifyIcon = new NotifyIcon
            {
                Text = "VTS Mouse",
                Icon = trayIcon,
                Visible = true,
                ContextMenuStrip = menu
            };

            _calibrationTimer = new System.Windows.Forms.Timer { Interval = 30 };
            _calibrationTimer.Tick += (_, __) => PollCalibration();

            _hotkeyWindow = new HotkeyWindow();
            _hotkeyWindow.HotkeyPressed += OnHotkeyPressed;
            _hotkeyWindow.CreateHandle(new CreateParams());
            UpdateHotkeyFocusState(IsVtsForeground());
            InitializeForegroundHook();
            UpdateAdminMenuState();

            _vtsWatchTimer = new System.Threading.Timer(_ => CheckVtsProcess(), null, 3000, 3000);
            _ = ConnectAsync();
        }

        private async Task ConnectAsync()
        {
            if (_connecting)
            {
                return;
            }

            _connecting = true;
            try
            {
                _config = Config.Load(_configPath);
                ApplyProfileForModel(_activeModelId, _activeModelName);
                _controllerLoop.UpdateConfig(_config);
                UpdateHotkeyFocusState(IsVtsForeground());
                Exception? lastError = null;
                var maxAttempts = Math.Max(1, _config.Vts.ConnectAttempts);
                var retrySeconds = Math.Max(0.0, _config.Vts.ConnectRetrySeconds);
                for (var attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        SetStatus($"Connecting... ({attempt}/{maxAttempts})");
                        await _vtsClient.ConnectAsync(_config.Vts, _envPath, _cts.Token).ConfigureAwait(true);
                        SetStatus("Connected");
                        _vtsProcessSeen = true;
                        await PostConnectAsync().ConfigureAwait(true);
                        return;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        SetStatus($"Connect attempt {attempt} failed: {ex.Message}");
                        await _vtsClient.DisconnectAsync().ConfigureAwait(true);
                        if (attempt < maxAttempts && retrySeconds > 0.0)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(retrySeconds), _cts.Token).ConfigureAwait(true);
                        }
                    }
                }

                SetStatus($"Error: {lastError?.Message ?? "Unable to connect to VTS."}");
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}");
            }
            finally
            {
                _connecting = false;
            }
        }

        private async Task PostConnectAsync()
        {
            await _vtsClient.SubscribeModelOutlineAsync(true, _cts.Token).ConfigureAwait(true);
            await _vtsClient.TryCreateCustomParametersAsync(
                new[]
                {
                    _config.Eye.ParamX,
                    _config.Eye.ParamY,
                    _config.Head.ParamX,
                    _config.Head.ParamY,
                    _config.Head.ParamZ,
                    _config.Body.ParamX,
                    _config.Body.ParamY,
                    _config.Body.ParamZ,
                    _config.Face.Blink.ParamLeft,
                    _config.Face.Blink.ParamRight,
                    _config.Face.Smile.ParamLeft,
                    _config.Face.Smile.ParamRight
                },
                _cts.Token).ConfigureAwait(true);

            UpdateModelPolling(_config);
            if (_config.Enabled)
            {
                _controllerLoop.Start();
            }

            var currentModel = await _vtsClient.TryGetCurrentModelAsync(_cts.Token).ConfigureAwait(true);
            if (currentModel.HasValue)
            {
                ApplyProfileForModel(currentModel.Value.ModelId, currentModel.Value.ModelName);
            }

            await TriggerHotkeysOnConnectAsync().ConfigureAwait(true);
        }

        private async Task TriggerHotkeysOnConnectAsync()
        {
            if (_connectHotkeysTriggered)
            {
                return;
            }

            _connectHotkeysTriggered = true;
            await TriggerHotkeysAsync("onConnect").ConfigureAwait(true);
        }

        private async Task TriggerHotkeysAsync(string trigger)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var entry in _activeHotkeys)
                {
                    var hotkey = entry.Value;
                    if (hotkey == null || string.IsNullOrWhiteSpace(hotkey.Id))
                    {
                        continue;
                    }

                    if (!TryGetHotkeyTriggerKind(hotkey, trigger, out var isResetTrigger))
                    {
                        continue;
                    }

                    var isToggle = IsToggleAction(hotkey.Action);
                    if (isResetTrigger)
                    {
                        if (isToggle && !IsHotkeyToggleActive(hotkey.Id))
                        {
                            CancelDurationTimer(hotkey.Id);
                            continue;
                        }
                        await _vtsClient.TriggerHotkeyAsync(hotkey.Id, _cts.Token).ConfigureAwait(true);
                        ClearHotkeyToggleActive(hotkey.Id);
                        CancelDurationTimer(hotkey.Id);
                        if (HotkeyHasTrigger(hotkey, "onDizzy"))
                        {
                            _controllerLoop.StopDizzyEffect();
                        }
                        continue;
                    }
                    if (isToggle && IsHotkeyToggleActive(hotkey.Id))
                    {
                        if (hotkey.DurationSeconds > 0 && hotkey.CooldownSeconds > 0)
                        {
                            if (!IsCooldownReady(hotkey.Id, hotkey.CooldownSeconds, now))
                            {
                                continue;
                            }

                            MarkHotkeyTriggered(hotkey.Id, now);
                            ScheduleDurationReset(hotkey);
                        }
                        continue;
                    }

                    if (!IsCooldownReady(hotkey.Id, hotkey.CooldownSeconds, now))
                    {
                        continue;
                    }

                    if (isToggle && hotkey.DurationSeconds > 0 && IsDurationActive(hotkey.Id))
                    {
                        MarkHotkeyTriggered(hotkey.Id, now);
                        ScheduleDurationReset(hotkey);
                        continue;
                    }

                    await _vtsClient.TriggerHotkeyAsync(hotkey.Id, _cts.Token).ConfigureAwait(true);
                    MarkHotkeyTriggered(hotkey.Id, now);
                    if (isToggle)
                    {
                        SetHotkeyToggleActive(hotkey.Id);
                    }

                    if (hotkey.DurationSeconds > 0 && isToggle)
                    {
                        ScheduleDurationReset(hotkey);
                    }
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Hotkey trigger failed: {ex.Message}");
            }
        }

        private async Task TriggerHotkeyByIdAsync(Config.HotkeyConfig hotkey)
        {
            await TriggerHotkeyByIdAsync(hotkey, null, null).ConfigureAwait(true);
        }

        private async Task TriggerHotkeyByIdAsync(Config.HotkeyConfig hotkey, double? durationOverride, double? cooldownOverride)
        {
            try
            {
                if (hotkey == null || string.IsNullOrWhiteSpace(hotkey.Id))
                {
                    return;
                }

                var now = DateTimeOffset.UtcNow;
                var isToggle = IsToggleAction(hotkey.Action);
                var durationSeconds = durationOverride ?? hotkey.DurationSeconds;
                var cooldownSeconds = cooldownOverride ?? hotkey.CooldownSeconds;

                if (isToggle && IsHotkeyToggleActive(hotkey.Id))
                {
                    if (durationSeconds > 0 && cooldownSeconds > 0)
                    {
                        if (!IsCooldownReady(hotkey.Id, cooldownSeconds, now))
                        {
                            return;
                        }

                        MarkHotkeyTriggered(hotkey.Id, now);
                        ScheduleDurationReset(hotkey, durationSeconds);
                    }
                    return;
                }

                if (!IsCooldownReady(hotkey.Id, cooldownSeconds, now))
                {
                    return;
                }

                if (isToggle && durationSeconds > 0 && IsDurationActive(hotkey.Id))
                {
                    MarkHotkeyTriggered(hotkey.Id, now);
                    ScheduleDurationReset(hotkey, durationSeconds);
                    return;
                }

                await _vtsClient.TriggerHotkeyAsync(hotkey.Id, _cts.Token).ConfigureAwait(true);
                MarkHotkeyTriggered(hotkey.Id, now);
                if (isToggle)
                {
                    SetHotkeyToggleActive(hotkey.Id);
                }

                if (durationSeconds > 0 && isToggle)
                {
                    ScheduleDurationReset(hotkey, durationSeconds);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Hotkey trigger failed: {ex.Message}");
            }
        }

        private async Task TriggerHotkeysAsync(string trigger, IReadOnlyCollection<Config.HotkeyConfig> allowed)
        {
            try
            {
                if (allowed == null || allowed.Count == 0)
                {
                    return;
                }

                var now = DateTimeOffset.UtcNow;
                foreach (var entry in allowed)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Id))
                    {
                        continue;
                    }

                    if (!TryGetHotkeyTriggerKind(entry, trigger, out var isResetTrigger))
                    {
                        continue;
                    }

                    var isToggle = IsToggleAction(entry.Action);
                    if (isResetTrigger)
                    {
                        if (isToggle && !IsHotkeyToggleActive(entry.Id))
                        {
                            CancelDurationTimer(entry.Id);
                            continue;
                        }
                        await _vtsClient.TriggerHotkeyAsync(entry.Id, _cts.Token).ConfigureAwait(true);
                        ClearHotkeyToggleActive(entry.Id);
                        CancelDurationTimer(entry.Id);
                        if (HotkeyHasTrigger(entry, "onDizzy"))
                        {
                            _controllerLoop.StopDizzyEffect();
                        }
                        continue;
                    }
                    if (isToggle && IsHotkeyToggleActive(entry.Id))
                    {
                        if (entry.DurationSeconds > 0)
                        {
                            MarkHotkeyTriggered(entry.Id, now);
                            ScheduleDurationReset(entry);
                        }
                        continue;
                    }

                    if (isToggle && entry.DurationSeconds > 0 && IsDurationActive(entry.Id))
                    {
                        MarkHotkeyTriggered(entry.Id, now);
                        ScheduleDurationReset(entry);
                        continue;
                    }

                    await _vtsClient.TriggerHotkeyAsync(entry.Id, _cts.Token).ConfigureAwait(true);
                    MarkHotkeyTriggered(entry.Id, now);
                    if (isToggle)
                    {
                        SetHotkeyToggleActive(entry.Id);
                    }

                    if (entry.DurationSeconds > 0 && isToggle)
                    {
                        ScheduleDurationReset(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Hotkey trigger failed: {ex.Message}");
            }
        }

        private static bool TryGetHotkeyTriggerKind(Config.HotkeyConfig hotkey, string trigger, out bool isResetTrigger)
        {
            isResetTrigger = false;
            if (hotkey.ResetTriggers != null)
            {
                foreach (var entry in hotkey.ResetTriggers)
                {
                    if (string.Equals(entry, trigger, StringComparison.OrdinalIgnoreCase))
                    {
                        isResetTrigger = true;
                        return true;
                    }
                }
            }

            if (hotkey.Triggers != null)
            {
                foreach (var entry in hotkey.Triggers)
                {
                    if (string.Equals(entry, trigger, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HotkeyHasTrigger(Config.HotkeyConfig hotkey, string trigger)
        {
            if (string.IsNullOrWhiteSpace(trigger))
            {
                return false;
            }

            if (hotkey.Triggers == null)
            {
                return false;
            }

            foreach (var entry in hotkey.Triggers)
            {
                if (string.Equals(entry, trigger, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private Config.HotkeyConfig? FindHotkeyById(string hotkeyId)
        {
            if (string.IsNullOrWhiteSpace(hotkeyId))
            {
                return null;
            }

            foreach (var entry in _activeHotkeys.Values)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Id))
                {
                    continue;
                }

                if (string.Equals(entry.Id, hotkeyId, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        private List<Config.HotkeyConfig> FindHotkeysByExpression(string expression)
        {
            var results = new List<Config.HotkeyConfig>();
            if (string.IsNullOrWhiteSpace(expression))
            {
                return results;
            }

            foreach (var entry in _activeHotkeys.Values)
            {
                if (entry == null || entry.Expressions == null || entry.Expressions.Count == 0)
                {
                    continue;
                }

                foreach (var action in entry.Expressions)
                {
                    if (string.IsNullOrWhiteSpace(action))
                    {
                        continue;
                    }

                    if (string.Equals(action, expression, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(entry);
                        break;
                    }
                }
            }

            return results;
        }

        private bool ApplyBuiltInAnimation(string action, IReadOnlyCollection<Config.HotkeyConfig> hotkeys, double? durationOverride)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return false;
            }

            var normalized = action.Trim().ToLowerInvariant();
            if (normalized == "dizzy")
            {
                if (hotkeys == null || hotkeys.Count == 0)
                {
                    return false;
                }

                var duration = durationOverride ?? 0.0;
                if (duration <= 0)
                {
                    foreach (var hotkey in hotkeys)
                    {
                        if (hotkey == null)
                        {
                            continue;
                        }

                        var hotkeyDuration = hotkey.Expression?.DurationSeconds ?? hotkey.DurationSeconds;
                        if (hotkeyDuration > duration)
                        {
                            duration = hotkeyDuration;
                        }
                    }
                }

                _controllerLoop.StartDizzyEffect(duration);
                return true;
            }

            if (normalized is "sleep" or "afk")
            {
                _controllerLoop.ForceAfk(true);
                return true;
            }

            if (normalized is "wake" or "wakeup" or "awake")
            {
                _controllerLoop.ForceAfk(false);
                return true;
            }

            return false;
        }

        private static bool IsToggleAction(string? action)
        {
            return !string.IsNullOrWhiteSpace(action)
                && action.StartsWith("Toggle", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsCooldownReady(string hotkeyId, double cooldownSeconds, DateTimeOffset now)
        {
            if (cooldownSeconds <= 0 || string.IsNullOrWhiteSpace(hotkeyId))
            {
                return true;
            }

            lock (_hotkeyCooldownLock)
            {
                if (!_hotkeyLastTriggerAt.TryGetValue(hotkeyId, out var last))
                {
                    return true;
                }

                return now - last >= TimeSpan.FromSeconds(cooldownSeconds);
            }
        }

        private void MarkHotkeyTriggered(string hotkeyId, DateTimeOffset now)
        {
            if (string.IsNullOrWhiteSpace(hotkeyId))
            {
                return;
            }

            lock (_hotkeyCooldownLock)
            {
                _hotkeyLastTriggerAt[hotkeyId] = now;
            }
        }

        private bool IsDurationActive(string hotkeyId)
        {
            if (string.IsNullOrWhiteSpace(hotkeyId))
            {
                return false;
            }

            lock (_hotkeyDurationLock)
            {
                return _hotkeyDurationTimers.ContainsKey(hotkeyId);
            }
        }

        private bool IsHotkeyToggleActive(string hotkeyId)
        {
            if (string.IsNullOrWhiteSpace(hotkeyId))
            {
                return false;
            }

            lock (_hotkeyToggleLock)
            {
                return _hotkeyToggleActive.TryGetValue(hotkeyId, out var active) && active;
            }
        }

        private void SetHotkeyToggleActive(string hotkeyId)
        {
            if (string.IsNullOrWhiteSpace(hotkeyId))
            {
                return;
            }

            lock (_hotkeyToggleLock)
            {
                _hotkeyToggleActive[hotkeyId] = true;
            }
        }

        private void ClearHotkeyToggleActive(string hotkeyId)
        {
            if (string.IsNullOrWhiteSpace(hotkeyId))
            {
                return;
            }

            lock (_hotkeyToggleLock)
            {
                if (_hotkeyToggleActive.ContainsKey(hotkeyId))
                {
                    _hotkeyToggleActive[hotkeyId] = false;
                }
            }
        }

        private void ScheduleDurationReset(Config.HotkeyConfig hotkey)
        {
            ScheduleDurationReset(hotkey, hotkey.DurationSeconds);
        }

        private void ScheduleDurationReset(Config.HotkeyConfig hotkey, double durationSeconds)
        {
            if (durationSeconds <= 0 || string.IsNullOrWhiteSpace(hotkey.Id))
            {
                return;
            }

            CancelDurationTimer(hotkey.Id);

            var duration = TimeSpan.FromSeconds(durationSeconds);
            var cts = new CancellationTokenSource();
            lock (_hotkeyDurationLock)
            {
                _hotkeyDurationTimers[hotkey.Id] = cts;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(duration, cts.Token).ConfigureAwait(true);
                    if (!cts.IsCancellationRequested)
                    {
                        await _vtsClient.TriggerHotkeyAsync(hotkey.Id, _cts.Token).ConfigureAwait(true);
                        ClearHotkeyToggleActive(hotkey.Id);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    SetStatus($"Hotkey duration reset failed: {ex.Message}");
                }
                finally
                {
                    lock (_hotkeyDurationLock)
                    {
                        if (_hotkeyDurationTimers.TryGetValue(hotkey.Id, out var current) && current == cts)
                        {
                            _hotkeyDurationTimers.Remove(hotkey.Id);
                        }
                    }

                    cts.Dispose();
                }
            });
        }

        private void CancelDurationTimer(string hotkeyId)
        {
            if (string.IsNullOrWhiteSpace(hotkeyId))
            {
                return;
            }

            CancellationTokenSource? existing = null;
            lock (_hotkeyDurationLock)
            {
                if (_hotkeyDurationTimers.TryGetValue(hotkeyId, out existing))
                {
                    _hotkeyDurationTimers.Remove(hotkeyId);
                }
            }

            if (existing != null)
            {
                existing.Cancel();
                existing.Dispose();
            }
        }

        private void CancelAllDurationTimers()
        {
            List<CancellationTokenSource> timers;
            lock (_hotkeyDurationLock)
            {
                timers = _hotkeyDurationTimers.Values.ToList();
                _hotkeyDurationTimers.Clear();
            }

            foreach (var timer in timers)
            {
                timer.Cancel();
                timer.Dispose();
            }
        }

        private void ClearHotkeyCooldowns()
        {
            lock (_hotkeyCooldownLock)
            {
                _hotkeyLastTriggerAt.Clear();
            }
        }

        private void ClearHotkeyToggleStates()
        {
            lock (_hotkeyToggleLock)
            {
                _hotkeyToggleActive.Clear();
            }
        }

        private async Task ReconnectAsync()
        {
            if (_connecting)
            {
                return;
            }

            _connecting = true;
            try
            {
                SetStatus("Reconnecting...");
                _controllerLoop.Stop();
                await _vtsClient.DisconnectAsync().ConfigureAwait(true);
                _connectHotkeysTriggered = false;
                _config = Config.Load(_configPath);
                _controllerLoop.UpdateConfig(_config);
                UpdateHotkeyFocusState(IsVtsForeground());
                await _vtsClient.ConnectAsync(_config.Vts, _envPath, _cts.Token).ConfigureAwait(true);
                SetStatus("Connected");
                await PostConnectAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}");
            }
            finally
            {
                _connecting = false;
            }
        }

        private void SetStatus(string status)
        {
            _statusItem.Text = $"Status: {status}";
        }

        private void OnApiErrorReceived(VtsClient.ApiErrorInfo errorInfo)
        {
            if (ShouldIgnoreApiError(errorInfo.ErrorId))
            {
                return;
            }

            var throttleSeconds = Math.Max(0.0, _config.ErrorHandling.ThrottleSeconds);
            if (throttleSeconds > 0.0)
            {
                lock (_apiErrorLock)
                {
                    var now = DateTimeOffset.UtcNow;
                    if ((now - _lastApiErrorAt).TotalSeconds < throttleSeconds)
                    {
                        return;
                    }

                    _lastApiErrorAt = now;
                }
            }

            if (_uiContext != null)
            {
                _uiContext.Post(_ => SetStatus(errorInfo.Message), null);
                return;
            }

            SetStatus(errorInfo.Message);
        }

        private void OnModelOutlineReceived(VtsClient.ModelOutlineInfo info)
        {
            _controllerLoop.UpdateOutlineInfo(info);
        }

        private void OnHotkeyPressed(int id)
        {
            if (!IsVtsForeground())
            {
                SetStatus("Hotkey ignored: VTS not focused.");
                return;
            }

            switch (id)
            {
                case HotkeyToggleId:
                    _hotkeyTrackingSuspended = !_hotkeyTrackingSuspended;
                    _controllerLoop.SetFaceFound(!_hotkeyTrackingSuspended);
                    var state = _hotkeyTrackingSuspended ? "Paused" : "Resumed";
                    var hotkeyLabel = string.IsNullOrWhiteSpace(_config.Vts.Debug.TrackingToggleHotkey)
                        ? string.Empty
                        : $" ({_config.Vts.Debug.TrackingToggleHotkey})";
                    SetStatus($"Tracking: {state}{hotkeyLabel}");
                    break;
                case HotkeyDumpId:
                    _ = DumpHotkeysToFileAsync();
                    break;
                case HotkeyCalibrateId:
                    _ = ArmCalibrationAsync();
                    break;
                case HotkeyFreezeId:
                    _hotkeyTrackingFrozen = !_hotkeyTrackingFrozen;
                    _controllerLoop.SetTrackingFrozen(_hotkeyTrackingFrozen);
                    var freezeState = _hotkeyTrackingFrozen ? "Frozen" : "Unfrozen";
                    var freezeLabel = string.IsNullOrWhiteSpace(_config.Vts.Debug.FreezeTrackingHotkey)
                        ? string.Empty
                        : $" ({_config.Vts.Debug.FreezeTrackingHotkey})";
                    SetStatus($"Tracking: {freezeState}{freezeLabel}");
                    break;
            }
        }

        private void UpdateHotkeyFocusState(bool isFocused)
        {
            if (_uiInvoker.InvokeRequired)
            {
                if (_uiInvoker.IsDisposed)
                {
                    return;
                }

                _uiInvoker.BeginInvoke((Action)(() => UpdateHotkeyFocusState(isFocused)));
                return;
            }

            if (_hotkeyWindow == null)
            {
                return;
            }

            if (isFocused)
            {
                if (_debugHotkeysActive)
                {
                    return;
                }

                RegisterTrackingHotkey(_config.Vts.Debug.TrackingToggleHotkey);
                RegisterHotkeyDump(_config.Vts.Debug.DumpHotkey);
                RegisterCalibrateHotkey(_config.Vts.Debug.CalibrateHotkey);
                RegisterFreezeHotkey(_config.Vts.Debug.FreezeTrackingHotkey);
                _debugHotkeysActive = true;
                return;
            }

            if (_debugHotkeysActive)
            {
                UnregisterDebugHotkeys();
                _debugHotkeysActive = false;
            }
        }

        private void UnregisterDebugHotkeys()
        {
            if (_hotkeyWindow == null)
            {
                return;
            }

            if (_hotkeyRegistered)
            {
                UnregisterHotKey(_hotkeyWindow.Handle, HotkeyToggleId);
                _hotkeyRegistered = false;
                _hotkeyVk = 0;
            }

            if (_hotkeyDumpRegistered)
            {
                UnregisterHotKey(_hotkeyWindow.Handle, HotkeyDumpId);
                _hotkeyDumpRegistered = false;
                _hotkeyDumpVk = 0;
            }

            if (_hotkeyCalibrateRegistered)
            {
                UnregisterHotKey(_hotkeyWindow.Handle, HotkeyCalibrateId);
                _hotkeyCalibrateRegistered = false;
                _hotkeyCalibrateVk = 0;
            }

            if (_hotkeyFreezeRegistered)
            {
                UnregisterHotKey(_hotkeyWindow.Handle, HotkeyFreezeId);
                _hotkeyFreezeRegistered = false;
                _hotkeyFreezeVk = 0;
            }
        }

        private void RegisterTrackingHotkey(string? hotkey) =>
            RegisterHotkey(hotkey, HotkeyToggleId, ref _hotkeyRegistered, ref _hotkeyVk, "tracking hotkey");

        private void RegisterHotkeyDump(string? hotkey) =>
            RegisterHotkey(hotkey, HotkeyDumpId, ref _hotkeyDumpRegistered, ref _hotkeyDumpVk, "hotkey dump");

        private void RegisterCalibrateHotkey(string? hotkey) =>
            RegisterHotkey(hotkey, HotkeyCalibrateId, ref _hotkeyCalibrateRegistered, ref _hotkeyCalibrateVk, "calibrate hotkey");

        private void RegisterFreezeHotkey(string? hotkey) =>
            RegisterHotkey(hotkey, HotkeyFreezeId, ref _hotkeyFreezeRegistered, ref _hotkeyFreezeVk, "freeze hotkey");

        private void RegisterHotkey(string? hotkey, int id, ref bool registered, ref uint vk, string failLabel)
        {
            if (_hotkeyWindow == null)
            {
                return;
            }

            if (registered)
            {
                try
                {
                    UnregisterHotKey(_hotkeyWindow.Handle, id);
                }
                catch
                {
                }

                registered = false;
                vk = 0;
            }

            if (!TryParseHotkey(hotkey, out var parsed))
            {
                return;
            }

            vk = parsed;
            var success = RegisterHotKey(_hotkeyWindow.Handle, id, MOD_NOREPEAT, parsed);
            if (!success)
            {
                SetStatus($"Warning: failed to register {failLabel}.");
                vk = 0;
                return;
            }

            registered = true;
        }

        private async Task DumpHotkeysToFileAsync()
        {
            try
            {
                if (!IsVtsForeground())
                {
                    SetStatus("Hotkey dump skipped: VTS is not focused.");
                    return;
                }

                await UpdateProfileFromVtsAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                SetStatus($"Hotkey dump failed: {ex.Message}");
            }
        }

        private async Task EnsureProfileForCurrentModelAsync(IReadOnlyList<Models.AvailableHotkey> hotkeys)
        {
            var modelInfo = await _vtsClient.TryGetCurrentModelAsync(_cts.Token).ConfigureAwait(true);
            if (!modelInfo.HasValue || string.IsNullOrWhiteSpace(modelInfo.Value.ModelId))
            {
                return;
            }

            var modelId = modelInfo.Value.ModelId!;
            var modelName = modelInfo.Value.ModelName ?? string.Empty;
            var key = $"modelId:{modelId}";
            if (_config.Profiles.ContainsKey(key))
            {
                return;
            }

            var profile = new Config.ProfileConfig
            {
                ModelName = modelName,
                Hotkeys = new Dictionary<string, Config.HotkeyConfig>(StringComparer.OrdinalIgnoreCase)
            };

            foreach (var entry in hotkeys)
            {
                if (string.IsNullOrWhiteSpace(entry.HotkeyId))
                {
                    continue;
                }

                var name = string.IsNullOrWhiteSpace(entry.Name) ? $"Hotkey {profile.Hotkeys.Count + 1}" : entry.Name;
                if (profile.Hotkeys.ContainsKey(name))
                {
                    name = $"{name} ({profile.Hotkeys.Count + 1})";
                }

                profile.Hotkeys[name] = new Config.HotkeyConfig
                {
                    Id = entry.HotkeyId,
                    Action = entry.Type ?? string.Empty,
                    Expressions = new List<string>(),
                    Expression = new Config.HotkeyExpressionTiming(),
                    Triggers = new List<string>(),
                    ResetTriggers = new List<string>(),
                    DurationSeconds = 0,
                    CooldownSeconds = 0
                };
            }

            if (profile.Hotkeys.Count == 0)
            {
                profile.Hotkeys["Template"] = new Config.HotkeyConfig
                {
                    Id = string.Empty,
                    Action = string.Empty,
                    Expressions = new List<string>(),
                    Expression = new Config.HotkeyExpressionTiming(),
                    Triggers = new List<string>(),
                    ResetTriggers = new List<string>(),
                    DurationSeconds = 0,
                    CooldownSeconds = 0
                };
            }

            _config.Profiles[key] = profile;
            _config.Save(_configPath);
            ApplyProfileForModel(modelId, modelName);
            SetStatus($"Profile created: {(!string.IsNullOrWhiteSpace(modelName) ? modelName : modelId)}");
        }

        private async Task UpdateProfileFromVtsAsync()
        {
            try
            {
                var hotkeys = await _vtsClient.TryGetHotkeysInCurrentModelAsync(_cts.Token).ConfigureAwait(true);
                if (hotkeys == null)
                {
                    SetStatus("Update profile failed: VTS does not support HotkeysInCurrentModelRequest.");
                    return;
                }

                var modelInfo = await _vtsClient.TryGetCurrentModelAsync(_cts.Token).ConfigureAwait(true);
                if (!modelInfo.HasValue || string.IsNullOrWhiteSpace(modelInfo.Value.ModelId))
                {
                    SetStatus("Update profile failed: no model ID.");
                    return;
                }

                var modelId = modelInfo.Value.ModelId!;
                var modelName = modelInfo.Value.ModelName ?? string.Empty;
                var key = $"modelId:{modelId}";
                if (!_config.Profiles.TryGetValue(key, out var profile))
                {
                    await EnsureProfileForCurrentModelAsync(hotkeys).ConfigureAwait(true);
                    return;
                }

                var byId = profile.Hotkeys
                    .Where(kv => !string.IsNullOrWhiteSpace(kv.Value.Id))
                    .ToDictionary(kv => kv.Value.Id, kv => new KeyValuePair<string, Config.HotkeyConfig>(kv.Key, kv.Value), StringComparer.OrdinalIgnoreCase);

                var updated = new Dictionary<string, Config.HotkeyConfig>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in hotkeys)
                {
                    if (string.IsNullOrWhiteSpace(entry.HotkeyId))
                    {
                        continue;
                    }

                    if (byId.TryGetValue(entry.HotkeyId, out var existing))
                    {
                        var name = string.IsNullOrWhiteSpace(entry.Name) ? existing.Key : entry.Name;
                        updated[name] = new Config.HotkeyConfig
                        {
                            Id = entry.HotkeyId,
                            Action = entry.Type ?? existing.Value.Action ?? string.Empty,
                            Expressions = existing.Value.Expressions != null ? new List<string>(existing.Value.Expressions) : new List<string>(),
                            Expression = existing.Value.Expression ?? new Config.HotkeyExpressionTiming(),
                            Triggers = existing.Value.Triggers != null ? new List<string>(existing.Value.Triggers) : new List<string>(),
                            ResetTriggers = existing.Value.ResetTriggers != null ? new List<string>(existing.Value.ResetTriggers) : new List<string>(),
                            DurationSeconds = existing.Value.DurationSeconds,
                            CooldownSeconds = existing.Value.CooldownSeconds
                        };
                    }
                    else
                    {
                        var name = string.IsNullOrWhiteSpace(entry.Name) ? $"Hotkey {updated.Count + 1}" : entry.Name;
                        updated[name] = new Config.HotkeyConfig
                        {
                            Id = entry.HotkeyId,
                            Action = entry.Type ?? string.Empty,
                            Expressions = new List<string>(),
                            Expression = new Config.HotkeyExpressionTiming(),
                            Triggers = new List<string>(),
                            ResetTriggers = new List<string>(),
                            DurationSeconds = 0,
                            CooldownSeconds = 0
                        };
                    }
                }

                profile.Hotkeys = updated;
                if (!string.IsNullOrWhiteSpace(modelName))
                {
                    profile.ModelName = modelName;
                }

                _config.Profiles[key] = profile;
                _config.Save(_configPath);
                ApplyProfileForModel(modelId, modelName);
                SetStatus($"Profile updated: {(!string.IsNullOrWhiteSpace(modelName) ? modelName : modelId)}");
            }
            catch (Exception ex)
            {
                SetStatus($"Update profile failed: {ex.Message}");
            }
        }

        private static bool TryParseHotkey(string? hotkey, out uint vk)
        {
            vk = 0;
            if (string.IsNullOrWhiteSpace(hotkey))
            {
                return false;
            }

            var trimmed = hotkey.Trim();
            if (trimmed.Length == 0 || trimmed.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (trimmed.StartsWith("F", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(trimmed[1..], out var fKey)
                && fKey is >= 1 and <= 12)
            {
                vk = VK_F1 + (uint)(fKey - 1);
                return true;
            }

            return false;
        }

        private void ApplyProfileForModel(string? modelId, string? modelName)
        {
            _activeModelId = modelId;
            _activeModelName = modelName;
            CancelAllDurationTimers();
            ClearHotkeyCooldowns();
            ClearHotkeyToggleStates();
            _centerInside = false;
            _modelInside = false;

            var profileKey = FindProfileKey(modelId, modelName);
            _activeProfileKey = profileKey;
            if (profileKey != null
                && _config.Profiles.TryGetValue(profileKey, out var profile)
                && profile.Hotkeys.Count > 0)
            {
                _activeHotkeys = CopyHotkeys(profile.Hotkeys);
                if (!string.IsNullOrWhiteSpace(profile.ModelName))
                {
                    SetStatus($"Profile: {profile.ModelName}");
                }
            }
            else
            {
                _activeHotkeys = new Dictionary<string, Config.HotkeyConfig>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private string? FindProfileKey(string? modelId, string? modelName)
        {
            if (_config.Profiles == null || _config.Profiles.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(modelId))
            {
                var idKey = $"modelId:{modelId}";
                if (_config.Profiles.ContainsKey(idKey))
                {
                    return idKey;
                }
            }

            if (!string.IsNullOrWhiteSpace(modelName))
            {
                var nameKey = $"modelName:{modelName}";
                if (_config.Profiles.ContainsKey(nameKey))
                {
                    return nameKey;
                }
            }

            return null;
        }

        private static Dictionary<string, Config.HotkeyConfig> CopyHotkeys(Dictionary<string, Config.HotkeyConfig> source)
        {
            var copy = new Dictionary<string, Config.HotkeyConfig>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in source)
            {
                if (entry.Value == null)
                {
                    continue;
                }

                copy[entry.Key] = new Config.HotkeyConfig
                {
                    Id = entry.Value.Id ?? string.Empty,
                    Action = entry.Value.Action ?? string.Empty,
                    Expressions = entry.Value.Expressions != null ? new List<string>(entry.Value.Expressions) : new List<string>(),
                    Expression = entry.Value.Expression ?? new Config.HotkeyExpressionTiming(),
                    Triggers = entry.Value.Triggers != null ? new List<string>(entry.Value.Triggers) : new List<string>(),
                    ResetTriggers = entry.Value.ResetTriggers != null ? new List<string>(entry.Value.ResetTriggers) : new List<string>(),
                    DurationSeconds = entry.Value.DurationSeconds,
                    CooldownSeconds = entry.Value.CooldownSeconds
                };
            }

            return copy;
        }

        private bool ShouldIgnoreApiError(int errorId)
        {
            var ignoreList = _config.ErrorHandling.IgnoreErrorIds;
            return ignoreList != null && ignoreList.Contains(errorId);
        }

        private void CycleHz()
        {
            var current = _config.Vts.Inject.Hz;
            var index = Array.IndexOf(HzOptions, current);
            if (index < 0)
            {
                index = 1;
            }

            var next = HzOptions[(index + 1) % HzOptions.Length];
            _config.Vts.Inject.Hz = next;
            _config.Save(_configPath);
            _controllerLoop.UpdateConfig(_config);
            _hzItem.Text = FormatHzLabel(next);
        }

        private static string FormatHzLabel(int hz)
        {
            return hz == 30 ? "Hz: 30 (Potato)" : $"Hz: {hz}";
        }

        private void Exit()
        {
            _cts.Cancel();
            try { _expressionPipeTask?.Wait(2000); } catch { }
            _controllerLoop.Dispose();
            _vtsClient.Dispose();
            CancelAllDurationTimers();
            ClearHotkeyCooldowns();
            ClearHotkeyToggleStates();
            _configWatcher?.Dispose();
            _configReloadTimer?.Dispose();
            _vtsWatchTimer?.Dispose();
            _modelPollTimer?.Dispose();
            if (_foregroundHook != IntPtr.Zero)
            {
                UnhookWinEvent(_foregroundHook);
                _foregroundHook = IntPtr.Zero;
            }
            _calibrationTimer.Stop();
            _calibrationTimer.Dispose();
            _rawInputListener.Dispose();
            _uiInvoker.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            if (_hotkeyWindow != null)
            {
                try
                {
                    UnregisterHotKey(_hotkeyWindow.Handle, HotkeyToggleId);
                }
                catch
                {
                }

                try
                {
                    UnregisterHotKey(_hotkeyWindow.Handle, HotkeyDumpId);
                }
                catch
                {
                }

                try
                {
                    UnregisterHotKey(_hotkeyWindow.Handle, HotkeyCalibrateId);
                }
                catch
                {
                }

                _hotkeyWindow.HotkeyPressed -= OnHotkeyPressed;
                _hotkeyWindow.DestroyHandle();
                _hotkeyWindow = null;
                _hotkeyRegistered = false;
                _hotkeyVk = 0;
                _hotkeyDumpRegistered = false;
                _hotkeyDumpVk = 0;
                _hotkeyCalibrateRegistered = false;
                _hotkeyCalibrateVk = 0;
            }
            ExitThread();
        }

        private void InitializeConfigWatcher()
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                return;
            }

            _configReloadTimer = new System.Threading.Timer(_ => ReloadConfigSafe(), null, Timeout.Infinite, Timeout.Infinite);
            _configWatcher = new FileSystemWatcher(dir, Path.GetFileName(_configPath))
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };

            _configWatcher.Changed += (_, __) => ScheduleConfigReload();
            _configWatcher.Created += (_, __) => ScheduleConfigReload();
            _configWatcher.Renamed += (_, __) => ScheduleConfigReload();
            _configWatcher.EnableRaisingEvents = true;
        }

        private void InitializeForegroundHook()
        {
            _foregroundHookCallback = ForegroundChanged;
            _foregroundHook = SetWinEventHook(
                EVENT_SYSTEM_FOREGROUND,
                EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _foregroundHookCallback,
                0,
                0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        }

        private void ForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
        {
            UpdateHotkeyFocusState(IsVtsForeground());
        }

        private void UpdateAdminMenuState()
        {
            if (_restartAdminItem == null)
            {
                return;
            }

            _restartAdminItem.Visible = !IsRunningElevated();
        }

        private static bool IsRunningElevated()
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (identity == null)
            {
                return false;
            }

            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private void RestartAsAdmin()
        {
            var exePath = Application.ExecutablePath;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo(exePath)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(startInfo);
                Exit();
            }
            catch
            {
                // Ignore if user cancels UAC.
            }
        }

        private void ScheduleConfigReload()
        {
            _configReloadTimer?.Change(200, Timeout.Infinite);
        }

        private void ReloadConfigSafe()
        {
            lock (_configReloadLock)
            {
                if (_configReloading)
                {
                    return;
                }

                _configReloading = true;
            }

            try
            {
                var updated = Config.Load(_configPath);
                _config = updated;
                ApplyProfileForModel(_activeModelId, _activeModelName);
                _controllerLoop.UpdateConfig(_config);
                UpdateHotkeyFocusState(IsVtsForeground());
                _rawInputListener.SetEnabled(_config.Model.DeltaMode.RawInput.Enabled);
                _uiContext?.Post(_ => _hzItem.Text = FormatHzLabel(_config.Vts.Inject.Hz), null);
                if (_config.Enabled && !_calibrationTrackingSuspended)
                {
                    _controllerLoop.Start();
                }
                else if (!_config.Enabled)
                {
                    _controllerLoop.Stop();
                }

                if (_vtsClient.IsAuthenticated)
                {
                    _ = _vtsClient.TryCreateCustomParametersAsync(
                        new[]
                        {
                            _config.Eye.ParamX,
                            _config.Eye.ParamY,
                            _config.Head.ParamX,
                            _config.Head.ParamY,
                            _config.Head.ParamZ,
                            _config.Body.ParamX,
                            _config.Body.ParamY,
                            _config.Body.ParamZ,
                            _config.Face.Blink.ParamLeft,
                            _config.Face.Blink.ParamRight,
                            _config.Face.Smile.ParamLeft,
                            _config.Face.Smile.ParamRight
                        },
                        _cts.Token);
                }

                UpdateModelPolling(_config);
            }
            catch (Exception ex)
            {
                SetStatus($"Config reload error: {ex.Message}");
            }
            finally
            {
                lock (_configReloadLock)
                {
                    _configReloading = false;
                }
            }
        }

        private void OnMonitorTransitioned(bool movedToSecondary)
        {
            var trigger = movedToSecondary
                ? "onMonitorTransitionSecondary"
                : "onMonitorTransitionPrimary";
            _ = TriggerHotkeysAsync(trigger);
        }

        private void OnDeltaModeChanged(bool isDeltaMode)
        {
            if (isDeltaMode)
            {
                _ = TriggerHotkeysAsync("onDeltaMode");
            }
            else
            {
                _ = TriggerHotkeysAsync("offDeltaMode");
            }
        }

        private void OnCenterHovered()
        {
            if (_controllerLoop.IsDizzyActive)
            {
                _centerInside = true;
                return;
            }

            if (!_centerInside)
            {
                _ = TriggerHotkeysAsync("onCenter");
            }

            _centerInside = true;
        }

        private void OnCenterExited()
        {
            _ = TriggerHotkeysAsync("offCenter");
            _centerInside = false;
        }

        private void OnModelHovered()
        {
            if (!_modelInside)
            {
                _ = TriggerHotkeysAsync("onModel");
            }

            _modelInside = true;
        }

        private void OnModelExited()
        {
            _ = TriggerHotkeysAsync("offModel");
            _modelInside = false;
        }

        private void OnDizzyTriggered()
        {
            var eligible = GetEligibleHotkeys("onDizzy");
            if (eligible.Count == 0)
            {
                return;
            }

            _controllerLoop.StartDizzyEffect(GetHotkeyDurationSeconds("onDizzy"));
            MarkTriggerCooldown(eligible);
            _ = TriggerHotkeysAsync("onDizzy", eligible);
        }

        private void OnWakeJoltPanic()
        {
            _ = TriggerHotkeysAsync("onWakeJolt");
        }

        private void OnWakeJoltReturn()
        {
            _ = TriggerHotkeysAsync("offWakeJolt");
        }

        private void OnSmartIdleChanged(bool isIdle)
        {
            if (_controllerLoop.IsAfkForced)
            {
                return;
            }

            if (isIdle)
            {
                CancelAllDurationTimers();
                _ = TriggerHotkeysAsync("onSmartMode");
            }
            else
            {
                _ = TriggerHotkeysAsync("offSmartMode");
            }
        }

        private void OnAfkChanged(bool isAfk)
        {
            if (isAfk)
            {
                CancelAllDurationTimers();
                _ = TriggerHotkeysAsync("onAFK");
            }
            else
            {
                _ = TriggerHotkeysAsync("offAFK");
            }
        }

        private double GetHotkeyDurationSeconds(string trigger)
        {
            if (string.IsNullOrWhiteSpace(trigger) || _activeHotkeys.Count == 0)
            {
                return 0.0;
            }

            double max = 0.0;
            foreach (var entry in _activeHotkeys.Values)
            {
                if (entry == null || entry.DurationSeconds <= 0.0)
                {
                    continue;
                }

                if (HotkeyHasTrigger(entry, trigger))
                {
                    max = Math.Max(max, entry.DurationSeconds);
                }
            }

            return max;
        }

        private List<Config.HotkeyConfig> GetEligibleHotkeys(string trigger)
        {
            var result = new List<Config.HotkeyConfig>();
            if (string.IsNullOrWhiteSpace(trigger))
            {
                return result;
            }

            var now = DateTimeOffset.UtcNow;
            foreach (var entry in _activeHotkeys.Values)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Id))
                {
                    continue;
                }

                if (!HotkeyHasTrigger(entry, trigger))
                {
                    continue;
                }

                if (entry.CooldownSeconds <= 0)
                {
                    result.Add(entry);
                    continue;
                }

                if (IsCooldownReady(entry.Id, entry.CooldownSeconds, now))
                {
                    result.Add(entry);
                }
            }

            return result;
        }

        private void MarkTriggerCooldown(IEnumerable<Config.HotkeyConfig> hotkeys)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var entry in hotkeys)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Id))
                {
                    continue;
                }

                if (entry.CooldownSeconds <= 0)
                {
                    continue;
                }

                MarkHotkeyTriggered(entry.Id, now);
            }
        }

        private void CheckVtsProcess()
        {
            if (Interlocked.Exchange(ref _vtsWatchRunning, 1) == 1)
            {
                return;
            }

            try
            {
                var running = IsVtsProcessRunning();
                if (running)
                {
                    _vtsProcessSeen = true;
                    UpdateHotkeyFocusState(IsVtsForeground());
                    return;
                }

                UpdateHotkeyFocusState(false);

                if (_vtsProcessSeen)
                {
                    if (_uiContext != null)
                    {
                        _uiContext.Post(_ => Exit(), null);
                    }
                    else
                    {
                        Exit();
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _vtsWatchRunning, 0);
            }
        }

        private static bool IsVtsProcessRunning()
        {
            foreach (var name in VtsProcessNames)
            {
                var procs = Process.GetProcessesByName(name);
                var found = procs.Length > 0;
                foreach (var p in procs) p.Dispose();
                if (found) return true;
            }

            return false;
        }


        private void UpdateModelPolling(Config config)
        {
            var enabled = config.Model.UseModelCenter;
            if (!enabled)
            {
                _modelPollTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }

            var intervalMs = Math.Max(250, (int)Math.Round(1000.0 / Math.Max(0.1f, config.Model.Hz)));
            if (_modelPollTimer == null)
            {
                _modelPollTimer = new System.Threading.Timer(_ => _ = PollModelAsync(), null, 0, intervalMs);
            }
            else
            {
                _modelPollTimer.Change(0, intervalMs);
            }
        }

        private async Task PollModelAsync()
        {
            if (!_vtsClient.IsAuthenticated)
            {
                return;
            }

            if (Interlocked.Exchange(ref _modelPolling, 1) == 1)
            {
                return;
            }

            try
            {
                var modelInfo = await _vtsClient.TryGetCurrentModelAsync(_cts.Token).ConfigureAwait(true);
                if (modelInfo.HasValue)
                {
                    _controllerLoop.UpdateModelTransform(modelInfo.Value.PositionX, modelInfo.Value.PositionY, modelInfo.Value.Size);
                    if (!string.Equals(modelInfo.Value.ModelId, _activeModelId, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(modelInfo.Value.ModelName, _activeModelName, StringComparison.OrdinalIgnoreCase))
                    {
                        ApplyProfileForModel(modelInfo.Value.ModelId, modelInfo.Value.ModelName);
                    }
                }

                var stats = await _vtsClient.TryGetStatisticsAsync(_cts.Token).ConfigureAwait(true);
                if (stats.HasValue
                    && stats.Value.WindowWidth.HasValue
                    && stats.Value.WindowHeight.HasValue
                    && stats.Value.WindowWidth.Value > 0
                    && stats.Value.WindowHeight.Value > 0)
                {
                    _windowWidth = stats.Value.WindowWidth.Value;
                    _windowHeight = stats.Value.WindowHeight.Value;
                    _hasWindowSize = true;
                    _controllerLoop.UpdateWindowSize(_windowWidth, _windowHeight);
                }

                if (TryGetVtsClientBounds(out var bounds))
                {
                    _windowClientBounds = bounds;
                    _hasWindowClientBounds = true;
                    _controllerLoop.UpdateWindowBounds(bounds);
                }

            }
            finally
            {
                Interlocked.Exchange(ref _modelPolling, 0);
            }
        }

        private async Task ArmCalibrationAsync()
        {
            if (!_vtsClient.IsAuthenticated)
            {
                SetStatus("Calibration failed: not connected.");
                return;
            }

            if (!_calibrationTrackingSuspended)
            {
                _calibrationTrackingSuspended = true;
                _controllerLoop.Stop();
                if (!_calibrationOverrideApplied)
                {
                    if (_config.Model.Mapping.UseVirtualDesktop || _config.Model.Mapping.UsePrimaryMonitor)
                    {
                        _calibrationPrevVirtualDesktop = _config.Model.Mapping.UseVirtualDesktop;
                        _calibrationPrevPrimaryMonitor = _config.Model.Mapping.UsePrimaryMonitor;
                        _config.Model.Mapping.UseVirtualDesktop = false;
                        _config.Model.Mapping.UsePrimaryMonitor = false;
                        _controllerLoop.UpdateConfig(_config);
                        _config.Save(_configPath);
                        _calibrationOverrideApplied = true;
                    }
                    else
                    {
                        _calibrationOverrideApplied = true;
                    }
                }

                await _vtsClient.SendNeutralParamsAsync(_config, true, _cts.Token).ConfigureAwait(true);
                _calibrationNeutralTimer = new System.Threading.Timer(
                    _ => _ = _vtsClient.SendNeutralParamsAsync(_config, true, _cts.Token),
                    null,
                    0,
                    1000 / 30);
            }

            _lastMouseDown = IsLeftMouseDown();
            _calibrationArmed = true;
            _calibrateItem.Text = "Calibrate Head Offset (Click...)";
            SetStatus("Calibration armed: click the model head.");
            _calibrationTimer.Start();
        }

        private void PollCalibration()
        {
            if (!_calibrationArmed)
            {
                _calibrationTimer.Stop();
                return;
            }

            var isDown = IsLeftMouseDown();
            if (isDown && !_lastMouseDown)
            {
                _calibrationArmed = false;
                _calibrationTimer.Stop();
                _calibrateItem.Text = "Calibrate Head Offset (Next Click)";
                _ = CalibrateAsync();
            }

            _lastMouseDown = isDown;
        }

        private async Task CalibrateAsync()
        {
            if (Interlocked.Exchange(ref _calibrationRunning, 1) == 1)
            {
                return;
            }

            try
            {
                if (!_vtsClient.IsAuthenticated)
                {
                    SetStatus("Calibration failed: not connected.");
                    return;
                }

                var cursor = Cursor.Position;
                var screen = Screen.FromPoint(cursor) ?? Screen.PrimaryScreen;
                if (screen == null)
                {
                    SetStatus("Calibration failed: no screen.");
                    return;
                }
                var trackingBounds = screen.Bounds;
                var clientBounds = _hasWindowClientBounds ? _windowClientBounds : trackingBounds;

                var modelInfo = await _vtsClient.TryGetCurrentModelAsync(_cts.Token).ConfigureAwait(true);
                if (!modelInfo.HasValue || !modelInfo.Value.PositionX.HasValue || !modelInfo.Value.PositionY.HasValue)
                {
                    SetStatus("Calibration failed: model position unavailable.");
                    return;
                }

                // Use Win32 client bounds for center so it's in the same pixel space as the cursor.
                var windowWidth = clientBounds.Width;
                var windowHeight = clientBounds.Height;
                if (windowWidth <= 0 || windowHeight <= 0)
                {
                    windowWidth = _hasWindowSize ? _windowWidth : 0;
                    windowHeight = _hasWindowSize ? _windowHeight : 0;
                }

                // vtsHeight used for model position Y scale (VTS coordinate system)
                var vtsHeight = _hasWindowSize ? _windowHeight : windowHeight;
                if (vtsHeight <= 0) vtsHeight = windowHeight;

                var screenCenterX = clientBounds.Left + windowWidth / 2.0f;
                var screenCenterY = clientBounds.Top + windowHeight / 2.0f;

                var targetOffsetY = cursor.Y - screenCenterY;

                var modelOffsetY = ModelMath.PositionToPixelsY(
                    -modelInfo.Value.PositionY.Value,
                    vtsHeight);
                var offsetY = targetOffsetY - modelOffsetY;

                _config.Model.OffsetY = offsetY;
                if (_controllerLoop.TryGetOutlineHeightNormalized(out var outlineHeight))
                {
                    _config.Model.OutlineRefHeight = outlineHeight;
                }
                _config.Save(_configPath);
                _controllerLoop.UpdateConfig(_config);
                SetStatus($"Calibrated: offsetY={offsetY:F1}");
            }
            catch (Exception ex)
            {
                SetStatus($"Calibration failed: {ex.Message}");
            }
            finally
            {
                if (_calibrationOverrideApplied)
                {
                    var changed = false;
                    if (_config.Model.Mapping.UseVirtualDesktop != _calibrationPrevVirtualDesktop)
                    {
                        _config.Model.Mapping.UseVirtualDesktop = _calibrationPrevVirtualDesktop;
                        changed = true;
                    }
                    if (_config.Model.Mapping.UsePrimaryMonitor != _calibrationPrevPrimaryMonitor)
                    {
                        _config.Model.Mapping.UsePrimaryMonitor = _calibrationPrevPrimaryMonitor;
                        changed = true;
                    }
                    if (changed)
                    {
                        _config.Save(_configPath);
                        _controllerLoop.UpdateConfig(_config);
                    }

                    _calibrationOverrideApplied = false;
                }

                if (_calibrationTrackingSuspended)
                {
                    _calibrationNeutralTimer?.Dispose();
                    _calibrationNeutralTimer = null;
                    _calibrationTrackingSuspended = false;
                    if (_config.Enabled)
                    {
                        _controllerLoop.Start();
                    }
                }

                Interlocked.Exchange(ref _calibrationRunning, 0);
            }
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private static bool IsLeftMouseDown()
        {
            return (GetAsyncKeyState(0x01) & 0x8000) != 0;
        }

        private sealed class HotkeyWindow : NativeWindow
        {
            public event Action<int>? HotkeyPressed;

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY)
                {
                    HotkeyPressed?.Invoke(m.WParam.ToInt32());
                }

                base.WndProc(ref m);
            }
        }

        private static string ResolvePath(string fileName)
        {
            var baseDir = AppContext.BaseDirectory;
            var found = FindUpwards(baseDir, fileName) ?? FindUpwards(Environment.CurrentDirectory, fileName);
            return found ?? Path.Combine(baseDir, fileName);
        }

        private static bool TryGetVtsClientBounds(out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            foreach (var name in VtsProcessNames)
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    var handle = process.MainWindowHandle;
                    if (handle == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!GetClientRect(handle, out var rect))
                    {
                        continue;
                    }

                    var point = new POINT { X = 0, Y = 0 };
                    if (!ClientToScreen(handle, ref point))
                    {
                        continue;
                    }

                    var width = rect.Right - rect.Left;
                    var height = rect.Bottom - rect.Top;
                    if (width <= 0 || height <= 0)
                    {
                        continue;
                    }

                    bounds = new Rectangle(point.X, point.Y, width, height);
                    return true;
                }
            }

            return false;
        }

        private static bool IsVtsForeground()
        {
            var handle = GetForegroundWindow();
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            if (GetWindowThreadProcessId(handle, out var processId) == 0 || processId == 0)
            {
                return false;
            }

            try
            {
                using var process = Process.GetProcessById((int)processId);
                foreach (var name in VtsProcessNames)
                {
                    if (string.Equals(process.ProcessName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private void PopulateDeltaRadiusMenu()
        {
            _deltaRadiusItem.DropDownItems.Clear();

            var windows = GetOpenWindows();
            if (windows.Count == 0)
            {
                _deltaRadiusItem.DropDownItems.Add(new ToolStripMenuItem("(no windows)") { Enabled = false });
                return;
            }

            foreach (var window in windows.OrderBy(w => w.ProcessName).ThenBy(w => w.Title))
            {
                var title = window.Title;
                var label = string.IsNullOrWhiteSpace(window.ProcessName)
                    ? title
                    : $"{window.ProcessName} - {title}";

                var item = new ToolStripMenuItem(label)
                {
                    Checked = IsRadiusTarget(title),
                    Tag = title
                };
                item.Click += (_, __) => ToggleRadiusTarget(title);
                _deltaRadiusItem.DropDownItems.Add(item);
            }
        }

        private bool IsRadiusTarget(string title)
        {
            var targets = _config.Model.DeltaMode.RadiusWindowTitles;
            foreach (var entry in targets)
            {
                if (string.Equals(entry, title, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void ToggleRadiusTarget(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            var targets = _config.Model.DeltaMode.RadiusWindowTitles;
            var removed = false;
            for (var i = targets.Count - 1; i >= 0; i--)
            {
                if (string.Equals(targets[i], title, StringComparison.OrdinalIgnoreCase))
                {
                    targets.RemoveAt(i);
                    removed = true;
                }
            }

            if (!removed)
            {
                targets.Add(title);
            }

            _config.Save(_configPath);
        }

        private static List<WindowInfo> GetOpenWindows()
        {
            var windows = new List<WindowInfo>();
            EnumWindows((handle, _) =>
            {
                if (!IsWindowVisible(handle))
                {
                    return true;
                }

                var length = GetWindowTextLength(handle);
                if (length <= 0)
                {
                    return true;
                }

                var buffer = new StringBuilder(length + 1);
                if (GetWindowText(handle, buffer, buffer.Capacity) <= 0)
                {
                    return true;
                }

                var title = buffer.ToString().Trim();
                if (string.IsNullOrWhiteSpace(title))
                {
                    return true;
                }

                var processName = string.Empty;
                if (GetWindowThreadProcessId(handle, out var processId) != 0 && processId != 0)
                {
                    try
                    {
                        using var process = Process.GetProcessById((int)processId);
                        processName = process.ProcessName;
                    }
                    catch
                    {
                        processName = string.Empty;
                    }
                }

                windows.Add(new WindowInfo(title, processName));
                return true;
            }, IntPtr.Zero);

            return windows;
        }

        public bool TryEnqueue(RumiAction action)
        {
            if (action == null)
            {
                return false;
            }

            switch (action.Type)
            {
                case RumiActionType.EnterAfk:
                    _controllerLoop.ForceAfk(true);
                    return true;
                case RumiActionType.ExitAfk:
                    _controllerLoop.ForceAfk(false);
                    return true;
                case RumiActionType.StartDizzy:
                    _controllerLoop.StartDizzyEffect(0);
                    return true;
                case RumiActionType.StopDizzy:
                    _controllerLoop.StopDizzyEffect();
                    return true;
                case RumiActionType.TriggerHotkey:
                    if (!string.IsNullOrWhiteSpace(action.Name))
                    {
                        var hotkeys = FindHotkeysByExpression(action.Name);
                        foreach (var hotkey in hotkeys)
                        {
                            _ = TriggerHotkeyByIdAsync(hotkey);
                        }
                        return hotkeys.Count > 0;
                    }
                    return false;
                case RumiActionType.Blink:
                    _ = TriggerExpressionAsync("blink", null);
                    return true;
                case RumiActionType.WinkLeft:
                    _ = TriggerExpressionAsync("winkLeft", null);
                    return true;
                case RumiActionType.WinkRight:
                    _ = TriggerExpressionAsync("winkRight", null);
                    return true;
                case RumiActionType.Smile:
                    _ = TriggerExpressionAsync("smile", null);
                    return true;
                case RumiActionType.HalfSmile:
                    _ = TriggerExpressionAsync("halfSmile", null);
                    return true;
                default:
                    return false;
            }
        }

        private void StartExpressionPipeServer()
        {
            if (_expressionPipeTask != null)
            {
                return;
            }

            if (!_config.Expression.Enabled)
            {
                return;
            }

            if (string.IsNullOrEmpty(_config.Expression.AuthToken))
            {
                const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
                var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(24);
                _config.Expression.AuthToken = new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
                _config.Save(_configPath);
            }

            _expressionPipeTask = Task.Run(() => ExpressionTcpLoopAsync(_cts.Token));
        }

private async Task ExpressionTcpLoopAsync(CancellationToken token)
        {
            var bindAddress = IPAddress.TryParse(_config.Expression.BindAddress, out var parsed) ? parsed : IPAddress.Any;
            var listener = new TcpListener(bindAddress, _config.Expression.Port);
            listener.Start();
            try
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        using var client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);

                        client.ReceiveTimeout = 30_000;
                        using var reader = new StreamReader(client.GetStream());
                        var authToken = _config.Expression.AuthToken;

                        while (!token.IsCancellationRequested)
                        {
                            var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                            if (line == null) break;
                            if (line.Length > 4096) break;
                            if (string.IsNullOrWhiteSpace(line)) continue;

                            if (!TryParseExpressionRequest(line, authToken, out var actions, out var durationOverride))
                            {
                                if (!string.IsNullOrEmpty(authToken)) break; // bad token — close
                                continue;
                            }

                            foreach (var actionName in actions)
                            {
                                var rumiAction = MapToRumiAction(actionName);
                                if (rumiAction != null && TryEnqueue(rumiAction)) continue;
                                _ = TriggerExpressionAsync(actionName, durationOverride);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        SetStatus($"Expression TCP error: {ex.Message}");
                        await Task.Delay(1000, token).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                listener.Stop();
                listener.Server.Dispose();
            }
        }

        private async Task TriggerExpressionAsync(string action, double? durationOverride)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return;
            }

            var hotkeys = FindHotkeysByExpression(action);
            var animationApplied = ApplyBuiltInAnimation(action, hotkeys, durationOverride);
            if (hotkeys.Count == 0 && !animationApplied)
            {
                SetStatus($"Expression '{action}' had no matching hotkeys.");
                return;
            }

            foreach (var hotkey in hotkeys)
            {
                SetStatus($"Expression '{action}' -> hotkey '{hotkey.Id}'");
                var effectiveDuration = durationOverride ?? hotkey.Expression?.DurationSeconds;
                var cooldownOverride = hotkey.Expression?.CooldownSeconds;
                await TriggerHotkeyByIdAsync(hotkey, effectiveDuration, cooldownOverride).ConfigureAwait(true);
            }

            // Built-in animation actions (sleep, wake, dizzy, etc.) are handled above; nothing more to do here.
        }

        private static RumiAction? MapToRumiAction(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return null;
            }

            var normalized = action.Trim().ToLowerInvariant();
            return normalized switch
            {
                "sleep" or "afk" => new RumiAction(RumiActionType.EnterAfk),
                "wake" or "wakeup" or "awake" => new RumiAction(RumiActionType.ExitAfk),
                "dizzy" => new RumiAction(RumiActionType.StartDizzy),
                "stopdizzy" => new RumiAction(RumiActionType.StopDizzy),
                "blink" => new RumiAction(RumiActionType.Blink),
                "winkleft" => new RumiAction(RumiActionType.WinkLeft),
                "winkright" => new RumiAction(RumiActionType.WinkRight),
                "smile" => new RumiAction(RumiActionType.Smile),
                "halfsmile" => new RumiAction(RumiActionType.HalfSmile),
                _ => null
            };
        }

        private static readonly JsonSerializerOptions ExpressionJsonOptions = new() { PropertyNameCaseInsensitive = true };

        private bool TryParseExpressionRequest(string payload, string authToken, out List<string> actions, out double? durationOverride)
        {
            actions = new List<string>();
            durationOverride = null;

            ExpressionRequest? req;
            try
            {
                req = JsonSerializer.Deserialize<ExpressionRequest>(payload, ExpressionJsonOptions);
            }
            catch
            {
                return false;
            }

            if (req == null) return false;

            // Token check — if auth is configured, every message must include the matching token
            if (!string.IsNullOrEmpty(authToken) && req.Token != authToken)
                return false;

            if (req.DurationSeconds is double duration && duration > 0)
                durationOverride = duration;

            if (req.Actions != null && req.Actions.Count > 0)
            {
                foreach (var entry in req.Actions)
                    if (!string.IsNullOrWhiteSpace(entry))
                        actions.Add(entry.Trim());
            }
            else
            {
                var parsedAction = req.Action;
                if (string.IsNullOrWhiteSpace(parsedAction) && !string.IsNullOrWhiteSpace(req.Trigger))
                    parsedAction = ExtractExpressionAction(req.Trigger!);
                if (!string.IsNullOrWhiteSpace(parsedAction))
                    actions.Add(parsedAction!.Trim());
            }

            return actions.Count > 0;
        }

        private static string? ExtractExpressionAction(string trigger)
        {
            if (string.IsNullOrWhiteSpace(trigger))
            {
                return null;
            }

            const string prefix = "onExpression.";
            if (trigger.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return trigger[prefix.Length..];
            }

            return null;
        }

        private sealed class WindowInfo
        {
            public WindowInfo(string title, string processName)
            {
                Title = title;
                ProcessName = processName;
            }

            public string Title { get; }
            public string ProcessName { get; }
        }

        private sealed class ExpressionRequest
        {
            public string? Token { get; set; }
            public string? Action { get; set; }
            public List<string>? Actions { get; set; }
            public string? Trigger { get; set; }
            public double? DurationSeconds { get; set; }
        }

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        private static string? FindUpwards(string startDir, string fileName)
        {
            var dir = new DirectoryInfo(startDir);
            for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
