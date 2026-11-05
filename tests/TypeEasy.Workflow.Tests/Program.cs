using System.Net;
using System.Text;
using System.Text.Json;
using TypeEasy.Core;
class Program {
 static int count;
 static void Assert(bool b,string message){count++;if(!b)throw new Exception(message);}
 static async Task<int> Main(string[] args){try{
 if(args.Contains("--qa-verify-failure"))throw new Exception("Intentional guard probe");
 var root=Path.Combine(Path.GetTempPath(),"ShelterType-workflow-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
 var c=new Course{Id="wubi-test",Category="五笔",Variant="86版",Group="入门",Items=new(){new(){Prompt="一",Expected="g"},new(){Prompt="二",Expected="f"},new(){Prompt="三",Expected="d"}}};
 var random=ShuffledPractice.Create(c,Path.Combine(root,"Shuffled"));Assert(random.Id==c.Id&&random.Group==c.Group,"Shuffle stays in place");
 using(var session=PracticeSession.Open(random,Path.Combine(root,"Progress")))session.Feed(random.Items[0].Expected);
 var loaded=ShuffledPractice.Load(Path.Combine(root,"Shuffled"),new[]{c},Path.Combine(root,"Progress")).Single();using(var session=PracticeSession.Open(loaded,Path.Combine(root,"Progress")))Assert(session.State.ItemIndex==1,"Resume shuffled order");
 var old=JsonSerializer.Deserialize<Course>(JsonSerializer.Serialize(random))!;old.Id="shuffle:"+c.Id;using(var session=PracticeSession.Open(old,Path.Combine(root,"OldProgress")))session.Feed(old.Items[0].Expected);
 PracticeSession.MigrateCourseIdentity(old,random,Path.Combine(root,"OldProgress"));using(var session=PracticeSession.Open(random,Path.Combine(root,"OldProgress")))Assert(session.State.ItemIndex==1,"Legacy shuffled checkpoint migrates");
 Directory.CreateDirectory(Path.Combine(root,"Articles"));File.WriteAllText(Path.Combine(root,"Articles","sample.txt"),"Hello world.");var articles=ArticleLibrary.Apply(new[]{c},root);var article=articles.Single(ArticleLibrary.IsArticle);Assert(article.Category=="英文","Import English article");ArticleLibrary.SetHidden(root,article.Id,true);Assert(!ArticleLibrary.Apply(new[]{c},root).Any(ArticleLibrary.IsArticle),"Removed article hidden");ArticleLibrary.SetHidden(root,article.Id,false);Assert(ArticleLibrary.Apply(new[]{c},root).Any(ArticleLibrary.IsArticle),"Removed article restored");
 var run=new SpeedTestResult{Mode="英文",SourceId="sample",SourceTitle="sample",ElapsedSeconds=60,CorrectInputs=8,Errors=2,InputCounts=new(){new("a","s","c","t",2),new("a","a","c","t",8)}};
 var data=new TestAnalytics(new[]{run});Assert(data.Keys.Single().Opportunities==10&&data.Confusions.Single().Count==2,"Counted attempts aggregate without duplication");Assert(data.Contexts.Single().Errors==2&&data.MissingDetails==0,"Scoped context counts");Assert(new TestAnalytics(new[]{new SpeedTestResult()}).MissingDetails==1,"Legacy detail absence explicit");
 Assert(TestAnalytics.CompactSummary(new[]{run}).Length<4000,"Compact token context");
 var transport=new Fake();using(var client=new AiClient(transport)){var messages=Enumerable.Range(0,20).Select(i=>new AiMessage(i%2==0?"user":"assistant","message "+i)).ToArray();var answer=await client.AskAsync("http://localhost:1234/v1","test","secret",TestAnalytics.CompactSummary(new[]{run}),messages,"zh-CN",default);Assert(answer=="test response","Compatible response parsed");Assert(transport.Messages==7&&transport.Model=="test","Only six recent turns plus compact summary");}
 foreach(var url in new[]{"http://remote.example/v1","https://user:pass@example.org/v1","https://example.org/v1?key=x"}){bool rejected=false;try{AiClient.Endpoint(url);}catch(ArgumentException){rejected=true;}Assert(rejected,"Unsafe endpoint rejected");}
 foreach(var protocol in new[]{AiProtocol.Responses,AiProtocol.AnthropicMessages}){
 using var client=new AiClient(new ProtocolFake(protocol));var answer=await client.AskAsync("https://example.org/v1","model","secret","summary",new[]{new AiMessage("user","hello")},"en",default,protocol);Assert(answer=="one\ntwo","Multi-block response parsed for "+protocol);
 }
 Console.WriteLine($"PASS {count} workflow checks.");return 0;
 }catch(Exception e){Console.Error.WriteLine("QA FAILED (caught): "+e);return 1;}}
 sealed class Fake:HttpMessageHandler{public int Messages;public string? Model;protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){var body=await request.Content!.ReadAsStringAsync(token);using var doc=JsonDocument.Parse(body);Messages=doc.RootElement.GetProperty("messages").GetArrayLength();Model=doc.RootElement.GetProperty("model").GetString();if(request.RequestUri!.AbsolutePath!="/v1/chat/completions")throw new Exception("Wrong endpoint");return new(HttpStatusCode.OK){Content=new StringContent("{\"choices\":[{\"message\":{\"content\":\"test response\"}}]}",Encoding.UTF8,"application/json")};}}
 sealed class ProtocolFake(AiProtocol protocol):HttpMessageHandler{
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
 using var doc=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));var root=doc.RootElement;
 string result;
 if(protocol==AiProtocol.Responses){
 Assert(request.RequestUri!.AbsolutePath=="/v1/responses","Responses path");Assert(request.Headers.Authorization?.Scheme=="Bearer","Responses auth");Assert(root.GetProperty("input").GetArrayLength()==1&&!root.GetProperty("store").GetBoolean()&&root.GetProperty("instructions").GetString()!.Contains("summary"),"Responses context and storage");
 result="""{"output":[{"type":"reasoning","summary":[]},{"type":"message","content":[{"type":"output_text","text":"one"},{"type":"output_text","text":"two"}]}]}""";
 }else{
 Assert(request.RequestUri!.AbsolutePath=="/v1/messages","Anthropic path");Assert(request.Headers.Authorization==null&&request.Headers.GetValues("x-api-key").Single()=="secret"&&request.Headers.Contains("anthropic-version"),"Anthropic headers");Assert(root.GetProperty("system").GetString()!.Contains("summary")&&root.GetProperty("messages")[0].GetProperty("role").GetString()=="user","Anthropic system separate from turns");
 result="""{"content":[{"type":"thinking","thinking":"hidden"},{"type":"text","text":"one"},{"type":"text","text":"two"}]}""";
 }
 return new(HttpStatusCode.OK){Content=new StringContent(result)};
 }}
}
