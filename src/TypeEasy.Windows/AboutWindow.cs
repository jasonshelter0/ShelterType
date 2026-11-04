using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace TypeEasy.Windows;

public sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        Title = UiText.T("关于 ShelterType", "About ShelterType", "À propos de ShelterType");
        Width = 570; Height = 560; MinWidth = 470; MinHeight = 430; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 14;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(UiText.Culture.IetfLanguageTag);
        SetResourceReference(BackgroundProperty, "Bg"); SetResourceReference(ForegroundProperty, "Ink");
        SourceInitialized += (_, _) => ApplyTitleTheme(this); Activated += (_, _) => ApplyTitleTheme(this);
        var root = new DockPanel { Margin = new Thickness(28) }; Content = root;
        var done = new Button { Content = UiText.T("关闭", "Close", "Fermer"), HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 84, Margin = new Thickness(0, 18, 0, 0) };
        done.Click += (_, _) => Close(); DockPanel.SetDock(done, Dock.Bottom); root.Children.Add(done);
        var body = new StackPanel(); root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var brand = new StackPanel { Orientation = Orientation.Horizontal };
        brand.Children.Add(new BrandMark { Width = 64, Height = 64, Margin = new Thickness(0, 0, 14, 0) });
        brand.Children.Add(new TextBlock { Text = "ShelterType", FontFamily = new FontFamily("Segoe UI"), FontSize = 27, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        body.Children.Add(brand);
        var assembly = typeof(AboutWindow).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? assembly.GetName().Version?.ToString(3) ?? UiText.T("未知", "Unknown", "Inconnue");
        var versionLabel = new TextBlock { Text = UiText.Format("版本 {0}", "Version {0}", "Version {0}", version), Margin = new Thickness(0, 5, 0, 20), FontSize = 12 };
        versionLabel.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); body.Children.Add(versionLabel);
        void Paragraph(string text) => body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 23, Margin = new Thickness(0, 0, 0, 14) });
        Paragraph(UiText.T("独立重构的 Windows 打字练习工具，并非金山官方产品。", "An independently rebuilt typing tutor for Windows. This is not an official Kingsoft product.", "Outil d’entraînement à la frappe pour Windows, développé indépendamment. Il ne s’agit pas d’un produit officiel de Kingsoft."));
        Paragraph(UiText.T("原生 WPF 界面，练习可离线使用，不使用浏览器内核。", "Native WPF interface with offline practice and no browser engine.", "Interface WPF native, exercices hors ligne, sans moteur de navigateur."));
        Paragraph(UiText.T("感谢金山打字通 2008 的原作者与资料贡献者。原版课程、词库和教学图片由项目维护者取得授权后随软件提供，版权归原权利人所有。完整致谢见 ACKNOWLEDGEMENTS.md。", "Thanks to the original authors and contributors of Kingsoft TypeEasy 2008. Its lessons, dictionaries and teaching images are included with permission obtained by the project maintainer. Copyright remains with the respective rights holders. See ACKNOWLEDGEMENTS.md.", "Merci aux auteurs et contributeurs de Kingsoft TypeEasy 2008. Les cours, dictionnaires et illustrations sont inclus avec l’autorisation obtenue par le responsable du projet. Les droits restent à leurs titulaires. Voir ACKNOWLEDGEMENTS.md."));
        Paragraph(UiText.T("切换界面语言不会翻译课程正文、汉字或五笔内容。", "Changing the interface language does not translate lesson texts, Chinese characters or Wubi content.", "Changer la langue de l’interface ne traduit pas les textes des cours, les caractères chinois ni le contenu Wubi."));
        Paragraph(UiText.T("进度、成绩和设置保存在程序旁的 UserData 文件夹。备份时先关闭程序，再复制整个文件夹。上下文分析仅描述观察到的关联。", "Progress, results and settings are stored locally in the UserData folder beside the application. Close the app before copying this folder for backup. Context analysis describes observed associations only.", "La progression, les résultats et les paramètres sont conservés localement dans le dossier UserData, à côté de l’application. Fermez l’application avant de copier ce dossier pour une sauvegarde. L’analyse du contexte décrit uniquement les associations observées."));
        Paragraph(UiText.T("检查更新时连接 GitHub，不上传练习记录。", "Update checks connect to GitHub without uploading practice records.", "La recherche de mises à jour contacte GitHub sans transmettre les données d’exercice."));
        var repository = new Button { Content = "GitHub / ShelterType", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        repository.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://github.com/jasonshelter0/ShelterType") { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception) { MessageBox.Show(this, "https://github.com/jasonshelter0/ShelterType", "ShelterType"); }
        };
        body.Children.Add(repository);
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    internal static void ApplyTitleTheme(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle; if (handle == IntPtr.Zero) return;
        var color = (window.TryFindResource("Bg") as SolidColorBrush)?.Color ?? Colors.Black;
        int dark = color.R + color.G + color.B < 384 ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref dark, 4);
    }
}
