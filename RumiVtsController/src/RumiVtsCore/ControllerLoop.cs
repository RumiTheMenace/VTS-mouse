using System.Diagnostics;
using System.Text.Json;
namespace RumiVtsController
{
    internal sealed class ControllerLoop : IDisposable
    {
        private static readonly JsonSerializerOptions GazeBiasReadOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly JsonSerializerOptions GazeBiasWriteOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        private readonly VtsClient _vtsClient;
        private readonly MouseInput _mouseInput;
        private readonly RawInputListener? _rawInput;
        private readonly string _configDirectory;
        private Config _config;
        private System.Threading.Timer? _timer;
        private float _headX;
        private float _headY;
        private float _headZ;
        private float _bodyX;
        private float _bodyY;
        private float _lastTargetX;
        private float _lastTargetY;
        private bool _hasHeadOutput;
        private float _lastBodyTargetX;
        private float _lastBodyTargetY;
        private bool _hasBodyOutput;
        private readonly object _modelLock = new();
        private float _modelOffsetX;
        private float _modelOffsetY;
        private bool _hasModelPosition;
        private bool _hasOutlineCenter;
        private bool _hasOutlineHeight;
        private float _outlineCenterX;
        private float _outlineCenterY;
        private float _outlineHullHeight;
        private readonly List<Models.InjectedParameterValue> _paramBuffer = new(12);
        private readonly Models.InjectedParameterValue _eyeXParam = new();
        private readonly Models.InjectedParameterValue _eyeYParam = new();
        private readonly Models.InjectedParameterValue _headXParam = new();
        private readonly Models.InjectedParameterValue _headYParam = new();
        private readonly Models.InjectedParameterValue _headZParam = new();
        private readonly Models.InjectedParameterValue _bodyXParam = new();
        private readonly Models.InjectedParameterValue _bodyYParam = new();
        private readonly Models.InjectedParameterValue _bodyZParam = new();
        private readonly Models.InjectedParameterValue _blinkLParam = new();
        private readonly Models.InjectedParameterValue _blinkRParam = new();
        private readonly Models.InjectedParameterValue _smileLParam = new();
        private readonly Models.InjectedParameterValue _smileRParam = new();
        private bool _hasCursor;
        private int _lastCursorX;
        private int _lastCursorY;
        private long _lastMovementTicks;
        private long _lastSendTicks;
        private long _lastComputeTicks;
        private static readonly double TickFrequency = Stopwatch.Frequency;
        private bool _useDeltaMode;
        private bool _hasDeltaCursor;
        private int _deltaCursorX;
        private int _deltaCursorY;
        private float _deltaOffsetX;
        private float _deltaOffsetY;
        private float _deltaSmoothedX;
        private float _deltaSmoothedY;
        private bool _deltaSpringActive;
        private float _deltaSpringTargetX;
        private float _deltaSpringTargetY;
        private long _centerHoldStartTicks;
        private long _hiddenCursorStartTicks;
        private bool _hasPrimaryState;
        private bool _lastOnPrimary;
        private bool _hasBodyCursor;
        private int _bodyCursorX;
        private int _bodyCursorY;
        private float _bodyZ;
        private float _bodyHold;
        private float _bodyFlickAverage;
        private int _bodyFlickLastSign;
        private int _bodyFlickOscillationCount;
        private bool _bodyFlickCrossedModel;
        private long _bodyFlickLastFlipTicks;
        private bool _hasHeadZCursor;
        private int _headZCursorX;
        private int _headZCursorY;
        private float _headZHold;
        private float _headZFlickAverage;
        private int _headZFlickLastSign;
        private int _headZFlickOscillationCount;
        private bool _headZFlickCrossedModel;
        private long _headZFlickLastFlipTicks;
        private float _gazeWeight = 1.0f;
        private volatile bool _trackingSuspended;
        private volatile bool _trackingFrozen;
        private volatile bool _faceFound = true;
        private bool _suspendZeroSent;
        private bool _hasFrozenCursor;
        private int _frozenCursorX;
        private int _frozenCursorY;
        private bool _frozenDeltaMode;
        private float _jitterEyeX;
        private float _jitterEyeY;
        private float _jitterHeadX;
        private float _jitterHeadY;
        private float _jitterEyeTargetX;
        private float _jitterEyeTargetY;
        private float _jitterHeadTargetX;
        private float _jitterHeadTargetY;
        private float _jitterEyeVelX;
        private float _jitterEyeVelY;
        private float _jitterHeadVelX;
        private float _jitterHeadVelY;
        private float _jitterBodyX;
        private float _jitterBodyY;
        private float _jitterBodyVelX;
        private float _jitterBodyVelY;
        private float _jitterBodyTargetX;
        private float _jitterBodyTargetY;
        private long _jitterBodyStartTicks;
        private long _jitterBodyIntervalTicks;
        private float _jitterBodyAmpScale;
        private long _lastEyeJitterTicks;
        private long _lastHeadJitterTicks;
        private long _jitterEyeStartTicks;
        private long _jitterEyeIntervalTicks;
        private float _jitterEyeAmpScale;
        private long _jitterHeadStartTicks;
        private long _jitterHeadIntervalTicks;
        private float _jitterHeadAmpScale;
        private long _lastJitterUpdateTicks;
        private int _lastMotionDeltaX;
        private int _lastMotionDeltaY;
        private long _lastAutoGazeUpdateTicks;
        private bool _centerHoverActive;
        private long _centerHoverEnterTicks;
        private bool _modelHoverActive;
        private long _modelHoverEnterTicks;
        private bool _smartIdleActive;
        private bool _afkActive;
        private bool _afkForced;
        private long _afkStartTicks;
        private bool _wakeupActive;
        private long _wakeupStartTicks;
        private bool _dizzyActive;
        private long _dizzyStartTicks;
        private long _dizzyEndTicks;
        private float _dizzyEyeX;
        private float _dizzyEyeY;
        private float _dizzyHeadX;
        private float _dizzyHeadY;
        private float _dizzyBodyX;
        private float _dizzyBodyY;
        private float _dizzyBodyZ;
        private float _dizzyHeadZ;

        private bool _sleepFadingIn;

        // AFK sleep-lerp entry values
        private float _afkEntryHeadX;
        private float _afkEntryHeadY;
        private float _afkEntryHeadZ;
        private float _afkEntryBodyX;
        private float _afkEntryBodyY;
        private float _afkEntryBodyZ;
        private float _afkEntryEyeX;
        private float _afkEntryEyeY;

        // Last-frame output values (captured for entry-value lerps)
        private float _lastOutputHeadZ;
        private float _lastOutputBodyZ;
        private float _lastOutputEyeX;
        private float _lastOutputEyeY;

        // Wake ease-out state
        private bool _wakeEaseOutActive;
        private long _wakeEaseOutStartTicks;
        private long _wakeEaseOutEndTicks;
        private float _wakeEaseOutHeadX;
        private float _wakeEaseOutHeadY;
        private float _wakeEaseOutHeadZ;
        private float _wakeEaseOutBodyX;
        private float _wakeEaseOutBodyY;
        private float _wakeEaseOutBodyZ;
        private float _wakeEaseOutEyeX;
        private float _wakeEaseOutEyeY;
        private float _wakeEaseOutBlinkL;
        private float _wakeEaseOutBlinkR;
        private float _wakeEaseOutSmileL;
        private float _wakeEaseOutSmileR;

        // Wake jolt phased sequence
        private bool _wakeJoltActive;
        private int _wakeJoltPhase; // 0=Jump 1=Hold 2=Compose 3=Breathe
        private long _wakeJoltPhaseStartTicks;
        private float _wakeJoltEntryHeadX;
        private float _wakeJoltEntryHeadY;
        private float _wakeJoltEntryHeadZ;
        private float _wakeJoltEntryBodyY;
        private float _wakeJoltEntryEyeX;
        private float _wakeJoltEntryEyeY;

        // Time delta for frame-rate-independent dizzy blend
        private float _lastTickDeltaSeconds;

        private bool _blinkActive;
        private long _blinkStartTicks;
        private long _blinkEndTicks;
        private long _blinkDurationTicks;
        private long _blinkNextTicks;
        private long _blinkCooldownEndTicks;
        private bool _smileActive;
        private long _smileStartTicks;
        private long _smileEndTicks;
        private long _smileCooldownEndTicks;
        private bool _winkActive;
        private int _winkSide;
        private long _winkStartTicks;
        private long _winkEndTicks;
        private long _winkDurationTicks;
        private long _winkCooldownEndTicks;
        private bool _lastCenterHover;
        private bool _lastModelHover;
        private bool _lastDeltaMode;
        private bool _lastSmartIdle;
        private bool _lastAfk;
        private bool _lastDizzy;
        private int _windowWidth;
        private int _windowHeight;
        private bool _hasWindowSize;
        private int _windowLeft;
        private int _windowTop;
        private int _windowClientWidth;
        private int _windowClientHeight;
        private bool _hasWindowBounds;
        private bool _autoWakeResetPending;

        // Bounce DVD animation
        private bool _bounceDvdActive;
        private float _bounceX;
        private float _bounceY;
        private float _velX;
        private float _velY;
        private float _bounceRotation;
        private bool _bounceVelInitialized;
        private long _lastBounceTicks;

        // Autonomous gaze - interest center
        private float _autoInterestX;
        private float _autoInterestY;

        // Autonomous gaze - eye spring
        private float _autoGazeX;
        private float _autoGazeY;
        private float _autoGazeVelX;
        private float _autoGazeVelY;

        // Autonomous gaze - head spring
        private float _autoHeadX;
        private float _autoHeadY;
        private float _autoHeadVelX;
        private float _autoHeadVelY;
        private float _autoHeadInterestX;
        private float _autoHeadInterestY;
        private float _autoHeadZ;
        private float _autoHeadZVel;

        // Autonomous gaze - body spring
        private float _autoBodyX;
        private float _autoBodyY;
        private float _autoBodyVelX;
        private float _autoBodyVelY;
        private float _autoBodyInterestX;
        private float _autoBodyInterestY;

        // Saccade target
        private float _autoSaccadeTargetX;
        private float _autoSaccadeTargetY;
        private long _autoSaccadeStartTicks;
        private long _autoSaccadeIntervalTicks;

        // Head and body interval timers
        private long _autoHeadIntervalStartTicks;
        private long _autoHeadIntervalTicks;
        private long _autoBodyIntervalStartTicks;
        private long _autoBodyIntervalTicks;

        // Gaze history grid - both counters live here
        private float[,] _mouseDwellGrid = new float[0, 0];
        private float[,] _gazeVisitGrid = new float[0, 0];
        private int _gazeGridSize;
        private long _lastBiasPersistTicks;

        // Weighted centroid of mouse dwell - the decay target when mouse is still
        private float _biasCentroidX;
        private float _biasCentroidY;
        private bool _hasBiasCentroid;

        private const float SoftClampRange = 2.0f;
        private static readonly float SoftClampNorm = MathF.Tanh(1.0f);
        private static readonly float[] BlinkFadeStrip = [0.9f, 0.5f, 0.7f, 0.3f, 0.5f, 0.0f];

        public event Action<bool>? MonitorTransitioned;
        public event Action<bool>? DeltaModeChanged;
        public event Action? CenterHovered;
        public event Action? CenterExited;
        public event Action? ModelHovered;
        public event Action? ModelExited;
        public event Action? DizzyTriggered;
        public event Action? WakeJoltPanic;
        public event Action? WakeJoltReturn;
        public event Action<bool>? SmartIdleChanged;
        public event Action<bool>? AfkChanged;

        public bool IsDizzyActive => _dizzyActive;

        public ControllerLoop(VtsClient vtsClient, MouseInput mouseInput, RawInputListener? rawInput, Config config, string configPath)
        {
            _vtsClient = vtsClient;
            _mouseInput = mouseInput;
            _rawInput = rawInput;
            _config = config;
            _configDirectory = Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory;
            LoadGazeBias();
        }

        public void UpdateConfig(Config config)
        {
            ConfigConflictResolver.ResolveMutualExclusion(
                _config,
                config,
                c => c.Model.LegacyGaze.Enabled,
                (c, v) => c.Model.LegacyGaze.Enabled = v,
                c => c.Model.AutonomousGaze.Enabled,
                (c, v) => c.Model.AutonomousGaze.Enabled = v,
                defaultA: true);

            SaveGazeBias(force: true);
            _config = config;
            LoadGazeBias();
            if (_timer != null)
            {
                var intervalMs = Math.Max(1, (int)Math.Round(1000.0 / Math.Max(1, _config.Vts.Inject.Hz)));
                _timer.Change(0, intervalMs);
            }
        }

        public void UpdateWindowSize(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            lock (_modelLock)
            {
                _windowWidth = width;
                _windowHeight = height;
                _hasWindowSize = true;
            }
        }

        public void UpdateWindowBounds(Rectangle bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            lock (_modelLock)
            {
                _windowLeft = bounds.Left;
                _windowTop = bounds.Top;
                _windowClientWidth = bounds.Width;
                _windowClientHeight = bounds.Height;
                _hasWindowBounds = true;
            }
        }

        public void SetTrackingSuspended(bool suspended)
        {
            _trackingSuspended = suspended;
        }

        public void SetFaceFound(bool faceFound)
        {
            _faceFound = faceFound;
            if (faceFound)
            {
                _suspendZeroSent = false;
            }
        }

        public void SetTrackingFrozen(bool frozen)
        {
            _trackingFrozen = frozen;
            if (!_trackingFrozen)
            {
                _hasFrozenCursor = false;
            }
            else
            {
                var cursor = _mouseInput.GetCursorPosition();
                _frozenCursorX = cursor.X;
                _frozenCursorY = cursor.Y;
                _hasFrozenCursor = true;
                _frozenDeltaMode = _useDeltaMode;
            }
        }

        public void Start()
        {
            if (_timer != null)
            {
                return;
            }

            var intervalMs = Math.Max(1, (int)Math.Round(1000.0 / Math.Max(1, _config.Vts.Inject.Hz)));
            _timer = new System.Threading.Timer(_ => Tick(), null, 0, intervalMs);
        }

        public void Stop()
        {
            SaveGazeBias(force: true);
            _timer?.Dispose();
            _timer = null;
        }

        public void Dispose()
        {
            Stop();
        }

        public void UpdateModelTransform(float? offsetX, float? offsetY, float? scale)
        {
            lock (_modelLock)
            {
                if (offsetX.HasValue && float.IsFinite(offsetX.Value))
                {
                    _modelOffsetX = offsetX.Value;
                    _hasModelPosition = true;
                }

                if (offsetY.HasValue && float.IsFinite(offsetY.Value))
                {
                    _modelOffsetY = offsetY.Value;
                    _hasModelPosition = true;
                }

                _ = scale;
            }
        }

        public void UpdateOutlineInfo(VtsClient.ModelOutlineInfo info)
        {
            lock (_modelLock)
            {
                if (info.HasCenter)
                {
                    if (!info.IsNormalized && info.WindowWidth > 0 && info.WindowHeight > 0)
                    {
                        _outlineCenterX = info.CenterX / (info.WindowWidth / 2.0f);
                        _outlineCenterY = info.CenterY / (info.WindowHeight / 2.0f);
                    }
                    else
                    {
                        _outlineCenterX = info.CenterX;
                        _outlineCenterY = info.CenterY;
                    }

                    _hasOutlineCenter = true;
                }

                if (info.HasHullHeight)
                {
                    if (!info.IsNormalized && info.WindowHeight > 0)
                    {
                        _outlineHullHeight = info.HullHeight / (info.WindowHeight / 2.0f);
                    }
                    else
                    {
                        _outlineHullHeight = info.HullHeight;
                    }

                    _hasOutlineHeight = true;
                }
            }
        }

        public bool TryGetOutlineHeightNormalized(out float height)
        {
            lock (_modelLock)
            {
                if (_hasOutlineHeight)
                {
                    height = _outlineHullHeight;
                    return true;
                }
            }

            height = 0.0f;
            return false;
        }

        private bool IsBounceDvdTriggered()
        {
            if (_trackingSuspended) return false;
            var anim = _config.Animations;
            if (!anim.Enabled || !anim.BounceDvd.Enabled) return false;
            // Non-exclusive bounce yields to exclusive animations unless stackable
            if (!anim.BounceDvd.Exclusive && !anim.BounceDvd.Stackable)
            {
                if ((_wakeupActive || _wakeEaseOutActive) && anim.Wake.Exclusive) return false;
                if ((_afkActive || _sleepFadingIn) && anim.Sleep.Exclusive) return false;
                if (_dizzyActive && anim.Dizzy.Exclusive) return false;
            }

            var trigger = anim.BounceDvd.Trigger ?? string.Empty;
            return trigger switch
            {
                "onSmartMode"  => _smartIdleActive,
                "offSmartMode" => !_smartIdleActive,
                "onAFK"        => _afkActive,
                "offAFK"       => !_afkActive,
                "onDeltaMode"  => _useDeltaMode,
                "offDeltaMode" => !_useDeltaMode,
                "onCenter"     => _centerHoverActive,
                "offCenter"    => !_centerHoverActive,
                "onModel"      => _modelHoverActive,
                "offModel"     => !_modelHoverActive,
                "onDizzy"      => _dizzyActive,
                "offDizzy"     => !_dizzyActive,
                "always"       => true,
                _              => false
            };
        }

        private void TickBounceDvd(long nowTicks)
        {
            var shouldBeActive = IsBounceDvdTriggered();
            var wasActive = _bounceDvdActive;
            _bounceDvdActive = shouldBeActive;

            if (wasActive && !_bounceDvdActive)
            {
                _bounceX = 0f;
                _bounceY = 0f;
                _bounceRotation = 0f;
                _velX = 0f;
                _velY = 0f;
                _bounceVelInitialized = false;
                _lastBounceTicks = 0;
                _ = _vtsClient.MoveModelAsync(0f, 0f, 0f, _config.Animations.BounceDvd.ResetScale, CancellationToken.None);
                return;
            }

            if (!_bounceDvdActive)
            {
                return;
            }

            var dvd = _config.Animations.BounceDvd;

            if (!_bounceVelInitialized)
            {
                var angleDeg = Random.Shared.NextSingle() * 359f + 1f;
                var angleRad = angleDeg * MathF.PI / 180f;
                _velX = MathF.Cos(angleRad) * dvd.SpeedX;
                _velY = MathF.Sin(angleRad) * dvd.SpeedY;
                _bounceVelInitialized = true;
                _lastBounceTicks = nowTicks;
            }

            // Advance position (normalized units per tick)
            _bounceX += _velX;
            _bounceY += _velY;

            // Hard-reflect off normalized screen bounds [-1, 1]
            if (_bounceX > 1.0f)
            {
                _bounceX = 2.0f - _bounceX;
                _velX = -MathF.Abs(_velX);
            }
            else if (_bounceX < -1.0f)
            {
                _bounceX = -2.0f - _bounceX;
                _velX = MathF.Abs(_velX);
            }

            if (_bounceY > 1.0f)
            {
                _bounceY = 2.0f - _bounceY;
                _velY = -MathF.Abs(_velY);
            }
            else if (_bounceY < -1.0f)
            {
                _bounceY = -2.0f - _bounceY;
                _velY = MathF.Abs(_velY);
            }

            // Advance spin (degrees per second); direction tied to Y velocity sign
            if (_lastBounceTicks > 0 && dvd.SpinSpeed != 0f)
            {
                var dt = (float)((nowTicks - _lastBounceTicks) / TickFrequency);
                if (dt > 0.1f) dt = 0.1f;
                var spinDir = _velY >= 0.0f ? 1.0f : -1.0f;
                _bounceRotation += spinDir * dvd.SpinSpeed * dt;
                if (_bounceRotation >= 360f) _bounceRotation -= 360f;
                else if (_bounceRotation < -360f) _bounceRotation += 360f;
            }
            _lastBounceTicks = nowTicks;

            _ = _vtsClient.MoveModelAsync(_bounceX, _bounceY, _bounceRotation, dvd.BounceScale, CancellationToken.None);
        }

        private void EvaluateIdleState(long nowTicks)
        {
            var smartEnabled = _config.Vts.Smart.Enabled;
            var idleSeconds = 0.0;
            var isIdle = smartEnabled && IsSmartIdle(nowTicks, out idleSeconds);
            if (isIdle != _smartIdleActive)
            {
                _smartIdleActive = isIdle;
                SmartIdleChanged?.Invoke(_smartIdleActive);
            }

            var isAfk = false;
            if (_afkForced)
            {
                isAfk = true;
            }
            else
            {
                var afkAfterSeconds = Math.Max(0.0, _config.Vts.Smart.AfkAfterSeconds);
                isAfk = isIdle && afkAfterSeconds > 0.0 && idleSeconds >= afkAfterSeconds;
            }
            var wasAfk = _afkActive;
            if (isAfk != _afkActive)
            {
                ApplyAfkStateChange(isAfk, wasAfk, nowTicks);
                AfkChanged?.Invoke(_afkActive);
            }
        }

