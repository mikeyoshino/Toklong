using Toklong.Mobile.Core;

namespace Toklong.Mobile.Services;

public sealed class StartupMotionPreference
    : IStartupMotionPreference
{
    public bool IsReducedMotionEnabled
    {
        get
        {
#if IOS || MACCATALYST
            return UIKit.UIAccessibility.IsReduceMotionEnabled;
#elif ANDROID
            var resolver =
                Android.App.Application.Context.ContentResolver;
            var scale = Android.Provider.Settings.Global.GetFloat(
                resolver,
                Android.Provider.Settings.Global.AnimatorDurationScale,
                1f);
            return scale == 0f;
#else
            return false;
#endif
        }
    }
}
