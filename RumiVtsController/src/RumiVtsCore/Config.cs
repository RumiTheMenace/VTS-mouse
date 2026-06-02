using System.Text.Json;

namespace RumiVtsController
{
    internal sealed class Config
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public bool Enabled { get; set; } = true;
        public VtsConfig Vts { get; set; } = new();
        public ModelTrackingConfig Model { get; set; } = new();
        public EyeConfig Eye { get; set; } = new();
        public HeadConfig Head { get; set; } = new();
        public BodyConfig Body { get; set; } = new();
        public FaceConfig Face { get; set; } = new();
        public AnimationsConfig Animations { get; set; } = new();
        public Dictionary<string, ProfileConfig> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public ExpressionConfig Expression { get; set; } = new();
        public ErrorHandlingConfig ErrorHandling { get; set; } = new();

        public static Config Load(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Missing config.json", path);
            }

            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<Config>(json, JsonOptions);
            if (config == null)
            {
                throw new InvalidDataException("Failed to parse config.json.");
            }

            return config;
        }

        public void Save(string path)
        {
            var json = JsonSerializer.Serialize(this, WriteOptions);
            File.WriteAllText(path, json);
        }

        public sealed class VtsConfig
        {
            public int Port { get; set; } = 8001;
            public string PluginName { get; set; } = "RumiVtsController";
            public string PluginDeveloper { get; set; } = "Deer";
            public int ConnectAttempts { get; set; } = 3;
            public double ConnectRetrySeconds { get; set; } = 5.0;
            public InjectConfig Inject { get; set; } = new();
            public DebugConfig Debug { get; set; } = new();
            public SmartConfig Smart { get; set; } = new();
        }

        public sealed class InjectConfig
        {
            public int Hz { get; set; } = 60;
        }

        public sealed class EyeConfig
        {
            public string ParamX { get; set; } = "";
            public string ParamY { get; set; } = "";
            public float Weight { get; set; } = 1.0f;
            public float Scale { get; set; } = 1.1f;
            public float ScaleX { get; set; } = 1.0f;
            public float ScaleY { get; set; } = 1.0f;
        }

        public sealed class HeadConfig
        {
            public string ParamX { get; set; } = "";
            public string ParamY { get; set; } = "";
            public string ParamZ { get; set; } = "";
            public float Weight { get; set; } = 1.0f;
            public float WeightZ { get; set; } = 1.0f;
            public float SpeedRange { get; set; } = 0.15f;
            public float DistanceRange { get; set; } = 0.35f;
            public float MinAlpha { get; set; } = 0.02f;
            public float MaxAlpha { get; set; } = 0.35f;
            public HeadZConfig Z { get; set; } = new();
        }

        public sealed class HeadZConfig
        {
            public float RangePx { get; set; } = 120.0f;
            public float Scale { get; set; } = 0.6f;
            public float DyScale { get; set; } = 0.15f;
            public float Smoothing { get; set; } = 0.08f;
            public float ReturnSpeed { get; set; } = 0.03f;
            public float MovementThresholdPx { get; set; } = 0.5f;
            public bool UsePrimaryCenter { get; set; } = false;
            public bool HoldPeak { get; set; } = true;
            public float FlipSpeed { get; set; } = 0.06f;
            public bool InvertZ { get; set; } = false;
        }

        public sealed class BodyConfig
        {
            public string ParamX { get; set; } = "";
            public string ParamY { get; set; } = "";
            public string ParamZ { get; set; } = "";
            public float Weight { get; set; } = 1.0f;
            public float WeightZ { get; set; } = 1.0f;
            public float SpeedRange { get; set; } = 0.15f;
            public float DistanceRange { get; set; } = 0.35f;
            public float MinAlpha { get; set; } = 0.02f;
            public float MaxAlpha { get; set; } = 0.35f;
            public BodyZConfig Z { get; set; } = new();
        }

        public sealed class BodyZConfig
        {
            public float RangePx { get; set; } = 120.0f;
            public float Scale { get; set; } = 0.6f;
            public float DyScale { get; set; } = 0.15f;
            public float Smoothing { get; set; } = 0.08f;
            public float ReturnSpeed { get; set; } = 0.03f;
            public float MovementThresholdPx { get; set; } = 0.5f;
            public bool UsePrimaryCenter { get; set; } = false;
            public bool HoldPeak { get; set; } = true;
            public float FlipSpeed { get; set; } = 0.06f;
            public bool InvertZ { get; set; } = false;
        }

        public sealed class FaceConfig
        {
            public BlinkConfig Blink { get; set; } = new();
            public SmileConfig Smile { get; set; } = new();
            public WinkConfig Wink { get; set; } = new();
        }

        public sealed class BlinkConfig
        {
            public bool Enabled { get; set; } = true;
            public string ParamLeft { get; set; } = "";
            public string ParamRight { get; set; } = "";
            public float Weight { get; set; } = 1.0f;
            public float MinIntervalSeconds { get; set; } = 2.5f;
            public float MaxIntervalSeconds { get; set; } = 6.0f;
            public float IntervalBias { get; set; } = 2.0f;
            public float DurationSeconds { get; set; } = 0.12f;
            public float CooldownSeconds { get; set; } = 0.2f;
            public float AttentionScaleMultiplier { get; set; } = 2.0f;
            public float MotionThresholdPx { get; set; } = 80.0f;
            public float MotionChance { get; set; } = 0.2f;
            public float AttackSeconds { get; set; } = 0.04f;
            public float ReleaseSeconds { get; set; } = 0.06f;
            public float ShockScale { get; set; } = 0.0f;
            public float ShockSeconds { get; set; } = 0.03f;
        }

        public sealed class SmileConfig
        {
            public bool Enabled { get; set; } = true;
            public string ParamLeft { get; set; } = "";
            public string ParamRight { get; set; } = "";
            public float Weight { get; set; } = 1.0f;
            public float DurationSeconds { get; set; } = 1.5f;
            public float CooldownSeconds { get; set; } = 1.0f;
            public List<string> Triggers { get; set; } = new();
        }

        public sealed class WinkConfig
        {
            public bool Enabled { get; set; } = true;
            public float Weight { get; set; } = 1.0f;
            public float DurationSeconds { get; set; } = 0.25f;
            public float CooldownSeconds { get; set; } = 1.0f;
            public float RightChance { get; set; } = 0.5f;
            public float SmileScale { get; set; } = 0.0f;
            public float AttackSeconds { get; set; } = 0.04f;
            public float ReleaseSeconds { get; set; } = 0.06f;
            public float ShockScale { get; set; } = 0.0f;
            public float ShockSeconds { get; set; } = 0.03f;
            public List<string> Triggers { get; set; } = new();
        }

        public sealed class WorldRelativeConfig
        {
            public bool Enabled { get; set; } = false;
            public float Sensitivity { get; set; } = 1.0f;
            public int MainMonitorIndex { get; set; } = -1;
            public float MainMonitorWeight { get; set; } = 0.8f;
            public float OtherMonitorWeight { get; set; } = 0.2f;
            public float WeightLerpSpeed { get; set; } = 0.05f;
        }

        public sealed class MappingConfig
        {
            public float RangePxX { get; set; } = 600.0f;
            public float RangePxY { get; set; } = 400.0f;
            public float Deadzone { get; set; } = 0.002f;
            public float ClampRadius { get; set; } = 1.0f;
            public bool UseVirtualDesktop { get; set; } = false;
            public bool UsePrimaryMonitor { get; set; } = false;
            public WorldRelativeConfig WorldRelative { get; set; } = new();
        }

        public sealed class ModelTrackingConfig
        {
            public bool UseModelCenter { get; set; } = true;
            public float Hz { get; set; } = 2.0f;
            public float OffsetY { get; set; } = 0.0f;
            public float OutlineRefHeight { get; set; } = 0.0f;
            public MappingConfig Mapping { get; set; } = new();
            public ModelHotkeyConfig Hotkeys { get; set; } = new();
            public ModelFlickConfig Flick { get; set; } = new();
            public ModelDizzyConfig Dizzy { get; set; } = new();
            public ModelBodyBreathConfig BodyBreath { get; set; } = new();
            public ModelAfkConfig Afk { get; set; } = new();
            public LegacyGazeConfig LegacyGaze { get; set; } = new();
            public AutonomousGazeConfig AutonomousGaze { get; set; } = new();
            public DeltaModeConfig DeltaMode { get; set; } = new();
        }

        public sealed class LegacyGazeConfig
        {
            public bool Enabled { get; set; } = true;
            public JitterConfig Jitter { get; set; } = new();
        }

        public sealed class ModelHotkeyConfig
        {
            public float CenterRadiusPx { get; set; } = 0.0f;
            public float HoverExitRadiusScale { get; set; } = 1.2f;
            public double HoverDwellSeconds { get; set; } = 0.15;
            public float OutlineRadiusScale { get; set; } = 0.5f;
            public float DizzyThresholdPx { get; set; } = 0.0f;
        }

        public sealed class ModelFlickConfig
        {
            public bool Enabled { get; set; } = false;
            public float ThresholdPx { get; set; } = 120.0f;
            public float AverageAlpha { get; set; } = 0.25f;
            public int OscillationsRequired { get; set; } = 3;
            public double OscillationWindowSeconds { get; set; } = 1.5;
        }

        public sealed class ModelDizzyConfig
        {
            public bool BlendTracking { get; set; } = true;
        }

        public sealed class ModelBodyBreathConfig
        {
            public bool Enabled { get; set; } = false;
            public float Hz { get; set; } = 0.12f;
            public float AmpX { get; set; } = 0.01f;
            public float AmpY { get; set; } = 0.005f;
        }

        public sealed class AutonomousAttentionConfig
        {
            public float SnapAlpha { get; set; } = 1.0f;
            public float AttractionAlpha { get; set; } = 1.0f;
        }

        public sealed class AutonomousWanderConfig
        {
            public float SpringStrength { get; set; } = 1.0f;
            public float SpringDamping { get; set; } = 1.0f;
            public float Scale { get; set; } = 1.0f;
            public float Deadzone { get; set; } = 0.0f;
            public float MinHz { get; set; } = 0.1f;
            public float MaxHz { get; set; } = 1.0f;
            public float Bias { get; set; } = 1.0f;
            public float BlinkSuppressSeconds { get; set; } = 0.0f;
        }

        public sealed class AutonomousHeadZConfig
        {
            public float Scale { get; set; } = 0.18f;
            public float DyScale { get; set; } = 0.08f;
            public float SpringStrength { get; set; } = 6.0f;
            public float SpringDamping { get; set; } = 4.0f;
        }

        public sealed class AutonomousAxisConfig
        {
            public AutonomousAttentionConfig Attention { get; set; } = new();
            public AutonomousWanderConfig Wander { get; set; } = new();
            public AutonomousHeadZConfig HeadZ { get; set; } = new();
        }

        public sealed class AutonomousGazeConfig
        {
            public bool Enabled { get; set; } = false;
            public float InterestRadiusX { get; set; } = 0.5f;
            public float InterestRadiusY { get; set; } = 0.5f;
            public float CursorAttractionStrength { get; set; } = 55.0f;
            public float InterestDecaySpeed { get; set; } = 0.001f;
            public float FastSnapThreshold { get; set; } = 0.6f;
            public float MotionReferenceRange { get; set; } = 8.0f;
            public AutonomousAxisConfig Eye { get; set; } = new()
            {
                Attention = new() { SnapAlpha = 0.4f, AttractionAlpha = 0.15f },
                Wander = new()
                {
                    SpringStrength = 50.0f, SpringDamping = 16.0f, Scale = 1.0f, Deadzone = 0.0f,
                    MinHz = 0.15f, MaxHz = 2.0f, Bias = 8.0f, BlinkSuppressSeconds = 0.15f
                }
            };
            public AutonomousAxisConfig Head { get; set; } = new()
            {
                Attention = new() { SnapAlpha = 0.25f, AttractionAlpha = 0.08f },
                Wander = new()
                {
                    SpringStrength = 28.0f, SpringDamping = 10.0f, Scale = 0.85f, Deadzone = 0.0f,
                    MinHz = 0.1f, MaxHz = 0.5f, Bias = 10.0f, BlinkSuppressSeconds = 0.0f
                }
            };
            public AutonomousAxisConfig Body { get; set; } = new()
            {
                Attention = new() { SnapAlpha = 0.1f, AttractionAlpha = 0.03f },
                Wander = new()
                {
                    SpringStrength = 8.0f, SpringDamping = 6.0f, Scale = 0.6f, Deadzone = 0.0f,
                    MinHz = 0.05f, MaxHz = 0.2f, Bias = 1.0f, BlinkSuppressSeconds = 0.0f
                }
            };
            public GazeHistoryConfig History { get; set; } = new();
        }

        public sealed class GazeHistoryConfig
        {
            public bool Enabled { get; set; } = true;
            public int GridSize { get; set; } = 3;
            public float MouseDwellDecayRate { get; set; } = 0.9999f;
            public float GazeVisitDecayRate { get; set; } = 0.995f;
            public float FamiliarityWeight { get; set; } = 0.55f;
            public string PersistPath { get; set; } = "gaze_bias.json";
            public float PersistIntervalSeconds { get; set; } = 30.0f;
        }

        public sealed class ModelAfkConfig
        {
        }

        public sealed class ErrorHandlingConfig
        {
            public double ThrottleSeconds { get; set; } = 1.0;
            public List<int> IgnoreErrorIds { get; set; } = new();
        }

        public sealed class ExpressionConfig
        {
            public bool Enabled { get; set; } = false;
            public int Port { get; set; } = 5100;
        }

        public sealed class DebugConfig
        {
            public string TrackingToggleHotkey { get; set; } = "F1";
            public string FreezeTrackingHotkey { get; set; } = "F4";
            public string CalibrateHotkey { get; set; } = "F2";
            public string DumpHotkey { get; set; } = "F3";
        }

        public sealed class JitterConfig
        {
            public bool Enabled { get; set; }
            public JitterTimingConfig Timing { get; set; } = new();
            public JitterAttentionConfig Attention { get; set; } = new();
            public JitterAmplitudeConfig Amplitude { get; set; } = new();
            public JitterSpringConfig Spring { get; set; } = new();
        }

        public sealed class JitterTimingConfig
        {
            public JitterAxisTiming Eye { get; set; } = new() { MinHz = 20.0f, MaxHz = 45.0f, Bias = 1.0f };
            public JitterAxisTiming Head { get; set; } = new() { MinHz = 6.0f, MaxHz = 14.0f, Bias = 1.0f };
            public JitterAxisTiming Body { get; set; } = new() { MinHz = 0.05f, MaxHz = 0.2f, Bias = 1.0f };
        }

        public sealed class JitterAxisTiming
        {
            public float MinHz { get; set; } = 1.0f;
            public float MaxHz { get; set; } = 1.0f;
            public float Bias { get; set; } = 1.0f;
        }

        public sealed class JitterAttentionConfig
        {
            public float Scale { get; set; } = 0.0f;
            public float FadeInSeconds { get; set; } = 0.2f;
            public float ReturnDelaySeconds { get; set; } = 0.4f;
            public JitterAttentionAutoConfig Auto { get; set; } = new();
        }

        public sealed class JitterAttentionAutoConfig
        {
            public bool Enabled { get; set; } = false;
            public float IdleScale { get; set; } = 0.0f;
            public float SlowScale { get; set; } = 0.35f;
            public float FastScale { get; set; } = 0.8f;
            public float SlowThresholdPx { get; set; } = 1.5f;
            public float FastThresholdPx { get; set; } = 6.0f;
        }

        public sealed class JitterAmplitudeConfig
        {
            public JitterAxisAmplitude Eye { get; set; } = new() { AmpX = 0.04f, AmpY = 0.04f, ScaleMin = 0.6f, ScaleMax = 1.4f };
            public JitterAxisAmplitude Head { get; set; } = new() { AmpX = 0.015f, AmpY = 0.015f, ScaleMin = 0.6f, ScaleMax = 1.2f };
            public JitterAxisAmplitude Body { get; set; } = new() { AmpX = 0.06f, AmpY = 0.04f, ScaleMin = 0.2f, ScaleMax = 0.8f };
            public float BiasToDelta { get; set; } = 0.6f;
            public float IdleScale { get; set; } = 0.3f;
        }

        public sealed class JitterAxisAmplitude
        {
            public float AmpX { get; set; } = 0.0f;
            public float AmpY { get; set; } = 0.0f;
            public float ScaleMin { get; set; } = 1.0f;
            public float ScaleMax { get; set; } = 1.0f;
        }

        public sealed class JitterSpringConfig
        {
            public JitterAxisSpring Eye { get; set; } = new() { Strength = 60.0f, Damping = 14.0f };
            public JitterAxisSpring Head { get; set; } = new() { Strength = 28.0f, Damping = 10.0f };
            public JitterAxisSpring Body { get; set; } = new() { Strength = 8.0f, Damping = 6.0f };
        }

        public sealed class JitterAxisSpring
        {
            public float Strength { get; set; } = 0.0f;
            public float Damping { get; set; } = 0.0f;
        }

        public sealed class SmartConfig
        {
            public bool Enabled { get; set; } = true;
            public double IdleAfterSeconds { get; set; } = 5.0;
            public double KeepAliveHz { get; set; } = 2.0;
            public double IdleComputeHz { get; set; } = 30.0;
            public double MovementThresholdPx { get; set; } = 0.5;
            public double AfkAfterSeconds { get; set; } = 60.0;
        }

        public sealed class DeltaModeConfig
        {
            public RawInputConfig RawInput { get; set; } = new();
            public bool Enabled { get; set; } = true;
            public bool UseFullscreen { get; set; } = true;
            public bool UseWindowedFocus { get; set; } = true;
            public float CenterRadiusPx { get; set; } = 8.0f;
            public double CenterHoldSeconds { get; set; } = 1.5;
            public double HiddenCursorDelaySeconds { get; set; } = 3.0;
            public float Scale { get; set; } = 1.0f;
            public float ReturnSpeed { get; set; } = 0.12f;
            public float MaxOffset { get; set; } = 1.0f;
            public float MovementThresholdPx { get; set; } = 0.5f;
            public float Smoothing { get; set; } = 0.35f;
            public float SpringHoldFactor { get; set; } = 0.5f;
            public float EyeOffsetScale { get; set; } = 1.0f;
            public float RadiusOverridePx { get; set; } = 320.0f;
            public List<string> RadiusWindowTitles { get; set; } = new();
        }

        public sealed class RawInputConfig
        {
            public bool Enabled { get; set; } = true;
            public bool PreferRawDelta { get; set; } = true;
        }

        public sealed class AnimationsConfig
        {
            public bool Enabled { get; set; } = false;
            public BounceDvdConfig BounceDvd { get; set; } = new();
            public SleepAnimConfig Sleep { get; set; } = new();
            public WakeAnimConfig Wake { get; set; } = new();
            public DizzyAnimConfig Dizzy { get; set; } = new();
        }

        public sealed class SleepAnimConfig
        {
            public bool Enabled { get; set; } = true;
            public bool Exclusive { get; set; } = true;
            public bool WakeOnEnd { get; set; } = true;
            public SleepPoseConfig Sleep { get; set; } = new();
            public BreathingConfig Breathing { get; set; } = new();
        }

        public sealed class SleepPoseConfig
        {
            public float FadeInSeconds { get; set; } = 1.0f;
            public SleepHeadConfig Head { get; set; } = new();
            public SleepEyeConfig Eye { get; set; } = new();
            public SleepBodyConfig Body { get; set; } = new();
        }

        public sealed class SleepHeadConfig
        {
            public float TiltX { get; set; } = 0.5f;
            public float TiltY { get; set; } = -1.0f;
            public float TiltZ { get; set; } = -2.0f;
        }

        public sealed class SleepEyeConfig
        {
            public float Smile { get; set; } = 1.0f;
        }

        public sealed class SleepBodyConfig
        {
            public float OffsetY { get; set; } = -0.5f;
            public float OffsetZ { get; set; } = -1.0f;
        }

        public sealed class WakeAnimConfig
        {
            public bool Enabled { get; set; } = true;
            public bool Exclusive { get; set; } = true;
            public float EaseOutSeconds { get; set; } = 1.0f;
            public WakeJoltConfig WakeJolt { get; set; } = new();
        }

        public sealed class WakeJoltConfig
        {
            public bool Enabled { get; set; } = true;
            public float JumpBodyY { get; set; } = 0.8f;
            public float JumpSeconds { get; set; } = 0.15f;
            public float HoldSeconds { get; set; } = 0.4f;
            public float HeadTiltX { get; set; } = -0.3f;
            public float HeadTiltY { get; set; } = 0.0f;
            public float HeadTiltZ { get; set; } = 0.2f;
            public float EyeX { get; set; } = 0.6f;
            public float EyeY { get; set; } = 0.4f;
            public float ComposeSeconds { get; set; } = 1.0f;
            public float ComposedHeadX { get; set; } = 0.3f;
            public float ComposedHeadY { get; set; } = 0.0f;
            public float ComposedHeadZ { get; set; } = -0.1f;
            public float ComposedBlink { get; set; } = 1.0f;
            public float BreatheSeconds { get; set; } = 2.0f;
        }

        public sealed class BreathingConfig
        {
            public float ExhaleRatio { get; set; } = 0.6f;
            public float Amplitude { get; set; } = 0.08f;
            public float Hz { get; set; } = 0.2f;
        }

        public sealed class DizzyAnimConfig
        {
            public bool Enabled { get; set; } = true;
            public bool Exclusive { get; set; } = true;
            public DizzyInConfig DizzyIn { get; set; } = new();
            public DizzyOutConfig DizzyOut { get; set; } = new();
        }

        public sealed class DizzyInConfig
        {
            public float SpinUpSeconds { get; set; } = 1.0f;
            public float BlendAlpha { get; set; } = 0.6f;
            public float EyeRadius { get; set; } = 0.45f;
            public float EyeSpinHz { get; set; } = 0.9f;
            public float HeadRadius { get; set; } = 0.28f;
            public float HeadSpinHz { get; set; } = 0.45f;
            public float BodyXYAmplitudeX { get; set; } = 0.02f;
            public float BodyXYAmplitudeY { get; set; } = 0.015f;
            public float BodyXYSpinHz { get; set; } = 0.2f;
            public float BodyMin { get; set; } = -0.2f;
            public float BodyMax { get; set; } = 0.2f;
            public float BodySpinHz { get; set; } = 0.15f;
        }

        public sealed class DizzyOutConfig
        {
            public float EaseOutSeconds { get; set; } = 1.0f;
        }

        public sealed class BounceDvdConfig
        {
            public bool Enabled { get; set; } = false;
            public bool Exclusive { get; set; } = false;
            public bool Stackable { get; set; } = true;
            public string Trigger { get; set; } = "smartIdle";
            public float SpeedX { get; set; } = 0.003f;
            public float SpeedY { get; set; } = 0.003f;
            public float SpinSpeed { get; set; } = 45.0f;
            public float BounceScale { get; set; } = -100f;
            public float ResetScale { get; set; } = -100f;
        }

        public sealed class ProfileConfig
        {
            public string ModelName { get; set; } = string.Empty;
            public Dictionary<string, HotkeyConfig> Hotkeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        public sealed class HotkeyConfig
        {
            public string Id { get; set; } = string.Empty;
            public string Action { get; set; } = string.Empty;
            public List<string> Expressions { get; set; } = new();
            public HotkeyExpressionTiming Expression { get; set; } = new();
            public List<string> Triggers { get; set; } = new();
            public List<string> ResetTriggers { get; set; } = new();
            public double DurationSeconds { get; set; } = 0;
            public double CooldownSeconds { get; set; } = 0;
        }

        public sealed class HotkeyExpressionTiming
        {
            public double? DurationSeconds { get; set; }
            public double? CooldownSeconds { get; set; }
        }
    }
}
