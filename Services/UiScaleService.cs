using System;

namespace POTimeTracker.Services
{
    /// <summary>
    /// Global UI scale applied to every window via LayoutTransform, driven by the
    /// user-configurable font size setting. Keeping this centralized lets all open
    /// windows react live when the value changes in Settings.
    /// </summary>
    public static class UiScaleService
    {
        public const double MinScale = 0.85;
        public const double MaxScale = 1.4;
        public const double DefaultScale = 1.0;

        public static double Current { get; private set; } = DefaultScale;

        public static event Action<double>? ScaleChanged;

        public static void Initialize()
        {
            var config = CredentialService.LoadConfig();
            double raw = (config != null && config.FontScale > 0) ? config.FontScale : DefaultScale;
            Current = Clamp(raw);
        }

        public static void SetScale(double scale)
        {
            var clamped = Clamp(scale);
            if (Math.Abs(clamped - Current) < 0.001) return;
            Current = clamped;
            ScaleChanged?.Invoke(Current);
        }

        public static double Clamp(double value) => Math.Max(MinScale, Math.Min(MaxScale, value));
    }
}