        private void ApplyAfkStateChange(bool isAfk, bool wasAfk, long nowTicks)
        {
            _afkActive = isAfk;
            if (_afkActive)
            {
                if (_config.Animations.Sleep.Exclusive)
                {
                    StopDizzyEffect();
                    _wakeupActive = false;
                    _wakeEaseOutActive = false;
                }
                _afkStartTicks = nowTicks;
                _wakeupActive = false;
                _wakeEaseOutActive = false;
                _afkEntryHeadX = _headX;
                _afkEntryHeadY = _headY;
                _afkEntryBodyX = _bodyX;
                _afkEntryBodyY = _bodyY;
                _afkEntryBodyZ = _lastOutputBodyZ;
                _afkEntryHeadZ = _lastOutputHeadZ;
                _afkEntryEyeX = _lastOutputEyeX;
                _afkEntryEyeY = _lastOutputEyeY;
                return;
            }

            _sleepFadingIn = false;
            var wakeAnim = _config.Animations.Wake;
            if (_config.Animations.Sleep.WakeOnEnd && wakeAnim.Enabled && wasAfk)
            {
                if (wakeAnim.Exclusive)
                {
                    StopDizzyEffect();
                }
                _autoWakeResetPending = true;
                var jolt = wakeAnim.WakeJolt;
                if (jolt.Enabled)
                {
                    _wakeupActive = true;
                    _wakeupStartTicks = nowTicks;
                    _wakeJoltActive = true;
                    _wakeJoltPhase = 0;
                    _wakeJoltPhaseStartTicks = nowTicks;
                    _wakeJoltEntryHeadX = _headX;
                    _wakeJoltEntryHeadY = _headY;
                    _wakeJoltEntryHeadZ = _lastOutputHeadZ;
                    _wakeJoltEntryBodyY = _bodyY;
                    _wakeJoltEntryEyeX = _lastOutputEyeX;
                    _wakeJoltEntryEyeY = _lastOutputEyeY;
                    WakeJoltPanic?.Invoke();
                }
                else
                {
                    var easeOutSecs = Math.Max(0.0f, wakeAnim.EaseOutSeconds);
                    if (easeOutSecs > 0.0f)
                    {
                        _wakeEaseOutActive = true;
                        _wakeEaseOutStartTicks = nowTicks;
                        _wakeEaseOutEndTicks = nowTicks + (long)(easeOutSecs * TickFrequency);
                        _wakeEaseOutHeadX = _headX;
                        _wakeEaseOutHeadY = _headY;
                        _wakeEaseOutHeadZ = _lastOutputHeadZ;
                        _wakeEaseOutBodyX = _bodyX;
                        _wakeEaseOutBodyY = _bodyY;
                        _wakeEaseOutBodyZ = _lastOutputBodyZ;
                        _wakeEaseOutEyeX = _lastOutputEyeX;
                        _wakeEaseOutEyeY = _lastOutputEyeY;
                        _wakeEaseOutBlinkL = -1.0f;
                        _wakeEaseOutBlinkR = -1.0f;
                        _wakeEaseOutSmileL = Math.Clamp(_config.Animations.Sleep.Sleep.Eye.Smile, -1.0f, 1.0f);
                        _wakeEaseOutSmileR = _wakeEaseOutSmileL;
                    }
                }
            }
        }

