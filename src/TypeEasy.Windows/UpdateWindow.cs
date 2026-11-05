using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ShelterType.Update;

namespace TypeEasy.Windows;

/// <summary>All network work stays asynchronous; only an explicit download click can install an update.</summary>
public sealed class UpdateWindow : Window
{
    private readonly UpdateClient client = new();
    private readonly Action<ReleaseUpdate?> checkedRelease;
    private readonly Action<PreparedUpdate> install;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 12) };
    private readonly TextBlock detail = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock notes = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 10, 0, 0) };
    private readonly ProgressBar progress = new() { Height = 5, Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed };
    private readonly Button primary = new() { MinWidth = 130, Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button cancel = new() { MinWidth = 85 };
    private CancellationTokenSource? operation;
    private ReleaseUpdate? release;
    private bool busy, closed;

    public UpdateWindow(ReleaseUpdate? initial, Action<ReleaseUpdate?> checkedRelease, Action<PreparedUpdate> install)
    {
        release = initial; this.checkedRelease = checkedRelease; this.install = install;
        Title = UiText.T("ShelterType 更新", "ShelterType updates", "Mises à jour de ShelterType");
        Width = 560; Height = 420; MinWidth = 470; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 14;
        SetResourceReference(BackgroundProperty, "Bg"); SetResourceReference(ForegroundProperty, "Ink");
        SourceInitialized += (_, _) => AboutWindow.ApplyTitleTheme(this);
        var root = new DockPanel { Margin = new Thickness(26) }; Content = root;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        actions.Children.Add(cancel); actions.Children.Add(primary); DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var body = new StackPanel(); root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        body.Children.Add(new TextBlock { Text = "ShelterType", FontSize = 25, FontWeight = FontWeights.SemiBold });
        var current = new TextBlock { Text = UiText.Format("当前版本 {0}", "Current version {0}", "Version actuelle : {0}", MainWindow.CurrentVersion.ToString(3)), FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
        current.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); body.Children.Add(current);
        body.Children.Add(status); body.Children.Add(progress);
        detail.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); body.Children.Add(detail);
        var changelog = new Expander { Header = UiText.T("版本说明", "Release notes", "Notes de version"), Content = notes, Margin = new Thickness(0, 10, 0, 0) };
        changelog.SetResourceReference(ForegroundProperty, "Ink"); body.Children.Add(changelog);
        primary.Click += async (_, _) => { if (release is null) await CheckAsync(); else await DownloadAsync(); };
        cancel.Click += (_, _) => { if (busy) operation?.Cancel(); else Close(); };
        Closed += (_, _) => { closed = true; operation?.Cancel(); client.Dispose(); };
        Loaded += async (_, _) => { if (release is null) await CheckAsync(); else ShowRelease(); };
        ShowRelease();
    }

    private void ShowRelease()
    {
        notes.Text = release?.Notes is { } text ? text[..Math.Min(text.Length, 12000)] : "";
        detail.Text = release is null ? "" : UiText.T("下载并校验完成后，将保存进度、更新程序并重新打开。", "After download and verification, progress is saved and the app updates and restarts.", "Après téléchargement et vérification, la progression est enregistrée, puis l’application est mise à jour et redémarre.");
        if (release is not null)
            status.Text = UiText.Format("发现新版本 {0}", "Version {0} is available", "La version {0} est disponible", release.Version.ToString(3));
        primary.Content = release is null ? UiText.T("重新检查", "Check again", "Vérifier à nouveau") : UiText.T("下载并更新", "Download and update", "Télécharger et installer");
        cancel.Content = UiText.T("关闭", "Close", "Fermer");
    }

    private async Task CheckAsync()
    {
        await RunAsync(async token =>
        {
            status.Text = UiText.T("正在检查更新…", "Checking for updates…", "Recherche de mises à jour…");
            progress.IsIndeterminate = true;
            release = await client.CheckAsync(MainWindow.CurrentVersion, token);
            if (closed) return;
            checkedRelease(release);
            ShowRelease();
            if (release is null) status.Text = UiText.T("已是最新版本。", "You’re up to date.", "Votre version est à jour.");
        });
    }

    private async Task DownloadAsync()
    {
        if (release is null) return;
        await RunAsync(async token =>
        {
            progress.IsIndeterminate = false; progress.Value = 0;
            status.Text = UiText.T("正在下载…", "Downloading…", "Téléchargement…");
            var report = new Progress<DownloadProgress>(p =>
            {
                if (closed) return;
                progress.Value = p.Fraction;
                status.Text = UiText.Format("正在下载 {0:P0}", "Downloading {0:P0}", "Téléchargement : {0:P0}", p.Fraction);
            });
            var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShelterType", "Updates");
            var prepared = await client.DownloadAsync(release, cache, report, token, AppContext.BaseDirectory);
            token.ThrowIfCancellationRequested();
            if (closed) return;
            status.Text = UiText.T("正在保存并重新启动…", "Saving and restarting…", "Enregistrement et redémarrage…");
            install(prepared);
        });
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (busy || closed) return;
        busy = true; primary.IsEnabled = false; progress.Visibility = Visibility.Visible;
        cancel.Content = UiText.T("取消", "Cancel", "Annuler");
        operation = new CancellationTokenSource();
        try { await action(operation.Token); }
        catch (OperationCanceledException)
        {
            if (!closed) status.Text = operation.IsCancellationRequested
                ? UiText.T("已取消。", "Cancelled.", "Annulé.")
                : UiText.T("连接超时，请重试。", "The connection timed out. Please retry.", "Le délai de connexion est dépassé. Réessayez.");
        }
        catch (Exception error)
        {
            if (!closed)
            {
                status.Text = error is InvalidDataException
                    ? UiText.T("更新包验证失败，当前版本未更改。", "Update verification failed. The current version is unchanged.", "La vérification a échoué. La version actuelle reste inchangée.")
                    : error is HttpRequestException
                    ? UiText.T("无法连接更新服务，请稍后重试。", "Unable to reach the update service. Please retry later.", "Le service de mise à jour est inaccessible. Réessayez plus tard.")
                    : UiText.T("无法完成更新。请确认程序目录可写，然后重试。", "Unable to update. Make sure the app folder is writable, then retry.", "Mise à jour impossible. Vérifiez les droits d’écriture du dossier de l’application, puis réessayez.");
                // Useful local diagnostics without exposing technical details throughout the UI.
                try
                {
                    var log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShelterType", "Updates");
                    Directory.CreateDirectory(log); File.AppendAllText(Path.Combine(log, "errors.log"), $"{DateTimeOffset.UtcNow:O} {error}\n");
                }
                catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
            }
        }
        finally
        {
            operation.Dispose(); operation = null; busy = false;
            if (!closed)
            {
                primary.IsEnabled = true; progress.Visibility = Visibility.Collapsed;
                primary.Content = release is null ? UiText.T("重新检查", "Check again", "Vérifier à nouveau") : UiText.T("下载并更新", "Download and update", "Télécharger et installer");
                cancel.Content = UiText.T("关闭", "Close", "Fermer");
            }
        }
    }
}
