using LocalCraft.Core.Settings;

namespace LocalCraft.App.Infrastructure;

/// <summary>
/// 动画偏好的运行时快照。页面动画和入场动画在开始时读取这里。
/// </summary>
public static class MotionPreferences
{
    public static bool Enabled { get; private set; } = true;

    public static MotionLevel Level { get; private set; } = MotionLevel.Normal;

    public static int PageTransitionMilliseconds => !Enabled
        ? 0
        : Level switch
        {
            MotionLevel.Low => 120,
            MotionLevel.Normal => 220,
            _ => 320
        };

    public static bool EntranceAnimationsEnabled => Enabled && Level != MotionLevel.Low;

    public static void Apply(AppSettings settings)
    {
        Enabled = settings.AnimationsEnabled;
        Level = settings.MotionLevel;
    }
}
