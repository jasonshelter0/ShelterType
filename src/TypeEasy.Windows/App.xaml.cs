using System.Windows;
namespace TypeEasy.Windows;
public partial class App : Application
{
    public App()
    {
        var icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/ShelterType;component/Assets/ShelterType.ico"));
        icon.Freeze();
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, args) =>
        {
            if (sender is Window window && ReferenceEquals(args.OriginalSource, window) && window.Icon is null)
                window.SetCurrentValue(Window.IconProperty, icon);
        }));
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        MainWindow = new MainWindow();
        MainWindow.Show();
        _ = Task.Run(() => DictionaryMigration.CleanLegacy(AppContext.BaseDirectory));
    }
}