        private void Tick()
        {
            var nowTicks = Stopwatch.GetTimestamp();

            var rawDt = _lastComputeTicks > 0
                ? (float)((nowTicks - _lastComputeTicks) / TickFrequency)
                : (float)(1.0 / Math.Max(1, _config.Vts.Inject.Hz));
            _lastTickDeltaSeconds = Math.Min(rawDt, 0.1f);

            EvaluateIdleState(nowTicks);

            if (!_vtsClient.IsAuthenticated)
            {
                return;
            }

            TickBounceDvd(nowTicks);

            int? rawDeltaX = null;
            int? rawDeltaY = null;
            var rawPreferred = false;
            var cursor = Point.Empty;

            if (!_trackingSuspended)
            {
                cursor = _mouseInput.GetCursorPosition();
                if (_trackingFrozen)
                {
                    if (!_hasFrozenCursor)
                    {
                        _frozenCursorX = cursor.X;
                        _frozenCursorY = cursor.Y;
                        _hasFrozenCursor = true;
                    }
                    else
                    {
                        cursor = new Point(_frozenCursorX, _frozenCursorY);
                    }
                }
                var screen = Screen.FromPoint(cursor) ?? Screen.PrimaryScreen;
                if (screen != null)
                {
                    UpdateMonitorTransition(cursor);

                    var cursorVisible = _mouseInput.IsCursorVisible();
                    var useDeltaMode = _trackingFrozen
                        ? _frozenDeltaMode
                        : ShouldUseDeltaMode(cursor, screen, cursorVisible, nowTicks);
                    if (useDeltaMode != _useDeltaMode)
                    {
                        _useDeltaMode = useDeltaMode;
                        _lastMovementTicks = nowTicks;
                        DeltaModeChanged?.Invoke(_useDeltaMode);
                        if (!_useDeltaMode)
                        {
                            _deltaOffsetX = 0.0f;
                            _deltaOffsetY = 0.0f;
                            _deltaSmoothedX = 0.0f;
                            _deltaSmoothedY = 0.0f;
                            _deltaSpringActive = false;
                            _deltaSpringTargetX = 0.0f;
                            _deltaSpringTargetY = 0.0f;
                            _centerHoldStartTicks = 0;
                        }
                        else
                        {
                            _hasDeltaCursor = false;
                            _deltaOffsetX = 0.0f;
                            _deltaOffsetY = 0.0f;
                            _deltaSmoothedX = 0.0f;
                            _deltaSmoothedY = 0.0f;
                            _deltaSpringActive = false;
                            _deltaSpringTargetX = 0.0f;
                            _deltaSpringTargetY = 0.0f;
                        }
                    }

                    var rawAvailable = _rawInput != null && _config.Model.DeltaMode.RawInput.Enabled;
                    var hasRawDelta = false;
                    var rawDx = 0;
                    var rawDy = 0;
                    if (_trackingFrozen && rawAvailable)
                    {
                        _rawInput!.TryConsumeDelta(out _, out _);
                    }
                    else if (rawAvailable && _rawInput!.TryConsumeDelta(out rawDx, out rawDy))
                    {
                        hasRawDelta = true;
                    }

                    rawPreferred = _useDeltaMode && rawAvailable && _config.Model.DeltaMode.RawInput.PreferRawDelta;
                    rawDeltaX = hasRawDelta ? rawDx : (int?)null;
                    rawDeltaY = hasRawDelta ? rawDy : (int?)null;

                    UpdateMovementState(cursor.X, cursor.Y, nowTicks, rawDeltaX, rawDeltaY);

                    AdvanceFaceAnimationState(nowTicks);
                    UpdateGazeHistory(nowTicks);

                    var smartEnabled = _config.Vts.Smart.Enabled;
                    if (smartEnabled && ShouldSkipCompute(nowTicks))
                    {
                        return;
                    }
                }
            }

            var smartEnabledOuter = _config.Vts.Smart.Enabled;
            var shouldSkip = !_trackingSuspended && smartEnabledOuter && ShouldSkipSend(nowTicks);
            var cursorMotionMagnitude = MathF.Sqrt((_lastMotionDeltaX * _lastMotionDeltaX) + (_lastMotionDeltaY * _lastMotionDeltaY));

            float eyeX = 0.0f;
            float eyeY = 0.0f;
            float headZ = 0.0f;
            var eyeEnabled = _config.Eye.Weight > 0.0f
                && !string.IsNullOrWhiteSpace(_config.Eye.ParamX)
                && !string.IsNullOrWhiteSpace(_config.Eye.ParamY);
            var headXYEnabled = _config.Head.Weight > 0.0f
                && !string.IsNullOrWhiteSpace(_config.Head.ParamX)
                && !string.IsNullOrWhiteSpace(_config.Head.ParamY);
            var headZEnabled = _config.Head.WeightZ > 0.0f
                && !string.IsNullOrWhiteSpace(_config.Head.ParamZ);
            var bodyZEnabled = _config.Body.WeightZ > 0.0f
                && !string.IsNullOrWhiteSpace(_config.Body.ParamZ);
            var bodyXYEnabled = _config.Body.Weight > 0.0f
                && !string.IsNullOrWhiteSpace(_config.Body.ParamX)
                && !string.IsNullOrWhiteSpace(_config.Body.ParamY);

            if (!_trackingSuspended)
            {
                float modelOffsetY = 0.0f;
                float modelOffsetX = 0.0f;
                float outlineCenterX = 0.0f;
                float outlineCenterY = 0.0f;
                float outlineHeight = 0.0f;
                bool hasModelPosition;
                bool hasOutlineCenter;
                bool hasOutlineHeight;
                int snapWindowLeft = 0;
                int snapWindowTop = 0;
                int snapWindowClientWidth = 0;
                int snapWindowClientHeight = 0;
                int snapWindowWidth = 0;
                int snapWindowHeight = 0;
                bool snapHasWindowBounds;
                bool snapHasWindowSize;

                lock (_modelLock)
                {
                    modelOffsetX = _modelOffsetX;
                    modelOffsetY = _modelOffsetY;
                    hasModelPosition = _hasModelPosition;
                    outlineCenterX = _outlineCenterX;
                    outlineCenterY = _outlineCenterY;
                    outlineHeight = _outlineHullHeight;
                    hasOutlineCenter = _hasOutlineCenter;
                    hasOutlineHeight = _hasOutlineHeight;
                    snapWindowLeft = _windowLeft;
                    snapWindowTop = _windowTop;
                    snapWindowClientWidth = _windowClientWidth;
                    snapWindowClientHeight = _windowClientHeight;
                    snapWindowWidth = _windowWidth;
                    snapWindowHeight = _windowHeight;
                    snapHasWindowBounds = _hasWindowBounds;
                    snapHasWindowSize = _hasWindowSize;
                }

                if (_bounceDvdActive)
                {
                    modelOffsetX = _bounceX;
                    modelOffsetY = _bounceY;
                    hasModelPosition = true;
                }

                var screen = Screen.FromPoint(cursor) ?? Screen.PrimaryScreen;
                if (screen != null)
                {
                    var trackingBounds = ResolveTrackingBounds(screen);
                    if (trackingBounds.Width <= 0 || trackingBounds.Height <= 0)
                    {
                        trackingBounds = screen.Bounds;
                    }

                    var primaryBounds = Screen.PrimaryScreen?.Bounds ?? trackingBounds;
                    var vtsOnPrimary = false;
                    if (snapHasWindowBounds)
                    {
                        var windowBounds = new Rectangle(snapWindowLeft, snapWindowTop, snapWindowClientWidth, snapWindowClientHeight);
                        if (windowBounds.Width > 0 && windowBounds.Height > 0)
                        {
                            var windowScreen = Screen.FromRectangle(windowBounds);
                            vtsOnPrimary = windowScreen != null && windowScreen.Primary;
                        }
                    }

                    var preferPrimarySpace = _config.Model.Mapping.UsePrimaryMonitor && !vtsOnPrimary;
                    var vtsWidth = trackingBounds.Width;
                    var vtsHeight = trackingBounds.Height;
                    if (snapHasWindowSize)
                    {
                        vtsWidth = snapWindowWidth;
                        vtsHeight = snapWindowHeight;
                    }
                    else if (snapHasWindowBounds)
                    {
                        vtsWidth = snapWindowClientWidth;
                        vtsHeight = snapWindowClientHeight;
                    }
                    if (vtsWidth <= 0 || vtsHeight <= 0)
                    {
                        vtsWidth = trackingBounds.Width;
                        vtsHeight = trackingBounds.Height;
                    }

                    var windowWidth = trackingBounds.Width;
                    var windowHeight = trackingBounds.Height;
                    var baseLeft = trackingBounds.Left;
                    var baseTop = trackingBounds.Top;
                    if (!preferPrimarySpace)
                    {
                        if (snapHasWindowBounds)
                        {
                            // Always use Win32 client bounds for position and size so that
                            // baseCenterX is in the same pixel space as the cursor coordinates.
                            baseLeft = snapWindowLeft;
                            baseTop = snapWindowTop;
                            windowWidth = snapWindowClientWidth;
                            windowHeight = snapWindowClientHeight;
                        }
                        else if (snapHasWindowSize)
                        {
                            windowWidth = snapWindowWidth;
                            windowHeight = snapWindowHeight;
                        }
                    }
                    if (windowWidth <= 0 || windowHeight <= 0)
                    {
                        windowWidth = trackingBounds.Width;
                        windowHeight = trackingBounds.Height;
                    }

                    var absoluteWindowWidth = trackingBounds.Width;
                    var absoluteWindowHeight = trackingBounds.Height;
                    var absoluteBaseLeft = trackingBounds.Left;
                    var absoluteBaseTop = trackingBounds.Top;
                    if (snapHasWindowBounds)
                    {
                        absoluteBaseLeft = snapWindowLeft;
                        absoluteBaseTop = snapWindowTop;
                        absoluteWindowWidth = snapWindowClientWidth;
                        absoluteWindowHeight = snapWindowClientHeight;
                    }
                    else if (snapHasWindowSize)
                    {
                        absoluteWindowWidth = snapWindowWidth;
                        absoluteWindowHeight = snapWindowHeight;
                    }

                    if (absoluteWindowWidth <= 0 || absoluteWindowHeight <= 0)
                    {
                        absoluteWindowWidth = trackingBounds.Width;
                        absoluteWindowHeight = trackingBounds.Height;
                    }

                    var halfWindowWidth = windowWidth / 2.0f;
                    var halfWindowHeight = windowHeight / 2.0f;
                    var baseCenterX = baseLeft + halfWindowWidth;
                    var baseCenterY = baseTop + halfWindowHeight;
                    var centerX = baseCenterX;
                    var centerY = baseCenterY;
                    var absoluteHalfWindowWidth = absoluteWindowWidth / 2.0f;
                    var absoluteHalfWindowHeight = absoluteWindowHeight / 2.0f;
                    var absoluteBaseCenterX = absoluteBaseLeft + absoluteHalfWindowWidth;
                    var absoluteBaseCenterY = absoluteBaseTop + absoluteHalfWindowHeight;
                    var worldRelativeCenterX = absoluteBaseCenterX;
                    var worldRelativeCenterY = absoluteBaseCenterY;

                    if (_config.Model.UseModelCenter)
                    {
                        var offsetY = _config.Model.OffsetY;
                        var scale = 1.0f;
                        if (hasOutlineHeight && _config.Model.OutlineRefHeight > 0.0f)
                        {
                            scale = outlineHeight / Math.Max(0.0001f, _config.Model.OutlineRefHeight);
                        }

                        var scaledOffsetY = offsetY * scale;
                        if (preferPrimarySpace)
                        {
                            var scaleY = primaryBounds.Height / Math.Max(1.0f, vtsHeight);
                            scaledOffsetY *= scaleY;
                        }
                        if (hasModelPosition)
                        {
                            var vtsMinHalf = Math.Min(vtsWidth, vtsHeight) / 2.0f;
                            var modelOffsetPxX = modelOffsetX * vtsMinHalf;
                            var modelOffsetPxY = -modelOffsetY * (vtsHeight / 2.0f);
                            if (preferPrimarySpace)
                            {
                                var scaleX = primaryBounds.Height / Math.Max(1.0f, vtsHeight);
                                var scaleY = primaryBounds.Height / Math.Max(1.0f, vtsHeight);
                                modelOffsetPxX *= scaleX;
                                modelOffsetPxY *= scaleY;
                            }

                            centerX += modelOffsetPxX;
                            centerY += modelOffsetPxY + scaledOffsetY;
                        }
                        else
                        {
                            centerY += scaledOffsetY;
                        }

                        if (hasModelPosition)
                        {
                            var vtsMinHalf = Math.Min(vtsWidth, vtsHeight) / 2.0f;
                            var modelOffsetPxX = modelOffsetX * vtsMinHalf;
                            var modelOffsetPxY = -modelOffsetY * (vtsHeight / 2.0f);
                            worldRelativeCenterX += modelOffsetPxX;
                            worldRelativeCenterY += modelOffsetPxY + (offsetY * scale);
                        }
                        else
                        {
                            worldRelativeCenterY += offsetY * scale;
                        }
                    }

                    UpdateHoverTriggers(
                        nowTicks,
                        cursor,
                        centerX,
                        centerY,
                        baseCenterX,
                        baseCenterY,
                        preferPrimarySpace,
                        vtsWidth,
                        vtsHeight,
                        hasOutlineCenter,
                        outlineCenterX,
                        outlineCenterY,
                        hasOutlineHeight,
                        outlineHeight,
                        primaryBounds);

                    var wr = _config.Model.Mapping.WorldRelative;
                    var useWorldRelative = !_useDeltaMode && wr.Enabled;
                    var rangeX = _config.Model.Mapping.RangePxX;
                    var rangeY = _config.Model.Mapping.RangePxY;
                    if (useWorldRelative)
                    {
                        var mainBounds = ResolveMainMonitor()?.Bounds ?? SystemInformation.VirtualScreen;
                        rangeX = Math.Abs(worldRelativeCenterX - mainBounds.Left) / Math.Max(0.01f, wr.Sensitivity);
                        rangeY = Math.Abs(worldRelativeCenterY - mainBounds.Top) / Math.Max(0.01f, wr.Sensitivity);
                    }

                    var invRangeX = 1.0f / Math.Max(1.0f, rangeX);
                    var invRangeY = 1.0f / Math.Max(1.0f, rangeY);
                    var clampRadius = _config.Model.Mapping.ClampRadius;
                    var eyeScaleX = _config.Eye.Scale * _config.Eye.ScaleX;
                    var eyeScaleY = _config.Eye.Scale * _config.Eye.ScaleY;

                    float headTargetX = 0.0f;
                    float headTargetY = 0.0f;
                    float bodyTargetX = 0.0f;
                    float bodyTargetY = 0.0f;

                    if (_useDeltaMode)
                    {
                        var useRaw = rawPreferred && rawDeltaX.HasValue && rawDeltaY.HasValue;
                        UpdateDeltaOffset(
                            cursor.X,
                            cursor.Y,
                            rangeX,
                            rangeY,
                            useRaw ? rawDeltaX : null,
                            useRaw ? rawDeltaY : null,
                            allowCursorFallback: !rawPreferred);
                        var smoothing = Math.Clamp(_config.Model.DeltaMode.Smoothing, 0.0f, 1.0f);
                        if (smoothing <= 0.0f)
                        {
                            _deltaSmoothedX = _deltaOffsetX;
                            _deltaSmoothedY = _deltaOffsetY;
                        }
                        else
                        {
                            _deltaSmoothedX = Lerp(_deltaSmoothedX, _deltaOffsetX, smoothing);
                            _deltaSmoothedY = Lerp(_deltaSmoothedY, _deltaOffsetY, smoothing);
                        }
                        var baseRawX = (float)((baseCenterX - centerX) * invRangeX);
                        var baseRawY = (float)((centerY - baseCenterY) * invRangeY);

                        baseRawX = SoftClampRaw(baseRawX, SoftClampRange);
                        baseRawY = SoftClampRaw(baseRawY, SoftClampRange);

                        var eyeOffsetScale = Math.Max(0.0f, _config.Model.DeltaMode.EyeOffsetScale);
                        if (eyeEnabled)
                        {
                            var (baseEyeX, baseEyeY) = ApplyCircularClamp(baseRawX, baseRawY, clampRadius);
                            var (scaledEyeX, scaledEyeY) = ApplyCircularClamp(baseEyeX * eyeScaleX, baseEyeY * eyeScaleY);
                            eyeX = Math.Clamp(scaledEyeX + (_deltaSmoothedX * eyeOffsetScale), -1.0f, 1.0f);
                            eyeY = Math.Clamp(scaledEyeY + (_deltaSmoothedY * eyeOffsetScale), -1.0f, 1.0f);
                        }

                        var baseTargetX = Math.Clamp(baseRawX, -1.0f, 1.0f);
                        var baseTargetY = Math.Clamp(baseRawY, -1.0f, 1.0f);
                        var (baseHeadX, baseHeadY) = ApplyCircularClamp(baseTargetX, baseTargetY, 1.0f);
                        var sharedTargetX = Math.Clamp(baseHeadX + _deltaSmoothedX, -1.0f, 1.0f);
                        var sharedTargetY = Math.Clamp(baseHeadY + _deltaSmoothedY, -1.0f, 1.0f);

                        if (headXYEnabled)
                        {
                            headTargetX = sharedTargetX;
                            headTargetY = sharedTargetY;
                        }

                        if (bodyXYEnabled)
                        {
                            bodyTargetX = sharedTargetX;
                            bodyTargetY = sharedTargetY;
                        }
                    }
                    else
                    {
                        var gazeCenterX = useWorldRelative ? worldRelativeCenterX : centerX;
                        var gazeCenterY = useWorldRelative ? worldRelativeCenterY : centerY;
                        var dx = cursor.X - gazeCenterX;
                        var dy = gazeCenterY - cursor.Y;
                        var rawX = (float)(dx * invRangeX);
                        var rawY = (float)(dy * invRangeY);

                        if (useWorldRelative)
                        {
                            var cursorScreen = Screen.FromPoint(cursor);
                            float targetGazeWeight;
                            if (cursorScreen != null && snapHasWindowBounds)
                            {
                                var rumiScreen = Screen.FromRectangle(new Rectangle(snapWindowLeft, snapWindowTop, snapWindowClientWidth, snapWindowClientHeight));
                                var mainScreen = ResolveMainMonitor();
                                if (rumiScreen != null && cursorScreen.DeviceName == rumiScreen.DeviceName)
                                {
                                    targetGazeWeight = 1.0f;
                                }
                                else if (mainScreen != null && cursorScreen.DeviceName == mainScreen.DeviceName)
                                {
                                    targetGazeWeight = 1.0f;
                                }
                                else
                                {
                                    targetGazeWeight = Math.Clamp(wr.OtherMonitorWeight, 0.0f, 1.0f);
                                }
                            }
                            else
                            {
                                targetGazeWeight = 1.0f;
                            }

                            _gazeWeight = Lerp(_gazeWeight, targetGazeWeight, wr.WeightLerpSpeed);
                            rawX *= _gazeWeight;
                            rawY *= _gazeWeight;
                        }

                        rawX = SoftClampRaw(rawX, SoftClampRange);
                        rawY = SoftClampRaw(rawY, SoftClampRange);

                        if (_autoWakeResetPending)
                        {
                            ResetAutonomousGaze(rawX, rawY);
                            _autoWakeResetPending = false;
                        }

                        if (eyeEnabled)
                        {
                            if (_config.Model.LegacyGaze.Enabled)
                            {
                                var (eyeBaseX, eyeBaseY) = ApplyCircularClamp(rawX, rawY, clampRadius);
                                (eyeX, eyeY) = ApplyCircularClamp(eyeBaseX * eyeScaleX, eyeBaseY * eyeScaleY);
                            }
                            else
                            {
                                UpdateAutonomousGaze(
                                    rawX,
                                    rawY,
                                    cursorMotionMagnitude,
                                    nowTicks,
                                    eyeScaleX,
                                    eyeScaleY,
                                    clampRadius,
                                    out eyeX,
                                    out eyeY,
                                    out var autoHeadX,
                                    out var autoHeadY,
                                    out var autoHeadZ,
                                    out var autoBodyX,
                                    out var autoBodyY);

                                if (headXYEnabled)
                                {
                                    _headX = autoHeadX;
                                    _headY = autoHeadY;
                                }

                                if (headZEnabled)
                                {
                                    _headZ = autoHeadZ;
                                }

                                if (bodyXYEnabled)
                                {
                                    _bodyX = autoBodyX;
                                    _bodyY = autoBodyY;
                                }
                            }
                        }

                        var targetX = Math.Clamp(rawX, -1.0f, 1.0f);
                        var targetY = Math.Clamp(rawY, -1.0f, 1.0f);
                        var (sharedTargetX, sharedTargetY) = ApplyCircularClamp(targetX, targetY, 1.0f);

                        if (headXYEnabled)
                        {
                            headTargetX = sharedTargetX;
                            headTargetY = sharedTargetY;
                        }

                        if (bodyXYEnabled)
                        {
                            bodyTargetX = sharedTargetX;
                            bodyTargetY = sharedTargetY;
                        }
                    }

                    if (bodyZEnabled)
                    {
                        var bodyRawDeltaX = rawDeltaX;
                        var bodyRawDeltaY = rawDeltaY;
                        if (rawPreferred && !rawDeltaX.HasValue)
                        {
                            bodyRawDeltaX = 0;
                            bodyRawDeltaY = 0;
                        }

                        var rawBodyTarget = ComputeBodyTarget(bodyRawDeltaX, bodyRawDeltaY, cursor, nowTicks);
                        float bodyTarget;
                        float bodyAlpha;
                        var bodyHasInput = Math.Abs(rawBodyTarget) > 0.0001f;
                        if (_config.Body.Z.HoldPeak)
                        {
                            bodyTarget = ApplyBodyHold(rawBodyTarget);
                            bodyAlpha = bodyHasInput
                                ? Math.Clamp(_config.Body.Z.Smoothing, 0.0f, 1.0f)
                                : Math.Clamp(_config.Body.Z.ReturnSpeed, 0.0f, 1.0f);
                        }
                        else
                        {
                            bodyTarget = rawBodyTarget;
                            var bodySmoothing = Math.Clamp(_config.Body.Z.Smoothing, 0.0f, 1.0f);
                            var bodyReturn = Math.Clamp(_config.Body.Z.ReturnSpeed, 0.0f, 1.0f);
                            bodyAlpha = bodyHasInput ? bodySmoothing : bodyReturn;
                        }

                        if (bodyAlpha <= 0.0f)
                        {
                            _bodyZ = bodyTarget;
                        }
                        else
                        {
                            _bodyZ = Lerp(_bodyZ, bodyTarget, bodyAlpha);
                        }
                    }

                    if (headZEnabled)
                    {
                        var headRawDeltaX = rawDeltaX;
                        var headRawDeltaY = rawDeltaY;
                        if (rawPreferred && !rawDeltaX.HasValue)
                        {
                            headRawDeltaX = 0;
                            headRawDeltaY = 0;
                        }

                        var rawHeadTarget = ComputeHeadZTarget(headRawDeltaX, headRawDeltaY, cursor, nowTicks);
                        float headTarget;
                        float headAlpha;
                        var headHasInput = Math.Abs(rawHeadTarget) > 0.0001f;
                        if (_config.Head.Z.HoldPeak)
                        {
                            headTarget = ApplyHeadZHold(rawHeadTarget);
                            headAlpha = headHasInput
                                ? Math.Clamp(_config.Head.Z.Smoothing, 0.0f, 1.0f)
                                : Math.Clamp(_config.Head.Z.ReturnSpeed, 0.0f, 1.0f);
                        }
                        else
                        {
                            headTarget = rawHeadTarget;
                            var headSmoothing = Math.Clamp(_config.Head.Z.Smoothing, 0.0f, 1.0f);
                            var headReturn = Math.Clamp(_config.Head.Z.ReturnSpeed, 0.0f, 1.0f);
                            headAlpha = headHasInput ? headSmoothing : headReturn;
                        }

                        if (headAlpha <= 0.0f)
                        {
                            _headZ = headTarget;
                        }
                        else
                        {
                            _headZ = Lerp(_headZ, headTarget, headAlpha);
                        }

                        headZ = _headZ;
                    }

                    if (!headXYEnabled)
                    {
                        _headX = headTargetX;
                        _headY = headTargetY;
                        _lastTargetX = headTargetX;
                        _lastTargetY = headTargetY;
                        _hasHeadOutput = true;
                    }
                    else if (!_hasHeadOutput)
                    {
                        _headX = headTargetX;
                        _headY = headTargetY;
                        _lastTargetX = headTargetX;
                        _lastTargetY = headTargetY;
                        _hasHeadOutput = true;
                    }
                    else
                    {
                        var deltaX = headTargetX - _lastTargetX;
                        var deltaY = headTargetY - _lastTargetY;
                        _lastTargetX = headTargetX;
                        _lastTargetY = headTargetY;

                        var speedMagnitude = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
                        var speedRange = Math.Max(0.0001f, _config.Head.SpeedRange);
                        var speedT = Math.Clamp(speedMagnitude / speedRange, 0.0f, 1.0f);
                        var speedQuad = speedT * speedT;

                        var distanceX = headTargetX - _headX;
                        var distanceY = headTargetY - _headY;
                        var distanceMagnitude = MathF.Sqrt(distanceX * distanceX + distanceY * distanceY);
                        var distanceRange = Math.Max(0.0001f, _config.Head.DistanceRange);
                        var distanceT = Math.Clamp(distanceMagnitude / distanceRange, 0.0f, 1.0f);
                        var distanceQuad = distanceT * distanceT;

                        var response = MathF.Max(speedQuad, distanceQuad);
                        var alpha = Lerp(_config.Head.MinAlpha, _config.Head.MaxAlpha, response);

                        _headX = Lerp(_headX, headTargetX, alpha);
                        _headY = Lerp(_headY, headTargetY, alpha);
                    }

                    if (!bodyXYEnabled)
                    {
                        _bodyX = bodyTargetX;
                        _bodyY = bodyTargetY;
                        _lastBodyTargetX = bodyTargetX;
                        _lastBodyTargetY = bodyTargetY;
                        _hasBodyOutput = true;
                    }
                    else if (!_hasBodyOutput)
                    {
                        _bodyX = bodyTargetX;
                        _bodyY = bodyTargetY;
                        _lastBodyTargetX = bodyTargetX;
                        _lastBodyTargetY = bodyTargetY;
                        _hasBodyOutput = true;
                    }
                    else
                    {
                        var deltaX = bodyTargetX - _lastBodyTargetX;
                        var deltaY = bodyTargetY - _lastBodyTargetY;
                        _lastBodyTargetX = bodyTargetX;
                        _lastBodyTargetY = bodyTargetY;

                        var speedMagnitude = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
                        var speedRange = Math.Max(0.0001f, _config.Body.SpeedRange);
                        var speedT = Math.Clamp(speedMagnitude / speedRange, 0.0f, 1.0f);
                        var speedQuad = speedT * speedT;

                        var distanceX = bodyTargetX - _bodyX;
                        var distanceY = bodyTargetY - _bodyY;
                        var distanceMagnitude = MathF.Sqrt(distanceX * distanceX + distanceY * distanceY);
                        var distanceRange = Math.Max(0.0001f, _config.Body.DistanceRange);
                        var distanceT = Math.Clamp(distanceMagnitude / distanceRange, 0.0f, 1.0f);
                        var distanceQuad = distanceT * distanceT;

                        var response = MathF.Max(speedQuad, distanceQuad);
                        var alpha = Lerp(_config.Body.MinAlpha, _config.Body.MaxAlpha, response);

                        _bodyX = Lerp(_bodyX, bodyTargetX, alpha);
                        _bodyY = Lerp(_bodyY, bodyTargetY, alpha);
                    }
                } // end screen != null
            } // end !_trackingSuspended

            var dizzyActive = UpdateDizzyState(nowTicks);

            var sleepAnim = _config.Animations.Sleep;
            var wakeAnim = _config.Animations.Wake;
            var afkActive = _afkActive && sleepAnim.Enabled;
            if (afkActive)
            {
                // Lerp from live tracking values to the sleep pose over fadeInSeconds
                var sleep = sleepAnim.Sleep;
                var fadeInSeconds = Math.Max(0.001f, sleep.FadeInSeconds);
                var t = (float)((nowTicks - _afkStartTicks) / TickFrequency);
                var fadeT = Math.Clamp(t / fadeInSeconds, 0.0f, 1.0f);
                _sleepFadingIn = fadeT < 1.0f;

                // Asymmetric breathing — exhale ratio controls how long each phase takes
                var breathing = sleepAnim.Breathing;
                var breath = 0.0f;
                if (breathing.Amplitude != 0.0f && breathing.Hz > 0.0f)
                {
                    var exhaleRatio = Math.Clamp(breathing.ExhaleRatio, 0.01f, 0.99f);
                    var inhaleRatio = 1.0f - exhaleRatio;
                    var rawPhase = (t * breathing.Hz) % 1.0f;
                    if (rawPhase < 0.0f) rawPhase += 1.0f;
                    float mappedPhase;
                    if (rawPhase < inhaleRatio)
                        mappedPhase = rawPhase / inhaleRatio * 0.5f;
                    else
                        mappedPhase = 0.5f + (rawPhase - inhaleRatio) / exhaleRatio * 0.5f;
                    breath = MathF.Sin(mappedPhase * MathF.PI * 2.0f) * breathing.Amplitude;
                }

                eyeX = Lerp(_afkEntryEyeX, 0.0f, fadeT);
                eyeY = Lerp(_afkEntryEyeY, 0.0f, fadeT);
                _headX = Lerp(_afkEntryHeadX, sleep.Head.TiltX, fadeT);
                _headY = Lerp(_afkEntryHeadY, sleep.Head.TiltY, fadeT);
                _bodyX = Lerp(_afkEntryBodyX, 0.0f, fadeT);
                _bodyY = Lerp(_afkEntryBodyY, sleep.Body.OffsetY, fadeT);
                _bodyZ = Lerp(_afkEntryBodyZ, sleep.Body.OffsetZ + breath, fadeT);
                headZ = Lerp(_afkEntryHeadZ, sleep.Head.TiltZ, fadeT);
            }
            else if (_wakeupActive)
            {
                if (_wakeJoltActive)
                {
                    var jolt = wakeAnim.WakeJolt;
                    var phaseElapsed = (float)((nowTicks - _wakeJoltPhaseStartTicks) / TickFrequency);

                    switch (_wakeJoltPhase)
                    {
                        case 0: // Jump — rise into the wake-jolt peak
                        {
                            var dur = Math.Max(0.001f, jolt.JumpSeconds);
                            var t = Math.Clamp(phaseElapsed / dur, 0.0f, 1.0f);
                            _headX = Lerp(_wakeJoltEntryHeadX, jolt.HeadTiltX, t);
                            _headY = Lerp(_wakeJoltEntryHeadY, jolt.HeadTiltY, t);
                            headZ = Lerp(_wakeJoltEntryHeadZ, jolt.HeadTiltZ, t);
                            eyeX = Lerp(_wakeJoltEntryEyeX, jolt.EyeX, t);
                            eyeY = Lerp(_wakeJoltEntryEyeY, jolt.EyeY, t);
                            _bodyX = 0.0f;
                            _bodyY = Lerp(_wakeJoltEntryBodyY, jolt.JumpBodyY, t);
                            _bodyZ = 0.0f;
                            if (phaseElapsed >= dur)
                            {
                                _wakeJoltPhase = 1;
                                _wakeJoltPhaseStartTicks = nowTicks;
                            }
                            break;
                        }
                        case 1: // Hold — stay at the wake-jolt peak
                        {
                            var dur = Math.Max(0.001f, jolt.HoldSeconds);
                            _headX = jolt.HeadTiltX;
                            _headY = jolt.HeadTiltY;
                            headZ = jolt.HeadTiltZ;
                            eyeX = jolt.EyeX;
                            eyeY = jolt.EyeY;
                            _bodyX = 0.0f;
                            _bodyY = jolt.JumpBodyY;
                            _bodyZ = 0.0f;
                            if (phaseElapsed >= dur)
                            {
                                WakeJoltReturn?.Invoke();
                                _wakeJoltPhase = 2;
                                _wakeJoltPhaseStartTicks = nowTicks;
                            }
                            break;
                        }
                        case 2: // Compose — lerp from jolt peak to composed pose
                        {
                            var dur = Math.Max(0.001f, jolt.ComposeSeconds);
                            var t = Math.Clamp(phaseElapsed / dur, 0.0f, 1.0f);
                            _headX = Lerp(jolt.HeadTiltX, jolt.ComposedHeadX, t);
                            _headY = Lerp(jolt.HeadTiltY, jolt.ComposedHeadY, t);
                            headZ = Lerp(jolt.HeadTiltZ, jolt.ComposedHeadZ, t);
                            eyeX = Lerp(jolt.EyeX, 0.0f, t);
                            eyeY = Lerp(jolt.EyeY, 0.0f, t);
                            _bodyX = 0.0f;
                            _bodyY = Lerp(jolt.JumpBodyY, 0.0f, t);
                            _bodyZ = 0.0f;
                            if (phaseElapsed >= dur)
                            {
                                _wakeJoltPhase = 3;
                                _wakeJoltPhaseStartTicks = nowTicks;
                            }
                            break;
                        }
                        case 3: // Breathe — hold composed pose with asymmetric breathing
                        {
                            var breathing = sleepAnim.Breathing;
                            var breath = 0.0f;
                            if (breathing.Amplitude != 0.0f && breathing.Hz > 0.0f)
                            {
                                var exhaleRatio = Math.Clamp(breathing.ExhaleRatio, 0.01f, 0.99f);
                                var inhaleRatio = 1.0f - exhaleRatio;
                                var rawPhase = (phaseElapsed * breathing.Hz) % 1.0f;
                                if (rawPhase < 0.0f) rawPhase += 1.0f;
                                float mappedPhase;
                                if (rawPhase < inhaleRatio)
                                    mappedPhase = rawPhase / inhaleRatio * 0.5f;
                                else
                                    mappedPhase = 0.5f + (rawPhase - inhaleRatio) / exhaleRatio * 0.5f;
                                breath = MathF.Sin(mappedPhase * MathF.PI * 2.0f) * breathing.Amplitude;
                            }
                            _headX = jolt.ComposedHeadX;
                            _headY = jolt.ComposedHeadY;
                            headZ = jolt.ComposedHeadZ;
                            eyeX = 0.0f;
                            eyeY = 0.0f;
                            _bodyX = 0.0f;
                            _bodyY = 0.0f;
                            _bodyZ = breath;
                            if (phaseElapsed >= Math.Max(0.001f, jolt.BreatheSeconds))
                            {
                                var easeOutSecs = Math.Max(0.0f, wakeAnim.EaseOutSeconds);
                                if (easeOutSecs > 0.0f)
                                {
                                    _wakeEaseOutActive = true;
                                    _wakeEaseOutStartTicks = nowTicks;
                                    _wakeEaseOutEndTicks = nowTicks + (long)(easeOutSecs * TickFrequency);
                                    _wakeEaseOutHeadX = jolt.ComposedHeadX;
                                    _wakeEaseOutHeadY = jolt.ComposedHeadY;
                                    _wakeEaseOutHeadZ = jolt.ComposedHeadZ;
                                    _wakeEaseOutBodyX = 0.0f;
                                    _wakeEaseOutBodyY = 0.0f;
                                    _wakeEaseOutBodyZ = 0.0f;
                                    _wakeEaseOutEyeX = 0.0f;
                                    _wakeEaseOutEyeY = 0.0f;
                                    _wakeEaseOutBlinkL = -Math.Clamp(jolt.ComposedBlink, 0.0f, 1.0f);
                                    _wakeEaseOutBlinkR = _wakeEaseOutBlinkL;
                                    _wakeEaseOutSmileL = 0.0f;
                                    _wakeEaseOutSmileR = 0.0f;
                                }
                                _wakeJoltActive = false;
                                _wakeupActive = false;
                            }
                            break;
                        }
                    }
                }
            }
            else if (!ApplyDizzyEffect(
                nowTicks,
                dizzyActive,
                eyeEnabled,
                headXYEnabled,
                bodyXYEnabled,
                headZEnabled,
                bodyZEnabled,
                ref eyeX,
                ref eyeY,
                ref _headX,
                ref _headY,
                ref _bodyX,
                ref _bodyY,
                ref headZ,
                ref _bodyZ))
            {
                if (_config.Model.LegacyGaze.Enabled)
                {
                    ApplyJitter(nowTicks, eyeEnabled, headXYEnabled, bodyXYEnabled, ref eyeX, ref eyeY, ref _headX, ref _headY, ref _bodyX, ref _bodyY);
                }
            }

            // Blend from the saved wake pose back toward live tracking over easeOutSeconds
            if (_wakeEaseOutActive && !afkActive && !_wakeupActive && !dizzyActive)
            {
                if (nowTicks >= _wakeEaseOutEndTicks)
                {
                    _wakeEaseOutActive = false;
                }
                else
                {
                    var elapsed = (float)((nowTicks - _wakeEaseOutStartTicks) / TickFrequency);
                    var easeOutSecs = Math.Max(0.001f, wakeAnim.EaseOutSeconds);
                    var easeT = Math.Clamp(elapsed / easeOutSecs, 0.0f, 1.0f);
                    eyeX = Lerp(_wakeEaseOutEyeX, eyeX, easeT);
                    eyeY = Lerp(_wakeEaseOutEyeY, eyeY, easeT);
                    _headX = Lerp(_wakeEaseOutHeadX, _headX, easeT);
                    _headY = Lerp(_wakeEaseOutHeadY, _headY, easeT);
                    _bodyX = Lerp(_wakeEaseOutBodyX, _bodyX, easeT);
                    _bodyY = Lerp(_wakeEaseOutBodyY, _bodyY, easeT);
                    _bodyZ = Lerp(_wakeEaseOutBodyZ, _bodyZ, easeT);
                    headZ = Lerp(_wakeEaseOutHeadZ, headZ, easeT);
                }
            }

            if (!dizzyActive && !_wakeupActive && !afkActive && bodyXYEnabled)
            {
                var bodyBreath = _config.Model.BodyBreath;
                if (bodyBreath.Enabled && (bodyBreath.AmpX != 0.0f || bodyBreath.AmpY != 0.0f))
                {
                    var hz = Math.Max(0.0f, bodyBreath.Hz);
                    if (hz > 0.0f)
                    {
                        var t = (float)(nowTicks / TickFrequency);
                        var angle = t * MathF.PI * 2.0f * hz;
                        var breathX = MathF.Sin(angle) * bodyBreath.AmpX;
                        var breathY = MathF.Cos(angle) * bodyBreath.AmpY;
                        _bodyX = Math.Clamp(_bodyX + breathX, -1.0f, 1.0f);
                        _bodyY = Math.Clamp(_bodyY + breathY, -1.0f, 1.0f);
                    }
                }
            }

            // Capture last-frame output values for next-tick entry lerps
            _lastOutputEyeX = eyeX;
            _lastOutputEyeY = eyeY;
            _lastOutputHeadZ = headZ;
            _lastOutputBodyZ = _bodyZ;

            var motionX = _lastMotionDeltaX;
            var motionY = _lastMotionDeltaY;
            var motionMagnitude = MathF.Sqrt((motionX * motionX) + (motionY * motionY));
            var attentionScale = ComputeAttentionScale(motionMagnitude);
            var allowFace = !afkActive && !_wakeupActive && !dizzyActive;
            UpdateFaceExpressions(
                nowTicks,
                motionMagnitude,
                attentionScale,
                allowFace,
                out var blinkLValue,
                out var blinkRValue,
                out var smileLValue,
                out var smileRValue);

            if (afkActive)
            {
                if (_config.Face.Blink.Enabled)
                {
                    var fadeInSecs = Math.Max(0.001f, sleepAnim.Sleep.FadeInSeconds);
                    var elapsed = Math.Max(0.0f, (float)((nowTicks - _afkStartTicks) / TickFrequency));
                    var keyframeCount = BlinkFadeStrip.Length;
                    var sliceCount = keyframeCount - 1;
                    var sliceDuration = fadeInSecs / sliceCount;
                    var sliceIdx = Math.Clamp((int)(elapsed / sliceDuration), 0, sliceCount - 1);
                    var elapsedInSlice = elapsed - (sliceIdx * sliceDuration);
                    var t = Math.Clamp(elapsedInSlice / sliceDuration, 0.0f, 1.0f);
                    var blinkValue = Lerp(BlinkFadeStrip[sliceIdx] - 1.0f, BlinkFadeStrip[sliceIdx + 1] - 1.0f, t);
                    blinkLValue = blinkValue;
                    blinkRValue = blinkValue;
                }
                else
                {
                    blinkLValue = -1.0f;
                    blinkRValue = -1.0f;
                }
                smileLValue = Math.Clamp(sleepAnim.Sleep.Eye.Smile, -1.0f, 1.0f);
                smileRValue = smileLValue;
            }
            else if (_wakeupActive && _wakeJoltActive && _config.Face.Blink.Enabled)
            {
                var jolt = wakeAnim.WakeJolt;
                switch (_wakeJoltPhase)
                {
                    case 0:
                    case 1:
                        blinkLValue = 0.0f;
                        blinkRValue = 0.0f;
                        break;
                    case 2:
                    {
                        var phaseElapsed = (float)((nowTicks - _wakeJoltPhaseStartTicks) / TickFrequency);
                        var t = Math.Clamp(phaseElapsed / Math.Max(0.001f, jolt.ComposeSeconds), 0.0f, 1.0f);
                        var target = -Math.Clamp(jolt.ComposedBlink, 0.0f, 1.0f);
                        blinkLValue = Lerp(0.0f, target, t);
                        blinkRValue = blinkLValue;
                        break;
                    }
                    case 3:
                        blinkLValue = -Math.Clamp(jolt.ComposedBlink, 0.0f, 1.0f);
                        blinkRValue = blinkLValue;
                        break;
                }
                smileLValue = 0.0f;
                smileRValue = 0.0f;
            }
            else if (_wakeEaseOutActive && !afkActive && !dizzyActive)
            {
                var elapsed = (float)((nowTicks - _wakeEaseOutStartTicks) / TickFrequency);
                var easeOutSecs = Math.Max(0.001f, wakeAnim.EaseOutSeconds);
                var easeT = Math.Clamp(elapsed / easeOutSecs, 0.0f, 1.0f);
                blinkLValue = Lerp(_wakeEaseOutBlinkL, blinkLValue, easeT);
                blinkRValue = Lerp(_wakeEaseOutBlinkR, blinkRValue, easeT);
                smileLValue = Lerp(_wakeEaseOutSmileL, smileLValue, easeT);
                smileRValue = Lerp(_wakeEaseOutSmileR, smileRValue, easeT);
            }

            _paramBuffer.Clear();
            if (eyeEnabled)
            {
                UpdateParam(_eyeXParam, _config.Eye.ParamX, eyeX, _config.Eye.Weight);
                UpdateParam(_eyeYParam, _config.Eye.ParamY, eyeY, _config.Eye.Weight);
                AddIfValid(_paramBuffer, _eyeXParam);
                AddIfValid(_paramBuffer, _eyeYParam);
            }
            if (headXYEnabled)
            {
                UpdateParam(_headXParam, _config.Head.ParamX, _headX, _config.Head.Weight);
                UpdateParam(_headYParam, _config.Head.ParamY, _headY, _config.Head.Weight);
                AddIfValid(_paramBuffer, _headXParam);
                AddIfValid(_paramBuffer, _headYParam);
            }
            if (headZEnabled)
            {
                var headZOutput = _config.Head.Z.InvertZ ? -headZ : headZ;
                UpdateParam(_headZParam, _config.Head.ParamZ, headZOutput, _config.Head.WeightZ);
                AddIfValid(_paramBuffer, _headZParam);
            }
            if (bodyXYEnabled)
            {
                UpdateParam(_bodyXParam, _config.Body.ParamX, _bodyX, _config.Body.Weight);
                UpdateParam(_bodyYParam, _config.Body.ParamY, _bodyY, _config.Body.Weight);
                AddIfValid(_paramBuffer, _bodyXParam);
                AddIfValid(_paramBuffer, _bodyYParam);
            }
            if (bodyZEnabled)
            {
                var bodyOutput = _config.Body.Z.InvertZ ? -_bodyZ : _bodyZ;
                UpdateParam(_bodyZParam, _config.Body.ParamZ, bodyOutput, _config.Body.WeightZ);
                AddIfValid(_paramBuffer, _bodyZParam);
            }
            if (_config.Face.Blink.Enabled && _config.Face.Blink.Weight > 0.0f)
            {
                UpdateParam(_blinkLParam, _config.Face.Blink.ParamLeft, blinkLValue, _config.Face.Blink.Weight);
                UpdateParam(_blinkRParam, _config.Face.Blink.ParamRight, blinkRValue, _config.Face.Blink.Weight);
                AddIfValid(_paramBuffer, _blinkLParam);
                AddIfValid(_paramBuffer, _blinkRParam);
            }
            if (_config.Face.Smile.Enabled && _config.Face.Smile.Weight > 0.0f)
            {
                UpdateParam(_smileLParam, _config.Face.Smile.ParamLeft, smileLValue, _config.Face.Smile.Weight);
                UpdateParam(_smileRParam, _config.Face.Smile.ParamRight, smileRValue, _config.Face.Smile.Weight);
                AddIfValid(_paramBuffer, _smileLParam);
                AddIfValid(_paramBuffer, _smileRParam);
            }

            _lastComputeTicks = nowTicks;

            if (!_faceFound)
            {
                if (!_suspendZeroSent)
                {
                    foreach (var p in _paramBuffer)
                    {
                        p.Value = 0f;
                        p.Weight = 1f;
                    }
                    _ = _vtsClient.SendParamsAsync(_paramBuffer, false, CancellationToken.None);
                    _suspendZeroSent = true;
                }
                else if (nowTicks - _lastSendTicks >= (long)(TickFrequency / Math.Max(0.1, _config.Vts.Smart.KeepAliveHz)))
                {
                    _ = _vtsClient.SendParamsAsync(Array.Empty<Models.InjectedParameterValue>(), false, CancellationToken.None);
                    _lastSendTicks = nowTicks;
                }
                return;
            }

            if (shouldSkip)
            {
                return;
            }

            _lastSendTicks = nowTicks;
            _ = _vtsClient.SendParamsAsync(_paramBuffer, true, CancellationToken.None);
        }

