using Microsoft.Win32;
using System.Text.Json;
using C=Microsoft.UI.Xaml.Controls;
namespace CardReview;
public sealed class BackgroundPreferences
{
    public bool StartWithWindows { get; set; }
    public bool CloseToTray { get; set; } = true;
}
public sealed partial class ReviewForm
{
    const int ShowAppMessage=0x84A1,ExitAppMessage=0x84A2;
    bool exitRequested,backgroundReady;NotifyIcon? tray;BackgroundPreferences backgroundPreferences=new();
    string BackgroundPath=>Path.Combine(AppDataStorage.Root,"background-settings.json");
    void SaveBackground(){File.WriteAllText(BackgroundPath+".tmp",JsonSerializer.Serialize(backgroundPreferences));File.Move(BackgroundPath+".tmp",BackgroundPath,true);}
    void RegisterStartup(bool enabled)
    {
        using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");if(enabled)key.SetValue("AhastationCommunity","\""+Environment.ProcessPath+"\" --background");else key.DeleteValue("AhastationCommunity",false);
    }
    Microsoft.UI.Xaml.FrameworkElement BuildBackgroundSettings()
    {
        try{if(File.Exists(BackgroundPath))backgroundPreferences=JsonSerializer.Deserialize<BackgroundPreferences>(File.ReadAllText(BackgroundPath))??new();}catch{backgroundPreferences=new(){StartWithWindows=false};}
        var body=Stack(10);body.Children.Add(TextBlock("启动与关闭",22));
        var startup=new C.ToggleSwitch{Header="开机启动",IsOn=backgroundPreferences.StartWithWindows};startup.Toggled+=(_,_)=>{try{RegisterStartup(startup.IsOn);backgroundPreferences.StartWithWindows=startup.IsOn;SaveBackground();}catch(Exception ex){NativeMessage("开机启动设置失败",ex.Message);}};body.Children.Add(startup);
        var close=new C.ComboBox{Header="点击右上角叉号时",HorizontalAlignment=Microsoft.UI.Xaml.HorizontalAlignment.Stretch};close.Items.Add("放到后台运行（系统托盘）");close.Items.Add("关闭软件（完全退出）");close.SelectedIndex=backgroundPreferences.CloseToTray?0:1;close.SelectionChanged+=(_,_)=>{backgroundPreferences.CloseToTray=close.SelectedIndex==0;Persist();};body.Children.Add(close);
        void Persist(){try{SaveBackground();}catch(Exception ex){NativeMessage("设置保存失败",ex.Message);}}
        body.Children.Add(NativeButton("退出软件",()=>{exitRequested=true;Close();}));return Surface(body);
    }
    void InitializeBackground()
    {
        tray=new NotifyIcon{Text="Ahastation · 后台运行",Icon=(System.Drawing.Icon)(Icon??SystemIcons.Application).Clone(),Visible=true};var menu=new ContextMenuStrip();menu.Items.Add("打开 Ahastation",null,(_,_)=>RestoreFromTray());menu.Items.Add("退出",null,(_,_)=>{exitRequested=true;Close();});tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>RestoreFromTray();
        backgroundReady=true;try{if(!Environment.GetCommandLineArgs().Contains("--test-background")){RegisterStartup(backgroundPreferences.StartWithWindows);SaveBackground();}}catch(Exception ex){tray.ShowBalloonTip(5000,"开机启动未启用",ex.Message,ToolTipIcon.Warning);}
        FormClosed+=(_,_)=>{tray.Visible=false;tray.Icon?.Dispose();tray.Dispose();menu.Dispose();};
        if(Environment.GetCommandLineArgs().Contains("--background"))BeginInvoke((Action)HideToTray);
    }
    void RestoreFromTray(){ShowInTaskbar=true;Show();if(WindowState==FormWindowState.Minimized)WindowState=FormWindowState.Normal;Activate();BringToFront();if(nativeReady){PositionNativeSurfaces();nativeHost?.Invalidate(true);Invalidate(true);}}
    void HideToTray(){SaveActiveNativeNoteText();sheetGrid.EndEdit();SaveSheet();SaveCanvas();if(!chatReadOnly)deepSeekChat.Draft=chatInput.Text;SaveChat();WindowState=FormWindowState.Minimized;Hide();ShowInTaskbar=false;}
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if((e.CloseReason is CloseReason.UserClosing or CloseReason.TaskManagerClosing)&&!exitRequested&&backgroundReady&&backgroundPreferences.CloseToTray){e.Cancel=true;HideToTray();return;}
        if(nativeReady&&canvasTool==CanvasTool.Pen&&canvasPenPoints.Count>=2)FinishCanvasPen(false,false);
        base.OnFormClosing(e);
    }
}