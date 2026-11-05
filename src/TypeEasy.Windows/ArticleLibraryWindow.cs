using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using TypeEasy.Core;
namespace TypeEasy.Windows;
public sealed class ArticleLibraryWindow:Window
{
    private readonly string root,dataRoot;
    private readonly ListBox list=new(){DisplayMemberPath="Title"};
    private readonly TextBlock preview=StatsUi.Text("",15),status=StatsUi.Text("",12,"Muted");
    private List<Course> available;
    public ArticleLibraryWindow(string userRoot,string courseRoot,List<Course> courses){root=userRoot;dataRoot=courseRoot;available=courses;StatsUi.Window(this,UiText.T("文章管理","Text library","Bibliothèque"),900,700);
        var layout=new DockPanel{Margin=new Thickness(24)};Content=layout;
        var actions=new WrapPanel{Margin=new Thickness(0,0,0,15)};
        void Action(string text,RoutedEventHandler run)=>actions.Children.Add(StatsUi.Button(text,run));
        Action(UiText.T("导入 TXT","Import TXT","Importer TXT"),(_,_)=>Guard(()=>{var picker=new Microsoft.Win32.OpenFileDialog{Filter="Text (*.txt)|*.txt",Multiselect=true};if(picker.ShowDialog(this)!=true)return;var folder=Path.Combine(root,"Articles");Directory.CreateDirectory(folder);foreach(var file in picker.FileNames){if(new FileInfo(file).Length>1024*1024)throw new IOException("Text file too large.");var target=Path.Combine(folder,Path.GetFileName(file));if(File.Exists(target))target=Path.Combine(folder,Path.GetFileNameWithoutExtension(file)+"-"+Guid.NewGuid().ToString("N")[..6]+".txt");File.Copy(file,target);}Reload();}));
        Action(UiText.T("移除选中文章","Remove selected","Retirer le texte"),(_,_)=>Guard(()=>{if(list.SelectedItem is Course c){ArticleLibrary.SetHidden(root,c.Id,true);Reload();}}));
        Action(UiText.T("恢复已移除文章","Restore removed","Restaurer"),(_,_)=>Guard(()=>{foreach(var id in ArticleLibrary.Hidden(root))ArticleLibrary.SetHidden(root,id,false);Reload();}));
        Action(UiText.T("打开原文目录","Open source folder","Dossier source"),(_,_)=>Guard(OpenSource));
        Action(UiText.T("我的文章目录","My text folder","Mes textes"),(_,_)=>Guard(()=>{var path=Path.Combine(root,"Articles");Directory.CreateDirectory(path);Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}));
        DockPanel.SetDock(actions,Dock.Top);layout.Children.Add(actions);DockPanel.SetDock(status,Dock.Bottom);layout.Children.Add(status);
        var columns=new Grid();columns.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(280)});columns.ColumnDefinitions.Add(new ColumnDefinition());columns.Children.Add(list);var scroll=new ScrollViewer{Content=preview,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(20,0,0,0)};Grid.SetColumn(scroll,1);columns.Children.Add(scroll);layout.Children.Add(columns);
        list.SelectionChanged+=(_,_)=>{if(list.SelectedItem is Course c)preview.Text=string.Concat(c.Items.Select(i=>i.Expected));};list.ItemsSource=available.Where(ArticleLibrary.IsArticle).ToArray();
    }
    private void Reload(){available=ArticleLibrary.Apply(CourseCatalog.Load(dataRoot),root);list.ItemsSource=available.Where(ArticleLibrary.IsArticle).ToArray();status.Text=UiText.T("已保存。","Saved.","Enregistré.");}
    private void OpenSource(){if(list.SelectedItem is not Course c)return;string path;
        if(c.Id.StartsWith("user-article:"))path=Path.Combine(root,"Articles",Path.GetFileName(c.Id[13..]));
        else if(c.Origin=="public-domain")path=Path.Combine(dataRoot,"Courses","public-articles.json");
        else {var folder=Path.GetFullPath(Path.Combine(dataRoot,"Data"));path=Path.GetFullPath(Path.Combine(folder,c.Id.Split('#')[0]));if(!path.StartsWith(folder+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid source path.");}
        if(File.Exists(path)){var start=new ProcessStartInfo("explorer.exe"){UseShellExecute=true};start.ArgumentList.Add("/select,");start.ArgumentList.Add(path);Process.Start(start);}else status.Text=UiText.T("原文件不可用。","Source file unavailable.","Source indisponible.");
    }
    private void Guard(Action action){try{action();}catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception){status.Text=e.Message;}}
}
