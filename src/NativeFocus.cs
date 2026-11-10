using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using X = Microsoft.UI.Xaml;
using C = Microsoft.UI.Xaml.Controls;

namespace CardReview;

public sealed partial class ReviewForm
{
    const string GazeOrigin = "https://ahastation-gaze.local/";
    C.TextBlock nativeFocusStatus = null!, nativeFocusElapsed = null!, nativeFocusLookDuration = null!;
    C.Button nativeFocusToggle = null!, nativeFocusStop = null!, nativeFocusCalibrate = null!;
    C.Border nativeGazeSlot = null!;
    C.WebView2 nativeGazeView = null!;
    readonly System.Windows.Forms.Timer nativeFocusTimer = new() { Interval = 200 };
    bool nativeFocusActive, nativeFocusStarting, nativeFocusCounting, nativeFocusDecreasing, nativeFocusCalibrating;
    bool nativeFocusScreenDeductEnabled = true;
    bool nativeFocusNeedsCalibration = true, nativeGazeConfigured, nativeFocusShuttingDown;
    TimeSpan nativeFocusAccumulated;
    DateTime nativeFocusRunStartedUtc;
    long nativeFocusLastFrameTick;
    int nativeGazeEpoch;
    string nativeGazeState = "unknown", nativeGazeReason = "请先完成视线校准";
    FormWindowState nativeGazePreviousWindowState;
    TaskCompletionSource<bool>? nativeGazeReady, nativeGazeStarted;

    C.ScrollViewer BuildNativeFocus()
    {
        var body=Stack(16);body.HorizontalAlignment=X.HorizontalAlignment.Left;body.Margin=new X.Thickness(24);
        body.Children.Add(TextBlock("专注模式",30));
        body.Children.Add(TextBlock("根据双眼虹膜位置和头部姿态估算视线。看屏幕时按下方开关暂停或扣减，移开视线后专注时间增加。首次开启请跟随圆点完成校准。",14));
        var screenTimerOption=new C.ToggleSwitch{Header="盯着屏幕减少专注时间",IsOn=nativeFocusScreenDeductEnabled,OnContent="开",OffContent="关"};
        try
        {
            string setting=Path.Combine(AppDataStorage.Root,"focus-screen-deduct.txt");
            if(File.Exists(setting)){nativeFocusScreenDeductEnabled=File.ReadAllText(setting).Trim()!="off";screenTimerOption.IsOn=nativeFocusScreenDeductEnabled;}
        }
        catch(IOException){}
        screenTimerOption.Toggled+=(_,_)=>
        {
            nativeFocusScreenDeductEnabled=screenTimerOption.IsOn;
            try{Directory.CreateDirectory(AppDataStorage.Root);File.WriteAllText(Path.Combine(AppDataStorage.Root,"focus-screen-deduct.txt"),nativeFocusScreenDeductEnabled?"on":"off");}
            catch(IOException ex){nativeFocusStatus.Text="无法保存此设置："+ex.Message;}
            if(nativeFocusActive)NativeFocusTick(this,EventArgs.Empty);
        };
        var screenTimerSetting=Stack(4);screenTimerSetting.Children.Add(screenTimerOption);screenTimerSetting.Children.Add(TextBlock("开启：看屏幕时扣减专注时间；关闭：看屏幕时暂停计时。",12));body.Children.Add(Surface(screenTimerSetting));
        var statusCard=Stack(12);
        statusCard.Children.Add(TextBlock("本次专注",18));
        nativeFocusElapsed=TextBlock("00:00:00",44);statusCard.Children.Add(nativeFocusElapsed);
        nativeFocusStatus=TextBlock("准备开始，摄像头尚未开启。",16);statusCard.Children.Add(nativeFocusStatus);
        nativeFocusLookDuration=TextBlock("视线状态：等待校准",13);statusCard.Children.Add(nativeFocusLookDuration);
        nativeFocusToggle=NativeButton("开始专注并开启摄像头",NativeFocusToggleClicked,true);
        nativeFocusStop=NativeButton("结束专注",NativeFocusStopClicked);nativeFocusStop.IsEnabled=false;
        nativeFocusCalibrate=NativeButton("重新校准视线",async()=>await BeginNativeGazeCalibrationAsync());nativeFocusCalibrate.IsEnabled=false;
        statusCard.Children.Add(nativeFocusToggle);
        var secondary=new C.Grid{ColumnSpacing=8};secondary.ColumnDefinitions.Add(new C.ColumnDefinition{Width=Star});secondary.ColumnDefinitions.Add(new C.ColumnDefinition{Width=Star});
        Put(secondary,nativeFocusCalibrate);Put(secondary,nativeFocusStop,0,1);statusCard.Children.Add(secondary);body.Children.Add(Surface(statusCard));
        nativeGazeSlot=new C.Border{Height=310,Child=TextBlock("开启后显示双眼跟踪预览",14)};body.Children.Add(Surface(nativeGazeSlot));
        body.Children.Add(TextBlock("识别和校准均在本机完成，不保存或上传摄像头画面。普通摄像头的视线估算存在误差；眨眼、遮挡、多人入镜或坐姿变化过大时，会暂时停表并提示调整。",12));
        var page=Scroll(body);page.HorizontalContentAlignment=X.HorizontalAlignment.Left;
        page.SizeChanged+=(_,_)=>body.Width=Math.Max(180,Math.Min(900,page.ActualWidth-48));
        nativeGazeView=new C.WebView2{Visibility=X.Visibility.Collapsed,HorizontalAlignment=X.HorizontalAlignment.Left,VerticalAlignment=X.VerticalAlignment.Top};
        nativeRoot.Children.Add(nativeGazeView);
        nativeRoot.LayoutUpdated+=(_,_)=>PositionNativeGazeView();
        FormClosing+=(_,_)=>{nativeFocusShuttingDown=true;nativeGazeView.Close();};
        return page;
    }

