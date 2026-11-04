using System.Windows;
namespace TypeEasy.Windows;
public partial class MainWindow
{
    private DictionaryWindow? dictionaryWindow;
    private void Dictionary_Click(object sender, RoutedEventArgs e) => OpenDictionary();
    private void OpenDictionary()
    {
        Guard(() => session?.Pause());
        if (dictionaryWindow is null)
        {
            dictionaryWindow = new DictionaryWindow(session?.Course.Category == "五笔" ? session.CurrentItem?.Prompt ?? "" : "") { Owner = this };
            dictionaryWindow.Closed += (_, _) => { dictionaryWindow = null; InputBox.Focus(); };
            dictionaryWindow.Show();
        }
        dictionaryWindow.Activate();
    }
}
