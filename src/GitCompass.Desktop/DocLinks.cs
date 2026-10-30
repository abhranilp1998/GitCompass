using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using GitCompass.Domain;

namespace GitCompass.Desktop;

/// <summary>"Learn more on git-scm.com ↗" pill buttons. Only https links open, in the default browser.</summary>
public static class DocLinks
{
    public static Button Chip(string url, Action<string>? onError = null)
    {
        var button = new Button { Content = GitGlossary.LinkText(url), ToolTip = url };
        button.SetResourceReference(FrameworkElement.StyleProperty, "LinkChip");
        var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "the web";
        AutomationProperties.SetName(button, "Official documentation on " + host + " (opens in your browser)");
        AutomationProperties.SetHelpText(button, url);
        button.Click += (_, _) => Open(url, onError);
        return button;
    }

    public static void Open(string url, Action<string>? onError = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) { onError?.Invoke("Only https links can be opened."); return; }
        try { Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true }); }
        catch (Exception e) { onError?.Invoke("Your browser couldn't be opened: " + e.Message); }
    }
}
