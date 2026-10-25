
namespace LiveryGallery.Services;

/// <summary>The in-game navigation timing the user set in Settings</summary>
internal static class NavigationSettings
{
    public static NavigationTiming TimingFrom(AppSettingsData settings) => NavigationTiming.Create(
        settings.NavigationKeyIntervalMs, settings.NavigationKeyHoldMs, settings.NavigationSwitchDelayMs,
        settings.NavigationTurnDelayMs, settings.NavigationWrapDelayMs,
        settings.NavigationMenuBackDelayMs, settings.NavigationMenuOpenDelayMs);
}
