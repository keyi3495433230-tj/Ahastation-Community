using System.Security.Cryptography;
using System.Text.Json;
using X = Microsoft.UI.Xaml;
using C = Microsoft.UI.Xaml.Controls;
namespace CardReview;
public sealed partial class ReviewForm
{
    readonly DeepSeekStore deepSeekStore=new(AppDataStorage.Root);
    readonly DeepSeekClient deepSeekClient=new();
    DeepSeekChatState deepSeekChat=new();
    ChatHistory chatHistory=new();
    C.ListView chatHistoryList=null!;
    bool chatLoading;
    C.Grid chatMainPage=null!,chatFullLayout=null!;
    C.StackPanel chatActions=null!;
    C.TextBlock chatHistoryExpand=null!;
    C.TextBlock chatHeading=null!;
    C.StackPanel chatMessages=null!;
    C.ScrollViewer chatScroll=null!;
    X.FrameworkElement? chatScrollTarget,chatLatestReply;
    C.TextBox chatInput=null!,chatPersona=null!;
    C.TextBlock chatStatus=null!,deepSeekKeyStatus=null!;
    C.Button chatSend=null!,chatNew=null!,chatSavePersona=null!;
    C.Button chatSettingsButton=null!;
    C.ComboBox chatModel=null!;
    C.ToggleSwitch chatMemory=null!;
    C.TextBlock chatMemoryHint=null!;
    CancellationTokenSource? chatCancellation;
    readonly System.Windows.Forms.Timer chatDraftSaveTimer=new(){Interval=350};
    bool chatBusy,chatReadOnly;
    C.Border BuildDeepSeekSettings()
    {
        var body=Stack(10);body.Children.Add(TextBlock("DeepSeek API",22));
        deepSeekKeyStatus=TextBlock(deepSeekStore.HasKey?"已绑定 API Key（仅本机加密保存）":"尚未绑定 API Key",14);body.Children.Add(deepSeekKeyStatus);
        var key=new C.PasswordBox{Header="API Key",PlaceholderText="输入 DeepSeek 平台的 API Key"};body.Children.Add(key);
        body.Children.Add(NativeButton("保存 API Key",()=>{
            try{deepSeekStore.Bind(key.Password);key.Password="";deepSeekKeyStatus.Text="已绑定 API Key，可以前往聊天";if(chatStatus!=null)chatStatus.Text="API Key 已绑定，输入消息开始聊天。";}
            catch(Exception ex) when(ex is ArgumentException or IOException or UnauthorizedAccessException or CryptographicException){deepSeekKeyStatus.Text="绑定失败："+ex.Message;}
        },true));
        var verify=NativeButton("测试连接",()=>{});
        verify.Click+=async(_,_)=>{verify.IsEnabled=false;deepSeekKeyStatus.Text="正在验证密钥…";try{using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));await deepSeekClient.VerifyAsync(deepSeekStore.ReadKey(),timeout.Token);deepSeekKeyStatus.Text="连接成功，API Key 可用";}catch(Exception ex){deepSeekKeyStatus.Text=ChatError(ex);}finally{verify.IsEnabled=true;}};body.Children.Add(verify);
        body.Children.Add(NativeButton("解除绑定",()=>{try{deepSeekStore.Unbind();deepSeekKeyStatus.Text="已解除绑定";}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){deepSeekKeyStatus.Text="解除失败："+ex.Message;}}));
        body.Children.Add(NativeButton("前往聊天",()=>NativeSelectPage(7)));
        body.Children.Add(TextBlock("接口：https://api.deepseek.com · 消息和人设会发送给 DeepSeek，调用按其账户计费。密钥由 Windows 当前账户加密。",12));return Surface(body);
    }
    C.Grid BuildNativeChat()
    {
        string loadError="";try{chatHistory=deepSeekStore.LoadHistory();deepSeekChat=chatHistory.Conversations.First(c=>c.Id==chatHistory.ActiveId).State;}catch(Exception ex) when(ex is IOException or JsonException or UnauthorizedAccessException){chatReadOnly=true;loadError="聊天记录无法读取，已保留原文件并暂停编辑。";}
        var page=chatMainPage=GridRows(Auto,Star,Auto);page.Padding=new X.Thickness(24);var header=Stack(8);
        var titleRow=new C.Grid();titleRow.ColumnDefinitions.Add(new(){Width=Star});titleRow.ColumnDefinitions.Add(new(){Width=Auto});chatHeading=TextBlock("聊天 · DeepSeek",30);Put(titleRow,chatHeading);
        chatSettingsButton=NativeButton("聊天设置",()=>{});chatSettingsButton.VerticalAlignment=X.VerticalAlignment.Center;Put(titleRow,chatSettingsButton,0,1);header.Children.Add(titleRow);
        var settingsBody=Stack(12);settingsBody.Width=360;settingsBody.Children.Add(TextBlock("聊天设置",20));
        var settingsScroll=Scroll(settingsBody);settingsScroll.MaxHeight=480;
        var settingsFlyout=new C.Flyout{Content=settingsScroll,Placement=Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight};chatSettingsButton.Flyout=settingsFlyout;
        settingsFlyout.Opening+=(_,_)=>{settingsBody.Width=Math.Max(200,Math.Min(360,page.ActualWidth-64));settingsScroll.MaxHeight=Math.Max(180,page.ActualHeight-80);};
        var actions=chatActions=new C.StackPanel{Orientation=C.Orientation.Horizontal,Spacing=8};
        chatModel=new C.ComboBox{Width=180};chatModel.Items.Add("deepseek-flash");chatModel.Items.Add("deepseek-v4-pro");chatModel.SelectedItem=deepSeekChat.Model;chatModel.IsEnabled=!chatReadOnly;
        chatModel.SelectionChanged+=(_,_)=>{if(chatLoading)return;deepSeekChat.Model=(string)chatModel.SelectedItem;SaveChat();};actions.Children.Add(chatModel);
        chatNew=NativeButton("新对话",NewChatConversation);chatNew.IsEnabled=!chatReadOnly;actions.Children.Add(chatNew);actions.Children.Add(NativeButton("API 设置",()=>NativeSelectPage(6)));header.Children.Add(actions);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(chatModel,"DeepSeek 模型");
        chatMemory=new C.ToggleSwitch{Header="开启记忆",OnContent="已开启 · 记住最近20轮对话",OffContent="已关闭 · 每条消息独立回答",IsOn=deepSeekChat.MemoryEnabled,IsEnabled=!chatReadOnly};
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(chatMemory,"开启记忆");
        chatMemory.Toggled+=(_,_)=>{if(chatLoading)return;deepSeekChat.MemoryEnabled=chatMemory.IsOn;UpdateChatMemoryHint();if(SaveChat())chatStatus.Text="记忆设置已保存，从下一条消息开始生效。";};settingsBody.Children.Add(chatMemory);
        var personaBody=Stack(8);chatPersona=new C.TextBox{Text=deepSeekChat.Persona,AcceptsReturn=true,TextWrapping=X.TextWrapping.Wrap,Height=100,MaxLength=8000,IsEnabled=!chatReadOnly};Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(chatPersona,"聊天人设");personaBody.Children.Add(chatPersona);
        chatSavePersona=NativeButton("保存人设",()=>{deepSeekChat.Persona=chatPersona.Text.Trim();if(SaveChat())chatStatus.Text="人设已保存，从下一条消息开始生效。";});chatSavePersona.IsEnabled=!chatReadOnly;personaBody.Children.Add(chatSavePersona);
        settingsBody.Children.Add(TextBlock("聊天人设 · 设置身份、语气与回答方式",14));settingsBody.Children.Add(personaBody);
        chatStatus=TextBlock(loadError.Length>0?loadError:deepSeekStore.HasKey?"准备好了，输入消息开始聊天。":"请先到设置中绑定 DeepSeek API Key。",13);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(chatStatus,Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);Put(page,header);
        chatMessages=Stack(12);chatMessages.Padding=new X.Thickness(0,8,0,8);chatScroll=Scroll(chatMessages);Put(page,chatScroll,1);
        chatMessages.LayoutUpdated+=(_,_)=>ApplyChatScrollTarget();
        var compose=Stack(8);chatInput=new C.TextBox{PlaceholderText="输入消息…（Enter 发送，Shift+Enter 换行）",Text=deepSeekChat.Draft,AcceptsReturn=true,TextWrapping=X.TextWrapping.Wrap,Height=90,MaxLength=16000,IsEnabled=!chatReadOnly};
        chatInput.TextChanged+=(_,_)=>{if(chatLoading)return;deepSeekChat.Draft=chatInput.Text;chatDraftSaveTimer.Stop();chatDraftSaveTimer.Start();};chatInput.PreviewKeyDown+=async(_,e)=>{if(e.Key==Windows.System.VirtualKey.Enter&&(ModifierKeys&Keys.Shift)==0){e.Handled=true;await SendChatAsync();}};
        var editor=new C.Grid{ColumnSpacing=10};editor.ColumnDefinitions.Add(new(){Width=Star});editor.ColumnDefinitions.Add(new(){Width=Auto});
        chatInput.BorderThickness=new X.Thickness(0);chatInput.Background=Brush(Color.Transparent);Put(editor,chatInput);
        chatSend=NativeButton("发送",async()=>{if(chatBusy){chatCancellation?.Cancel();chatStatus.Text="正在停止回复…";chatSend.IsEnabled=false;}else await SendChatAsync();},true);
        chatSend.Width=chatSend.Height=chatSend.MinWidth=chatSend.MinHeight=44;chatSend.Padding=new X.Thickness(0);chatSend.CornerRadius=new X.CornerRadius(22);chatSend.HorizontalAlignment=X.HorizontalAlignment.Right;chatSend.VerticalAlignment=X.VerticalAlignment.Bottom;chatSend.Margin=new X.Thickness(0,0,4,4);UpdateChatSendButton();Put(editor,chatSend,0,1);
        var editorFrame=Surface(editor);editorFrame.Padding=new X.Thickness(8);editorFrame.CornerRadius=new X.CornerRadius(16);compose.Children.Add(editorFrame);
        compose.Children.Add(chatStatus);
        chatMemoryHint=TextBlock("",12);UpdateChatMemoryHint();compose.Children.Add(chatMemoryHint);Put(page,compose,2);
        var layout=chatFullLayout=new C.Grid();layout.ColumnDefinitions.Add(new(){Width=new X.GridLength(220)});layout.ColumnDefinitions.Add(new(){Width=new X.GridLength(8)});layout.ColumnDefinitions.Add(new(){Width=Star});
        var sidebar=GridRows(Auto,Star);sidebar.Padding=new X.Thickness(12,24,8,12);
        C.TextBlock expandHistory=null!;ChatHistoryDivider divider=null!;bool dragging=false;double dragWidth=0,dragStartWidth=0;int dragStartX=0;var dividerDragTimer=new System.Windows.Forms.Timer{Interval=16};
        C.TextBlock HistoryTitle(Action action,string name){var text=TextBlock("",20);text.HorizontalAlignment=X.HorizontalAlignment.Left;var link=new Microsoft.UI.Xaml.Documents.Hyperlink{TextDecorations=Windows.UI.Text.TextDecorations.None};link.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run{Text="历史对话"});link.Click+=(_,_)=>action();text.Inlines.Add(link);Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(link,name);return text;}
        var collapseHistory=HistoryTitle(()=>SetHistoryCollapsed(true),"历史对话 · 收起");Put(sidebar,collapseHistory);
        expandHistory=chatHistoryExpand=HistoryTitle(()=>SetHistoryCollapsed(false),"历史对话 · 展开");header.Children.Insert(0,expandHistory);
        chatHistoryList=new C.ListView{SelectionMode=C.ListViewSelectionMode.Single,IsEnabled=!chatReadOnly};Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(chatHistoryList,"历史对话");
        chatHistoryList.SelectionChanged+=(_,_)=>{if(!chatLoading&&chatHistoryList.SelectedItem is C.ListViewItem item&&item.Tag is string id)SwitchChatConversation(id);};Put(sidebar,chatHistoryList,1);
        divider=new ChatHistoryDivider{Width=8,HorizontalContentAlignment=X.HorizontalAlignment.Stretch,VerticalContentAlignment=X.VerticalAlignment.Stretch};var dividerHitArea=new C.Grid{Background=Brush(Color.Transparent)};dividerHitArea.Children.Add(new C.Border{Width=1,Background=nativeStroke,HorizontalAlignment=X.HorizontalAlignment.Center});divider.Content=dividerHitArea;Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(divider,"拖动调整历史对话宽度");

        double LimitWidth()=>Math.Max(120,Math.Min(420,layout.ActualWidth-340));
        void UpdateHistoryLayout(){sidebar.Visibility=chatHistory.HistoryCollapsed?X.Visibility.Collapsed:X.Visibility.Visible;divider.Visibility=sidebar.Visibility;expandHistory.Visibility=chatHistory.HistoryCollapsed?X.Visibility.Visible:X.Visibility.Collapsed;double preferred=double.IsFinite(chatHistory.HistoryWidth)?chatHistory.HistoryWidth:220;layout.ColumnDefinitions[0].Width=new X.GridLength(chatHistory.HistoryCollapsed?0:dragging?Math.Clamp(dragWidth,0,LimitWidth()):Math.Clamp(preferred,120,LimitWidth()));layout.ColumnDefinitions[1].Width=new X.GridLength(chatHistory.HistoryCollapsed?0:8);}
        void SetHistoryCollapsed(bool collapsed){chatHistory.HistoryCollapsed=collapsed;UpdateHistoryLayout();SaveChat();}
        divider.PointerPressed+=(_,e)=>{if(!e.GetCurrentPoint(divider).Properties.IsLeftButtonPressed)return;dragging=true;dragWidth=dragStartWidth=layout.ColumnDefinitions[0].ActualWidth;dragStartX=Control.MousePosition.X;dividerDragTimer.Start();divider.CapturePointer(e.Pointer);e.Handled=true;};
        divider.PointerMoved+=(_,e)=>{if(!dragging)return;UpdateDrag();e.Handled=true;};
        void UpdateDrag(){dragWidth=Math.Clamp(dragStartWidth+(Control.MousePosition.X-dragStartX)/(nativeRoot.XamlRoot?.RasterizationScale??1),0,LimitWidth());UpdateHistoryLayout();}
        dividerDragTimer.Tick+=(_,_)=>{if(!dragging)return;if((Control.MouseButtons&MouseButtons.Left)==0)FinishDrag();else UpdateDrag();};
        void FinishDrag(){if(!dragging)return;UpdateDrag();dragging=false;dividerDragTimer.Stop();if(dragWidth<72)chatHistory.HistoryCollapsed=true;else chatHistory.HistoryWidth=Math.Clamp(dragWidth,120,LimitWidth());UpdateHistoryLayout();SaveChat();}
        divider.PointerReleased+=(_,e)=>{FinishDrag();divider.ReleasePointerCapture(e.Pointer);e.Handled=true;};divider.PointerCaptureLost+=(_,_)=>{if((Control.MouseButtons&MouseButtons.Left)==0)FinishDrag();};divider.PointerCanceled+=(_,_)=>FinishDrag();
        Put(layout,sidebar);Put(layout,divider,0,1);Put(layout,page,0,2);layout.SizeChanged+=(_,_)=>UpdateHistoryLayout();UpdateHistoryLayout();
        FormClosing+=(_,_)=>{dividerDragTimer.Stop();dividerDragTimer.Dispose();};
        chatDraftSaveTimer.Tick+=(_,_)=>{chatDraftSaveTimer.Stop();SaveChat();};
        FormClosing+=(_,_)=>{chatCancellation?.Cancel();chatDraftSaveTimer.Stop();if(!chatReadOnly)deepSeekChat.Draft=chatInput.Text;SaveChat();chatDraftSaveTimer.Dispose();};RenderChat();RefreshChatHistory();return layout;
    }
    void RefreshChatHistory()
    {
        if(chatHistoryList==null)return;chatLoading=true;
        try{chatHistoryList.Items.Clear();foreach(var c in chatHistory.Conversations){
            var body=Stack(4);body.Children.Add(TextBlock(c.Title,14));body.Children.Add(TextBlock(c.CreatedAt.ToString("MM-dd HH:mm"),11));
            var item=new C.ListViewItem{Content=body,Tag=c.Id,HorizontalContentAlignment=X.HorizontalAlignment.Stretch};Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item,c.Title);
            var menu=new C.MenuFlyout();var delete=new C.MenuFlyoutItem{Text="删除对话"};delete.Click+=(_,_)=>DeleteChatConversation(c.Id);menu.Items.Add(delete);item.ContextFlyout=menu;
            chatHistoryList.Items.Add(item);if(c.Id==chatHistory.ActiveId)chatHistoryList.SelectedItem=item;
        }}finally{chatLoading=false;}
    }
    void ShowChatConversation()
    {
        DisplayChatState(chatHistory.Conversations.First(c=>c.Id==chatHistory.ActiveId).State);RefreshChatHistory();
    }
    void DisplayChatState(DeepSeekChatState state)
    {
        chatLoading=true;try{deepSeekChat=state;chatInput.Text=deepSeekChat.Draft;chatPersona.Text=deepSeekChat.Persona;chatModel.SelectedItem=deepSeekChat.Model;chatMemory.IsOn=deepSeekChat.MemoryEnabled;}finally{chatLoading=false;}
        UpdateChatMemoryHint();RenderChat();
    }
    void SwitchChatConversation(string id)
    {
        if(chatBusy||chatReadOnly||id==chatHistory.ActiveId)return;
        if(!SaveChat()){RefreshChatHistory();return;}var previous=chatHistory.ActiveId;chatHistory.ActiveId=id;
        if(!SaveChat()){chatHistory.ActiveId=previous;RefreshChatHistory();return;}ShowChatConversation();chatStatus.Text="已切换对话。";
    }
    void NewChatConversation()
    {
        if(chatBusy||chatReadOnly||!SaveChat())return;
        var previous=chatHistory.ActiveId;var conversation=new ChatConversation{State=new(){Persona=deepSeekChat.Persona,Model=deepSeekChat.Model,MemoryEnabled=deepSeekChat.MemoryEnabled}};
        chatHistory.Conversations.Insert(0,conversation);chatHistory.ActiveId=conversation.Id;
        if(!SaveChat()){chatHistory.Conversations.Remove(conversation);chatHistory.ActiveId=previous;RefreshChatHistory();return;}
        ShowChatConversation();chatStatus.Text="已新建对话，之前的对话可在左侧选择。";
    }
    void DeleteChatConversation(string id)
    {
        if(chatBusy||chatReadOnly)return;var index=chatHistory.Conversations.FindIndex(c=>c.Id==id);if(index<0)return;
        var removed=chatHistory.Conversations[index];var previous=chatHistory.ActiveId;chatHistory.Conversations.RemoveAt(index);ChatConversation? replacement=null;
        if(chatHistory.Conversations.Count==0){replacement=new(){State=new(){Persona=deepSeekChat.Persona,Model=deepSeekChat.Model,MemoryEnabled=deepSeekChat.MemoryEnabled}};chatHistory.Conversations.Add(replacement);}
        if(id==previous)chatHistory.ActiveId=chatHistory.Conversations[Math.Min(index,chatHistory.Conversations.Count-1)].Id;
        if(!SaveChat()){if(replacement!=null)chatHistory.Conversations.Remove(replacement);chatHistory.Conversations.Insert(index,removed);chatHistory.ActiveId=previous;RefreshChatHistory();return;}
        ShowChatConversation();chatStatus.Text="对话已删除。";
    }
    void UpdateChatMemoryHint()
    {
        chatMemoryHint.Text=(deepSeekChat.MemoryEnabled?"记忆已开启：发送当前人设、当前消息和本对话最近20轮记录。":"记忆已关闭：只发送当前人设和当前消息，不发送历史对话。")+"聊天记录仍会保存，重新开启后可继续引用；新对话会清空上下文。学习资料不会自动发送。";
    }
    bool SaveChat()
    {
        chatDraftSaveTimer.Stop();
        if(chatReadOnly)return false;try{deepSeekStore.SaveHistory(chatHistory);if(chatHistoryList!=null)foreach(C.ListViewItem item in chatHistoryList.Items){var c=chatHistory.Conversations.FirstOrDefault(c=>c.Id==(string)item.Tag);if(c!=null){((C.TextBlock)((C.StackPanel)item.Content).Children[0]).Text=c.Title;Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item,c.Title);}}return true;}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){if(chatStatus!=null)chatStatus.Text="聊天未能保存："+ex.Message;return false;}
    }
    void RenderChat()
    {
        chatScrollTarget=null;chatLatestReply=null;chatMessages.Children.Clear();
        if(deepSeekChat.Messages.Count==0)chatMessages.Children.Add(TextBlock("你可以聊学习安排、请它解释一个概念，或展开上方的“聊天人设”设置交流风格。",16));
        foreach(var message in deepSeekChat.Messages){var row=Stack(5);row.Children.Add(TextBlock(message.Role=="user"?"你":"DeepSeek",12));var text=TextBlock(message.Content,15);text.IsTextSelectionEnabled=true;row.Children.Add(text);var bubble=Surface(row,message.Role=="user");chatMessages.Children.Add(bubble);if(message.Role=="assistant")chatLatestReply=bubble;}
        RevealLatestChatReply();
        MotionEnter(chatMessages,6);
    }
    void RevealLatestChatReply()
    {
        QueueChatScroll(chatLatestReply??chatMessages.Children.LastOrDefault() as X.FrameworkElement);
    }
    void QueueChatScroll(X.FrameworkElement? target)
    {
        chatScrollTarget=target;
        chatScroll.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,ApplyChatScrollTarget);
    }
    void ApplyChatScrollTarget()
    {
        var target=chatScrollTarget;if(target==null||!target.IsLoaded||target.ActualHeight<=0||chatScroll.ViewportHeight<=0)return;
        // Resolve layout before asking the scroll viewer to reveal the reply's start.
        chatScrollTarget=null;chatScroll.UpdateLayout();target.StartBringIntoView(new X.BringIntoViewOptions{AnimationDesired=MotionEnabled,VerticalAlignmentRatio=0,TargetRect=new Windows.Foundation.Rect(0,0,Math.Max(1,target.ActualWidth),Math.Min(target.ActualHeight,40))});
    }
    static string ChatError(Exception ex)=>ex switch{
        OperationCanceledException=>"请求已停止或超时，输入内容已保留，可重新发送。",
        System.Net.Http.HttpRequestException=>"网络连接失败，请检查网络后重试。",
        CryptographicException=>"此 API Key 无法在当前 Windows 账户解密，请重新绑定。",
        JsonException=>"DeepSeek 返回的数据无法读取，请重试。",
        _=>ex.Message
    };
    async Task SendChatAsync()
    {
        if(chatBusy){chatStatus.Text="DeepSeek 正在回复，请稍候，或点击“停止回复”。";return;}
        if(chatReadOnly){chatStatus.Text="聊天记录无法读取，已暂停发送以保护原文件。";return;}
        if(string.IsNullOrWhiteSpace(chatInput.Text)){chatStatus.Text="请先输入要发送的消息。";return;}
        string prompt=chatInput.Text.Trim();string key;try{key=deepSeekStore.ReadKey();}catch(Exception ex){chatStatus.Text=ChatError(ex);return;}
        var requestState=deepSeekChat;if(!SaveChat())return;RefreshChatHistory();
        chatBusy=true;chatHistoryList.IsEnabled=false;chatInput.IsEnabled=chatNew.IsEnabled=chatModel.IsEnabled=chatSavePersona.IsEnabled=chatPersona.IsEnabled=chatMemory.IsEnabled=false;
        chatCancellation=new CancellationTokenSource();chatStatus.Text="消息已发送，DeepSeek 正在回复…（点击右侧圆形按钮可停止）";UpdateChatSendButton();
        var pending=TextBlock("你："+prompt+"\n\n等待 DeepSeek 回复…",15);var pendingBubble=Surface(pending,true);chatMessages.Children.Add(pendingBubble);QueueChatScroll(pendingBubble);
        try{
            string reply=await deepSeekClient.ReplyAsync(key,requestState,prompt,chatCancellation.Token);
            if(chatCancellation.IsCancellationRequested){chatStatus.Text="回复已停止，输入内容已保留。";return;}
            requestState.Messages.Add(new("user",prompt));requestState.Messages.Add(new("assistant",reply));requestState.Draft="";if(ReferenceEquals(deepSeekChat,requestState))chatInput.Text="";
            if(SaveChat())chatStatus.Text="回复完成 · 已保存";RefreshChatHistory();
        }catch(Exception ex){if(!IsDisposed&&!Disposing)chatStatus.Text=ex is OperationCanceledException&&chatCancellation.IsCancellationRequested?"回复已停止，输入内容已保留。":ChatError(ex);}
        finally{chatCancellation.Dispose();chatCancellation=null;chatBusy=false;if(!IsDisposed){UpdateChatSendButton();chatHistoryList.IsEnabled=true;chatInput.IsEnabled=chatNew.IsEnabled=chatModel.IsEnabled=chatSavePersona.IsEnabled=chatPersona.IsEnabled=chatMemory.IsEnabled=true;RenderChat();}}
    }
    void UpdateChatSendButton()
    {
        chatSend.Content=new C.FontIcon{Glyph=chatBusy?"\uE71A":"\uE74A",FontSize=20};chatSend.IsEnabled=!chatReadOnly;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(chatSend,chatBusy?"停止回复":"发送");
        C.ToolTipService.SetToolTip(chatSend,chatBusy?"停止回复":"发送（Enter）；Shift+Enter 换行");
    }
}
