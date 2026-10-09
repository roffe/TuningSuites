using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommonSuite;

namespace T7App.ViewModels;

/// <summary>frmMain's update check (m_msiUpdater): on startup and from Help → Check for updates.</summary>
public partial class MainWindowViewModel
{
    /// <summary>The status bar's update field (barUpdateText), e.g. "No new version(s) found...".</summary>
    [ObservableProperty]
    private string _updateText = "";

    /// <summary>T7Suite's releases (upstream's tag scheme, Directory.Build.props versions T7App by the same tags).</summary>
    public const string ReleaseTagPrefix = "T7suite_v";

    /// <summary>This build's file version, the T7suite_v tag padded to four parts; 0.0.0.0 without a tag.</summary>
    public static Version BuildVersion => typeof(MainWindowViewModel).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

    /// <summary>
    /// The newest T7Suite release when it's newer than this build, else null; the result goes into the status bar as
    /// msiupdater's messages did. A failed check only says so there.
    /// </summary>
    public async Task<Release?> CheckForUpdatesAsync()
    {
        try
        {
            using var http = new HttpClient(new HttpClientHandler { DefaultProxyCredentials = CredentialCache.DefaultNetworkCredentials })
            {
                Timeout = TimeSpan.FromSeconds(10),
            };
            // GitHub's API refuses requests without a user agent
            http.DefaultRequestHeaders.UserAgent.ParseAdd("T7Suite/" + BuildVersion);
            Release? newest = UpdateCheck.Newest(await http.GetStringAsync(UpdateCheck.ReleasesApi), ReleaseTagPrefix);
            Version current = UpdateCheck.Pad(BuildVersion);
            if (newest != null && newest.Version > current)
            {
                UpdateText = "A newer version is available: " + newest.Version;
                return newest;
            }
            UpdateText = newest != null && newest.Version < current ? "Versionnumber is too high: " + current : "No new version(s) found...";
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            UpdateText = "Update check failed: " + e.Message;
        }
        return null;
    }
}
