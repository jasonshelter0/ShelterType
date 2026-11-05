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
        ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var splash=new Window{Width=1200,Height=900,WindowStartupLocation=WindowStartupLocation.CenterScreen,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,Background=(System.Windows.Media.Brush)Resources["Bg"],Title="ShelterType"};
        var logo=new System.Windows.Controls.StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        logo.Children.Add(new BrandMark{Width=88,Height=88,HorizontalAlignment=HorizontalAlignment.Center});
        var name=new System.Windows.Controls.TextBlock{Text="ShelterType",FontSize=28,Margin=new Thickness(0,20,0,0),HorizontalAlignment=HorizontalAlignment.Center};name.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,"Ink");logo.Children.Add(name);splash.Content=logo;
        splash.ContentRendered+=(_,_)=>Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(()=>{
            try{
                var window=new MainWindow{Opacity=0};MainWindow=window;
                EventHandler? showMain=null;showMain=(_,_)=>{window.ContentRendered-=showMain;window.Opacity=1;splash.Close();ShutdownMode=ShutdownMode.OnMainWindowClose;window.Activate();};window.ContentRendered+=showMain;
                window.Show();
            }catch(Exception error){splash.Close();MessageBox.Show(error.Message,"ShelterType",MessageBoxButton.OK,MessageBoxImage.Error);Shutdown(1);}
        }));
        splash.Show();
        _ = Task.Run(() => DictionaryMigration.CleanLegacy(AppContext.BaseDirectory));
    }
}
