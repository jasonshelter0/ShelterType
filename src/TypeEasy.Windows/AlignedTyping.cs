using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace TypeEasy.Windows;

/// <summary>The reference and input share a measured line, font and left inset.</summary>
internal sealed class AlignedTyping
{
    private string cachedText="";
    private double cachedWidth;
    private readonly List<int> starts=new();
    public void Render(TextBlock reference,TextBox input,string text,int position,string error="")
    {
        reference.FontFamily=input.FontFamily=new FontFamily("Microsoft YaHei UI");
        reference.FontSize=input.FontSize=24;
        TextOptions.SetTextFormattingMode(reference,TextFormattingMode.Display);
        TextOptions.SetTextFormattingMode(input,TextFormattingMode.Display);
        reference.TextWrapping=TextWrapping.NoWrap;
        reference.Padding=new Thickness(11,0,11,0);
        input.Padding=new Thickness(10,8,10,8);
        input.TextWrapping=TextWrapping.NoWrap;
        input.HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden;
        input.VerticalScrollBarVisibility=ScrollBarVisibility.Hidden;
        var width=Math.Max(80,Math.Min(input.ActualWidth>0?input.ActualWidth:600,reference.ActualWidth>0?reference.ActualWidth:600)-48);
        if(cachedText!=text||Math.Abs(cachedWidth-width)>0.5||starts.Count==0)
        {
            cachedText=text;cachedWidth=width;starts.Clear();starts.Add(0);
            int start=0;
            for(int end=1;end<=text.Length;end++)
            {
                var part=text[start..end];
                var measure=new FormattedText(part,UiText.Culture,FlowDirection.LeftToRight,new Typeface(reference.FontFamily,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal),24,Brushes.Black,VisualTreeHelper.GetDpi(reference).PixelsPerDip);
                if(text[end-1]=='\n'){if(end<text.Length)starts.Add(end);start=end;}
                else if(measure.WidthIncludingTrailingWhitespace>width&&end-start>1)
                {
                    int split=end-1;
                    bool Word(char c)=>char.IsAsciiLetterOrDigit(c)||c is '\'' or '’';
                    if(split<text.Length && Word(text[split]) && Word(text[split-1]))
                    {
                        while(split>start && Word(text[split-1]))split--;
                        if(split==start)continue; // An overlong word stays whole.
                    }
                    if(split>start){starts.Add(split);start=split;}
                }
            }
        }
        position=Math.Clamp(position,0,text.Length);
        var line=starts.FindLastIndex(s=>s<=position);var begin=starts[Math.Max(0,line)];
        var finish=line+1<starts.Count?starts[line+1]:text.Length;
        reference.Inlines.Clear();
        if(position>begin){var done=new Run(text[begin..position]);done.SetResourceReference(TextElement.ForegroundProperty,"Muted");reference.Inlines.Add(done);}
        if(position<finish){var active=new Run(text[position]=='\n'?" ":text[position].ToString());active.SetResourceReference(TextElement.BackgroundProperty,"Selected");reference.Inlines.Add(active);reference.Inlines.Add(new Run(text[(position+1)..finish].TrimEnd('\r','\n')));}
        input.Text=text[begin..position].TrimEnd('\r','\n')+error;
        input.CaretIndex=input.Text.Length;
        input.ScrollToHorizontalOffset(0);
    }
}