    void SetNativeGazeFrameState(bool lookingAtScreen)
    {
        if(nativeGazeSlot is null)return;
        var color=lookingAtScreen?Brush(Color.FromArgb(196,43,28)):null;
        nativeGazeSlot.Background=color;nativeGazeSlot.BorderBrush=color;
    }

    void PositionNativeGazeView()
    {
        if(nativeFocusShuttingDown||nativeGazeView==null||nativeGazeSlot==null)return;
        bool loaded=nativeGazeReady?.Task.IsCompletedSuccessfully==true;
        bool visible=nativeFocusStarting||loaded&&(nativePage==3||nativeFocusActive);
        nativeGazeView.Visibility=visible?X.Visibility.Visible:X.Visibility.Collapsed;
        if(!visible)return;
        double x=0,y=0,w=1,h=1;
        bool interactive=nativeFocusCalibrating||nativePage==3;
        if(nativeFocusCalibrating){w=nativeRoot.ActualWidth;h=nativeRoot.ActualHeight;}
        else if(nativePage==3&&nativeGazeSlot.ActualWidth>0)
        {
            var p=nativeGazeSlot.TransformToVisual(nativeRoot).TransformPoint(new Windows.Foundation.Point());
            x=p.X;y=p.Y;w=nativeGazeSlot.ActualWidth;h=nativeGazeSlot.ActualHeight;
            var page=nativePages[3];
            var viewport=page.TransformToVisual(nativeRoot).TransformPoint(new Windows.Foundation.Point());
            // Hide a partially scrolled preview so its overlay cannot cover page controls.
            interactive=y>=viewport.Y&&y+h<=viewport.Y+page.ActualHeight;
        }
        nativeGazeView.IsHitTestVisible=interactive;nativeGazeView.Opacity=interactive?1:0;
        if(nativeGazeView.Width!=Math.Max(1,w))nativeGazeView.Width=Math.Max(1,w);
        if(nativeGazeView.Height!=Math.Max(1,h))nativeGazeView.Height=Math.Max(1,h);
        var margin=new X.Thickness(x,y,0,0);if(nativeGazeView.Margin!=margin)nativeGazeView.Margin=margin;
    }

