using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TypeEasy.Core;

namespace TypeEasy.Windows;

public sealed class KeyboardStatisticsPanel : StackPanel
{
    private readonly IReadOnlyList<KeyboardKeySummary> data;
    private readonly TeachingKeyboard keyboard=new(){ShowHands=false,Margin=new Thickness(0,8,0,4)};
    private readonly ComboBox metric=StatsUi.Choice(190);
    private readonly TextBlock legend=StatsUi.Text("",11,"Muted");
    private readonly StackPanel detail=new(),unmappedPanel=new();
    private readonly TextBox search=new(){MinWidth=150};
    private readonly WrapPanel chips=new(){Margin=new Thickness(0,8,0,0)};
    private readonly TextBlock pageLabel=StatsUi.Text("",11,"Muted");
    private readonly Button previous,next;
    private string? selected;
    private int page;
    private KeyboardMetric SelectedMetric=>(KeyboardMetric)Math.Max(0,metric.SelectedIndex);
    private sealed record Choice(string Text);

    public KeyboardStatisticsPanel(AnalyticsStore store,string? mode)
    {
        data=KeyboardStatistics.Build(store.ReadKeys(),store.ReadConfusions(),store.ReadContextRisks(),mode);
        var header=new DockPanel();DockPanel.SetDock(metric,Dock.Right);header.Children.Add(metric);
        header.Children.Add(StatsUi.Text(UiText.T("键盘统计","Keyboard statistics","Statistiques du clavier"),17));Children.Add(header);
        foreach(var text in new[]{UiText.T("输入频次","Input frequency","Fréquence de saisie"),UiText.T("错误次数","Error count","Nombre d’erreurs"),
            UiText.T("错误率","Error rate","Taux d’erreur"),UiText.T("常见混淆","Common confusions","Confusions"),UiText.T("上下文关联","Context associations","Contexte")})metric.Items.Add(text);
        metric.SelectedIndex=2;Children.Add(legend);Children.Add(keyboard);
        keyboard.KeySelected+=id=>{selected=id;RenderKeyboard();RenderDetail();};
        var unmappedHeader=new DockPanel();DockPanel.SetDock(search,Dock.Right);unmappedHeader.Children.Add(search);
        unmappedHeader.Children.Add(StatsUi.Text(UiText.T("提交字符","Submitted characters","Caractères validés"),14));
        search.ToolTip=UiText.T("搜索字符","Find a character","Rechercher un caractère");
        System.Windows.Automation.AutomationProperties.SetName(search,search.ToolTip.ToString());
        unmappedPanel.Children.Add(unmappedHeader);unmappedPanel.Children.Add(chips);
        var pager=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,6)};
        previous=StatsUi.Button(UiText.T("上一页","Previous","Précédent"),(_,_)=>{if(page>0){page--;RenderUnmapped();}});
        next=StatsUi.Button(UiText.T("下一页","Next","Suivant"),(_,_)=>{page++;RenderUnmapped();});pager.Children.Add(previous);pager.Children.Add(next);pager.Children.Add(pageLabel);unmappedPanel.Children.Add(pager);
        Children.Add(unmappedPanel);Children.Add(detail);
        Children.Add(StatsUi.Explanation(UiText.T(
            "频次统计已提交字符，不代表物理按键次数。错误率以目标字符的输入机会为分母。大写与符号合并到标准键位，Shift事件未单独记录。中文输入法只统计提交字符，不推断键位。\n上下文只描述关联；至少20次机会、3次错误才作比较，较基线高10个百分点才标记。",
            "Frequency counts submitted characters, not physical key presses. Error rate uses attempts for the expected character. Capitals and symbols share their standard key; Shift presses are not recorded. IME text is shown separately.\nContext is association only. Comparisons require 20 attempts and 3 errors; a difference of 10 percentage points flags a context.",
            "La fréquence compte les caractères validés, pas les frappes physiques. Le taux d’erreur utilise les tentatives du caractère attendu. Majuscules et symboles partagent leur touche standard ; les pressions sur Maj ne sont pas enregistrées. Le texte IME est séparé.\nLe contexte indique une association uniquement. Il faut 20 tentatives et 3 erreurs ; un écart de 10 points signale un contexte."),UiText.T("统计口径","How counts work","Méthode de calcul")));
        metric.SelectionChanged+=(_,_)=>{page=0;Render();};search.TextChanged+=(_,_)=>{page=0;RenderUnmapped();};Render();
    }
    private double Normalize(KeyboardKeySummary item)
    {
        var value=item.Value(SelectedMetric);
        if(SelectedMetric==KeyboardMetric.ErrorRate)return Math.Clamp(value/.5,0,1);
        if(SelectedMetric==KeyboardMetric.Context)return Math.Clamp(value/.30,0,1);
        var maximum=data.Select(x=>x.Value(SelectedMetric)).DefaultIfEmpty(0).Max();
        return maximum<=0?0:Math.Log(1+value)/Math.Log(1+maximum);
    }
    private KeyboardHeatValue Heat(KeyboardKeySummary item)=>new(Normalize(item),item.HasData(SelectedMetric),item.LowSample(SelectedMetric),Tooltip(item));
    private void Render()
    {
        selected??=data.OrderByDescending(x=>x.Value(SelectedMetric)).FirstOrDefault()?.KeyId;
        var max=data.Select(x=>x.Value(SelectedMetric)).DefaultIfEmpty(0).Max();
        var range=SelectedMetric==KeyboardMetric.ErrorRate?"0–50%+":SelectedMetric==KeyboardMetric.Context?UiText.T("0–30+ 个百分点","0–30+ percentage points","0–30+ points de pourcentage"):
            SelectedMetric==KeyboardMetric.Frequency?UiText.Format("0–{0:N0} 次提交","0–{0:N0} submissions","0–{0:N0} saisies",max):UiText.Format("0–{0:N0} 次","0–{0:N0} occurrences","0–{0:N0} occurrences",max);
        legend.Text=range+"    "+UiText.T("蓝→橙：少→多    灰：无数据    淡色：样本少","Blue to orange: low to high    Gray: no data    Faint: small sample","Bleu à orange : faible à élevé    Gris : aucune donnée    Clair : petit échantillon");
        RenderKeyboard();RenderUnmapped();RenderDetail();
    }
    private void RenderKeyboard()
    {
        var mapped=data.Where(x=>!x.Unmapped).ToArray();
        keyboard.Visibility=mapped.Length==0&&data.Count>0?Visibility.Collapsed:Visibility.Visible;
        keyboard.SetStatistics(mapped.ToDictionary(x=>x.KeyId,Heat),mapped.Any(x=>x.KeyId.StartsWith("Num",StringComparison.Ordinal)),selected);
    }
    private void RenderUnmapped()
    {
        chips.Children.Clear();var all=data.Where(x=>x.Unmapped).ToArray();
        unmappedPanel.Visibility=all.Length==0?Visibility.Collapsed:Visibility.Visible;
        var values=all.Where(x=>search.Text.Length==0||x.Label.Contains(search.Text,StringComparison.CurrentCultureIgnoreCase)).OrderByDescending(x=>x.Value(SelectedMetric)).ThenBy(x=>x.Label).ToArray();
        const int size=48;var pages=Math.Max(1,(values.Length+size-1)/size);page=Math.Clamp(page,0,pages-1);
        var field=(TryFindResource("Field") as SolidColorBrush)?.Color??Colors.DarkSlateGray;
        foreach(var item in values.Skip(page*size).Take(size))
        {
            var heat=Heat(item);var color=TeachingKeyboard.StatisticsColor(field,heat);
            var button=StatsUi.Button(DisplayLabel(item),(_,_)=>{selected=item.KeyId;RenderKeyboard();RenderDetail();});
            button.MinWidth=40;button.Padding=new Thickness(10,7,10,7);button.Margin=new Thickness(0,0,5,5);
            button.Background=new SolidColorBrush(color);button.Foreground=new SolidColorBrush(color.R+color.G+color.B>440?Colors.Black:Colors.White);
            button.ToolTip=heat.Tooltip;chips.Children.Add(button);
        }
        previous.IsEnabled=page>0;next.IsEnabled=page+1<pages;pageLabel.Text=UiText.Format("  {0}/{1} 页，{2} 个字符","  Page {0}/{1}, {2} characters","  Page {0}/{1}, {2} caractères",page+1,pages,values.Length);
    }
    private string Tooltip(KeyboardKeySummary item)
    {
        var rate=item.Opportunities==0?UiText.T("未知","unknown","inconnu"):item.ErrorRate.ToString("P1",UiText.Culture);
        var text=DisplayLabel(item)+"\n"+UiText.Format("提交 {0:N0} 次；目标机会 {1:N0} 次\n错误 {2:N0} 次，错误率 {3}",
            "Submitted {0:N0}; expected attempts {1:N0}\nErrors {2:N0}, error rate {3}",
            "Validés {0:N0} ; tentatives attendues {1:N0}\nErreurs {2:N0}, taux d’erreur {3}",item.Submitted,item.Opportunities,item.Errors,rate);
        if(SelectedMetric==KeyboardMetric.Confusions)text+="\n"+UiText.Format("最常见混淆 {0:N0} 次","Most frequent confusion: {0:N0} occurrences","Confusion la plus fréquente : {0:N0} occurrences",item.Value(KeyboardMetric.Confusions));
        if(SelectedMetric==KeyboardMetric.Context)text+="\n"+(item.Contexts.Any(c=>c.IsEligible)?UiText.Format("合格上下文最大正差值 +{0:0.0} 个百分点","Largest eligible increase above baseline: +{0:0.0} percentage points","Hausse admissible maximale par rapport à la référence : +{0:0.0} points",item.Value(KeyboardMetric.Context)*100):UiText.T("暂无达到比较样本量的上下文","No context yet meets the comparison sample size","Aucun contexte n’atteint encore la taille d’échantillon requise"));
        if(item.Opportunities==0)text+="\n"+UiText.T("该键无目标机会，错误率未知。","No expected attempts; error rate is unknown.","Aucune tentative attendue ; taux d’erreur inconnu.");
        if(item.ShiftOpportunities>0)text+="\n"+UiText.Format("含大写或符号 {0:N0} 次；未记录实际Shift事件。","Includes {0:N0} capital/symbol attempts; actual Shift presses are unknown.","Inclut {0:N0} tentatives de majuscules/symboles ; pressions réelles sur Maj inconnues.",item.ShiftOpportunities);
        if(!item.HasData(SelectedMetric))text+="\n"+UiText.T("此指标无数据","No data for this metric","Aucune donnée pour cet indicateur");
        else if(item.LowSample(SelectedMetric))text+="\n"+UiText.T("样本不足，谨慎比较","Small sample; compare cautiously","Petit échantillon ; comparer avec prudence");
        return text;
    }
    private void RenderDetail()
    {
        detail.Children.Clear();var item=data.FirstOrDefault(x=>x.KeyId==selected);
        if(item is null){detail.Children.Add(StatsUi.Text(UiText.T("选择按键查看明细。","Select a key for details.","Sélectionnez une touche pour les détails."),12,"Muted"));return;}
        detail.Children.Add(StatsUi.Text(DisplayLabel(item),17));detail.Children.Add(StatsUi.Text(string.Join("\n",Tooltip(item).Split('\n').Skip(1)),12,"Muted"));
        if(item.Targets.Count>0)detail.Children.Add(StatsUi.Text(string.Join("   ",item.Targets.Select(x=>UiText.Format("{0}（{1}）：{2}/{3} 错误","{0} ({1}): {2}/{3} errors","{0} ({1}) : {2}/{3} erreurs",CharacterLabel(x.Expected),UiText.Mode(x.Mode),x.Errors,x.Opportunities))),12));
        var confusions=item.Confusions.OrderByDescending(x=>x.Count).ToArray();
        if(confusions.Length>0)
        {
            var list=new ListBox{MaxHeight=115,Margin=new Thickness(0,6,0,8)};
            foreach(var c in confusions)list.Items.Add(UiText.Format("{0} → {1}   {2:N0} 次   {3}","{0} → {1}   {2:N0} times   {3}","{0} → {1}   {2:N0} fois   {3}",CharacterLabel(c.Expected),CharacterLabel(c.Actual),c.Count,UiText.Mode(c.Mode)));
            detail.Children.Add(list);
        }
        if(item.Contexts.Count>0)
        {
            var list=new ListBox{MaxHeight=190,Margin=new Thickness(0,6,0,0)};
            foreach(var c in item.Contexts.OrderByDescending(x=>x.IsRisk).ThenByDescending(x=>x.Errors))
            {
                var flag=c.IsRisk?UiText.T("值得留意","Worth reviewing","À examiner"):c.IsEligible?UiText.T("未达提示阈值","Below review threshold","Seuil non atteint"):UiText.T("样本不足","Insufficient sample","Échantillon insuffisant");
                var row=StatsUi.Text(UiText.Key(c.Before)+" 〔"+UiText.Key(c.Expected)+"〕 "+UiText.Key(c.After)+"   "+UiText.Mode(c.Mode)+"   "+flag+"\n"+
                    UiText.Format("错误 {0}/{1}；基线 {2}/{3}；平滑率 {4:P1} / {5:P1}","Errors {0}/{1}; baseline {2}/{3}; smoothed rates {4:P1} / {5:P1}","Erreurs {0}/{1} ; référence {2}/{3} ; taux lissés {4:P1} / {5:P1}",c.Errors,c.Opportunities,c.BaselineErrors,c.BaselineOpportunities,c.SmoothedErrorRate,c.BaselineErrorRate),12);
                var wrong=c.ActualCounts.Where(x=>x.Key!=c.Expected).OrderByDescending(x=>x.Value).Select(x=>CharacterLabel(x.Key)+" ×"+x.Value);
                var before=c.TypedBeforeCounts.OrderByDescending(x=>x.Value).Select(x=>CharacterLabel(x.Key)+" ×"+x.Value);
                row.ToolTip=UiText.T("误输入：","Wrong input: ","Saisie erronée : ")+string.Join(", ",wrong)+"\n"+UiText.T("此前提交：","Previously submitted: ","Texte validé avant : ")+string.Join(", ",before);
                list.Items.Add(row);
            }
            detail.Children.Add(new Expander{Header=UiText.T("前后文","Contexts","Contextes"),Content=list,IsExpanded=SelectedMetric==KeyboardMetric.Context});
        }
    }
    private static string CharacterLabel(string value)=>value=="（其他）"?UiText.T("其他","Other","Autres"):UiText.Key(value);
    private static string DisplayLabel(KeyboardKeySummary item)
    {
        if(item.Unmapped||!item.KeyId.StartsWith("Num",StringComparison.Ordinal))return CharacterLabel(item.Label);
        var key=item.KeyId[3..] switch { "Add"=>"+","Subtract"=>"−","Multiply"=>"*","Divide"=>"/","Decimal"=>".","Enter"=>"\n",var digit=>digit };
        return UiText.T("数字键盘 ","Number pad ","Pavé numérique ")+UiText.Key(key);
    }
}
