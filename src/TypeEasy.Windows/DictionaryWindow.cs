using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Wubi86.Core;
using Wubi86.Models;
namespace TypeEasy.Windows;

public sealed class DictionaryWindow : Window
{
    private readonly TextBox query = new();
    private readonly ListBox results = new();
    private readonly TextBlock details = new() { TextWrapping=TextWrapping.Wrap, FontSize=22 };
    private readonly Image glyph = new() { Height=180, Stretch=System.Windows.Media.Stretch.Uniform };
    private readonly DictionaryGlyphStore glyphStore = new(Path.Combine(AppContext.BaseDirectory,"Dictionary"));
    private int glyphRequest;
    private bool closed;
    private SearchService? search;
    private SearchIndex? index;
    private readonly ComboBox mode = StatsUi.Choice(220);
    private readonly CheckBox favoritesOnly = new();
    private readonly Button favorite = new();
    private readonly string favoritesPath = Path.Combine(AppContext.BaseDirectory,"UserData","Dictionary","favorites.json");
    private HashSet<string> ReadFavorites()
    {
        if(!File.Exists(favoritesPath)) return new(StringComparer.Ordinal);
        return System.Text.Json.JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(favoritesPath))
            ?? throw new InvalidDataException("Invalid favorites file.");
    }
    private void ToggleFavorite()
    {
        if(results.SelectedItem is not SearchResult entry)return;
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(favoritesPath)!);
            using var locked = new FileStream(favoritesPath+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            var values=ReadFavorites();
            if(!values.Remove(entry.DisplayText)) values.Add(entry.DisplayText);
            File.WriteAllText(favoritesPath+".tmp",System.Text.Json.JsonSerializer.Serialize(values));
            File.Move(favoritesPath+".tmp",favoritesPath,true);
            Search();
        } catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException) { details.Text=e.Message; }
    }
    public DictionaryWindow(string initial)
    {
        Title=UiText.T("五笔86词典","Wubi 86 dictionary","Dictionnaire Wubi 86");
        StatsUi.Window(this, Title, 720, 620); MinWidth=480; MinHeight=400;
        SetResourceReference(BackgroundProperty,"Bg"); SetResourceReference(ForegroundProperty,"Ink");
        var layout=new DockPanel { Margin=new Thickness(20) };
        foreach(var label in new[] { UiText.T("全部","All","Tout"), UiText.T("拼音","Pinyin","Pinyin"), UiText.T("五笔编码","Wubi code","Code Wubi") }) mode.Items.Add(label);
        mode.SelectedIndex=0; mode.Margin=new Thickness(0,0,0,8);
        DockPanel.SetDock(mode,Dock.Top); layout.Children.Add(mode);
        mode.SelectionChanged+=(_,_)=>Search();
        var tools = new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(0,0,0,10) };
        favoritesOnly.Content=UiText.T("收藏","Favorites","Favoris");
        favoritesOnly.SetResourceReference(ForegroundProperty,"Ink");
        favoritesOnly.VerticalAlignment=VerticalAlignment.Center;
        favoritesOnly.Checked+=(_,_)=>Search(); favoritesOnly.Unchecked+=(_,_)=>Search();
        favorite.Content=UiText.T("收藏 / 取消收藏","Toggle favorite","Ajouter / retirer");
        favorite.Margin=new Thickness(12,0,12,0); favorite.Click+=(_,_)=>ToggleFavorite();
        var roots=new Button { Content=UiText.T("字根表","Root chart","Table des radicaux") };
        roots.Click+=(_,_)=>new RootKeyboardWindow { Owner=this }.Show();
        tools.Children.Add(favoritesOnly); tools.Children.Add(favorite); tools.Children.Add(roots);
        DockPanel.SetDock(tools,Dock.Top); layout.Children.Add(tools);
        query.Text=initial;
        query.ToolTip=UiText.T("汉字、词组、拼音或五笔编码","Character, phrase, pinyin or Wubi code","Caractère, expression, pinyin ou code Wubi");
        System.Windows.Automation.AutomationProperties.SetName(query,query.ToolTip.ToString());
        query.Margin=new Thickness(0,0,0,12);
        DockPanel.SetDock(query,Dock.Top); layout.Children.Add(query);
        var bottom=new StackPanel { Margin=new Thickness(0,12,0,0) };
        bottom.Children.Add(details); bottom.Children.Add(new Border { Background=System.Windows.Media.Brushes.White, Child=glyph, Margin=new Thickness(0,8,0,0) });
        DockPanel.SetDock(bottom,Dock.Bottom); layout.Children.Add(bottom);
        results.DisplayMemberPath=nameof(SearchResult.DisplayText); layout.Children.Add(results); Content=layout;
        query.TextChanged+=(_,_)=>Search();
        results.SelectionChanged+=(_,_)=>ShowEntry();
        Loaded+=async(_,_)=>{
            try {
                index=new SearchIndex(await DictionaryLoader.LoadAsync(Path.Combine(AppContext.BaseDirectory,"Dictionary","Data")));
                search=new SearchService(index);
                Search(); query.Focus(); query.SelectAll();
            } catch(Exception e) when(e is IOException or UnauthorizedAccessException) { details.Text=e.Message; }
        };
        Closed+=(_,_)=>{closed=true;glyphRequest++; _=Task.Run(()=>glyphStore.Dispose());};
        Activated+=(_,_)=>ShowEntry();
        PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape)Close();};
    }
    private void Search()
    {
        if(search is null)return;
        var text=query.Text.Trim();
        results.ItemsSource=text.Length==0 ? Array.Empty<SearchResult>() : mode.SelectedIndex==0 ? search.Search(text,100)
            : (mode.SelectedIndex==1 ? index!.FindByPinyin(text,100) : index!.FindByWubi(text,100))
                .Select(entry=>new SearchResult(SearchKind.Character,entry.Character,entry.WubiCodes,entry.Pinyins,entry.Character)).ToArray();
        if(favoritesOnly.IsChecked==true) {
            try {
                var saved=ReadFavorites();
                results.ItemsSource=saved.Where(value=>text.Length==0||value.Contains(text,StringComparison.OrdinalIgnoreCase)
                    || search.Search(text,100).Any(hit=>hit.DisplayText==value))
                    .Order(StringComparer.Ordinal).SelectMany(value=>search.Search(value,1)).ToArray();
            } catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException) { details.Text=e.Message; return; }
        }
        results.SelectedIndex=results.Items.Count>0?0:-1;
        ShowEntry();
    }
    private async void ShowEntry()
    {
        int request = ++glyphRequest;
        glyph.Source=null;
        if(glyph.Parent is Border) ((Border)glyph.Parent).Visibility=Visibility.Collapsed;
        if(results.SelectedItem is not SearchResult entry) {
            details.Text=query.Text.Length==0?UiText.T("输入文字查询","Enter a query","Saisissez une recherche"):UiText.T("无匹配结果","No matches","Aucun résultat"); return;
        }
        details.Text=entry.DisplayText+"    "+(entry.WubiCode??string.Join("  ",entry.WubiCodes??Array.Empty<string>()))+"\n"+string.Join(" / ",entry.Pinyins??Array.Empty<string>());
        if(entry.GlyphKey is not { Length:1 } key)return;
        var bg = (TryFindResource("Bg") as System.Windows.Media.SolidColorBrush)?.Color ?? System.Windows.Media.Colors.White;
        var fg = (TryFindResource("Ink") as System.Windows.Media.SolidColorBrush)?.Color ?? System.Windows.Media.Colors.Black;
        bool dark = bg.R + bg.G + bg.B < 384;
        if(glyph.Parent is Border frame)frame.Background=new System.Windows.Media.SolidColorBrush(dark?bg:System.Windows.Media.Colors.White);
        try {
            var image=await Task.Run(()=>glyphStore.Load(key,bg,fg,dark));
            if(closed || request!=glyphRequest)return;
            glyph.Source=image;
            if(glyph.Parent is Border loadedFrame)loadedFrame.Visibility=image is null?Visibility.Collapsed:Visibility.Visible;
        }
        catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or System.IO.FileFormatException) {
            if(!closed && request==glyphRequest)glyph.Source=null;
        }
    }
}
