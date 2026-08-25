namespace MikuDanceProject.Core;

// 运行时全局详细日志开关，由 DancePluginConfig 在初始化与配置变更时同步。
// 供 Runtime 层（DanceController / UnityAssetBundleLoader / MikuFacialExpressionController）读取，
// 避免将 ConfigEntry 直接暴露给 Runtime 层。
internal static class VerboseLogState
{
    private static volatile bool _enabled;

    public static bool Enabled => _enabled;

    public static void Update(bool enabled)
    {
        _enabled = enabled;
    }
}