    async Task EnsureNativeGazeAsync()
    {
        if(nativeGazeReady?.Task.IsCompletedSuccessfully==true)return;
        string assets=Path.Combine(AppContext.BaseDirectory,"Assets","Gaze");
        if(!File.Exists(Path.Combine(assets,"face_landmarker.task")))throw new InvalidOperationException("视线识别模型缺失，请使用完整的软件发布目录。");
        PositionNativeGazeView();
        if(!nativeGazeConfigured)
        {
            var environment=await CoreWebView2Environment.CreateWithOptionsAsync(null,Path.Combine(AppDataStorage.Root,"GazeWebView"),null);
            await nativeGazeView.EnsureCoreWebView2Async(environment);
            var core=nativeGazeView.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping("ahastation-gaze.local",assets,CoreWebView2HostResourceAccessKind.Deny);
            core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.IsStatusBarEnabled=false;
            core.PermissionRequested+=(_,e)=>
            {
                e.State=e.Uri.StartsWith(GazeOrigin,StringComparison.OrdinalIgnoreCase)&&e.PermissionKind==CoreWebView2PermissionKind.Camera&&(nativeFocusStarting||nativeFocusActive)?CoreWebView2PermissionState.Allow:CoreWebView2PermissionState.Deny;
                e.SavesInProfile=false;
            };
            core.NavigationStarting+=(_,e)=>{if(!e.Uri.StartsWith(GazeOrigin,StringComparison.OrdinalIgnoreCase))e.Cancel=true;};
            core.NewWindowRequested+=(_,e)=>e.Handled=true;
            core.WebMessageReceived+=(_,e)=>
            {
                if(!e.Source.StartsWith(GazeOrigin,StringComparison.OrdinalIgnoreCase)||nativeFocusShuttingDown)return;
                try{using var json=JsonDocument.Parse(e.WebMessageAsJson);OnNativeGazeMessage(json.RootElement);}catch(JsonException){}
            };
            core.ProcessFailed+=(_,_)=>
            {
                PauseNativeFocusCounting(DateTime.UtcNow);nativeFocusActive=false;nativeFocusNeedsCalibration=true;nativeFocusTimer.Stop();
                nativeGazeReady=null;nativeGazeState="unknown";SetNativeGazeFrameState(false);EndNativeGazeCalibration();
                nativeFocusStatus.Text="视线识别进程已停止，请重新开启摄像头。";nativeFocusToggle.Content="重新开启摄像头";
                nativeGazeStarted?.TrySetException(new InvalidOperationException("视线识别进程已停止"));
            };
            nativeGazeConfigured=true;
        }
        nativeGazeReady=new(TaskCreationOptions.RunContinuationsAsynchronously);
        nativeGazeView.CoreWebView2.Navigate(GazeOrigin+"index.html");
        await nativeGazeReady.Task.WaitAsync(TimeSpan.FromSeconds(20));
        ApplyNativeGazeTheme();
    }

