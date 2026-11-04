using System.Diagnostics;
using System.IO;
using System.Windows;
using ShelterType.Update;

namespace TypeEasy.Windows;

public partial class MainWindow
{
    private readonly CancellationTokenSource updateLifetime = new();
    private ReleaseUpdate? availableUpdate;
    private bool updateNoticeDismissed;
    internal static Version CurrentVersion => typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 0, 0);

    private async Task CheckForUpdatesInBackgroundAsync()
    {
        if (!prefs.CheckForUpdates || Environment.GetCommandLineArgs().Any(a => a is "--test-profile" or "--no-update-check")) return;
        var elapsed = DateTimeOffset.UtcNow - (prefs.LastUpdateCheckUtc ?? DateTimeOffset.MinValue);
        if (elapsed >= TimeSpan.Zero && elapsed < TimeSpan.FromHours(24)) return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), updateLifetime.Token);
            if (!prefs.CheckForUpdates) return;
            prefs.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
            SavePrefs();
            using var client = new UpdateClient();
            var release = await client.CheckAsync(CurrentVersion, updateLifetime.Token);
            if (updateLifetime.IsCancellationRequested) return;
            availableUpdate = release;
            RefreshUpdateNotice();
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // An unavailable update service must never interrupt offline practice.
            // The manual update screen reports connection and package errors explicitly.
        }
    }

    private void RefreshUpdateNotice()
    {
        if (UpdateNotice is null) return;
        UpdateNotice.Visibility = availableUpdate is not null && !updateNoticeDismissed ? Visibility.Visible : Visibility.Collapsed;
        if (availableUpdate is not null)
            UpdateNoticeText.Text = UiText.Format("ShelterType {0} 可更新", "ShelterType {0} is available", "ShelterType {0} est disponible", availableUpdate.Version.ToString(3));
        OpenUpdateButton.Content = UiText.T("查看更新", "View update", "Voir la mise à jour");
        DismissUpdateButton.Content = UiText.T("稍后", "Later", "Plus tard");
    }

    private void DismissUpdate_Click(object sender, RoutedEventArgs e)
    {
        updateNoticeDismissed = true;
        RefreshUpdateNotice();
    }

    private void OpenUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (session is not null) Guard(() => session.Pause());
        var window = new UpdateWindow(availableUpdate, release =>
        {
            availableUpdate = release;
            prefs.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
            SavePrefs();
            RefreshUpdateNotice();
        }, prepared =>
        {
            // Save synchronously before handing over. A save failure aborts the update.
            session?.Pause();
            SavePrefsCore();
            using var process = Process.GetCurrentProcess();
            UpdateInstaller.Start(prepared, AppContext.BaseDirectory, process.Id, process.StartTime.ToUniversalTime(), UiText.Language);
            Application.Current.Shutdown();
        }) { Owner = this };
        window.ShowDialog();
    }
}
