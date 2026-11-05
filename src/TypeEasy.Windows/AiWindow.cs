using System.IO;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using TypeEasy.Core;
namespace TypeEasy.Windows;

public sealed class AiPanel : UserControl
{
    private sealed class Configuration{public AiProtocol Protocol{get;set;} public string Address{get;set;}="";public string Model{get;set;}="";public string Secret{get;set;}="";}
    private readonly string directory;
    private readonly TextBox address=new(),model=new(),prompt=new(){AcceptsReturn=true,MinHeight=65,MaxLength=4000,TextWrapping=TextWrapping.Wrap};
    private readonly PasswordBox key=new();
    private readonly ComboBox protocol=StatsUi.Choice(290);
    private readonly TextBox transcript=new(){IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly ComboBox history=StatsUi.Choice(340);
    private readonly TextBlock status=StatsUi.Text("",12,"Muted");
    private readonly Button send;
    private readonly CancellationTokenSource lifetime=new();
    private AiConversation conversation;
    private bool busy,closed;
    private readonly TextBox summaryPreview=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=130,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    public AiPanel(string root,string summary,string title)
    {
        directory=Path.Combine(root,"AI");conversation=new(){Summary=summary,Title=title};
        MinHeight=480;
        var layout=new DockPanel{Margin=new Thickness(24)};Content=layout;
        var header=new StackPanel();header.Children.Add(StatsUi.Text(UiText.T("AI 解读","AI coach","Analyse IA"),25));
        var options=new StackPanel();foreach(var label in new[]{"OpenAI Chat Completions","OpenAI Responses","Anthropic Messages"})protocol.Items.Add(label);protocol.SelectedIndex=0;options.Children.Add(StatsUi.Field(UiText.T("接口格式","API format","Format API"),protocol));options.Children.Add(StatsUi.Field("API Base URL",address));options.Children.Add(StatsUi.Field(UiText.T("模型","Model","Modèle"),model));options.Children.Add(StatsUi.Field("API Key",key));options.Children.Add(StatsUi.Button(UiText.T("保存配置","Save settings","Enregistrer"),(_,_)=>SaveConfiguration()));
        var expander=new Expander{Header=UiText.T("连接设置","Connection settings","Connexion"),Content=options,Margin=new Thickness(0,8,0,12)};header.Children.Add(expander);
        header.Children.Add(StatsUi.Text(UiText.T("发送时仅提交统计摘要和最近六条对话；对话保存在本机。","Sends a compact summary and the last six messages. Chats are saved locally.","Envoi d’un résumé et des six derniers messages. Conversations locales."),12,"Muted"));
        header.Children.Add(history);history.DisplayMemberPath="Title";history.SelectionChanged+=(_,_)=>{if(!busy&&history.SelectedItem is AiConversation saved){conversation=saved;Render();}};
        var preview=new Expander{Header=UiText.T("查看将发送的摘要","Preview summary","Voir le résumé"),Content=summaryPreview};header.Children.Add(preview);
        DockPanel.SetDock(header,Dock.Top);layout.Children.Add(header);
        var bottom=new StackPanel{Margin=new Thickness(0,12,0,0)};bottom.Children.Add(prompt);
        send=StatsUi.Button(UiText.T("分析 / 发送","Analyze / Send","Analyser / Envoyer"),async(_,_)=>await Send());bottom.Children.Add(send);bottom.Children.Add(status);DockPanel.SetDock(bottom,Dock.Bottom);layout.Children.Add(bottom);layout.Children.Add(transcript);
        try{var file=Path.Combine(directory,"settings.json");if(File.Exists(file)){var c=JsonSerializer.Deserialize<Configuration>(File.ReadAllText(file))??new Configuration();protocol.SelectedIndex=Enum.IsDefined(c.Protocol)?(int)c.Protocol:0;address.Text=c.Address;model.Text=c.Model;if(!string.IsNullOrEmpty(c.Secret))key.Password=Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(c.Secret),null,DataProtectionScope.CurrentUser));}else expander.IsExpanded=true;
            if(Directory.Exists(directory))history.ItemsSource=Directory.GetFiles(directory,"chat-*.json").Select(f=>{try{return JsonSerializer.Deserialize<AiConversation>(File.ReadAllText(f));}catch(JsonException){return null;}}).Where(c=>c!=null&&Guid.TryParseExact(c.Id,"N",out _)&&c.Summary!=null&&c.Messages!=null&&c.Messages.All(m=>m!=null&&m.Content!=null)).OrderByDescending(c=>c!.Updated).ToArray();
        }catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or CryptographicException or FormatException){status.Text=UiText.T("请重新填写连接配置。","Please re-enter connection settings.","Veuillez reconfigurer la connexion.");}
        Unloaded+=(_,_)=>{closed=true;lifetime.Cancel();};Render();
    }
    public static void Open(Window host,string root,string summary,string title)
    {
        if(host.Content is not UIElement previous)return;
        host.Content=null;
        var frame=new DockPanel();
        var back=StatsUi.Button(UiText.T("返回","Back","Retour"),(_,_)=>{frame.Children.Clear();host.Content=previous;});
        back.HorizontalAlignment=HorizontalAlignment.Left;back.Margin=new Thickness(24,16,0,0);DockPanel.SetDock(back,Dock.Top);frame.Children.Add(back);
        frame.Children.Add(new AiPanel(root,summary,title));host.Content=frame;
    }
    private bool SaveConfiguration(){try{_ = AiClient.Endpoint(address.Text,(AiProtocol)protocol.SelectedIndex);if(string.IsNullOrWhiteSpace(model.Text))throw new ArgumentException("Model is required.");Directory.CreateDirectory(directory);var c=new Configuration{Protocol=(AiProtocol)protocol.SelectedIndex,Address=address.Text.Trim(),Model=model.Text.Trim(),Secret=key.Password.Length==0?"":Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Password),null,DataProtectionScope.CurrentUser))};File.WriteAllText(Path.Combine(directory,"settings.json"),JsonSerializer.Serialize(c));status.Text=UiText.T("已保存。","Saved.","Enregistré.");return true;}catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or CryptographicException){status.Text=e.Message;return false;}}
    private void Save(){if(!Guid.TryParseExact(conversation.Id,"N",out _))throw new InvalidDataException("Invalid conversation ID.");Directory.CreateDirectory(directory);conversation.Updated=DateTimeOffset.Now;var file=Path.Combine(directory,"chat-"+conversation.Id+".json");File.WriteAllText(file+".tmp",JsonSerializer.Serialize(conversation));File.Move(file+".tmp",file,true);}
    private void Render(){summaryPreview.Text=conversation.Summary;transcript.Text=string.Join("\n\n",conversation.Messages.Select(x=>(x.Role=="user"?UiText.T("我","Me","Moi"):"AI")+"\n"+x.Content));}
    private async Task Send(){if(busy||!SaveConfiguration())return;busy=true;send.IsEnabled=false;history.IsEnabled=false;try{
        var question=string.IsNullOrWhiteSpace(prompt.Text)?UiText.T("分析我的打字表现，给出下一次练习建议。","Analyze my typing and suggest the next practice.","Analyse ma frappe et propose le prochain exercice."):prompt.Text.Trim();
        conversation.Messages.Add(new("user",question));Save();Render();prompt.Clear();status.Text=UiText.T("分析中…","Analyzing…","Analyse…");
        using var client=new AiClient();var answer=await client.AskAsync(address.Text,model.Text,key.Password,conversation.Summary,conversation.Messages,UiText.Language,lifetime.Token,(AiProtocol)protocol.SelectedIndex);conversation.Messages.Add(new("assistant",answer));Save();if(!closed){Render();status.Text="";}
    }catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException){if(!closed)status.Text=e.Message;}finally{busy=false;if(!closed){send.IsEnabled=true;history.IsEnabled=true;}}}
}
