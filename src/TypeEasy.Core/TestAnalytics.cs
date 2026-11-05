using System.Text.Json;
namespace TypeEasy.Core;

public sealed class TestAnalytics
{
    public IReadOnlyList<TypingKeySummary> Keys {get;}
    public IReadOnlyList<TypingConfusion> Confusions {get;}
    public IReadOnlyList<ContextRisk> Contexts {get;}
    public int MissingDetails {get;}
    public TestAnalytics(IEnumerable<SpeedTestResult> source)
    {
        var tests=source.ToArray();MissingDetails=tests.Count(x=>x.InputCounts is null);
        var rows=tests.SelectMany(t=>(t.InputCounts??new()).Select(a=>(t.Mode,Input:a))).ToArray();
        Keys=rows.GroupBy(x=>(x.Mode,x.Input.Expected)).Select(g=>new TypingKeySummary(g.Key.Mode,g.Key.Expected,g.Sum(x=>x.Input.Count),g.Where(x=>x.Input.Expected!=x.Input.Actual).Sum(x=>x.Input.Count),g.GroupBy(x=>x.Input.Actual).ToDictionary(a=>a.Key,a=>a.Sum(x=>x.Input.Count)))).ToArray();
        Confusions=Keys.SelectMany(k=>k.ActualCounts.Where(x=>x.Key!=k.Expected).Select(x=>new TypingConfusion(k.Mode,k.Expected,x.Key,x.Value))).OrderByDescending(x=>x.Count).ToArray();
        Contexts=rows.GroupBy(x=>(x.Mode,x.Input.Expected,x.Input.Before,x.Input.After)).Select(g=>{
            var baseline=Keys.First(k=>k.Mode==g.Key.Mode&&k.Expected==g.Key.Expected);
            return new ContextRisk{Mode=g.Key.Mode,Expected=g.Key.Expected,Before=g.Key.Before,After=g.Key.After,Opportunities=g.Sum(x=>x.Input.Count),Errors=g.Where(x=>x.Input.Expected!=x.Input.Actual).Sum(x=>x.Input.Count),BaselineOpportunities=baseline.Opportunities,BaselineErrors=baseline.Errors,ActualCounts=g.GroupBy(x=>x.Input.Actual).ToDictionary(a=>a.Key,a=>a.Sum(x=>x.Input.Count))};}).ToArray();
    }
    public static string OverallSummary(AnalyticsStore store)
    {
        var days=store.ReadDays();return JsonSerializer.Serialize(new{scope="all recorded practice and tests",seconds=Math.Round(days.Sum(x=>x.ActiveSeconds)),correct=days.Sum(x=>x.CorrectInputs),errors=days.Sum(x=>x.Errors),recentDays=days.OrderByDescending(x=>x.Day).Take(14).Select(x=>new{x.Day,x.Mode,seconds=Math.Round(x.ActiveSeconds),x.CorrectInputs,x.Errors}),confusions=store.ReadConfusions().Take(16),contexts=store.ReadContextRisks().Take(12).Select(x=>new{x.Mode,x.Before,x.Expected,x.After,x.Opportunities,x.Errors,x.ActualCounts})});
    }
    public static string CompactSummary(IEnumerable<SpeedTestResult> source)
    {
        var runs=source.ToArray();var detail=new TestAnalytics(runs);
        return JsonSerializer.Serialize(new {scope="saved timed tests",tests=runs.Length,missingDetail=detail.MissingDetails,seconds=Math.Round(runs.Sum(x=>x.ElapsedSeconds)),correct=runs.Sum(x=>x.CorrectInputs),errors=runs.Sum(x=>x.Errors),recent=runs.OrderByDescending(x=>x.FinishedAt).Take(12).Select(x=>new{date=x.FinishedAt.ToString("yyyy-MM-dd"),mode=x.Mode,status=x.Status.ToString(),seconds=Math.Round(x.ElapsedSeconds),cpm=Math.Round(x.CharactersPerMinute,1),accuracy=Math.Round(x.Accuracy,3)}),confusions=detail.Confusions.Take(16),contexts=detail.Contexts.Where(x=>x.Errors>0).OrderByDescending(x=>x.Errors).Take(12).Select(x=>new{x.Expected,x.Before,x.After,x.Errors,x.Opportunities,x.ActualCounts})});
    }
}
