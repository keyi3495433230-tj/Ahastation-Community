using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Markup;

namespace CardReview;

internal sealed class NativeXamlApplication : Microsoft.UI.Xaml.Application, IXamlMetadataProvider
{
    readonly IXamlMetadataProvider metadata = new Microsoft.UI.Xaml.XamlTypeInfo.XamlControlsXamlMetaDataProvider();
    readonly WindowsXamlManager manager;
    public NativeXamlApplication()
    {
        manager = WindowsXamlManager.InitializeForCurrentThread();
    }
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args) => Resources.MergedDictionaries.Add(new Microsoft.UI.Xaml.Controls.XamlControlsResources());
    public IXamlType GetXamlType(Type type) => metadata.GetXamlType(type);
    public IXamlType GetXamlType(string name) => metadata.GetXamlType(name);
    public XmlnsDefinition[] GetXmlnsDefinitions() => metadata.GetXmlnsDefinitions();
}

internal sealed class NativeMessageFilter : IMessageFilter
{
    [StructLayout(LayoutKind.Sequential)]
    struct NativeMessage { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public int time, x, y; }
    [DllImport("Microsoft.UI.Windowing.Core.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool ContentPreTranslateMessage(ref NativeMessage message);
    public bool PreFilterMessage(ref Message m)
    {
        var message = new NativeMessage { hwnd=m.HWnd, message=(uint)m.Msg, wParam=m.WParam, lParam=m.LParam };
        return ContentPreTranslateMessage(ref message);
    }
}

internal sealed class NativeIslandHost : Control
{
    DesktopWindowXamlSource? source;
    Microsoft.UI.Xaml.UIElement? pendingContent;
    IntPtr islandParentHandle;
    bool sourceInitialized;
    bool acrylicEnabled;
    public bool SetAcrylicEnabled(bool enabled)
    {
        enabled = enabled && Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported();
        if(source != null && acrylicEnabled != enabled)
        {
            source.SystemBackdrop = enabled ? new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop() : null;
            acrylicEnabled = enabled;
        }
        return acrylicEnabled;
    }
    public Microsoft.UI.Xaml.UIElement? Content
    {
        get => source?.Content ?? pendingContent;
        set
        {
            pendingContent=value;
            _ = Handle;
            if(sourceInitialized&&source!=null)
            {
                source.Content=value;
                ResizeIsland();
            }
        }
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var hostSource=new DesktopWindowXamlSource();
        try
        {
            hostSource.Initialize(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(Handle));
            source=hostSource;
            islandParentHandle=Handle;
        }
        catch(Exception hostError)
        {
            hostSource.Dispose();
            IntPtr ownerHandle=FindForm()?.Handle??IntPtr.Zero;
            if(ownerHandle==IntPtr.Zero||ownerHandle==Handle)throw;
            var ownerSource=new DesktopWindowXamlSource();
            try
            {
                ownerSource.Initialize(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(ownerHandle));
                source=ownerSource;
                islandParentHandle=ownerHandle;
            }
            catch(Exception ownerError)
            {
                ownerSource.Dispose();
                throw new AggregateException("无法将 WinUI 界面连接到窗口。",hostError,ownerError);
            }
        }
        sourceInitialized=true;
        source.TakeFocusRequested += (_, args) => Parent?.SelectNextControl(this,args.Request.Reason!=XamlSourceFocusNavigationReason.Last,true,true,true);
        if(pendingContent!=null)source.Content=pendingContent;
        ResizeIsland();
    }
    void ResizeIsland()
    {
        if(!sourceInitialized||source==null)return;
        var siteBridge=source.SiteBridge;
        if(siteBridge==null)return;
        int x=0,y=0;
        if(islandParentHandle!=Handle&&FindForm() is Form owner)
        {
            var screenOrigin=PointToScreen(Point.Empty);
            var ownerOrigin=owner.PointToScreen(Point.Empty);
            x=screenOrigin.X-ownerOrigin.X;
            y=screenOrigin.Y-ownerOrigin.Y;
        }
        siteBridge.MoveAndResize(new Windows.Graphics.RectInt32(x,y,Math.Max(1,ClientSize.Width),Math.Max(1,ClientSize.Height)));
        if(source?.Content is Microsoft.UI.Xaml.FrameworkElement root)
        {
            double scale=root.XamlRoot?.RasterizationScale??DeviceDpi/96.0;
            root.Width=Math.Max(1,ClientSize.Width/scale);root.Height=Math.Max(1,ClientSize.Height/scale);
        }
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        sourceInitialized=false;
        source?.Dispose();
        source=null;
        islandParentHandle=IntPtr.Zero;
        base.OnHandleDestroyed(e);
    }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e);source?.NavigateFocus(new XamlSourceFocusNavigationRequest((ModifierKeys&Keys.Shift)!=0?XamlSourceFocusNavigationReason.Last:XamlSourceFocusNavigationReason.First)); }
    protected override void OnDpiChangedAfterParent(EventArgs e) {base.OnDpiChangedAfterParent(e);ResizeIsland();}
    protected override void OnResize(EventArgs e) { base.OnResize(e); ResizeIsland(); }
    protected override void Dispose(bool disposing) { if(disposing) { source?.Dispose(); source=null; sourceInitialized=false; islandParentHandle=IntPtr.Zero; } base.Dispose(disposing); }
}