    void ApplyNativeGazeTheme()
    {
        if(nativeGazeReady?.Task.IsCompletedSuccessfully!=true||nativeFocusShuttingDown)return;
        nativeGazeView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new{command="theme",dark=darkTheme,accent=ColorTranslator.ToHtml(activeThemeColor)}));
    }

    void OnNativeGazeMessage(JsonElement message)
    {
        string type=message.GetProperty("type").GetString()??"";
        if(type=="ready"){nativeGazeReady?.TrySetResult(true);return;}
        if(!message.TryGetProperty("epoch",out var session)||session.GetInt32()!=nativeGazeEpoch)return;
        string reason=message.TryGetProperty("reason",out var text)?text.GetString()??"":"";
        switch(type)
        {
            case "started":nativeFocusNeedsCalibration=message.GetProperty("needsCalibration").GetBoolean();nativeGazeStarted?.TrySetResult(true);break;
            case "state":
                nativeGazeState=message.GetProperty("state").GetString()??"unknown";nativeGazeReason=reason;nativeFocusLastFrameTick=Environment.TickCount64;
                SetNativeGazeFrameState(nativeGazeState=="screen");
                NativeFocusTick(this,EventArgs.Empty);break;
            case "calibrated":
                nativeFocusNeedsCalibration=false;nativeGazeState="unknown";nativeGazeReason="校准完成，正在确认视线";SetNativeGazeFrameState(false);
                EndNativeGazeCalibration();break;
            case "calibrationFailed":
                nativeFocusNeedsCalibration=!message.TryGetProperty("hasProfile",out var hasProfile)||!hasProfile.GetBoolean();nativeGazeReason=reason;nativeGazeState="unknown";SetNativeGazeFrameState(false);EndNativeGazeCalibration();
                nativeFocusStatus.Text=reason;break;
            case "error":
                PauseNativeFocusCounting(DateTime.UtcNow);nativeFocusActive=false;nativeFocusTimer.Stop();nativeGazeState="unknown";
                SetNativeGazeFrameState(false);
                EndNativeGazeCalibration();nativeFocusStatus.Text=reason;nativeFocusToggle.Content="重新开启摄像头";
                nativeGazeStarted?.TrySetException(new InvalidOperationException(reason));break;
        }
    }

    async void NativeFocusToggleClicked()
    {
        if(nativeFocusStarting)return;
        if(nativeFocusActive){await PauseNativeFocusAsync();return;}
        await StartNativeFocusAsync();
    }

    async Task StartNativeFocusAsync()
    {
        nativeFocusStarting=true;nativeFocusToggle.IsEnabled=false;nativeFocusStop.IsEnabled=false;nativeFocusCalibrate.IsEnabled=false;
        nativeFocusStatus.Text="正在加载本地视线识别，请稍候…";
        try
        {
            await EnsureNativeGazeAsync();if(nativeFocusShuttingDown)return;
            nativeGazeState="unknown";nativeGazeReason="正在识别双眼";nativeFocusLastFrameTick=0;
            nativeGazeStarted=new(TaskCreationOptions.RunContinuationsAsynchronously);
            nativeGazeView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new{command="start",epoch=++nativeGazeEpoch}));
            await nativeGazeStarted.Task.WaitAsync(TimeSpan.FromSeconds(60));if(nativeFocusShuttingDown)return;
            nativeFocusActive=true;nativeFocusCounting=false;nativeFocusTimer.Start();nativeFocusToggle.Content="暂停监测";
            if(nativeFocusNeedsCalibration)await BeginNativeGazeCalibrationAsync();
        }
        catch(Exception ex)
        {
            await DisposeNativeFocusCameraAsync();
            if(!nativeFocusShuttingDown)nativeFocusStatus.Text="无法开启视线识别："+ex.Message;
        }
        finally
        {
            nativeFocusStarting=false;
            if(!nativeFocusShuttingDown){nativeFocusToggle.IsEnabled=true;nativeFocusStop.IsEnabled=nativeFocusActive||nativeFocusAccumulated>TimeSpan.Zero;nativeFocusCalibrate.IsEnabled=nativeFocusActive;PositionNativeGazeView();}
        }
    }

    async Task BeginNativeGazeCalibrationAsync()
    {
        if(!nativeFocusActive||nativeFocusCalibrating)return;
        PauseNativeFocusCounting(DateTime.UtcNow);nativeFocusNeedsCalibration=true;nativeFocusCalibrating=true;nativeGazeState="calibrating";
        SetNativeGazeFrameState(false);
        nativeGazePreviousWindowState=WindowState;WindowState=FormWindowState.Maximized;nativeNav.IsHitTestVisible=false;
        PositionNativeGazeView();await Task.Delay(250);
        if(!nativeFocusActive||nativeFocusShuttingDown)return;
        try
        {
            nativeRoot.UpdateLayout();PositionNativeGazeView();
            var monitor=Screen.FromHandle(Handle).Bounds;var origin=nativeHost!.PointToScreen(Point.Empty);
            var geometry=new{left=origin.X-monitor.X,top=origin.Y-monitor.Y,width=nativeHost.ClientSize.Width,height=nativeHost.ClientSize.Height,screenWidth=monitor.Width,screenHeight=monitor.Height};
            nativeGazeView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new{command="calibrate",geometry}));
        }
        catch(Exception ex){nativeGazeReason="无法开始校准："+ex.Message;EndNativeGazeCalibration();}
    }

    void EndNativeGazeCalibration()
    {
        if(!nativeFocusCalibrating)return;
        nativeFocusCalibrating=false;nativeNav.IsHitTestVisible=true;
        if(!nativeFocusShuttingDown)WindowState=nativeGazePreviousWindowState;
        PositionNativeGazeView();
    }

    void NativeFocusTick(object? sender,EventArgs e)
    {
        if(!nativeFocusActive||nativeFocusShuttingDown)return;
        var now=DateTime.UtcNow;
        bool fresh=nativeFocusLastFrameTick>0&&Environment.TickCount64-nativeFocusLastFrameTick<2000;
        if(nativeFocusCalibrating||nativeFocusNeedsCalibration)
        {
            PauseNativeFocusCounting(now);nativeFocusLookDuration.Text="视线状态：等待完成校准";
            nativeFocusStatus.Text=nativeFocusCalibrating?"正在校准，暂不累计专注时间":string.IsNullOrWhiteSpace(nativeGazeReason)?"请先完成视线校准":nativeGazeReason;
        }
        else if(nativeGazeState=="screen")
        {
            if(!nativeFocusScreenDeductEnabled)
            {
                PauseNativeFocusCounting(now);nativeFocusLookDuration.Text="视线状态：正在看屏幕";
                nativeFocusStatus.Text="你正在看屏幕，专注计时已暂停。";
            }
            else
            {
                if(!nativeFocusCounting||!nativeFocusDecreasing)
                {
                    PauseNativeFocusCounting(now);nativeFocusRunStartedUtc=now;nativeFocusCounting=true;nativeFocusDecreasing=true;
                }
                nativeFocusLookDuration.Text="视线状态：正在看屏幕";
                nativeFocusStatus.Text="你心不在焉，专注时间正在减少。";
            }
        }
        else
        {
            if(!nativeFocusCounting||nativeFocusDecreasing)
            {
                PauseNativeFocusCounting(now);nativeFocusRunStartedUtc=now;nativeFocusCounting=true;nativeFocusDecreasing=false;
            }
            if(nativeGazeState=="away")
            {nativeFocusLookDuration.Text="视线状态：已移开屏幕";nativeFocusStatus.Text="专注计时中";}
            else
            {nativeFocusLookDuration.Text="视线状态：暂时无法判断，按专注计时";nativeFocusStatus.Text="眼睛眨动或暂时被遮挡，专注计时继续。";}
        }
        UpdateNativeFocusElapsed(now);
    }

    void PauseNativeFocusCounting(DateTime now)
    {
        if(!nativeFocusCounting)return;
        var elapsed=now-nativeFocusRunStartedUtc;
        nativeFocusAccumulated+=nativeFocusDecreasing?-elapsed:elapsed;
        if(nativeFocusAccumulated<TimeSpan.Zero)nativeFocusAccumulated=TimeSpan.Zero;
        nativeFocusCounting=false;nativeFocusDecreasing=false;
    }

    void UpdateNativeFocusElapsed(DateTime now)
    {
        var ticks=nativeFocusAccumulated.Ticks;
        if(nativeFocusCounting)ticks+=(nativeFocusDecreasing?-1:1)*(now-nativeFocusRunStartedUtc).Ticks;
        var elapsed=TimeSpan.FromTicks(Math.Max(0,ticks));
        nativeFocusElapsed.Text=$"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    async Task PauseNativeFocusAsync()
    {
        PauseNativeFocusCounting(DateTime.UtcNow);nativeFocusToggle.IsEnabled=false;
        await DisposeNativeFocusCameraAsync();if(nativeFocusShuttingDown)return;
        UpdateNativeFocusElapsed(DateTime.UtcNow);nativeFocusStatus.Text="专注监测已暂停，摄像头已关闭。";
        nativeFocusLookDuration.Text="视线状态：监测已暂停";nativeFocusToggle.Content="继续监测";nativeFocusToggle.IsEnabled=true;nativeFocusCalibrate.IsEnabled=false;
    }

    async void NativeFocusStopClicked()
    {
        nativeFocusToggle.IsEnabled=false;nativeFocusStop.IsEnabled=false;
        await DisposeNativeFocusCameraAsync();if(nativeFocusShuttingDown)return;
        nativeFocusAccumulated=TimeSpan.Zero;nativeFocusNeedsCalibration=true;UpdateNativeFocusElapsed(DateTime.UtcNow);
        nativeFocusStatus.Text="本次专注已结束，摄像头已关闭。";nativeFocusLookDuration.Text="视线状态：等待校准";
        nativeFocusToggle.Content="开始专注并开启摄像头";nativeFocusToggle.IsEnabled=true;nativeFocusCalibrate.IsEnabled=false;
    }

    async Task DisposeNativeFocusCameraAsync(bool clearCalibration=false)
    {
        PauseNativeFocusCounting(DateTime.UtcNow);nativeFocusActive=false;if(!nativeFocusShuttingDown)nativeFocusTimer.Stop();nativeGazeEpoch++;
        if(!nativeFocusShuttingDown&&nativeGazeReady?.Task.IsCompletedSuccessfully==true)
        {
            try{await nativeGazeView.CoreWebView2.ExecuteScriptAsync(clearCalibration?"window.gaze?.stop(true)":"window.gaze?.stop(false)");}catch{}
        }
        EndNativeGazeCalibration();nativeGazeState="unknown";SetNativeGazeFrameState(false);nativeFocusLastFrameTick=0;PositionNativeGazeView();
    }
}
