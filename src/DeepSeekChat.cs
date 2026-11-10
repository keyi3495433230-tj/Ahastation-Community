using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace CardReview;
public sealed record ChatMessage(string Role,string Content);
public sealed class ChatConversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DeepSeekChatState State { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore]
    public string Title { get { var text=State.Messages.FirstOrDefault(m=>m.Role=="user")?.Content??State.Draft; text=text.Replace('\r',' ').Replace('\n',' ').Trim();return text.Length==0?"新对话":text.Length>24?text[..24]+"…":text; } }
}
public sealed class ChatHistory
{
    public bool HistoryCollapsed { get; set; }
    public double HistoryWidth { get; set; } = 220;
    public string ActiveId { get; set; } = "";
    public List<ChatConversation> Conversations { get; set; } = new();
}
public sealed class DeepSeekChatState
{
    public string Persona { get; set; } = "你是我的学习伙伴。用简洁、耐心的中文交流，帮助我理解问题、梳理思路并制定可执行的安排。不了解的信息请说明，不要编造。";
    public string Model { get; set; } = "deepseek-flash";
    public string Draft { get; set; } = "";
    public bool MemoryEnabled { get; set; } = true;
    public List<ChatMessage> Messages { get; set; } = new();
}
public sealed class DeepSeekClient
{
    readonly HttpClient http;
    public DeepSeekClient(HttpClient? client=null){http=client??new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromMinutes(2)};}
    public static object Payload(DeepSeekChatState state,string prompt) => new {
        model=state.Model,messages=new[]{new ChatMessage("system",state.Persona)}.Concat(state.MemoryEnabled?state.Messages.TakeLast(40):Enumerable.Empty<ChatMessage>()).Append(new("user",prompt)).Select(m=>new{role=m.Role,content=m.Content}),
        stream=false,thinking=new{type="disabled"},max_tokens=4096
    };
    static void CheckResponse(HttpResponseMessage response)
    {
        if(response.IsSuccessStatusCode)return;
        string message=response.StatusCode switch{
            HttpStatusCode.Unauthorized=>"API Key 无效或已失效，请在设置中重新绑定。",
            HttpStatusCode.PaymentRequired=>"DeepSeek 账户余额不足，请充值后重试。",
            HttpStatusCode.TooManyRequests=>"请求过于频繁，请稍后再试。",
            HttpStatusCode.BadRequest=>"请求未被接受，请检查所选模型或缩短消息后重试。",
            HttpStatusCode.Forbidden=>"此密钥没有访问权限。",
            _=>$"DeepSeek 暂时无法响应（HTTP {(int)response.StatusCode}），请稍后重试。"
        };throw new InvalidOperationException(message);
    }
    public async Task<string> ReplyAsync(string key,DeepSeekChatState state,string prompt,CancellationToken cancel)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,"https://api.deepseek.com/chat/completions");request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
        request.Content=new StringContent(JsonSerializer.Serialize(Payload(state,prompt)),Encoding.UTF8,"application/json");
        using var response=await http.SendAsync(request,cancel);CheckResponse(response);
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        if(!json.RootElement.TryGetProperty("choices",out var choices)||choices.GetArrayLength()==0||!choices[0].TryGetProperty("message",out var message)||!message.TryGetProperty("content",out var content)||string.IsNullOrWhiteSpace(content.GetString()))throw new InvalidOperationException("DeepSeek 返回了空回复，请重试。");
        string reply=content.GetString()!;
        if(choices[0].TryGetProperty("finish_reason",out var reason)&&reason.GetString()=="length")reply+="\n\n（本次回复达到长度上限，可以发送“请继续”。）";
        return reply;
    }
    public async Task VerifyAsync(string key,CancellationToken cancel)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.deepseek.com/models");request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
        using var response=await http.SendAsync(request,cancel);CheckResponse(response);
    }
}
public sealed class DeepSeekStore
{
    readonly string root;
    string KeyPath=>Path.Combine(root,"private-credentials","deepseek.bin");
    public string StatePath=>Path.Combine(root,"deepseek-chat.json");
    public DeepSeekStore(string root){this.root=root;}
    public bool HasKey=>File.Exists(KeyPath);
    public ChatHistory LoadHistory()
    {
        var path=Path.Combine(root,"chat-history","library.json");
        if(File.Exists(path)){
            var history=JsonSerializer.Deserialize<ChatHistory>(File.ReadAllText(path))??throw new JsonException();
            if(history.Conversations==null||history.Conversations.Count==0||history.Conversations.Any(c=>c==null||!Guid.TryParseExact(c.Id,"N",out _)||c.State==null)||history.Conversations.Select(c=>c.Id).Distinct().Count()!=history.Conversations.Count||!history.Conversations.Any(c=>c.Id==history.ActiveId))throw new JsonException("历史对话格式不正确");
            foreach(var c in history.Conversations)Validate(c.State);return history;
        }
        var result=new ChatHistory();var current=new ChatConversation{State=Load()};result.Conversations.Add(current);result.ActiveId=current.Id;
        var folder=Path.Combine(root,"chat-history");
        if(Directory.Exists(folder))foreach(var file in Directory.GetFiles(folder,"*.json").OrderByDescending(File.GetLastWriteTime)){
            var state=JsonSerializer.Deserialize<DeepSeekChatState>(File.ReadAllText(file))??throw new JsonException();Validate(state);
            result.Conversations.Add(new(){State=state,CreatedAt=File.GetLastWriteTime(file)});
        }
        SaveHistory(result);return result;
    }
    public void SaveHistory(ChatHistory history)
    {
        var folder=Path.Combine(root,"chat-history");Directory.CreateDirectory(folder);var path=Path.Combine(folder,"library.json");
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(history));
        // Copy contents without propagating EFS attributes: ReplaceFile can fail for encrypted files.
        if(File.Exists(path))File.WriteAllBytes(path+".bak",File.ReadAllBytes(path));
        File.Move(path+".tmp",path,true);
    }
    public void Bind(string key)
    {
        key=key.Trim();if(key.Length<8||key.Any(char.IsWhiteSpace)||key.Any(char.IsControl))throw new ArgumentException("请输入有效的 DeepSeek API Key。");
        byte[] raw=Encoding.UTF8.GetBytes(key);try{Directory.CreateDirectory(Path.GetDirectoryName(KeyPath)!);File.WriteAllBytes(KeyPath+".tmp",ProtectedData.Protect(raw,null,DataProtectionScope.CurrentUser));File.Move(KeyPath+".tmp",KeyPath,true);}finally{CryptographicOperations.ZeroMemory(raw);}
    }
    public string ReadKey(){if(!HasKey)throw new InvalidOperationException("请先在设置中绑定 DeepSeek API Key。");byte[] raw=ProtectedData.Unprotect(File.ReadAllBytes(KeyPath),null,DataProtectionScope.CurrentUser);try{return Encoding.UTF8.GetString(raw);}finally{CryptographicOperations.ZeroMemory(raw);}}
    public void Unbind(){if(File.Exists(KeyPath))File.Delete(KeyPath);}
    public DeepSeekChatState Load()
    {
        if(!File.Exists(StatePath))return new();var state=JsonSerializer.Deserialize<DeepSeekChatState>(File.ReadAllText(StatePath))??throw new JsonException();
        Validate(state);return state;
    }
    static void Validate(DeepSeekChatState state){if(state.Persona==null||state.Draft==null||state.Messages==null||state.Messages.Any(m=>m==null||m.Role is not ("user" or "assistant")||m.Content==null))throw new JsonException("聊天记录格式不正确");if(state.Model is not ("deepseek-flash" or "deepseek-v4-pro"))state.Model="deepseek-flash";}
    public void Save(DeepSeekChatState state){Directory.CreateDirectory(root);File.WriteAllText(StatePath+".tmp",JsonSerializer.Serialize(state));if(File.Exists(StatePath))File.Copy(StatePath,StatePath+".bak",true);File.Move(StatePath+".tmp",StatePath,true);}
    public void Archive(DeepSeekChatState state){if(state.Messages.Count==0)return;string folder=Path.Combine(root,"chat-history");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".json"),JsonSerializer.Serialize(state));}
}
