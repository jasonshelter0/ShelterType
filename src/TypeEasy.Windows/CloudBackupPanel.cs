using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using TypeEasy.Core;
namespace TypeEasy.Windows;
public sealed class CloudBackupPanel:StackPanel
{
    private sealed record Configuration(string Account,string Secret);
    private readonly string root;
    private readonly TextBox account=new();private readonly PasswordBox password=new();
    private readonly CheckBox chats=new();private readonly ListBox backups=new(){Height=150,DisplayMemberPath="DisplayName"};
    private readonly TextBlock status=StatsUi.Text("",12,"Muted");
    private readonly CancellationTokenSource lifetime=new();private readonly List<Button> buttons=new();private bool busy;
    public CloudBackupPanel(string userRoot){root=userRoot;
        Children.Add(StatsUi.Text(UiText.T("坚果云","Nutstore","Nutstore"),18));
        Children.Add(StatsUi.Text(UiText.T("使用坚果云第三方应用密码，备份保存在 /ShelterType。","Use a Nutstore app password. Backups are stored in /ShelterType.","Utilisez un mot de passe d’application Nutstore. Dossier : /ShelterType."),12,"Muted"));
        Children.Add(StatsUi.Field(UiText.T("账号（邮箱）","Account (email)","Compte (e-mail)"),account));Children.Add(StatsUi.Field(UiText.T("应用密码","App password","Mot de passe d’application"),password));
        chats.Content=UiText.T("包含 AI 对话","Include AI chats","Inclure les conversations IA");chats.SetResourceReference(Control.ForegroundProperty,"Ink");chats.Margin=new Thickness(0,8,0,10);Children.Add(chats);
        Children.Add(StatsUi.Text(UiText.T("包含练习记录、文章和收藏；不包含密钥与本机设置。","Includes practice records, texts and favorites; excludes keys and device settings.","Historique, textes et favoris inclus ; clés et réglages locaux exclus."),12,"Muted"));
        var actions=new WrapPanel{Margin=new Thickness(0,12,0,12)};
        void Action(string label,Func<Task> run){var button=StatsUi.Button(label,async(_,_)=>await Run(run));buttons.Add(button);actions.Children.Add(button);}
        Action(UiText.T("连接 / 刷新","Connect / Refresh","Connexion / Actualiser"),Refresh);
        Action(UiText.T("上传备份","Upload backup","Envoyer une sauvegarde"),async()=>{var bytes=ProfileBackup.Create(root,chats.IsChecked==true);using var client=Client();await client.UploadAsync(bytes,lifetime.Token);await Refresh();status.Text=UiText.T("备份已上传。","Backup uploaded.","Sauvegarde envoyée.");});
        Action(UiText.T("恢复选中备份","Restore selected","Restaurer la sélection"),async()=>{
            if(backups.SelectedItem is not CloudBackup selected){status.Text=UiText.T("请先选择备份。","Select a backup first.","Choisissez une sauvegarde.");return;}
            if(MessageBox.Show(Window.GetWindow(this),UiText.T("恢复会在下次启动时替换练习记录、文章与收藏，不会合并。当前数据会先保存为本机备份。继续？","On next launch, restore replaces records, texts and favorites without merging. A local backup is saved first. Continue?","Au prochain démarrage, les données seront remplacées sans fusion. Une sauvegarde locale sera créée. Continuer?"),"ShelterType",MessageBoxButton.OKCancel,MessageBoxImage.Question)!=MessageBoxResult.OK)return;
            using var client=Client();var bytes=await client.DownloadAsync(selected.Name,lifetime.Token);ProfileBackup.Stage(root,bytes);status.Text=UiText.T("已准备恢复。请关闭并重新打开 ShelterType；当前操作不会立刻覆盖记录。","Restore ready. Close and reopen ShelterType to apply it.","Restauration prête. Fermez puis rouvrez ShelterType.");
        });
        Children.Add(actions);Children.Add(backups);Children.Add(status);
        var local=StatsUi.Button(UiText.T("恢复本机备份…","Restore local backup…","Restaurer une sauvegarde locale…"),(_,_)=>{if(busy)return;try{var picker=new Microsoft.Win32.OpenFileDialog{Filter="ShelterType backup (*.zip)|*.zip",InitialDirectory=Path.Combine(root,"CloudBackup")};if(picker.ShowDialog(Window.GetWindow(this))!=true)return;if(new FileInfo(picker.FileName).Length>ProfileBackup.MaxArchive)throw new IOException("Backup exceeds 50 MB.");if(MessageBox.Show(Window.GetWindow(this),UiText.T("下次启动时用所选备份替换记录？当前记录会先备份。","Replace records on next launch? Current records will be backed up first.","Remplacer les données au prochain démarrage ? Une sauvegarde sera créée."),"ShelterType",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;ProfileBackup.Stage(root,File.ReadAllBytes(picker.FileName));status.Text=UiText.T("已准备恢复，请关闭并重新打开程序。","Restore ready. Close and reopen the app.","Restauration prête. Fermez et rouvrez l’application.");}catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException){status.Text=e.Message;}});local.Margin=new Thickness(0,8,0,0);Children.Add(local);
        var help=StatsUi.Button(UiText.T("如何获取应用密码","Get an app password","Obtenir un mot de passe"),(_,_)=>{try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://help.jianguoyun.com/?p=2064"){UseShellExecute=true});}catch(System.ComponentModel.Win32Exception){}});help.Margin=new Thickness(0,14,0,0);Children.Add(help);
        try{var path=Path.Combine(root,"CloudBackup","settings.json");if(File.Exists(path)){var c=JsonSerializer.Deserialize<Configuration>(File.ReadAllText(path));if(c!=null){account.Text=c.Account;password.Password=Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(c.Secret),null,DataProtectionScope.CurrentUser));}}}catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or CryptographicException or FormatException or ArgumentException){status.Text=UiText.T("请重新填写账号和应用密码。","Please re-enter account and app password.","Saisissez à nouveau vos identifiants.");}
        Unloaded+=(_,_)=>lifetime.Cancel();
    }
    private NutstoreClient Client()=>new(account.Text,password.Password);
    private async Task Refresh(){using var client=Client();backups.ItemsSource=await client.ListAsync(lifetime.Token);status.Text=UiText.Format("{0} 个云端备份（本机时间）","{0} cloud backups (local time)","{0} sauvegardes (heure locale)",backups.Items.Count);}
    private async Task Run(Func<Task> run){if(busy)return;busy=true;foreach(var b in buttons)b.IsEnabled=false;account.IsEnabled=password.IsEnabled=chats.IsEnabled=false;status.Text=UiText.T("处理中…","Working…","En cours…");try{
        using(var check=Client()){}var folder=Path.Combine(root,"CloudBackup");Directory.CreateDirectory(folder);var c=new Configuration(account.Text.Trim(),Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password.Password),null,DataProtectionScope.CurrentUser)));File.WriteAllText(Path.Combine(folder,"settings.json"),JsonSerializer.Serialize(c));await run();
    }catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException or OperationCanceledException or JsonException or System.Xml.XmlException or ArgumentException or FormatException or CryptographicException){if(!lifetime.IsCancellationRequested)status.Text=e.Message;}finally{busy=false;foreach(var b in buttons)b.IsEnabled=true;account.IsEnabled=password.IsEnabled=chats.IsEnabled=true;}}
}