        private static void UpdateParam(Models.InjectedParameterValue param, string id, float value, float weight)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                param.Id = string.Empty;
                return;
            }

            param.Id = id;
            param.Value = value;
            param.Weight = weight;
        }

        private static void AddIfValid(List<Models.InjectedParameterValue> values, Models.InjectedParameterValue param)
        {
            if (string.IsNullOrWhiteSpace(param.Id))
            {
                return;
            }

            values.Add(param);
        }

        private static float Lerp(float start, float end, float amount)
        {
            var clamped = Math.Clamp(amount, 0.0f, 1.0f);
            return start + (end - start) * clamped;
        }

        private static (float X, float Y) ApplyCircularClamp(float x, float y, float radius = 1.0f)
        {
            var clampedRadius = Math.Max(0.0001f, radius);
            var magnitudeSquared = x * x + y * y;
            var radiusSquared = clampedRadius * clampedRadius;
            if (magnitudeSquared <= radiusSquared)
            {
                return (x, y);
            }

            var scale = clampedRadius / MathF.Sqrt(magnitudeSquared);
            return (x * scale, y * scale);
        }

        private static float SoftClampRaw(float value, float range)
        {
            if (range <= 0.0f)
            {
                return value;
            }

            var normalized = value / range;
            if (SoftClampNorm <= 0.0f)
            {
                return Math.Clamp(normalized, -1.0f, 1.0f);
            }

            var softened = MathF.Tanh(normalized) / SoftClampNorm;
            return Math.Clamp(softened, -1.0f, 1.0f);
        }

        private void LoadGazeBias()
        {
            var history = _config.Model.AutonomousGaze.History;
            var gridSize = Math.Max(1, history.GridSize);
            var shouldSaveFresh = false;
            var path = ResolveBiasPath(history.PersistPath);
            _gazeGridSize = gridSize;

            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    var state = JsonSerializer.Deserialize<GazeBiasState>(json, GazeBiasReadOptions);
                    if (state != null
                        && state.GridSize == gridSize
                        && TryCopyBiasGrid(state.MouseDwell, gridSize, out _mouseDwellGrid)
                        && TryCopyBiasGrid(state.GazeVisit, gridSize, out _gazeVisitGrid))
                    {
                        RecomputeBiasCentroid();
                        _lastBiasPersistTicks = Stopwatch.GetTimestamp();
                        return;
                    }
                }
                catch
                {
                }

                shouldSaveFresh = true;
            }
            else
            {
                shouldSaveFresh = true;
            }

            _mouseDwellGrid = new float[gridSize, gridSize];
            _gazeVisitGrid = new float[gridSize, gridSize];
            RecomputeBiasCentroid();
            _lastBiasPersistTicks = Stopwatch.GetTimestamp();
            if (shouldSaveFresh)
            {
                SaveGazeBias(force: true);
            }
        }

        private void SaveGazeBias(bool force = false)
        {
            if (_gazeGridSize <= 0 || _mouseDwellGrid.Length == 0 || _gazeVisitGrid.Length == 0)
            {
                return;
            }

            var history = _config.Model.AutonomousGaze.History;
            if (!force && history.PersistIntervalSeconds <= 0.0f)
            {
                return;
            }

            try
            {
                var path = ResolveBiasPath(history.PersistPath);
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var state = new GazeBiasState
                {
                    GridSize = _gazeGridSize,
                    MouseDwell = ToJaggedArray(_mouseDwellGrid),
                    GazeVisit = ToJaggedArray(_gazeVisitGrid)
                };

                var json = JsonSerializer.Serialize(state, GazeBiasWriteOptions);
                File.WriteAllText(path, json);
                _lastBiasPersistTicks = Stopwatch.GetTimestamp();
            }
            catch
            {
            }
        }

        private void UpdateGazeHistory(long nowTicks)
        {
            var history = _config.Model.AutonomousGaze.History;
            EnsureGazeHistoryStorage(Math.Max(1, history.GridSize));

            if (!_smartIdleActive && history.Enabled)
            {
                var (mouseCol, mouseRow) = NormalizedToCell(_lastTargetX, _lastTargetY, _gazeGridSize);
                _mouseDwellGrid[mouseRow, mouseCol] += 1.0f;
                ApplyGridDecay(_mouseDwellGrid, history.MouseDwellDecayRate);
            }

            if (!_afkActive && history.Enabled && _config.Model.AutonomousGaze.Enabled && !_useDeltaMode)
            {
                var (gazeCol, gazeRow) = NormalizedToCell(_autoSaccadeTargetX, _autoSaccadeTargetY, _gazeGridSize);
                _gazeVisitGrid[gazeRow, gazeCol] += 1.0f;
                ApplyGridDecay(_gazeVisitGrid, history.GazeVisitDecayRate);
            }

            RecomputeBiasCentroid();

            var persistSeconds = Math.Max(0.0f, history.PersistIntervalSeconds);
            if (persistSeconds <= 0.0f)
            {
                return;
            }

            var persistTicks = (long)(persistSeconds * TickFrequency);
            if (persistTicks <= 0 || _lastBiasPersistTicks == 0 || nowTicks - _lastBiasPersistTicks >= persistTicks)
            {
                SaveGazeBias();
            }
        }

        private void UpdateAutonomousGaze(
            float cursorNormX,
            float cursorNormY,
            float motionMagnitude,
            long nowTicks,
            float eyeScaleX,
            float eyeScaleY,
            float clampRadius,
            out float eyeX,
            out float eyeY,
            out float headX,
            out float headY,
            out float headZ,
            out float bodyX,
            out float bodyY)
        {
            if (_lastAutoGazeUpdateTicks == 0)
            {
                ResetAutonomousGaze(cursorNormX, cursorNormY);
            }

            var dt = 0.0f;
            if (_lastAutoGazeUpdateTicks > 0)
            {
                dt = (float)((nowTicks - _lastAutoGazeUpdateTicks) / TickFrequency);
            }
            _lastAutoGazeUpdateTicks = nowTicks;

            if (dt <= 0.0f)
            {
                dt = 0.0f;
            }
            else if (dt > 0.1f)
            {
                dt = 0.1f;
            }

            var gaze = _config.Model.AutonomousGaze;
            var normalizedMotion = Math.Clamp(motionMagnitude / Math.Max(0.01f, gaze.MotionReferenceRange), 0.0f, 1.0f);

            if (normalizedMotion >= gaze.FastSnapThreshold)
            {
                _autoInterestX = Lerp(_autoInterestX, cursorNormX, gaze.Eye.Attention.SnapAlpha);
                _autoInterestY = Lerp(_autoInterestY, cursorNormY, gaze.Eye.Attention.SnapAlpha);
                _autoSaccadeTargetX = Lerp(_autoSaccadeTargetX, cursorNormX, gaze.Eye.Attention.SnapAlpha);
                _autoSaccadeTargetY = Lerp(_autoSaccadeTargetY, cursorNormY, gaze.Eye.Attention.SnapAlpha);
                _autoGazeVelX = 0.0f;
                _autoGazeVelY = 0.0f;

                _autoHeadInterestX = Lerp(_autoHeadInterestX, cursorNormX * gaze.Head.Wander.Scale, gaze.Head.Attention.SnapAlpha);
                _autoHeadInterestY = Lerp(_autoHeadInterestY, cursorNormY * gaze.Head.Wander.Scale, gaze.Head.Attention.SnapAlpha);
                _autoBodyInterestX = Lerp(_autoBodyInterestX, cursorNormX * gaze.Body.Wander.Scale, gaze.Body.Attention.SnapAlpha);
                _autoBodyInterestY = Lerp(_autoBodyInterestY, cursorNormY * gaze.Body.Wander.Scale, gaze.Body.Attention.SnapAlpha);
            }
            else
            {
                var attractionAlpha = Math.Clamp(normalizedMotion * Math.Max(0.0f, gaze.CursorAttractionStrength) * dt, 0.0f, 1.0f);

                var eyeAlpha = Math.Clamp(attractionAlpha * gaze.Eye.Attention.AttractionAlpha, 0.0f, 1.0f);
                _autoInterestX = Lerp(_autoInterestX, cursorNormX, eyeAlpha);
                _autoInterestY = Lerp(_autoInterestY, cursorNormY, eyeAlpha);

                var headAlpha = Math.Clamp(attractionAlpha * gaze.Head.Attention.AttractionAlpha, 0.0f, 1.0f);
                _autoHeadInterestX = Lerp(_autoHeadInterestX, cursorNormX * gaze.Head.Wander.Scale, headAlpha);
                _autoHeadInterestY = Lerp(_autoHeadInterestY, cursorNormY * gaze.Head.Wander.Scale, headAlpha);

                var bodyAlpha = Math.Clamp(attractionAlpha * gaze.Body.Attention.AttractionAlpha, 0.0f, 1.0f);
                _autoBodyInterestX = Lerp(_autoBodyInterestX, cursorNormX * gaze.Body.Wander.Scale, bodyAlpha);
                _autoBodyInterestY = Lerp(_autoBodyInterestY, cursorNormY * gaze.Body.Wander.Scale, bodyAlpha);
            }

            var movementThreshold = Math.Max(0.0, _config.Vts.Smart.MovementThresholdPx);
            if (motionMagnitude < movementThreshold)
            {
                var decayX = _hasBiasCentroid ? _biasCentroidX : 0.0f;
                var decayY = _hasBiasCentroid ? _biasCentroidY : 0.0f;
                var decaySpeed = Math.Clamp(gaze.InterestDecaySpeed, 0.0f, 1.0f);
                _autoInterestX = Lerp(_autoInterestX, decayX, decaySpeed);
                _autoInterestY = Lerp(_autoInterestY, decayY, decaySpeed);
            }

            _autoInterestX = Math.Clamp(_autoInterestX, -1.0f, 1.0f);
            _autoInterestY = Math.Clamp(_autoInterestY, -1.0f, 1.0f);

            var eyeFullAttention = gaze.Eye.Attention.SnapAlpha >= 1.0f
                && gaze.Eye.Attention.AttractionAlpha >= 1.0f;

            if (!eyeFullAttention && !_afkActive && !IsSaccadeBlinkSuppressed(nowTicks))
            {
                if (_autoSaccadeIntervalTicks <= 0)
                {
                    _autoSaccadeStartTicks = nowTicks;
                    _autoSaccadeIntervalTicks = NextSaccadeIntervalTicks(gaze.Eye.Wander.MinHz, gaze.Eye.Wander.MaxHz, gaze.Eye.Wander.Bias);
                }

                if (nowTicks - _autoSaccadeStartTicks >= _autoSaccadeIntervalTicks)
                {
                    _autoSaccadeStartTicks = nowTicks;
                    _autoSaccadeIntervalTicks = NextSaccadeIntervalTicks(gaze.Eye.Wander.MinHz, gaze.Eye.Wander.MaxHz, gaze.Eye.Wander.Bias);

                    var (baseX, baseY) = PickSaccadeTarget(
                        _autoInterestX,
                        _autoInterestY,
                        gaze.InterestRadiusX,
                        gaze.InterestRadiusY);
                    _autoSaccadeTargetX = Math.Clamp(
                        baseX,
                        _autoInterestX - gaze.InterestRadiusX,
                        _autoInterestX + gaze.InterestRadiusX);
                    _autoSaccadeTargetY = Math.Clamp(
                        baseY,
                        _autoInterestY - gaze.InterestRadiusY,
                        _autoInterestY + gaze.InterestRadiusY);
                    _autoSaccadeTargetX = Math.Clamp(_autoSaccadeTargetX, -1.0f, 1.0f);
                    _autoSaccadeTargetY = Math.Clamp(_autoSaccadeTargetY, -1.0f, 1.0f);
                }
            }
            else if (eyeFullAttention)
            {
                _autoSaccadeTargetX = cursorNormX;
                _autoSaccadeTargetY = cursorNormY;
            }

            var eyeCfg = gaze.Eye;
            if (eyeFullAttention)
            {
                _autoGazeX = _autoSaccadeTargetX;
                _autoGazeY = _autoSaccadeTargetY;
                _autoGazeVelX = 0.0f;
                _autoGazeVelY = 0.0f;
            }
            else
            {
                var eyeAttentionLevel = Math.Clamp((gaze.Eye.Attention.SnapAlpha + gaze.Eye.Attention.AttractionAlpha) * 0.5f, 0.0f, 1.0f);
                var dynamicEyeSpringStrength = Lerp(eyeCfg.Wander.SpringStrength, eyeCfg.Wander.SpringStrength * 6.0f, eyeAttentionLevel);
                var dynamicEyeSpringDamping = Lerp(eyeCfg.Wander.SpringDamping, eyeCfg.Wander.SpringDamping * 2.5f, eyeAttentionLevel);
                UpdateSpring(ref _autoGazeX, ref _autoGazeVelX, _autoSaccadeTargetX, Math.Max(0.0f, dynamicEyeSpringStrength), Math.Max(0.0f, dynamicEyeSpringDamping), dt);
                UpdateSpring(ref _autoGazeY, ref _autoGazeVelY, _autoSaccadeTargetY, Math.Max(0.0f, dynamicEyeSpringStrength), Math.Max(0.0f, dynamicEyeSpringDamping), dt);
            }

            var (clampedX, clampedY) = ApplyCircularClamp(_autoGazeX, _autoGazeY, clampRadius);
            eyeX = Math.Clamp(clampedX * eyeScaleX, -1.0f, 1.0f);
            eyeY = Math.Clamp(clampedY * eyeScaleY, -1.0f, 1.0f);

            var headCfg = gaze.Head;
            if (_autoHeadIntervalTicks <= 0)
            {
                _autoHeadIntervalStartTicks = nowTicks;
                _autoHeadIntervalTicks = NextSaccadeIntervalTicks(headCfg.Wander.MinHz, headCfg.Wander.MaxHz, headCfg.Wander.Bias);
            }

            var mouseStill = motionMagnitude < (float)_config.Vts.Smart.MovementThresholdPx;

            if (headCfg.Wander.MinHz > 0.0f && nowTicks - _autoHeadIntervalStartTicks >= _autoHeadIntervalTicks)
            {
                _autoHeadIntervalStartTicks = nowTicks;
                _autoHeadIntervalTicks = NextSaccadeIntervalTicks(headCfg.Wander.MinHz, headCfg.Wander.MaxHz, headCfg.Wander.Bias);
                float headWanderTargetX, headWanderTargetY;
                if (mouseStill)
                {
                    var angle = (float)(Random.Shared.NextDouble() * Math.PI * 2.0);
                    headWanderTargetX = MathF.Cos(angle) * gaze.InterestRadiusX * headCfg.Wander.Scale;
                    headWanderTargetY = MathF.Sin(angle) * gaze.InterestRadiusY * headCfg.Wander.Scale;
                }
                else
                {
                    headWanderTargetX = eyeFullAttention ? _autoInterestX : _autoGazeX;
                    headWanderTargetY = eyeFullAttention ? _autoInterestY : _autoGazeY;
                    headWanderTargetX *= headCfg.Wander.Scale;
                    headWanderTargetY *= headCfg.Wander.Scale;
                }
                _autoHeadInterestX = Lerp(_autoHeadInterestX, headWanderTargetX, 0.3f);
                _autoHeadInterestY = Lerp(_autoHeadInterestY, headWanderTargetY, 0.3f);
            }

            var headDx = _autoHeadInterestX - _autoHeadX;
            var headDy = _autoHeadInterestY - _autoHeadY;
            var headDist = MathF.Sqrt(headDx * headDx + headDy * headDy);
            float headTargetX, headTargetY;
            if (headDist > headCfg.Wander.Deadzone)
            {
                headTargetX = _autoHeadInterestX;
                headTargetY = _autoHeadInterestY;
            }
            else
            {
                headTargetX = _autoHeadX;
                headTargetY = _autoHeadY;
            }

            var headAttentionLevel = Math.Clamp((gaze.Head.Attention.SnapAlpha + gaze.Head.Attention.AttractionAlpha) * 0.5f, 0.0f, 1.0f);
            var dynamicHeadSpringStrength = Lerp(headCfg.Wander.SpringStrength, headCfg.Wander.SpringStrength * 6.0f, headAttentionLevel);
            var dynamicHeadSpringDamping = Lerp(headCfg.Wander.SpringDamping, headCfg.Wander.SpringDamping * 2.5f, headAttentionLevel);
            UpdateSpring(ref _autoHeadX, ref _autoHeadVelX, headTargetX, dynamicHeadSpringStrength, dynamicHeadSpringDamping, dt);
            UpdateSpring(ref _autoHeadY, ref _autoHeadVelY, headTargetY, dynamicHeadSpringStrength, dynamicHeadSpringDamping, dt);

            var headFullAttention = gaze.Head.Attention.SnapAlpha >= 1.0f
                && gaze.Head.Attention.AttractionAlpha >= 1.0f;

            if (headFullAttention)
            {
                _autoHeadX = _autoHeadInterestX;
                _autoHeadY = _autoHeadInterestY;
                _autoHeadVelX = 0.0f;
                _autoHeadVelY = 0.0f;
            }

            headX = Math.Clamp(_autoHeadX, -1.0f, 1.0f);
            headY = Math.Clamp(_autoHeadY, -1.0f, 1.0f);

            var headZCfg = gaze.Head.HeadZ;
            var headZTarget = (-_autoGazeX * headZCfg.Scale) + (-_autoGazeY * headZCfg.DyScale);
            headZTarget = Math.Clamp(headZTarget, -0.2f, 0.2f);
            UpdateSpring(
                ref _autoHeadZ,
                ref _autoHeadZVel,
                headZTarget,
                Math.Max(0.0f, headZCfg.SpringStrength),
                Math.Max(0.0f, headZCfg.SpringDamping),
                dt);
            headZ = Math.Clamp(_autoHeadZ, -0.2f, 0.2f);

            var bodyCfg = gaze.Body;
            if (_autoBodyIntervalTicks <= 0)
            {
                _autoBodyIntervalStartTicks = nowTicks;
                _autoBodyIntervalTicks = NextSaccadeIntervalTicks(bodyCfg.Wander.MinHz, bodyCfg.Wander.MaxHz, bodyCfg.Wander.Bias);
            }

            if (bodyCfg.Wander.MinHz > 0.0f && nowTicks - _autoBodyIntervalStartTicks >= _autoBodyIntervalTicks)
            {
                _autoBodyIntervalStartTicks = nowTicks;
                _autoBodyIntervalTicks = NextSaccadeIntervalTicks(bodyCfg.Wander.MinHz, bodyCfg.Wander.MaxHz, bodyCfg.Wander.Bias);
                float bodyWanderTargetX, bodyWanderTargetY;
                if (mouseStill)
                {
                    var angle = (float)(Random.Shared.NextDouble() * Math.PI * 2.0);
                    bodyWanderTargetX = MathF.Cos(angle) * gaze.InterestRadiusX * bodyCfg.Wander.Scale;
                    bodyWanderTargetY = MathF.Sin(angle) * gaze.InterestRadiusY * bodyCfg.Wander.Scale;
                }
                else
                {
                    bodyWanderTargetX = _autoHeadX * bodyCfg.Wander.Scale;
                    bodyWanderTargetY = _autoHeadY * bodyCfg.Wander.Scale;
                }
                _autoBodyInterestX = Lerp(_autoBodyInterestX, bodyWanderTargetX, 0.15f);
                _autoBodyInterestY = Lerp(_autoBodyInterestY, bodyWanderTargetY, 0.15f);
            }

            var bodyDx = _autoBodyInterestX - _autoBodyX;
            var bodyDy = _autoBodyInterestY - _autoBodyY;
            var bodyDist = MathF.Sqrt(bodyDx * bodyDx + bodyDy * bodyDy);
            float bodyTargetX, bodyTargetY;
            if (bodyDist > bodyCfg.Wander.Deadzone)
            {
                bodyTargetX = _autoBodyInterestX;
                bodyTargetY = _autoBodyInterestY;
            }
            else
            {
                bodyTargetX = _autoBodyX;
                bodyTargetY = _autoBodyY;
            }

            var bodyAttentionLevel = Math.Clamp((gaze.Body.Attention.SnapAlpha + gaze.Body.Attention.AttractionAlpha) * 0.5f, 0.0f, 1.0f);
            var dynamicBodySpringStrength = Lerp(bodyCfg.Wander.SpringStrength, bodyCfg.Wander.SpringStrength * 6.0f, bodyAttentionLevel);
            var dynamicBodySpringDamping = Lerp(bodyCfg.Wander.SpringDamping, bodyCfg.Wander.SpringDamping * 2.5f, bodyAttentionLevel);
            UpdateSpring(ref _autoBodyX, ref _autoBodyVelX, bodyTargetX, dynamicBodySpringStrength, dynamicBodySpringDamping, dt);
            UpdateSpring(ref _autoBodyY, ref _autoBodyVelY, bodyTargetY, dynamicBodySpringStrength, dynamicBodySpringDamping, dt);

            var bodyFullAttention = gaze.Body.Attention.SnapAlpha >= 1.0f
                && gaze.Body.Attention.AttractionAlpha >= 1.0f;

            if (bodyFullAttention)
            {
                _autoBodyX = _autoBodyInterestX;
                _autoBodyY = _autoBodyInterestY;
                _autoBodyVelX = 0.0f;
                _autoBodyVelY = 0.0f;
            }

            bodyX = Math.Clamp(_autoBodyX, -1.0f, 1.0f);
            bodyY = Math.Clamp(_autoBodyY, -1.0f, 1.0f);
        }

        private void ResetAutonomousGaze(float cursorNormX, float cursorNormY)
        {
            _autoInterestX = cursorNormX;
            _autoInterestY = cursorNormY;
            _autoGazeX = cursorNormX;
            _autoGazeY = cursorNormY;
            _autoGazeVelX = 0.0f;
            _autoGazeVelY = 0.0f;
            _autoSaccadeTargetX = cursorNormX;
            _autoSaccadeTargetY = cursorNormY;
            _autoSaccadeStartTicks = Stopwatch.GetTimestamp();
            _autoSaccadeIntervalTicks = 0;
            _lastAutoGazeUpdateTicks = 0;
            _autoHeadX = cursorNormX;
            _autoHeadY = cursorNormY;
            _autoHeadVelX = 0.0f;
            _autoHeadVelY = 0.0f;
            _autoHeadZ = 0.0f;
            _autoHeadZVel = 0.0f;
            _autoHeadInterestX = cursorNormX;
            _autoHeadInterestY = cursorNormY;
            _autoHeadIntervalStartTicks = 0;
            _autoHeadIntervalTicks = 0;
            _autoBodyX = cursorNormX;
            _autoBodyY = cursorNormY;
            _autoBodyVelX = 0.0f;
            _autoBodyVelY = 0.0f;
            _autoBodyInterestX = cursorNormX;
            _autoBodyInterestY = cursorNormY;
            _autoBodyIntervalStartTicks = 0;
            _autoBodyIntervalTicks = 0;
        }

        private (float X, float Y) PickSaccadeTarget(float interestCenterX, float interestCenterY, float interestRadiusX, float interestRadiusY)
        {
            var history = _config.Model.AutonomousGaze.History;
            if (!history.Enabled || _gazeGridSize <= 0)
            {
                return RandomPointInInterestRegion(interestCenterX, interestCenterY, interestRadiusX, interestRadiusY);
            }

            EnsureGazeHistoryStorage(Math.Max(1, history.GridSize));
            var dwellSum = 0.0f;
            var visitSum = 0.0f;
            var hasAnyWeight = false;
            for (var row = 0; row < _gazeGridSize; row++)
            {
                for (var col = 0; col < _gazeGridSize; col++)
                {
                    dwellSum += _mouseDwellGrid[row, col];
                    visitSum += _gazeVisitGrid[row, col];
                    hasAnyWeight |= _mouseDwellGrid[row, col] > 0.0f || _gazeVisitGrid[row, col] > 0.0f;
                }
            }

            if (!hasAnyWeight)
            {
                return RandomPointInInterestRegion(interestCenterX, interestCenterY, interestRadiusX, interestRadiusY);
            }

            var cellCount = _gazeGridSize * _gazeGridSize;
            var equalWeight = 1.0f / Math.Max(1, cellCount);
            var familiarityWeight = Math.Clamp(history.FamiliarityWeight, 0.0f, 1.0f);
            var scores = new float[cellCount];
            var maxScore = float.NegativeInfinity;
            var index = 0;

            for (var row = 0; row < _gazeGridSize; row++)
            {
                for (var col = 0; col < _gazeGridSize; col++)
                {
                    var mouseScore = dwellSum > 0.0f ? _mouseDwellGrid[row, col] / dwellSum : equalWeight;
                    var gazeScore = visitSum > 0.0f ? _gazeVisitGrid[row, col] / visitSum : equalWeight;
                    var score = (familiarityWeight * mouseScore) + ((1.0f - familiarityWeight) * (1.0f - gazeScore));
                    scores[index++] = score;
                    maxScore = Math.Max(maxScore, score);
                }
            }

            var totalWeight = 0.0;
            var weights = new double[scores.Length];
            for (var i = 0; i < scores.Length; i++)
            {
                var weight = Math.Exp(scores[i] - maxScore);
                weights[i] = weight;
                totalWeight += weight;
            }

            if (totalWeight <= 0.0)
            {
                return RandomPointInInterestRegion(interestCenterX, interestCenterY, interestRadiusX, interestRadiusY);
            }

            var sample = Random.Shared.NextDouble() * totalWeight;
            var chosenIndex = weights.Length - 1;
            for (var i = 0; i < weights.Length; i++)
            {
                sample -= weights[i];
                if (sample <= 0.0)
                {
                    chosenIndex = i;
                    break;
                }
            }

            var chosenRow = chosenIndex / _gazeGridSize;
            var chosenCol = chosenIndex % _gazeGridSize;
            var centroidX = (((chosenCol + 0.5f) / _gazeGridSize) * 2.0f) - 1.0f;
            var centroidY = 1.0f - (((chosenRow + 0.5f) / _gazeGridSize) * 2.0f);
            var jitterX = RandomRange(-Math.Abs(interestRadiusX), Math.Abs(interestRadiusX));
            var jitterY = RandomRange(-Math.Abs(interestRadiusY), Math.Abs(interestRadiusY));
            return (Math.Clamp(centroidX + jitterX, -1.0f, 1.0f), Math.Clamp(centroidY + jitterY, -1.0f, 1.0f));
        }

        private void RecomputeBiasCentroid()
        {
            if (_gazeGridSize <= 0 || _mouseDwellGrid.Length == 0)
            {
                _hasBiasCentroid = false;
                _biasCentroidX = 0.0f;
                _biasCentroidY = 0.0f;
                return;
            }

            var total = 0.0f;
            var sumX = 0.0f;
            var sumY = 0.0f;
            for (var row = 0; row < _gazeGridSize; row++)
            {
                for (var col = 0; col < _gazeGridSize; col++)
                {
                    var weight = _mouseDwellGrid[row, col];
                    if (weight <= 0.0f)
                    {
                        continue;
                    }

                    var centroidX = (((col + 0.5f) / _gazeGridSize) * 2.0f) - 1.0f;
                    var centroidY = 1.0f - (((row + 0.5f) / _gazeGridSize) * 2.0f);
                    sumX += centroidX * weight;
                    sumY += centroidY * weight;
                    total += weight;
                }
            }

            if (total <= 0.0001f)
            {
                _hasBiasCentroid = false;
                _biasCentroidX = 0.0f;
                _biasCentroidY = 0.0f;
                return;
            }

            _hasBiasCentroid = true;
            _biasCentroidX = sumX / total;
            _biasCentroidY = sumY / total;
        }

        private void EnsureGazeHistoryStorage(int gridSize)
        {
            if (gridSize <= 0)
            {
                return;
            }

            if (_gazeGridSize == gridSize
                && _mouseDwellGrid.GetLength(0) == gridSize
                && _mouseDwellGrid.GetLength(1) == gridSize
                && _gazeVisitGrid.GetLength(0) == gridSize
                && _gazeVisitGrid.GetLength(1) == gridSize)
            {
                return;
            }

            _gazeGridSize = gridSize;
            _mouseDwellGrid = new float[gridSize, gridSize];
            _gazeVisitGrid = new float[gridSize, gridSize];
            RecomputeBiasCentroid();
        }

        private static void ApplyGridDecay(float[,] grid, float decayRate)
        {
            var decay = Math.Clamp(decayRate, 0.0f, 1.0f);
            for (var row = 0; row < grid.GetLength(0); row++)
            {
                for (var col = 0; col < grid.GetLength(1); col++)
                {
                    grid[row, col] *= decay;
                }
            }
        }

        private static (int Col, int Row) NormalizedToCell(float x, float y, int gridSize)
        {
            var safeGridSize = Math.Max(1, gridSize);
            var clampedX = Math.Clamp(x, -1.0f, 1.0f);
            var clampedY = Math.Clamp(y, -1.0f, 1.0f);
            var col = (int)(((clampedX + 1.0f) / 2.0f) * safeGridSize);
            var row = (int)((1.0f - ((clampedY + 1.0f) / 2.0f)) * safeGridSize);
            col = Math.Clamp(col, 0, safeGridSize - 1);
            row = Math.Clamp(row, 0, safeGridSize - 1);
            return (col, row);
        }

        private static long NextSaccadeIntervalTicks(float minHz, float maxHz, float bias)
        {
            var min = Math.Max(0.0f, Math.Min(minHz, maxHz));
            var max = Math.Max(0.0f, Math.Max(minHz, maxHz));
            if (max <= 0.0f)
            {
                return long.MaxValue;
            }

            if (min <= 0.0f)
            {
                min = max;
            }

            return NextJitterIntervalTicks(min, max, bias);
        }

        private bool IsSaccadeBlinkSuppressed(long nowTicks)
        {
            if (_blinkActive)
            {
                return true;
            }

            var suppressSeconds = Math.Max(0.0f, _config.Model.AutonomousGaze.Eye.Wander.BlinkSuppressSeconds);
            if (suppressSeconds <= 0.0f || _blinkEndTicks <= 0)
            {
                return false;
            }

            var suppressTicks = (long)(suppressSeconds * TickFrequency);
            return nowTicks < _blinkEndTicks + suppressTicks;
        }

        private (float X, float Y) RandomPointInInterestRegion(float interestCenterX, float interestCenterY, float interestRadiusX, float interestRadiusY)
        {
            var jitterX = RandomRange(-Math.Abs(interestRadiusX), Math.Abs(interestRadiusX));
            var jitterY = RandomRange(-Math.Abs(interestRadiusY), Math.Abs(interestRadiusY));
            return (
                Math.Clamp(interestCenterX + jitterX, -1.0f, 1.0f),
                Math.Clamp(interestCenterY + jitterY, -1.0f, 1.0f));
        }

        private static float RandomRange(float min, float max)
        {
            if (max <= min)
            {
                return min;
            }

            return min + ((float)Random.Shared.NextDouble() * (max - min));
        }

        private string ResolveBiasPath(string persistPath)
        {
            if (Path.IsPathRooted(persistPath))
            {
                return persistPath;
            }

            return Path.Combine(_configDirectory, persistPath);
        }

        private static float[][] ToJaggedArray(float[,] grid)
        {
            var rows = grid.GetLength(0);
            var cols = grid.GetLength(1);
            var result = new float[rows][];
            for (var row = 0; row < rows; row++)
            {
                result[row] = new float[cols];
                for (var col = 0; col < cols; col++)
                {
                    result[row][col] = grid[row, col];
                }
            }

            return result;
        }

        private static bool TryCopyBiasGrid(float[][]? source, int gridSize, out float[,] target)
        {
            target = new float[gridSize, gridSize];
            if (source == null || source.Length != gridSize)
            {
                return false;
            }

            for (var row = 0; row < gridSize; row++)
            {
                var sourceRow = source[row];
                if (sourceRow == null || sourceRow.Length != gridSize)
                {
                    return false;
                }

                for (var col = 0; col < gridSize; col++)
                {
                    target[row, col] = sourceRow[col];
                }
            }

            return true;
        }

        private sealed class GazeBiasState
        {
            public int GridSize { get; set; }
            public float[][] MouseDwell { get; set; } = Array.Empty<float[]>();
            public float[][] GazeVisit { get; set; } = Array.Empty<float[]>();
        }

        private void ApplyJitter(
            long nowTicks,
            bool eyeEnabled,
            bool headXYEnabled,
            bool bodyXYEnabled,
            ref float eyeX,
            ref float eyeY,
            ref float headX,
            ref float headY,
            ref float bodyX,
            ref float bodyY)
        {
            var jitter = _config.Model.LegacyGaze.Jitter;
            if (!jitter.Enabled)
            {
                _jitterEyeX = 0.0f;
                _jitterEyeY = 0.0f;
                _jitterHeadX = 0.0f;
                _jitterHeadY = 0.0f;
                _jitterBodyX = 0.0f;
                _jitterBodyY = 0.0f;
                _jitterEyeTargetX = 0.0f;
                _jitterEyeTargetY = 0.0f;
                _jitterHeadTargetX = 0.0f;
                _jitterHeadTargetY = 0.0f;
                _jitterBodyTargetX = 0.0f;
                _jitterBodyTargetY = 0.0f;
                _jitterEyeVelX = 0.0f;
                _jitterEyeVelY = 0.0f;
                _jitterHeadVelX = 0.0f;
                _jitterHeadVelY = 0.0f;
                _jitterBodyVelX = 0.0f;
                _jitterBodyVelY = 0.0f;
                _lastEyeJitterTicks = 0;
                _lastHeadJitterTicks = 0;
                _jitterEyeStartTicks = 0;
                _jitterEyeIntervalTicks = 0;
                _jitterEyeAmpScale = 1.0f;
                _jitterHeadStartTicks = 0;
                _jitterHeadIntervalTicks = 0;
                _jitterHeadAmpScale = 1.0f;
                _jitterBodyStartTicks = 0;
                _jitterBodyIntervalTicks = 0;
                _jitterBodyAmpScale = 1.0f;
                _lastJitterUpdateTicks = 0;
                return;
            }

            var motionX = _lastMotionDeltaX;
            var motionY = _lastMotionDeltaY;
            var motionMagnitude = MathF.Sqrt((motionX * motionX) + (motionY * motionY));
            var hasMotion = motionMagnitude > 0.0f;
            var dirX = hasMotion ? motionX / motionMagnitude : 0.0f;
            var dirY = hasMotion ? motionY / motionMagnitude : 0.0f;
            var bias = Math.Clamp(jitter.Amplitude.BiasToDelta, 0.0f, 1.0f);
            var idleScale = Math.Clamp(jitter.Amplitude.IdleScale, 0.0f, 1.0f);
            var amplitudeScale = hasMotion ? 1.0f : idleScale;
            var attentionScale = Math.Clamp(jitter.Attention.Scale, 0.0f, 1.0f);
            if (jitter.Attention.Auto.Enabled)
            {
                var slowThreshold = Math.Max(0.0f, jitter.Attention.Auto.SlowThresholdPx);
                var fastThreshold = Math.Max(slowThreshold, jitter.Attention.Auto.FastThresholdPx);
                var autoScale = jitter.Attention.Auto.IdleScale;
                if (motionMagnitude >= fastThreshold && fastThreshold > 0.0f)
                {
                    autoScale = jitter.Attention.Auto.FastScale;
                }
                else if (motionMagnitude >= slowThreshold && slowThreshold > 0.0f)
                {
                    autoScale = jitter.Attention.Auto.SlowScale;
                }

                autoScale = Math.Clamp(autoScale, 0.0f, 1.0f);
                attentionScale = 1.0f - ((1.0f - attentionScale) * (1.0f - autoScale));
            }

            var attentionFactor = 1.0f - Math.Clamp(attentionScale, 0.0f, 1.0f);
            var fadeSeconds = Math.Max(0.0f, jitter.Attention.FadeInSeconds);
            var fadeFactor = 1.0f;
            if (fadeSeconds > 0.0f)
            {
                var idleSeconds = 0.0f;
                if (_lastMovementTicks > 0)
                {
                    idleSeconds = (float)((nowTicks - _lastMovementTicks) / TickFrequency);
                }

                fadeFactor = Math.Clamp(idleSeconds / fadeSeconds, 0.0f, 1.0f);
            }

            var motionBlend = 1.0f;
            if (jitter.Attention.Auto.Enabled)
            {
                var slowThreshold = Math.Max(0.0f, jitter.Attention.Auto.SlowThresholdPx);
                var fastThreshold = Math.Max(slowThreshold, jitter.Attention.Auto.FastThresholdPx);
                if (fastThreshold > 0.0f)
                {
                    var t = Math.Clamp((motionMagnitude - slowThreshold) / Math.Max(0.0001f, fastThreshold - slowThreshold), 0.0f, 1.0f);
                    motionBlend = 1.0f - (t * t);
                }
            }

            var attentionReturnDelay = Math.Max(0.0f, jitter.Attention.ReturnDelaySeconds);
            if (attentionReturnDelay > 0.0f)
            {
                var idleSeconds = 0.0f;
                if (_lastMovementTicks > 0)
                {
                    idleSeconds = (float)((nowTicks - _lastMovementTicks) / TickFrequency);
                }

                if (idleSeconds < attentionReturnDelay)
                {
                    motionBlend = 0.0f;
                }
            }
            var eyeMinHz = jitter.Timing.Eye.MinHz;
            var eyeMaxHz = jitter.Timing.Eye.MaxHz;
            var eyeBiasHz = jitter.Timing.Eye.Bias;
            var headMinHz = jitter.Timing.Head.MinHz;
            var headMaxHz = jitter.Timing.Head.MaxHz;
            var headBiasHz = jitter.Timing.Head.Bias;
            var bodyMinHz = jitter.Timing.Body.MinHz;
            var bodyMaxHz = jitter.Timing.Body.MaxHz;
            var bodyBiasHz = jitter.Timing.Body.Bias;

            if (_jitterEyeIntervalTicks <= 0)
            {
                _jitterEyeIntervalTicks = NextJitterIntervalTicks(
                    eyeMinHz,
                    eyeMaxHz,
                    eyeBiasHz);
            }

            if (_jitterEyeStartTicks == 0 || nowTicks - _jitterEyeStartTicks >= _jitterEyeIntervalTicks)
            {
                _jitterEyeStartTicks = nowTicks;
                _jitterEyeIntervalTicks = NextJitterIntervalTicks(
                    eyeMinHz,
                    eyeMaxHz,
                    eyeBiasHz);
                _jitterEyeAmpScale = NextJitterScale(jitter.Amplitude.Eye.ScaleMin, jitter.Amplitude.Eye.ScaleMax);
                _lastEyeJitterTicks = nowTicks;
                (_jitterEyeTargetX, _jitterEyeTargetY) = NextJitterTarget(
                    jitter.Amplitude.Eye.AmpX * _jitterEyeAmpScale,
                    jitter.Amplitude.Eye.AmpY * _jitterEyeAmpScale,
                    dirX,
                    dirY,
                    hasMotion ? bias : 0.0f,
                    amplitudeScale);
            }

            if (_jitterHeadIntervalTicks <= 0)
            {
                _jitterHeadIntervalTicks = NextJitterIntervalTicks(
                    headMinHz,
                    headMaxHz,
                    headBiasHz);
            }

            if (_jitterHeadStartTicks == 0 || nowTicks - _jitterHeadStartTicks >= _jitterHeadIntervalTicks)
            {
                _jitterHeadStartTicks = nowTicks;
                _jitterHeadIntervalTicks = NextJitterIntervalTicks(
                    headMinHz,
                    headMaxHz,
                    headBiasHz);
                _jitterHeadAmpScale = NextJitterScale(jitter.Amplitude.Head.ScaleMin, jitter.Amplitude.Head.ScaleMax);
                _lastHeadJitterTicks = nowTicks;
                (_jitterHeadTargetX, _jitterHeadTargetY) = NextJitterTarget(
                    jitter.Amplitude.Head.AmpX * _jitterHeadAmpScale,
                    jitter.Amplitude.Head.AmpY * _jitterHeadAmpScale,
                    dirX,
                    dirY,
                    hasMotion ? bias : 0.0f,
                    amplitudeScale);
            }

            if (_jitterBodyIntervalTicks <= 0)
            {
                _jitterBodyIntervalTicks = NextJitterIntervalTicks(
                    bodyMinHz,
                    bodyMaxHz,
                    bodyBiasHz);
            }

            if (_jitterBodyStartTicks == 0 || nowTicks - _jitterBodyStartTicks >= _jitterBodyIntervalTicks)
            {
                _jitterBodyStartTicks = nowTicks;
                _jitterBodyIntervalTicks = NextJitterIntervalTicks(
                    bodyMinHz,
                    bodyMaxHz,
                    bodyBiasHz);
                _jitterBodyAmpScale = NextJitterScale(jitter.Amplitude.Body.ScaleMin, jitter.Amplitude.Body.ScaleMax);
                (_jitterBodyTargetX, _jitterBodyTargetY) = NextJitterTarget(
                    jitter.Amplitude.Body.AmpX * _jitterBodyAmpScale,
                    jitter.Amplitude.Body.AmpY * _jitterBodyAmpScale,
                    dirX,
                    dirY,
                    hasMotion ? bias : 0.0f,
                    amplitudeScale);
            }

            var dt = 0.0f;
            if (_lastJitterUpdateTicks > 0)
            {
                dt = (float)((nowTicks - _lastJitterUpdateTicks) / TickFrequency);
            }
            _lastJitterUpdateTicks = nowTicks;
            if (dt <= 0.0f)
            {
                dt = 0.0f;
            }
            else if (dt > 0.1f)
            {
                dt = 0.1f;
            }

            var eyePhase = GetJitterPhase(nowTicks, _jitterEyeStartTicks, _jitterEyeIntervalTicks);
            var headPhase = GetJitterPhase(nowTicks, _jitterHeadStartTicks, _jitterHeadIntervalTicks);
            var bodyPhase = GetJitterPhase(nowTicks, _jitterBodyStartTicks, _jitterBodyIntervalTicks);
            var eyeStrength = jitter.Spring.Eye.Strength * JitterEaseScale(eyePhase);
            var eyeDamping = jitter.Spring.Eye.Damping * JitterEaseScale(eyePhase);
            var headStrength = jitter.Spring.Head.Strength * JitterEaseScale(headPhase);
            var headDamping = jitter.Spring.Head.Damping * JitterEaseScale(headPhase);
            var bodyStrength = jitter.Spring.Body.Strength * JitterEaseScale(bodyPhase);
            var bodyDamping = jitter.Spring.Body.Damping * JitterEaseScale(bodyPhase);

            UpdateSpring(ref _jitterEyeX, ref _jitterEyeVelX, _jitterEyeTargetX, eyeStrength, eyeDamping, dt);
            UpdateSpring(ref _jitterEyeY, ref _jitterEyeVelY, _jitterEyeTargetY, eyeStrength, eyeDamping, dt);
            UpdateSpring(ref _jitterHeadX, ref _jitterHeadVelX, _jitterHeadTargetX, headStrength, headDamping, dt);
            UpdateSpring(ref _jitterHeadY, ref _jitterHeadVelY, _jitterHeadTargetY, headStrength, headDamping, dt);
            UpdateSpring(ref _jitterBodyX, ref _jitterBodyVelX, _jitterBodyTargetX, bodyStrength, bodyDamping, dt);
            UpdateSpring(ref _jitterBodyY, ref _jitterBodyVelY, _jitterBodyTargetY, bodyStrength, bodyDamping, dt);

            if (eyeEnabled)
            {
                var jitterScale = attentionFactor * fadeFactor * motionBlend;
                eyeX = Math.Clamp(eyeX + (_jitterEyeX * jitterScale), -1.0f, 1.0f);
                eyeY = Math.Clamp(eyeY + (_jitterEyeY * jitterScale), -1.0f, 1.0f);
            }

            if (headXYEnabled)
            {
                var jitterScale = attentionFactor * fadeFactor * motionBlend;
                headX = Math.Clamp(headX + (_jitterHeadX * jitterScale), -1.0f, 1.0f);
                headY = Math.Clamp(headY + (_jitterHeadY * jitterScale), -1.0f, 1.0f);
            }

            if (bodyXYEnabled)
            {
                var jitterScale = attentionFactor * fadeFactor * motionBlend;
                bodyX = Math.Clamp(bodyX + (_jitterBodyX * jitterScale), -1.0f, 1.0f);
                bodyY = Math.Clamp(bodyY + (_jitterBodyY * jitterScale), -1.0f, 1.0f);
            }
        }

        private void AdvanceFaceAnimationState(long nowTicks)
        {
            var face = _config.Face;
            var blink = face.Blink;
            var wink = face.Wink;

            var blinkEnabled = blink.Enabled && blink.Weight > 0.0f
                && !string.IsNullOrWhiteSpace(blink.ParamLeft)
                && !string.IsNullOrWhiteSpace(blink.ParamRight);
            var winkEnabled = wink.Enabled && wink.Weight > 0.0f && blinkEnabled;

            if (!blinkEnabled)
            {
                _blinkActive = false;
                _blinkNextTicks = 0;
            }
            if (!winkEnabled)
            {
                _winkActive = false;
                _winkSide = 0;
            }

            if (_blinkActive && nowTicks >= _blinkEndTicks)
            {
                _blinkActive = false;
                _blinkStartTicks = 0;
                _blinkDurationTicks = 0;
                var cooldownTicks = (long)(Math.Max(0.0f, blink.CooldownSeconds) * TickFrequency);
                _blinkCooldownEndTicks = nowTicks + cooldownTicks;
                _blinkNextTicks = 0;
            }

            if (_winkActive && nowTicks >= _winkEndTicks)
            {
                _winkActive = false;
                _winkSide = 0;
                _winkStartTicks = 0;
                _winkDurationTicks = 0;
                var cooldownTicks = (long)(Math.Max(0.0f, wink.CooldownSeconds) * TickFrequency);
                _winkCooldownEndTicks = nowTicks + cooldownTicks;
            }

            if (blinkEnabled)
            {
                if (_blinkNextTicks == 0)
                {
                    ScheduleNextFaceEvent(ref _blinkNextTicks, nowTicks, blink.MinIntervalSeconds, blink.MaxIntervalSeconds, blink.IntervalBias, 1.0f, blink.AttentionScaleMultiplier);
                }

                var motionThreshold = Math.Max(0.0f, blink.MotionThresholdPx);
                var motionChance = Math.Clamp(blink.MotionChance, 0.0f, 1.0f);
                var motionMagnitude = MathF.Sqrt((_lastMotionDeltaX * _lastMotionDeltaX) + (_lastMotionDeltaY * _lastMotionDeltaY));
                if (!_blinkActive
                    && !_winkActive
                    && motionThreshold > 0.0f
                    && motionMagnitude >= motionThreshold
                    && nowTicks >= _blinkCooldownEndTicks
                    && nowTicks >= _blinkNextTicks
                    && motionChance > 0.0f
                    && Random.Shared.NextDouble() <= motionChance)
                {
                    StartBlink(nowTicks, blink);
                }
                else if (!_blinkActive && !_winkActive && nowTicks >= _blinkNextTicks && nowTicks >= _blinkCooldownEndTicks)
                {
                    StartBlink(nowTicks, blink);
                }
            }

            if (winkEnabled)
            {
                if (ShouldTriggerFace(wink.Triggers))
                {
                    if (!_winkActive && nowTicks >= _winkCooldownEndTicks)
                    {
                        StartWink(nowTicks, wink);
                    }
                }
            }

            void StartBlink(long ticks, Config.BlinkConfig cfg)
            {
                _blinkActive = true;
                _smileActive = false;
                _winkActive = false;
                _winkSide = 0;
                var durationTicks = (long)(Math.Max(0.05f, cfg.DurationSeconds) * TickFrequency);
                _blinkStartTicks = ticks;
                _blinkDurationTicks = Math.Max(1, durationTicks);
                _blinkEndTicks = ticks + _blinkDurationTicks;
                ScheduleNextFaceEvent(ref _blinkNextTicks, ticks, cfg.MinIntervalSeconds, cfg.MaxIntervalSeconds, cfg.IntervalBias, 1.0f, cfg.AttentionScaleMultiplier);
            }

            void StartWink(long ticks, Config.WinkConfig cfg)
            {
                _winkActive = true;
                _blinkActive = false;
                _smileActive = false;
                _winkSide = Random.Shared.NextDouble() <= Math.Clamp(cfg.RightChance, 0.0f, 1.0f) ? 1 : -1;
                var durationTicks = (long)(Math.Max(0.05f, cfg.DurationSeconds) * TickFrequency);
                _winkStartTicks = ticks;
                _winkDurationTicks = Math.Max(1, durationTicks);
                _winkEndTicks = ticks + _winkDurationTicks;
            }
        }

        private void UpdateFaceExpressions(
            long nowTicks,
            float motionMagnitude,
            float attentionScale,
            bool allowFace,
            out float blinkLValue,
            out float blinkRValue,
            out float smileLValue,
            out float smileRValue)
        {
            blinkLValue = 0.0f;
            blinkRValue = 0.0f;
            smileLValue = 0.0f;
            smileRValue = 0.0f;

            var face = _config.Face;
            var blink = face.Blink;
            var smile = face.Smile;
            var wink = face.Wink;

            if (!allowFace)
            {
                _blinkActive = false;
                _smileActive = false;
                _winkActive = false;
                _winkSide = 0;
                UpdateFaceTriggerState();
                return;
            }

            var blinkEnabled = blink.Enabled && blink.Weight > 0.0f
                && !string.IsNullOrWhiteSpace(blink.ParamLeft)
                && !string.IsNullOrWhiteSpace(blink.ParamRight);
            var smileEnabled = smile.Enabled && smile.Weight > 0.0f
                && !string.IsNullOrWhiteSpace(smile.ParamLeft)
                && !string.IsNullOrWhiteSpace(smile.ParamRight);

            if (!blinkEnabled)
            {
                _blinkActive = false;
            }
            if (!smileEnabled)
            {
                _smileActive = false;
            }

            if (_smileActive && nowTicks >= _smileEndTicks)
            {
                _smileActive = false;
                _smileStartTicks = 0;
                var cooldownTicks = (long)(Math.Max(0.0f, smile.CooldownSeconds) * TickFrequency);
                _smileCooldownEndTicks = nowTicks + cooldownTicks;
            }

            if (smileEnabled)
            {
                if (ShouldTriggerFace(smile.Triggers))
                {
                    if (!_smileActive && nowTicks >= _smileCooldownEndTicks)
                    {
                        _smileActive = true;
                        _blinkActive = false;
                        _winkActive = false;
                        _winkSide = 0;
                        var durationTicks = (long)(Math.Max(0.1f, smile.DurationSeconds) * TickFrequency);
                        _smileStartTicks = nowTicks;
                        _smileEndTicks = nowTicks + Math.Max(1, durationTicks);
                    }
                }
            }

            if (_winkActive)
            {
                var winkValue = ComputePulseValue(
                    nowTicks,
                    _winkStartTicks,
                    _winkDurationTicks,
                    wink.AttackSeconds,
                    wink.ReleaseSeconds,
                    wink.ShockScale,
                    wink.ShockSeconds);
                blinkLValue = _winkSide < 0 ? winkValue : 0.0f;
                blinkRValue = _winkSide > 0 ? winkValue : 0.0f;
                var smileScale = Math.Clamp(wink.SmileScale, 0.0f, 1.0f);
                if (smileScale > 0.0f)
                {
                    smileLValue = smileScale;
                    smileRValue = smileScale;
                }
                UpdateFaceTriggerState();
                return;
            }

            if (_blinkActive)
            {
                var blinkValue = ComputePulseValue(
                    nowTicks,
                    _blinkStartTicks,
                    _blinkDurationTicks,
                    blink.AttackSeconds,
                    blink.ReleaseSeconds,
                    blink.ShockScale,
                    blink.ShockSeconds);
                blinkLValue = blinkValue;
                blinkRValue = blinkValue;
                UpdateFaceTriggerState();
                return;
            }

            if (_smileActive)
            {
                smileLValue = 1.0f;
                smileRValue = 1.0f;
            }

            UpdateFaceTriggerState();
        }

        private bool ShouldTriggerFace(IReadOnlyList<string> triggers)
        {
            if (triggers == null || triggers.Count == 0)
            {
                return false;
            }

            foreach (var trigger in triggers)
            {
                if (IsFaceTriggerActive(trigger))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsFaceTriggerActive(string trigger)
        {
            if (string.IsNullOrWhiteSpace(trigger))
            {
                return false;
            }

            return trigger switch
            {
                "onCenter" => _centerHoverActive && !_lastCenterHover,
                "offCenter" => !_centerHoverActive && _lastCenterHover,
                "onModel" => _modelHoverActive && !_lastModelHover,
                "offModel" => !_modelHoverActive && _lastModelHover,
                "onDeltaMode" => _useDeltaMode && !_lastDeltaMode,
                "offDeltaMode" => !_useDeltaMode && _lastDeltaMode,
                "onSmartMode" => _smartIdleActive && !_lastSmartIdle,
                "offSmartMode" => !_smartIdleActive && _lastSmartIdle,
                "onAFK" => _afkActive && !_lastAfk,
                "offAFK" => !_afkActive && _lastAfk,
                "onDizzy" => _dizzyActive && !_lastDizzy,
                "offDizzy" => !_dizzyActive && _lastDizzy,
                _ => false
            };
        }

        private void UpdateFaceTriggerState()
        {
            _lastCenterHover = _centerHoverActive;
            _lastModelHover = _modelHoverActive;
            _lastDeltaMode = _useDeltaMode;
            _lastSmartIdle = _smartIdleActive;
            _lastAfk = _afkActive;
            _lastDizzy = _dizzyActive;
        }

        private static void ScheduleNextFaceEvent(
            ref long nextTicks,
            long nowTicks,
            float minSeconds,
            float maxSeconds,
            float bias,
            float attentionScale,
            float attentionMultiplier)
        {
            var interval = NextIntervalSeconds(minSeconds, maxSeconds, bias);
            if (attentionMultiplier > 0.0f)
            {
                interval *= Lerp(1.0f, attentionMultiplier, Math.Clamp(attentionScale, 0.0f, 1.0f));
            }

            var ticks = (long)(interval * TickFrequency);
            if (ticks <= 0)
            {
                ticks = 1;
            }
            nextTicks = nowTicks + ticks;
        }

        private float ComputeAttentionScale(float motionMagnitude)
        {
            var jitter = _config.Model.LegacyGaze.Jitter;
            var attentionScale = Math.Clamp(jitter.Attention.Scale, 0.0f, 1.0f);
            if (!jitter.Attention.Auto.Enabled)
            {
                return attentionScale;
            }

            var slowThreshold = Math.Max(0.0f, jitter.Attention.Auto.SlowThresholdPx);
            var fastThreshold = Math.Max(slowThreshold, jitter.Attention.Auto.FastThresholdPx);
            var autoScale = jitter.Attention.Auto.IdleScale;
            if (motionMagnitude >= fastThreshold && fastThreshold > 0.0f)
            {
                autoScale = jitter.Attention.Auto.FastScale;
            }
            else if (motionMagnitude >= slowThreshold && slowThreshold > 0.0f)
            {
                autoScale = jitter.Attention.Auto.SlowScale;
            }

            autoScale = Math.Clamp(autoScale, 0.0f, 1.0f);
            return 1.0f - ((1.0f - attentionScale) * (1.0f - autoScale));
        }

        private static double NextIntervalSeconds(float minSeconds, float maxSeconds, float bias)
        {
            var min = Math.Max(0.1f, Math.Min(minSeconds, maxSeconds));
            var max = Math.Max(0.1f, Math.Max(minSeconds, maxSeconds));
            if (max <= min)
            {
                return min;
            }

            var exponent = Math.Max(0.1f, bias);
            var u = Random.Shared.NextDouble();
            var skewed = Math.Pow(u, exponent);
            return min + ((max - min) * skewed);
        }

        private static float ComputePulseValue(
            long nowTicks,
            long startTicks,
            long durationTicks,
            float attackSeconds,
            float releaseSeconds,
            float shockScale,
            float shockSeconds)
        {
            if (startTicks == 0 || durationTicks <= 0)
            {
                return 0.0f;
            }

            var duration = (float)(durationTicks / TickFrequency);
            var elapsed = (float)((nowTicks - startTicks) / TickFrequency);
            if (elapsed < 0.0 || elapsed > duration)
            {
                return 0.0f;
            }

            var attack = Math.Max(0.0f, attackSeconds);
            var release = Math.Max(0.0f, releaseSeconds);
            if (attack + release > duration)
            {
                var scale = duration / Math.Max(0.0001f, attack + release);
                attack *= scale;
                release *= scale;
            }

            var value = -1.0f;
            if (attack > 0.0 && elapsed < attack)
            {
                var t = (float)(elapsed / attack);
                value = -SmoothStep(t);
            }
            else if (release > 0.0 && elapsed > duration - release)
            {
                var t = (float)((elapsed - (duration - release)) / release);
                value = -SmoothStep(1.0f - t);
            }

            if (shockScale > 0.0f && shockSeconds > 0.0f && elapsed < shockSeconds)
            {
                var t = (float)Math.Clamp(elapsed / shockSeconds, 0.0, 1.0);
                var shockValue = Math.Clamp(shockScale, 0.0f, 0.11f);
                value = Lerp(shockValue, value, SmoothStep(t));
            }

            return value;
        }

        private static float SmoothStep(float t)
        {
            var clamped = Math.Clamp(t, 0.0f, 1.0f);
            return clamped * clamped * (3.0f - (2.0f * clamped));
        }

        private static float ComputeWakeupBlink(long nowTicks, long startTicks, long endTicks, int blinkCount)
        {
            if (startTicks == 0 || endTicks <= startTicks || blinkCount <= 0)
            {
                return 0.0f;
            }

            var duration = (float)((endTicks - startTicks) / TickFrequency);
            if (duration <= 0.0f)
            {
                return 0.0f;
            }

            var elapsed = (float)((nowTicks - startTicks) / TickFrequency);
            if (elapsed < 0.0f || elapsed > duration)
            {
                return 0.0f;
            }

            var interval = duration / blinkCount;
            if (interval <= 0.0f)
            {
                return 0.0f;
            }

            var local = (elapsed % interval) / interval;
            var wave = MathF.Sin(local * MathF.PI);
            return -SmoothStep(wave);
        }

        private static (float X, float Y) NextJitterTarget(
            float ampX,
            float ampY,
            float dirX,
            float dirY,
            float bias,
            float amplitudeScale)
        {
            if ((ampX <= 0.0f && ampY <= 0.0f) || amplitudeScale <= 0.0f)
            {
                return (0.0f, 0.0f);
            }

            var angle = Random.Shared.NextDouble() * Math.PI * 2.0;
            var randX = (float)Math.Cos(angle);
            var randY = (float)Math.Sin(angle);
            var blend = Math.Clamp(bias, 0.0f, 1.0f);
            var blendedX = Lerp(randX, dirX, blend);
            var blendedY = Lerp(randY, dirY, blend);
            var magnitude = MathF.Sqrt((blendedX * blendedX) + (blendedY * blendedY));
            if (magnitude > 0.0001f)
            {
                blendedX /= magnitude;
                blendedY /= magnitude;
            }

            var strength = (float)Random.Shared.NextDouble();
            var scaledX = blendedX * ampX * amplitudeScale * strength;
            var scaledY = blendedY * ampY * amplitudeScale * strength;
            return (scaledX, scaledY);
        }

        private static long NextJitterIntervalTicks(float minHz, float maxHz, float bias)
        {
            var min = Math.Max(0.1f, Math.Min(minHz, maxHz));
            var max = Math.Max(0.1f, Math.Max(minHz, maxHz));
            var hz = NextJitterHz(min, max, bias);

            var ticks = (long)(TickFrequency / hz);
            return ticks <= 0 ? 1 : ticks;
        }

        private static float NextJitterHz(float min, float max, float bias)
        {
            if (max <= min)
            {
                return min;
            }

            var exponent = Math.Max(0.1f, bias);
            var u = (float)Random.Shared.NextDouble();
            var skewed = MathF.Pow(u, exponent);
            return min + ((max - min) * skewed);
        }

        private static float NextJitterScale(float minScale, float maxScale)
        {
            var min = Math.Max(0.0f, Math.Min(minScale, maxScale));
            var max = Math.Max(0.0f, Math.Max(minScale, maxScale));
            if (max <= min)
            {
                return min;
            }

            return (float)(min + (Random.Shared.NextDouble() * (max - min)));
        }

        private static float GetJitterPhase(long nowTicks, long startTicks, long intervalTicks)
        {
            if (startTicks <= 0 || intervalTicks <= 0)
            {
                return 1.0f;
            }

            var t = (nowTicks - startTicks) / (double)intervalTicks;
            return (float)Math.Clamp(t, 0.0, 1.0);
        }

        private static float JitterEaseScale(float phase)
        {
            var t = Math.Clamp(phase, 0.0f, 1.0f);
            var eased = t * t * t;
            return Lerp(0.3f, 2.2f, eased);
        }

        private static void UpdateSpring(ref float position, ref float velocity, float target, float stiffness, float damping, float dt)
        {
            if (dt <= 0.0f)
            {
                return;
            }

            var accel = (target - position) * stiffness - velocity * damping;
            velocity += accel * dt;
            position += velocity * dt;
        }


        private float ComputeBodyTarget(int? rawDeltaX, int? rawDeltaY, Point cursor, long nowTicks)
        {
            if (_config.Body.Z.UsePrimaryCenter)
            {
                var primary = Screen.PrimaryScreen;
                var bounds = primary?.Bounds ?? Screen.FromPoint(cursor)?.Bounds ?? Rectangle.Empty;
                if (bounds.IsEmpty)
                {
                    return 0.0f;
                }

                var centerX = bounds.Left + bounds.Width / 2.0f;
                var centerY = bounds.Top + bounds.Height / 2.0f;
                var centerDeltaX = (int)(cursor.X - centerX);
                var centerDeltaY = (int)(cursor.Y - centerY);
                var centerThreshold = Math.Max(0.0f, _config.Body.Z.MovementThresholdPx);
                var centerDistance = MathF.Sqrt((centerDeltaX * centerDeltaX) + (centerDeltaY * centerDeltaY));
                if (centerDistance < centerThreshold)
                {
                    return 0.0f;
                }

                var centerRange = Math.Max(1.0f, _config.Body.Z.RangePx);
                var centerNormalizedX = centerDeltaX / centerRange;
                var centerNormalizedY = centerDeltaY / centerRange;
                var centerTarget = (centerNormalizedX * _config.Body.Z.Scale)
                    + (centerNormalizedY * _config.Body.Z.DyScale);
                return Math.Clamp(centerTarget, -1.0f, 1.0f);
            }

            int deltaX;
            int deltaY;
            if (rawDeltaX.HasValue && rawDeltaY.HasValue)
            {
                deltaX = rawDeltaX.Value;
                deltaY = rawDeltaY.Value;
                _hasBodyCursor = true;
                _bodyCursorX = cursor.X;
                _bodyCursorY = cursor.Y;
            }
            else
            {
                if (!_hasBodyCursor)
                {
                    _hasBodyCursor = true;
                    _bodyCursorX = cursor.X;
                    _bodyCursorY = cursor.Y;
                    return 0.0f;
                }

                deltaX = cursor.X - _bodyCursorX;
                deltaY = cursor.Y - _bodyCursorY;
                _bodyCursorX = cursor.X;
                _bodyCursorY = cursor.Y;
            }

            var threshold = Math.Max(0.0f, _config.Body.Z.MovementThresholdPx);
            var distance = MathF.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
            if (distance < threshold)
            {
                return 0.0f;
            }

            var range = Math.Max(1.0f, _config.Body.Z.RangePx);
            var normalizedX = deltaX / range;
            var normalizedY = deltaY / range;
            var target = (normalizedX * _config.Body.Z.Scale) + (normalizedY * _config.Body.Z.DyScale);
            var clamped = Math.Clamp(target, -1.0f, 1.0f);

            if (_config.Model.Flick.Enabled)
            {
                var flickThreshold = Math.Max(0.0f, _config.Model.Flick.ThresholdPx);
                if (distance >= flickThreshold && flickThreshold > 0.0f)
                {
                    var sign = Math.Sign(clamped);
                    var windowSeconds = Math.Max(0.0, _config.Model.Flick.OscillationWindowSeconds);
                    var windowTicks = (long)(windowSeconds * TickFrequency);
                    if (windowTicks <= 0)
                    {
                        windowTicks = 0;
                    }

                    if (_bodyFlickLastFlipTicks != 0 && windowTicks > 0 && nowTicks - _bodyFlickLastFlipTicks > windowTicks)
                    {
                        _bodyFlickOscillationCount = 0;
                        _bodyFlickCrossedModel = false;
                    }

                    if (_modelHoverActive)
                    {
                        _bodyFlickCrossedModel = true;
                    }

                    if (sign != 0 && _bodyFlickLastSign != 0 && sign != _bodyFlickLastSign)
                    {
                        _bodyFlickOscillationCount++;
                        _bodyFlickLastFlipTicks = nowTicks;

                        var required = Math.Max(1, _config.Model.Flick.OscillationsRequired);
                    if (_bodyFlickCrossedModel && _bodyFlickOscillationCount >= required)
                    {
                        _bodyFlickLastSign = sign;
                        _bodyFlickOscillationCount = 0;
                        _bodyFlickCrossedModel = false;
                        TriggerDizzy(nowTicks);
                        return 0.0f;
                    }
                    }

                    var alpha = Math.Clamp(_config.Model.Flick.AverageAlpha, 0.0f, 1.0f);
                    if (alpha <= 0.0f)
                    {
                        _bodyFlickAverage = clamped;
                    }
                    else
                    {
                        _bodyFlickAverage = Lerp(_bodyFlickAverage, clamped, alpha);
                    }

                    if (sign != 0)
                    {
                        _bodyFlickLastSign = sign;
                    }

                    return _bodyFlickAverage;
                }
            }

            _bodyFlickAverage = clamped;
            _bodyFlickLastSign = Math.Sign(clamped);
            _bodyFlickOscillationCount = 0;
            _bodyFlickCrossedModel = false;
            return clamped;
        }

        private float ApplyBodyHold(float rawTarget)
        {
            var clampedTarget = Math.Clamp(rawTarget, -1.0f, 1.0f);
            if (Math.Abs(clampedTarget) > 0.0001f)
            {
                var holdSign = Math.Sign(_bodyHold);
                var targetSign = Math.Sign(clampedTarget);
                if (_bodyHold == 0.0f || holdSign == targetSign)
                {
                    if (Math.Abs(clampedTarget) > Math.Abs(_bodyHold))
                    {
                        _bodyHold = clampedTarget;
                    }
                }
                else
                {
                    var flipAlpha = Math.Clamp(_config.Body.Z.FlipSpeed, 0.0f, 1.0f);
                    _bodyHold = Lerp(_bodyHold, clampedTarget, flipAlpha);
                }
            }
            else
            {
                var returnAlpha = Math.Clamp(_config.Body.Z.ReturnSpeed, 0.0f, 1.0f);
                var holdSign = Math.Sign(_bodyHold);
                var outputSign = Math.Sign(_bodyZ);
                if (holdSign == outputSign && Math.Abs(_bodyHold) > Math.Abs(_bodyZ))
                {
                    _bodyHold = _bodyZ;
                }
                else if (holdSign != outputSign)
                {
                    _bodyHold = 0.0f;
                }

                _bodyHold = Lerp(_bodyHold, 0.0f, returnAlpha);
            }

            return _bodyHold;
        }

        private float ComputeHeadZTarget(int? rawDeltaX, int? rawDeltaY, Point cursor, long nowTicks)
        {
            var headZConfig = _config.Head.Z;
            if (headZConfig.UsePrimaryCenter)
            {
                var primary = Screen.PrimaryScreen;
                var bounds = primary?.Bounds ?? Screen.FromPoint(cursor)?.Bounds ?? Rectangle.Empty;
                if (bounds.IsEmpty)
                {
                    return 0.0f;
                }

                var centerX = bounds.Left + bounds.Width / 2.0f;
                var centerY = bounds.Top + bounds.Height / 2.0f;
                var centerDeltaX = (int)(cursor.X - centerX);
                var centerDeltaY = (int)(cursor.Y - centerY);
                var centerThreshold = Math.Max(0.0f, headZConfig.MovementThresholdPx);
                var centerDistance = MathF.Sqrt((centerDeltaX * centerDeltaX) + (centerDeltaY * centerDeltaY));
                if (centerDistance < centerThreshold)
                {
                    return 0.0f;
                }

                var centerRange = Math.Max(1.0f, headZConfig.RangePx);
                var centerNormalizedX = centerDeltaX / centerRange;
                var centerNormalizedY = centerDeltaY / centerRange;
                var centerTarget = (centerNormalizedX * headZConfig.Scale)
                    + (centerNormalizedY * headZConfig.DyScale);
                return Math.Clamp(centerTarget, -1.0f, 1.0f);
            }

            int deltaX;
            int deltaY;
            if (rawDeltaX.HasValue && rawDeltaY.HasValue)
            {
                deltaX = rawDeltaX.Value;
                deltaY = rawDeltaY.Value;
                _hasHeadZCursor = true;
                _headZCursorX = cursor.X;
                _headZCursorY = cursor.Y;
            }
            else
            {
                if (!_hasHeadZCursor)
                {
                    _hasHeadZCursor = true;
                    _headZCursorX = cursor.X;
                    _headZCursorY = cursor.Y;
                    return 0.0f;
                }

                deltaX = cursor.X - _headZCursorX;
                deltaY = cursor.Y - _headZCursorY;
                _headZCursorX = cursor.X;
                _headZCursorY = cursor.Y;
            }

            var threshold = Math.Max(0.0f, headZConfig.MovementThresholdPx);
            var distance = MathF.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
            if (distance < threshold)
            {
                return 0.0f;
            }

            var range = Math.Max(1.0f, headZConfig.RangePx);
            var normalizedX = deltaX / range;
            var normalizedY = deltaY / range;
            var target = (normalizedX * headZConfig.Scale) + (normalizedY * headZConfig.DyScale);
            var clamped = Math.Clamp(target, -1.0f, 1.0f);

            if (_config.Model.Flick.Enabled)
            {
                var flickThreshold = Math.Max(0.0f, _config.Model.Flick.ThresholdPx);
                if (distance >= flickThreshold && flickThreshold > 0.0f)
                {
                    var sign = Math.Sign(clamped);
                    var windowSeconds = Math.Max(0.0, _config.Model.Flick.OscillationWindowSeconds);
                    var windowTicks = (long)(windowSeconds * TickFrequency);
                    if (windowTicks <= 0)
                    {
                        windowTicks = 0;
                    }

                    if (_headZFlickLastFlipTicks != 0 && windowTicks > 0 && nowTicks - _headZFlickLastFlipTicks > windowTicks)
                    {
                        _headZFlickOscillationCount = 0;
                        _headZFlickCrossedModel = false;
                    }

                    if (_modelHoverActive)
                    {
                        _headZFlickCrossedModel = true;
                    }

                    if (sign != 0 && _headZFlickLastSign != 0 && sign != _headZFlickLastSign)
                    {
                        _headZFlickOscillationCount++;
                        _headZFlickLastFlipTicks = nowTicks;

                        var required = Math.Max(1, _config.Model.Flick.OscillationsRequired);
                        if (_headZFlickCrossedModel && _headZFlickOscillationCount >= required)
                        {
                            _headZFlickLastSign = sign;
                            _headZFlickOscillationCount = 0;
                            _headZFlickCrossedModel = false;
                            return 0.0f;
                        }
                    }

                    var alpha = Math.Clamp(_config.Model.Flick.AverageAlpha, 0.0f, 1.0f);
                    if (alpha <= 0.0f)
                    {
                        _headZFlickAverage = clamped;
                    }
                    else
                    {
                        _headZFlickAverage = Lerp(_headZFlickAverage, clamped, alpha);
                    }

                    if (sign != 0)
                    {
                        _headZFlickLastSign = sign;
                    }

                    return _headZFlickAverage;
                }
            }

            _headZFlickAverage = clamped;
            _headZFlickLastSign = Math.Sign(clamped);
            _headZFlickOscillationCount = 0;
            _headZFlickCrossedModel = false;
            return clamped;
        }

        private float ApplyHeadZHold(float rawTarget)
        {
            var clampedTarget = Math.Clamp(rawTarget, -1.0f, 1.0f);
            if (Math.Abs(clampedTarget) > 0.0001f)
            {
                var holdSign = Math.Sign(_headZHold);
                var targetSign = Math.Sign(clampedTarget);
                if (_headZHold == 0.0f || holdSign == targetSign)
                {
                    if (Math.Abs(clampedTarget) > Math.Abs(_headZHold))
                    {
                        _headZHold = clampedTarget;
                    }
                }
                else
                {
                    var flipAlpha = Math.Clamp(_config.Head.Z.FlipSpeed, 0.0f, 1.0f);
                    _headZHold = Lerp(_headZHold, clampedTarget, flipAlpha);
                }
            }
            else
            {
                var returnAlpha = Math.Clamp(_config.Head.Z.ReturnSpeed, 0.0f, 1.0f);
                var holdSign = Math.Sign(_headZHold);
                var outputSign = Math.Sign(_headZ);
                if (holdSign == outputSign && Math.Abs(_headZHold) > Math.Abs(_headZ))
                {
                    _headZHold = _headZ;
                }
                else if (holdSign != outputSign)
                {
                    _headZHold = 0.0f;
                }

                _headZHold = Lerp(_headZHold, 0.0f, returnAlpha);
            }

            return _headZHold;
        }

        private void UpdateMovementState(int cursorX, int cursorY, long nowTicks, int? inputDeltaX, int? inputDeltaY)
        {
            if (!_hasCursor)
            {
                _hasCursor = true;
                _lastCursorX = cursorX;
                _lastCursorY = cursorY;
                _lastMovementTicks = nowTicks;
                _lastMotionDeltaX = 0;
                _lastMotionDeltaY = 0;
                return;
            }

            var deltaX = cursorX - _lastCursorX;
            var deltaY = cursorY - _lastCursorY;
            var moved = IsMovement(deltaX, deltaY);
            var motionX = 0;
            var motionY = 0;

            if (inputDeltaX.HasValue && inputDeltaY.HasValue
                && IsMovement(inputDeltaX.Value, inputDeltaY.Value))
            {
                motionX = inputDeltaX.Value;
                motionY = inputDeltaY.Value;
                _lastCursorX = cursorX;
                _lastCursorY = cursorY;
                _lastMovementTicks = nowTicks;
            }
            else if (moved)
            {
                motionX = deltaX;
                motionY = deltaY;
                _lastCursorX = cursorX;
                _lastCursorY = cursorY;
                _lastMovementTicks = nowTicks;
            }

            _lastMotionDeltaX = motionX;
            _lastMotionDeltaY = motionY;
        }


        private bool IsSmartIdle(long nowTicks, out double idleSeconds)
        {
            idleSeconds = 0.0;
            var idleAfterSeconds = Math.Max(0.0, _config.Vts.Smart.IdleAfterSeconds);
            if (idleAfterSeconds <= 0.0)
            {
                return false;
            }

            if (_lastMovementTicks == 0)
            {
                return false;
            }

            idleSeconds = (nowTicks - _lastMovementTicks) / TickFrequency;
            if (idleSeconds < idleAfterSeconds)
            {
                return false;
            }

            return true;
        }

        private bool ShouldSkipSend(long nowTicks)
        {
            if (_blinkActive || _winkActive ||
                nowTicks < _blinkCooldownEndTicks ||
                nowTicks < _winkCooldownEndTicks ||
                _bounceDvdActive || _wakeupActive || _wakeEaseOutActive || _sleepFadingIn ||
                _dizzyActive || nowTicks < _dizzyEndTicks)
            {
                return false;
            }

            if (!IsSmartIdle(nowTicks, out _))
            {
                return false;
            }

            var keepAliveHz = Math.Max(0.1, _config.Vts.Smart.KeepAliveHz);
            var intervalTicks = (long)(TickFrequency / keepAliveHz);
            return nowTicks - _lastSendTicks < intervalTicks;
        }

        private bool ShouldSkipCompute(long nowTicks)
        {
            if (_blinkActive || _winkActive ||
                nowTicks < _blinkCooldownEndTicks ||
                nowTicks < _winkCooldownEndTicks ||
                _bounceDvdActive || _wakeupActive || _wakeEaseOutActive || _sleepFadingIn ||
                _dizzyActive || nowTicks < _dizzyEndTicks)
            {
                return false;
            }

            if (!IsSmartIdle(nowTicks, out _))
            {
                return false;
            }

            var computeHz = Math.Max(1.0, _config.Vts.Smart.IdleComputeHz);
            var intervalTicks = (long)(TickFrequency / computeHz);
            return nowTicks - _lastComputeTicks < intervalTicks;
        }

        private bool IsMovement(int deltaX, int deltaY)
        {
            var threshold = Math.Max(0.0, _config.Vts.Smart.MovementThresholdPx);
            if (threshold <= 0.0)
            {
                return deltaX != 0 || deltaY != 0;
            }

            var thresholdSquared = threshold * threshold;
            var distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
            return distanceSquared >= thresholdSquared;
        }

        private void UpdateHoverTriggers(
            long nowTicks,
            Point cursor,
            float centerX,
            float centerY,
            float baseCenterX,
            float baseCenterY,
            bool preferPrimarySpace,
            float vtsWidth,
            float vtsHeight,
            bool hasOutlineCenter,
            float outlineCenterX,
            float outlineCenterY,
            bool hasOutlineHeight,
            float outlineHeight,
            Rectangle primaryBounds)
        {
            var radius = Math.Max(0.0f, _config.Model.Hotkeys.CenterRadiusPx);
            if (radius > 0.0f)
            {
                var dx = cursor.X - centerX;
                var dy = cursor.Y - centerY;
                var distanceSq = (dx * dx) + (dy * dy);
                var enterRadius = radius;
                var exitScale = Math.Max(1.0f, _config.Model.Hotkeys.HoverExitRadiusScale);
                var exitRadius = enterRadius * exitScale;

                if (_centerHoverActive)
                {
                    if (distanceSq > exitRadius * exitRadius)
                    {
                        _centerHoverActive = false;
                        _centerHoverEnterTicks = 0;
                        CenterExited?.Invoke();
                    }
                }
                else
                {
                    if (distanceSq <= enterRadius * enterRadius)
                    {
                        if (_centerHoverEnterTicks == 0)
                        {
                            _centerHoverEnterTicks = nowTicks;
                        }

                        var dwellSeconds = Math.Max(0.0, _config.Model.Hotkeys.HoverDwellSeconds);
                        var dwellTicks = (long)(TickFrequency * dwellSeconds);
                        if (dwellTicks <= 0 || nowTicks - _centerHoverEnterTicks >= dwellTicks)
                        {
                            _centerHoverActive = true;
                            _centerHoverEnterTicks = 0;
                            CenterHovered?.Invoke();
                        }
                    }
                    else
                    {
                        _centerHoverEnterTicks = 0;
                    }
                }
            }
            else
            {
                if (_centerHoverActive)
                {
                    _centerHoverActive = false;
                    CenterExited?.Invoke();
                }
                _centerHoverEnterTicks = 0;
            }

            var outlineScale = Math.Max(0.0f, _config.Model.Hotkeys.OutlineRadiusScale);
            if (outlineScale > 0.0f && hasOutlineCenter && hasOutlineHeight)
            {
                var vtsMinHalf = Math.Min(vtsWidth, vtsHeight) / 2.0f;
                var outlineOffsetX = outlineCenterX * vtsMinHalf;
                var outlineOffsetY = -outlineCenterY * (vtsHeight / 2.0f);
                var radiusPx = outlineHeight * (vtsHeight / 2.0f) * outlineScale;
                if (preferPrimarySpace)
                {
                    var scaleX = primaryBounds.Height / Math.Max(1.0f, vtsHeight);
                    var scaleY = primaryBounds.Height / Math.Max(1.0f, vtsHeight);
                    outlineOffsetX *= scaleX;
                    outlineOffsetY *= scaleY;
                    radiusPx *= scaleY;
                }

                var modelCenterX = baseCenterX + outlineOffsetX;
                var modelCenterY = baseCenterY + outlineOffsetY;
                var dx = cursor.X - modelCenterX;
                var dy = cursor.Y - modelCenterY;
                var distanceSq = (dx * dx) + (dy * dy);
                var exitScale = Math.Max(1.0f, _config.Model.Hotkeys.HoverExitRadiusScale);
                var exitRadius = radiusPx * exitScale;
                if (_modelHoverActive)
                {
                    if (distanceSq > exitRadius * exitRadius)
                    {
                        _modelHoverActive = false;
                        _modelHoverEnterTicks = 0;
                        ModelExited?.Invoke();
                    }
                }
                else
                {
                    if (distanceSq <= radiusPx * radiusPx)
                    {
                        if (_modelHoverEnterTicks == 0)
                        {
                            _modelHoverEnterTicks = nowTicks;
                        }

                        var dwellSeconds = Math.Max(0.0, _config.Model.Hotkeys.HoverDwellSeconds);
                        var dwellTicks = (long)(TickFrequency * dwellSeconds);
                        if (dwellTicks <= 0 || nowTicks - _modelHoverEnterTicks >= dwellTicks)
                        {
                            _modelHoverActive = true;
                            _modelHoverEnterTicks = 0;
                            ModelHovered?.Invoke();
                        }
                    }
                    else
                    {
                        _modelHoverEnterTicks = 0;
                    }
                }
            }
            else
            {
                if (_modelHoverActive)
                {
                    _modelHoverActive = false;
                    ModelExited?.Invoke();
                }
                _modelHoverEnterTicks = 0;
            }
        }

        private bool UpdateDizzyState(long nowTicks)
        {
            if (!_dizzyActive)
            {
                return false;
            }

            if (_dizzyEndTicks != 0 && nowTicks >= _dizzyEndTicks)
            {
                _dizzyActive = false;
                _dizzyStartTicks = 0;
                _dizzyEndTicks = 0;
                _dizzyEyeX = 0.0f;
                _dizzyEyeY = 0.0f;
                _dizzyHeadX = 0.0f;
                _dizzyHeadY = 0.0f;
                _dizzyHeadZ = 0.0f;
                _dizzyBodyX = 0.0f;
                _dizzyBodyY = 0.0f;
                _dizzyBodyZ = 0.0f;
                return false;
            }

            return true;
        }

        private bool ApplyDizzyEffect(
            long nowTicks,
            bool dizzyActive,
            bool eyeEnabled,
            bool headXYEnabled,
            bool bodyXYEnabled,
            bool headZEnabled,
            bool bodyZEnabled,
            ref float eyeX,
            ref float eyeY,
            ref float headX,
            ref float headY,
            ref float bodyX,
            ref float bodyY,
            ref float headZ,
            ref float bodyZ)
        {
            if (!dizzyActive || !_config.Animations.Dizzy.Enabled)
            {
                return false;
            }

            var dizzyIn = _config.Animations.Dizzy.DizzyIn;
            var elapsedSeconds = (float)((nowTicks - _dizzyStartTicks) / TickFrequency);
            if (elapsedSeconds < 0.0f)
            {
                elapsedSeconds = 0.0f;
            }

            // Fix 2: spin-up envelope using smoothstep over spinUpSeconds
            var spinUpSeconds = Math.Max(0.001f, dizzyIn.SpinUpSeconds);
            var spinUpT = Math.Clamp(elapsedSeconds / spinUpSeconds, 0.0f, 1.0f);
            var spinUpEnvelope = spinUpT * spinUpT * (3.0f - 2.0f * spinUpT);

            var eyeRadius = Math.Clamp(dizzyIn.EyeRadius, 0.0f, 1.0f) * spinUpEnvelope;
            var headRadius = Math.Clamp(dizzyIn.HeadRadius, 0.0f, 1.0f) * spinUpEnvelope;
            var eyeHz = Math.Max(0.001f, dizzyIn.EyeSpinHz);
            var headHz = Math.Max(0.001f, dizzyIn.HeadSpinHz);
            var eyeAngle = elapsedSeconds * eyeHz * (MathF.PI * 2.0f);
            var headAngle = elapsedSeconds * headHz * (MathF.PI * 2.0f);
            var targetEyeX = MathF.Cos(eyeAngle) * eyeRadius;
            var targetEyeY = MathF.Sin(eyeAngle) * eyeRadius;
            var targetHeadX = MathF.Cos(headAngle) * headRadius;
            var targetHeadY = MathF.Sin(headAngle) * headRadius;
            // Fix 6: headZ driven from head circle (cosine component, same radius)
            var targetHeadZ = targetHeadX;
            var bodyXYHz = Math.Max(0.001f, dizzyIn.BodyXYSpinHz);
            var bodyXYAngle = elapsedSeconds * bodyXYHz * (MathF.PI * 2.0f);
            var targetBodyX = MathF.Sin(bodyXYAngle) * dizzyIn.BodyXYAmplitudeX * spinUpEnvelope;
            var targetBodyY = MathF.Cos(bodyXYAngle) * dizzyIn.BodyXYAmplitudeY * spinUpEnvelope;

            var bodyHz = Math.Max(0.001f, dizzyIn.BodySpinHz);
            var bodyAngle = elapsedSeconds * bodyHz * (MathF.PI * 2.0f);
            var bodyMin = MathF.Min(dizzyIn.BodyMin, dizzyIn.BodyMax);
            var bodyMax = MathF.Max(dizzyIn.BodyMin, dizzyIn.BodyMax);
            var bodyMid = (bodyMin + bodyMax) * 0.5f;
            var bodyAmp = (bodyMax - bodyMin) * 0.5f * spinUpEnvelope;
            var targetBody = bodyMid + (MathF.Sin(bodyAngle) * bodyAmp);
            targetBody = Math.Clamp(targetBody, -1.0f, 1.0f);

            // Fix 3: time-scaled blend alpha — frame-rate independent
            var rawBlendAlpha = Math.Clamp(dizzyIn.BlendAlpha, 0.0f, 20.0f);
            var dt = _lastTickDeltaSeconds > 0.0f ? _lastTickDeltaSeconds : (1.0f / Math.Max(1f, _config.Vts.Inject.Hz));
            var blendAlpha = Math.Clamp(1.0f - MathF.Exp(-rawBlendAlpha * dt * (float)_config.Vts.Inject.Hz), 0.0f, 1.0f);

            if (_config.Model.Dizzy.BlendTracking)
            {
                if (eyeEnabled)
                {
                    eyeX = Math.Clamp(Lerp(eyeX, targetEyeX, blendAlpha), -1.0f, 1.0f);
                    eyeY = Math.Clamp(Lerp(eyeY, targetEyeY, blendAlpha), -1.0f, 1.0f);
                }

                if (headXYEnabled)
                {
                    headX = Math.Clamp(Lerp(headX, targetHeadX, blendAlpha), -1.0f, 1.0f);
                    headY = Math.Clamp(Lerp(headY, targetHeadY, blendAlpha), -1.0f, 1.0f);
                }

                if (bodyXYEnabled)
                {
                    bodyX = Math.Clamp(Lerp(bodyX, targetBodyX, blendAlpha), -1.0f, 1.0f);
                    bodyY = Math.Clamp(Lerp(bodyY, targetBodyY, blendAlpha), -1.0f, 1.0f);
                }

                if (headZEnabled)
                {
                    headZ = Math.Clamp(Lerp(headZ, targetHeadZ, blendAlpha), -1.0f, 1.0f);
                }

                if (bodyZEnabled)
                {
                    bodyZ = Math.Clamp(Lerp(bodyZ, targetBody, blendAlpha), -1.0f, 1.0f);
                }
            }
            else
            {
                if (eyeEnabled)
                {
                    _dizzyEyeX = Lerp(_dizzyEyeX, targetEyeX, blendAlpha);
                    _dizzyEyeY = Lerp(_dizzyEyeY, targetEyeY, blendAlpha);
                    eyeX = Math.Clamp(_dizzyEyeX, -1.0f, 1.0f);
                    eyeY = Math.Clamp(_dizzyEyeY, -1.0f, 1.0f);
                }

                if (headXYEnabled || bodyXYEnabled)
                {
                    _dizzyHeadX = Lerp(_dizzyHeadX, targetHeadX, blendAlpha);
                    _dizzyHeadY = Lerp(_dizzyHeadY, targetHeadY, blendAlpha);
                    if (headXYEnabled)
                    {
                        headX = Math.Clamp(_dizzyHeadX, -1.0f, 1.0f);
                        headY = Math.Clamp(_dizzyHeadY, -1.0f, 1.0f);
                    }
                }

                if (bodyXYEnabled)
                {
                    _dizzyBodyX = Lerp(_dizzyBodyX, targetBodyX, blendAlpha);
                    _dizzyBodyY = Lerp(_dizzyBodyY, targetBodyY, blendAlpha);
                    bodyX = Math.Clamp(_dizzyBodyX, -1.0f, 1.0f);
                    bodyY = Math.Clamp(_dizzyBodyY, -1.0f, 1.0f);
                }

                if (headZEnabled)
                {
                    _dizzyHeadZ = Lerp(_dizzyHeadZ, targetHeadZ, blendAlpha);
                    headZ = Math.Clamp(_dizzyHeadZ, -1.0f, 1.0f);
                }

                if (bodyZEnabled)
                {
                    _dizzyBodyZ = Lerp(_dizzyBodyZ, targetBody, blendAlpha);
                    bodyZ = Math.Clamp(_dizzyBodyZ, -1.0f, 1.0f);
                }
            }

            return true;
        }

        private void TriggerDizzy(long nowTicks)
        {
            _bodyHold = 0.0f;
            _bodyFlickAverage = 0.0f;
            _bodyZ = 0.0f;
            _bodyFlickLastSign = 0;
            _bodyFlickOscillationCount = 0;
            _bodyFlickCrossedModel = false;
            _headZHold = 0.0f;
            _headZFlickAverage = 0.0f;
            _headZ = 0.0f;
            _headZFlickLastSign = 0;
            _headZFlickOscillationCount = 0;
            _headZFlickCrossedModel = false;
            _dizzyBodyX = 0.0f;
            _dizzyBodyY = 0.0f;
            DizzyTriggered?.Invoke();
        }

        public void StartDizzyEffect(double durationSeconds)
        {
            if (!_config.Animations.Dizzy.Enabled)
            {
                return;
            }

            if (_config.Animations.Dizzy.Exclusive)
            {
                _afkActive = false;
                _afkForced = false;
                _sleepFadingIn = false;
                _wakeupActive = false;
                _wakeEaseOutActive = false;
            }

            var safeDuration = Math.Max(0.0, durationSeconds);
            _dizzyActive = true;
            _dizzyStartTicks = Stopwatch.GetTimestamp();
            if (safeDuration <= 0.0)
            {
                _dizzyEndTicks = 0;
                return;
            }

            var durationTicks = (long)(safeDuration * TickFrequency);
            if (durationTicks <= 0)
            {
                durationTicks = 1;
            }

            _dizzyEndTicks = _dizzyStartTicks + durationTicks;
        }

        public void StopDizzyEffect()
        {
            if (!_dizzyActive)
            {
                return;
            }

            _dizzyActive = false;
            _dizzyStartTicks = 0;
            _dizzyEndTicks = 0;
            _dizzyEyeX = 0.0f;
            _dizzyEyeY = 0.0f;
            _dizzyHeadX = 0.0f;
            _dizzyHeadY = 0.0f;
            _dizzyHeadZ = 0.0f;
            _dizzyBodyX = 0.0f;
            _dizzyBodyY = 0.0f;
            _dizzyBodyZ = 0.0f;
        }

        public void ForceAfk(bool active)
        {
            if (_afkActive == active && _afkForced == active)
            {
                return;
            }

            var nowTicks = Stopwatch.GetTimestamp();
            var wasAfk = _afkActive;
            _afkForced = active;
            ApplyAfkStateChange(active, wasAfk, nowTicks);

            AfkChanged?.Invoke(_afkActive);
        }

        public bool IsAfkForced => _afkForced;

        private bool ShouldUseDeltaMode(Point cursor, Screen screen, bool cursorVisible, long nowTicks)
        {
            if (!_config.Model.DeltaMode.Enabled)
            {
                return false;
            }

            if (!cursorVisible)
            {
                _centerHoldStartTicks = 0;
                var hiddenDelay = _config.Model.DeltaMode.HiddenCursorDelaySeconds;
                if (hiddenDelay > 0.0)
                {
                    if (_hiddenCursorStartTicks == 0) _hiddenCursorStartTicks = nowTicks;
                    var hiddenSeconds = (nowTicks - _hiddenCursorStartTicks) / TickFrequency;
                    return hiddenSeconds >= hiddenDelay;
                }
                return true;
            }
            _hiddenCursorStartTicks = 0;

            var inFocus = false;
            Rectangle focusBounds = default;
            if (_config.Model.DeltaMode.UseFullscreen && _mouseInput.IsForegroundFullscreen(screen))
            {
                inFocus = true;
                focusBounds = screen.Bounds;
            }
            else if (_config.Model.DeltaMode.UseWindowedFocus
                && _mouseInput.TryGetForegroundWindowBounds(out var bounds)
                && bounds.Contains(cursor))
            {
                inFocus = true;
                focusBounds = bounds;
            }

            if (!inFocus || !IsCursorNearCenter(cursor, focusBounds))
            {
                _centerHoldStartTicks = 0;
                return false;
            }

            var holdSeconds = Math.Max(0.0, _config.Model.DeltaMode.CenterHoldSeconds);
            if (holdSeconds <= 0.0)
            {
                return true;
            }

            if (_centerHoldStartTicks == 0)
            {
                _centerHoldStartTicks = nowTicks;
                return false;
            }

            var heldSeconds = (nowTicks - _centerHoldStartTicks) / TickFrequency;
            return heldSeconds >= holdSeconds;
        }

        private static bool TryGetPrimaryBounds(out Rectangle bounds)
        {
            var primary = Screen.PrimaryScreen;
            if (primary == null)
            {
                bounds = Rectangle.Empty;
                return false;
            }

            bounds = primary.Bounds;
            return !bounds.IsEmpty;
        }

        private void UpdateMonitorTransition(Point cursor)
        {
            if (!TryGetPrimaryBounds(out var bounds))
            {
                return;
            }

            var onPrimary = bounds.Contains(cursor);
            if (!_hasPrimaryState)
            {
                _hasPrimaryState = true;
                _lastOnPrimary = onPrimary;
                return;
            }

            if (onPrimary != _lastOnPrimary)
            {
                _lastOnPrimary = onPrimary;
                MonitorTransitioned?.Invoke(!onPrimary);
            }
        }

        private Rectangle ResolveTrackingBounds(Screen cursorScreen)
        {
            if (_config.Model.Mapping.UsePrimaryMonitor)
            {
                var primary = Screen.PrimaryScreen;
                if (primary != null && !primary.Bounds.IsEmpty)
                {
                    return primary.Bounds;
                }
            }

            if (_config.Model.Mapping.UseVirtualDesktop)
            {
                var virtualBounds = SystemInformation.VirtualScreen;
                if (virtualBounds.Width > 0 && virtualBounds.Height > 0)
                {
                    return virtualBounds;
                }
            }

            if (_hasWindowBounds)
            {
                var windowBounds = new Rectangle(_windowLeft, _windowTop, _windowClientWidth, _windowClientHeight);
                if (windowBounds.Width > 0 && windowBounds.Height > 0)
                {
                    var windowScreen = Screen.FromRectangle(windowBounds);
                    if (windowScreen != null && !windowScreen.Bounds.IsEmpty)
                    {
                        return windowScreen.Bounds;
                    }
                }
            }

            return cursorScreen.Bounds;
        }

        private Screen? ResolveMainMonitor()
        {
            var screens = Screen.AllScreens;
            var requestedIndex = _config.Model.Mapping.WorldRelative.MainMonitorIndex;
            if (requestedIndex >= 0 && requestedIndex < screens.Length)
            {
                var requestedScreen = screens[requestedIndex];
                if (!requestedScreen.Bounds.IsEmpty)
                {
                    return requestedScreen;
                }
            }

            var primaryScreen = Screen.PrimaryScreen;
            if (primaryScreen != null && !primaryScreen.Bounds.IsEmpty)
            {
                return primaryScreen;
            }

            foreach (var candidate in screens)
            {
                if (!candidate.Bounds.IsEmpty)
                {
                    return candidate;
                }
            }

            return null;
        }

        private bool IsCursorNearCenter(Point cursor, Rectangle bounds)
        {
            var radius = ResolveDeltaRadius();
            if (radius <= 0.0f)
            {
                return true;
            }

            var centerX = bounds.Left + bounds.Width / 2.0f;
            var centerY = bounds.Top + bounds.Height / 2.0f;
            var dx = cursor.X - centerX;
            var dy = cursor.Y - centerY;
            return (dx * dx) + (dy * dy) <= radius * radius;
        }

        private float ResolveDeltaRadius()
        {
            var baseRadius = Math.Max(0.0f, _config.Model.DeltaMode.CenterRadiusPx);
            var overrideRadius = Math.Max(0.0f, _config.Model.DeltaMode.RadiusOverridePx);
            if (overrideRadius <= baseRadius)
            {
                return baseRadius;
            }

            var targets = _config.Model.DeltaMode.RadiusWindowTitles;
            if (targets.Count == 0)
            {
                return baseRadius;
            }

            if (!_mouseInput.TryGetForegroundWindowTitle(out var title))
            {
                return baseRadius;
            }

            foreach (var target in targets)
            {
                if (string.IsNullOrWhiteSpace(target))
                {
                    continue;
                }

                if (title.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return overrideRadius;
                }
            }

            return baseRadius;
        }

        private void UpdateDeltaOffset(
            int cursorX,
            int cursorY,
            float rangeX,
            float rangeY,
            int? rawDeltaX,
            int? rawDeltaY,
            bool allowCursorFallback)
        {
            if (rawDeltaX.HasValue && rawDeltaY.HasValue)
            {
                _hasDeltaCursor = true;
                _deltaCursorX = cursorX;
                _deltaCursorY = cursorY;
                ApplyDelta(rawDeltaX.Value, rawDeltaY.Value, rangeX, rangeY);
                return;
            }

            if (!allowCursorFallback)
            {
                ApplyDeltaReturn();
                return;
            }

            if (!_hasDeltaCursor)
            {
                _hasDeltaCursor = true;
                _deltaCursorX = cursorX;
                _deltaCursorY = cursorY;
                return;
            }

            var deltaX = cursorX - _deltaCursorX;
            var deltaY = cursorY - _deltaCursorY;
            _deltaCursorX = cursorX;
            _deltaCursorY = cursorY;
            ApplyDelta(deltaX, deltaY, rangeX, rangeY);
        }

        private void ApplyDeltaReturn()
        {
            var returnSpeed = Math.Clamp(_config.Model.DeltaMode.ReturnSpeed, 0.0f, 1.0f);
            if (returnSpeed <= 0.0f)
            {
                return;
            }

            if (!_deltaSpringActive)
            {
                var hold = Math.Clamp(_config.Model.DeltaMode.SpringHoldFactor, 0.0f, 1.0f);
                _deltaSpringTargetX = _deltaOffsetX * hold;
                _deltaSpringTargetY = _deltaOffsetY * hold;
                _deltaSpringActive = true;
            }

            _deltaOffsetX = Lerp(_deltaOffsetX, _deltaSpringTargetX, returnSpeed);
            _deltaOffsetY = Lerp(_deltaOffsetY, _deltaSpringTargetY, returnSpeed);
        }

        private void ApplyDelta(int deltaX, int deltaY, float rangeX, float rangeY)
        {
            deltaY = -deltaY;

            var threshold = Math.Max(0.0f, _config.Model.DeltaMode.MovementThresholdPx);
            if (threshold > 0.0f)
            {
                var thresholdSquared = threshold * threshold;
                var distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
                if (distanceSquared < thresholdSquared)
                {
                    ApplyDeltaReturn();
                    return;
                }
            }

            _deltaSpringActive = false;
            _deltaSpringTargetX = 0.0f;
            _deltaSpringTargetY = 0.0f;

            var scale = _config.Model.DeltaMode.Scale;
            var normalizedX = (float)(deltaX / Math.Max(1.0f, rangeX)) * scale;
            var normalizedY = (float)(deltaY / Math.Max(1.0f, rangeY)) * scale;

            _deltaOffsetX += normalizedX;
            _deltaOffsetY += normalizedY;

            var maxOffset = Math.Max(0.05f, _config.Model.DeltaMode.MaxOffset);
            _deltaOffsetX = Math.Clamp(_deltaOffsetX, -maxOffset, maxOffset);
            _deltaOffsetY = Math.Clamp(_deltaOffsetY, -maxOffset, maxOffset);
        }

    }
}
