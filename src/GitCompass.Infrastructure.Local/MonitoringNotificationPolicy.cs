namespace GitCompass.Infrastructure.Local;

/// <summary>Suppresses in-app monitoring notices without changing permission or scheduled fetches.</summary>
public static class MonitoringNotificationPolicy
{
    public static bool ShouldNotify(AppSettings settings, DateTimeOffset instant)
    {
        if (settings.MuteMonitoringNotifications) return false;
        if (settings.QuietHoursStart is not int start || settings.QuietHoursEnd is not int end) return true;
        var hour = instant.ToLocalTime().Hour;
        var quiet = start < end ? hour >= start && hour < end : hour >= start || hour < end;
        return !quiet;
    }
}
