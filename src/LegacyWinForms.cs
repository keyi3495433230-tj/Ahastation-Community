using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace CardReview;

internal static class LegacyStartup
{
    [STAThread]
    static void Main()
    {
        try
        {
            AppDataStorage.CompleteFactoryResetAfterExit(Environment.GetCommandLineArgs());
            Directory.CreateDirectory(AppDataStorage.Root);
            ApplicationConfiguration.Initialize();
            using var singleInstance = new Mutex(true, "Local\\AhastationCommunity_SingleInstance", out bool isFirstInstance);
            if (!isFirstInstance)
            {
                var existing=FindWindow(null,"Ahastation Community");
                if(Environment.GetCommandLineArgs().Contains("--exit")){if(existing!=IntPtr.Zero)PostMessage(existing,0x84A2,IntPtr.Zero,IntPtr.Zero);}else if(!Environment.GetCommandLineArgs().Contains("--background"))ActivateExistingWindow();
                return;
            }
            if(Environment.GetCommandLineArgs().Contains("--exit"))return;
            Application.ThreadException += (_, e) => ShowError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowError(e.ExceptionObject as Exception);
            var controller = Microsoft.UI.Dispatching.DispatcherQueueController.CreateOnCurrentThread();
            var xamlApplication = new NativeXamlApplication();
            Application.AddMessageFilter(new NativeMessageFilter());
            try { Application.Run(new ReviewForm()); controller.ShutdownQueue(); GC.KeepAlive(xamlApplication); }
            finally { singleInstance.ReleaseMutex(); }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr h,int message,IntPtr w,IntPtr l);
    static void ActivateExistingWindow()
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            var window = FindWindow(null, "Ahastation Community");
            if (window != IntPtr.Zero) { PostMessage(window,0x84A1,IntPtr.Zero,IntPtr.Zero); ShowWindow(window, 9); SetForegroundWindow(window); return; }
            Thread.Sleep(80);
        }
    }

    static void ShowError(Exception? ex)
    {
        var message = ex?.ToString() ?? "未知错误";
        try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "启动错误.log"), message); } catch { }
        MessageBox.Show(message, "Ahastation 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

public sealed partial class ReviewForm : Form
{
    private sealed class CanvasNode
    {
        public Guid Id = Guid.NewGuid();
        public string Kind = "";
        public string Content = "";
        public PointF Position;
        public Size Size;
        public bool Transparent;
        public Image? Bitmap;
        public Guid? GroupId;
        public List<PointF> VectorAnchors = new();
        public float Rotation;
        public bool Locked;
        public bool Hidden;
        public string Name = "";
        public float FontSize;
        public bool FontBold;
        public bool FontItalic;
        public bool FontStrikeout;
        public int FontAlign;
        public int FontVAlign;
        public string FontColor = "";
        public string FillColor = "";
        public float CornerRadius;
        public float Opacity = 1f;
        public float FillOpacity = 1f;
        public bool ClipMask;
        public float StrokeWidth; // 0 = 默认细边框
        public string StrokeColor = "";
        public int StrokeAlign = 1; // 0 内部, 1 中间, 2 外部
        public bool StrokeDash;
        public bool AutoLayout;
        public int AutoLayoutDirection;
        public float AutoLayoutGap = 24;
        public float AutoLayoutPadding = 16;
        public Guid? ComponentId;
        public Guid? ComponentInstanceId;
        public Guid? ComponentMasterNodeId;
        public LayerData Extra = new();
    }
    private sealed class CanvasLink
    {
        public Guid From;
        public Guid To;
        public string Label = "";
        public string Color = "";
        public int Arrow = 1; // 0 无箭头, 1 指向终点, 2 指向起点, 3 双向
    }
    private sealed record CanvasNodeState(Guid Id, string Kind, string Content, PointF Position, Size Size, bool Transparent, Guid? GroupId, List<PointF> VectorAnchors, float Rotation, bool Locked, bool Hidden, string Name, float FontSize, bool FontBold, bool FontItalic, bool FontStrikeout, int FontAlign, int FontVAlign, string FontColor, string FillColor, float Opacity, float FillOpacity, float CornerRadius, bool ClipMask, float StrokeWidth, string StrokeColor, int StrokeAlign, bool StrokeDash, bool AutoLayout, int AutoLayoutDirection, float AutoLayoutGap, float AutoLayoutPadding, Guid? ComponentId, Guid? ComponentInstanceId, Guid? ComponentMasterNodeId, string Extra = "");
    private sealed record SheetInfo(Guid Id, string Name);
    private sealed record CellVisual(bool Hidden = false, int Background = 0, bool Bold = false);
    private sealed record SheetVisual(float Zoom, Dictionary<string, CellVisual> Cells);
    private sealed record CanvasState(List<CanvasNodeState> Nodes, List<(Guid From, Guid To, string Label, string Color, int Arrow)> Links);
    private readonly record struct CanvasAnchor(PointF Point, PointF Normal);
    private readonly record struct CanvasConnectionPort(CanvasNode Node, bool IsOutput, PointF Point);
    private readonly record struct CanvasRoundedCorner(PointF Entry, PointF Exit, PointF Control1, PointF Control2, bool IsRounded);
    private readonly record struct CanvasAnchorHandle(int Index, PointF Point);
    private sealed class SmoothCanvasPanel : Panel
    {
        public SmoothCanvasPanel() { DoubleBuffered = true; ResizeRedraw = true; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true); }
    }
    private sealed class AcrylicSidebarPanel : Panel
    {
        public bool DarkMode { get; set; }
        public AcrylicSidebarPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
            var top = DarkMode ? Color.FromArgb(36, 38, 44) : Color.FromArgb(232, 237, 247);
            var bottom = DarkMode ? Color.FromArgb(43, 45, 52) : Color.FromArgb(241, 239, 248);
            using (var wash = new LinearGradientBrush(ClientRectangle, top, bottom, LinearGradientMode.Vertical))
                e.Graphics.FillRectangle(wash, ClientRectangle);
            using (var glow = new SolidBrush(Color.FromArgb(DarkMode ? 10 : 38, Color.White)))
                e.Graphics.FillEllipse(glow, -Width / 2, -Height / 3, Width * 2, Height / 2);
            using (var coolGlow = new SolidBrush(Color.FromArgb(DarkMode ? 8 : 10, 116, 159, 230)))
                e.Graphics.FillEllipse(coolGlow, -Width / 2, Height / 5, Width * 2, Height / 2);
            using (var edge = new LinearGradientBrush(new Rectangle(Math.Max(0, Width - 2), 0, 2, Height), DarkMode ? Color.FromArgb(35, 255, 255, 255) : Color.FromArgb(145, 255, 255, 255), DarkMode ? Color.FromArgb(18, 199, 204, 220) : Color.FromArgb(90, 199, 204, 220), LinearGradientMode.Horizontal))
                e.Graphics.FillRectangle(edge, Math.Max(0, Width - 2), 0, 2, Height);
        }
    }
    private sealed class SoftCardShadowPanel : Panel
    {
        public SoftCardShadowPanel() { BackColor = Color.Transparent; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            for (int spread = 12; spread >= 1; spread--)
            {
                int alpha = Math.Max(1, 8 - spread / 2);
                using var brush = new SolidBrush(Color.FromArgb(alpha, 35, 31, 27));
                using var path = new GraphicsPath();
                var bounds = new Rectangle(12 - spread, 12 - spread + 2, Width - 24 + spread * 2, Height - 24 + spread * 2);
                int diameter = Math.Min(32 + spread * 2, Math.Min(bounds.Width, bounds.Height));
                path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
                path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
                path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure(); e.Graphics.FillPath(brush, path);
            }
        }
    }
    private sealed class CardCurtainPanel : Panel
    {
        public int Alpha { get; set; }
        public CardCurtainPanel() { Enabled = false; BackColor = Color.Transparent; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Alpha <= 0) return;
            using var brush = new SolidBrush(Color.FromArgb(Math.Clamp(Alpha, 0, 255), Color.White));
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }
    }
    private sealed class StartupSplashPanel : Panel
    {
        private Image? logo;
        private float progress, fade;
        public StartupSplashPanel(Image logo)
        {
            this.logo = logo; BackColor = Color.FromArgb(43, 45, 49); Dock = DockStyle.Fill;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }
        public void SetProgress(float value, float fadeValue) { progress = Math.Clamp(value, 0f, 1f); fade = Math.Clamp(fadeValue, 0f, 1f); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (logo == null) return;
            float t = Math.Clamp(progress / 0.84f, 0f, 1f);
            float eased = 1f - MathF.Pow(1f - t, 3f);
            float scale = Math.Clamp(0.015f + eased * 0.985f, 0.015f, 1.08f);
            float size = Math.Min(ClientSize.Width, ClientSize.Height) * 0.28f * scale;
            float alpha = 1f - fade;
            var center = new PointF(ClientSize.Width / 2f, ClientSize.Height / 2f);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (size > 2)
            {
                float aspect = logo.Width / (float)logo.Height;
                float drawWidth = aspect >= 1f ? size : size * aspect;
                float drawHeight = aspect >= 1f ? size / aspect : size;
                using var attributes = new ImageAttributes();
                attributes.SetColorMatrix(new ColorMatrix { Matrix33 = alpha });
                var destination = new RectangleF(center.X - drawWidth / 2f, center.Y - drawHeight / 2f, drawWidth, drawHeight);
                e.Graphics.DrawImage(logo, Rectangle.Round(destination), 0, 0, logo.Width, logo.Height, GraphicsUnit.Pixel, attributes);
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) { logo?.Dispose(); logo = null; } base.Dispose(disposing); }
    }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==ShowAppMessage&&backgroundReady){RestoreFromTray();m.Result=IntPtr.Zero;return;}
        if(m.Msg==ExitAppMessage&&backgroundReady){exitRequested=true;Close();m.Result=IntPtr.Zero;return;}
        base.WndProc(ref m);
    }
    private readonly List<(string Word, string Type, string Meaning, string Tag, string Note, int Difficulty)> cards =
    [
        ("serendipity", "词汇 · 英语", "意外发现珍奇事物的本领", "高频词", "", 0),
        ("ephemeral", "词汇 · 英语", "短暂的；转瞬即逝的", "形容词", "", 0),
        ("沉舟侧畔千帆过", "古诗文 · 刘禹锡", "病树前头万木春", "名句", "", 0),
        ("光合作用", "生物 · 七年级", "绿色植物利用光能制造有机物", "概念", "", 0),
        ("费曼学习法", "方法 · 学习", "用自己的话向别人解释一个概念", "方法论", "", 0)
    ];
    private readonly List<(string Word, string Type, string Meaning, string Tag, string Note, int Difficulty)> savedCards = new();
    private readonly List<(string Word, string Type, string Meaning, string Tag, string Note, int Difficulty)> reviewSource = new();
    private readonly string wrongDataPath = Path.Combine(AppDataStorage.Root, "wrongbook.tsv");
    private readonly string canvasDataPath = Path.Combine(AppDataStorage.Root, "canvas.tsv");
    private readonly string canvasBoardsPath = Path.Combine(AppDataStorage.Root, "canvases");
    private readonly string canvasIndexPath = Path.Combine(AppDataStorage.Root, "canvases.tsv");
    private readonly string currentCanvasPath = Path.Combine(AppDataStorage.Root, "current-canvas.txt");
    readonly List<(Guid Id, string Name)> canvasBoards = new(); Guid activeCanvasId;
    readonly Panel card = new(); readonly SoftCardShadowPanel cardShadow = new(); readonly CardCurtainPanel cardCurtain = new(); readonly System.Windows.Forms.Timer cardTransitionTimer = new(); long cardTransitionStarted; string? lastRenderedCardKey; Font? currentCardFont; readonly Panel titleArea = new(); readonly Panel wordPage = new(); readonly Panel canvasPage = new(); readonly Panel sheetPage = new(); readonly DataGridView sheetGrid = new(); readonly VScrollBar wordSheetScrollBar = new(); bool syncingWordSheetScroll; readonly Panel wordSheetGuide = new(); readonly PictureBox wordSheetGuideImage = new(); readonly Label wordSheetGuideHint = new(); readonly Button sheetTab = new(); readonly Button sheetPicker = new(); readonly System.Windows.Forms.Timer sheetSaveTimer = new(); readonly string sheetDataPath = Path.Combine(AppDataStorage.Root, "sheet.json"); readonly string sheetsPath = Path.Combine(AppDataStorage.Root, "sheets"); readonly List<SheetInfo> sheets = new(); readonly Dictionary<string, CellVisual> sheetCells = new(); Guid activeSheetId; float sheetZoom = 1f; bool loadingSheet, sheetSelected; readonly SmoothCanvasPanel canvasSurface = new(); readonly Panel canvasLayersPanel = new(); readonly FlowLayoutPanel canvasLayersList = new(); readonly Button canvasLayersToggle = new(); readonly Panel canvasPropsPanel = new(); readonly TextBox canvasPropName = new(); readonly Label canvasPropsMultiHint = new(); readonly Button canvasPicker = new(); readonly Button wordsTab = new(); readonly Button organizeTab = new(); readonly Label word = new(); readonly Label hint = new(); readonly Label typeLabel = new(); readonly Label source = new(); readonly Label instruction = new(); readonly FlowLayoutPanel wrongList = new(); readonly FlowLayoutPanel wordBankList = new(); readonly TextBox englishInput = new(); readonly TextBox chineseInput = new(); readonly TextBox noteInput = new(); readonly CheckBox difficulty1 = new(), difficulty2 = new(), difficulty3 = new(); readonly Button wrongMeaningToggle = new(); readonly Label timerLabel = new(); readonly System.Windows.Forms.Timer clockTimer = new(); readonly System.Windows.Forms.Timer canvasSaveTimer = new(); readonly DateTime openedAt = DateTime.Now;
    Font? sheetBoldFont; Point tabDragOrigin, tabWindowOrigin; bool tabDragActive, tabWasDragged; Guid wordSheetId; bool wordSheetActive = true; readonly Button wordSheetButton = new(); readonly Button importButton = new(); readonly Label index = new(); readonly Label tag = new(); readonly Label wordSheetIndicator = new();
    readonly List<CanvasNode> canvasNodes = new(); readonly List<CanvasLink> canvasLinks = new(); readonly HashSet<Guid> canvasSelectedIds = new(); readonly Dictionary<Guid, PointF> canvasSelectionStartPositions = new(); readonly Stack<CanvasState> canvasUndo = new(); readonly Stack<CanvasState> canvasRedo = new();
    List<(CanvasNode Node, int Index)>? canvasPaintNodes; List<CanvasLink>? canvasPaintLinks; Dictionary<Guid, CanvasNode>? canvasPaintNodeMap; bool canvasPaintCacheDirty = true;
    readonly System.Windows.Forms.Timer tabTransitionTimer = new(); Panel? tabIndicator; int activeWorkspaceTab = -1, tabAnimationStartCenter, tabAnimationTargetCenter, tabAnimationElapsed;
    // 画布内部剪贴板：模板节点保留源 Id 用于连线/分组重映射，粘贴时生成新 Id
    List<CanvasNode>? canvasClipboardNodes; List<(Guid From, Guid To, string Label, string Color, int Arrow)>? canvasClipboardLinks; int canvasClipboardSequence = -1; PointF canvasClipboardPasteOffset; Guid canvasClipboardBoardId;
    int layerDragIndex = -1, canvasLayerPage; Point layerDragStart; bool layerDragging;
    const int CanvasLayerPageSize = 80;
    readonly Button canvasLayerPrevious = new(), canvasLayerNext = new(); readonly Label canvasLayerPageLabel = new();
    readonly List<(PointF Start, PointF End)> canvasSnapGuides = new();
    readonly Panel canvasAlignBar = new();
    readonly List<Button> canvasAlignButtons = new();
    readonly NumericUpDown canvasPropX = new(), canvasPropY = new(), canvasPropWidth = new(), canvasPropHeight = new(), canvasPropRotation = new(), canvasPropOpacity = new(), canvasPropFillOpacity = new(); readonly List<Control> canvasTransformPropControls = new(); readonly Dictionary<Control, Point> canvasTextPropOriginalLocations = new(); bool canvasTransformHistoryPushed;
    readonly TextBox canvasPropContent = new(); readonly Label canvasPropContentLabel = new(); readonly NumericUpDown canvasPropFontSize = new(); readonly Button canvasPropAlignLeft = new(), canvasPropAlignCenter = new(), canvasPropAlignRight = new(), canvasPropAlignTop = new(), canvasPropAlignMiddle = new(), canvasPropAlignBottom = new(); readonly CheckBox canvasPropBold = new(), canvasPropItalic = new(), canvasPropStrikeout = new(); readonly Button canvasPropColor = new(), canvasPropColorReset = new(); readonly Button canvasPropFillColor = new(), canvasPropFillColorReset = new(); readonly NumericUpDown canvasPropCornerRadius = new(); readonly NumericUpDown canvasPropStrokeWidth = new(); readonly Button canvasPropStrokeColor = new(), canvasPropStrokeColorReset = new(), canvasPropStrokeInner = new(), canvasPropStrokeCenter = new(), canvasPropStrokeOuter = new(); readonly CheckBox canvasPropStrokeDash = new(); readonly List<Control> canvasStrokePropControls = new(); readonly List<Control> canvasTextPropControls = new(); readonly List<Control> canvasSquarePropControls = new(); readonly List<Control> canvasFillColorPropControls = new(); readonly List<Control> canvasNodeNameControls = new(); readonly TextBox canvasPropLinkText = new(); readonly Button canvasPropLinkColor = new(), canvasPropLinkColorReset = new(); readonly Button canvasPropLinkArrowNone = new(), canvasPropLinkArrowEnd = new(), canvasPropLinkArrowStart = new(), canvasPropLinkArrowBoth = new(); readonly List<Control> canvasLinkPropControls = new(); bool canvasPropsLoading;
    const int FontAlignLeft = 0, FontAlignCenter = 1, FontAlignRight = 2;
    const int FontAlignTop = 0, FontAlignMiddle = 1, FontAlignBottom = 2;
    const int AlignLeft = 0, AlignCenterH = 1, AlignRight = 2, AlignTop = 3, AlignMiddleV = 4, AlignBottom = 5, AlignDistributeH = 6, AlignDistributeV = 7;
 readonly List<(string Word, string Meaning, int Count)> wrongCards = new(); readonly List<int> order = new(); readonly string dataPath = Path.Combine(AppDataStorage.Root, "wordbank.tsv"); bool randomMode, showingAnswer, hideWrongMeaning, organizeSelected, canvasSpaceDown, canvasPanning, canvasNodeDragging, canvasNodeResizing, canvasNodeRotating, canvasNodeChanged, canvasNodeHistoryPushed, canvasAltClonePending, canvasWiring, wireStartedAtOutput = true, canvasCutting, canvasCutChanged, canvasMarqueeSelecting, canvasMarqueeShift, canvasAnchorDragging, loadingCanvas; MouseButtons wireButton = MouseButtons.None; CanvasNode? canvasHoveredNode; int canvasAnchorIndex = -1; Guid? canvasVectorEditNodeId; bool canvasExporting; HashSet<Guid>? canvasExportNodeIds; int current, known = 3, repeats; Point dragStart, canvasPanLast, canvasMouseDown, canvasCutLast, wireStartPoint, canvasMarqueeStart, canvasMarqueeEnd; Rectangle canvasMarqueeBounds = Rectangle.Empty; PointF canvasOffset, canvasNodeStartPosition, wireEnd; float canvasZoom = 1f, canvasRotationStartAngle, canvasNodeStartRotation; Size canvasNodeStartSize; CanvasNode? selectedCanvasNode, wireStartNode, wireTargetNode; CanvasLink? selectedCanvasLink; bool dragging;
    int canvasAnchorPreviewInsertIndex = -1; PointF canvasAnchorPreviewPoint, canvasAnchorPreviewNormalized, canvasAnchorDragOffset; bool canvasAnchorPreviewMidpoint;
    readonly Cursor knifeCursor = CreateKnifeCursor();
    readonly Cursor rotateCursor = Cursors.SizeAll;
    readonly TextBox canvasInlineEditor = new();
    readonly HashSet<Control> themedContainers = new();
    readonly AcrylicSidebarPanel navigationRail = new();
    readonly Panel navigationIndicator = new();
    readonly Panel navigationIdentity = new();
    readonly Panel navigationAvatar = new();
    readonly Label navigationApplicationTitle = new();
    readonly Label navigationSectionLabel = new();
    readonly Label navigationIdentityTitle = new();
    readonly Label navigationIdentityHint = new();
    readonly System.Windows.Forms.Timer navigationMotionTimer = new();
    readonly System.Windows.Forms.Timer sidebarCollapseTimer = new();
    readonly Button navigationCollapseButton = new();
    readonly ToolTip navigationToolTip = new();
    readonly List<(Control Control, Point ExpandedLocation)> sidebarShiftControls = new();
    readonly List<(Control Control, Point ExpandedLocation)> centeredShiftControls = new();
    readonly Button[] workspaceNavigation = new Button[4];
    readonly Panel settingsPage = new();
    Panel? themeSettingsCard, themeSettingsPreview;
    Label? themeSettingsTitle, themeSettingsSubtitle, themeSettingsVersion;
    bool layingOutThemeSettings;
    readonly Dictionary<Label, Color> themeLabelOriginalColors = new();
    readonly Panel themeColorPreview = new();
    readonly Label themeColorName = new();
    Panel? workspaceTabBar;
    bool settingsSelected;
    Color activeThemeColor = Color.FromArgb(0, 103, 192);
    readonly string themeColorPath = Path.Combine(AppDataStorage.Root, "theme-color.txt");
    readonly string themeModePath = Path.Combine(AppDataStorage.Root, "theme-mode.txt");
    bool darkTheme;
    readonly Color pageLight = Color.FromArgb(247, 246, 242), pageDark = Color.FromArgb(31, 32, 36);
    readonly Color surfaceLight = Color.White, surfaceDark = Color.FromArgb(43, 45, 49);
    readonly Color textLight = Color.FromArgb(39, 37, 43), textDark = Color.FromArgb(242, 242, 245);
    string wordBankSearch = "";
    int navigationMotionStartTop, navigationMotionTargetTop, navigationMotionElapsed;
    int cardRestingTop, cardShadowRestingTop;
    float sidebarCollapseProgress, sidebarCollapseStart, sidebarCollapseTarget;
    int sidebarCollapseElapsed, expandedRailWidth, expandedCanvasWidth, expandedSheetWidth, sidebarCollapseDelta, centerCollapseDelta;
    Point expandedCanvasLocation, expandedSheetLocation, expandedPickerLocation, expandedSettingsLocation;
    Guid? canvasInlineEditNodeId;
    CanvasLink? canvasInlineEditLink;
    const float CanvasGridSize = 28;
    Size lastWindowClientSize;
    bool trackingWindowResize;
    bool applyingWindowResize;

    public ReviewForm()
    {
        Text = "Ahastation Community"; StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.Sizable; ControlBox = true; MaximizeBox = true; MinimizeBox = true;
        MinimumSize = new Size(1024, 640);
        ClientSize = new Size(1920, 1080); BackColor = Color.FromArgb(247,246,242);
        Icon = LoadEmbeddedAppIcon();
        Region = null;
        Font = new Font(UiFontName(), 10); KeyPreview = true;
        cardTransitionTimer.Interval = 15; cardTransitionTimer.Tick += (_, _) => AnimateCardReveal();
        LoadThemeColor();
        BuildUi(); BuildTabs(); BuildSettingsShell(); ApplyWinUiControls(this); ApplyThemeToInterface(); SelectWorkspaceTab(0); LoadSheets(); LoadCanvas(); LoadTitle(); LoadWordBank(); if (wordSheetId != Guid.Empty) RefreshCardsFromWordSheet(); LoadWrongBook(); ResetOrder(); RenderCard(); FitToScreen(); CaptureSidebarLayoutBases(); ApplySidebarCollapseProgress(0f);
        lastWindowClientSize = ClientSize; trackingWindowResize = false;
        Shown += (_,_) => InitializeNativeUi();
        sheetSaveTimer.Interval = 500; sheetSaveTimer.Tick += (_, _) => { sheetSaveTimer.Stop(); SaveSheet(); };
        canvasSaveTimer.Interval = 350; canvasSaveTimer.Tick += (_, _) => { canvasSaveTimer.Stop(); if (!loadingCanvas) SaveCanvas(); };
        tabTransitionTimer.Interval = 15; tabTransitionTimer.Tick += (_, _) => AnimateTabIndicator();
        navigationMotionTimer.Interval = 15; navigationMotionTimer.Tick += (_, _) => AnimateWorkspaceNavigation();
        sidebarCollapseTimer.Interval = 15; sidebarCollapseTimer.Tick += (_, _) => AnimateSidebarCollapse();
        FormClosing += (_, _) => { sheetGrid.EndEdit(); sheetSaveTimer.Stop(); SaveSheet(); sheetBoldFont?.Dispose(); currentCardFont?.Dispose(); cardTransitionTimer.Stop(); cardTransitionTimer.Dispose(); canvasSaveTimer.Stop(); tabTransitionTimer.Stop(); tabTransitionTimer.Dispose(); navigationMotionTimer.Stop(); navigationMotionTimer.Dispose(); sidebarCollapseTimer.Stop(); sidebarCollapseTimer.Dispose(); navigationToolTip.Dispose(); SaveCanvas(); DisposeCanvasImages(); Icon?.Dispose(); };
        clockTimer.Interval = 1000; clockTimer.Tick += (_, _) => UpdateClock(); clockTimer.Start(); UpdateClock();
        KeyDown += (_, e) => { if (organizeSelected || sheetSelected || settingsSelected) return; if (e.KeyCode == Keys.Right) Advance(true); else if (e.KeyCode == Keys.Left) Advance(false); };
        KeyDown += (_, e) =>
        {
            if (canvasInlineEditor.Visible) return;
            if (!organizeSelected || canvasSelectedIds.Count == 0) return;
            float step = (ModifierKeys & Keys.Shift) == Keys.Shift ? 10 : 1, dx = 0, dy = 0;
            switch (e.KeyCode)
            {
                case Keys.Left: case Keys.NumPad4: dx = -step; break;
                case Keys.Right: case Keys.NumPad6: dx = step; break;
                case Keys.Up: case Keys.NumPad8: dy = -step; break;
                case Keys.Down: case Keys.NumPad2: dy = step; break;
                default: return;
            }
            e.Handled = true; e.SuppressKeyPress = true;
            PushCanvasUndo();
            foreach (var node in CanvasSelectedNodes().Where(node => !node.Locked)) node.Position = new PointF(node.Position.X + dx, node.Position.Y + dy);
            canvasSurface.Invalidate(); SaveCanvas();
        };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Menu) UpdateCanvasCursor(); };
        KeyUp += (_, e) => { if (e.KeyCode == Keys.Space) { canvasSpaceDown = false; UpdateCanvasCursor(); e.SuppressKeyPress = true; } else if (e.KeyCode == Keys.Menu) UpdateCanvasCursor(); };
    }

    void ApplyWinUiControls(Control root)
    {
        if (root is Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
            button.Font = new Font(UiFontName(), Math.Max(9, button.Font.Size), FontStyle.Regular);
            string text = button.Text.Trim();
            bool grade = text is "没背过" or "背过";
            bool primary = !grade && (text is "OK" or "创建" or "保存" or "确定" or "使用EXCEL" or "导入" or "导出" || text.Contains("添加"));
            if (primary) { button.BackColor = activeThemeColor; button.ForeColor = Color.White; }
            else if (text is "没背过") { button.BackColor = Color.FromArgb(255, 242, 239); button.ForeColor = Color.FromArgb(160, 66, 48); }
            else if (text is "背过") { button.BackColor = Color.FromArgb(232, 245, 235); button.ForeColor = Color.FromArgb(31, 107, 65); }
            else if (button.Name.Contains("close", StringComparison.OrdinalIgnoreCase))
            { button.BackColor = Color.FromArgb(243, 243, 243); button.ForeColor = Color.FromArgb(31, 31, 31); }
            else
            { button.BackColor = Color.FromArgb(243, 243, 243); button.ForeColor = Color.FromArgb(31, 31, 31); }
            button.FlatAppearance.MouseOverBackColor = primary ? Blend(activeThemeColor, Color.White, 0.10f) : Blend(button.BackColor, Color.Black, 0.06f);
            button.FlatAppearance.MouseDownBackColor = primary ? Blend(activeThemeColor, Color.Black, 0.12f) : Blend(button.BackColor, Color.Black, 0.12f);
            if (button.Region is null && button.Width > 2 && button.Height > 2)
                button.Region = RoundedRegion(button, 4);
        }
        if (root is TextBoxBase textBox)
        {
            textBox.BorderStyle = BorderStyle.FixedSingle;
            textBox.BackColor = Color.FromArgb(255, 255, 255);
            textBox.ForeColor = Color.FromArgb(36, 36, 42);
            if (textBox.Width > 2 && textBox.Height > 2) textBox.Region = RoundedRegion(textBox, 4);
        }
        if (root is NumericUpDown numeric)
        {
            numeric.BackColor = Color.FromArgb(255, 255, 255);
            numeric.ForeColor = Color.FromArgb(36, 36, 42);
        }
        if (root is CheckBox checkBox)
        {
            checkBox.FlatStyle = FlatStyle.Flat;
            checkBox.ForeColor = Color.FromArgb(48, 48, 52);
        }
        if (root is Label label)
        {
            if (label.ForeColor == Color.Gray || label.ForeColor.ToArgb() == Color.FromArgb(120, 114, 108).ToArgb() || label.ForeColor.ToArgb() == Color.FromArgb(140, 134, 128).ToArgb())
                label.ForeColor = Color.FromArgb(105, 105, 113);
            else if (label.ForeColor.ToArgb() == Color.FromArgb(70, 67, 65).ToArgb() || label.ForeColor.ToArgb() == Color.FromArgb(39, 37, 43).ToArgb())
                label.ForeColor = Color.FromArgb(36, 36, 42);
        }
        if (root is DataGridView grid)
        {
            grid.BackgroundColor = Color.White;
            grid.GridColor = Color.FromArgb(229, 229, 229);
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(243, 243, 243);
            grid.RowHeadersDefaultCellStyle.BackColor = Color.FromArgb(243, 243, 243);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(204, 228, 247);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(31, 31, 31);
        }

        if ((root is Form or Panel or FlowLayoutPanel or TabPage or UserControl or SplitContainer) && themedContainers.Add(root))
            root.ControlAdded += (_, e) => { if (e.Control is { } added) ApplyWinUiControls(added); };

        if (root.BackColor.ToArgb() == Color.FromArgb(247, 246, 242).ToArgb() || root.BackColor.ToArgb() == Color.FromArgb(250, 249, 246).ToArgb())
            root.BackColor = Color.FromArgb(243, 243, 243);
        else if (root.BackColor.ToArgb() == Color.FromArgb(232, 229, 224).ToArgb())
            root.BackColor = Color.FromArgb(243, 243, 243);

        foreach (Control child in root.Controls)
            ApplyWinUiControls(child);
    }

    readonly List<(Panel Swatch, Color Color)> themeSwatches = new();
    readonly List<Panel> themeRecentSwatches = new();
    readonly List<Color> recentThemeColors = new();
    readonly string recentThemeColorsPath = Path.Combine(AppDataStorage.Root, "recent-theme-colors.txt");
    void BuildThemeSettingsPage()
    {
        settingsPage.Controls.Clear();
        themeSwatches.Clear();
        themeRecentSwatches.Clear();
        var title = Label("个性化  ›  颜色", 26, FontStyle.Regular, Color.FromArgb(39, 37, 43));
        themeSettingsTitle = title;
        title.Location = new Point(52, 44); title.AutoSize = true; settingsPage.Controls.Add(title);
        var subtitle = Label("选择主题色和应用的外观模式。", 11, FontStyle.Regular, Color.FromArgb(120, 114, 108));
        themeSettingsSubtitle = subtitle;
        subtitle.Location = new Point(54, 88); subtitle.AutoSize = true; settingsPage.Controls.Add(subtitle);
        themeSettingsVersion = Label("当前版本：1.2.0", 9, FontStyle.Regular, Color.FromArgb(120, 114, 108));
        themeSettingsVersion.Name = "themeSettingsVersion"; themeSettingsVersion.AutoSize = true; settingsPage.Controls.Add(themeSettingsVersion);

        var card = new Panel { Location = new Point(48, 142), Size = new Size(1120, 720), BackColor = Color.White };
        themeSettingsCard = card;
        card.Region = RoundedRegion(card, 8); settingsPage.Controls.Add(card);
        var section = Label("主题色", 15, FontStyle.Bold, Color.FromArgb(39, 37, 43));
        section.Location = new Point(32, 24); section.AutoSize = true; card.Controls.Add(section);
        var modePicker = new ComboBox { Location = new Point(870, 22), Size = new Size(210, 38), DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font(UiFontName(), 10) };
        modePicker.Items.AddRange(new object[] { "手动", "浅色", "深色" }); modePicker.SelectedIndex = darkTheme ? 2 : 1;
        modePicker.SelectedIndexChanged += (_, _) => { if (modePicker.SelectedIndex == 1) SetThemeMode(false); else if (modePicker.SelectedIndex == 2) SetThemeMode(true); };
        card.Controls.Add(modePicker);

        var recentTitle = Label("最近使用的颜色", 10, FontStyle.Regular, Color.FromArgb(55, 55, 55));
        recentTitle.Location = new Point(56, 78); recentTitle.AutoSize = true; card.Controls.Add(recentTitle);
        for (int i = 0; i < 4; i++)
        {
            var swatch = new Panel { Location = new Point(56 + i * 62, 112), Size = new Size(54, 54), Cursor = Cursors.Hand, Visible = false };
            swatch.Region = RoundedRegion(swatch, 5);
            swatch.Paint += (_, e) => PaintThemeColorSwatch(swatch, e);
            swatch.Click += (_, _) => { if (swatch.Tag is Color color) SetThemeColor(color); };
            card.Controls.Add(swatch); themeRecentSwatches.Add(swatch);
        }

        var windowsTitle = Label("Windows 颜色", 10, FontStyle.Regular, Color.FromArgb(55, 55, 55));
        windowsTitle.Location = new Point(56, 204); windowsTitle.AutoSize = true; card.Controls.Add(windowsTitle);
        var palette = new[]
        {
            "#FFB900", "#FF8C00", "#F7630C", "#CA5010", "#DA3B01", "#EF6950", "#D13438", "#FF4343", "#E74856",
            "#E81123", "#EA005E", "#C30052", "#E3008C", "#BF0077", "#C239B3", "#881798", "#0078D4", "#0063B1",
            "#8E8CD8", "#6B69D6", "#8764B8", "#744DA9", "#B146C2", "#881798", "#0099BC", "#2D7D9A", "#00B7C3",
            "#038387", "#00B294", "#008272", "#00CC6A", "#10893E", "#7A7574", "#5D5A58", "#68768A", "#515C6B",
            "#567C73", "#486860", "#498205", "#107C10", "#767676", "#4A4A4A", "#69797E", "#4A5459", "#647C64",
            "#525E54", "#7E735F", "#827A68"
        };
        for (int i = 0; i < palette.Length; i++)
        {
            var color = ColorTranslator.FromHtml(palette[i]);
            var swatch = new Panel { Location = new Point(56 + (i % 9) * 62, 236 + (i / 9) * 62), Size = new Size(54, 54), BackColor = color, Cursor = Cursors.Hand, Tag = palette[i] };
            swatch.Region = RoundedRegion(swatch, 5);
            swatch.Paint += (_, e) => PaintThemeColorSwatch(swatch, e);
            swatch.Click += (_, _) => SetThemeColor(color);
            navigationToolTip.SetToolTip(swatch, palette[i]); card.Controls.Add(swatch); themeSwatches.Add((swatch, color));
        }

        var customTitle = Label("自定义颜色", 10, FontStyle.Regular, Color.FromArgb(55, 55, 55));
        customTitle.Location = new Point(56, 596); customTitle.AutoSize = true; card.Controls.Add(customTitle);
        themeColorPreview.Location = new Point(56, 630); themeColorPreview.Size = new Size(48, 42); themeColorPreview.Region = RoundedRegion(themeColorPreview, 5); card.Controls.Add(themeColorPreview);
        themeColorName.Location = new Point(116, 632); themeColorName.Size = new Size(280, 38); themeColorName.Font = new Font(UiFontName(), 9); themeColorName.ForeColor = Color.FromArgb(70, 67, 65); themeColorName.TextAlign = ContentAlignment.MiddleLeft; card.Controls.Add(themeColorName);
        var choose = Button("查看颜色", Color.FromArgb(250, 250, 252), Color.FromArgb(45, 45, 45));
        choose.Location = new Point(870, 628); choose.Size = new Size(210, 42); choose.Region = RoundedRegion(choose, 5);
        choose.Click += (_, _) =>
        {
            using var dialog = new ColorDialog { FullOpen = true, Color = activeThemeColor };
            if (dialog.ShowDialog(this) == DialogResult.OK) SetThemeColor(dialog.Color);
        };
        card.Controls.Add(choose);
        var reset = Button("默认", Color.FromArgb(250, 250, 252), Color.FromArgb(45, 45, 45));
        reset.Location = new Point(410, 628); reset.Size = new Size(100, 42); reset.Region = RoundedRegion(reset, 5);
        reset.Click += (_, _) => SetThemeColor(Color.FromArgb(0, 103, 192)); card.Controls.Add(reset);

        var preview = new Panel { Location = new Point(48, 886), Size = new Size(1120, 150), BackColor = Color.White };
        themeSettingsPreview = preview;
        preview.Region = RoundedRegion(preview, 8); settingsPage.Controls.Add(preview);
        var previewTitle = Label("外观模式", 15, FontStyle.Bold, Color.FromArgb(39, 37, 43));
        previewTitle.Location = new Point(32, 22); previewTitle.AutoSize = true; preview.Controls.Add(previewTitle);
        var modeHint = Label("浅色或深色背景会应用到侧边栏、属性面板和图层面板。", 9, FontStyle.Regular, Color.FromArgb(120, 114, 108));
        modeHint.Location = new Point(32, 56); modeHint.AutoSize = true; preview.Controls.Add(modeHint);
        var lightMode = Button("浅色", Color.FromArgb(232, 232, 236), Color.FromArgb(45, 45, 45));
        lightMode.Name = "themeModeLight"; lightMode.Location = new Point(32, 94); lightMode.Size = new Size(104, 38); lightMode.Region = RoundedRegion(lightMode, 5);
        lightMode.Click += (_, _) => SetThemeMode(false); preview.Controls.Add(lightMode);
        var darkMode = Button("深色", Color.FromArgb(232, 232, 236), Color.FromArgb(45, 45, 45));
        darkMode.Name = "themeModeDark"; darkMode.Location = new Point(148, 94); darkMode.Size = new Size(104, 38); darkMode.Region = RoundedRegion(darkMode, 5);
        darkMode.Click += (_, _) => SetThemeMode(true); preview.Controls.Add(darkMode);
        var sample = Button("主要操作", activeThemeColor, Color.White);
        sample.Name = "themeSamplePrimary"; sample.Location = new Point(870, 90); sample.Size = new Size(210, 42); sample.Region = RoundedRegion(sample, 5); preview.Controls.Add(sample);
        RefreshThemeSettingsUi();
        LayoutThemeSettingsPage();
    }

    void LayoutThemeSettingsPage()
    {
        if (layingOutThemeSettings || themeSettingsCard == null || themeSettingsPreview == null) return;
        layingOutThemeSettings = true;
        try
        {
            float scale = Math.Clamp(ClientSize.Width / 1920f, 0.5f, 1.5f);
            int sideMargin = Math.Max(24, (int)MathF.Round(48 * scale));
            int idealWidth = Math.Max(1, (int)MathF.Round(1120 * scale));
            int availableWidth = Math.Max(1, settingsPage.ClientSize.Width - sideMargin * 2);
            int cardWidth = Math.Min(idealWidth, availableWidth);
            float resizeFactor = themeSettingsCard.Width > 0 ? cardWidth / (float)themeSettingsCard.Width : 1f;
            if (Math.Abs(resizeFactor - 1f) > 0.005f)
            {
                themeSettingsCard.Scale(new SizeF(resizeFactor, resizeFactor));
                themeSettingsPreview.Scale(new SizeF(resizeFactor, resizeFactor));
            }

            int left = Math.Max(0, (settingsPage.ClientSize.Width - cardWidth) / 2);
            themeSettingsTitle!.Location = new Point(left + (int)MathF.Round(4 * scale), (int)MathF.Round(44 * scale));
            themeSettingsSubtitle!.Location = new Point(left + (int)MathF.Round(6 * scale), (int)MathF.Round(88 * scale));
            var oldTitleFont = themeSettingsTitle.Font;
            var oldSubtitleFont = themeSettingsSubtitle.Font;
            themeSettingsTitle.Font = new Font(UiFontName(), 26 * scale, FontStyle.Bold);
            themeSettingsSubtitle.Font = new Font(UiFontName(), 11 * scale, FontStyle.Regular);
            oldTitleFont.Dispose(); oldSubtitleFont.Dispose();
            float fit = cardWidth / (float)idealWidth;
            themeSettingsCard.Size = new Size(cardWidth, (int)MathF.Round(720 * scale * fit));
            themeSettingsPreview.Size = new Size(cardWidth, (int)MathF.Round(150 * scale * fit));
            themeSettingsCard.Location = new Point(left, (int)MathF.Round(142 * scale));
            themeSettingsPreview.Location = new Point(left, (int)MathF.Round(886 * scale));
            themeSettingsVersion!.Location = new Point(left + (int)MathF.Round(4 * scale), themeSettingsPreview.Bottom + (int)MathF.Round(18 * scale));
            themeSettingsCard.Region = RoundedRegion(themeSettingsCard, Math.Max(4, (int)MathF.Round(8 * scale)));
            themeSettingsPreview.Region = RoundedRegion(themeSettingsPreview, Math.Max(4, (int)MathF.Round(8 * scale)));
            foreach (var (swatch, _) in themeSwatches)
            {
                var previousRegion = swatch.Region;
                swatch.Region = RoundedRegion(swatch, Math.Max(4, (int)MathF.Round(12 * scale)));
                previousRegion?.Dispose();
            }
            foreach (var swatch in themeRecentSwatches)
            {
                var previousRegion = swatch.Region;
                swatch.Region = RoundedRegion(swatch, Math.Max(3, (int)MathF.Round(5 * scale)));
                previousRegion?.Dispose();
            }
            foreach (var button in themeSettingsCard.Controls.OfType<Button>().Concat(themeSettingsPreview.Controls.OfType<Button>()))
            {
                int radius = button.Name == "themeSamplePrimary" ? 10 : 9;
                var previousRegion = button.Region;
                button.Region = RoundedRegion(button, Math.Max(4, (int)MathF.Round(radius * scale)));
                previousRegion?.Dispose();
            }
            var oldPreviewRegion = themeColorPreview.Region;
            themeColorPreview.Region = RoundedRegion(themeColorPreview, Math.Max(4, (int)MathF.Round(12 * scale)));
            oldPreviewRegion?.Dispose();
            settingsPage.AutoScrollMinSize = new Size(0, themeSettingsVersion.Bottom + sideMargin);
        }
        finally { layingOutThemeSettings = false; }
    }

    void LoadThemeColor()
    {
        try
        {
            if (File.Exists(themeColorPath)) activeThemeColor = ColorTranslator.FromHtml(File.ReadAllText(themeColorPath, Encoding.UTF8).Trim());
            darkTheme = File.Exists(themeModePath) && File.ReadAllText(themeModePath, Encoding.UTF8).Trim().Equals("dark", StringComparison.OrdinalIgnoreCase);
            if (File.Exists(recentThemeColorsPath))
                foreach (var line in File.ReadAllLines(recentThemeColorsPath, Encoding.UTF8).Take(4))
                    if (ColorTranslator.FromHtml(line.Trim()) is Color recent && recent.A != 0 && recentThemeColors.All(existing => existing.ToArgb() != recent.ToArgb())) recentThemeColors.Add(recent);
            if (recentThemeColors.All(existing => existing.ToArgb() != activeThemeColor.ToArgb())) recentThemeColors.Insert(0, activeThemeColor);
            if (recentThemeColors.Count > 4) recentThemeColors.RemoveRange(4, recentThemeColors.Count - 4);
        }
        catch { activeThemeColor = Color.FromArgb(0, 103, 192); darkTheme = false; }
    }

    void RefreshThemeSettingsUi()
    {
        if (themeColorPreview.IsDisposed) return;
        themeColorPreview.BackColor = activeThemeColor;
        themeColorName.Text = $"当前主题色  {ColorTranslator.ToHtml(activeThemeColor)}";
        themeColorPreview.Invalidate();
        foreach (var (swatch, _) in themeSwatches) swatch.Invalidate();
        UpdateThemeRecentSwatches();
        var sample = settingsPage.Controls.OfType<Panel>().SelectMany(panel => panel.Controls.OfType<Button>()).FirstOrDefault(button => button.Name == "themeSamplePrimary");
        if (sample != null)
        {
            sample.BackColor = activeThemeColor;
            sample.FlatAppearance.MouseOverBackColor = Blend(activeThemeColor, Color.White, 0.10f);
            sample.FlatAppearance.MouseDownBackColor = Blend(activeThemeColor, Color.Black, 0.12f);
        }
        var lightMode = settingsPage.Controls.Find("themeModeLight", true).OfType<Button>().FirstOrDefault();
        var darkMode = settingsPage.Controls.Find("themeModeDark", true).OfType<Button>().FirstOrDefault();
        if (lightMode != null) { lightMode.BackColor = !darkTheme ? activeThemeColor : Color.FromArgb(232, 232, 236); lightMode.ForeColor = !darkTheme ? Color.White : textLight; }
        if (darkMode != null) { darkMode.BackColor = darkTheme ? activeThemeColor : Color.FromArgb(232, 232, 236); darkMode.ForeColor = darkTheme ? Color.White : textLight; }
    }

    void SetThemeColor(Color color)
    {
        activeThemeColor = Color.FromArgb(255, color.R, color.G, color.B);
        recentThemeColors.RemoveAll(existing => existing.ToArgb() == activeThemeColor.ToArgb());
        recentThemeColors.Insert(0, activeThemeColor);
        if (recentThemeColors.Count > 4) recentThemeColors.RemoveRange(4, recentThemeColors.Count - 4);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(themeColorPath)!);
            File.WriteAllText(themeColorPath, ColorTranslator.ToHtml(activeThemeColor), Encoding.UTF8);
            File.WriteAllLines(recentThemeColorsPath, recentThemeColors.Select(ColorTranslator.ToHtml), Encoding.UTF8);
        }
        catch { }
        ApplyThemeToInterface();
        RefreshThemeSettingsUi();
        if (settingsSelected) UpdateNavigationSelection(3);
        else if (activeWorkspaceTab >= 0) UpdateNavigationSelection(activeWorkspaceTab);
    }

    void UpdateThemeRecentSwatches()
    {
        if (themeRecentSwatches.Count == 0) return;
        var colors = recentThemeColors.Count > 0 ? recentThemeColors : new List<Color> { activeThemeColor };
        for (int i = 0; i < themeRecentSwatches.Count; i++)
        {
            var swatch = themeRecentSwatches[i];
            swatch.Visible = i < colors.Count;
            if (i < colors.Count) { swatch.Tag = colors[i]; swatch.BackColor = colors[i]; swatch.Invalidate(); }
        }
    }

    void PaintThemeColorSwatch(Panel swatch, PaintEventArgs e)
    {
        Color color = swatch.Tag is Color tagged ? tagged : swatch.BackColor;
        if (color.ToArgb() != activeThemeColor.ToArgb()) return;
        using var outer = new Pen(Color.White, 4f);
        using var inner = new Pen(Color.FromArgb(35, 35, 35), 2f);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.DrawRectangle(outer, 2, 2, swatch.Width - 5, swatch.Height - 5);
        e.Graphics.DrawRectangle(inner, 4, 4, swatch.Width - 9, swatch.Height - 9);
        float x = swatch.Width - 19, y = 5;
        using var marker = new SolidBrush(Color.FromArgb(30, 30, 30));
        e.Graphics.FillRectangle(marker, x, y, 14, 14);
        using var check = new Pen(Color.White, 1.8f);
        e.Graphics.DrawLines(check, new[] { new PointF(x + 3, y + 7), new PointF(x + 6, y + 10), new PointF(x + 11, y + 4) });
    }

    void SetThemeMode(bool dark)
    {
        darkTheme = dark;
        try { Directory.CreateDirectory(Path.GetDirectoryName(themeModePath)!); File.WriteAllText(themeModePath, dark ? "dark" : "light", Encoding.UTF8); } catch { }
        ApplyThemeToInterface(); RefreshThemeSettingsUi();
    }

    void ApplyThemeToInterface()
    {
        if (nativeReady) { ApplyNativeTheme(); return; }
        var page = darkTheme ? pageDark : pageLight;
        var text = darkTheme ? textDark : textLight;
        var muted = darkTheme ? Color.FromArgb(180, 183, 191) : Color.FromArgb(105, 105, 113);
        BackColor = page; wordPage.BackColor = page; canvasPage.BackColor = page; sheetPage.BackColor = page; settingsPage.BackColor = page;
        card.BackColor = Color.White; wordBankList.BackColor = Color.White; wrongList.BackColor = Color.White;
        sheetGrid.BackgroundColor = Color.White; sheetGrid.DefaultCellStyle.BackColor = Color.White; sheetGrid.DefaultCellStyle.ForeColor = textLight;
        sheetGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(243, 243, 243);
        sheetGrid.ColumnHeadersDefaultCellStyle.ForeColor = textLight; sheetGrid.RowHeadersDefaultCellStyle.BackColor = Color.FromArgb(243, 243, 243);
        sheetGrid.RowHeadersDefaultCellStyle.ForeColor = textLight; sheetGrid.GridColor = Color.FromArgb(229, 229, 229);
        navigationRail.DarkMode = darkTheme;
        navigationRail.BackColor = darkTheme ? Color.FromArgb(37, 39, 46) : Color.FromArgb(239, 239, 247);
        navigationRail.Invalidate();
        navigationIdentity.BackColor = darkTheme ? Color.FromArgb(48, 50, 56) : Color.FromArgb(248, 248, 252);
        navigationIdentityTitle.ForeColor = darkTheme ? textDark : textLight; navigationIdentityHint.ForeColor = muted; navigationApplicationTitle.ForeColor = darkTheme ? textDark : textLight;
        navigationCollapseButton.BackColor = darkTheme ? Color.FromArgb(52, 54, 60) : Color.FromArgb(243, 243, 243); navigationCollapseButton.ForeColor = text;
        canvasSurface.BackColor = darkTheme ? Color.FromArgb(24, 25, 29) : Color.FromArgb(250, 249, 246);
        canvasLayersPanel.BackColor = darkTheme ? Color.FromArgb(48, 50, 56) : Color.FromArgb(245, 245, 247);
        canvasLayersList.BackColor = canvasLayersPanel.BackColor;
        canvasPropsPanel.BackColor = darkTheme ? Color.FromArgb(36, 38, 44) : Color.FromArgb(230, 235, 246);
        wordSheetGuide.BackColor = page;
        wordSheetGuideHint.ForeColor = darkTheme ? Color.FromArgb(180, 183, 191) : Color.FromArgb(105, 105, 113);
        foreach (var panel in EnumerateControls(this).OfType<Panel>())
        {
            if (panel == card || panel.Parent == settingsPage) panel.BackColor = Color.White;
            panel.Invalidate();
        }
        foreach (var button in EnumerateControls(this).OfType<Button>())
        {
            string label = button.Text.Trim();
            bool primary = label is "OK" or "创建" or "保存" or "确定" or "使用EXCEL" or "导入" or "导出" or "主要操作" || label.Contains("添加");
            bool activeToggle = button == wordSheetButton && wordSheetId != Guid.Empty && activeSheetId == wordSheetId;
            if (primary || activeToggle) { button.BackColor = activeThemeColor; button.ForeColor = Color.White; button.FlatAppearance.MouseOverBackColor = Blend(activeThemeColor, Color.White, .10f); button.FlatAppearance.MouseDownBackColor = Blend(activeThemeColor, Color.Black, .12f); }
            else if (button == navigationCollapseButton) { }
            else if (button == canvasLayersToggle || button == sheetPicker || button.Parent == canvasAlignBar) { button.BackColor = darkTheme ? Color.FromArgb(43, 45, 49) : Color.FromArgb(39, 37, 43); button.ForeColor = Color.White; }
            else if (button.Parent == navigationRail) { button.BackColor = navigationRail.BackColor; button.ForeColor = darkTheme ? textDark : textLight; }
            else if (button.Name.Contains("themeMode", StringComparison.Ordinal)) { }
            else if (IsWithinControl(button, canvasLayersPanel) || IsWithinControl(button, canvasPropsPanel)) { }
        }
        foreach (var label in EnumerateControls(this).OfType<Label>())
        {
            if (label.Parent == canvasSurface || label.Parent == navigationAvatar) continue;
            if (!themeLabelOriginalColors.ContainsKey(label)) themeLabelOriginalColors[label] = label.ForeColor;
            bool darkSurface = IsWithinControl(label, navigationRail) || IsWithinControl(label, canvasPropsPanel) || IsWithinControl(label, canvasLayersPanel) || label.Parent == wordPage || label.Parent == canvasPage || label.Parent == settingsPage || label.Parent == wordSheetGuide;
            label.ForeColor = darkTheme && darkSurface ? textDark : themeLabelOriginalColors[label];
        }
        foreach (var entry in EnumerateControls(this).OfType<TextBoxBase>()) { entry.BackColor = Color.White; entry.ForeColor = textLight; }
        foreach (var item in EnumerateControls(this).OfType<CheckBox>()) item.ForeColor = IsWithinControl(item, canvasPropsPanel) && darkTheme ? textDark : textLight;
        foreach (var item in EnumerateControls(this).OfType<NumericUpDown>()) { item.BackColor = Color.White; item.ForeColor = textLight; }
        foreach (var (swatch, _) in themeSwatches) swatch.Invalidate();
        ConfigureWindows11TitleBar();
        UpdateWordSheetUi();
        UpdateNavigationSelection(settingsSelected ? 3 : activeWorkspaceTab < 0 ? 0 : activeWorkspaceTab);
        if (canvasSurface.IsHandleCreated) canvasSurface.Invalidate();
        if (canvasLayersPanel.Visible) RefreshCanvasLayers();
        RefreshWordBank(); RefreshWrongBook();
    }

    static bool IsWithinControl(Control control, Control ancestor)
    {
        for (Control? current = control; current != null; current = current.Parent)
            if (current == ancestor) return true;
        return false;
    }

    static IEnumerable<Control> EnumerateControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in EnumerateControls(child)) yield return descendant;
        }
    }

    void SelectSettingsPage()
    {
        if (nativeReady) { NativeSelectPage(4); return; }
        settingsSelected = true; organizeSelected = false; sheetSelected = false;
        wordPage.Visible = canvasPage.Visible = sheetPage.Visible = false;
        settingsPage.Visible = true; canvasPicker.Visible = false; canvasSpaceDown = false; canvasPanning = false;
        if (workspaceTabBar != null) workspaceTabBar.Visible = false;
        UpdateCanvasCursor(); canvasAlignBar.Visible = false;
        UpdateNavigationSelection(3);
    }

    void UpdateNavigationSelection(int selected)
    {
        if (nativeReady) return;
        for (int index = 0; index < workspaceNavigation.Length; index++)
        {
            var item = workspaceNavigation[index];
            if (item == null) continue;
            item.BackColor = selected == index ? Blend(navigationRail.BackColor, activeThemeColor, 0.12f) : navigationRail.BackColor;
            item.ForeColor = selected == index ? activeThemeColor : darkTheme ? Color.FromArgb(224, 226, 233) : Color.FromArgb(31, 31, 31);
            item.Font = new Font(UiFontName(), 10, selected == index ? FontStyle.Bold : FontStyle.Regular);
        }
        navigationIndicator.BackColor = activeThemeColor;
        int targetTop = selected == 3 ? navigationRail.Height - 71 : 137 + selected * 60;
        if (activeWorkspaceTab < 0 || activeWorkspaceTab == selected)
        {
            navigationMotionTimer.Stop(); navigationIndicator.Top = targetTop;
        }
        else
        {
            navigationMotionStartTop = navigationIndicator.Top; navigationMotionTargetTop = targetTop; navigationMotionElapsed = 0;
            navigationMotionTimer.Stop(); navigationMotionTimer.Start();
        }
        activeWorkspaceTab = selected;
    }

    void BuildSettingsShell()
    {
        Text = "Ahastation Community";
        BackColor = Color.FromArgb(243, 243, 243);
        wordPage.BackColor = BackColor;

        var tabBar = Controls.Cast<Control>().FirstOrDefault(control => control.Controls.Contains(wordsTab));
        if (tabBar != null) tabBar.Visible = false;
        importButton.Location = new Point(1310, 78);
        var modeButton = wordPage.Controls.OfType<Button>().FirstOrDefault(button => button.Name == "mode");
        if (modeButton != null) modeButton.Location = new Point(1492, 78);
        var resetButton = wordPage.Controls.OfType<Button>().FirstOrDefault(button => button.Text == "重置全部");
        if (resetButton != null) resetButton.Location = new Point(1614, 78);
        var helpButton = wordPage.Controls.OfType<Button>().FirstOrDefault(button => button.Text == "使用说明");
        if (helpButton != null) { helpButton.Location = new Point(1736, 78); helpButton.Size = new Size(150, 42); }

        foreach (var title in wordPage.Controls.OfType<Label>())
        {
            if (title.Text == "词库") title.Location = new Point(460, 145);
            else if (title.Text == "本地保存 · 可编辑") title.Location = new Point(460, 192);
            else if (title.Text == "难度") title.Location = new Point(460, 994);
        }
        wordBankList.Location = new Point(460, 225); wordBankList.Size = new Size(292, 625);
        englishInput.Location = new Point(460, 870); englishInput.Size = new Size(176, 34);
        chineseInput.Location = new Point(460, 912); chineseInput.Size = new Size(176, 34);
        noteInput.Location = new Point(460, 954); noteInput.Size = new Size(176, 34);
        difficulty1.Location = new Point(520, 991); difficulty2.Location = new Point(554, 991); difficulty3.Location = new Point(588, 991);
        var add = wordPage.Controls.OfType<Button>().FirstOrDefault(button => button.Text == "OK");
        if (add != null) { add.Location = new Point(648, 870); add.Size = new Size(104, 118); }
        var filter = wordPage.Controls.OfType<Button>().FirstOrDefault(button => button.Text == "筛选单词");
        if (filter != null) { filter.Location = new Point(460, 1026); filter.Size = new Size(292, 38); }

        canvasPage.Location = new Point(440, 82); canvasPage.Size = new Size(1480, 998);
        sheetPage.Location = new Point(440, 82); sheetPage.Size = new Size(1480, 998);
        canvasPage.BackColor = BackColor; sheetPage.BackColor = BackColor;
        settingsPage.Name = "settingsPage"; settingsPage.Location = new Point(440, 82); settingsPage.Size = new Size(1480, 998); settingsPage.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right; settingsPage.AutoScroll = true; settingsPage.BackColor = BackColor; settingsPage.Visible = false;
        settingsPage.Resize += (_, _) => LayoutThemeSettingsPage();
        BuildThemeSettingsPage(); Controls.Add(settingsPage);
        canvasLayersPanel.Size = new Size(canvasLayersPanel.Width, canvasPage.Height);
        canvasLayersPanel.Location = new Point(canvasPage.ClientSize.Width - canvasLayersPanel.Width, 0);
        canvasLayersList.Height = canvasLayersPanel.Height - 170;
        canvasAlignBar.Left = (canvasPage.ClientSize.Width - canvasAlignBar.Width) / 2;
        canvasPicker.Location = new Point(460, ClientSize.Height - 48);

        navigationRail.Bounds = new Rectangle(0, 0, 440, 1080);
        navigationRail.BackColor = Color.FromArgb(239, 239, 247);
        navigationRail.Controls.Clear();
        navigationApplicationTitle.Text = "Ahastation Community"; navigationApplicationTitle.Font = new Font(UiFontName(), 16, FontStyle.Bold); navigationApplicationTitle.ForeColor = Color.FromArgb(31, 31, 35);
        navigationApplicationTitle.Location = new Point(28, 31); navigationApplicationTitle.Size = new Size(300, 34);
        navigationCollapseButton.Text = "‹"; navigationCollapseButton.Location = new Point(372, 20); navigationCollapseButton.Size = new Size(44, 40);
        navigationCollapseButton.FlatStyle = FlatStyle.Flat; navigationCollapseButton.FlatAppearance.BorderSize = 0; navigationCollapseButton.BackColor = Color.FromArgb(243, 243, 243); navigationCollapseButton.ForeColor = Color.FromArgb(31, 31, 31);
        navigationCollapseButton.Font = new Font(UiFontName(), 16, FontStyle.Regular); navigationCollapseButton.Cursor = Cursors.Hand; navigationCollapseButton.TabStop = false; navigationCollapseButton.Region = RoundedRegion(navigationCollapseButton, 8);
        navigationCollapseButton.Click += (_, _) => ToggleSidebarCollapse(); navigationToolTip.SetToolTip(navigationCollapseButton, "收起侧边栏"); navigationRail.Controls.Add(navigationCollapseButton);
        navigationIdentity.Location = new Point(24, 91); navigationIdentity.Size = new Size(392, 76); navigationIdentity.BackColor = Color.FromArgb(248, 248, 252);
        navigationIdentity.Region = RoundedRegion(navigationIdentity, 12);
        navigationAvatar.Location = new Point(12, 12); navigationAvatar.Size = new Size(48, 48); navigationAvatar.BackColor = Color.FromArgb(224, 239, 251);
        using (var path = new GraphicsPath()) { path.AddEllipse(0, 0, navigationAvatar.Width, navigationAvatar.Height); navigationAvatar.Region = new Region(path); }
        var initials = Label("AH", 13, FontStyle.Bold, activeThemeColor);
        initials.Location = new Point(0, 0); initials.Size = navigationAvatar.Size; initials.TextAlign = ContentAlignment.MiddleCenter; navigationAvatar.Controls.Add(initials);
        navigationIdentityTitle.Text = "本地学习库"; navigationIdentityTitle.Font = new Font(UiFontName(), 10, FontStyle.Bold); navigationIdentityTitle.ForeColor = Color.FromArgb(31, 31, 35);
        navigationIdentityTitle.Location = new Point(72, 16); navigationIdentityTitle.Size = new Size(145, 22);
        navigationIdentityHint.Text = "词库已保存在此设备"; navigationIdentityHint.Font = new Font(UiFontName(), 8); navigationIdentityHint.ForeColor = Color.FromArgb(105, 105, 112);
        navigationIdentityHint.Location = new Point(72, 39); navigationIdentityHint.Size = new Size(145, 20);
        navigationIdentity.Controls.Add(navigationAvatar); navigationIdentity.Controls.Add(navigationIdentityTitle); navigationIdentity.Controls.Add(navigationIdentityHint);
        navigationSectionLabel.Text = "工作区"; navigationSectionLabel.Font = new Font(UiFontName(), 9, FontStyle.Bold); navigationSectionLabel.ForeColor = Color.FromArgb(105, 105, 112);
        navigationSectionLabel.Location = new Point(28, 96); navigationSectionLabel.Size = new Size(220, 22); navigationRail.Controls.Add(navigationSectionLabel);
        navigationIndicator.Location = new Point(16, 137); navigationIndicator.Size = new Size(4, 30);
        navigationIndicator.BackColor = activeThemeColor; navigationIndicator.Region = RoundedRegion(navigationIndicator, 2);
        navigationRail.Controls.Add(navigationIndicator);
        string[] names = { "单词复习", "梳理画布", "表格词库", "设置" };
        for (int index = 0; index < names.Length; index++)
        {
            int selected = index;
            var item = new Button { Text = names[index], FlatStyle = FlatStyle.Flat, BackColor = navigationRail.BackColor, ForeColor = Color.FromArgb(31, 31, 31), Font = new Font(UiFontName(), 10), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(18, 0, 8, 0), Location = new Point(16, index == 3 ? navigationRail.Height - 80 : 128 + index * 60), Size = new Size(408, 48), Name = $"navigation{index}", TabStop = false, Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
            item.FlatAppearance.BorderSize = 0; item.Region = RoundedRegion(item, 10);
            item.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 232, 237);
            item.FlatAppearance.MouseDownBackColor = Color.FromArgb(220, 220, 226);
            item.MouseEnter += (_, _) => item.BackColor = Color.FromArgb(232, 232, 237);
            item.MouseLeave += (_, _) => item.BackColor = activeWorkspaceTab == selected ? Blend(navigationRail.BackColor, activeThemeColor, 0.12f) : navigationRail.BackColor;
            item.Click += (_, _) => { if (selected == 3) SelectSettingsPage(); else SelectWorkspaceTab(selected); };
            navigationToolTip.SetToolTip(item, names[index]);
            workspaceNavigation[index] = item; navigationRail.Controls.Add(item);
        }
        navigationIndicator.BringToFront();
        Controls.Add(navigationRail); navigationRail.BringToFront();
        navigationRail.BringToFront();
        navigationCollapseButton.BringToFront();

        foreach (var label in wordPage.Controls.OfType<Label>().Where(label => label.Text is "词库" or "本地保存 · 可编辑" or "难度"))
            sidebarShiftControls.Add((label, label.Location));
        sidebarShiftControls.AddRange(new Control[] { wordBankList, englishInput, chineseInput, noteInput, difficulty1, difficulty2, difficulty3, add!, filter! }.Select(control => (control, control.Location)));
        centeredShiftControls.AddRange(new Control[] { cardShadow, card, titleArea, wordSheetIndicator, instruction }
            .Concat(wordPage.Controls.OfType<Button>().Where(button => button.Text is "没背过" or "背过"))
            .Select(control => (control, control.Location)));

        SelectWorkspaceTab(0);
    }

    void WireWindowDrag(Control surface, Func<Point, bool>? canDrag = null)
    {
        Point startCursor = Point.Empty, startWindow = Point.Empty;
        bool dragging = false;
        surface.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || (canDrag != null && !canDrag(e.Location))) return;
            startCursor = Cursor.Position;
            startWindow = Location;
            dragging = false;
            surface.Capture = true;
        };
        surface.MouseMove += (_, e) =>
        {
            if (!surface.Capture || (Control.MouseButtons & MouseButtons.Left) == 0) return;
            Point cursor = Cursor.Position;
            int dx = cursor.X - startCursor.X, dy = cursor.Y - startCursor.Y;
            if (!dragging && (Math.Abs(dx) >= SystemInformation.DragSize.Width / 2 || Math.Abs(dy) >= SystemInformation.DragSize.Height / 2))
            {
                dragging = true;
                if (WindowState == FormWindowState.Maximized)
                {
                    float horizontalAnchor = Width <= 0 ? 0.5f : Math.Clamp((cursor.X - Bounds.Left) / (float)Width, 0f, 1f);
                    int titleOffset = Math.Max(0, e.Y);
                    WindowState = FormWindowState.Normal;
                    Location = new Point(cursor.X - (int)MathF.Round(Width * horizontalAnchor), cursor.Y - titleOffset);
                    startCursor = cursor;
                    startWindow = Location;
                    dx = dy = 0;
                }
            }
            if (dragging) Location = new Point(startWindow.X + dx, startWindow.Y + dy);
        };
        surface.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) { surface.Capture = false; dragging = false; } };
    }

    void CaptureSidebarLayoutBases()
    {
        for (int index = 0; index < sidebarShiftControls.Count; index++)
            sidebarShiftControls[index] = (sidebarShiftControls[index].Control, sidebarShiftControls[index].Control.Location);
        for (int index = 0; index < centeredShiftControls.Count; index++)
            centeredShiftControls[index] = (centeredShiftControls[index].Control, centeredShiftControls[index].Control.Location);
        float scale = ClientSize.Width / 1920f;
        expandedRailWidth = navigationRail.Width;
        sidebarCollapseDelta = (int)MathF.Round(368 * scale);
        centerCollapseDelta = (int)MathF.Round(184 * scale);
        expandedCanvasLocation = canvasPage.Location; expandedCanvasWidth = canvasPage.Width;
        expandedSheetLocation = sheetPage.Location; expandedSheetWidth = sheetPage.Width;
        expandedSettingsLocation = settingsPage.Location;
        expandedPickerLocation = canvasPicker.Location;
    }

    void ToggleSidebarCollapse()
    {
        sidebarCollapseStart = sidebarCollapseProgress;
        sidebarCollapseTarget = sidebarCollapseProgress < 0.5f ? 1f : 0f;
        sidebarCollapseElapsed = 0;
        sidebarCollapseTimer.Stop(); sidebarCollapseTimer.Start();
    }

    void AnimateSidebarCollapse()
    {
        const int duration = 240;
        sidebarCollapseElapsed = Math.Min(duration, sidebarCollapseElapsed + sidebarCollapseTimer.Interval);
        float progress = sidebarCollapseElapsed / (float)duration;
        float eased = 1f - MathF.Pow(1f - progress, 3f);
        ApplySidebarCollapseProgress(sidebarCollapseStart + (sidebarCollapseTarget - sidebarCollapseStart) * eased);
        if (sidebarCollapseElapsed >= duration)
        {
            ApplySidebarCollapseProgress(sidebarCollapseTarget);
            sidebarCollapseTimer.Stop();
        }
    }

    void ApplySidebarCollapseProgress(float progress)
    {
        sidebarCollapseProgress = Math.Clamp(progress, 0f, 1f);
        float p = sidebarCollapseProgress;
        int dx = (int)MathF.Round(sidebarCollapseDelta * p);
        int centerDx = (int)MathF.Round(centerCollapseDelta * p);
        float scale = ClientSize.Width / 1920f;
        navigationRail.Width = Math.Max((int)MathF.Round(72 * scale), expandedRailWidth - dx);
        foreach (var (control, location) in sidebarShiftControls)
            control.Location = new Point(location.X - dx, location.Y);
        foreach (var (control, location) in centeredShiftControls)
            control.Location = new Point(location.X - centerDx, location.Y);

        canvasPage.Location = new Point(expandedCanvasLocation.X - dx, expandedCanvasLocation.Y);
        canvasPage.Width = expandedCanvasWidth + dx;
        sheetPage.Location = new Point(expandedSheetLocation.X - dx, expandedSheetLocation.Y);
        sheetPage.Width = expandedSheetWidth + dx;
        settingsPage.Location = new Point(navigationRail.Width, expandedSettingsLocation.Y);
        settingsPage.Width = Math.Max(1, ClientSize.Width - settingsPage.Left);
        canvasPicker.Location = new Point(expandedPickerLocation.X - dx, expandedPickerLocation.Y);
        canvasLayersPanel.Location = new Point(canvasPage.ClientSize.Width - canvasLayersPanel.Width, 0);
        canvasAlignBar.Left = (canvasPage.ClientSize.Width - canvasAlignBar.Width) / 2;

        bool collapsed = p >= 0.78f;
        navigationApplicationTitle.Text = collapsed ? "AH" : "Ahastation Community";
        navigationApplicationTitle.Location = new Point((int)MathF.Round(80 * scale), (int)MathF.Round(31 * scale));
        navigationApplicationTitle.Width = (int)MathF.Round(300 * scale);
        navigationApplicationTitle.Visible = false;
        navigationCollapseButton.Text = collapsed ? "›" : "‹";
        navigationCollapseButton.Location = new Point((int)MathF.Round(16 * scale), (int)MathF.Round(20 * scale));
        navigationToolTip.SetToolTip(navigationCollapseButton, collapsed ? "展开侧边栏" : "收起侧边栏");
        navigationIdentity.Visible = false;
        navigationIdentityTitle.Visible = navigationIdentityHint.Visible = false;
        navigationSectionLabel.Visible = !collapsed;

        string[] fullNames = { "单词复习", "梳理画布", "表格词库", "设置" };
        string[] shortNames = { "词", "梳", "表", "设" };
        for (int index = 0; index < workspaceNavigation.Length; index++)
        {
            var item = workspaceNavigation[index];
            if (item == null) continue;
            item.Text = collapsed ? shortNames[index] : fullNames[index];
            item.TextAlign = collapsed ? ContentAlignment.MiddleCenter : ContentAlignment.MiddleLeft;
            item.Padding = collapsed ? Padding.Empty : new Padding((int)MathF.Round(18 * scale), 0, (int)MathF.Round(8 * scale), 0);
            item.Width = navigationRail.Width - (int)MathF.Round(32 * scale);
            var previousRegion = item.Region;
            item.Region = RoundedRegion(item, (int)MathF.Round(10 * scale));
            previousRegion?.Dispose();
        }
        navigationIndicator.Region = RoundedRegion(navigationIndicator, Math.Max(1, (int)MathF.Round(2 * scale)));
        navigationRail.PerformLayout(); navigationRail.Invalidate();
    }

    void FitToScreen()
    {
        var area = (Screen.PrimaryScreen ?? Screen.FromPoint(Cursor.Position)).WorkingArea;
        MinimumSize = new Size(480, 360);
        ClientSize = new Size(Math.Min(area.Width - 40, (int)(1200 * DeviceDpi / 96f)), Math.Min(area.Height - 90, (int)(800 * DeviceDpi / 96f)));
        Region = null;
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
    }

    void ScaleWindowLayout()
    {
        if (!trackingWindowResize || applyingWindowResize || lastWindowClientSize.Width <= 0 || lastWindowClientSize.Height <= 0 || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        float scaleX = ClientSize.Width / (float)lastWindowClientSize.Width;
        float scaleY = ClientSize.Height / (float)lastWindowClientSize.Height;
        if (Math.Abs(scaleX - 1f) < 0.002f && Math.Abs(scaleY - 1f) < 0.002f) return;
        applyingWindowResize = true;
        try
        {
            Scale(new SizeF(scaleX, scaleY));
            for (int i = 0; i < sidebarShiftControls.Count; i++)
            {
                var (control, point) = sidebarShiftControls[i];
                sidebarShiftControls[i] = (control, new Point((int)MathF.Round(point.X * scaleX), (int)MathF.Round(point.Y * scaleY)));
            }
            for (int i = 0; i < centeredShiftControls.Count; i++)
            {
                var (control, point) = centeredShiftControls[i];
                centeredShiftControls[i] = (control, new Point((int)MathF.Round(point.X * scaleX), (int)MathF.Round(point.Y * scaleY)));
            }
            expandedRailWidth = (int)MathF.Round(expandedRailWidth * scaleX);
            sidebarCollapseDelta = (int)MathF.Round(sidebarCollapseDelta * scaleX);
            centerCollapseDelta = (int)MathF.Round(centerCollapseDelta * scaleX);
            expandedCanvasLocation = new Point((int)MathF.Round(expandedCanvasLocation.X * scaleX), (int)MathF.Round(expandedCanvasLocation.Y * scaleY));
            expandedSheetLocation = new Point((int)MathF.Round(expandedSheetLocation.X * scaleX), (int)MathF.Round(expandedSheetLocation.Y * scaleY));
            expandedSettingsLocation = new Point((int)MathF.Round(expandedSettingsLocation.X * scaleX), (int)MathF.Round(expandedSettingsLocation.Y * scaleY));
            expandedPickerLocation = new Point((int)MathF.Round(expandedPickerLocation.X * scaleX), (int)MathF.Round(expandedPickerLocation.Y * scaleY));
            expandedCanvasWidth = (int)MathF.Round(expandedCanvasWidth * scaleX);
            expandedSheetWidth = (int)MathF.Round(expandedSheetWidth * scaleX);
            int corner = Math.Max(8, (int)MathF.Round(28 * Math.Min(scaleX, scaleY)));
            var oldRegion = Region; Region = RoundedRegion(this, corner); oldRegion?.Dispose();
            lastWindowClientSize = ClientSize;
        }
        finally { applyingWindowResize = false; }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ConfigureWindows11TitleBar();
        var started = Environment.TickCount64;
        var popup = new System.Windows.Forms.Timer { Interval = 15 };
        var splash = new StartupSplashPanel(LoadEmbeddedStartupLogo());
        Controls.Add(splash); splash.BringToFront();
        Region = null;
        popup.Tick += (_, _) =>
        {
            long elapsed = Environment.TickCount64 - started;
            float iconProgress = Math.Clamp(elapsed / 950f, 0f, 1f);
            float fadeProgress = Math.Clamp((elapsed - 1950f) / 150f, 0f, 1f);
            splash.SetProgress(iconProgress, fadeProgress);
            if (elapsed >= 2100)
            {
                Controls.Remove(splash); splash.Dispose();
                popup.Stop();
                popup.Dispose();
            }
        };
        popup.Start();
    }

    void ConfigureWindows11TitleBar()
    {
        if (!IsHandleCreated || IsDisposed || !Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported()) return;
        try
        {
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(Handle);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            var titleBar = appWindow.TitleBar;
            var surface = BackColor;
            var foreground = darkTheme ? Color.FromArgb(242, 242, 246) : Color.FromArgb(31, 31, 31);
            var inactive = darkTheme ? Color.FromArgb(170, 172, 180) : Color.FromArgb(105, 105, 113);
            var hover = darkTheme ? Color.FromArgb(55, 57, 63) : Color.FromArgb(231, 231, 236);
            var pressed = darkTheme ? Color.FromArgb(68, 70, 77) : Color.FromArgb(218, 218, 224);
            static Windows.UI.Color NativeColor(Color color) => Windows.UI.Color.FromArgb(color.A, color.R, color.G, color.B);

            titleBar.BackgroundColor = NativeColor(surface);
            titleBar.ForegroundColor = NativeColor(foreground);
            titleBar.InactiveBackgroundColor = NativeColor(surface);
            titleBar.InactiveForegroundColor = NativeColor(inactive);
            titleBar.ButtonBackgroundColor = NativeColor(surface);
            titleBar.ButtonForegroundColor = NativeColor(foreground);
            titleBar.ButtonHoverBackgroundColor = NativeColor(hover);
            titleBar.ButtonHoverForegroundColor = NativeColor(foreground);
            titleBar.ButtonPressedBackgroundColor = NativeColor(pressed);
            titleBar.ButtonPressedForegroundColor = NativeColor(foreground);
            titleBar.ButtonInactiveBackgroundColor = NativeColor(surface);
            titleBar.ButtonInactiveForegroundColor = NativeColor(inactive);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Windows 11 title bar customization unavailable: {ex}");
        }
    }

    static Icon LoadEmbeddedAppIcon()
    {
        var assembly = typeof(ReviewForm).Assembly;
        var resource = assembly.GetManifestResourceNames().FirstOrDefault(name => name.EndsWith(".啊哈单词.ico", StringComparison.OrdinalIgnoreCase));
        if (resource != null)
        {
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream != null) { using var loaded = new Icon(stream); return (Icon)loaded.Clone(); }
        }
        return (Icon)SystemIcons.Application.Clone();
    }

    static Image LoadEmbeddedStartupLogo()
    {
        var assembly = typeof(ReviewForm).Assembly;
        var resource = assembly.GetManifestResourceNames().FirstOrDefault(name => name.EndsWith(".啊哈单词-splash-mark.png", StringComparison.OrdinalIgnoreCase));
        if (resource != null)
        {
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream != null) using (var loaded = Image.FromStream(stream)) return new Bitmap(loaded);
        }
        return LoadEmbeddedAppIcon().ToBitmap();
    }

    static Image LoadEmbeddedWordSheetGuide()
    {
        var assembly = typeof(ReviewForm).Assembly;
        var resource = assembly.GetManifestResourceNames().FirstOrDefault(name => name.EndsWith(".表格词库引导.png", StringComparison.OrdinalIgnoreCase));
        if (resource != null)
        {
            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream != null) using (var loaded = Image.FromStream(stream)) return new Bitmap(loaded);
        }
        return new Bitmap(1, 1);
    }

    void BuildUi()
    {
        importButton.Text = "使用EXCEL"; importButton.BackColor = Color.FromArgb(39,37,43); importButton.ForeColor = Color.White; importButton.FlatStyle = FlatStyle.Flat; importButton.FlatAppearance.BorderSize = 0; importButton.Font = new Font(UiFontName(),11,FontStyle.Bold); importButton.TabStop=false; importButton.Location = new(1490, 25); importButton.Size = new(170, 42); importButton.Region=RoundedRegion(importButton,12); importButton.TextAlign = ContentAlignment.MiddleCenter; importButton.Click += (_, _) => { if (wordSheetId == Guid.Empty) ImportFile(); else ToggleWordSheet(); }; Controls.Add(importButton);
        var mode = Button("顺序复习", Color.FromArgb(232,229,224), Color.FromArgb(100,94,88)); mode.TabStop=false; mode.Name="mode"; mode.Location=new(1675,25); mode.Size=new(110,42); mode.Region=RoundedRegion(mode,12); mode.TextAlign=ContentAlignment.MiddleCenter; mode.Click+=(_,_)=>{randomMode=!randomMode;mode.Text=randomMode?"随机抽取":"顺序复习";ResetOrder();RenderCard();}; Controls.Add(mode);
        var reset = Button("重置全部", Color.FromArgb(244,231,223), Color.FromArgb(167,107,71)); reset.TabStop=false; reset.Location = new(1795,25); reset.Size = new(110, 42); reset.Region=RoundedRegion(reset,12); reset.TextAlign = ContentAlignment.MiddleCenter; reset.Click += (_, _) => ResetAll(); Controls.Add(reset);
        var help = Button("使用说明", Color.FromArgb(232,229,224), Color.FromArgb(100,94,88)); help.TabStop=false; help.Location = new(1690, 88); help.Size = new(190, 40); help.Region=RoundedRegion(help,12); help.TextAlign = ContentAlignment.MiddleCenter; help.Click += (_, _) => MessageBox.Show("软件使用说明：\n\n请先用背单词软件去背，去过一遍，然后电脑上打开 EXCEL 表格，在 A 的下面写单词，B 的下面写翻译，这些写的时候尽量去写自己背过一遍但还是忘了的单词。", "使用说明", MessageBoxButtons.OK, MessageBoxIcon.Information); Controls.Add(help);
        var bankTitle = Label("词库", 16, FontStyle.Bold, Color.FromArgb(39,37,43)); bankTitle.Location = new(30, 145); bankTitle.AutoSize = true; Controls.Add(bankTitle);
        var bankSub = Label("本地保存 · 可编辑", 9, FontStyle.Regular, Color.Gray); bankSub.Location = new(30, 192); bankSub.AutoSize = true; Controls.Add(bankSub);
        wordBankList.Location = new(25, 225); wordBankList.Size = new(300, 625); wordBankList.FlowDirection=FlowDirection.TopDown; wordBankList.WrapContents=false; wordBankList.AutoScroll=true; wordBankList.HorizontalScroll.Enabled=false; wordBankList.HorizontalScroll.Visible=false; wordBankList.BackColor=Color.FromArgb(247,246,242); Controls.Add(wordBankList);
        englishInput.PlaceholderText="英语单词"; englishInput.Location=new(25,870); englishInput.Size=new(176,34); englishInput.Font=new Font(UiFontName(),10); Controls.Add(englishInput);
        chineseInput.PlaceholderText="中文翻译"; chineseInput.Location=new(25,912); chineseInput.Size=new(176,34); chineseInput.Font=new Font(UiFontName(),10); Controls.Add(chineseInput);
        noteInput.PlaceholderText="备注"; noteInput.Location=new(25,954); noteInput.Size=new(176,34); noteInput.Font=new Font(UiFontName(),10); Controls.Add(noteInput);
        var difficultyLabel=Label("难度",9,FontStyle.Regular,Color.FromArgb(120,114,108));difficultyLabel.Location=new(25,997);difficultyLabel.AutoSize=true;Controls.Add(difficultyLabel);
        difficulty1.Location=new(86,995);difficulty1.Size=new(20,20);difficulty1.TabStop=false;Controls.Add(difficulty1);
        difficulty2.Location=new(122,995);difficulty2.Size=new(20,20);difficulty2.TabStop=false;Controls.Add(difficulty2);
        difficulty3.Location=new(158,995);difficulty3.Size=new(20,20);difficulty3.TabStop=false;Controls.Add(difficulty3);
        var addWord = Button("OK", Color.FromArgb(39,37,43), Color.White); addWord.TabStop=false; addWord.Location=new(214,870); addWord.Size=new(75,118); addWord.Region=RoundedRegion(addWord,12); addWord.Click+=(_,_)=>AddWord(); Controls.Add(addWord);
        var filter = Button("筛选单词", Color.FromArgb(232,229,224), Color.FromArgb(100,94,88)); filter.TabStop=false; filter.Location=new(25,1026); filter.Size=new(275,38); filter.Region=RoundedRegion(filter,12); filter.TextAlign=ContentAlignment.MiddleCenter; filter.Click+=(_,_)=>ShowDifficultyFilter(filter); Controls.Add(filter);

        cardShadow.Location = new Point(833, 238); cardShadow.Size = new Size(694, 384); Controls.Add(cardShadow); cardShadow.SendToBack();
        card.Location = new(845, 250); card.Size = new(670, 360); card.BackColor = Color.White; card.BorderStyle = BorderStyle.None; card.Region = RoundedRegion(card, 16); card.Cursor = Cursors.Hand; Controls.Add(card);
        index.Visible = false; index.Location = new(25, 22); index.AutoSize = true; index.Font = new Font("Consolas", 9); index.ForeColor = Color.Gray; card.Controls.Add(index);
        titleArea.Location=new(845,205); titleArea.Size=new(670,38); titleArea.BackColor=Color.Transparent; titleArea.Cursor=Cursors.Hand; Controls.Add(titleArea);
        typeLabel.Text = "今日背诵"; typeLabel.Font = new Font(UiFontName(), 12, FontStyle.Bold); typeLabel.ForeColor = Color.FromArgb(70,67,65); typeLabel.Dock=DockStyle.Fill; typeLabel.TextAlign=ContentAlignment.MiddleCenter; typeLabel.Cursor=Cursors.Hand; titleArea.Controls.Add(typeLabel);
        titleArea.Click+=(_,_)=>typeLabel.Visible=!typeLabel.Visible; typeLabel.Click+=(_,_)=>typeLabel.Visible=!typeLabel.Visible; titleArea.MouseUp+=(_,e)=>{if(e.Button==MouseButtons.Right)EditCardTitle();}; typeLabel.MouseUp+=(_,e)=>{if(e.Button==MouseButtons.Right)EditCardTitle();};
        word.Font = new Font(UiFontName(), 28, FontStyle.Bold); currentCardFont = word.Font; word.AutoSize = false; word.TextAlign = ContentAlignment.MiddleCenter; word.Location = new(45, 118); word.Size = new(580, 100); word.Padding = new Padding(5); card.Controls.Add(word);
        hint.Font = new Font(UiFontName(), 10); hint.ForeColor = Color.Gray; hint.AutoSize=false; hint.TextAlign=ContentAlignment.MiddleCenter; hint.Location = new(45, 218); hint.Size = new(580, 82); card.Controls.Add(hint);
        tag.Visible = false; tag.AutoSize = true; tag.BorderStyle = BorderStyle.FixedSingle; tag.Location = new(25, 307); tag.Padding = new Padding(8,4,8,4); tag.ForeColor = Color.Gray; card.Controls.Add(tag);
        cardCurtain.Dock = DockStyle.Fill; cardCurtain.Visible = false; card.Controls.Add(cardCurtain); cardCurtain.Region = RoundedRegion(cardCurtain, 16); cardCurtain.BringToFront();
        wordSheetIndicator.Text = "正在使用表格词库"; wordSheetIndicator.Font = new Font(UiFontName(), 9, FontStyle.Bold); wordSheetIndicator.ForeColor = activeThemeColor; wordSheetIndicator.AutoSize = false; wordSheetIndicator.Size = new Size(670, 26); wordSheetIndicator.Location = new Point(845, 1013); wordSheetIndicator.TextAlign = ContentAlignment.MiddleCenter; wordSheetIndicator.Visible = false; wordSheetIndicator.BackColor = Color.Transparent; Controls.Add(wordSheetIndicator);
        card.Click += (_, _) => { if (!dragging && showingAnswer) Advance(true); }; card.MouseDown += (_, e) => { dragStart=e.Location; dragging=false; }; card.MouseMove += (_, e) => { if(Math.Abs(e.X-dragStart.X)>20) dragging=true; }; card.MouseUp += (_, e) => { if(Math.Abs(e.X-dragStart.X)>70) Advance(e.X>dragStart.X); };

        instruction.Text="点击“背过了”先查看意思，再点击卡片进入下一张"; instruction.Font=new Font(UiFontName(),9); instruction.ForeColor=Color.Gray; instruction.Location=new(770,630);instruction.Width=820;instruction.TextAlign=ContentAlignment.MiddleCenter;Controls.Add(instruction);
        var forgot=Button("没背过", Color.FromArgb(244,231,223), Color.FromArgb(167,107,71)); forgot.Location=new(845,675);forgot.Size=new(310,62);forgot.Region=RoundedRegion(forgot,16);forgot.TextAlign=ContentAlignment.MiddleCenter;forgot.Click+=(_,_)=>Advance(false);Controls.Add(forgot);
        var remembered=Button("背过", Color.FromArgb(223,235,225), Color.FromArgb(83,121,99)); remembered.Location=new(1175,675);remembered.Size=new(340,62);remembered.Region=RoundedRegion(remembered,16);remembered.TextAlign=ContentAlignment.MiddleCenter;remembered.Click+=(_,_)=>Advance(true);Controls.Add(remembered);
        var wrongTitle=Label("错题本",16,FontStyle.Bold,Color.FromArgb(39,37,43));wrongTitle.Location=new(1615,145);wrongTitle.AutoSize=true;Controls.Add(wrongTitle);
        var wrongSub=Label("勾选已经重新掌握的单词",9,FontStyle.Regular,Color.Gray);wrongSub.Location=new(1615,188);wrongSub.AutoSize=true;Controls.Add(wrongSub);
        wrongList.Location=new(1610,220);wrongList.Size=new(280,760);wrongList.FlowDirection=FlowDirection.TopDown;wrongList.WrapContents=false;wrongList.AutoScroll=true;wrongList.HorizontalScroll.Enabled=false;wrongList.HorizontalScroll.Visible=false;wrongList.VerticalScroll.Visible=true;wrongList.BackColor=Color.FromArgb(247,246,242);Controls.Add(wrongList);
        wrongMeaningToggle.Text="遮住翻译";wrongMeaningToggle.BackColor=Color.FromArgb(232,229,224);wrongMeaningToggle.ForeColor=Color.FromArgb(100,94,88);wrongMeaningToggle.FlatStyle=FlatStyle.Flat;wrongMeaningToggle.FlatAppearance.BorderSize=0;wrongMeaningToggle.Font=new Font(UiFontName(),9);wrongMeaningToggle.Size=new(120,34);wrongMeaningToggle.Location=new(1750,992);wrongMeaningToggle.Region=RoundedRegion(wrongMeaningToggle,12);wrongMeaningToggle.TabStop=false;wrongMeaningToggle.Click+=(_,_)=>{hideWrongMeaning=!hideWrongMeaning;wrongMeaningToggle.Text=hideWrongMeaning?"显示翻译":"遮住翻译";RefreshWrongBook();};Controls.Add(wrongMeaningToggle);
        var timerHotspot = new Panel { Location = new(1770, 1020), Size = new(120, 35), Anchor = AnchorStyles.Bottom | AnchorStyles.Right, Cursor = Cursors.Hand, BackColor = Color.Transparent }; timerLabel.TextAlign = ContentAlignment.MiddleCenter; timerLabel.Dock = DockStyle.Fill; timerLabel.Font = new Font("Consolas", 11); timerLabel.ForeColor = Color.FromArgb(140,134,128); timerHotspot.Controls.Add(timerLabel); timerHotspot.Click += (_, _) => timerLabel.Visible = !timerLabel.Visible; timerLabel.Click += (_, _) => timerLabel.Visible = false; Controls.Add(timerHotspot);
    }
    void BuildTabs()
    {
        var existing = Controls.Cast<Control>().ToArray();
        Controls.Clear();
        wordPage.Bounds = new Rectangle(0, 0, 1920, 1080);
        wordPage.BackColor = Color.FromArgb(247, 246, 242);
        foreach (var control in existing) wordPage.Controls.Add(control);
        // Reparenting changes sibling order: the opaque card must cover its shadow.
        cardShadow.SendToBack();
        card.BringToFront();
        Controls.Add(wordPage);

        canvasPage.Location = new Point(0, 72);
        canvasPage.Size = new Size(1920, 1008);
        canvasPage.BackColor = Color.FromArgb(250, 249, 246);
        canvasPage.Visible = false;
        canvasSurface.Dock = DockStyle.Fill;
        canvasSurface.TabStop = true;
        canvasSurface.BackColor = Color.FromArgb(250, 249, 246);
        canvasSurface.Paint += (_, e) => DrawCanvas(e.Graphics, e.ClipRectangle);
        canvasSurface.Resize += (_, _) => InvalidateCanvasPaintCache();
        canvasSurface.MouseDown += CanvasMouseDown;
        canvasSurface.MouseMove += CanvasMouseMove;
        canvasSurface.MouseUp += CanvasMouseUp;
        canvasSurface.MouseDoubleClick += CanvasMouseDoubleClick;
        canvasSurface.MouseEnter += (_, _) => { if (organizeSelected) canvasSurface.Focus(); };
        canvasSurface.MouseLeave += (_, _) => { if(IsCanvasPaintTool)canvasSurface.Invalidate(); if (canvasAnchorPreviewInsertIndex >= 0) { ClearCanvasAnchorInsertionPreview(); canvasSurface.Invalidate(); } };
        canvasSurface.MouseWheel += CanvasMouseWheel;
        canvasSurface.AllowDrop = true;
        canvasSurface.DragEnter += CanvasSurfaceDragEnter;
        canvasSurface.DragDrop += CanvasSurfaceDragDrop;
        canvasInlineEditor.Visible = false;
        canvasInlineEditor.BorderStyle = BorderStyle.FixedSingle;
        canvasInlineEditor.BackColor = Color.White;
        canvasInlineEditor.ForeColor = Color.FromArgb(70, 67, 65);
        canvasInlineEditor.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && (e.Modifiers & Keys.Shift) != Keys.Shift) { CommitCanvasInlineEdit(); e.SuppressKeyPress = true; }
        };
        canvasInlineEditor.LostFocus += (_, _) => CommitCanvasInlineEdit();
        canvasSurface.Controls.Add(canvasInlineEditor);
        canvasPage.Controls.Add(canvasSurface);
        canvasLayersPanel.BackColor = Color.FromArgb(245, 245, 247);
        canvasLayersPanel.Size = new Size(270, canvasPage.Height);
        canvasLayersPanel.Anchor = AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom;
        canvasLayersPanel.Location = new Point(canvasPage.Width - canvasLayersPanel.Width, 0);
        canvasLayersPanel.Visible = false;
        var layersHeader = Label("图层", 14, FontStyle.Bold, Color.FromArgb(39, 37, 43));
        layersHeader.Location = new Point(16, 14); layersHeader.Size = new Size(238, 32); canvasLayersPanel.Controls.Add(layersHeader);
        canvasLayersList.Location = new Point(12, 54); canvasLayersList.Size = new Size(246, canvasLayersPanel.Height - 170); canvasLayersList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right; canvasLayersList.FlowDirection = FlowDirection.TopDown; canvasLayersList.WrapContents = false; canvasLayersList.AutoScroll = true; canvasLayersList.BackColor = canvasLayersPanel.BackColor; canvasLayersPanel.Controls.Add(canvasLayersList);
        canvasLayerPrevious.Text = "‹"; canvasLayerPrevious.Location = new Point(14, canvasLayersPanel.Height - 52); canvasLayerPrevious.Size = new Size(38, 34); canvasLayerPrevious.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; canvasLayerPrevious.Click += (_, _) => { if (canvasLayerPage > 0) { canvasLayerPage--; RefreshCanvasLayers(); } };
        canvasLayerPageLabel.Location = new Point(56, canvasLayersPanel.Height - 52); canvasLayerPageLabel.Size = new Size(148, 34); canvasLayerPageLabel.TextAlign = ContentAlignment.MiddleCenter; canvasLayerPageLabel.Font = new Font(UiFontName(), 8); canvasLayerPageLabel.ForeColor = Color.FromArgb(120, 114, 108);
        canvasLayerNext.Text = "›"; canvasLayerNext.Location = new Point(208, canvasLayersPanel.Height - 52); canvasLayerNext.Size = new Size(38, 34); canvasLayerNext.Anchor = AnchorStyles.Bottom | AnchorStyles.Right; canvasLayerNext.Click += (_, _) => { if ((canvasLayerPage + 1) * CanvasLayerPageSize < canvasNodes.Count) { canvasLayerPage++; RefreshCanvasLayers(); } };
        foreach (var pageControl in new Control[] { canvasLayerPrevious, canvasLayerPageLabel, canvasLayerNext }) { pageControl.BackColor = Color.FromArgb(232, 229, 224); canvasLayersPanel.Controls.Add(pageControl); }
        canvasLayersToggle.Text = "图层  ▴"; canvasLayersToggle.Size = new Size(112, 38); canvasLayersToggle.Anchor = AnchorStyles.Bottom | AnchorStyles.Right; canvasLayersToggle.Location = new Point(canvasPage.Width - 136, canvasPage.Height - 48); canvasLayersToggle.Region = RoundedRegion(canvasLayersToggle, 10); canvasLayersToggle.FlatStyle = FlatStyle.Flat; canvasLayersToggle.FlatAppearance.BorderSize = 0; canvasLayersToggle.BackColor = Color.FromArgb(39, 37, 43); canvasLayersToggle.ForeColor = Color.White; canvasLayersToggle.Font = new Font(UiFontName(), 10, FontStyle.Bold); canvasLayersToggle.Click += (_, _) => ToggleCanvasLayers();
        canvasPage.Controls.Add(canvasLayersPanel); canvasPage.Controls.Add(canvasLayersToggle);
        canvasLayersPanel.BringToFront(); canvasLayersToggle.BringToFront();
        canvasPropsPanel.BackColor = Color.FromArgb(230, 235, 246);
        canvasPropsPanel.Size = new Size(270, canvasPage.Height);
        canvasPropsPanel.AutoScroll = true;
        canvasPropsPanel.Anchor = AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom;
        canvasPropsPanel.Location = new Point(canvasPage.Width - canvasPropsPanel.Width, 0);
        canvasPropsPanel.Visible = false;
        var propsHeader = Label("属性", 14, FontStyle.Bold, Color.FromArgb(39, 37, 43));
        propsHeader.Location = new Point(16, 14); propsHeader.Size = new Size(238, 32); canvasPropsPanel.Controls.Add(propsHeader);
        var propsNameLabel = Label("名字", 9, FontStyle.Regular, Color.FromArgb(120, 114, 108));
        propsNameLabel.Location = new Point(16, 58); propsNameLabel.AutoSize = true; canvasPropsPanel.Controls.Add(propsNameLabel);
        canvasPropName.Location = new Point(16, 80); canvasPropName.Size = new Size(238, 30); canvasPropName.Font = new Font(UiFontName(), 10);
        canvasPropName.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { ApplyCanvasPropName(); canvasSurface.Focus(); e.SuppressKeyPress = true; } };
        canvasPropName.LostFocus += (_, _) => ApplyCanvasPropName();
        canvasPropsPanel.Controls.Add(canvasPropName);
        canvasNodeNameControls.AddRange(new Control[] { propsNameLabel, canvasPropName });
        var transformHeader = Label("位置与变换", 9, FontStyle.Bold, Color.FromArgb(70, 67, 65));
        transformHeader.Location = new Point(16, 118); transformHeader.AutoSize = true; canvasPropsPanel.Controls.Add(transformHeader);
        void AddTransformField(string label, NumericUpDown input, int x, int y, decimal minimum, decimal maximum, int decimals, decimal increment, int fieldWidth = 108)
        {
            var caption = Label(label, 8, FontStyle.Regular, Color.FromArgb(120, 114, 108));
            caption.Location = new Point(x, y); caption.AutoSize = true; canvasPropsPanel.Controls.Add(caption);
            input.Location = new Point(x, y + 17); input.Size = new Size(fieldWidth, 28); input.Minimum = minimum; input.Maximum = maximum; input.DecimalPlaces = decimals; input.Increment = increment; input.Font = new Font(UiFontName(), 9);
            input.Enter += (_, _) => canvasTransformHistoryPushed = false;
            input.ValueChanged += (_, _) => ApplyCanvasPropTransform();
            input.Validated += (_, _) => { ApplyCanvasPropTransform(); canvasTransformHistoryPushed = false; };
            input.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { ApplyCanvasPropTransform(); canvasTransformHistoryPushed = false; canvasSurface.Focus(); e.SuppressKeyPress = true; } };
            canvasPropsPanel.Controls.Add(input); canvasTransformPropControls.Add(caption); canvasTransformPropControls.Add(input);
        }
        AddTransformField("X", canvasPropX, 16, 118, -100000, 100000, 1, 1);
        AddTransformField("Y", canvasPropY, 136, 118, -100000, 100000, 1, 1);
        AddTransformField("宽", canvasPropWidth, 16, 164, 1, 100000, 1, 1);
        AddTransformField("高", canvasPropHeight, 136, 164, 1, 100000, 1, 1);
        AddTransformField("角度°", canvasPropRotation, 16, 210, -3600, 3600, 1, 1, 72);
        AddTransformField("不透明度%", canvasPropOpacity, 96, 210, 0, 100, 0, 1, 72);
        AddTransformField("填充%", canvasPropFillOpacity, 176, 210, 0, 100, 0, 1, 72);
        canvasPropsMultiHint.Text = ""; canvasPropsMultiHint.Font = new Font(UiFontName(), 9); canvasPropsMultiHint.ForeColor = Color.FromArgb(120, 114, 108);
        canvasPropsMultiHint.Location = new Point(16, 84); canvasPropsMultiHint.AutoSize = true; canvasPropsMultiHint.Visible = false; canvasPropsPanel.Controls.Add(canvasPropsMultiHint);
        canvasPropContentLabel.Text = "内容"; canvasPropContentLabel.Font = new Font(UiFontName(), 9, FontStyle.Regular); canvasPropContentLabel.ForeColor = Color.FromArgb(120, 114, 108);
        canvasPropContentLabel.Location = new Point(16, 118); canvasPropContentLabel.AutoSize = true; canvasPropsPanel.Controls.Add(canvasPropContentLabel);
        canvasPropContent.Location = new Point(16, 138); canvasPropContent.Size = new Size(238, 92); canvasPropContent.Multiline = true; canvasPropContent.Font = new Font(UiFontName(), 10);
        canvasPropContent.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && (e.Modifiers & Keys.Control) == Keys.Control) { ApplyCanvasPropContent(); canvasSurface.Focus(); e.SuppressKeyPress = true; } };
        canvasPropContent.LostFocus += (_, _) => ApplyCanvasPropContent();
        canvasPropsPanel.Controls.Add(canvasPropContent);
        var propsFontSizeLabel = Label("字号", 9, FontStyle.Regular, Color.FromArgb(120, 114, 108));
        propsFontSizeLabel.Location = new Point(16, 240); propsFontSizeLabel.AutoSize = true; canvasPropsPanel.Controls.Add(propsFontSizeLabel);
        canvasPropFontSize.Location = new Point(16, 260); canvasPropFontSize.Size = new Size(110, 30); canvasPropFontSize.Minimum = 4; canvasPropFontSize.Maximum = 96; canvasPropFontSize.DecimalPlaces = 1; canvasPropFontSize.Increment = 1; canvasPropFontSize.Font = new Font(UiFontName(), 10);
        canvasPropFontSize.ValueChanged += (_, _) => ApplyCanvasPropFontSize();
        canvasPropsPanel.Controls.Add(canvasPropFontSize);
        var propsAlignLabel = Label("对齐", 9, FontStyle.Regular, Color.FromArgb(120, 114, 108));
        propsAlignLabel.Location = new Point(16, 300); propsAlignLabel.AutoSize = true; canvasPropsPanel.Controls.Add(propsAlignLabel);
        canvasPropAlignLeft.Text = "左对齐"; canvasPropAlignCenter.Text = "居中"; canvasPropAlignRight.Text = "右对齐";
        foreach (var (button, index) in new[] { (canvasPropAlignLeft, 0), (canvasPropAlignCenter, 1), (canvasPropAlignRight, 2) })
        {
            button.Location = new Point(16 + index * 81, 320); button.Size = new Size(76, 30); button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0; button.Font = new Font(UiFontName(), 9); button.BackColor = Color.FromArgb(232, 229, 224); button.ForeColor = Color.FromArgb(70, 67, 65);
            button.Click += (_, _) => ApplyCanvasPropFontAlign(index);
            canvasPropsPanel.Controls.Add(button);
        }
        canvasPropAlignTop.Text = "上对齐"; canvasPropAlignMiddle.Text = "居中"; canvasPropAlignBottom.Text = "下对齐";
        foreach (var (button, index) in new[] { (canvasPropAlignTop, 0), (canvasPropAlignMiddle, 1), (canvasPropAlignBottom, 2) })
        {
            button.Location = new Point(16 + index * 81, 352); button.Size = new Size(76, 30); button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0; button.Font = new Font(UiFontName(), 9); button.BackColor = Color.FromArgb(232, 229, 224); button.ForeColor = Color.FromArgb(70, 67, 65);
            button.Click += (_, _) => ApplyCanvasPropFontVAlign(index);
            canvasPropsPanel.Controls.Add(button);
        }
        var propsStyleLabel = Label("样式", 9, FontStyle.Regular, Color.FromArgb(120, 114, 108));
        propsStyleLabel.Location = new Point(16, 392); propsStyleLabel.AutoSize = true; canvasPropsPanel.Controls.Add(propsStyleLabel);
        canvasPropBold.Text = "加粗"; canvasPropBold.Location = new Point(16, 414); canvasPropBold.AutoSize = true; canvasPropBold.Font = new Font(UiFontName(), 10); canvasPropBold.ThreeState = true;
        canvasPropItalic.Text = "倾斜"; canvasPropItalic.Location = new Point(96, 414); canvasPropItalic.AutoSize = true; canvasPropItalic.Font = new Font(UiFontName(), 10); canvasPropItalic.ThreeState = true;
        canvasPropStrikeout.Text = "删除线"; canvasPropStrikeout.Location = new Point(176, 414); canvasPropStrikeout.AutoSize = true; canvasPropStrikeout.Font = new Font(UiFontName(), 10); canvasPropStrikeout.ThreeState = true;
        canvasPropBold.CheckedChanged += (_, _) => ApplyCanvasPropFontStyle();
        canvasPropItalic.CheckedChanged += (_, _) => ApplyCanvasPropFontStyle();
        canvasPropStrikeout.CheckedChanged += (_, _) => ApplyCanvasPropFontStyle();
        canvasPropsPanel.Controls.Add(canvasPropBold); canvasPropsPanel.Controls.Add(canvasPropItalic); canvasPropsPanel.Controls.Add(canvasPropStrikeout);
        var propsColorLabel = Label("字体颜色", 9, FontStyle.Regular, Color.FromArgb(120, 114, 108));
        propsColorLabel.Location = new Point(16, 446); propsColorLabel.AutoSize = true; canvasPropsPanel.Controls.Add(propsColorLabel);
        canvasPropColor.Location = new Point(16, 466); canvasPropColor.Size = new Size(154, 30); canvasPropColor.FlatStyle = FlatStyle.Flat; canvasPropColor.FlatAppearance.BorderSize = 0; canvasPropColor.Font = new Font(UiFontName(), 9); canvasPropColor.Text = "选择颜色";
        canvasPropColor.Click += (_, _) => PickCanvasPropColor();
        canvasPropsPanel.Controls.Add(canvasPropColor);
        canvasPropColorReset.Location = new Point(180, 466); canvasPropColorReset.Size = new Size(64, 30); canvasPropColorReset.FlatStyle = FlatStyle.Flat; canvasPropColorReset.FlatAppearance.BorderSize = 0; canvasPropColorReset.Font = new Font(UiFontName(), 9); canvasPropColorReset.Text = "默认"; canvasPropColorReset.BackColor = Color.FromArgb(232, 229, 224); canvasPropColorReset.ForeColor = Color.FromArgb(70, 67, 65);
        canvasPropColorReset.Click += (_, _) => ResetCanvasPropColor();
        canvasPropsPanel.Controls.Add(canvasPropColorReset);
        var propsFillColorLabel = Label("填充颜色", 9, FontStyle.Regular, Color.FromArgb(120, 114, 108));
        propsFillColorLabel.Location = new Point(16, 506); propsFillColorLabel.AutoSize = true; canvasPropsPanel.Controls.Add(propsFillColorLabel);
        canvasPropFillColor.Location = new Point(16, 526); canvasPropFillColor.Size = new Size(154, 30); canvasPropFillColor.FlatStyle = FlatStyle.Flat; canvasPropFillColor.FlatAppearance.BorderSize = 0; canvasPropFillColor.Font = new Font(UiFontName(), 9); canvasPropFillColor.Text = "选择颜色";
        canvasPropFillColor.Click += (_, _) => PickCanvasPropFillColor();
        canvasPropsPanel.Controls.Add(canvasPropFillColor);
        canvasPropFillColorReset.Location = new Point(180, 526); canvasPropFillColorReset.Size = new Size(64, 30); canvasPropFillColorReset.FlatStyle = FlatStyle.Flat; canvasPropFillColorReset.FlatAppearance.BorderSize = 0; canvasPropFillColorReset.Font = new Font(UiFontName(), 9); canvasPropFillColorReset.Text = "默认"; canvasPropFillColorReset.BackColor = Color.FromArgb(232, 229, 224); canvasPropFillColorReset.ForeColor = Color.FromArgb(70, 67, 65);
        canvasPropFillColorReset.Click += (_, _) => ResetCanvasPropFillColor();
        canvasPropsPanel.Controls.Add(canvasPropFillColorReset);
        var propsCornerRadiusLabel = Label("圆角", 9, FontStyle.Regular, Color.FromArgb(120, 114, 108));
        propsCornerRadiusLabel.Location = new Point(16, 566); propsCornerRadiusLabel.AutoSize = true; canvasPropsPanel.Controls.Add(propsCornerRadiusLabel);
        canvasPropCornerRadius.Location = new Point(16, 586); canvasPropCornerRadius.Size = new Size(110, 30); canvasPropCornerRadius.Minimum = 0; canvasPropCornerRadius.Maximum = 500; canvasPropCornerRadius.DecimalPlaces = 0; canvasPropCornerRadius.Increment = 1; canvasPropCornerRadius.Font = new Font(UiFontName(), 10);
        canvasPropCornerRadius.ValueChanged += (_, _) => ApplyCanvasPropCornerRadius();
        canvasPropsPanel.Controls.Add(canvasPropCornerRadius);
        var propsStrokeLabel = Label("描边", 9, FontStyle.Regular, Color.FromArgb(120, 114, 108));
        propsStrokeLabel.Location = new Point(16, 626); propsStrokeLabel.AutoSize = true; canvasPropsPanel.Controls.Add(propsStrokeLabel);
        var propsStrokeWidthLabel = Label("粗细", 9, FontStyle.Regular, Color.FromArgb(120, 114, 108));
        propsStrokeWidthLabel.Location = new Point(16, 654); propsStrokeWidthLabel.AutoSize = true; canvasPropsPanel.Controls.Add(propsStrokeWidthLabel);
        canvasPropStrokeWidth.Location = new Point(60, 650); canvasPropStrokeWidth.Size = new Size(76, 30); canvasPropStrokeWidth.Minimum = 0; canvasPropStrokeWidth.Maximum = 100; canvasPropStrokeWidth.DecimalPlaces = 0; canvasPropStrokeWidth.Increment = 1; canvasPropStrokeWidth.Font = new Font(UiFontName(), 10);
        canvasPropStrokeWidth.ValueChanged += (_, _) => ApplyCanvasPropStrokeWidth();
        canvasPropsPanel.Controls.Add(canvasPropStrokeWidth);
        canvasPropStrokeDash.Text = "虚线"; canvasPropStrokeDash.Location = new Point(146, 654); canvasPropStrokeDash.AutoSize = true; canvasPropStrokeDash.Font = new Font(UiFontName(), 10); canvasPropStrokeDash.ThreeState = true;
        canvasPropStrokeDash.CheckedChanged += (_, _) => ApplyCanvasPropStrokeDash();
        canvasPropsPanel.Controls.Add(canvasPropStrokeDash);
        canvasPropStrokeColor.Location = new Point(16, 694); canvasPropStrokeColor.Size = new Size(154, 30); canvasPropStrokeColor.FlatStyle = FlatStyle.Flat; canvasPropStrokeColor.FlatAppearance.BorderSize = 0; canvasPropStrokeColor.Font = new Font(UiFontName(), 9); canvasPropStrokeColor.Text = "选择颜色";
        canvasPropStrokeColor.Click += (_, _) => PickCanvasPropStrokeColor();
        canvasPropsPanel.Controls.Add(canvasPropStrokeColor);
        canvasPropStrokeColorReset.Location = new Point(180, 694); canvasPropStrokeColorReset.Size = new Size(64, 30); canvasPropStrokeColorReset.FlatStyle = FlatStyle.Flat; canvasPropStrokeColorReset.FlatAppearance.BorderSize = 0; canvasPropStrokeColorReset.Font = new Font(UiFontName(), 9); canvasPropStrokeColorReset.Text = "默认"; canvasPropStrokeColorReset.BackColor = Color.FromArgb(232, 229, 224); canvasPropStrokeColorReset.ForeColor = Color.FromArgb(70, 67, 65);
        canvasPropStrokeColorReset.Click += (_, _) => ResetCanvasPropStrokeColor();
        canvasPropsPanel.Controls.Add(canvasPropStrokeColorReset);
        var propsStrokeAlignLabel = Label("描边位置", 9, FontStyle.Regular, Color.FromArgb(120, 114, 108));
        propsStrokeAlignLabel.Location = new Point(16, 734); propsStrokeAlignLabel.AutoSize = true; canvasPropsPanel.Controls.Add(propsStrokeAlignLabel);
        canvasPropStrokeInner.Text = "内部"; canvasPropStrokeCenter.Text = "中间"; canvasPropStrokeOuter.Text = "外部";
        foreach (var (button, align) in new[] { (canvasPropStrokeInner, 0), (canvasPropStrokeCenter, 1), (canvasPropStrokeOuter, 2) })
        {
            button.Location = new Point(16 + align * 78, 756); button.Size = new Size(74, 30); button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0; button.Font = new Font(UiFontName(), 9); button.BackColor = Color.FromArgb(232, 229, 224); button.ForeColor = Color.FromArgb(70, 67, 65);
            int alignValue = align;
            button.Click += (_, _) => ApplyCanvasPropStrokeAlign(alignValue);
            canvasPropsPanel.Controls.Add(button);
        }
        canvasStrokePropControls.AddRange(new Control[] { propsStrokeLabel, propsStrokeWidthLabel, canvasPropStrokeWidth, canvasPropStrokeDash, canvasPropStrokeColor, canvasPropStrokeColorReset, propsStrokeAlignLabel, canvasPropStrokeInner, canvasPropStrokeCenter, canvasPropStrokeOuter });
        canvasTextPropControls.AddRange(new Control[] { canvasPropContentLabel, canvasPropContent, propsFontSizeLabel, canvasPropFontSize, propsAlignLabel, canvasPropAlignLeft, canvasPropAlignCenter, canvasPropAlignRight, canvasPropAlignTop, canvasPropAlignMiddle, canvasPropAlignBottom, propsStyleLabel, canvasPropBold, canvasPropItalic, canvasPropStrikeout, propsColorLabel, canvasPropColor, canvasPropColorReset });
        canvasSquarePropControls.AddRange(new Control[] { propsCornerRadiusLabel, canvasPropCornerRadius });
        canvasFillColorPropControls.AddRange(new Control[] { propsFillColorLabel, canvasPropFillColor, canvasPropFillColorReset });
        foreach (var control in canvasTextPropControls.Concat(canvasFillColorPropControls)) canvasTextPropOriginalLocations[control] = control.Location;
        var propsLinkTextLabel = Label("文字内容", 9, FontStyle.Regular, Color.FromArgb(120, 114, 108));
        propsLinkTextLabel.Location = new Point(16, 58); propsLinkTextLabel.AutoSize = true; canvasPropsPanel.Controls.Add(propsLinkTextLabel);
        canvasPropLinkText.Location = new Point(16, 80); canvasPropLinkText.Size = new Size(238, 30); canvasPropLinkText.Font = new Font(UiFontName(), 10);
        canvasPropLinkText.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { ApplyCanvasLinkLabel(); canvasSurface.Focus(); e.SuppressKeyPress = true; } };
        canvasPropLinkText.LostFocus += (_, _) => ApplyCanvasLinkLabel();
        canvasPropsPanel.Controls.Add(canvasPropLinkText);
        var propsLinkColorLabel = Label("线条颜色", 9, FontStyle.Regular, Color.FromArgb(120, 114, 108));
        propsLinkColorLabel.Location = new Point(16, 124); propsLinkColorLabel.AutoSize = true; canvasPropsPanel.Controls.Add(propsLinkColorLabel);
        canvasPropLinkColor.Location = new Point(16, 146); canvasPropLinkColor.Size = new Size(170, 30); canvasPropLinkColor.FlatStyle = FlatStyle.Flat; canvasPropLinkColor.FlatAppearance.BorderSize = 0; canvasPropLinkColor.Font = new Font(UiFontName(), 9); canvasPropLinkColor.Text = "选择颜色";
        canvasPropLinkColor.Click += (_, _) => PickCanvasLinkColor();
        canvasPropsPanel.Controls.Add(canvasPropLinkColor);
        canvasPropLinkColorReset.Location = new Point(190, 146); canvasPropLinkColorReset.Size = new Size(64, 30); canvasPropLinkColorReset.FlatStyle = FlatStyle.Flat; canvasPropLinkColorReset.FlatAppearance.BorderSize = 0; canvasPropLinkColorReset.Font = new Font(UiFontName(), 9); canvasPropLinkColorReset.Text = "默认"; canvasPropLinkColorReset.BackColor = Color.FromArgb(232, 229, 224); canvasPropLinkColorReset.ForeColor = Color.FromArgb(70, 67, 65);
        canvasPropLinkColorReset.Click += (_, _) => ResetCanvasLinkColor();
        canvasPropsPanel.Controls.Add(canvasPropLinkColorReset);
        var propsLinkArrowLabel = Label("箭头朝向", 9, FontStyle.Regular, Color.FromArgb(120, 114, 108));
        propsLinkArrowLabel.Location = new Point(16, 190); propsLinkArrowLabel.AutoSize = true; canvasPropsPanel.Controls.Add(propsLinkArrowLabel);
        canvasPropLinkArrowNone.Text = "无箭头"; canvasPropLinkArrowEnd.Text = "→ 终点"; canvasPropLinkArrowStart.Text = "起点 →"; canvasPropLinkArrowBoth.Text = "双向";
        foreach (var (button, arrow) in new[] { (canvasPropLinkArrowNone, 0), (canvasPropLinkArrowEnd, 1), (canvasPropLinkArrowStart, 2), (canvasPropLinkArrowBoth, 3) })
        {
            button.Location = new Point(16 + arrow * 60, 212); button.Size = new Size(56, 30); button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0; button.Font = new Font(UiFontName(), 9); button.BackColor = Color.FromArgb(232, 229, 224); button.ForeColor = Color.FromArgb(70, 67, 65);
            int arrowValue = arrow;
            button.Click += (_, _) => ApplyCanvasLinkArrow(arrowValue);
            canvasPropsPanel.Controls.Add(button);
        }
        canvasLinkPropControls.AddRange(new Control[] { propsLinkTextLabel, canvasPropLinkText, propsLinkColorLabel, canvasPropLinkColor, canvasPropLinkColorReset, propsLinkArrowLabel, canvasPropLinkArrowNone, canvasPropLinkArrowEnd, canvasPropLinkArrowStart, canvasPropLinkArrowBoth });
        foreach (var control in canvasLinkPropControls) control.Visible = false;
        var fixedProps = new HashSet<Control>(canvasNodeNameControls.Concat(canvasTransformPropControls).Concat(canvasLinkPropControls)) { propsHeader, canvasPropsMultiHint };
        foreach (Control control in canvasPropsPanel.Controls)
            if (!fixedProps.Contains(control)) control.Top += 174;
        canvasPage.Controls.Add(canvasPropsPanel);
        BuildCanvasAlignBar(); canvasPage.Controls.Add(canvasAlignBar);
        canvasPicker.Size = new Size(170, 38); canvasPicker.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; canvasPicker.Location = new Point(24, ClientSize.Height - 48); canvasPicker.Region = RoundedRegion(canvasPicker, 10); canvasPicker.FlatStyle = FlatStyle.Flat; canvasPicker.FlatAppearance.BorderSize = 0; canvasPicker.BackColor = Color.FromArgb(232, 229, 224); canvasPicker.ForeColor = Color.FromArgb(70, 67, 65); canvasPicker.Font = new Font(UiFontName(), 10, FontStyle.Bold); canvasPicker.Text = "画布：主画布  ▾"; canvasPicker.Visible = false; canvasPicker.Click += (_, _) => ShowCanvasBoardMenu();
        Controls.Add(canvasPage);

        sheetPage.Location = new Point(0, 72); sheetPage.Size = new Size(1920, 1008);
        sheetPage.BackColor = Color.White; sheetPage.Visible = false;
        var sheetHeader = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = Color.FromArgb(247, 246, 242) };
        var sheetTitle = Label("表格", 16, FontStyle.Bold, Color.FromArgb(39, 37, 43));
        sheetTitle.Location = new Point(24, 14); sheetTitle.Size = new Size(120, 36); sheetHeader.Controls.Add(sheetTitle);
        var sheetHint = Label("Ctrl+滚轮缩放 · F1遮挡 · F2绿色 · F3红色 · F4加粗 · 自动保存", 9, FontStyle.Regular, Color.FromArgb(120, 114, 108));
        sheetHint.Location = new Point(150, 21); sheetHint.Size = new Size(900, 28); sheetHeader.Controls.Add(sheetHint);
        sheetGrid.Dock = DockStyle.Fill;
        sheetGrid.BackgroundColor = Color.White; sheetGrid.BorderStyle = BorderStyle.None;
        sheetGrid.RowHeadersWidth = 64; sheetGrid.RowHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        sheetGrid.ColumnHeadersHeight = 34; sheetGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        sheetGrid.RowTemplate.Height = 28; sheetGrid.AllowUserToAddRows = true; sheetGrid.AllowUserToDeleteRows = true;
        sheetGrid.AllowUserToResizeColumns = true; sheetGrid.AllowUserToResizeRows = true;
        sheetGrid.MultiSelect = true; sheetGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        sheetGrid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
        sheetGrid.EnableHeadersVisualStyles = false; sheetGrid.GridColor = Color.FromArgb(229, 229, 229);
        sheetGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(243, 243, 243);
        sheetGrid.RowHeadersDefaultCellStyle.BackColor = Color.FromArgb(243, 243, 243);
        sheetGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(204, 228, 247);
        sheetGrid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(31, 31, 31);
        for (int i = 0; i < 26; i++) sheetGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = ((char)('A' + i)).ToString(), HeaderText = ((char)('A' + i)).ToString(), Width = 125, SortMode = DataGridViewColumnSortMode.NotSortable });
        sheetGrid.Rows.Add(100);
        sheetGrid.RowPostPaint += (_, e) => sheetGrid.Rows[e.RowIndex].HeaderCell.Value = (e.RowIndex + 1).ToString();
        sheetGrid.CellValueChanged += (_, _) => QueueSheetSave();
        sheetGrid.UserDeletedRow += (_, _) => QueueSheetSave();
        sheetGrid.KeyDown += SheetGridKeyDown;
        sheetGrid.MouseWheel += SheetGridMouseWheel;
        sheetGrid.CellFormatting += SheetGridCellFormatting;
        sheetGrid.Scroll += (_, e) => { if (e.ScrollOrientation == ScrollOrientation.VerticalScroll) SyncWordSheetScrollBarFromGrid(); };
        sheetGrid.RowsAdded += (_, _) => UpdateWordSheetScrollBarRange();
        sheetGrid.RowsRemoved += (_, _) => UpdateWordSheetScrollBarRange();
        sheetGrid.SizeChanged += (_, _) => UpdateWordSheetScrollBarRange();
        wordSheetScrollBar.Visible = false;
        wordSheetScrollBar.ValueChanged += (_, _) => SyncGridFromWordSheetScrollBar();
        var sheetFooter = new Panel { Dock = DockStyle.Bottom, Height = 68, BackColor = Color.FromArgb(247, 246, 242) };
        sheetPicker.Text = "表格：表格 1  ▾"; sheetPicker.Size = new Size(230, 40);
        sheetPicker.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        sheetPicker.Region = RoundedRegion(sheetPicker, 10); sheetPicker.FlatStyle = FlatStyle.Flat;
        sheetPicker.FlatAppearance.BorderSize = 0; sheetPicker.BackColor = Color.FromArgb(39, 37, 43);
        sheetPicker.ForeColor = Color.White; sheetPicker.Font = new Font(UiFontName(), 10, FontStyle.Bold);
        sheetPicker.Click += (_, _) => ShowSheetMenu();
        sheetPicker.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) ShowSheetMenu(); };
        var addSheetButton = new Button { Text = "+", Size = new Size(46, 40), Anchor = AnchorStyles.Top | AnchorStyles.Right, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(39, 37, 43), ForeColor = Color.White, Font = new Font(UiFontName(), 17, FontStyle.Bold), TabStop = false };
        addSheetButton.FlatAppearance.BorderSize = 0; addSheetButton.Region = RoundedRegion(addSheetButton, 10);
        addSheetButton.AccessibleName = "导入 Excel 表格"; addSheetButton.Click += (_, _) => ImportExcelSheet();
        var addSheetTip = new ToolTip(); addSheetTip.SetToolTip(addSheetButton, "导入 Excel 为新表格");
        wordSheetButton.Text = "作为表格词库"; wordSheetButton.Size = new Size(170, 40); wordSheetButton.Anchor = AnchorStyles.Top | AnchorStyles.Left; wordSheetButton.FlatStyle = FlatStyle.Flat; wordSheetButton.FlatAppearance.BorderSize = 0; wordSheetButton.Font = new Font(UiFontName(), 10, FontStyle.Bold); wordSheetButton.TabStop = false; wordSheetButton.Region = RoundedRegion(wordSheetButton, 10);
        wordSheetButton.Click += (_, _) => SetWordSheet();
        void LayoutSheetFooter()
        {
            int y = Math.Max(8, (sheetFooter.ClientSize.Height - 40) / 2);
            int right = sheetFooter.ClientSize.Width - 28;
            addSheetButton.Location = new Point(right - addSheetButton.Width, y);
            sheetPicker.Location = new Point(addSheetButton.Left - 12 - sheetPicker.Width, y);
            wordSheetButton.Location = new Point(24, y);
        }
        sheetFooter.Resize += (_, _) => LayoutSheetFooter();
        LayoutSheetFooter();
        sheetFooter.Controls.Add(wordSheetButton);
        sheetFooter.Controls.Add(sheetPicker);
        sheetFooter.Controls.Add(addSheetButton);
        sheetPage.Controls.Add(sheetGrid); sheetPage.Controls.Add(sheetHeader); sheetPage.Controls.Add(sheetFooter);
        BuildWordSheetGuide();
        sheetPage.Controls.Add(wordSheetScrollBar);
        sheetPage.Resize += (_, _) => LayoutWordSheetScrollBar();
        sheetFooter.BringToFront(); sheetHeader.BringToFront(); Controls.Add(sheetPage);

        var tabBar = new Panel { Location = new Point(88, 18), Size = new Size(340, 46), BackColor = Color.FromArgb(247, 246, 242) };
        workspaceTabBar = tabBar;
        wordsTab.Text = "单词"; wordsTab.Location = new Point(0, 0); wordsTab.Size = new Size(104, 42); wordsTab.Region = RoundedRegion(wordsTab, 12); wordsTab.TabStop = false; wordsTab.FlatStyle = FlatStyle.Flat; wordsTab.FlatAppearance.BorderSize = 0; wordsTab.Font = new Font(UiFontName(), 11, FontStyle.Bold); wordsTab.TextAlign = ContentAlignment.MiddleCenter;
        organizeTab.Text = "梳理"; organizeTab.Location = new Point(116, 0); organizeTab.Size = new Size(104, 42); organizeTab.Region = RoundedRegion(organizeTab, 12); organizeTab.TabStop = false; organizeTab.FlatStyle = FlatStyle.Flat; organizeTab.FlatAppearance.BorderSize = 0; organizeTab.Font = new Font(UiFontName(), 11, FontStyle.Bold); organizeTab.TextAlign = ContentAlignment.MiddleCenter;
        sheetTab.Text = "表格"; sheetTab.Location = new Point(232, 0); sheetTab.Size = new Size(104, 42); sheetTab.Region = RoundedRegion(sheetTab, 12); sheetTab.TabStop = false; sheetTab.FlatStyle = FlatStyle.Flat; sheetTab.FlatAppearance.BorderSize = 0; sheetTab.Font = new Font(UiFontName(), 11, FontStyle.Bold); sheetTab.TextAlign = ContentAlignment.MiddleCenter;
        var tabTrack = new Panel { Location = new Point(12, 43), Size = new Size(312, 1), BackColor = Color.FromArgb(221, 217, 210) };
        tabIndicator = new Panel { Location = new Point(35, 41), Size = new Size(34, 4), BackColor = activeThemeColor };
        tabIndicator.Region = RoundedRegion(tabIndicator, 2);
        wordsTab.Click += (_, _) => { if (!tabWasDragged) SelectWorkspaceTab(0); };
        organizeTab.Click += (_, _) => { if (!tabWasDragged) SelectWorkspaceTab(1); };
        sheetTab.Click += (_, _) => { if (!tabWasDragged) SelectWorkspaceTab(2); };
        tabBar.Cursor = Cursors.SizeAll;
        foreach (var dragTarget in new Control[] { tabBar, wordsTab, organizeTab, sheetTab })
        {
            dragTarget.MouseDown += (sender, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                tabDragOrigin = Cursor.Position; tabWindowOrigin = Location; tabDragActive = true; tabWasDragged = false;
                if (sender is Control ctrl) ctrl.Capture = true;
            };
            dragTarget.MouseMove += (_, e) =>
            {
                if (!tabDragActive || e.Button != MouseButtons.Left) return;
                var offset = new Size(Cursor.Position.X - tabDragOrigin.X, Cursor.Position.Y - tabDragOrigin.Y);
                if (!tabWasDragged && Math.Abs(offset.Width) + Math.Abs(offset.Height) < 5) return;
                tabWasDragged = true;
                Location = tabWindowOrigin + offset;
            };
            dragTarget.MouseUp += (sender, _) =>
            {
                tabDragActive = false;
                if (sender is Control ctrl) ctrl.Capture = false;
            };
        }
        tabBar.Controls.Add(tabTrack); tabBar.Controls.Add(wordsTab); tabBar.Controls.Add(organizeTab); tabBar.Controls.Add(sheetTab); tabBar.Controls.Add(tabIndicator); tabIndicator.BringToFront(); Controls.Add(tabBar); tabBar.Visible = false;
        Controls.Add(canvasPicker); canvasPicker.BringToFront();
        SelectWorkspaceTab(0);
    }

    void BuildWordSheetGuide()
    {
        wordSheetGuide.Name = "wordSheetGuide";
        wordSheetGuide.Visible = false;
        wordSheetGuideImage.Image = LoadEmbeddedWordSheetGuide();
        wordSheetGuideImage.SizeMode = PictureBoxSizeMode.Zoom;
        wordSheetGuideImage.TabStop = false;
        wordSheetGuideHint.Text = "2号、3号的字会在卡牌点击后出现";
        wordSheetGuideHint.Font = new Font(UiFontName(), 11);
        wordSheetGuideHint.TextAlign = ContentAlignment.MiddleCenter;
        wordSheetGuideHint.ForeColor = Color.FromArgb(100, 94, 88);
        wordSheetGuide.Controls.Add(wordSheetGuideImage);
        wordSheetGuide.Controls.Add(wordSheetGuideHint);
        sheetPage.Controls.Add(wordSheetGuide);
        sheetPage.Resize += (_, _) => LayoutWordSheetGuide();
        LayoutWordSheetGuide();
    }

    void LayoutWordSheetGuide()
    {
        int left = Math.Min(800, Math.Max(580, sheetPage.ClientSize.Width * 54 / 100));
        int width = Math.Max(360, sheetPage.ClientSize.Width - left);
        int height = Math.Max(200, sheetPage.ClientSize.Height - 130);
        wordSheetGuide.Bounds = new Rectangle(left, 62, width, height);
        var image = wordSheetGuideImage.Image;
        int imageTop = 8, maxWidth = Math.Max(1, width - 32), maxHeight = Math.Max(1, height - 82);
        float scale = image == null ? 1f : Math.Min(maxWidth / (float)image.Width, maxHeight / (float)image.Height);
        int imageWidth = image == null ? maxWidth : (int)MathF.Round(image.Width * scale);
        int imageHeight = image == null ? maxHeight : (int)MathF.Round(image.Height * scale);
        wordSheetGuideImage.Bounds = new Rectangle((width - imageWidth) / 2, imageTop, imageWidth, imageHeight);
        wordSheetGuideHint.Bounds = new Rectangle(12, imageTop + imageHeight + 10, width - 24, 38);
    }
    void SelectWorkspaceTab(int selected)
    {
        if (nativeReady) { NativeSelectPage(selected); return; }
        selected = Math.Clamp(selected, 0, 2);
        int previousWorkspaceTab = activeWorkspaceTab;
        settingsSelected = false;
        if (workspaceTabBar != null) workspaceTabBar.Visible = false;
        settingsPage.Visible = false;
        organizeSelected = selected == 1; sheetSelected = selected == 2;
        wordPage.Visible = selected == 0;
        canvasPage.Visible = selected == 1;
        sheetPage.Visible = selected == 2;
        (selected == 0 ? wordPage : selected == 1 ? canvasPage : sheetPage).BringToFront();
        navigationRail.BringToFront();
        navigationCollapseButton.BringToFront();
        Controls.Find("minimizeWindow", false).FirstOrDefault()?.BringToFront();
        Controls.Find("maximizeWindow", false).FirstOrDefault()?.BringToFront();
        Controls.Find("closeButton", false).FirstOrDefault()?.BringToFront();
        canvasPicker.Visible = selected == 1;
        canvasSpaceDown = false;
        canvasPanning = false;
        UpdateCanvasCursor();
        foreach (var (button, index) in new[] { (wordsTab, 0), (organizeTab, 1), (sheetTab, 2) })
        {
            button.BackColor = selected == index ? Color.FromArgb(39, 37, 43) : Color.FromArgb(232, 229, 224);
            button.ForeColor = selected == index ? Color.White : Color.FromArgb(100, 94, 88);
        }
        UpdateNavigationSelection(selected);
        if (tabIndicator != null)
        {
            int targetCenter = 52 + selected * 116;
            if (previousWorkspaceTab < 0 || previousWorkspaceTab == selected)
            {
                tabTransitionTimer.Stop(); tabIndicator.Left = targetCenter - tabIndicator.Width / 2; tabIndicator.Invalidate();
            }
            else
            {
                tabAnimationStartCenter = tabIndicator.Left + tabIndicator.Width / 2;
                tabAnimationTargetCenter = targetCenter; tabAnimationElapsed = 0;
                tabTransitionTimer.Stop(); tabTransitionTimer.Start();
            }
        }
        if (organizeSelected) { canvasSurface.Focus(); canvasPicker.BringToFront(); UpdateCanvasAlignBar(); }
        else canvasAlignBar.Visible = false;
        if (sheetSelected) { UpdateWordSheetUi(); sheetGrid.Focus(); }
    }
    void AnimateWorkspaceNavigation()
    {
        const int duration = 210;
        navigationMotionElapsed = Math.Min(duration, navigationMotionElapsed + navigationMotionTimer.Interval);
        float progress = navigationMotionElapsed / (float)duration;
        float eased = progress < 0.5f ? 4f * progress * progress * progress : 1f - MathF.Pow(-2f * progress + 2f, 3f) / 2f;
        navigationIndicator.Top = (int)MathF.Round(navigationMotionStartTop + (navigationMotionTargetTop - navigationMotionStartTop) * eased);
        if (navigationMotionElapsed >= duration)
        {
            navigationIndicator.Top = navigationMotionTargetTop;
            navigationMotionTimer.Stop();
        }
    }
    void AnimateTabIndicator()
    {
        if (tabIndicator == null) { tabTransitionTimer.Stop(); return; }
        const int duration = 240;
        tabAnimationElapsed = Math.Min(duration, tabAnimationElapsed + tabTransitionTimer.Interval);
        float progress = tabAnimationElapsed / (float)duration;
        float eased = 1f - MathF.Pow(1f - progress, 3f);
        float center = tabAnimationStartCenter + (tabAnimationTargetCenter - tabAnimationStartCenter) * eased;
        int width = 34 + (int)MathF.Round(8f * MathF.Sin(progress * MathF.PI));
        var oldRegion = tabIndicator.Region;
        tabIndicator.Width = width;
        tabIndicator.Left = (int)MathF.Round(center - width / 2f);
        tabIndicator.Region = RoundedRegion(tabIndicator, 2);
        oldRegion?.Dispose();
        tabIndicator.Invalidate();
        if (tabAnimationElapsed >= duration)
        {
            tabIndicator.Width = 34; tabIndicator.Left = tabAnimationTargetCenter - 17;
            tabTransitionTimer.Stop();
        }
    }
    void QueueSheetSave() { if (loadingSheet) return; sheetSaveTimer.Stop(); sheetSaveTimer.Start(); }
    string SheetFile(Guid id) => Path.Combine(sheetsPath, $"{id:D}.json");
    string SheetVisualFile(Guid id) => Path.Combine(sheetsPath, $"{id:D}.visual.json");
    string WordSheetFile => Path.Combine(sheetsPath, "word-sheet.txt");
    void LayoutWordSheetScrollBar()
    {
        if (nativeReady) return;
        if (wordSheetId == Guid.Empty || activeSheetId != wordSheetId) return;
        int top = 62, bottom = 68;
        wordSheetScrollBar.Bounds = new Rectangle(0, top, Math.Max(16, SystemInformation.VerticalScrollBarWidth), Math.Max(1, sheetPage.ClientSize.Height - top - bottom));
        sheetGrid.Dock = DockStyle.None;
        sheetGrid.Bounds = new Rectangle(wordSheetScrollBar.Right, top, Math.Max(1, sheetPage.ClientSize.Width - wordSheetScrollBar.Width), Math.Max(1, sheetPage.ClientSize.Height - top - bottom));
        UpdateWordSheetScrollBarRange();
    }
    void UpdateWordSheetScrollBarRange()
    {
        if (!wordSheetScrollBar.Visible || sheetGrid.RowCount == 0) return;
        int visibleRows = Math.Max(1, sheetGrid.DisplayedRowCount(false));
        int maxFirstRow = Math.Max(0, sheetGrid.RowCount - visibleRows);
        syncingWordSheetScroll = true;
        try
        {
            wordSheetScrollBar.Minimum = 0;
            wordSheetScrollBar.SmallChange = 1;
            wordSheetScrollBar.LargeChange = visibleRows;
            wordSheetScrollBar.Maximum = maxFirstRow + visibleRows - 1;
            int first = sheetGrid.FirstDisplayedScrollingRowIndex;
            wordSheetScrollBar.Value = Math.Clamp(first < 0 ? 0 : first, 0, maxFirstRow);
        }
        catch (ArgumentOutOfRangeException) { }
        finally { syncingWordSheetScroll = false; }
    }
    void SyncWordSheetScrollBarFromGrid() => UpdateWordSheetScrollBarRange();
    void SyncGridFromWordSheetScrollBar()
    {
        if (syncingWordSheetScroll || !wordSheetScrollBar.Visible || sheetGrid.RowCount == 0) return;
        syncingWordSheetScroll = true;
        try
        {
            int first = Math.Clamp(wordSheetScrollBar.Value, 0, Math.Max(0, sheetGrid.RowCount - 1));
            if (sheetGrid.FirstDisplayedScrollingRowIndex != first) sheetGrid.FirstDisplayedScrollingRowIndex = first;
        }
        catch (ArgumentOutOfRangeException) { }
        finally { syncingWordSheetScroll = false; }
    }
    void UpdateWordSheetUi()
    {
        if (nativeReady) { UpdateNativeSheetAppearance(); return; }
        bool active = wordSheetId != Guid.Empty && activeSheetId == wordSheetId;
        wordSheetGuide.Visible = active;
        if (active && sheetSelected) wordSheetGuide.BringToFront();
        LayoutWordSheetGuide();
        wordSheetScrollBar.Visible = active;
        sheetGrid.ScrollBars = active ? ScrollBars.None : ScrollBars.Both;
        if (active) LayoutWordSheetScrollBar(); else sheetGrid.Dock = DockStyle.Fill;
        wordSheetButton.Text = active ? "✓ 表格词库" : "作为表格词库";
        wordSheetButton.BackColor = active ? activeThemeColor : Color.FromArgb(243, 243, 243);
        wordSheetButton.ForeColor = active ? Color.White : Color.FromArgb(31, 31, 31);
        importButton.Text = wordSheetId == Guid.Empty ? "使用EXCEL" : "使用表格词库";
        for (int c = 0; c < sheetGrid.Columns.Count; c++)
        {
            var column = sheetGrid.Columns[c];
            column.HeaderText = active && c < 3 ? new[] { "顶部文字", "中间文字", "底部文字" }[c] : column.Name;
            column.Visible = !active || c < 3;
            column.ReadOnly = active && c >= 3;
            column.DefaultCellStyle.BackColor = active && c >= 3 ? Color.Black : Color.White;
            column.DefaultCellStyle.ForeColor = active && c >= 3 ? Color.Black : Color.FromArgb(39, 37, 43);
            column.DefaultCellStyle.SelectionBackColor = active && c >= 3 ? Color.Black : Color.FromArgb(220, 235, 250);
            column.DefaultCellStyle.SelectionForeColor = active && c >= 3 ? Color.Black : Color.FromArgb(39, 37, 43);
            column.HeaderCell.Style.BackColor = active && c >= 3 ? Color.Black : Color.FromArgb(242, 243, 248);
            column.HeaderCell.Style.ForeColor = active && c >= 3 ? Color.Black : Color.FromArgb(39, 37, 43);
        }
        if (active && sheetGrid.CurrentCell?.ColumnIndex >= 3) sheetGrid.CurrentCell = sheetGrid[0, sheetGrid.CurrentCell.RowIndex];
        if (active) { LayoutWordSheetScrollBar(); wordSheetScrollBar.BringToFront(); }
        sheetGrid.Invalidate();
    }
    void SetWordSheet()
    {
        sheetGrid.EndEdit(); sheetSaveTimer.Stop(); SaveSheet();
        wordSheetId = activeSheetId; wordSheetActive = true;
        File.WriteAllText(WordSheetFile, wordSheetId.ToString("D"), Encoding.UTF8);
        UpdateWordSheetUi(); RefreshCardsFromWordSheet(); ResetOrder(); RenderCard();
        SelectWorkspaceTab(0);
    }
    void ToggleWordSheet()
    {
        wordSheetActive = !wordSheetActive;
        if (wordSheetActive)
        {
            if (wordSheetId == activeSheetId) sheetGrid.EndEdit();
            RefreshCardsFromWordSheet();
        }
        else
        {
            reviewSource.Clear(); reviewSource.AddRange(savedCards);
            cards.Clear(); cards.AddRange(reviewSource);
            current = 0; showingAnswer = false;
        }
        ResetOrder(); RenderCard();
    }
    void RefreshCardsFromWordSheet()
    {
        if (wordSheetId == Guid.Empty || !wordSheetActive) return;
        string[][] rows;
        if (wordSheetId == activeSheetId)
            rows = sheetGrid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).Select(r => r.Cells.Cast<DataGridViewCell>().Take(3).Select(c => c.Value?.ToString() ?? "").ToArray()).ToArray();
        else
        {
            var file = SheetFile(wordSheetId);
            rows = File.Exists(file) ? JsonSerializer.Deserialize<string[][]>(File.ReadAllText(file, Encoding.UTF8)) ?? [] : [];
        }
        reviewSource.Clear();
        foreach (var row in rows)
        {
            if (row.Length < 2) continue;
            var top = row[0]?.Trim() ?? ""; var middle = row[1]?.Trim() ?? ""; var bottom = row.Length > 2 ? row[2]?.Trim() ?? "" : "";
            if (top.Length == 0 && middle.Length == 0 && bottom.Length == 0) continue;
            if (top == "顶部文字" && middle == "中间文字" && bottom == "底部文字") continue;
            reviewSource.Add((top, "表格词库", middle, "自定义", bottom, 0));
        }
        cards.Clear(); cards.AddRange(reviewSource); current = 0; showingAnswer = false;
    }
    void SaveSheetIndex()
    {
        Directory.CreateDirectory(sheetsPath);
        File.WriteAllText(Path.Combine(sheetsPath, "index.json"), JsonSerializer.Serialize(sheets), Encoding.UTF8);
        File.WriteAllText(Path.Combine(sheetsPath, "current.txt"), activeSheetId.ToString("D"), Encoding.UTF8);
        sheetPicker.Text = $"表格：{sheets.First(x => x.Id == activeSheetId).Name}  ▾";
    }
    void LoadSheets()
    {
        Directory.CreateDirectory(sheetsPath);
        var indexFile = Path.Combine(sheetsPath, "index.json");
        if (File.Exists(indexFile))
        {
            try { sheets.AddRange(JsonSerializer.Deserialize<List<SheetInfo>>(File.ReadAllText(indexFile, Encoding.UTF8)) ?? []); }
            catch (Exception ex) { MessageBox.Show($"表格列表读取失败：{ex.Message}", "表格", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        if (sheets.Count == 0)
        {
            activeSheetId = Guid.NewGuid(); sheets.Add(new SheetInfo(activeSheetId, "表格 1"));
            if (File.Exists(sheetDataPath)) File.Copy(sheetDataPath, SheetFile(activeSheetId));
        }
        var currentFile = Path.Combine(sheetsPath, "current.txt");
        if (File.Exists(currentFile) && Guid.TryParse(File.ReadAllText(currentFile, Encoding.UTF8).Trim(), out var savedId) && sheets.Any(x => x.Id == savedId)) activeSheetId = savedId;
        if (activeSheetId == Guid.Empty) activeSheetId = sheets[0].Id;
        if (File.Exists(WordSheetFile) && Guid.TryParse(File.ReadAllText(WordSheetFile, Encoding.UTF8).Trim(), out var savedWordSheetId) && sheets.Any(x => x.Id == savedWordSheetId)) wordSheetId = savedWordSheetId;
        LoadSheet(); SaveSheetIndex(); UpdateWordSheetUi();
    }
    void SaveSheet()
    {
        if (loadingSheet || activeSheetId == Guid.Empty) return;
        var rows = sheetGrid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).Select(r => r.Cells.Cast<DataGridViewCell>().Select(c => c.Value?.ToString() ?? "").ToArray()).ToArray();
        Directory.CreateDirectory(sheetsPath);
        File.WriteAllText(SheetFile(activeSheetId), JsonSerializer.Serialize(rows), Encoding.UTF8);
        File.WriteAllText(SheetVisualFile(activeSheetId), JsonSerializer.Serialize(new SheetVisual(sheetZoom, sheetCells)), Encoding.UTF8);
        if (activeSheetId == wordSheetId) { RefreshCardsFromWordSheet(); ResetOrder(); RenderCard(); }
    }
    void LoadSheet()
    {
        try
        {
            loadingSheet = true;
            sheetGrid.Rows.Clear(); sheetGrid.Rows.Add(100);
            sheetCells.Clear(); sheetZoom = 1f;
            var visualFile = SheetVisualFile(activeSheetId);
            if (File.Exists(visualFile))
            {
                var visual = JsonSerializer.Deserialize<SheetVisual>(File.ReadAllText(visualFile, Encoding.UTF8));
                if (visual != null)
                {
                    sheetZoom = Math.Clamp(visual.Zoom, .5f, 2.5f);
                    foreach (var (key, value) in visual.Cells) sheetCells[key] = value;
                }
            }
            ApplySheetZoom();
            var file = SheetFile(activeSheetId);
            if (!File.Exists(file)) return;
            var rows = JsonSerializer.Deserialize<string[][]>(File.ReadAllText(file, Encoding.UTF8));
            if (rows == null) return;
            while (sheetGrid.Rows.Count - 1 < rows.Length) sheetGrid.Rows.Add();
            for (int r = 0; r < rows.Length; r++)
                for (int c = 0; c < Math.Min(rows[r].Length, sheetGrid.Columns.Count); c++)
                    sheetGrid[c, r].Value = rows[r][c];
        }
        catch (Exception ex) { MessageBox.Show($"表格读取失败：{ex.Message}", "表格", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { loadingSheet = false; }
    }
    void SwitchSheet(Guid id)
    {
        if (id == activeSheetId) return;
        sheetGrid.EndEdit(); sheetSaveTimer.Stop(); SaveSheet();
        activeSheetId = id; LoadSheet(); SaveSheetIndex(); UpdateWordSheetUi(); sheetGrid.Focus();
    }
    void CreateSheet()
    {
        sheetGrid.EndEdit(); sheetSaveTimer.Stop(); SaveSheet();
        var number = 1;
        while (sheets.Any(x => x.Name == $"表格 {number}")) number++;
        activeSheetId = Guid.NewGuid(); sheets.Add(new SheetInfo(activeSheetId, $"表格 {number}"));
        LoadSheet(); SaveSheetIndex(); UpdateWordSheetUi(); sheetGrid.Focus();
    }
    void ImportExcelSheet()
    {
        using var dialog = new OpenFileDialog { Filter = "Excel 文件 (*.xlsx;*.csv;*.tsv)|*.xlsx;*.csv;*.tsv", Title = "导入为新表格", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Guid previousId = activeSheetId, createdId = Guid.Empty;
        try
        {
            var extension = Path.GetExtension(dialog.FileName);
            var importedRows = extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                ? ReadExcelSheet(dialog.FileName)
                : ReadDelimitedSheet(dialog.FileName, extension.Equals(".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : ',');
            if (importedRows.Count == 0 || !importedRows.Any(row => row.Any(value => !string.IsNullOrEmpty(value))))
            { MessageBox.Show(this, "文件里没有可导入的单元格。", "导入 Excel", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

            sheetGrid.EndEdit(); sheetSaveTimer.Stop(); SaveSheet();
            var baseName = Path.GetFileNameWithoutExtension(dialog.FileName).Trim();
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "导入表格";
            var name = baseName; var suffix = 2;
            while (sheets.Any(sheet => sheet.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) name = $"{baseName} ({suffix++})";
            createdId = Guid.NewGuid(); activeSheetId = createdId; sheets.Add(new SheetInfo(createdId, name));
            LoadSheet();

            var columnCount = importedRows.Max(row => row.Length);
            while (sheetGrid.Columns.Count < columnCount)
            {
                var index = sheetGrid.Columns.Count;
                var columnName = ExcelColumnName(index);
                sheetGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = columnName, HeaderText = columnName, Width = (int)(125 * sheetZoom), SortMode = DataGridViewColumnSortMode.NotSortable });
            }
            while (sheetGrid.Rows.Count - 1 < importedRows.Count) sheetGrid.Rows.Add();
            loadingSheet = true;
            try
            {
                for (int r = 0; r < importedRows.Count; r++)
                    for (int c = 0; c < importedRows[r].Length; c++)
                        if (c < sheetGrid.Columns.Count) sheetGrid[c, r].Value = importedRows[r][c];
            }
            finally { loadingSheet = false; }
            ApplySheetZoom(); SaveSheet(); SaveSheetIndex(); UpdateWordSheetUi();
            if (sheetGrid.Columns.Count > 0 && sheetGrid.Rows.Count > 1) sheetGrid.CurrentCell = sheetGrid[0, 0];
            sheetGrid.Focus();
            MessageBox.Show(this, $"已将 {importedRows.Count:N0} 行导入为新表格“{name}”。", "导入完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            if (createdId != Guid.Empty)
            {
                sheets.RemoveAll(sheet => sheet.Id == createdId);
                foreach (var file in new[] { SheetFile(createdId), SheetVisualFile(createdId) }) if (File.Exists(file)) File.Delete(file);
                activeSheetId = previousId; LoadSheet(); SaveSheetIndex(); UpdateWordSheetUi();
            }
            MessageBox.Show(this, $"导入失败：{ex.Message}", "导入 Excel", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    static string ExcelColumnName(int zeroBased)
    {
        var name = "";
        for (var n = zeroBased + 1; n > 0; n = (n - 1) / 26) name = (char)('A' + (n - 1) % 26) + name;
        return name;
    }
    static List<string[]> ReadDelimitedSheet(string path, char delimiter)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        var rows = new List<string[]>(); var row = new List<string>(); var value = new StringBuilder(); bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { value.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else value.Append(ch);
            }
            else if (ch == '"' && value.Length == 0) quoted = true;
            else if (ch == delimiter) { row.Add(value.ToString()); value.Clear(); }
            else if (ch is '\r' or '\n')
            {
                row.Add(value.ToString()); value.Clear(); rows.Add(row.ToArray()); row.Clear();
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                if (rows.Count > 10_000) throw new InvalidDataException("单次最多导入 10,000 行。");
            }
            else value.Append(ch);
        }
        if (quoted) throw new InvalidDataException("CSV 文件中的引号没有成对出现。");
        if (value.Length > 0 || row.Count > 0) { row.Add(value.ToString()); rows.Add(row.ToArray()); }
        if (rows.Count > 0 && rows.Max(r => r.Length) > 100) throw new InvalidDataException("单次最多导入 100 列。");
        if (rows.Count > 0 && (long)rows.Count * rows.Max(r => r.Length) > 350_000) throw new InvalidDataException("表格内容太大，请拆分后再导入（最多 350,000 个单元格）。");
        return rows;
    }
    static List<string[]> ReadExcelSheet(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var shared = new List<string>();
        var sharedEntry = zip.GetEntry("xl/sharedStrings.xml");
        if (sharedEntry != null)
        {
            using var reader = new StreamReader(sharedEntry.Open()); var xml = XDocument.Load(reader);
            shared = xml.Descendants().Where(x => x.Name.LocalName == "si").Select(si => string.Concat(si.Descendants().Where(t => t.Name.LocalName == "t").Select(t => t.Value))).ToList();
        }
        var sheetPath = "xl/worksheets/sheet1.xml";
        var workbookEntry = zip.GetEntry("xl/workbook.xml"); var relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry != null && relsEntry != null)
        {
            using var wbReader = new StreamReader(workbookEntry.Open()); var workbook = XDocument.Load(wbReader);
            using var relReader = new StreamReader(relsEntry.Open()); var rels = XDocument.Load(relReader);
            var firstSheet = workbook.Descendants().FirstOrDefault(x => x.Name.LocalName == "sheet");
            var relationId = firstSheet?.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value;
            var target = rels.Descendants().FirstOrDefault(x => x.Name.LocalName == "Relationship" && (string?)x.Attribute("Id") == relationId)?.Attribute("Target")?.Value;
            if (!string.IsNullOrWhiteSpace(target))
            {
                target = target.Replace('\\', '/').TrimStart('/');
                if (target.StartsWith("../", StringComparison.Ordinal)) target = target[3..];
                sheetPath = target.StartsWith("xl/", StringComparison.Ordinal) ? target : "xl/" + target;
            }
        }
        var sheetEntry = zip.GetEntry(sheetPath) ?? zip.GetEntry("xl/worksheets/sheet1.xml") ?? throw new InvalidDataException("找不到 Excel 工作表。");
        using var sheetReader = new StreamReader(sheetEntry.Open()); var document = XDocument.Load(sheetReader);
        var values = new SortedDictionary<int, SortedDictionary<int, string>>(); var maxRow = 0; var maxColumn = 0;
        foreach (var rowElement in document.Descendants().Where(x => x.Name.LocalName == "row"))
        {
            int.TryParse((string?)rowElement.Attribute("r"), out var rowNumber);
            foreach (var cell in rowElement.Elements().Where(x => x.Name.LocalName == "c"))
            {
                var reference = (string?)cell.Attribute("r") ?? ""; var column = 0; var rowFromRef = 0;
                foreach (var ch in reference)
                {
                    if (ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z') column = column * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
                    else if (char.IsDigit(ch)) rowFromRef = rowFromRef * 10 + (ch - '0');
                }
                if (column == 0) continue;
                var actualRow = Math.Max(rowNumber, rowFromRef);
                if (actualRow < 1 || actualRow > 10_000 || column > 100) throw new InvalidDataException("单次最多导入 10,000 行、100 列。");
                var type = (string?)cell.Attribute("t");
                string value;
                if (type == "inlineStr") value = string.Concat(cell.Descendants().Where(x => x.Name.LocalName == "t").Select(x => x.Value));
                else
                {
                    value = cell.Elements().FirstOrDefault(x => x.Name.LocalName == "v")?.Value ?? "";
                    if (type == "s" && int.TryParse(value, out var sharedIndex) && sharedIndex >= 0 && sharedIndex < shared.Count) value = shared[sharedIndex];
                    else if (type == "b") value = value == "1" ? "TRUE" : "FALSE";
                }
                if (!values.TryGetValue(actualRow - 1, out var cells)) values[actualRow - 1] = cells = new SortedDictionary<int, string>();
                cells[column - 1] = value; maxRow = Math.Max(maxRow, actualRow); maxColumn = Math.Max(maxColumn, column);
            }
        }
        if ((long)maxRow * maxColumn > 350_000) throw new InvalidDataException("表格内容太大，请拆分后再导入（最多 350,000 个单元格）。");
        var rows = Enumerable.Range(0, maxRow).Select(_ => new string[maxColumn]).ToArray();
        foreach (var (rowIndex, cells) in values) foreach (var (columnIndex, value) in cells) rows[rowIndex][columnIndex] = value;
        return rows.Select(row => row.Select(value => value ?? "").ToArray()).ToList();
    }
    void DeleteSheet(Guid id)
    {
        var target = sheets.FirstOrDefault(x => x.Id == id);
        if (target == null) return;
        if (MessageBox.Show($"确定删除“{target.Name}”及其中的全部内容吗？", "删除表格", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        sheetGrid.EndEdit(); sheetSaveTimer.Stop();
        if (id != activeSheetId) SaveSheet();
        sheets.Remove(target);
        if (id == wordSheetId) { wordSheetId = Guid.Empty; wordSheetActive = true; if (File.Exists(WordSheetFile)) File.Delete(WordSheetFile); importButton.Text = "使用EXCEL"; reviewSource.Clear(); reviewSource.AddRange(savedCards); cards.Clear(); cards.AddRange(reviewSource); ResetOrder(); RenderCard(); }
        if (sheets.Count == 0) sheets.Add(new SheetInfo(Guid.NewGuid(), "表格 1"));
        if (id == activeSheetId) { activeSheetId = sheets[0].Id; LoadSheet(); }
        var file = SheetFile(id); if (File.Exists(file)) File.Delete(file);
        var visualFile = SheetVisualFile(id); if (File.Exists(visualFile)) File.Delete(visualFile);
        SaveSheetIndex(); UpdateWordSheetUi();
    }
    void ShowSheetMenu()
    {
        var menu = new ContextMenuStrip();
        foreach (var sheet in sheets)
        {
            var item = new ToolStripMenuItem($"{(sheet.Id == activeSheetId ? "✓  " : "     ")}{sheet.Name}  （右键删除）");
            item.Click += (_, _) => SwitchSheet(sheet.Id);
            item.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) { menu.Close(); DeleteSheet(sheet.Id); } };
            menu.Items.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("＋ 新建表格", null, (_, _) => CreateSheet());
        menu.Show(sheetPicker, new Point(0, -menu.GetPreferredSize(Size.Empty).Height));
    }
    void SheetGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.V && !sheetGrid.IsCurrentCellInEditMode)
        {
            var start = sheetGrid.CurrentCell; if (start == null || !Clipboard.ContainsText()) return;
            var lines = Clipboard.GetText().TrimEnd('\r', '\n').Split('\n');
            loadingSheet = true;
            try
            {
                for (int r = 0; r < lines.Length; r++)
                {
                    var row = start.RowIndex + r;
                    while (row >= sheetGrid.Rows.Count - 1) sheetGrid.Rows.Add();
                    var cells = lines[r].TrimEnd('\r').Split('\t');
                    for (int c = 0; c < cells.Length && start.ColumnIndex + c < sheetGrid.Columns.Count; c++)
                        if (!sheetGrid.Columns[start.ColumnIndex + c].ReadOnly) sheetGrid[start.ColumnIndex + c, row].Value = cells[c];
                }
            }
            finally { loadingSheet = false; }
            QueueSheetSave(); e.Handled = true; e.SuppressKeyPress = true;
        }
    }
    void ApplySheetShortcut(int operation)
    {
        foreach (DataGridViewCell cell in sheetGrid.SelectedCells)
        {
            if (cell.OwningColumn.ReadOnly) continue;
            var key = $"{cell.RowIndex}:{cell.ColumnIndex}";
            var old = sheetCells.GetValueOrDefault(key) ?? new CellVisual();
            var updated = operation switch
            {
                1 => old with { Hidden = !old.Hidden },
                2 => old with { Background = old.Background == 2 ? 0 : 2 },
                3 => old with { Background = old.Background == 3 ? 0 : 3 },
                _ => old with { Bold = !old.Bold }
            };
            if (updated == new CellVisual()) sheetCells.Remove(key); else sheetCells[key] = updated;
        }
        sheetGrid.Invalidate(); QueueSheetSave();
    }
    void SheetGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (!sheetCells.TryGetValue($"{e.RowIndex}:{e.ColumnIndex}", out var visual)) return;
        var style = e.CellStyle;
        if (style == null) return;
        if (visual.Background != 0)
        {
            var color = visual.Background == 2 ? Color.FromArgb(204, 237, 207) : Color.FromArgb(249, 207, 203);
            style.BackColor = color; style.SelectionBackColor = color;
        }
        if (visual.Bold && sheetBoldFont != null) style.Font = sheetBoldFont;
        if (visual.Hidden && e.Value != null && !string.IsNullOrEmpty(e.Value.ToString()))
        { e.Value = "●●●●●●"; e.FormattingApplied = true; }
    }
    void SheetGridMouseWheel(object? sender, MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Control) == Keys.Control)
        {
            var next = Math.Clamp(sheetZoom + (e.Delta > 0 ? .1f : -.1f), .5f, 2.5f);
            if (Math.Abs(next - sheetZoom) > .001f) { sheetZoom = next; ApplySheetZoom(); QueueSheetSave(); }
            if (e is HandledMouseEventArgs zoomHandled) zoomHandled.Handled = true;
            return;
        }
        if (!wordSheetScrollBar.Visible || e.Delta == 0) return;
        int lines = SystemInformation.MouseWheelScrollLines;
        if (lines < 0) lines = Math.Max(1, sheetGrid.DisplayedRowCount(false));
        lines = Math.Max(1, lines) * Math.Max(1, Math.Abs(e.Delta) / 120);
        int maxValue = Math.Max(wordSheetScrollBar.Minimum, wordSheetScrollBar.Maximum - wordSheetScrollBar.LargeChange + 1);
        int nextValue = Math.Clamp(wordSheetScrollBar.Value - Math.Sign(e.Delta) * lines, wordSheetScrollBar.Minimum, maxValue);
        if (nextValue != wordSheetScrollBar.Value) wordSheetScrollBar.Value = nextValue;
        if (e is HandledMouseEventArgs scrollHandled) scrollHandled.Handled = true;
    }
    void ApplySheetZoom()
    {
        sheetBoldFont?.Dispose();
        sheetGrid.Font = new Font(UiFontName(), 10f * sheetZoom);
        sheetBoldFont = new Font(sheetGrid.Font, FontStyle.Bold);
        sheetGrid.ColumnHeadersDefaultCellStyle.Font = sheetGrid.Font;
        sheetGrid.RowHeadersDefaultCellStyle.Font = sheetGrid.Font;
        sheetGrid.ColumnHeadersHeight = (int)(34 * sheetZoom);
        sheetGrid.RowHeadersWidth = (int)(64 * sheetZoom);
        sheetGrid.RowTemplate.Height = (int)(28 * sheetZoom);
        foreach (DataGridViewColumn column in sheetGrid.Columns) column.Width = (int)(125 * sheetZoom);
        foreach (DataGridViewRow row in sheetGrid.Rows) row.Height = (int)(28 * sheetZoom);
        sheetGrid.Invalidate();
    }
    void UpdateCanvasCursor()
    {
        canvasSurface.Cursor = canvasWiring ? Cursors.Cross : canvasPanning ? Cursors.SizeAll : canvasCutting || (organizeSelected && (ModifierKeys & Keys.Alt) == Keys.Alt) ? knifeCursor : canvasNodeResizing ? Cursors.SizeNWSE : canvasNodeRotating ? rotateCursor : canvasVectorEditNodeId.HasValue ? Cursors.Cross : canvasSpaceDown || canvasTool==CanvasTool.Hand ? Cursors.Hand : canvasTool!=CanvasTool.Select ? Cursors.Cross : Cursors.Default;
    }
    void InvalidateCanvasPaintCache() => canvasPaintCacheDirty = true;
    void EnsureCanvasPaintCache()
    {
        if (!canvasPaintCacheDirty && canvasPaintNodes != null && canvasPaintLinks != null && canvasPaintNodeMap != null) return;
        var viewport = canvasSurface.ClientRectangle;
        var byId = new Dictionary<Guid, CanvasNode>(canvasNodes.Count);
        foreach (var node in canvasNodes) byId[node.Id] = node;
        var visibleNodes = new List<(CanvasNode Node, int Index)>();
        for (int i = 0; i < canvasNodes.Count; i++)
        {
            var node = canvasNodes[i];
            if (!node.Hidden && CanvasVisualBounds(node).IntersectsWith(viewport)) visibleNodes.Add((node, i));
        }
        var visibleLinks = new List<CanvasLink>();
        foreach (var link in canvasLinks)
            if (byId.TryGetValue(link.From, out var from) && byId.TryGetValue(link.To, out var to) && CanvasLinkBounds(link, from, to).IntersectsWith(viewport)) visibleLinks.Add(link);
        canvasPaintNodes = visibleNodes; canvasPaintLinks = visibleLinks; canvasPaintNodeMap = byId; canvasPaintCacheDirty = false;
    }
    void ToggleCanvasLayers()
    {
        canvasLayersPanel.Visible = !canvasLayersPanel.Visible;
        canvasLayersToggle.Text = canvasLayersPanel.Visible ? "图层  ▾" : "图层  ▴";
        if (canvasLayersPanel.Visible) RefreshCanvasLayers();
        UpdateCanvasProps();
        canvasLayersToggle.BringToFront();
    }
    void LayoutCanvasSidebars()
    {
        if (nativeReady) return;
        int right = canvasPage.Width;
        if (canvasLayersPanel.Visible)
        {
            canvasLayersPanel.Location = new Point(right - canvasLayersPanel.Width, 0);
            right -= canvasLayersPanel.Width;
        }
        if (canvasPropsPanel.Visible)
        {
            canvasPropsPanel.Location = new Point(right - canvasPropsPanel.Width, 0);
            right -= canvasPropsPanel.Width;
        }
        canvasLayersToggle.Location = new Point(Math.Max(16, right - canvasLayersToggle.Width - 24), canvasPage.Height - 48);
    }
    static readonly Color[] canvasGroupPalette = new[]
    {
        Color.FromArgb(232, 110, 90), Color.FromArgb(245, 176, 65), Color.FromArgb(142, 180, 60), Color.FromArgb(70, 170, 130), Color.FromArgb(64, 170, 200),
        Color.FromArgb(90, 140, 230), Color.FromArgb(150, 110, 220), Color.FromArgb(220, 110, 170), Color.FromArgb(190, 150, 80), Color.FromArgb(100, 160, 90)
    };
    // 剪贴蒙版配对标识色（偏紫蓝，与成组的左侧色条区分）
    static readonly Color[] canvasClipPairPalette = new[]
    {
        Color.FromArgb(110, 105, 220), Color.FromArgb(60, 150, 210), Color.FromArgb(170, 110, 200), Color.FromArgb(50, 170, 160), Color.FromArgb(210, 130, 110),
        Color.FromArgb(120, 130, 220), Color.FromArgb(150, 100, 180), Color.FromArgb(70, 160, 130)
    };
    static Color CanvasGroupColor(Guid groupId)
    {
        var bytes = groupId.ToByteArray();
        uint hash = BitConverter.ToUInt32(bytes, 0) ^ BitConverter.ToUInt32(bytes, 4) ^ BitConverter.ToUInt32(bytes, 8) ^ BitConverter.ToUInt32(bytes, 12);
        return canvasGroupPalette[hash % (uint)canvasGroupPalette.Length];
    }
    void RefreshCanvasLayers()
    {
        if (!canvasLayersPanel.Visible) return;
        layerDragIndex = -1; layerDragging = false;
        // 剪贴蒙版配对：ClipMask 图层与它正下方的图层同属一组（连续剪贴形成链），每组分配同一颜色
        var clipColorById = new Dictionary<Guid, Color>();
        int[] clipComponent = new int[canvasNodes.Count];
        Array.Fill(clipComponent, -1);
        for (int i = 1; i < canvasNodes.Count; i++)
        {
            if (!canvasNodes[i].ClipMask) continue;
            int component = clipComponent[i - 1] >= 0 ? clipComponent[i - 1] : i - 1;
            clipComponent[i - 1] = component;
            clipComponent[i] = component;
        }
        var clipComponentColors = new Dictionary<int, Color>();
        int clipColorIndex = 0;
        for (int i = 0; i < canvasNodes.Count; i++)
        {
            if (clipComponent[i] < 0) continue;
            if (!clipComponentColors.TryGetValue(clipComponent[i], out var pairColor))
            {
                pairColor = canvasClipPairPalette[clipColorIndex % canvasClipPairPalette.Length];
                clipComponentColors[clipComponent[i]] = pairColor;
                clipColorIndex++;
            }
            clipColorById[canvasNodes[i].Id] = pairColor;
        }
        int pageCount = Math.Max(1, (canvasNodes.Count + CanvasLayerPageSize - 1) / CanvasLayerPageSize);
        canvasLayerPage = Math.Clamp(canvasLayerPage, 0, pageCount - 1);
        canvasLayerPageLabel.Text = canvasNodes.Count == 0 ? "无图层" : $"{canvasLayerPage * CanvasLayerPageSize + 1}–{Math.Min((canvasLayerPage + 1) * CanvasLayerPageSize, canvasNodes.Count)} / {canvasNodes.Count}";
        canvasLayerPrevious.Enabled = canvasLayerPage > 0;
        canvasLayerNext.Enabled = canvasLayerPage + 1 < pageCount;
        canvasLayersList.SuspendLayout();
        // Controls.Clear() 只会摘除控件，不会释放其原生窗口和独立 Font；反复点图层会累积数千个 GDI 资源。
        foreach (Control row in canvasLayersList.Controls.Cast<Control>().ToArray())
        {
            foreach (Control child in row.Controls)
            {
                if (child is System.Windows.Forms.Button || child is System.Windows.Forms.Label) child.Font.Dispose();
            }
            row.Dispose();
        }
        canvasLayersList.Controls.Clear();
        int firstDisplayIndex = canvasLayerPage * CanvasLayerPageSize;
        int lastDisplayIndex = Math.Min(firstDisplayIndex + CanvasLayerPageSize, canvasNodes.Count);
        for (int displayIndex = firstDisplayIndex; displayIndex < lastDisplayIndex; displayIndex++)
        {
            int i = canvasNodes.Count - 1 - displayIndex;
            var node = canvasNodes[i];
            bool layerIsClipped = node.ClipMask;
            string preview = !string.IsNullOrWhiteSpace(node.Name) ? node.Name : string.IsNullOrWhiteSpace(node.Content) ? CanvasShapeDisplayName(node.Kind) : node.Content.Replace("\r", " ").Replace("\n", " ");
            int previewLimit = layerIsClipped ? 17 : 22;
            if (preview.Length > previewLimit) preview = preview[..previewLimit] + "…";
            var glyph = node.Kind switch { "text" => "▤", "image" => "▧", "circle" => "○", "triangle" => "△", "diamond" => "◇", "star" => "☆", _ => "□" };
            var layerButton = Button($"{(layerIsClipped ? "↳ " : "")}{glyph}  {preview}", Color.White, Color.FromArgb(70, 67, 65));
            layerButton.Size = new Size(176, 40); layerButton.Margin = new Padding(0, 0, 0, 6); layerButton.TextAlign = ContentAlignment.MiddleLeft; layerButton.Padding = new Padding(10, 0, 4, 0); layerButton.Font = new Font(UiFontName(), 9, FontStyle.Regular);
            if (canvasSelectedIds.Contains(node.Id)) layerButton.BackColor = Color.FromArgb(244, 231, 223);
            if (node.Hidden) layerButton.ForeColor = Color.FromArgb(170, 165, 158);
            Color? groupPaintColor = node.GroupId is Guid layerGroupId ? CanvasGroupColor(layerGroupId) : null;
            Color? clipPaintColor = clipColorById.TryGetValue(node.Id, out var pairPaintColor) ? pairPaintColor : null;
            if (groupPaintColor.HasValue || clipPaintColor.HasValue)
            {
                layerButton.Paint += (_, paintArgs) =>
                {
                    if (groupPaintColor is Color gc) { using var groupBrush = new SolidBrush(gc); paintArgs.Graphics.FillRectangle(groupBrush, 0, 0, 6, layerButton.Height); }
                    // 剪贴蒙版配对图层：右侧同色色条
                    if (clipPaintColor is Color cc) { using var clipBrush = new SolidBrush(cc); paintArgs.Graphics.FillRectangle(clipBrush, layerButton.Width - 5, 0, 5, layerButton.Height); }
                };
            }
            var rowPanel = new Panel { Width = 246, Height = 46, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 0) };
            layerButton.Tag = displayIndex;
            int rowIndex = displayIndex - firstDisplayIndex;
            layerButton.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { layerDragIndex = rowIndex; layerDragStart = e.Location; layerDragging = false; } };
            layerButton.MouseMove += (_, e) =>
            {
                if (layerDragIndex < 0 || e.Button != MouseButtons.Left) return;
                if (!layerDragging && (Math.Abs(e.X - layerDragStart.X) > 4 || Math.Abs(e.Y - layerDragStart.Y) > 4)) { layerDragging = true; layerButton.Capture = true; }
                if (!layerDragging) return;
                // 列表可能在拖动期间被重建，旧行已不属于列表时直接终止拖动
                if (!canvasLayersList.Controls.Contains(rowPanel)) { layerDragIndex = -1; layerDragging = false; return; }
                var screenPoint = layerButton.PointToScreen(e.Location);
                var listPoint = canvasLayersList.PointToClient(screenPoint);
                int targetIndex = CanvasLayerRowIndex(listPoint);
                if (targetIndex != layerDragIndex)
                {
                    layerButton.BackColor = Color.FromArgb(255, 240, 220);
                    canvasLayersList.Controls.SetChildIndex(rowPanel, targetIndex);
                    layerDragIndex = targetIndex;
                }
            };
            layerButton.MouseUp += (_, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    var layerMenu = new ContextMenuStrip();
                    layerMenu.Items.Add("置顶", null, (_, _) => MoveLayerToTop(node));
                    layerMenu.Items.Add("置底", null, (_, _) => MoveLayerToBottom(node));
                    layerMenu.Items.Add(new ToolStripSeparator());
                    layerMenu.Items.Add(node.ClipMask ? "取消剪贴蒙版" : "创建剪贴蒙版（裁剪到下图层）", null, (_, _) => ToggleLayerClipMask(node));
                    layerMenu.Items.Add(new ToolStripSeparator());
                    layerMenu.Items.Add("删除", null, (_, _) => DeleteLayerNode(node));
                    layerMenu.Show(layerButton, e.Location);
                    return;
                }
                if (e.Button != MouseButtons.Left || layerDragIndex < 0) return;
                if (layerDragging)
                {
                    int oldNodeIndex = canvasNodes.Count - 1 - ((int)layerButton.Tag!);
                    int newNodeIndex = canvasNodes.Count - 1 - (canvasLayerPage * CanvasLayerPageSize + layerDragIndex);
                    if (oldNodeIndex != newNodeIndex && oldNodeIndex >= 0 && oldNodeIndex < canvasNodes.Count && newNodeIndex >= 0 && newNodeIndex < canvasNodes.Count)
                    {
                        PushCanvasUndo();
                        var item = canvasNodes[oldNodeIndex];
                        canvasNodes.RemoveAt(oldNodeIndex);
                        canvasNodes.Insert(newNodeIndex, item);
                        canvasSurface.Invalidate();
                        SaveCanvas();
                    }
                    layerButton.Capture = false;
                    RefreshCanvasLayers();
                }
                else { SetCanvasSelection(CanvasGroupMembers(node)); canvasSurface.Invalidate(); RefreshCanvasLayers(); }
                layerDragIndex = -1; layerDragging = false;
            };

            rowPanel.Controls.Add(layerButton);

            var eyeButton = new Button { Text = node.Hidden ? "○" : "●", Size = new Size(28, 28), Location = new Point(182, 9), FlatStyle = FlatStyle.Flat, BackColor = Color.Transparent, ForeColor = node.Hidden ? Color.FromArgb(170, 165, 158) : Color.FromArgb(70, 67, 65), Font = new Font(UiFontName(), 10, FontStyle.Regular), TabStop = false, Cursor = Cursors.Hand };
            eyeButton.FlatAppearance.BorderSize = 0;
            eyeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 229, 224);
            eyeButton.Click += (_, _) => { PushCanvasUndo(); node.Hidden = !node.Hidden; canvasSurface.Invalidate(); RefreshCanvasLayers(); SaveCanvas(); };
            rowPanel.Controls.Add(eyeButton);

            var lockButton = new Button { Text = node.Locked ? "🔒" : "🔓", Size = new Size(28, 28), Location = new Point(210, 9), FlatStyle = FlatStyle.Flat, BackColor = Color.Transparent, ForeColor = node.Locked ? Color.FromArgb(190, 105, 80) : Color.FromArgb(145, 140, 132), Font = new Font(UiFontName(), 9, FontStyle.Regular), TabStop = false, Cursor = Cursors.Hand };
            lockButton.FlatAppearance.BorderSize = 0;
            lockButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 229, 224);
            lockButton.Click += (_, _) => { PushCanvasUndo(); node.Locked = !node.Locked; canvasSurface.Invalidate(); RefreshCanvasLayers(); SaveCanvas(); };
            rowPanel.Controls.Add(lockButton);

            canvasLayersList.Controls.Add(rowPanel);
        }
        canvasLayersList.ResumeLayout();
    }
    // 按图层行的实际高度计算拖动落点，避免滚动和不同 DPI 下拖动顺序错位
    int CanvasLayerRowIndex(Point listPoint)
    {
        int y = listPoint.Y;
        for (int i = 0; i < canvasLayersList.Controls.Count; i++)
        {
            int rowHeight = canvasLayersList.Controls[i].Height + canvasLayersList.Controls[i].Margin.Top + canvasLayersList.Controls[i].Margin.Bottom;
            if (y < rowHeight) return i;
            y -= rowHeight;
        }
        return Math.Max(0, canvasLayersList.Controls.Count - 1);
    }
    void MoveLayerToTop(CanvasNode node)
    {
        PushCanvasUndo();
        canvasNodes.Remove(node);
        canvasNodes.Add(node);
        SetCanvasSelection(CanvasGroupMembers(node));
        canvasSurface.Invalidate();
        RefreshCanvasLayers();
        SaveCanvas();
    }
    void MoveLayerToBottom(CanvasNode node)
    {
        PushCanvasUndo();
        canvasNodes.Remove(node);
        canvasNodes.Insert(0, node);
        SetCanvasSelection(CanvasGroupMembers(node));
        canvasSurface.Invalidate();
        RefreshCanvasLayers();
        SaveCanvas();
    }
    void ToggleLayerClipMask(CanvasNode node)
    {
        if(node.Locked || (!node.ClipMask && canvasNodes.IndexOf(node)<=0))return;
        PushCanvasUndo();
        node.ClipMask = !node.ClipMask;
        SetCanvasSelection(CanvasGroupMembers(node));
        canvasSurface.Invalidate();
        RefreshCanvasLayers();
        SaveCanvas();
    }
    void DeleteLayerNode(CanvasNode node)
    {
        PushCanvasUndo();
        canvasNodes.Remove(node);
        canvasLinks.RemoveAll(link => link.From == node.Id || link.To == node.Id);
        InvalidateCanvasPaintCache();
        if (selectedCanvasLink != null && (selectedCanvasLink.From == node.Id || selectedCanvasLink.To == node.Id)) selectedCanvasLink = null;
        canvasSelectedIds.Remove(node.Id);
        canvasSelectionStartPositions.Remove(node.Id);
        if (canvasVectorEditNodeId == node.Id) canvasVectorEditNodeId = null;
        selectedCanvasNode = canvasNodes.LastOrDefault(item => canvasSelectedIds.Contains(item.Id));
        canvasSurface.Invalidate();
        RefreshCanvasLayers();
        UpdateCanvasAlignBar();
        UpdateCanvasProps();
        SaveCanvas();
    }
    static CanvasNode CloneCanvasNodeTemplate(CanvasNode n) => new()
    {
        Id = n.Id, Kind = n.Kind, Content = n.Content, Position = n.Position, Size = n.Size,
        Transparent = n.Transparent, Bitmap = n.Bitmap, GroupId = n.GroupId,
        VectorAnchors = n.VectorAnchors.ToList(), Rotation = n.Rotation, Locked = n.Locked, Hidden = n.Hidden, Name = n.Name,
        FontSize = n.FontSize, FontBold = n.FontBold, FontItalic = n.FontItalic, FontStrikeout = n.FontStrikeout,
        FontAlign = n.FontAlign, FontVAlign = n.FontVAlign, FontColor = n.FontColor,
        FillColor = n.FillColor, CornerRadius = n.CornerRadius, Opacity = n.Opacity, FillOpacity = n.FillOpacity, ClipMask = n.ClipMask,
        StrokeWidth = n.StrokeWidth, StrokeColor = n.StrokeColor, StrokeAlign = n.StrokeAlign, StrokeDash = n.StrokeDash,
        AutoLayout = n.AutoLayout, AutoLayoutDirection = n.AutoLayoutDirection, AutoLayoutGap = n.AutoLayoutGap, AutoLayoutPadding = n.AutoLayoutPadding,
        ComponentId = n.ComponentId, ComponentInstanceId = n.ComponentInstanceId, ComponentMasterNodeId = n.ComponentMasterNodeId
        , Extra = CopyLayerData(n.Extra)
    };
    // 把当前选中打包成剪贴板模板；只复制了组的一部分时，模板解除该组归属
    (List<CanvasNode> Nodes, List<(Guid From, Guid To, string Label, string Color, int Arrow)> Links) BuildCanvasClipboardFromSelection()
    {
        var sources = CanvasSelectedNodes();
        var sourceIds = sources.Select(node => node.Id).ToHashSet();
        var templates = sources.Select(CloneCanvasNodeTemplate).ToList();
        foreach (var template in templates)
        {
            // 普通复制是独立副本，不应意外成为原组件的又一个主组件/实例。
            template.ComponentId = null; template.ComponentInstanceId = null; template.ComponentMasterNodeId = null;
            if (template.GroupId is Guid groupId && canvasNodes.Any(node => node.GroupId == groupId && !sourceIds.Contains(node.Id)))
                template.GroupId = null;
        }
        var links = canvasLinks
            .Where(link => sourceIds.Contains(link.From) && sourceIds.Contains(link.To))
            .Select(link => (link.From, link.To, link.Label, link.Color, link.Arrow)).ToList();
        return (templates, links);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern uint GetClipboardSequenceNumber();
    int GetSystemClipboardSequence()
    {
        try { return (int)GetClipboardSequenceNumber(); }
        catch { return -1; }
    }
    void CopyCanvasSelection()
    {
        var (nodes, links) = BuildCanvasClipboardFromSelection();
        if (nodes.Count == 0) return;
        canvasClipboardNodes = nodes; canvasClipboardLinks = links;
        canvasClipboardSequence = GetSystemClipboardSequence();
        canvasClipboardBoardId = activeCanvasId;
        canvasClipboardPasteOffset = PointF.Empty;
    }
    void CutCanvasSelection()
    {
        if (canvasSelectedIds.Count == 0) return;
        CopyCanvasSelection();
        DeleteCanvasSelection();
    }
    // 依据模板创建新节点/连线（新 Id、图片按源画布文件复制、重映射分组与连线），加入画布并返回新节点
    List<CanvasNode> InstantiateCanvasTemplates(List<CanvasNode> templates, List<(Guid From, Guid To, string Label, string Color, int Arrow)> links, PointF offset, Guid sourceBoardId)
    {
        var idMap = new Dictionary<Guid, Guid>();
        var newNodes = new List<CanvasNode>();
        foreach (var template in templates)
        {
            var node = CloneCanvasNodeTemplate(template);
            var newId = Guid.NewGuid();
            idMap[template.Id] = newId;
            node.Id = newId;
            node.Position = new PointF(template.Position.X + offset.X, template.Position.Y + offset.Y);
            node.Bitmap = null;
            node.Locked = false; // 副本解锁，方便摆放
            if (node.Kind == "image")
            {
                string sourcePath = Path.Combine(canvasBoardsPath, sourceBoardId.ToString("D"), "images", $"{template.Id:D}.png");
                string targetPath = CanvasImageFile(newId);
                if (File.Exists(sourcePath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    File.Copy(sourcePath, targetPath, true);
                    node.Bitmap = LoadCanvasNodeImage(newId);
                }
                else if (template.Bitmap != null)
                {
                    node.Bitmap = new Bitmap(template.Bitmap);
                    SaveCanvasNodeImage(node);
                }
            }
            newNodes.Add(node);
        }
        var groupMap = new Dictionary<Guid, Guid>();
        foreach (var node in newNodes)
        {
            if (node.GroupId is not Guid oldGroup) continue;
            if (!groupMap.TryGetValue(oldGroup, out var newGroup)) { newGroup = Guid.NewGuid(); groupMap[oldGroup] = newGroup; }
            node.GroupId = newGroup;
        }
        foreach (var link in links)
        {
            if (!idMap.TryGetValue(link.From, out var from) || !idMap.TryGetValue(link.To, out var to)) continue;
            canvasLinks.Add(new CanvasLink { From = from, To = to, Label = link.Label, Color = link.Color, Arrow = link.Arrow });
        }
        canvasNodes.AddRange(newNodes);
        return newNodes;
    }
    void PasteCanvasClipboard()
    {
        if (canvasClipboardNodes == null || canvasClipboardNodes.Count == 0) return;
        float step = 24f / canvasZoom;
        canvasClipboardPasteOffset = new PointF(canvasClipboardPasteOffset.X + step, canvasClipboardPasteOffset.Y + step);
        PushCanvasUndo();
        var newNodes = InstantiateCanvasTemplates(canvasClipboardNodes, canvasClipboardLinks ?? new(), canvasClipboardPasteOffset, canvasClipboardBoardId);
        FinishCanvasClones(newNodes);
    }
    void DuplicateCanvasSelection()
    {
        var (nodes, links) = BuildCanvasClipboardFromSelection();
        if (nodes.Count == 0) return;
        PushCanvasUndo();
        float step = 24f / canvasZoom;
        canvasRepeat=new(step,step,1,1,0,true);
        var newNodes = InstantiateCanvasTemplates(nodes, links, new PointF(step, step), activeCanvasId);
        FinishCanvasClones(newNodes);
    }
    void FinishCanvasClones(List<CanvasNode> newNodes)
    {
        SetCanvasSelection(newNodes);
        canvasSurface.Invalidate();
        RefreshCanvasLayers();
        UpdateCanvasAlignBar();
        SaveCanvas();
    }
    void SelectAllCanvasNodes()
    {
        var nodes = canvasNodes.Where(node => !node.Hidden && !node.Locked).ToList();
        if (nodes.Count == 0) return;
        SetCanvasSelection(nodes);
        canvasSurface.Invalidate();
        if (canvasLayersPanel.Visible) RefreshCanvasLayers();
    }
    void BringCanvasSelectionToFront(bool toFront)
    {
        var selected = CanvasSelectedNodes();
        if (selected.Count == 0) return;
        PushCanvasUndo();
        foreach (var node in selected) canvasNodes.Remove(node);
        if (toFront) canvasNodes.AddRange(selected);
        else canvasNodes.InsertRange(0, selected);
        SetCanvasSelection(selected);
        canvasSurface.Invalidate();
        RefreshCanvasLayers();
        SaveCanvas();
    }
    void DeleteCanvasSelection()
    {
        var selected = CanvasSelectedNodes().Where(node => !node.Locked).ToList();
        if (selected.Count == 0) return;
        PushCanvasUndo();
        var ids = selected.Select(node => node.Id).ToHashSet();
        var affectedLayouts = selected.Where(node => node.AutoLayout && node.GroupId.HasValue).Select(node => node.GroupId!.Value).Distinct().ToList();
        canvasNodes.RemoveAll(node => ids.Contains(node.Id));
        foreach (var layoutId in affectedLayouts) ApplyCanvasAutoLayout(layoutId);
        canvasLinks.RemoveAll(link => ids.Contains(link.From) || ids.Contains(link.To));
        InvalidateCanvasPaintCache();
        if (selectedCanvasLink != null && (ids.Contains(selectedCanvasLink.From) || ids.Contains(selectedCanvasLink.To))) selectedCanvasLink = null;
        canvasSelectedIds.Clear();
        canvasSelectionStartPositions.Clear();
        if (selectedCanvasNode != null && ids.Contains(selectedCanvasNode.Id)) selectedCanvasNode = null;
        if (canvasVectorEditNodeId is Guid editId && ids.Contains(editId)) canvasVectorEditNodeId = null;
        canvasSurface.Invalidate();
        RefreshCanvasLayers();
        UpdateCanvasAlignBar();
        UpdateCanvasProps();
        SaveCanvas();
    }
    void SetCanvasSelection(IEnumerable<CanvasNode> nodes)
    {
        selectedCanvasLink = null;
        canvasSelectedIds.Clear();
        foreach (var node in nodes)
            if (canvasNodes.Any(item => item.Id == node.Id)) canvasSelectedIds.Add(node.Id);
        selectedCanvasNode = canvasNodes.LastOrDefault(node => canvasSelectedIds.Contains(node.Id));
        if(canvasPathEditId.HasValue&&!canvasSelectedIds.Contains(canvasPathEditId.Value))CancelCanvasPaint();
        canvasSelectionStartPositions.Clear();
        foreach (var node in canvasNodes.Where(node => canvasSelectedIds.Contains(node.Id))) canvasSelectionStartPositions[node.Id] = node.Position;
        UpdateCanvasAlignBar();
        UpdateCanvasProps();
        OpenNativePropertiesForSelection();
    }
    void UpdateCanvasProps()
    {
        bool show = canvasSelectedIds.Count > 0 || selectedCanvasLink != null;
        canvasPropsPanel.Visible = !nativeReady && show;
        LayoutCanvasSidebars();
        if (!show) return;
        if (selectedCanvasLink != null)
        {
            foreach (var control in canvasTransformPropControls) control.Visible = false;
            foreach (var control in canvasTextPropControls) control.Visible = false;
            foreach (var control in canvasSquarePropControls) control.Visible = false;
            foreach (var control in canvasFillColorPropControls) control.Visible = false;
            foreach (var control in canvasStrokePropControls) control.Visible = false;
            foreach (var control in canvasNodeNameControls) control.Visible = false;
            canvasPropsMultiHint.Visible = false;
            foreach (var control in canvasLinkPropControls) control.Visible = true;
            var link = selectedCanvasLink;
            if (canvasPropLinkText.Text != link.Label) canvasPropLinkText.Text = link.Label;
            UpdateCanvasLinkColorButton(link);
            SyncCanvasLinkArrowButtons(link.Arrow);
            canvasPropsPanel.BringToFront(); canvasLayersToggle.BringToFront();
            return;
        }
        var node = canvasSelectedIds.Count == 1 ? selectedCanvasNode : null;
        var allSelected = CanvasSelectedNodes();
        var textNodes = allSelected.Where(item => item.Kind == "text").ToList();
        var shapeNodes = allSelected.Where(IsCanvasShapeKind).ToList();
        var fillNodes = allSelected.Where(item => item.Kind == "text" || IsCanvasShapeKind(item)).ToList();
        bool single = node != null;
        bool multi = !single && allSelected.Count > 1;
        int textPropsOffset = single && node?.Kind == "text" ? 160 : 0;
        foreach (var (control, location) in canvasTextPropOriginalLocations) control.Location = new Point(location.X, location.Y + textPropsOffset);
        foreach (var control in canvasLinkPropControls) control.Visible = false;
        foreach (var control in canvasTransformPropControls) control.Visible = single;
        foreach (var control in canvasNodeNameControls) control.Visible = true;
        canvasPropName.Enabled = single;
        canvasPropsMultiHint.Visible = multi;
        if (single && node != null) { if (canvasPropName.Text != node.Name) canvasPropName.Text = node.Name; }
        else if (multi) canvasPropsMultiHint.Text = textNodes.Count > 0 ? $"已选中 {allSelected.Count} 个控件，可批量修改文字样式" : $"已选中 {allSelected.Count} 个控件，名字仅单选时可编辑";
        bool textProps = textNodes.Count > 0;
        canvasPropsLoading = true;
        if (single && node != null)
        {
            canvasPropX.Value = Math.Clamp((decimal)node.Position.X, canvasPropX.Minimum, canvasPropX.Maximum);
            canvasPropY.Value = Math.Clamp((decimal)node.Position.Y, canvasPropY.Minimum, canvasPropY.Maximum);
            canvasPropWidth.Value = Math.Clamp(node.Size.Width, (int)canvasPropWidth.Minimum, (int)canvasPropWidth.Maximum);
            canvasPropHeight.Value = Math.Clamp(node.Size.Height, (int)canvasPropHeight.Minimum, (int)canvasPropHeight.Maximum);
            canvasPropRotation.Value = Math.Clamp((decimal)node.Rotation, canvasPropRotation.Minimum, canvasPropRotation.Maximum);
            canvasPropOpacity.Value = (decimal)Math.Round(node.Opacity * 100f);
            canvasPropFillOpacity.Value = (decimal)Math.Round(node.FillOpacity * 100f);
        }
        // 内容仅单选文字控件可编辑；其余文字样式支持多选批量
        canvasPropContentLabel.Visible = textProps && single;
        canvasPropContent.Visible = textProps && single;
        foreach (var control in canvasTextPropControls)
        {
            if (control == canvasPropContentLabel || control == canvasPropContent) continue;
            control.Visible = textProps;
        }
        foreach (var control in canvasSquarePropControls) control.Visible = single && node != null && node.Kind == "square";
        foreach (var control in canvasFillColorPropControls) control.Visible = fillNodes.Count > 0;
        foreach (var control in canvasStrokePropControls) control.Visible = shapeNodes.Count > 0;
        if (textProps)
        {
            if (single && node != null) { if (canvasPropContent.Text != node.Content) canvasPropContent.Text = node.Content; }
            float firstSize = CanvasNodeFontSize(textNodes[0]);
            bool sizeMixed = textNodes.Any(item => Math.Abs(CanvasNodeFontSize(item) - firstSize) > 0.05f);
            if (sizeMixed) { canvasPropFontSize.Text = ""; }
            else { canvasPropFontSize.Value = (decimal)firstSize; }
            SetStyleCheckState(canvasPropBold, textNodes.Select(item => item.FontBold));
            SetStyleCheckState(canvasPropItalic, textNodes.Select(item => item.FontItalic));
            SetStyleCheckState(canvasPropStrikeout, textNodes.Select(item => item.FontStrikeout));
            int firstAlign = textNodes[0].FontAlign;
            SyncCanvasPropAlignButtons(textNodes.All(item => item.FontAlign == firstAlign) ? firstAlign : -1);
            int firstVAlign = textNodes[0].FontVAlign;
            SyncCanvasPropVAlignButtons(textNodes.All(item => item.FontVAlign == firstVAlign) ? firstVAlign : -1);
            UpdateCanvasPropColorButton(textNodes);
        }
        if (shapeNodes.Count > 0)
        {
            if (single && node != null)
            {
                decimal strokeWidthValue = (decimal)Math.Clamp(node.StrokeWidth, 0f, (float)canvasPropStrokeWidth.Maximum);
                if (canvasPropStrokeWidth.Value != strokeWidthValue) canvasPropStrokeWidth.Value = strokeWidthValue;
                canvasPropStrokeDash.Checked = node.StrokeDash;
                UpdateCanvasPropStrokeColorButton(node);
                SyncCanvasPropStrokeAlignButtons(node.StrokeAlign);
                if (node.Kind == "square")
                {
                    decimal radiusValue = (decimal)Math.Clamp(node.CornerRadius, 0f, (float)canvasPropCornerRadius.Maximum);
                    if (canvasPropCornerRadius.Value != radiusValue) canvasPropCornerRadius.Value = radiusValue;
                }
            }
            else
            {
                float firstStroke = shapeNodes[0].StrokeWidth;
                if (shapeNodes.All(item => Math.Abs(item.StrokeWidth - firstStroke) < 0.05f)) canvasPropStrokeWidth.Value = (decimal)firstStroke;
                else canvasPropStrokeWidth.Text = "";
                SetStyleCheckState(canvasPropStrokeDash, shapeNodes.Select(item => item.StrokeDash));
                UpdateCanvasPropStrokeColorButton(shapeNodes);
                int firstStrokeAlign = shapeNodes[0].StrokeAlign;
                SyncCanvasPropStrokeAlignButtons(shapeNodes.All(item => item.StrokeAlign == firstStrokeAlign) ? firstStrokeAlign : -1);
            }
        }
        if (fillNodes.Count > 0) UpdateCanvasPropFillColorButton(fillNodes);
        canvasPropsLoading = false;
        canvasPropsPanel.BringToFront(); canvasLayersToggle.BringToFront();
    }
    static void SetStyleCheckState(CheckBox box, IEnumerable<bool> values)
    {
        var list = values.ToList();
        box.CheckState = list.All(v => v) ? CheckState.Checked : list.All(v => !v) ? CheckState.Unchecked : CheckState.Indeterminate;
    }
    List<CanvasNode> CanvasStyleNodes() => CanvasSelectedNodes().Where(item => item.Kind == "text").ToList();
    List<CanvasNode> CanvasShapeStyleNodes() => CanvasSelectedNodes().Where(IsCanvasShapeKind).ToList();
    List<CanvasNode> CanvasFillStyleNodes() => CanvasSelectedNodes().Where(item => item.Kind == "text" || IsCanvasShapeKind(item)).ToList();
    void ApplyStyleBatch(IEnumerable<CanvasNode> nodes, Action<CanvasNode> change)
    {
        var list = nodes.ToList();
        if (list.Count == 0) return;
        PushCanvasUndo();
        foreach (var item in list) change(item);
        SaveCanvas();
        // 画布使用可视对象缓存；仅使窗口局部区域失效会继续绘制旧样式。
        InvalidateCanvasPaintCache();
        canvasSurface.Invalidate();
    }
    void ApplyCanvasPropName()
    {
        var node = canvasSelectedIds.Count == 1 ? selectedCanvasNode : null;
        if (node == null) return;
        var name = canvasPropName.Text.Trim();
        if (name == node.Name) return;
        PushCanvasUndo(); node.Name = name; RefreshCanvasLayers(); SaveCanvas();
    }
    void ApplyCanvasPropTransform()
    {
        if (canvasPropsLoading || canvasSelectedIds.Count != 1 || selectedCanvasNode is not { } node) return;
        var position = new PointF((float)canvasPropX.Value, (float)canvasPropY.Value);
        var size = new Size((int)Math.Round(canvasPropWidth.Value), (int)Math.Round(canvasPropHeight.Value));
        float rotation = (float)canvasPropRotation.Value;
        float opacity = (float)canvasPropOpacity.Value / 100f;
        float fillOpacity = (float)canvasPropFillOpacity.Value / 100f;
        if (node.Position == position && node.Size == size && Math.Abs(node.Rotation - rotation) < 0.01f && Math.Abs(node.Opacity - opacity) < 0.001f && Math.Abs(node.FillOpacity - fillOpacity) < 0.001f) return;
        if (!canvasTransformHistoryPushed) { PushCanvasUndo(); canvasTransformHistoryPushed = true; }
        if(node.Position!=position||node.Size!=size||Math.Abs(node.Rotation-rotation)>.01f)
        {
            var original=canvasUndo.Peek().Nodes.FirstOrDefault(n=>n.Id==node.Id);
            if(original!=null)canvasRepeat=new(position.X+size.Width/2f-original.Position.X-original.Size.Width/2f,position.Y+size.Height/2f-original.Position.Y-original.Size.Height/2f,(float)size.Width/original.Size.Width,(float)size.Height/original.Size.Height,rotation-original.Rotation,false);
        }
        node.Position = position; node.Size = size; node.Rotation = rotation; node.Opacity = opacity; node.FillOpacity = fillOpacity;
        InvalidateCanvasPaintCache(); canvasSurface.Invalidate();
        canvasSaveTimer.Stop(); canvasSaveTimer.Start();
    }
    CanvasNode? CanvasSingleTextPropNode()
    {
        var node = canvasSelectedIds.Count == 1 ? selectedCanvasNode : null;
        return node != null && node.Kind == "text" ? node : null;
    }
    void ApplyCanvasPropContent()
    {
        var node = CanvasSingleTextPropNode();
        if (node == null) return;
        var text = canvasPropContent.Text;
        if (text == node.Content) return;
        PushCanvasUndo(); node.Content = text; RefreshCanvasLayers(); SaveCanvas(); canvasSurface.Invalidate(CanvasBounds(node));
    }
    void ApplyCanvasPropFontSize()
    {
        if (canvasPropsLoading) return;
        var nodes = CanvasStyleNodes();
        if (nodes.Count == 0) return;
        float size = (float)canvasPropFontSize.Value;
        if (nodes.All(item => Math.Abs(item.FontSize - size) < 0.05f)) return;
        ApplyStyleBatch(nodes, item => item.FontSize = size);
    }
    void ApplyCanvasPropFontAlign(int align)
    {
        var nodes = CanvasStyleNodes();
        if (nodes.Count == 0 || nodes.All(item => item.FontAlign == align)) return;
        ApplyStyleBatch(nodes, item => item.FontAlign = align);
        SyncCanvasPropAlignButtons(align);
    }
    void ApplyCanvasPropCornerRadius()
    {
        if (canvasPropsLoading) return;
        var node = canvasSelectedIds.Count == 1 ? selectedCanvasNode : null;
        if (node == null || node.Kind != "square") return;
        float radius = (float)canvasPropCornerRadius.Value;
        if (Math.Abs(radius - node.CornerRadius) < 0.05f) return;
        PushCanvasUndo(); node.CornerRadius = radius; SaveCanvas(); InvalidateCanvasPaintCache(); canvasSurface.Invalidate();
    }
    void SyncCanvasPropAlignButtons(int align)
    {
        var buttons = new[] { (canvasPropAlignLeft, FontAlignLeft), (canvasPropAlignCenter, FontAlignCenter), (canvasPropAlignRight, FontAlignRight) };
        foreach (var (button, value) in buttons)
        {
            bool active = value == align;
            button.BackColor = active ? Color.FromArgb(39, 37, 43) : Color.FromArgb(232, 229, 224);
            button.ForeColor = active ? Color.White : Color.FromArgb(70, 67, 65);
        }
    }
    void ApplyCanvasPropFontVAlign(int valign)
    {
        var nodes = CanvasStyleNodes();
        if (nodes.Count == 0 || nodes.All(item => item.FontVAlign == valign)) return;
        ApplyStyleBatch(nodes, item => item.FontVAlign = valign);
        SyncCanvasPropVAlignButtons(valign);
    }
    void SyncCanvasPropVAlignButtons(int valign)
    {
        var buttons = new[] { (canvasPropAlignTop, FontAlignTop), (canvasPropAlignMiddle, FontAlignMiddle), (canvasPropAlignBottom, FontAlignBottom) };
        foreach (var (button, value) in buttons)
        {
            bool active = value == valign;
            button.BackColor = active ? Color.FromArgb(39, 37, 43) : Color.FromArgb(232, 229, 224);
            button.ForeColor = active ? Color.White : Color.FromArgb(70, 67, 65);
        }
    }
    void ApplyCanvasPropFontStyle()
    {
        if (canvasPropsLoading) return;
        var nodes = CanvasStyleNodes();
        if (nodes.Count == 0) return;
        // 用户点击不应停在“混合”态：点到混合态一律视为勾上
        canvasPropsLoading = true;
        if (canvasPropBold.CheckState == CheckState.Indeterminate) canvasPropBold.CheckState = CheckState.Checked;
        if (canvasPropItalic.CheckState == CheckState.Indeterminate) canvasPropItalic.CheckState = CheckState.Checked;
        if (canvasPropStrikeout.CheckState == CheckState.Indeterminate) canvasPropStrikeout.CheckState = CheckState.Checked;
        canvasPropsLoading = false;
        bool bold = canvasPropBold.Checked, italic = canvasPropItalic.Checked, strikeout = canvasPropStrikeout.Checked;
        if (nodes.All(item => item.FontBold == bold && item.FontItalic == italic && item.FontStrikeout == strikeout)) return;
        ApplyStyleBatch(nodes, item => { item.FontBold = bold; item.FontItalic = italic; item.FontStrikeout = strikeout; });
    }
    void PickCanvasPropColor()
    {
        var nodes = CanvasStyleNodes();
        if (nodes.Count == 0) return;
        using var dialog = new ColorDialog { FullOpen = true, Color = CanvasNodeTextColor(nodes[0], false) };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var value = dialog.Color.ToArgb().ToString(System.Globalization.CultureInfo.InvariantCulture);
        ApplyStyleBatch(nodes, item => item.FontColor = value);
        UpdateCanvasPropColorButton(nodes);
    }
    void ResetCanvasPropColor()
    {
        var nodes = CanvasStyleNodes();
        if (nodes.Count == 0 || nodes.All(item => string.IsNullOrEmpty(item.FontColor))) return;
        ApplyStyleBatch(nodes, item => item.FontColor = "");
        UpdateCanvasPropColorButton(nodes);
    }
    void UpdateCanvasPropColorButton(CanvasNode node) => UpdateCanvasPropColorButton(new List<CanvasNode> { node });
    void UpdateCanvasPropColorButton(List<CanvasNode> nodes)
    {
        bool allEmpty = nodes.All(item => string.IsNullOrEmpty(item.FontColor));
        bool allSame = !allEmpty && nodes.All(item => item.FontColor == nodes[0].FontColor);
        if (allEmpty)
        {
            canvasPropColor.BackColor = Color.FromArgb(232, 229, 224); canvasPropColor.ForeColor = Color.FromArgb(70, 67, 65);
            canvasPropColor.Text = "选择颜色";
        }
        else if (allSame)
        {
            var color = CanvasNodeTextColor(nodes[0], false);
            canvasPropColor.BackColor = color;
            bool dark = color.R * 0.299 + color.G * 0.587 + color.B * 0.114 < 128;
            canvasPropColor.ForeColor = dark ? Color.White : Color.FromArgb(39, 37, 43);
            canvasPropColor.Text = "颜色：自定义";
        }
        else
        {
            canvasPropColor.BackColor = Color.FromArgb(210, 206, 199); canvasPropColor.ForeColor = Color.FromArgb(70, 67, 65);
            canvasPropColor.Text = "多种颜色（点击统一）";
        }
    }
    void PickCanvasPropFillColor()
    {
        var nodes = CanvasFillStyleNodes();
        if (nodes.Count == 0) return;
        using var dialog = new ColorDialog { FullOpen = true, Color = CanvasNodeFillColor(nodes[0]) };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var value = dialog.Color.ToArgb().ToString(System.Globalization.CultureInfo.InvariantCulture);
        ApplyStyleBatch(nodes, item => item.FillColor = value);
        UpdateCanvasPropFillColorButton(nodes);
    }
    void ResetCanvasPropFillColor()
    {
        var nodes = CanvasFillStyleNodes();
        if (nodes.Count == 0 || nodes.All(item => string.IsNullOrEmpty(item.FillColor))) return;
        ApplyStyleBatch(nodes, item => item.FillColor = "");
        UpdateCanvasPropFillColorButton(nodes);
    }
    void UpdateCanvasPropFillColorButton(CanvasNode node) => UpdateCanvasPropFillColorButton(new List<CanvasNode> { node });
    void UpdateCanvasPropFillColorButton(List<CanvasNode> nodes)
    {
        bool allEmpty = nodes.All(item => string.IsNullOrEmpty(item.FillColor));
        bool allSame = !allEmpty && nodes.All(item => item.FillColor == nodes[0].FillColor);
        if (allEmpty)
        {
            canvasPropFillColor.BackColor = Color.FromArgb(232, 229, 224); canvasPropFillColor.ForeColor = Color.FromArgb(70, 67, 65);
            canvasPropFillColor.Text = "选择颜色";
        }
        else if (allSame)
        {
            var color = CanvasNodeFillColor(nodes[0]);
            canvasPropFillColor.BackColor = color;
            bool dark = color.R * 0.299 + color.G * 0.587 + color.B * 0.114 < 128;
            canvasPropFillColor.ForeColor = dark ? Color.White : Color.FromArgb(39, 37, 43);
            canvasPropFillColor.Text = "颜色：自定义";
        }
        else
        {
            canvasPropFillColor.BackColor = Color.FromArgb(210, 206, 199); canvasPropFillColor.ForeColor = Color.FromArgb(70, 67, 65);
            canvasPropFillColor.Text = "多种颜色（点击统一）";
        }
    }
    CanvasNode? CanvasSingleShapeNode()
    {
        var node = canvasSelectedIds.Count == 1 ? selectedCanvasNode : null;
        return node != null && IsCanvasShapeKind(node) ? node : null;
    }
    static Color CanvasNodeStrokeColor(CanvasNode node) => TryCanvasStoredColor(node.StrokeColor, out var color) ? color : Color.FromArgb(190, 186, 179);
    void ApplyCanvasPropStrokeWidth()
    {
        if (canvasPropsLoading) return;
        var nodes = CanvasShapeStyleNodes();
        if (nodes.Count == 0) return;
        float width = (float)canvasPropStrokeWidth.Value;
        if (nodes.All(item => Math.Abs(item.StrokeWidth - width) < 0.05f)) return;
        ApplyStyleBatch(nodes, item => item.StrokeWidth = width);
    }
    void ApplyCanvasPropStrokeDash()
    {
        if (canvasPropsLoading) return;
        var nodes = CanvasShapeStyleNodes();
        if (nodes.Count == 0) return;
        canvasPropsLoading = true;
        if (canvasPropStrokeDash.CheckState == CheckState.Indeterminate) canvasPropStrokeDash.CheckState = CheckState.Checked;
        canvasPropsLoading = false;
        bool dashed = canvasPropStrokeDash.Checked;
        if (nodes.All(item => item.StrokeDash == dashed)) return;
        ApplyStyleBatch(nodes, item => item.StrokeDash = dashed);
    }
    void ApplyCanvasPropStrokeAlign(int align)
    {
        var nodes = CanvasShapeStyleNodes();
        if (nodes.Count == 0 || nodes.All(item => item.StrokeAlign == align)) return;
        ApplyStyleBatch(nodes, item => item.StrokeAlign = align);
        SyncCanvasPropStrokeAlignButtons(align);
    }
    void SyncCanvasPropStrokeAlignButtons(int align)
    {
        var buttons = new[] { (canvasPropStrokeInner, 0), (canvasPropStrokeCenter, 1), (canvasPropStrokeOuter, 2) };
        foreach (var (button, value) in buttons)
        {
            bool active = value == align;
            button.BackColor = active ? Color.FromArgb(39, 37, 43) : Color.FromArgb(232, 229, 224);
            button.ForeColor = active ? Color.White : Color.FromArgb(70, 67, 65);
        }
    }
    void PickCanvasPropStrokeColor()
    {
        var nodes = CanvasShapeStyleNodes();
        if (nodes.Count == 0) return;
        using var dialog = new ColorDialog { FullOpen = true, Color = CanvasNodeStrokeColor(nodes[0]) };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var value = dialog.Color.ToArgb().ToString(System.Globalization.CultureInfo.InvariantCulture);
        ApplyStyleBatch(nodes, item => item.StrokeColor = value);
        UpdateCanvasPropStrokeColorButton(nodes);
    }
    void ResetCanvasPropStrokeColor()
    {
        var nodes = CanvasShapeStyleNodes();
        if (nodes.Count == 0 || nodes.All(item => string.IsNullOrEmpty(item.StrokeColor))) return;
        ApplyStyleBatch(nodes, item => item.StrokeColor = "");
        UpdateCanvasPropStrokeColorButton(nodes);
    }
    void UpdateCanvasPropStrokeColorButton(CanvasNode node) => UpdateCanvasPropStrokeColorButton(new List<CanvasNode> { node });
    void UpdateCanvasPropStrokeColorButton(List<CanvasNode> nodes)
    {
        bool allEmpty = nodes.All(item => string.IsNullOrEmpty(item.StrokeColor));
        bool allSame = !allEmpty && nodes.All(item => item.StrokeColor == nodes[0].StrokeColor);
        if (allEmpty)
        {
            canvasPropStrokeColor.BackColor = Color.FromArgb(232, 229, 224); canvasPropStrokeColor.ForeColor = Color.FromArgb(70, 67, 65);
            canvasPropStrokeColor.Text = "选择颜色";
        }
        else if (allSame)
        {
            var color = CanvasNodeStrokeColor(nodes[0]);
            canvasPropStrokeColor.BackColor = color;
            bool dark = color.R * 0.299 + color.G * 0.587 + color.B * 0.114 < 128;
            canvasPropStrokeColor.ForeColor = dark ? Color.White : Color.FromArgb(39, 37, 43);
            canvasPropStrokeColor.Text = "颜色：自定义";
        }
        else
        {
            canvasPropStrokeColor.BackColor = Color.FromArgb(210, 206, 199); canvasPropStrokeColor.ForeColor = Color.FromArgb(70, 67, 65);
            canvasPropStrokeColor.Text = "多种颜色（点击统一）";
        }
    }
    List<CanvasNode> CanvasSelectedNodes() => canvasNodes.Where(node => canvasSelectedIds.Contains(node.Id)).ToList();
    static readonly Color CanvasLinkDefaultColor = Color.FromArgb(114, 132, 119);
    static Color CanvasLinkColor(CanvasLink link) => int.TryParse(link.Color, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var argb) ? Color.FromArgb(argb) : CanvasLinkDefaultColor;
    void InvalidateCanvasLink(CanvasLink link) { var bounds = CanvasLinkBounds(link); bounds.Inflate(120, 30); canvasSurface.Invalidate(bounds); }
    void SetCanvasLinkSelection(CanvasLink? link)
    {
        bool changed = selectedCanvasLink != link;
        selectedCanvasLink = link;
        if (link != null)
        {
            canvasSelectedIds.Clear();
            canvasSelectionStartPositions.Clear();
            selectedCanvasNode = null;
            UpdateCanvasAlignBar();
        }
        UpdateCanvasProps();
        if (changed) canvasSurface.Invalidate();
        OpenNativePropertiesForSelection();
    }
    void DeleteSelectedCanvasLink()
    {
        var link = selectedCanvasLink;
        if (link == null) return;
        DeleteCanvasLink(link);
    }
    void DeleteCanvasLink(CanvasLink link)
    {
        if (!canvasLinks.Contains(link)) return;
        PushCanvasUndo();
        canvasLinks.Remove(link);
        InvalidateCanvasPaintCache();
        if (canvasInlineEditLink == link) CancelCanvasInlineEdit();
        selectedCanvasLink = null;
        canvasSurface.Refresh();
        UpdateCanvasProps();
        SaveCanvas();
    }
    void ApplyCanvasLinkLabel()
    {
        var link = selectedCanvasLink;
        if (link == null) return;
        var text = canvasPropLinkText.Text;
        if (text == link.Label) return;
        PushCanvasUndo(); link.Label = text; SaveCanvas(); InvalidateCanvasLink(link);
    }
    void PickCanvasLinkColor()
    {
        var link = selectedCanvasLink;
        if (link == null) return;
        using var dialog = new ColorDialog { FullOpen = true, Color = CanvasLinkColor(link) };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        PushCanvasUndo();
        link.Color = dialog.Color.ToArgb().ToString(System.Globalization.CultureInfo.InvariantCulture);
        SaveCanvas(); InvalidateCanvasLink(link); UpdateCanvasLinkColorButton(link);
    }
    void ApplyNativeCanvasLinkColor(Color color)
    {
        var link = selectedCanvasLink;
        if (link == null) return;
        PushCanvasUndo();
        link.Color = Color.FromArgb(color.R, color.G, color.B).ToArgb().ToString(System.Globalization.CultureInfo.InvariantCulture);
        SaveCanvas();
        InvalidateCanvasLink(link);
        UpdateCanvasLinkColorButton(link);
    }
    void ResetCanvasLinkColor()
    {
        var link = selectedCanvasLink;
        if (link == null || string.IsNullOrEmpty(link.Color)) return;
        PushCanvasUndo(); link.Color = ""; SaveCanvas(); InvalidateCanvasLink(link); UpdateCanvasLinkColorButton(link);
    }
    void UpdateCanvasLinkColorButton(CanvasLink link)
    {
        if (string.IsNullOrEmpty(link.Color))
        {
            canvasPropLinkColor.BackColor = Color.FromArgb(232, 229, 224); canvasPropLinkColor.ForeColor = Color.FromArgb(70, 67, 65);
            canvasPropLinkColor.Text = "选择颜色";
        }
        else
        {
            var color = CanvasLinkColor(link);
            canvasPropLinkColor.BackColor = color;
            bool dark = color.R * 0.299 + color.G * 0.587 + color.B * 0.114 < 128;
            canvasPropLinkColor.ForeColor = dark ? Color.White : Color.FromArgb(39, 37, 43);
            canvasPropLinkColor.Text = "颜色：自定义";
        }
    }
    void ApplyCanvasLinkArrow(int arrow)
    {
        var link = selectedCanvasLink;
        if (link == null || link.Arrow == arrow) return;
        PushCanvasUndo(); link.Arrow = arrow; SaveCanvas(); InvalidateCanvasLink(link); SyncCanvasLinkArrowButtons(arrow);
    }
    void SyncCanvasLinkArrowButtons(int arrow)
    {
        var buttons = new[] { (canvasPropLinkArrowNone, 0), (canvasPropLinkArrowEnd, 1), (canvasPropLinkArrowStart, 2), (canvasPropLinkArrowBoth, 3) };
        foreach (var (button, value) in buttons)
        {
            bool active = value == arrow;
            button.BackColor = active ? Color.FromArgb(39, 37, 43) : Color.FromArgb(232, 229, 224);
            button.ForeColor = active ? Color.White : Color.FromArgb(70, 67, 65);
        }
    }
    List<CanvasNode> CanvasGroupMembers(CanvasNode node) => node.GroupId is Guid groupId ? canvasNodes.Where(item => item.GroupId == groupId).ToList() : new List<CanvasNode> { node };
    // 拖动吸附：把被拖动节点的 左/中/右、上/中/下 与静止节点同名线对齐，返回整体修正量，并记录辅助线（世界坐标）
    void UpdateCanvasSnapping(List<CanvasNode> movedNodes, float dx, float dy, out float adjustX, out float adjustY)
    {
        adjustX = 0; adjustY = 0;
        canvasSnapGuides.Clear();
        var movedIds = movedNodes.Select(node => node.Id).ToHashSet();
        var targets = canvasNodes.Where(node => !movedIds.Contains(node.Id) && !node.Hidden).ToArray();
        if (targets.Length == 0 || movedNodes.Count == 0) return;
        float threshold = 7f / canvasZoom;
        float padding = 6f / canvasZoom;
        float bestDeltaX = 0, bestDeltaY = 0;
        float bestAbsX = float.MaxValue, bestAbsY = float.MaxValue;
        float guideVX = 0, guideVTop = 0, guideVBottom = 0, guideHY = 0, guideHLeft = 0, guideHRight = 0;
        bool foundX = false, foundY = false;
        foreach (var moved in movedNodes)
        {
            if (!canvasSelectionStartPositions.TryGetValue(moved.Id, out var start)) continue;
            var mb = new RectangleF(start.X + dx, start.Y + dy, moved.Size.Width, moved.Size.Height);
            float[] movedX = { mb.Left, mb.Left + mb.Width / 2f, mb.Right };
            float[] movedY = { mb.Top, mb.Top + mb.Height / 2f, mb.Bottom };
            foreach (var target in targets)
            {
                var tb = new RectangleF(target.Position.X, target.Position.Y, target.Size.Width, target.Size.Height);
                float[] targetX = { tb.Left, tb.Left + tb.Width / 2f, tb.Right };
                float[] targetY = { tb.Top, tb.Top + tb.Height / 2f, tb.Bottom };
                for (int i = 0; i < 3; i++)
                {
                    float deltaX = targetX[i] - movedX[i];
                    if (Math.Abs(deltaX) < bestAbsX) { bestAbsX = Math.Abs(deltaX); bestDeltaX = deltaX; foundX = true; guideVX = targetX[i]; guideVTop = Math.Min(mb.Top, tb.Top) - padding; guideVBottom = Math.Max(mb.Bottom, tb.Bottom) + padding; }
                    float deltaY = targetY[i] - movedY[i];
                    if (Math.Abs(deltaY) < bestAbsY) { bestAbsY = Math.Abs(deltaY); bestDeltaY = deltaY; foundY = true; guideHY = targetY[i]; guideHLeft = Math.Min(mb.Left, tb.Left) - padding; guideHRight = Math.Max(mb.Right, tb.Right) + padding; }
                }
            }
        }
        if (foundX && bestAbsX <= threshold)
        {
            adjustX = bestDeltaX;
            canvasSnapGuides.Add((new PointF(guideVX, guideVTop), new PointF(guideVX, guideVBottom)));
        }
        if (foundY && bestAbsY <= threshold)
        {
            adjustY = bestDeltaY;
            canvasSnapGuides.Add((new PointF(guideHLeft, guideHY), new PointF(guideHRight, guideHY)));
        }
    }
    void AlignCanvasSelection(int mode)
    {
        var nodes = CanvasSelectedNodes().Where(node => !node.Locked && !node.Hidden).ToList();
        if (nodes.Count < 2 || (mode >= AlignDistributeH && nodes.Count < 3)) return;
        PushCanvasUndo();
        float minLeft = float.MaxValue, minTop = float.MaxValue, maxRight = float.MinValue, maxBottom = float.MinValue;
        foreach (var node in nodes)
        {
            minLeft = Math.Min(minLeft, node.Position.X);
            minTop = Math.Min(minTop, node.Position.Y);
            maxRight = Math.Max(maxRight, node.Position.X + node.Size.Width);
            maxBottom = Math.Max(maxBottom, node.Position.Y + node.Size.Height);
        }
        switch (mode)
        {
            case AlignLeft: foreach (var node in nodes) node.Position = new PointF(minLeft, node.Position.Y); break;
            case AlignCenterH:
                float centerX = (minLeft + maxRight) / 2f;
                foreach (var node in nodes) node.Position = new PointF(centerX - node.Size.Width / 2f, node.Position.Y);
                break;
            case AlignRight: foreach (var node in nodes) node.Position = new PointF(maxRight - node.Size.Width, node.Position.Y); break;
            case AlignTop: foreach (var node in nodes) node.Position = new PointF(node.Position.X, minTop); break;
            case AlignMiddleV:
                float centerY = (minTop + maxBottom) / 2f;
                foreach (var node in nodes) node.Position = new PointF(node.Position.X, centerY - node.Size.Height / 2f);
                break;
            case AlignBottom: foreach (var node in nodes) node.Position = new PointF(node.Position.X, maxBottom - node.Size.Height); break;
            case AlignDistributeH:
            {
                float totalWidth = nodes.Sum(node => node.Size.Width);
                float gapX = (maxRight - minLeft - totalWidth) / (nodes.Count - 1);
                float cursorX = minLeft;
                foreach (var node in nodes.OrderBy(node => node.Position.X)) { node.Position = new PointF(cursorX, node.Position.Y); cursorX += node.Size.Width + gapX; }
                break;
            }
            case AlignDistributeV:
            {
                float totalHeight = nodes.Sum(node => node.Size.Height);
                float gapY = (maxBottom - minTop - totalHeight) / (nodes.Count - 1);
                float cursorY = minTop;
                foreach (var node in nodes.OrderBy(node => node.Position.Y)) { node.Position = new PointF(node.Position.X, cursorY); cursorY += node.Size.Height + gapY; }
                break;
            }
        }
        canvasSurface.Invalidate();
        SaveCanvas();
        UpdateCanvasAlignBar();
    }
    void BuildCanvasAlignBar()
    {
        canvasAlignBar.BackColor = Color.FromArgb(39, 37, 43);
        canvasAlignBar.Visible = false;
        var modes = new (int Mode, string Tip)[]
        {
            (AlignLeft, "左对齐"), (AlignCenterH, "水平居中"), (AlignRight, "右对齐"),
            (AlignTop, "顶对齐"), (AlignMiddleV, "垂直居中"), (AlignBottom, "底对齐"),
            (AlignDistributeH, "水平等距分布（需 3 个以上）"), (AlignDistributeV, "垂直等距分布（需 3 个以上）")
        };
        var toolTip = new ToolTip();
        int x = 10;
        for (int i = 0; i < modes.Length; i++)
        {
            int mode = modes[i].Mode;
            if (i == 3 || i == 6)
            {
                var separator = new Panel { Size = new Size(1, 20), Location = new Point(x + 5, 11), BackColor = Color.FromArgb(120, 116, 110) };
                canvasAlignBar.Controls.Add(separator);
                x += 12;
            }
            var button = new Button { Size = new Size(34, 32), Location = new Point(x, 5), FlatStyle = FlatStyle.Flat, TabStop = false, BackColor = Color.FromArgb(39, 37, 43), ForeColor = Color.White };
            button.FlatAppearance.BorderSize = 0;
            button.Region = RoundedRegion(button, 7);
            int paintedMode = mode;
            button.Paint += (_, e) => DrawAlignIcon(e.Graphics, button.Enabled, paintedMode);
            button.Click += (_, _) => AlignCanvasSelection(paintedMode);
            button.MouseEnter += (_, _) => { if (button.Enabled) button.BackColor = Color.FromArgb(72, 68, 76); };
            button.MouseLeave += (_, _) => button.BackColor = Color.FromArgb(39, 37, 43);
            toolTip.SetToolTip(button, modes[i].Tip);
            canvasAlignButtons.Add(button);
            canvasAlignBar.Controls.Add(button);
            x += 38;
        }
        canvasAlignBar.Width = x + 8;
        canvasAlignBar.Location = new Point((canvasPage.Width - canvasAlignBar.Width) / 2, 14);
        canvasAlignBar.Region = RoundedRegion(canvasAlignBar, 12);
    }
    void UpdateCanvasAlignBar()
    {
        if (nativeReady) { canvasAlignBar.Visible = false; return; }
        int count = CanvasSelectedNodes().Count(node => !node.Locked && !node.Hidden);
        canvasAlignBar.Visible = organizeSelected && count >= 2;
        if (canvasAlignBar.Visible)
        {
            // 按钮顺序与 modes 一致：6、7 为等距分布
            for (int i = 0; i < canvasAlignButtons.Count; i++) canvasAlignButtons[i].Enabled = i < 6 || count >= 3;
            canvasAlignBar.BringToFront();
        }
    }
    static void DrawAlignIcon(Graphics graphics, bool enabled, int mode)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(enabled ? Color.White : Color.FromArgb(125, 121, 116), 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var brush = new SolidBrush(enabled ? Color.White : Color.FromArgb(125, 121, 116));
        int[] lengths = { 14, 8, 12, 8 };
        switch (mode)
        {
            case AlignLeft: for (int i = 0; i < 4; i++) graphics.DrawLine(pen, 9, 8 + i * 4, 9 + lengths[i], 8 + i * 4); break;
            case AlignCenterH: for (int i = 0; i < 4; i++) graphics.DrawLine(pen, 17 - lengths[i] / 2f, 8 + i * 4, 17 + lengths[i] / 2f, 8 + i * 4); break;
            case AlignRight: for (int i = 0; i < 4; i++) graphics.DrawLine(pen, 25 - lengths[i], 8 + i * 4, 25, 8 + i * 4); break;
            case AlignTop: for (int i = 0; i < 4; i++) graphics.DrawLine(pen, 10 + i * 4, 7, 10 + i * 4, 7 + lengths[i]); break;
            case AlignMiddleV: for (int i = 0; i < 4; i++) graphics.DrawLine(pen, 10 + i * 4, 16 - lengths[i] / 2f, 10 + i * 4, 16 + lengths[i] / 2f); break;
            case AlignBottom: for (int i = 0; i < 4; i++) graphics.DrawLine(pen, 10 + i * 4, 25 - lengths[i], 10 + i * 4, 25); break;
            case AlignDistributeH:
                graphics.FillRectangle(brush, 7, 10, 3, 12); graphics.FillRectangle(brush, 16, 10, 3, 12); graphics.FillRectangle(brush, 25, 10, 3, 12);
                DrawArrowHeads(graphics, pen, 11, 15, 16, true); DrawArrowHeads(graphics, pen, 24, 20, 16, true);
                break;
            case AlignDistributeV:
                graphics.FillRectangle(brush, 10, 6, 12, 3); graphics.FillRectangle(brush, 10, 15, 12, 3); graphics.FillRectangle(brush, 10, 24, 12, 3);
                DrawArrowHeads(graphics, pen, 16, 10, 14, false); DrawArrowHeads(graphics, pen, 16, 23, 19, false);
                break;
        }
    }
    static void DrawArrowHeads(Graphics graphics, Pen pen, int from, int to, int fixedLine, bool horizontal)
    {
        float dir = Math.Sign(to - from);
        if (horizontal)
        {
            graphics.DrawLine(pen, from, fixedLine, to, fixedLine);
            graphics.DrawLine(pen, to, fixedLine, to - dir * 3, fixedLine - 2.5f);
            graphics.DrawLine(pen, to, fixedLine, to - dir * 3, fixedLine + 2.5f);
        }
        else
        {
            graphics.DrawLine(pen, fixedLine, from, fixedLine, to);
            graphics.DrawLine(pen, fixedLine, to, fixedLine - 2.5f, to - dir * 3);
            graphics.DrawLine(pen, fixedLine, to, fixedLine + 2.5f, to - dir * 3);
        }
    }
    static Rectangle CanvasRect(Point start, Point end) => Rectangle.FromLTRB(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Max(start.X, end.X), Math.Max(start.Y, end.Y));
    static List<PointF> DefaultCanvasAnchors() => new() { new PointF(0, 0), new PointF(1, 0), new PointF(1, 1), new PointF(0, 1) };
    // 各形状进入锚点编辑时的初始轮廓（归一化坐标 0~1），圆形用多点多边形近似
    static List<PointF> DefaultShapeAnchors(string kind)
    {
        switch (kind)
        {
            case "triangle":
                return new List<PointF> { new(0.5f, 0), new(1, 1), new(0, 1) };
            case "diamond":
                return new List<PointF> { new(0.5f, 0), new(1, 0.5f), new(0.5f, 1), new(0, 0.5f) };
            case "star":
                var points = new List<PointF>(10);
                for (int i = 0; i < 10; i++)
                {
                    double angle = -Math.PI / 2 + i * Math.PI / 5;
                    float ratio = i % 2 == 0 ? 1f : 0.382f;
                    points.Add(new PointF(0.5f + (float)Math.Cos(angle) * 0.5f * ratio, 0.5f + (float)Math.Sin(angle) * 0.5f * ratio));
                }
                return points;
            case "circle":
                var ellipse = new List<PointF>(24);
                for (int i = 0; i < 24; i++)
                {
                    double angle = i * Math.PI * 2 / 24;
                    ellipse.Add(new PointF(0.5f + (float)Math.Cos(angle) * 0.5f, 0.5f + (float)Math.Sin(angle) * 0.5f));
                }
                return ellipse;
            default:
                return DefaultCanvasAnchors();
        }
    }
    List<PointF> CanvasAnchors(CanvasNode node) => node.VectorAnchors.Count >= 3 ? node.VectorAnchors : DefaultShapeAnchors(node.Kind);
    PointF CanvasAnchorScreenPoint(CanvasNode node, int anchorIndex)
    {
        var bounds = CanvasBounds(node); var anchor = CanvasAnchors(node)[anchorIndex];
        var point = new PointF(bounds.Left + anchor.X * bounds.Width, bounds.Top + anchor.Y * bounds.Height);
        return RotateCanvasPoint(point, CanvasNodeCenter(bounds), node.Rotation);
    }
    int CanvasAnchorAt(CanvasNode node, Point point) => CanvasAnchorAt(node, point, out _);
    int CanvasAnchorAt(CanvasNode node, Point point, out PointF handlePoint)
    {
        handlePoint = PointF.Empty;
        var bounds = CanvasBounds(node); var center = CanvasNodeCenter(bounds);
        float bestDistance = 10f * 10f; int bestIndex = -1;
        foreach (var handle in CanvasAnchorHandles(node, bounds))
        {
            var screen = RotateCanvasPoint(handle.Point, center, node.Rotation);
            float dx = screen.X - point.X, dy = screen.Y - point.Y, distance = dx * dx + dy * dy;
            if (distance > bestDistance) continue;
            bestDistance = distance; bestIndex = handle.Index; handlePoint = screen;
        }
        return bestIndex;
    }
    bool TryGetCanvasAnchorInsertion(CanvasNode node, Point cursor, out int insertIndex, out PointF normalized, out PointF screenPoint, out bool midpoint)
    {
        insertIndex = -1; normalized = PointF.Empty; screenPoint = PointF.Empty; midpoint = false;
        var anchors = CanvasAnchors(node);
        if (anchors.Count < 3) return false;
        var bounds = CanvasBounds(node);
        var center = CanvasNodeCenter(bounds);
        float bestDistanceSquared = 12f * 12f;
        int bestIndex = -1; PointF bestNormalized = PointF.Empty, bestScreen = PointF.Empty; bool bestMidpoint = false;
        var localPoints = anchors.Select(anchor => new PointF(bounds.Left + anchor.X * bounds.Width, bounds.Top + anchor.Y * bounds.Height)).ToList();
        var rounded = CanvasRoundedCorners(localPoints, node.CornerRadius * canvasZoom);
        void Consider(PointF localA, PointF localB, int candidateIndex, bool allowMidpointSnap)
        {
            var screenA = RotateCanvasPoint(localA, center, node.Rotation);
            var screenB = RotateCanvasPoint(localB, center, node.Rotation);
            float dx = screenB.X - screenA.X, dy = screenB.Y - screenA.Y;
            float lengthSquared = dx * dx + dy * dy;
            if (lengthSquared < 0.001f) return;
            float t = Math.Clamp(((cursor.X - screenA.X) * dx + (cursor.Y - screenA.Y) * dy) / lengthSquared, 0f, 1f);
            float projectedX = screenA.X + dx * t, projectedY = screenA.Y + dy * t;
            float offsetX = cursor.X - projectedX, offsetY = cursor.Y - projectedY;
            float distanceSquared = offsetX * offsetX + offsetY * offsetY;
            if (distanceSquared > bestDistanceSquared) return;

            float segmentLength = MathF.Sqrt(lengthSquared);
            bool snapToMidpoint = allowMidpointSnap && Math.Abs(t - 0.5f) * segmentLength <= 12f;
            if (snapToMidpoint) t = 0.5f;
            bestDistanceSquared = distanceSquared;
            bestIndex = candidateIndex;
            var projectedLocal = new PointF(localA.X + (localB.X - localA.X) * t, localA.Y + (localB.Y - localA.Y) * t);
            bestNormalized = new PointF((projectedLocal.X - bounds.Left) / Math.Max(1f, bounds.Width), (projectedLocal.Y - bounds.Top) / Math.Max(1f, bounds.Height));
            bestScreen = RotateCanvasPoint(projectedLocal, center, node.Rotation);
            bestMidpoint = snapToMidpoint;
        }
        for (int i = 0; i < anchors.Count; i++)
        {
            int next = (i + 1) % anchors.Count;
            int candidateIndex = i + 1;
            Consider(rounded[i].Exit, rounded[next].Entry, candidateIndex, true);
            if (!rounded[next].IsRounded) continue;
            var corner = rounded[next]; var previous = corner.Entry;
            for (int step = 1; step <= 16; step++)
            {
                float t = step / 16f;
                float inverse = 1f - t;
                var current = new PointF(
                    inverse * inverse * inverse * corner.Entry.X + 3 * inverse * inverse * t * corner.Control1.X + 3 * inverse * t * t * corner.Control2.X + t * t * t * corner.Exit.X,
                    inverse * inverse * inverse * corner.Entry.Y + 3 * inverse * inverse * t * corner.Control1.Y + 3 * inverse * t * t * corner.Control2.Y + t * t * t * corner.Exit.Y);
                Consider(previous, current, candidateIndex, false);
                previous = current;
            }
        }
        insertIndex = bestIndex; normalized = bestNormalized; screenPoint = bestScreen; midpoint = bestMidpoint;
        return bestIndex >= 0;
    }
    void ClearCanvasAnchorInsertionPreview()
    {
        canvasAnchorPreviewInsertIndex = -1;
        canvasAnchorPreviewMidpoint = false;
    }
    static readonly string[] canvasShapeKinds = { "square", "circle", "triangle", "diamond", "star" };
    static bool IsCanvasShapeKind(CanvasNode node) => canvasShapeKinds.Contains(node.Kind);
    static bool IsCanvasShapeKind(string kind) => canvasShapeKinds.Contains(kind);
    static string CanvasShapeDisplayName(string kind) => kind switch
    {
        "text" => "文本框", "image" => "图片", "circle" => "圆形", "triangle" => "三角形", "diamond" => "菱形", "star" => "五角星", _ => "方块"
    };
    GraphicsPath CanvasShapePath(CanvasNode node, Rectangle bounds)
    {
        var path = new GraphicsPath();
        if (node.Kind == "circle" && node.VectorAnchors.Count < 3) { path.AddEllipse(bounds); return path; }
        var points = CanvasShapeVertices(node, bounds);
        if (points.Count < 3) { path.AddRectangle(bounds); return path; }
        var corners = CanvasRoundedCorners(points, node.CornerRadius * canvasZoom);
        if (!corners.Any(corner => corner.IsRounded)) { path.AddPolygon(points.ToArray()); return path; }
        path.StartFigure();
        for (int i = 0; i < corners.Count; i++)
        {
            var current = corners[i]; var next = corners[(i + 1) % corners.Count];
            path.AddLine(current.Exit, next.Entry);
            if (next.IsRounded) path.AddBezier(next.Entry, next.Control1, next.Control2, next.Exit);
        }
        path.CloseFigure();
        return path;
    }
    List<PointF> CanvasShapeVertices(CanvasNode node, Rectangle bounds)
    {
        if (node.VectorAnchors.Count >= 3)
            return node.VectorAnchors.Select(anchor => new PointF(bounds.Left + anchor.X * bounds.Width, bounds.Top + anchor.Y * bounds.Height)).ToList();
        return node.Kind switch
        {
            "triangle" => new List<PointF> { new(bounds.Left + bounds.Width / 2f, bounds.Top), new(bounds.Right, bounds.Bottom), new(bounds.Left, bounds.Bottom) },
            "diamond" => new List<PointF> { new(bounds.Left + bounds.Width / 2f, bounds.Top), new(bounds.Right, bounds.Top + bounds.Height / 2f), new(bounds.Left + bounds.Width / 2f, bounds.Bottom), new(bounds.Left, bounds.Top + bounds.Height / 2f) },
            "star" => Enumerable.Range(0, 10).Select(i => { double angle = -Math.PI / 2 + i * Math.PI / 5; float ratio = i % 2 == 0 ? 1f : 0.382f; return new PointF(bounds.Left + bounds.Width / 2f + (float)Math.Cos(angle) * bounds.Width / 2f * ratio, bounds.Top + bounds.Height / 2f + (float)Math.Sin(angle) * bounds.Height / 2f * ratio); }).ToList(),
            _ => new List<PointF> { new(bounds.Left, bounds.Top), new(bounds.Right, bounds.Top), new(bounds.Right, bounds.Bottom), new(bounds.Left, bounds.Bottom) }
        };
    }
    static List<CanvasRoundedCorner> CanvasRoundedCorners(IReadOnlyList<PointF> points, float requestedRadius)
    {
        int count = points.Count;
        var result = points.Select(point => new CanvasRoundedCorner(point, point, point, point, false)).ToList();
        if (count < 3 || requestedRadius <= 0.5f) return result;
        double area = 0;
        for (int i = 0; i < count; i++) { var a = points[i]; var b = points[(i + 1) % count]; area += a.X * b.Y - b.X * a.Y; }
        float orientation = area >= 0 ? 1f : -1f;
        for (int i = 0; i < count; i++)
        {
            var previous = points[(i + count - 1) % count]; var vertex = points[i]; var next = points[(i + 1) % count];
            float inX = vertex.X - previous.X, inY = vertex.Y - previous.Y, outX = next.X - vertex.X, outY = next.Y - vertex.Y;
            float previousLength = MathF.Sqrt(inX * inX + inY * inY), nextLength = MathF.Sqrt(outX * outX + outY * outY);
            if (previousLength < 0.01f || nextLength < 0.01f) continue;
            float turn = (inX * outY - inY * outX) / (previousLength * nextLength);
            if (turn * orientation <= 0.001f) continue; // 保留凹角，避免圆角翻折
            float ux = -inX / previousLength, uy = -inY / previousLength;
            float vx = outX / nextLength, vy = outY / nextLength;
            float dot = Math.Clamp(ux * vx + uy * vy, -1f, 1f);
            float halfAngle = MathF.Acos(dot) / 2f;
            float tangent = MathF.Tan(halfAngle);
            if (tangent < 0.001f) continue;
            float trim = Math.Min(requestedRadius / tangent, Math.Min(previousLength, nextLength) * 0.49f);
            float radius = trim * tangent;
            float bisectorX = ux + vx, bisectorY = uy + vy;
            float bisectorLength = MathF.Sqrt(bisectorX * bisectorX + bisectorY * bisectorY);
            if (bisectorLength < 0.001f || radius < 0.5f) continue;
            bisectorX /= bisectorLength; bisectorY /= bisectorLength;
            float centerDistance = radius / MathF.Sin(halfAngle);
            var center = new PointF(vertex.X + bisectorX * centerDistance, vertex.Y + bisectorY * centerDistance);
            var entry = new PointF(vertex.X + ux * trim, vertex.Y + uy * trim);
            var exit = new PointF(vertex.X + vx * trim, vertex.Y + vy * trim);
            float startRx = entry.X - center.X, startRy = entry.Y - center.Y, endRx = exit.X - center.X, endRy = exit.Y - center.Y;
            float sweepRadians = orientation * (MathF.PI - MathF.Acos(dot));
            float k = 4f / 3f * MathF.Tan(MathF.Abs(sweepRadians) / 4f);
            float sign = MathF.Sign(sweepRadians);
            var control1 = new PointF(entry.X + sign * -startRy * k, entry.Y + sign * startRx * k);
            var control2 = new PointF(exit.X - sign * -endRy * k, exit.Y - sign * endRx * k);
            result[i] = new CanvasRoundedCorner(entry, exit, control1, control2, true);
        }
        return result;
    }
    List<CanvasAnchorHandle> CanvasAnchorHandles(CanvasNode node, Rectangle bounds)
    {
        var anchors = CanvasAnchors(node);
        var points = anchors.Select(anchor => new PointF(bounds.Left + anchor.X * bounds.Width, bounds.Top + anchor.Y * bounds.Height)).ToList();
        var rounded = CanvasRoundedCorners(points, node.CornerRadius * canvasZoom);
        var handles = new List<CanvasAnchorHandle>(points.Count * 2);
        for (int i = 0; i < rounded.Count; i++)
        {
            if (rounded[i].IsRounded)
            {
                handles.Add(new CanvasAnchorHandle(i, rounded[i].Entry));
                handles.Add(new CanvasAnchorHandle(i, rounded[i].Exit));
            }
            else handles.Add(new CanvasAnchorHandle(i, points[i]));
        }
        return handles;
    }
    // 形状描边：GDI+ 画笔默认居中于路径；内部/外部描边通过裁剪只保留内侧或外侧一半
    void DrawCanvasNodeStroke(Graphics graphics, CanvasNode node, GraphicsPath shape, Color color)
    {
        float width = Math.Max(0.5f, node.StrokeWidth);
        using var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        if (node.StrokeDash) pen.DashStyle = DashStyle.Dash;
        if (node.StrokeAlign == 1) { graphics.DrawPath(pen, shape); return; }
        var state = graphics.Save();
        try
        {
            if (node.StrokeAlign == 0)
            {
                using var inside = new Region(shape);
                graphics.SetClip(inside, CombineMode.Intersect);
            }
            else
            {
                using var outside = new Region(new RectangleF(-1_000_000, -1_000_000, 2_000_000, 2_000_000));
                outside.Exclude(shape);
                graphics.SetClip(outside, CombineMode.Intersect);
            }
            graphics.DrawPath(pen, shape);
        }
        finally { graphics.Restore(state); }
    }
    Region CanvasNodeClipRegion(CanvasNode node)
    {
        var bounds = CanvasBounds(node);
        GraphicsPath path;
        if (IsCanvasShapeKind(node))
        {
            path = CanvasShapePath(node, bounds);
            if (Math.Abs(node.Rotation % 360f) > 0.01f)
            {
                using var rotationMatrix = new Matrix();
                rotationMatrix.RotateAt(node.Rotation, CanvasNodeCenter(bounds), MatrixOrder.Append);
                path.Transform(rotationMatrix);
            }
        }
        else if (node.Kind == "image" && Math.Abs(node.Rotation % 360f) > 0.01f)
        {
            path = new GraphicsPath();
            path.AddPolygon(Enumerable.Range(0, 4).Select(i => CanvasRotationCorner(node, i)).ToArray());
        }
        else
        {
            path = new GraphicsPath();
            path.AddRectangle(bounds);
        }
        using (path) return new Region(path);
    }
    Rectangle CanvasBounds(CanvasNode node) => new((int)Math.Round(node.Position.X * canvasZoom + canvasOffset.X), (int)Math.Round(node.Position.Y * canvasZoom + canvasOffset.Y), Math.Max(1, (int)Math.Round(node.Size.Width * canvasZoom)), Math.Max(1, (int)Math.Round(node.Size.Height * canvasZoom)));
    static PointF RotateCanvasPoint(PointF point, PointF center, float degrees)
    {
        double radians = degrees * Math.PI / 180d;
        float dx = point.X - center.X, dy = point.Y - center.Y;
        return new PointF(center.X + (float)(dx * Math.Cos(radians) - dy * Math.Sin(radians)), center.Y + (float)(dx * Math.Sin(radians) + dy * Math.Cos(radians)));
    }
    static PointF CanvasNodeCenter(Rectangle bounds) => new(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
    bool IsCanvasRotatable(CanvasNode node) => node.Kind is "image" or "text" or "path" || IsCanvasShapeKind(node);
    static float CanvasNodeFontSize(CanvasNode node) => node.FontSize > 0 ? node.FontSize : 10f;
    static bool TryCanvasStoredColor(string? value, out Color color)
    {
        color = Color.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int argb))
        {
            color = Color.FromArgb(argb);
            return true;
        }
        try
        {
            color = ColorTranslator.FromHtml(value);
            return !color.IsEmpty;
        }
        catch (ArgumentException) { return false; }
        catch (FormatException) { return false; }
    }
    Color CanvasNodeTextColor(CanvasNode node, bool placeholder)
    {
        if (placeholder) return Color.FromArgb(165, 160, 153);
        if (TryCanvasStoredColor(node.FontColor, out var color)) return color;
        if(node.Kind=="text" && node.Transparent && darkTheme)return Color.FromArgb(230,233,240);
        return IsCanvasShapeKind(node.Kind) ? Color.FromArgb(167, 107, 71) : Color.FromArgb(70, 67, 65);
    }
    static Color CanvasNodeFillColor(CanvasNode node)
    {
        if (TryCanvasStoredColor(node.FillColor, out var color)) return color;
        return node.Kind == "text" ? Color.White : Color.FromArgb(244, 231, 223);
    }
    Font CanvasNodeFont(CanvasNode node, float zoom)
    {
        var style = FontStyle.Regular;
        if (node.FontBold) style |= FontStyle.Bold;
        if (node.FontItalic) style |= FontStyle.Italic;
        if (node.FontStrikeout) style |= FontStyle.Strikeout;
        string family=string.IsNullOrWhiteSpace(node.Extra.FontFamily)?UiFontName():node.Extra.FontFamily;
        try{return new Font(family, Math.Clamp(CanvasNodeFontSize(node) * zoom, 1, 768), style);}
        catch(ArgumentException){return new Font(UiFontName(),Math.Clamp(CanvasNodeFontSize(node)*zoom,1,768),style);}
    }
    static TextFormatFlags CanvasNodeAlignFlags(CanvasNode node) => node.FontAlign switch
    {
        FontAlignCenter => TextFormatFlags.HorizontalCenter,
        FontAlignRight => TextFormatFlags.Right,
        _ => TextFormatFlags.Default
    };
    static StringAlignment CanvasNodeStringAlignment(CanvasNode node) => node.FontAlign switch
    {
        FontAlignLeft => StringAlignment.Near,
        FontAlignRight => StringAlignment.Far,
        _ => StringAlignment.Center
    };
    static TextFormatFlags CanvasNodeVAlignFlags(CanvasNode node) => node.FontVAlign switch
    {
        FontAlignMiddle => TextFormatFlags.VerticalCenter,
        FontAlignBottom => TextFormatFlags.Bottom,
        _ => TextFormatFlags.Top
    };
    static StringAlignment CanvasNodeVStringAlignment(CanvasNode node) => node.FontVAlign switch
    {
        FontAlignTop => StringAlignment.Near,
        FontAlignBottom => StringAlignment.Far,
        _ => StringAlignment.Center
    };
    PointF CanvasRotationCorner(CanvasNode node, int index)
    {
        var bounds = CanvasBounds(node); var center = CanvasNodeCenter(bounds);
        var corner = index switch { 0 => new PointF(bounds.Left, bounds.Top), 1 => new PointF(bounds.Right, bounds.Top), 2 => new PointF(bounds.Right, bounds.Bottom), _ => new PointF(bounds.Left, bounds.Bottom) };
        return RotateCanvasPoint(corner, center, node.Rotation);
    }
    int CanvasRotationCornerAt(CanvasNode node, Point point, int tolerance = 11)
    {
        if (!IsCanvasRotatable(node) || canvasVectorEditNodeId == node.Id || canvasSelectedIds.Count > 1) return -1;
        for (int i = 0; i < 4; i++)
        {
            var corner = CanvasRotationCorner(node, i);
            if (Math.Abs(corner.X - point.X) <= tolerance && Math.Abs(corner.Y - point.Y) <= tolerance) return i;
        }
        return -1;
    }
    Rectangle CanvasVisualBounds(CanvasNode node)
    {
        if(node.Kind=="path"){var pathBounds=LayerScreenBounds(node);if(!canvasExporting)pathBounds.Inflate(34,34);return pathBounds;}
        var bounds = CanvasBounds(node);
        Rectangle result;
        if (!IsCanvasRotatable(node) || Math.Abs(node.Rotation % 360f) < 0.01f) result = bounds;
        else
        {
            var corners = Enumerable.Range(0, 4).Select(i => CanvasRotationCorner(node, i)).ToArray();
            result = Rectangle.FromLTRB((int)Math.Floor(corners.Min(p => p.X)), (int)Math.Floor(corners.Min(p => p.Y)), (int)Math.Ceiling(corners.Max(p => p.X)), (int)Math.Ceiling(corners.Max(p => p.Y)));
        }
        if (IsCanvasShapeKind(node) && node.StrokeWidth > 0.01f && node.StrokeAlign == 2)
        {
            int pad = (int)Math.Ceiling(node.StrokeWidth / 2f) + 2;
            result.Inflate(pad, pad);
        }
        if (node.VectorAnchors.Count >= 3)
        {
            var anchorPoints = CanvasAnchors(node).Select(anchor =>
            {
                var point = new PointF(bounds.Left + anchor.X * bounds.Width, bounds.Top + anchor.Y * bounds.Height);
                return RotateCanvasPoint(point, CanvasNodeCenter(bounds), node.Rotation);
            }).ToArray();
            int pad = canvasVectorEditNodeId == node.Id ? 12 : 2;
            var anchorBounds = Rectangle.FromLTRB((int)Math.Floor(anchorPoints.Min(point => point.X)), (int)Math.Floor(anchorPoints.Min(point => point.Y)), (int)Math.Ceiling(anchorPoints.Max(point => point.X)), (int)Math.Ceiling(anchorPoints.Max(point => point.Y)));
            anchorBounds.Inflate(pad, pad);
            result = Rectangle.Union(result, anchorBounds);
        }
        if(!canvasExporting && (canvasSelectedIds.Contains(node.Id)||canvasHoveredNode==node))result.Inflate(34,34);
        return result;
    }
    void DrawCanvas(Graphics graphics, Rectangle clip) => DrawLayerWorkspace(graphics,clip);
    void DrawLegacyCanvas(Graphics graphics, Rectangle clip)
    {
        float spacing = 28 * canvasZoom;
        while(spacing<14)spacing*=2;
        graphics.Clear(canvasSurface.BackColor);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (!canvasExporting)
        {
            if(canvasWorkspace.Grid) using (var brush = new SolidBrush(darkTheme ? Color.FromArgb(65, 65, 69) : Color.FromArgb(218, 218, 221)))
            {
                float ox = ((canvasOffset.X % spacing) + spacing) % spacing;
                float oy = ((canvasOffset.Y % spacing) + spacing) % spacing;
                for (float y = oy + (float)Math.Floor((clip.Top - oy) / (double)spacing) * spacing; y < clip.Bottom; y += spacing)
                    for (float x = ox + (float)Math.Floor((clip.Left - ox) / (double)spacing) * spacing; x < clip.Right; x += spacing)
                        graphics.FillEllipse(brush, x - 1, y - 1, 2, 2);
            }
            if (canvasVectorEditNodeId.HasValue)
            {
                using (var hintFont = new Font(UiFontName(), 9))
                using (var hintBrush = new SolidBrush(Color.FromArgb(145, 140, 132)))
                    graphics.DrawString("拖动锚点调整轮廓  ·  Esc 退出锚点编辑", hintFont, hintBrush, 28, 22);
            }
        }
        List<(CanvasNode Node, int Index)> drawNodes;
        List<CanvasLink> drawLinks;
        Dictionary<Guid, CanvasNode> nodesById;
        if (canvasExporting)
        {
            nodesById = canvasNodes.ToDictionary(node => node.Id);
            drawNodes = canvasNodes.Select((node, index) => (node, index))
                .Where(item => canvasExportNodeIds == null || canvasExportNodeIds.Contains(item.node.Id)).ToList();
            drawLinks = canvasExportNodeIds == null
                ? canvasLinks
                : canvasLinks.Where(link => canvasExportNodeIds.Contains(link.From) && canvasExportNodeIds.Contains(link.To)).ToList();
        }
        else
        {
            EnsureCanvasPaintCache();
            nodesById = canvasPaintNodeMap!; drawNodes = canvasPaintNodes!; drawLinks = canvasPaintLinks!;
        }
        foreach (var link in drawLinks)
        {
            if (!nodesById.TryGetValue(link.From, out var from) || !nodesById.TryGetValue(link.To, out var to)) continue;
            if(from.Hidden || to.Hidden)continue;
            if (!CanvasLinkBounds(link, from, to).IntersectsWith(clip)) continue;
            DrawCanvasLink(graphics, link, from, to, false, PointF.Empty, canvasInlineEditLink == link ? "" : link.Label);
        }
        if (canvasWiring && wireStartNode != null) DrawCanvasLink(graphics, null, wireStartNode, wireTargetNode, true, wireEnd);
        foreach (var (node, nodeIndex) in drawNodes)
        {
            if (node.Hidden) continue;
            var bounds = CanvasBounds(node);
            if (!CanvasVisualBounds(node).IntersectsWith(clip)) continue;
            Region? nodeClip = null;
            if (node.ClipMask)
            {
                // PS 剪贴蒙版语义：本图层只被正下方的一个图层裁剪；基底缺失或被隐藏时本图层不显示
                var baseNode = nodeIndex > 0 ? canvasNodes[nodeIndex - 1] : null;
                if (baseNode == null || baseNode.Hidden) continue;
                nodeClip = CanvasNodeClipRegion(baseNode);
            }
            var nodeGraphicsState = graphics.Save();
            if (nodeClip != null) graphics.SetClip(nodeClip, CombineMode.Replace);
            if (IsCanvasRotatable(node) && Math.Abs(node.Rotation % 360f) > 0.01f)
            {
                var center = CanvasNodeCenter(bounds);
                using var rotationMatrix = new Matrix();
                rotationMatrix.RotateAt(node.Rotation, center, MatrixOrder.Append);
                graphics.MultiplyTransform(rotationMatrix, MatrixOrder.Append);
            }
            bool isText = node.Kind == "text";
            int nodeAlpha = (int)Math.Round(255f * Math.Clamp(node.Opacity, 0f, 1f));
            int fillAlpha = (int)Math.Round(nodeAlpha * Math.Clamp(node.FillOpacity, 0f, 1f));
            Color NodeAlpha(Color color) => Color.FromArgb(color.A * nodeAlpha / 255, color);
            Color NodeFillAlpha(Color color) => Color.FromArgb(color.A * fillAlpha / 255, color);
            using var fill = new SolidBrush(NodeFillAlpha(IsCanvasShapeKind(node) || isText ? CanvasNodeFillColor(node) : Color.FromArgb(244, 231, 223)));
            bool isSelected = !canvasExporting && canvasSelectedIds.Contains(node.Id);
            var outline = canvasWiring && wireTargetNode == node ? Color.FromArgb(83, 121, 99) : isSelected ? canvasSelectionColor : Color.FromArgb(190, 186, 179);
            // 选中/连线目标时边框保持不透明，避免低透明度时看不清选中框
            using var border = new Pen(isSelected || (canvasWiring && wireTargetNode == node) ? outline : NodeAlpha(outline), isSelected || (canvasWiring && wireTargetNode == node) ? 2 : 1);
            if (IsCanvasShapeKind(node))
            {
                using var shape = CanvasShapePath(node, bounds);
                graphics.FillPath(fill, shape);
                if (node.StrokeWidth > 0.01f)
                {
                    // 选中提示画在自定义描边下方，不能用橙色虚线覆盖用户选择的颜色。
                    if (!canvasExporting && (isSelected || (canvasWiring && wireTargetNode == node)))
                    {
                        using var indicator = new Pen(Color.FromArgb(145, outline), Math.Max(3f, node.StrokeWidth + 4f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                        graphics.DrawPath(indicator, shape);
                    }
                    DrawCanvasNodeStroke(graphics, node, shape, NodeAlpha(CanvasNodeStrokeColor(node)));
                }
                else if (!canvasExporting && (isSelected || (canvasWiring && wireTargetNode == node)))
                {
                    // 无描边时不绘制默认轮廓；仅在编辑画布中保留选中/连线状态提示。
                    using var indicator = new Pen(NodeAlpha(outline), 1.5f) { DashStyle = DashStyle.Dash };
                    graphics.DrawPath(indicator, shape);
                }
            }
            else
            {
                if (!isText || !node.Transparent) graphics.FillRectangle(fill, bounds);
                if (node.Kind != "image" && (!isText || !node.Transparent || isSelected || (canvasWiring && wireTargetNode == node))) graphics.DrawRectangle(border, bounds);
            }
            if (node.Kind == "image")
            {
                if (node.Bitmap != null)
                {
                    if (fillAlpha >= 255) graphics.DrawImage(node.Bitmap, bounds);
                    else
                    {
                        using var imageAttributes = new ImageAttributes();
                        imageAttributes.SetColorMatrix(new ColorMatrix { Matrix33 = fillAlpha / 255f });
                        graphics.DrawImage(node.Bitmap, bounds, 0, 0, node.Bitmap.Width, node.Bitmap.Height, GraphicsUnit.Pixel, imageAttributes);
                    }
                }
                else
                {
                    using var placeholder = new SolidBrush(NodeFillAlpha(Color.FromArgb(238, 235, 229)));
                    graphics.FillRectangle(placeholder, bounds);
                    TextRenderer.DrawText(graphics, "图片无法加载", Font, bounds, NodeFillAlpha(Color.FromArgb(145, 140, 132)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                graphics.DrawRectangle(border, bounds);
            }
            else if (isText)
            {
                using var contentFont = CanvasNodeFont(node, canvasZoom);
                int padding = Math.Max(2, (int)Math.Round(12 * canvasZoom));
                var textBounds = new Rectangle(bounds.X + padding, bounds.Y + padding, Math.Max(1, bounds.Width - padding * 2), Math.Max(1, bounds.Height - padding * 2));
                if (canvasInlineEditNodeId != node.Id)
                {
                    using var format = new StringFormat { Alignment = CanvasNodeStringAlignment(node), LineAlignment = CanvasNodeVStringAlignment(node), Trimming = StringTrimming.EllipsisCharacter, FormatFlags=StringFormatFlags.LineLimit };
                    using var textBrush = new SolidBrush(NodeFillAlpha(CanvasNodeTextColor(node, string.IsNullOrEmpty(node.Content))));
                    graphics.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    graphics.DrawString(string.IsNullOrEmpty(node.Content)?"双击编辑文字":node.Content, contentFont, textBrush, textBounds, format);
                }
            }
            if (!canvasExporting && canvasVectorEditNodeId == node.Id)
            {
                using var anchorFill = new SolidBrush(Color.White);
                using var anchorPen = new Pen(Color.FromArgb(190, 105, 80), 2);
                foreach (var anchor in CanvasAnchorHandles(node, bounds))
                {
                    int radius = 5;
                    var point = anchor.Point;
                    var handle = new RectangleF(point.X - radius, point.Y - radius, radius * 2, radius * 2);
                    graphics.FillRectangle(anchorFill, handle);
                    graphics.DrawRectangle(anchorPen, handle.X, handle.Y, handle.Width, handle.Height);
                }
                if (canvasAnchorPreviewInsertIndex >= 0)
                {
                    const float previewRadius = 8f;
                    var preview = new RectangleF(canvasAnchorPreviewPoint.X - previewRadius, canvasAnchorPreviewPoint.Y - previewRadius, previewRadius * 2, previewRadius * 2);
                    using var previewFill = new SolidBrush(Color.FromArgb(190, 105, 80));
                    using var previewOutline = new Pen(Color.White, 2f);
                    graphics.FillEllipse(previewFill, preview);
                    graphics.DrawEllipse(previewOutline, preview);
                    if (canvasAnchorPreviewMidpoint)
                    {
                        using var halfFont = new Font(UiFontName(), 8f, FontStyle.Bold);
                        using var halfBrush = new SolidBrush(Color.White);
                        using var halfFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                        graphics.DrawString("½", halfFont, halfBrush, preview, halfFormat);
                    }
                }
            }
            else if (!canvasExporting && nativeReady && canvasSelectedIds.Contains(node.Id))
            {
                DrawCanvasWorkspaceSelection(graphics,node,bounds);
            }
            else if (!canvasExporting && selectedCanvasNode == node && canvasSelectedIds.Count <= 1)
            {
                // 四角旋转圆点（形状右下角是缩放角，不画旋转点）
                if (IsCanvasRotatable(node))
                {
                    using var rotateBrush = new SolidBrush(Color.White);
                    using var rotatePen = new Pen(Color.FromArgb(190, 105, 80), 1.5f);
                    for (int cornerIndex = 0; cornerIndex < 4; cornerIndex++)
                    {
                        if (IsCanvasShapeKind(node) && cornerIndex == 2) continue;
                        var cornerPos = CanvasRotationCorner(node, cornerIndex);
                        var handle = new RectangleF(cornerPos.X - 4.5f, cornerPos.Y - 4.5f, 9, 9);
                        graphics.FillEllipse(rotateBrush, handle);
                        graphics.DrawEllipse(rotatePen, handle);
                    }
                }
                var grip = ResizeGripBounds(node, bounds);
                using var gripBrush = new SolidBrush(Color.FromArgb(205, 199, 190));
                using var gripPen = new Pen(Color.FromArgb(120, 112, 102), 1);
                graphics.FillRectangle(gripBrush, grip);
                graphics.DrawLine(gripPen, grip.Left + 3, grip.Bottom - 1, grip.Right - 1, grip.Top + 3);
                graphics.DrawLine(gripPen, grip.Left + 7, grip.Bottom - 1, grip.Right - 1, grip.Top + 7);
            }
            if (!canvasExporting && node.Locked)
            {
                using var lockBrush = new SolidBrush(Color.FromArgb(200, 190, 105, 80));
                graphics.FillRectangle(lockBrush, bounds.Right - 16, bounds.Top + 4, 14, 14);
            }
            if (!canvasExporting && (canvasHoveredNode == node || canvasSelectedIds.Contains(node.Id) || canvasWiring))
            {
                int portSize = 9;
                foreach (bool output in new[] { false, true })
                {
                    var portPoint = new PointF(output ? bounds.Right + 18 : bounds.Left - 18, bounds.Top + bounds.Height / 2f);
                    var portBounds = new RectangleF(portPoint.X - portSize / 2f, portPoint.Y - portSize / 2f, portSize, portSize);
                    var portColor = output ? Color.FromArgb(190, 105, 80) : Color.FromArgb(83, 121, 99);
                    using var portFill = new SolidBrush(wireTargetNode == node && output != wireStartedAtOutput ? Color.FromArgb(83, 121, 99) : portColor);
                    using var portOutline = new Pen(Color.White, 2);
                    graphics.FillEllipse(portFill, portBounds); graphics.DrawEllipse(portOutline, portBounds);
                }
            }
            graphics.Restore(nodeGraphicsState);
            nodeClip?.Dispose();
        }
        if (canvasMarqueeSelecting && !canvasMarqueeBounds.IsEmpty)
        {
            using var marqueeFill = new SolidBrush(Color.FromArgb(35, 83, 121, 99));
            using var marqueePen = new Pen(Color.FromArgb(83, 121, 99), 1) { DashStyle = DashStyle.Dash };
            graphics.FillRectangle(marqueeFill, canvasMarqueeBounds);
            graphics.DrawRectangle(marqueePen, canvasMarqueeBounds);
        }
        if (canvasSnapGuides.Count > 0)
        {
            using var guidePen = new Pen(Color.FromArgb(255, 59, 59), 1.4f);
            foreach (var (guideStart, guideEnd) in canvasSnapGuides)
            {
                var a = new PointF(guideStart.X * canvasZoom + canvasOffset.X, guideStart.Y * canvasZoom + canvasOffset.Y);
                var b = new PointF(guideEnd.X * canvasZoom + canvasOffset.X, guideEnd.Y * canvasZoom + canvasOffset.Y);
                graphics.DrawLine(guidePen, a, b);
            }
        }
        DrawCanvasWorkspaceOverlay(graphics);
    }
    void CanvasMouseWheel(object? sender, MouseEventArgs e)
        => HandleCanvasWheel(e,ModifierKeys);
    void HandleCanvasWheel(MouseEventArgs e,Keys modifiers)
    {
        if (!organizeSelected || e.Delta == 0 || canvasDrawing || canvasResizeNode != null) return;
        CommitCanvasInlineEdit();
        if((modifiers & (Keys.Control|Keys.Alt))!=0)ZoomCanvasAt(canvasZoom*(float)Math.Pow(1.12,e.Delta/120d),e.Location);
        else
        {
            float delta=e.Delta/120f*64;
            canvasOffset=(modifiers & Keys.Shift)!=0 ? new PointF(canvasOffset.X+delta,canvasOffset.Y) : new PointF(canvasOffset.X,canvasOffset.Y+delta);
            CanvasViewportChanged();
        }
    }
    void DrawCanvasLink(Graphics graphics, CanvasLink? link, CanvasNode from, CanvasNode? to, bool temporary, PointF endPoint, string label = "")
    {
        var (start, c1, c2, end) = CanvasLinkCurve(from, to, endPoint, link);
        bool selected = !temporary && !canvasExporting && link != null && selectedCanvasLink == link;
        // 选中状态只增加外描边，不覆盖连线自身颜色；否则属性面板改色后仍会显示橙色。
        Color color = temporary ? Color.FromArgb(190, 105, 80) : link != null ? CanvasLinkColor(link) : CanvasLinkDefaultColor;
        int arrow = link?.Arrow ?? 0;
        bool endArrow = !temporary && (arrow == 1 || arrow == 3);
        bool startArrow = !temporary && (arrow == 2 || arrow == 3);
        if (selected)
        {
            var halo = darkTheme ? Color.FromArgb(150, 255, 255, 255) : Color.FromArgb(150, 35, 35, 40);
            using var selectionPen = new Pen(halo, 7f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawBezier(selectionPen, start, c1, c2, end);
        }
        using var pen = new Pen(color, selected ? 4f : 3f) { DashStyle = DashStyle.Solid, StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawBezier(pen, start, c1, c2, end);
        using var endpointBrush = new SolidBrush(color);
        if (!startArrow) graphics.FillEllipse(endpointBrush, start.X - 4, start.Y - 4, 8, 8);
        if (!endArrow) graphics.FillEllipse(endpointBrush, end.X - 4, end.Y - 4, 8, 8);
        if (startArrow) DrawCanvasLinkArrowHead(graphics, start, new PointF(start.X - c1.X, start.Y - c1.Y), color);
        if (endArrow) DrawCanvasLinkArrowHead(graphics, end, new PointF(end.X - c2.X, end.Y - c2.Y), color);
        if (!temporary && !string.IsNullOrWhiteSpace(label))
        {
            float mx = (start.X + 3 * c1.X + 3 * c2.X + end.X) / 8f, my = (start.Y + 3 * c1.Y + 3 * c2.Y + end.Y) / 8f;
            using var font = new Font(UiFontName(), Math.Clamp(10 * canvasZoom, 6, 36));
            var size = graphics.MeasureString(label, font);
            float bx = mx - size.Width / 2f - 4, by = my - size.Height / 2f - 2;
            using var bg = new SolidBrush(Color.FromArgb(250, 249, 246));
            using var fg = new SolidBrush(Color.FromArgb(90, 87, 85));
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            graphics.FillRectangle(bg, bx, by, size.Width + 8, size.Height + 4);
            graphics.DrawString(label, font, fg, mx, my, format);
        }
    }
    // 实心（方形）箭头：tip 为箭头尖端，direction 为朝向，三角形底边垂直于连线方向
    void DrawCanvasLinkArrowHead(Graphics graphics, PointF tip, PointF direction, Color color)
    {
        float dx = direction.X, dy = direction.Y;
        float length = (float)Math.Sqrt(dx * dx + dy * dy);
        if (length < 0.001f) return;
        dx /= length; dy /= length;
        float size = 8.5f * Math.Clamp(canvasZoom, 0.5f, 4f);
        float back = size * 1.5f, half = size * 0.8f;
        var baseCenter = new PointF(tip.X - dx * back, tip.Y - dy * back);
        float perpX = -dy, perpY = dx;
        var points = new[]
        {
            tip,
            new PointF(baseCenter.X + perpX * half, baseCenter.Y + perpY * half),
            new PointF(baseCenter.X - perpX * half, baseCenter.Y - perpY * half)
        };
        using var brush = new SolidBrush(color);
        graphics.FillPolygon(brush, points);
    }
    (PointF Start, PointF Control1, PointF Control2, PointF End) CanvasLinkCurve(CanvasNode from, CanvasNode? to, PointF endPoint, CanvasLink? link = null)
    {
        var fromBounds = CanvasBounds(from);
        PointF targetCenter = to == null ? endPoint : new PointF(CanvasBounds(to).Left + CanvasBounds(to).Width / 2f, CanvasBounds(to).Top + CanvasBounds(to).Height / 2f);
        int laneIndex = -1;
        int pairCount = 0;
        if (to != null)
        {
            var pair = canvasLinks.Where(other => (other.From == from.Id && other.To == to.Id) || (other.From == to.Id && other.To == from.Id)).ToList();
            pairCount = pair.Count;
            laneIndex = link == null ? pair.Count : pair.FindIndex(other => ReferenceEquals(other, link));
            if (link == null) pairCount++;
        }
        float lane = pairCount > 1 && laneIndex >= 0 ? (laneIndex == 0 ? -1f : 1f) : 0f;
        var fromAnchor = CanvasLinkEdgeAnchor(from, targetCenter, lane);
        var start = fromAnchor.Point;
        CanvasAnchor? toAnchor = to == null ? null : CanvasLinkEdgeAnchor(to, new PointF(fromBounds.Left + fromBounds.Width / 2f, fromBounds.Top + fromBounds.Height / 2f), lane);
        PointF end = toAnchor?.Point ?? targetCenter;
        float dx = end.X - start.X, dy = end.Y - start.Y;
        float bend = Math.Max(42, (float)Math.Sqrt(dx * dx + dy * dy) * 0.38f);
        var c1 = new PointF(start.X + fromAnchor.Normal.X * bend, start.Y + fromAnchor.Normal.Y * bend);
        var c2 = toAnchor is { } anchor ? new PointF(end.X + anchor.Normal.X * bend, end.Y + anchor.Normal.Y * bend) : end;
        return (start, c1, c2, end);
    }
    CanvasAnchor CanvasLinkEdgeAnchor(CanvasNode node, PointF toward, float lane)
    {
        var bounds = CanvasBounds(node);
        var center = CanvasNodeCenter(bounds);
        var localToward = IsCanvasRotatable(node) ? RotateCanvasPoint(toward, center, -node.Rotation) : toward;
        var facing = CanvasEdgeAnchor(bounds, localToward);
        CanvasAnchor anchor;
        if (Math.Abs(facing.Normal.X) > 0.5f)
        {
            float tangentOffset = Math.Min(48f * canvasZoom, bounds.Height * 0.22f) * lane;
            float y = center.Y + tangentOffset;
            float side = facing.Normal.X;
            anchor = new CanvasAnchor(new PointF(side < 0 ? bounds.Left : bounds.Right, y), new PointF(side, 0));
        }
        else
        {
            float tangentOffset = Math.Min(48f * canvasZoom, bounds.Width * 0.22f) * lane;
            float x = center.X + tangentOffset;
            float side = facing.Normal.Y;
            anchor = new CanvasAnchor(new PointF(x, side < 0 ? bounds.Top : bounds.Bottom), new PointF(0, side));
        }
        return RotateCanvasAnchor(node, anchor);
    }
    Rectangle CanvasLinkBounds(CanvasLink link)
    {
        var from = canvasNodes.FirstOrDefault(node => node.Id == link.From);
        var to = canvasNodes.FirstOrDefault(node => node.Id == link.To);
        if (from == null || to == null) return Rectangle.Empty;
        return CanvasLinkBounds(link, from, to);
    }
    Rectangle CanvasLinkBounds(CanvasLink link, CanvasNode from, CanvasNode to)
    {
        var (p0, p1, p2, p3) = CanvasLinkCurve(from, to, PointF.Empty, link);
        float minX = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        float maxX = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
        float minY = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        float maxY = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));
        var bounds = Rectangle.FromLTRB((int)Math.Floor(minX), (int)Math.Floor(minY), (int)Math.Ceiling(maxX), (int)Math.Ceiling(maxY));
        bounds.Inflate(string.IsNullOrEmpty(link.Label) ? 8 : 120, string.IsNullOrEmpty(link.Label) ? 8 : 30);
        return bounds;
    }
    static CanvasAnchor CanvasEdgeAnchor(Rectangle bounds, PointF toward)
    {
        var center = new PointF(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
        float dx = toward.X - center.X, dy = toward.Y - center.Y;
        if (Math.Abs(dx) < 0.01f && Math.Abs(dy) < 0.01f) return new CanvasAnchor(center, new PointF(1, 0));
        float halfWidth = bounds.Width / 2f, halfHeight = bounds.Height / 2f;
        float tx = Math.Abs(dx) < 0.01f ? float.PositiveInfinity : halfWidth / Math.Abs(dx);
        float ty = Math.Abs(dy) < 0.01f ? float.PositiveInfinity : halfHeight / Math.Abs(dy);
        if (tx <= ty)
        {
            float side = Math.Sign(dx);
            return new CanvasAnchor(new PointF(center.X + side * halfWidth, center.Y + dy * tx), new PointF(side, 0));
        }
        float verticalSide = Math.Sign(dy);
        return new CanvasAnchor(new PointF(center.X + dx * ty, center.Y + verticalSide * halfHeight), new PointF(0, verticalSide));
    }
    CanvasAnchor RotateCanvasAnchor(CanvasNode node, CanvasAnchor anchor)
    {
        if (!IsCanvasRotatable(node) || Math.Abs(node.Rotation % 360f) < 0.01f) return anchor;
        var center = CanvasNodeCenter(CanvasBounds(node));
        var point = RotateCanvasPoint(anchor.Point, center, node.Rotation);
        double radians = node.Rotation * Math.PI / 180d;
        var normal = new PointF((float)(anchor.Normal.X * Math.Cos(radians) - anchor.Normal.Y * Math.Sin(radians)), (float)(anchor.Normal.X * Math.Sin(radians) + anchor.Normal.Y * Math.Cos(radians)));
        return new CanvasAnchor(point, normal);
    }
    CanvasNode? CanvasNodeAt(Point point)
    {
        for (int i = canvasNodes.Count - 1; i >= 0; i--)
        {
            var node = canvasNodes[i]; if (node.Hidden || node.Locked) continue;
            if (IsCanvasShapeKind(node))
            {
                using var shapeRegion = CanvasNodeClipRegion(node);
                if (shapeRegion.IsVisible(point)) return node;
                continue;
            }
            var bounds = CanvasBounds(node);
            var hitPoint = IsCanvasRotatable(node) ? RotateCanvasPoint(point, CanvasNodeCenter(bounds), -node.Rotation) : point;
            if(node.Kind=="path")
            {
                using var path=LayerVectorPath(node,bounds);
                using var stroke=new Pen(Color.Black,Math.Max(10,node.StrokeWidth*canvasZoom+6));
                if(path.IsOutlineVisible(hitPoint,stroke)||(node.Extra.Closed&&node.Extra.FillPath&&path.IsVisible(hitPoint)))return node;
                continue;
            }
            if (bounds.Contains(Point.Round(hitPoint))) return node;
        }
        return null;
    }
    PointF CanvasPortPoint(CanvasNode node, bool output)
    {
        var bounds = CanvasBounds(node);
        var center = new PointF(output ? bounds.Right + 18 : bounds.Left - 18, bounds.Top + bounds.Height / 2f);
        return IsCanvasRotatable(node) ? RotateCanvasPoint(center, CanvasNodeCenter(bounds), node.Rotation) : center;
    }
    CanvasConnectionPort? CanvasConnectionPortAt(Point point)
    {
        const float hitRadius = 12f;
        for (int i = canvasNodes.Count - 1; i >= 0; i--)
        {
            var node = canvasNodes[i];
            if (node.Hidden || node.Locked || !IsCanvasRotatable(node) && node.Kind != "text") continue;
            var bounds = CanvasBounds(node); var center = CanvasNodeCenter(bounds);
            var local = IsCanvasRotatable(node) ? RotateCanvasPoint(point, center, -node.Rotation) : point;
            foreach (bool output in new[] { false, true })
            {
                var port = new PointF(output ? bounds.Right + 18 : bounds.Left - 18, bounds.Top + bounds.Height / 2f);
                float dx = local.X - port.X, dy = local.Y - port.Y;
                if (dx * dx + dy * dy <= hitRadius * hitRadius)
                    return new CanvasConnectionPort(node, output, CanvasPortPoint(node, output));
            }
        }
        return null;
    }
    // 缩放手柄热区：图片内移到距角点 21px 处，给右下角旋转圆点留出独立热区；形状保持贴角
    Rectangle ResizeGripBounds(CanvasNode node, Rectangle bounds) => node.Kind == "image"
        ? new Rectangle(bounds.Right - 21, bounds.Bottom - 21, 12, 12)
        : new Rectangle(bounds.Right - 16, bounds.Bottom - 16, 16, 16);
    bool IsResizeGrip(CanvasNode node, Point point)
    {
        var bounds = CanvasBounds(node);
        var localPoint = IsCanvasRotatable(node) ? RotateCanvasPoint(point, CanvasNodeCenter(bounds), -node.Rotation) : point;
        return ResizeGripBounds(node, bounds).Contains(Point.Round(localPoint));
    }
    bool IsSquareResizeCorner(CanvasNode node, Point point) => IsCanvasShapeKind(node) && CanvasRotationCornerAt(node, point) == 2;
    void CanvasMouseDownCore(object? sender, MouseEventArgs e)
    {
        if(nativeReady && CanvasWorkspaceMouseDown(e))return;
        if (canvasInlineEditor.Visible && !canvasInlineEditor.Bounds.Contains(e.Location)) CommitCanvasInlineEdit();
        if (e.Button == MouseButtons.Middle)
        {
            canvasPanning = true;
            canvasPanLast = e.Location;
            canvasSurface.Capture = true;
            UpdateCanvasCursor();
            return;
        }
        if (e.Button == MouseButtons.Right)
        {
            wireStartNode = CanvasNodeAt(e.Location);
            if (wireStartNode == null) { ShowCanvasContextMenu(e.Location); return; }
            var rightClickGroup = CanvasGroupMembers(wireStartNode);
            if (!rightClickGroup.All(node => canvasSelectedIds.Contains(node.Id))) SetCanvasSelection(rightClickGroup);
            else selectedCanvasNode = wireStartNode;
            wireStartPoint = e.Location;
            wireEnd = e.Location;
            wireTargetNode = null;
            wireStartedAtOutput = true;
            wireButton = MouseButtons.Right;
            canvasWiring = false;
            canvasSurface.Capture = true;
            canvasSurface.Invalidate();
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        if (canvasSpaceDown) { canvasPanning = true; canvasPanLast = e.Location; canvasSurface.Capture = true; UpdateCanvasCursor(); return; }
        if ((ModifierKeys & Keys.Alt) == 0 && !canvasVectorEditNodeId.HasValue && CanvasConnectionPortAt(e.Location) is CanvasConnectionPort port)
        {
            wireStartNode = port.Node; wireStartedAtOutput = port.IsOutput; wireButton = MouseButtons.Left;
            wireStartPoint = e.Location; wireEnd = port.Point; wireTargetNode = null; canvasWiring = true;
            canvasSurface.Capture = true; canvasSurface.Cursor = Cursors.Cross; canvasSurface.Invalidate(); return;
        }
        var clickedNode = CanvasNodeAt(e.Location);
        if (clickedNode == null && selectedCanvasNode != null && canvasSelectedIds.Count == 1 && CanvasRotationCornerAt(selectedCanvasNode, e.Location, 9) >= 0) clickedNode = selectedCanvasNode;
        if (canvasVectorEditNodeId is Guid editId)
        {
            var editNode = canvasNodes.FirstOrDefault(node => node.Id == editId);
            PointF hitHandle = PointF.Empty;
            int anchorIndex = editNode == null ? -1 : CanvasAnchorAt(editNode, e.Location, out hitHandle);
            if (editNode != null && anchorIndex >= 0)
            {
                ClearCanvasAnchorInsertionPreview();
                var rawAnchor = CanvasAnchorScreenPoint(editNode, anchorIndex);
                canvasAnchorDragOffset = new PointF(rawAnchor.X - hitHandle.X, rawAnchor.Y - hitHandle.Y);
                canvasAnchorDragging = true; canvasAnchorIndex = anchorIndex; canvasNodeChanged = false; canvasNodeHistoryPushed = false;
                canvasSurface.Capture = true; canvasSurface.Cursor = Cursors.Cross; return;
            }
            if (editNode != null && TryGetCanvasAnchorInsertion(editNode, e.Location, out int insertIndex, out var normalized, out _, out _))
            {
                PushCanvasUndo();
                editNode.VectorAnchors.Insert(insertIndex, normalized);
                ClearCanvasAnchorInsertionPreview();
                InvalidateCanvasPaintCache();
                SaveCanvas();
                canvasSurface.Invalidate();
                canvasSurface.Cursor = Cursors.Cross;
                return;
            }
            if (editNode == null || clickedNode?.Id != editId)
            {
                canvasVectorEditNodeId = null;
                ClearCanvasAnchorInsertionPreview();
                canvasSurface.Invalidate();
            }
        }
        if ((ModifierKeys & Keys.Alt) == Keys.Alt && clickedNode == null)
        {
            canvasCutting = true; canvasCutChanged = false; canvasCutLast = e.Location; canvasSurface.Capture = true;
            CutCanvasLinks(e.Location, e.Location); UpdateCanvasCursor(); return;
        }
        if (clickedNode == null)
        {
            var clickedLink = CanvasLinkAt(e.Location);
            if (clickedLink != null)
            {
                canvasNodeDragging = canvasNodeResizing = false;
                canvasAltClonePending = false;
                SetCanvasLinkSelection(clickedLink);
                canvasSurface.Capture = true;
                if (canvasLayersPanel.Visible) RefreshCanvasLayers();
                return;
            }
            canvasNodeDragging = canvasNodeResizing = false;
            canvasAltClonePending = false;
            canvasMarqueeSelecting = true;
            canvasMarqueeShift = (ModifierKeys & Keys.Shift) == Keys.Shift;
            if (!canvasMarqueeShift) SetCanvasSelection(Array.Empty<CanvasNode>());
            canvasMarqueeStart = canvasMarqueeEnd = e.Location;
            canvasMarqueeBounds = Rectangle.Empty;
            canvasSurface.Capture = true;
            canvasSurface.Invalidate();
            if (canvasLayersPanel.Visible) RefreshCanvasLayers();
            return;
        }
        if ((ModifierKeys & Keys.Shift) == Keys.Shift && !IsSquareResizeCorner(clickedNode, e.Location) && !IsResizeGrip(clickedNode, e.Location))
        {
            var toggleNodes = CanvasGroupMembers(clickedNode);
            var selected = CanvasSelectedNodes();
            if (toggleNodes.All(node => canvasSelectedIds.Contains(node.Id))) selected.RemoveAll(node => toggleNodes.Any(toggle => toggle.Id == node.Id));
            else foreach (var node in toggleNodes) if (!selected.Any(item => item.Id == node.Id)) selected.Add(node);
            SetCanvasSelection(selected);
            canvasSurface.Invalidate();
            if (canvasLayersPanel.Visible) RefreshCanvasLayers();
            UpdateCanvasCursor();
            return;
        }
        var clickedGroup = CanvasGroupMembers(clickedNode);
        if (!clickedGroup.All(node => canvasSelectedIds.Contains(node.Id))) SetCanvasSelection(clickedGroup);
        selectedCanvasNode = clickedNode;
        UpdateCanvasProps();
        OpenNativePropertiesForSelection();
        if (selectedCanvasNode != null && selectedCanvasNode.Locked) { canvasSurface.Invalidate(); return; }
        if (canvasLayersPanel.Visible) RefreshCanvasLayers();
        int rotationCorner = !nativeReady && canvasSelectedIds.Count == 1 ? CanvasRotationCornerAt(selectedCanvasNode!, e.Location, 9) : -1;
        if (rotationCorner >= 0 && selectedCanvasNode is { } rotatingNode && !(IsCanvasShapeKind(rotatingNode) && rotationCorner == 2))
        {
            var center = CanvasNodeCenter(CanvasBounds(rotatingNode));
            canvasRotationStartAngle = (float)(Math.Atan2(e.Y - center.Y, e.X - center.X) * 180d / Math.PI);
            canvasNodeStartRotation = rotatingNode.Rotation;
            canvasNodeRotating = true; canvasNodeDragging = canvasNodeResizing = false;
            canvasMouseDown = e.Location; canvasNodeChanged = canvasNodeHistoryPushed = false;
            canvasSurface.Capture = true; canvasSurface.Cursor = rotateCursor; canvasSurface.Invalidate(); return;
        }
        canvasSelectionStartPositions.Clear();
        foreach (var node in CanvasSelectedNodes()) canvasSelectionStartPositions[node.Id] = node.Position;
        canvasNodeResizing = canvasSelectedIds.Count == 1 && (IsResizeGrip(selectedCanvasNode!, e.Location) || IsSquareResizeCorner(selectedCanvasNode!, e.Location));
        canvasNodeDragging = !canvasNodeResizing;
        if (canvasNodeResizing) canvasNodeDragging = false;
        canvasAltClonePending = canvasNodeDragging && canvasSelectedIds.Count > 0 && (ModifierKeys & Keys.Alt) == Keys.Alt;
        if (selectedCanvasNode != null)
        {
            canvasMouseDown = e.Location;
            canvasNodeStartPosition = selectedCanvasNode.Position;
            canvasNodeStartSize = selectedCanvasNode.Size;
            canvasNodeChanged = false;
            canvasNodeHistoryPushed = false;
            canvasSurface.Capture = true;
        }
        canvasSurface.Invalidate();
        UpdateCanvasCursor();
    }
    void CanvasMouseMove(object? sender, MouseEventArgs e)
    {
        if(nativeReady && CanvasWorkspaceMouseMove(e))return;
        if (e.Button == MouseButtons.None && canvasVectorEditNodeId is Guid editingId && !canvasAnchorDragging)
        {
            var editingNode = canvasNodes.FirstOrDefault(node => node.Id == editingId);
            int nextIndex = -1;
            PointF nextPoint = PointF.Empty, nextNormalized = PointF.Empty;
            bool nextMidpoint = false;
            if (editingNode != null && CanvasAnchorAt(editingNode, e.Location) < 0)
                TryGetCanvasAnchorInsertion(editingNode, e.Location, out nextIndex, out nextNormalized, out nextPoint, out nextMidpoint);
            bool changed = nextIndex != canvasAnchorPreviewInsertIndex
                || nextPoint != canvasAnchorPreviewPoint
                || nextNormalized != canvasAnchorPreviewNormalized
                || nextMidpoint != canvasAnchorPreviewMidpoint;
            canvasAnchorPreviewInsertIndex = nextIndex;
            canvasAnchorPreviewPoint = nextPoint;
            canvasAnchorPreviewNormalized = nextNormalized;
            canvasAnchorPreviewMidpoint = nextMidpoint;
            canvasSurface.Cursor = Cursors.Cross;
            if (changed) canvasSurface.Invalidate();
            return;
        }
        // 空闲悬停时提示操作光标：旋转角点显示旋转光标，缩放手柄显示斜向尺寸光标
        if (e.Button == MouseButtons.None && !canvasNodeDragging && !canvasNodeResizing && !canvasNodeRotating && !canvasPanning && !canvasWiring && !canvasCutting && !canvasMarqueeSelecting && !canvasAnchorDragging)
        {
            Cursor? hoverCursor = null;
            if (selectedCanvasNode != null && canvasSelectedIds.Count == 1)
            {
                int hoverCorner = CanvasRotationCornerAt(selectedCanvasNode, e.Location, 9);
                if (hoverCorner >= 0 && !(IsCanvasShapeKind(selectedCanvasNode) && hoverCorner == 2)) hoverCursor = rotateCursor;
                else if (IsResizeGrip(selectedCanvasNode, e.Location) || IsSquareResizeCorner(selectedCanvasNode, e.Location)) hoverCursor = Cursors.SizeNWSE;
            }
            if (hoverCursor != null && canvasSurface.Cursor != hoverCursor) canvasSurface.Cursor = hoverCursor;
            else if (hoverCursor == null && canvasSurface.Cursor != Cursors.Default) canvasSurface.Cursor = Cursors.Default;
        }
        if (canvasAnchorDragging && e.Button == MouseButtons.Left && canvasVectorEditNodeId is Guid editId)
        {
            var node = canvasNodes.FirstOrDefault(item => item.Id == editId);
            if (node != null && canvasAnchorIndex >= 0)
            {
                var bounds = CanvasBounds(node);
                var desiredScreenPoint = new PointF(e.Location.X + canvasAnchorDragOffset.X, e.Location.Y + canvasAnchorDragOffset.Y);
                var localPoint = RotateCanvasPoint(desiredScreenPoint, CanvasNodeCenter(bounds), -node.Rotation);
                // 允许锚点超出原始边界；归一化坐标可为负数或大于 1。
                var next = new PointF((localPoint.X - bounds.Left) / (float)Math.Max(1, bounds.Width), (localPoint.Y - bounds.Top) / (float)Math.Max(1, bounds.Height));
                if (node.VectorAnchors[canvasAnchorIndex] != next)
                {
                    if (!canvasNodeHistoryPushed) { PushCanvasUndo(); canvasNodeHistoryPushed = true; }
                    node.VectorAnchors[canvasAnchorIndex] = next;
                    canvasNodeChanged = true;
                    InvalidateCanvasPaintCache();
                    canvasSurface.Invalidate();
                }
            }
            return;
        }
        if (canvasMarqueeSelecting && e.Button == MouseButtons.Left)
        {
            canvasMarqueeEnd = e.Location;
            canvasMarqueeBounds = CanvasRect(canvasMarqueeStart, canvasMarqueeEnd);
            canvasSurface.Invalidate();
            return;
        }
        if (canvasCutting && e.Button == MouseButtons.Left)
        {
            CutCanvasLinks(canvasCutLast, e.Location);
            canvasCutLast = e.Location;
            canvasSurface.Cursor = knifeCursor;
            return;
        }
        if (wireStartNode != null && e.Button == wireButton && wireButton != MouseButtons.None)
        {
            if (wireButton == MouseButtons.Right && !canvasWiring && Math.Abs(e.X - wireStartPoint.X) + Math.Abs(e.Y - wireStartPoint.Y) > 10) canvasWiring = true;
            if (canvasWiring)
            {
                if (wireButton == MouseButtons.Left && canvasTool != CanvasTool.Connect)
                {
                    var targetPort = CanvasConnectionPortAt(e.Location);
                    bool compatible = targetPort.HasValue && targetPort.Value.Node != wireStartNode && targetPort.Value.IsOutput != wireStartedAtOutput;
                    wireTargetNode = compatible ? targetPort!.Value.Node : null;
                    wireEnd = compatible ? targetPort!.Value.Point : e.Location;
                }
                else
                {
                    var candidate = CanvasNodeAt(e.Location);
                    wireTargetNode = candidate == wireStartNode ? null : candidate;
                    wireEnd = e.Location;
                }
                canvasSurface.Cursor = wireTargetNode == null ? Cursors.Cross : Cursors.Hand;
                canvasSurface.Invalidate();
            }
            return;
        }
        if (canvasPanning)
        {
            int dx = e.X - canvasPanLast.X, dy = e.Y - canvasPanLast.Y;
        if (dx != 0 || dy != 0) { canvasOffset = new PointF(canvasOffset.X + dx, canvasOffset.Y + dy); canvasPanLast = e.Location; InvalidateCanvasPaintCache(); canvasSurface.Invalidate(); }
            return;
        }
        if (selectedCanvasNode != null && canvasNodeRotating && e.Button == MouseButtons.Left)
        {
            var center = CanvasNodeCenter(CanvasBounds(selectedCanvasNode));
            float currentAngle = (float)(Math.Atan2(e.Y - center.Y, e.X - center.X) * 180d / Math.PI);
            float deltaAngle = currentAngle - canvasRotationStartAngle;
            while (deltaAngle > 180) deltaAngle -= 360;
            while (deltaAngle < -180) deltaAngle += 360;
            float nextRotation = (canvasNodeStartRotation + deltaAngle) % 360f;
            if((ModifierKeys & Keys.Shift)!=0)nextRotation=(float)Math.Round(nextRotation/15f)*15f;
            if (Math.Abs(nextRotation - selectedCanvasNode.Rotation) > 0.01f)
            {
                if (!canvasNodeHistoryPushed) { PushCanvasUndo(); canvasNodeHistoryPushed = true; }
                selectedCanvasNode.Rotation = nextRotation; canvasNodeChanged = true; canvasSurface.Invalidate();
            }
            return;
        }
        if (selectedCanvasNode != null && (canvasNodeDragging || canvasNodeResizing) && e.Button == MouseButtons.Left)
        {
            int moveX = e.X - canvasMouseDown.X, moveY = e.Y - canvasMouseDown.Y;
            if (canvasAltClonePending && Math.Abs(moveX) + Math.Abs(moveY) > 5)
            {
                var original = selectedCanvasNode;
                PushCanvasUndo();
                var duplicates=CloneRepeatNodes(CanvasSelectedNodes().Where(n=>!n.Locked).ToList());
                SetCanvasSelection(duplicates);
                selectedCanvasNode=duplicates.First();
                canvasSelectionStartPositions.Clear();foreach(var n in duplicates)canvasSelectionStartPositions[n.Id]=n.Position;
                canvasNodeStartPosition=selectedCanvasNode.Position;canvasNodeStartSize=selectedCanvasNode.Size;
                canvasAltClonePending = false;
                canvasNodeChanged = true;
                canvasNodeHistoryPushed = true;
                RefreshCanvasLayers();
            }
            if (!canvasNodeHistoryPushed && (moveX != 0 || moveY != 0)) { PushCanvasUndo(); canvasNodeHistoryPushed = true; }
            var movedNodes = CanvasSelectedNodes();
            if (movedNodes.Count == 0) movedNodes.Add(selectedCanvasNode);
            movedNodes = movedNodes.Where(node => !node.Locked).ToList();
            if (movedNodes.Count == 0) return;
            var movedIds = movedNodes.Select(node => node.Id).ToHashSet();
            var attachedLinks = canvasLinks.Where(link => movedIds.Contains(link.From) || movedIds.Contains(link.To)).ToArray();
            var oldLinkBounds = attachedLinks.Select(CanvasLinkBounds).Where(bounds => !bounds.IsEmpty).ToArray();
            var oldBounds = movedNodes.ToDictionary(node => node.Id, CanvasBounds);
            float localMoveX = moveX, localMoveY = moveY;
            if (canvasNodeResizing && IsCanvasRotatable(selectedCanvasNode) && Math.Abs(selectedCanvasNode.Rotation % 360f) > 0.01f)
            {
                double radians = -selectedCanvasNode.Rotation * Math.PI / 180d;
                localMoveX = (float)(moveX * Math.Cos(radians) - moveY * Math.Sin(radians));
                localMoveY = (float)(moveX * Math.Sin(radians) + moveY * Math.Cos(radians));
            }
            float dx = localMoveX / canvasZoom, dy = localMoveY / canvasZoom;
            if (canvasNodeDragging)
            {
                // Shift 吸附对齐 / 网格吸附
                float snapX = 0, snapY = 0;
                if (canvasWorkspace.SmartSnap && (ModifierKeys & Keys.Alt) == 0) UpdateCanvasSnapping(movedNodes, dx, dy, out snapX, out snapY);
                else if (canvasSnapGuides.Count > 0) canvasSnapGuides.Clear();
                dx += snapX; dy += snapY;
                if((ModifierKeys & Keys.Shift)!=0){if(Math.Abs(moveX)>=Math.Abs(moveY))dy=0;else dx=0;}
                // 默认开启网格吸附，按住 Alt 临时禁用
                if (canvasWorkspace.GridSnap && (ModifierKeys & Keys.Alt) != Keys.Alt && (ModifierKeys & Keys.Shift) != Keys.Shift)
                {
                    var anchor = selectedCanvasNode != null && movedNodes.Any(n => n.Id == selectedCanvasNode.Id) ? selectedCanvasNode : movedNodes[0];
                    if (canvasSelectionStartPositions.TryGetValue(anchor.Id, out var anchorStart))
                    {
                        float rawX = anchorStart.X + dx, rawY = anchorStart.Y + dy;
                        dx += (float)(Math.Round(rawX / CanvasGridSize) * CanvasGridSize) - rawX;
                        dy += (float)(Math.Round(rawY / CanvasGridSize) * CanvasGridSize) - rawY;
                    }
                }
                foreach (var node in movedNodes)
                    if (canvasSelectionStartPositions.TryGetValue(node.Id, out var startPosition)) node.Position = new PointF(startPosition.X + dx, startPosition.Y + dy);
                if (canvasPaintNodes != null && movedNodes.Any(node => !canvasPaintNodes.Any(cached => cached.Node.Id == node.Id) && CanvasVisualBounds(node).IntersectsWith(canvasSurface.ClientRectangle))) InvalidateCanvasPaintCache();
            }
            else
            {
                int newWidth = Math.Max(selectedCanvasNode.Kind == "text" ? 40 : 60, canvasNodeStartSize.Width + (int)Math.Round(dx));
                int newHeight = Math.Max(selectedCanvasNode.Kind == "text" ? 30 : 60, canvasNodeStartSize.Height + (int)Math.Round(dy));
                if ((ModifierKeys & Keys.Shift) == Keys.Shift)
                {
                    if (IsCanvasShapeKind(selectedCanvasNode))
                    {
                        int side = Math.Max(60, Math.Abs(dx) >= Math.Abs(dy) ? newWidth : newHeight);
                        newWidth = newHeight = side;
                    }
                    else if (selectedCanvasNode.Kind == "image" && canvasNodeStartSize.Height > 0 && canvasNodeStartSize.Width > 0)
                    {
                        // 图片保持原始宽高比，以位移较大的轴为准
                        float ratio = canvasNodeStartSize.Width / (float)canvasNodeStartSize.Height;
                        if (Math.Abs(dx) >= Math.Abs(dy)) newHeight = Math.Max(60, (int)Math.Round(newWidth / ratio));
                        else newWidth = Math.Max(60, (int)Math.Round(newHeight * ratio));
                    }
                }
                selectedCanvasNode.Size = new Size(newWidth, newHeight);
                if (selectedCanvasNode.AutoLayout && selectedCanvasNode.GroupId is Guid layoutGroup) ApplyCanvasAutoLayout(layoutGroup);
            }
            canvasNodeChanged = dx != 0 || dy != 0;
            if (canvasNodeDragging)
            {
                // 吸附辅助线可能延伸到节点边界之外，拖动时整面重绘
                canvasSurface.Invalidate();
            }
            else
            {
                foreach (var node in movedNodes)
                {
                    var oldNodeBounds = oldBounds[node.Id]; oldNodeBounds.Inflate(3, 3);
                    var newNodeBounds = CanvasBounds(node); newNodeBounds.Inflate(3, 3);
                    canvasSurface.Invalidate(oldNodeBounds); canvasSurface.Invalidate(newNodeBounds);
                }
                foreach (var bounds in oldLinkBounds) canvasSurface.Invalidate(bounds);
                foreach (var link in attachedLinks)
                {
                    var bounds = CanvasLinkBounds(link);
                    if (!bounds.IsEmpty) canvasSurface.Invalidate(bounds);
                }
            }
            return;
        }
        var over = CanvasNodeAt(e.Location);
        if (canvasHoveredNode?.Id != over?.Id) { canvasHoveredNode = over; canvasSurface.Invalidate(); }
        canvasSurface.Cursor = (ModifierKeys & Keys.Alt) == Keys.Alt ? knifeCursor : canvasSpaceDown ? Cursors.Hand : CanvasConnectionPortAt(e.Location).HasValue ? Cursors.Cross : selectedCanvasNode != null && canvasSelectedIds.Count == 1 && IsSquareResizeCorner(selectedCanvasNode, e.Location) ? Cursors.SizeNWSE : selectedCanvasNode != null && canvasSelectedIds.Count == 1 && CanvasRotationCornerAt(selectedCanvasNode, e.Location) >= 0 ? rotateCursor : over != null && IsResizeGrip(over, e.Location) ? Cursors.SizeNWSE : over != null ? Cursors.SizeAll : CanvasLinkAt(e.Location) != null ? Cursors.Hand : Cursors.Default;
    }
    void MoveCanvasSelection(float dx, float dy)
    {
        var movedNodes = CanvasSelectedNodes();
        if (movedNodes.Count == 0) return;
        PushCanvasUndo();
        var movedIds = movedNodes.Select(node => node.Id).ToHashSet();
        var attachedLinks = canvasLinks.Where(link => movedIds.Contains(link.From) || movedIds.Contains(link.To)).ToArray();
        var oldLinkBounds = attachedLinks.Select(CanvasLinkBounds).Where(bounds => !bounds.IsEmpty).ToArray();
        foreach (var node in movedNodes)
        {
            var oldBounds = CanvasBounds(node); oldBounds.Inflate(3, 3);
            node.Position = new PointF(node.Position.X + dx, node.Position.Y + dy);
            var newBounds = CanvasBounds(node); newBounds.Inflate(3, 3);
            canvasSurface.Invalidate(oldBounds); canvasSurface.Invalidate(newBounds);
        }
        foreach (var bounds in oldLinkBounds) canvasSurface.Invalidate(bounds);
        foreach (var link in attachedLinks)
        {
            var bounds = CanvasLinkBounds(link);
            if (!bounds.IsEmpty) canvasSurface.Invalidate(bounds);
        }
        SaveCanvas();
    }
    void CutCanvasLinks(Point start, Point end)
    {
        var cut = canvasLinks.Where(link =>
        {
            var from = canvasNodes.FirstOrDefault(n => n.Id == link.From);
            var to = canvasNodes.FirstOrDefault(n => n.Id == link.To);
            if (from == null || to == null || from.Hidden || to.Hidden) return false;
            var (p0, p1, p2, p3) = CanvasLinkCurve(from, to, PointF.Empty, link);
            using var path = new GraphicsPath();
            path.AddBezier(p0, p1, p2, p3);
            path.Flatten(null, 0.5f);
            var points = path.PathPoints;
            for (int i = 1; i < points.Length; i++)
            {
                if (SegmentsNear(points[i - 1], points[i], start, end, 8)) return true;
            }
            return false;
        }).ToArray();
        if (cut.Length == 0) return;
        if (!canvasCutChanged) { PushCanvasUndo(); canvasCutChanged = true; }
        foreach (var link in cut) canvasLinks.Remove(link);
        InvalidateCanvasPaintCache();
        if (selectedCanvasLink != null && cut.Contains(selectedCanvasLink)) SetCanvasLinkSelection(null);
        canvasSurface.Invalidate();
    }
    static bool SegmentsNear(PointF a, PointF b, PointF c, PointF d, float tolerance)
    {
        if (SegmentsIntersect(a, b, c, d)) return true;
        float Distance(PointF p, PointF x, PointF y)
        {
            float dx = y.X - x.X, dy = y.Y - x.Y;
            float length = dx * dx + dy * dy;
            float t = length < 0.001f ? 0 : Math.Clamp(((p.X - x.X) * dx + (p.Y - x.Y) * dy) / length, 0, 1);
            float px = x.X + t * dx, py = x.Y + t * dy;
            return (float)Math.Sqrt((p.X - px) * (p.X - px) + (p.Y - py) * (p.Y - py));
        }
        return Math.Min(Math.Min(Distance(a, c, d), Distance(b, c, d)), Math.Min(Distance(c, a, b), Distance(d, a, b))) <= tolerance;
    }
    static bool SegmentsIntersect(PointF a, PointF b, PointF c, PointF d)
    {
        static float Cross(PointF p, PointF q, PointF r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
        static bool OnSegment(PointF p, PointF q, PointF r) => q.X <= Math.Max(p.X, r.X) + 0.001f && q.X >= Math.Min(p.X, r.X) - 0.001f && q.Y <= Math.Max(p.Y, r.Y) + 0.001f && q.Y >= Math.Min(p.Y, r.Y) - 0.001f;
        float abC = Cross(a, b, c), abD = Cross(a, b, d), cdA = Cross(c, d, a), cdB = Cross(c, d, b);
        if (((abC > 0 && abD < 0) || (abC < 0 && abD > 0)) && ((cdA > 0 && cdB < 0) || (cdA < 0 && cdB > 0))) return true;
        return Math.Abs(abC) < 0.001f && OnSegment(a, c, b) || Math.Abs(abD) < 0.001f && OnSegment(a, d, b) || Math.Abs(cdA) < 0.001f && OnSegment(c, a, d) || Math.Abs(cdB) < 0.001f && OnSegment(c, b, d);
    }
    static Cursor CreateKnifeCursor()
    {
        using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var outline = new Pen(Color.FromArgb(45, 42, 39), 6) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var blade = new Pen(Color.WhiteSmoke, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var handle = new Pen(Color.FromArgb(190, 105, 80), 5) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawLine(outline, 6, 26, 25, 7);
            graphics.DrawLine(blade, 6, 26, 22, 10);
            graphics.DrawLine(handle, 20, 12, 27, 5);
        }
        return new Cursor(bitmap.GetHicon());
    }
    static Cursor CreateRotateCursor()
    {
        using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var outline = new Pen(Color.FromArgb(45, 42, 39), 5) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var arc = new Pen(Color.WhiteSmoke, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawArc(outline, 5, 5, 22, 22, 42, 286);
            graphics.DrawArc(arc, 5, 5, 22, 22, 42, 286);
            using var arrowOutline = new SolidBrush(Color.FromArgb(45, 42, 39));
            using var arrow = new SolidBrush(Color.FromArgb(190, 105, 80));
            PointF[] arrowPoints = { new(24, 3), new(30, 10), new(21, 11) };
            graphics.FillPolygon(arrowOutline, arrowPoints);
            graphics.FillPolygon(arrow, arrowPoints);
        }
        return new Cursor(bitmap.GetHicon());
    }
    void CanvasMouseUpCore(object? sender, MouseEventArgs e)
    {
        if(nativeReady && CanvasWorkspaceMouseUp(e))return;
        if (e.Button == MouseButtons.Middle)
        {
            if (canvasPanning)
            {
                canvasPanning = false;
                canvasSurface.Capture = false;
                UpdateCanvasCursor();
                SaveCanvas();
            }
            return;
        }
        if ((e.Button == MouseButtons.Left || e.Button == MouseButtons.Right) && wireStartNode != null && wireButton == e.Button)
        {
            var startNode = wireStartNode;
            CanvasNode? from = null, to = null;
            if (canvasWiring && wireButton == MouseButtons.Left)
            {
                if(canvasTool==CanvasTool.Connect)
                {
                    var target=CanvasNodeAt(e.Location);if(target!=null&&target!=startNode){from=startNode;to=target;}
                }
                var targetPort = CanvasConnectionPortAt(e.Location);
                if (targetPort is CanvasConnectionPort port && port.Node != startNode && port.IsOutput != wireStartedAtOutput)
                {
                    from = wireStartedAtOutput ? startNode : port.Node;
                    to = wireStartedAtOutput ? port.Node : startNode;
                }
            }
            else if (canvasWiring && wireButton == MouseButtons.Right)
            {
                var target = CanvasNodeAt(e.Location);
                if (target != null && target != startNode) { from = startNode; to = target; }
            }
            bool showMenu = wireButton == MouseButtons.Right && !canvasWiring;
            bool createAtDrop = canvasWiring && from == null && to == null
                && CanvasNodeAt(e.Location) == null && CanvasConnectionPortAt(e.Location) == null;
            bool connectFromStart = wireButton == MouseButtons.Right || wireStartedAtOutput;
            canvasWiring = false;
            wireStartNode = null;
            wireTargetNode = null;
            wireButton = MouseButtons.None;
            canvasSurface.Capture = false;
            int pairLinkCount = from == null || to == null ? 2 : canvasLinks.Count(link => (link.From == from.Id && link.To == to.Id) || (link.From == to.Id && link.To == from.Id));
            if (from != null && to != null && pairLinkCount < 2)
            {
                PushCanvasUndo();
                canvasLinks.Add(new CanvasLink { From = from.Id, To = to.Id });
                SaveCanvas();
            }
            canvasSurface.Invalidate();
            if (showMenu) ShowCanvasContextMenu(e.Location);
            else if (createAtDrop && nativeReady)
            {
                UpdateCanvasCursor();
                ShowNativeConnectedCreateMenu(startNode, e.Location, connectFromStart);
            }
            else UpdateCanvasCursor();
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        if (canvasAnchorDragging)
        {
            canvasAnchorDragging = false; canvasAnchorIndex = -1; canvasSurface.Capture = false;
            UpdateCanvasCursor();
            if (canvasNodeChanged) SaveCanvas();
            canvasNodeChanged = canvasNodeHistoryPushed = false;
            canvasSurface.Invalidate();
            return;
        }
        if (canvasMarqueeSelecting)
        {
            var selected = canvasMarqueeShift ? CanvasSelectedNodes() : new List<CanvasNode>();
            if (!canvasMarqueeBounds.IsEmpty)
            {
                var hits = canvasNodes.Where(node => !node.Hidden && !node.Locked && canvasMarqueeBounds.IntersectsWith(CanvasBounds(node))).ToList();
                var groupIds = hits.Where(node => node.GroupId.HasValue).Select(node => node.GroupId!.Value).ToHashSet();
                foreach (var node in canvasNodes.Where(node => !node.Hidden && !node.Locked && (hits.Any(hit => hit.Id == node.Id) || (node.GroupId.HasValue && groupIds.Contains(node.GroupId.Value)))))
                    if (!selected.Any(item => item.Id == node.Id)) selected.Add(node);
            }
            var oldMarquee = canvasMarqueeBounds;
            canvasMarqueeSelecting = false; canvasMarqueeBounds = Rectangle.Empty;
            canvasSurface.Capture = false;
            SetCanvasSelection(selected);
            if (!oldMarquee.IsEmpty) canvasSurface.Invalidate(oldMarquee);
            if (canvasLayersPanel.Visible) RefreshCanvasLayers();
            canvasSurface.Invalidate();
            return;
        }
        if (canvasCutting)
        {
            CutCanvasLinks(canvasCutLast, e.Location);
            canvasCutting = false; canvasSurface.Capture = false; UpdateCanvasCursor();
            if (canvasCutChanged) SaveCanvas();
            canvasCutChanged = false;
            return;
        }
        bool changed = canvasPanning || canvasNodeChanged;
        canvasPanning = canvasNodeDragging = canvasNodeResizing = canvasNodeRotating = false;
        canvasAltClonePending = false;
        canvasNodeChanged = false;
        canvasSurface.Capture = false;
        canvasSelectionStartPositions.Clear();
        if (canvasSnapGuides.Count > 0) { canvasSnapGuides.Clear(); canvasSurface.Invalidate(); }
        UpdateCanvasCursor();
        UpdateCanvasAlignBar();
        if (changed) SaveCanvas();
    }
    void CanvasMouseDoubleClick(object? sender, MouseEventArgs e)
    {
        if(canvasTool==CanvasTool.Pen){FinishCanvasPen(false);return;}
        var node = CanvasNodeAt(e.Location);
        if(node?.Kind=="path"){EditCanvasPath(node);return;}
        if (node != null && IsCanvasShapeKind(node))
        {
            ToggleCanvasVectorEdit(node);
            return;
        }
        if (node?.Kind == "text")
        {
            SetCanvasSelection(new[] { node });
            ShowCanvasInlineEditor(node);
            return;
        }
        var link = CanvasLinkAt(e.Location);
        if (link != null) { SetCanvasLinkSelection(link); ShowCanvasInlineEditor(link); }
    }
    void ShowCanvasInlineEditor(CanvasNode node)
    {
        CommitCanvasInlineEdit();
        canvasNodeDragging = canvasNodeResizing = canvasNodeRotating = canvasAltClonePending = false;
        canvasSurface.Capture = false;
        var bounds = CanvasBounds(node);
        canvasInlineEditor.Font = CanvasNodeFont(node, canvasZoom);
        int pad = Math.Max(1, (int)Math.Round(12 * canvasZoom));
        canvasInlineEditor.Location = new Point(bounds.X + pad, bounds.Y + pad);
        canvasInlineEditor.Size = new Size(Math.Max(20, bounds.Width - pad * 2), Math.Max(20, bounds.Height - pad * 2));
        canvasInlineEditor.Multiline = true;
        canvasInlineEditor.ScrollBars = ScrollBars.Vertical;
        canvasInlineEditor.WordWrap = true;
        canvasInlineEditor.Text = node.Content;
        canvasInlineEditNodeId = node.Id;
        canvasInlineEditLink = null;
        canvasInlineEditor.Visible = true;
        canvasInlineEditor.Focus();
        canvasInlineEditor.SelectAll();
        canvasSurface.Invalidate();
    }
    void ShowCanvasInlineEditor(CanvasLink link)
    {
        CommitCanvasInlineEdit();
        var from = canvasNodes.FirstOrDefault(n => n.Id == link.From);
        var to = canvasNodes.FirstOrDefault(n => n.Id == link.To);
        if (from == null || to == null) return;
        var (p0, p1, p2, p3) = CanvasLinkCurve(from, to, PointF.Empty, link);
        int sx = (int)Math.Round((p0.X + 3 * p1.X + 3 * p2.X + p3.X) / 8f);
        int sy = (int)Math.Round((p0.Y + 3 * p1.Y + 3 * p2.Y + p3.Y) / 8f);
        int width = Math.Max(80, (int)Math.Round(160 * canvasZoom));
        canvasInlineEditor.Font = new Font(UiFontName(), Math.Clamp(10 * canvasZoom, 6, 36));
        canvasInlineEditor.Location = new Point(sx - width / 2, sy - 14);
        canvasInlineEditor.Size = new Size(width, (int)Math.Round(28 * canvasZoom));
        canvasInlineEditor.Multiline = false;
        canvasInlineEditor.ScrollBars = ScrollBars.None;
        canvasInlineEditor.WordWrap = false;
        canvasInlineEditor.Text = link.Label;
        canvasInlineEditNodeId = null;
        canvasInlineEditLink = link;
        canvasInlineEditor.Visible = true;
        canvasInlineEditor.Focus();
        canvasInlineEditor.SelectAll();
        canvasSurface.Invalidate();
    }
    void CancelCanvasInlineEdit()
    {
        canvasInlineEditNodeId = null;
        canvasInlineEditLink = null;
        if (canvasInlineEditor.Visible) { canvasInlineEditor.Visible = false; canvasSurface.Invalidate(); }
    }
    void CommitCanvasInlineEdit()
    {
        if (!canvasInlineEditor.Visible) return;
        var nodeId = canvasInlineEditNodeId;
        var link = canvasInlineEditLink;
        var text = canvasInlineEditor.Text;
        canvasInlineEditNodeId = null;
        canvasInlineEditLink = null;
        canvasInlineEditor.Visible = false;
        if (nodeId is Guid id)
        {
            var node = canvasNodes.FirstOrDefault(n => n.Id == id);
            if (node == null) return;
            if (node.Content != text) { PushCanvasUndo(); node.Content = text; SaveCanvas(); }
            canvasSurface.Invalidate(CanvasBounds(node));
        }
        else if (link != null)
        {
            if (link.Label != text) { PushCanvasUndo(); link.Label = text; SaveCanvas(); }
            canvasSurface.Invalidate(CanvasLinkBounds(link));
        }
    }
    CanvasLink? CanvasLinkAt(Point point)
    {
        const float threshold = 10;
        var nodesById = new Dictionary<Guid, CanvasNode>(canvasNodes.Count);
        foreach (var node in canvasNodes) nodesById[node.Id] = node;
        foreach (var link in canvasLinks)
        {
            if (!nodesById.TryGetValue(link.From, out var from) || !nodesById.TryGetValue(link.To, out var to) || from.Hidden || to.Hidden) continue;
            var (p0, p1, p2, p3) = CanvasLinkCurve(from, to, PointF.Empty, link);
            var prev = p0;
            for (int i = 1; i <= 24; i++)
            {
                float t = i / 24f, u = 1 - t;
                var curr = new PointF(u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X, u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y);
                float px = point.X, py = point.Y;
                float dist = Math.Abs((curr.Y - prev.Y) * px - (curr.X - prev.X) * py + curr.X * prev.Y - curr.Y * prev.X) / (float)Math.Sqrt((curr.X - prev.X) * (curr.X - prev.X) + (curr.Y - prev.Y) * (curr.Y - prev.Y) + 0.001);
                if (dist < threshold && Math.Min(prev.X, curr.X) - threshold <= px && px <= Math.Max(prev.X, curr.X) + threshold && Math.Min(prev.Y, curr.Y) - threshold <= py && py <= Math.Max(prev.Y, curr.Y) + threshold)
                    return link;
                prev = curr;
            }
        }
        return null;
    }
    void ToggleCanvasVectorEdit(CanvasNode node)
    {
        if (canvasVectorEditNodeId == node.Id) canvasVectorEditNodeId = null;
        else
        {
            if (node.VectorAnchors.Count < 3) node.VectorAnchors = DefaultShapeAnchors(node.Kind);
            SetCanvasSelection(new[] { node });
            canvasVectorEditNodeId = node.Id;
        }
        ClearCanvasAnchorInsertionPreview();
        canvasSurface.Cursor = canvasVectorEditNodeId.HasValue ? Cursors.Cross : Cursors.Default;
        canvasSurface.Invalidate();
    }
    void ApplyCanvasAutoLayout(Guid groupId)
    {
        var members = canvasNodes.Where(item => item.GroupId == groupId && item.AutoLayout).ToList();
        if (members.Count < 2) return;
        var horizontal = members[0].AutoLayoutDirection == 0;
        members = members.OrderBy(item => horizontal ? item.Position.X : item.Position.Y).ThenBy(item => horizontal ? item.Position.Y : item.Position.X).ToList();
        float padding = Math.Max(0, members[0].AutoLayoutPadding), gap = Math.Max(0, members[0].AutoLayoutGap);
        float left = members.Min(item => item.Position.X), top = members.Min(item => item.Position.Y);
        float crossSize = horizontal ? members.Max(item => item.Size.Height) : members.Max(item => item.Size.Width);
        float cursor = padding;
        foreach (var item in members)
        {
            item.Position = horizontal
                ? new PointF(left + cursor, top + padding + (crossSize - item.Size.Height) / 2f)
                : new PointF(left + padding + (crossSize - item.Size.Width) / 2f, top + cursor);
            cursor += (horizontal ? item.Size.Width : item.Size.Height) + gap;
        }
    }
    void SetCanvasAutoLayout(IReadOnlyList<CanvasNode> nodes, int direction)
    {
        if (nodes.Count < 2) return;
        PushCanvasUndo();
        var groupId = nodes.Select(item => item.GroupId).FirstOrDefault(id => id.HasValue) ?? Guid.NewGuid();
        foreach (var item in nodes) { item.GroupId = groupId; item.AutoLayout = true; item.AutoLayoutDirection = direction; }
        var setting = nodes.FirstOrDefault(item => item.AutoLayout);
        if (setting != null) foreach (var item in nodes) { item.AutoLayoutGap = setting.AutoLayoutGap; item.AutoLayoutPadding = setting.AutoLayoutPadding; }
        ApplyCanvasAutoLayout(groupId);
        canvasSurface.Invalidate(); SaveCanvas(); RefreshCanvasLayers();
    }
    void PromptCanvasAutoLayoutSpacing(IReadOnlyList<CanvasNode> nodes, bool padding)
    {
        var layoutNode = nodes.FirstOrDefault(item => item.AutoLayout);
        if (layoutNode?.GroupId is not Guid groupId) return;
        if (nativeReady)
        {
            NativePrompt(padding ? "设置自动布局内边距" : "设置自动布局间距", (padding ? layoutNode.AutoLayoutPadding : layoutNode.AutoLayoutGap).ToString(), value =>
            {
                if (!float.TryParse(value, out var amount) || amount < 0 || amount > 500) return;
                PushCanvasUndo();
                foreach (var item in canvasNodes.Where(item => item.GroupId == groupId && item.AutoLayout))
                    if (padding) item.AutoLayoutPadding = amount; else item.AutoLayoutGap = amount;
                ApplyCanvasAutoLayout(groupId); canvasSurface.Invalidate(); SaveCanvas();
            });
            return;
        }
        using var dialog = new Form { Text = padding ? "设置自动布局内边距" : "设置自动布局间距", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(270, 112), MaximizeBox = false, MinimizeBox = false };
        var input = new NumericUpDown { Minimum = 0, Maximum = 500, Value = (decimal)(padding ? layoutNode.AutoLayoutPadding : layoutNode.AutoLayoutGap), Location = new Point(16, 16), Width = 230 };
        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(160, 64), Width = 86 };
        dialog.Controls.Add(input); dialog.Controls.Add(ok); dialog.AcceptButton = ok;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        PushCanvasUndo();
        foreach (var item in canvasNodes.Where(item => item.GroupId == groupId && item.AutoLayout))
            if (padding) item.AutoLayoutPadding = (float)input.Value; else item.AutoLayoutGap = (float)input.Value;
        ApplyCanvasAutoLayout(groupId); canvasSurface.Invalidate(); SaveCanvas();
    }
    void CreateCanvasComponent(IReadOnlyList<CanvasNode> nodes)
    {
        if (nodes.Count == 0) return;
        PushCanvasUndo(); var componentId = Guid.NewGuid();
        foreach (var item in nodes) { item.ComponentId = componentId; item.ComponentInstanceId = null; item.ComponentMasterNodeId = null; }
        canvasSurface.Invalidate(); SaveCanvas();
    }
    void CreateCanvasComponentInstance(IReadOnlyList<CanvasNode> sources)
    {
        if (sources.Count == 0 || sources.Any(item => item.ComponentId == null)) return;
        PushCanvasUndo();
        var componentId = sources[0].ComponentId!.Value; var instanceId = Guid.NewGuid();
        var idMap = new Dictionary<Guid, Guid>(); var clones = new List<CanvasNode>();
        foreach (var source in sources)
        {
            var clone = CloneCanvasNodeTemplate(source); clone.Id = Guid.NewGuid(); idMap[source.Id] = clone.Id;
            clone.Position = new PointF(source.Position.X + 36, source.Position.Y + 36); clone.ComponentId = componentId; clone.ComponentInstanceId = instanceId; clone.ComponentMasterNodeId = source.Id;
            clone.GroupId = sources.Count > 1 ? instanceId : null; clone.AutoLayout = source.AutoLayout;
            if (source.Kind == "image" && source.Bitmap != null) { clone.Bitmap = new Bitmap(source.Bitmap); SaveCanvasNodeImage(clone); }
            clones.Add(clone);
        }
        foreach (var link in canvasLinks.Where(link => idMap.ContainsKey(link.From) && idMap.ContainsKey(link.To)))
            canvasLinks.Add(new CanvasLink { From = idMap[link.From], To = idMap[link.To], Label = link.Label, Color = link.Color, Arrow = link.Arrow });
        canvasNodes.AddRange(clones); SetCanvasSelection(clones); canvasSurface.Invalidate(); RefreshCanvasLayers(); SaveCanvas();
    }
    void SyncCanvasComponentInstances()
    {
        bool changed = false;
        foreach (var componentId in canvasNodes.Where(item => item.ComponentId.HasValue && item.ComponentInstanceId == null).Select(item => item.ComponentId!.Value).Distinct())
        {
            var masters = canvasNodes.Where(item => item.ComponentId == componentId && item.ComponentInstanceId == null).ToDictionary(item => item.Id);
            var root = masters.Values.FirstOrDefault(); if (root == null) continue;
            foreach (var instanceId in canvasNodes.Where(item => item.ComponentId == componentId && item.ComponentInstanceId.HasValue).Select(item => item.ComponentInstanceId!.Value).Distinct())
            {
                var instanceNodes = canvasNodes.Where(item => item.ComponentId == componentId && item.ComponentInstanceId == instanceId).ToList();
                var instanceRoot = instanceNodes.FirstOrDefault(item => item.ComponentMasterNodeId == root.Id); if (instanceRoot == null) continue;
                float dx = instanceRoot.Position.X - root.Position.X, dy = instanceRoot.Position.Y - root.Position.Y;
                foreach (var clone in instanceNodes)
                {
                    if (clone.ComponentMasterNodeId is not Guid masterId || !masters.TryGetValue(masterId, out var master)) continue;
                    var position = new PointF(master.Position.X + dx, master.Position.Y + dy);
                    if (clone.Content == master.Content && clone.Kind == master.Kind && clone.Size == master.Size && clone.Position == position && clone.Transparent == master.Transparent && clone.Rotation == master.Rotation && clone.FontSize == master.FontSize && clone.FontBold == master.FontBold && clone.FontItalic == master.FontItalic && clone.FontStrikeout == master.FontStrikeout && clone.FontAlign == master.FontAlign && clone.FontVAlign == master.FontVAlign && clone.FontColor == master.FontColor && clone.FillColor == master.FillColor && clone.Opacity == master.Opacity && clone.FillOpacity == master.FillOpacity && clone.CornerRadius == master.CornerRadius && clone.StrokeWidth == master.StrokeWidth && clone.StrokeColor == master.StrokeColor && clone.StrokeAlign == master.StrokeAlign && clone.StrokeDash == master.StrokeDash && clone.VectorAnchors.SequenceEqual(master.VectorAnchors) && EncodeLayerData(clone.Extra)==EncodeLayerData(master.Extra)) continue;
                    clone.Kind = master.Kind; clone.Content = master.Content; clone.Position = position; clone.Size = master.Size; clone.Transparent = master.Transparent; clone.Rotation = master.Rotation;
                    clone.FontSize = master.FontSize; clone.FontBold = master.FontBold; clone.FontItalic = master.FontItalic; clone.FontStrikeout = master.FontStrikeout; clone.FontAlign = master.FontAlign; clone.FontVAlign = master.FontVAlign;
                    clone.FontColor = master.FontColor; clone.FillColor = master.FillColor; clone.Opacity = master.Opacity; clone.FillOpacity = master.FillOpacity; clone.CornerRadius = master.CornerRadius; clone.StrokeWidth = master.StrokeWidth; clone.StrokeColor = master.StrokeColor; clone.StrokeAlign = master.StrokeAlign; clone.StrokeDash = master.StrokeDash; clone.VectorAnchors = master.VectorAnchors.ToList();clone.Extra=CopyLayerData(master.Extra);
                    changed = true;
                }
            }
        }
        if (changed) canvasSurface.Invalidate();
    }
    void ShowCanvasContextMenu(Point point)
    {
        var node = CanvasNodeAt(point);
        if (node != null)
        {
            selectedCanvasLink = null;
            if (!CanvasGroupMembers(node).All(item => canvasSelectedIds.Contains(item.Id))) SetCanvasSelection(CanvasGroupMembers(node));
        }
        else if (canvasSelectedIds.Count == 0) selectedCanvasNode = null;
        canvasSurface.Invalidate();
        var worldPosition = new PointF((point.X - canvasOffset.X) / canvasZoom, (point.Y - canvasOffset.Y) / canvasZoom);
        var menu = new ContextMenuStrip();
        var hoverLink = node == null ? CanvasLinkAt(point) : null;
        if (node == null && hoverLink == null && selectedCanvasLink != null)
        {
            selectedCanvasLink = null;
            UpdateCanvasProps();
        }
        if (hoverLink != null)
        {
            SetCanvasLinkSelection(hoverLink);
            menu.Items.Add("编辑连线标签", null, (_, _) => { SetCanvasLinkSelection(hoverLink); ShowCanvasInlineEditor(hoverLink); });
            menu.Items.Add("删除连线", null, (_, _) => DeleteCanvasLink(hoverLink));
            menu.Items.Add(new ToolStripSeparator());
        }
        if (node?.Kind == "text") menu.Items.Add("编辑文本", null, (_, _) => EditCanvasText(node));
        if (node != null && IsCanvasShapeKind(node)) menu.Items.Add(canvasVectorEditNodeId == node.Id ? "退出锚点编辑" : "编辑锚点", null, (_, _) => ToggleCanvasVectorEdit(node));
        if (node?.Kind == "text") menu.Items.Add(node.Transparent ? "恢复文本框背景" : "背景透明（只显示文字）", null, (_, _) => { PushCanvasUndo(); node.Transparent = !node.Transparent; canvasSurface.Invalidate(CanvasBounds(node)); SaveCanvas(); });
        if (node != null)
        {
            int deleteCount = CanvasSelectedNodes().Count;
            menu.Items.Add(deleteCount > 1 ? $"删除选中的 {deleteCount} 个对象" : "删除对象", null, (_, _) => DeleteCanvasSelection());
        }
        var selectedNodes = CanvasSelectedNodes();
        if (hoverLink == null && selectedNodes.Count > 0)
        {
            string layerSuffix = selectedNodes.Count > 1 ? $"（{selectedNodes.Count} 个）" : "";
            menu.Items.Add("置顶" + layerSuffix, null, (_, _) => BringCanvasSelectionToFront(true));
            menu.Items.Add("置底" + layerSuffix, null, (_, _) => BringCanvasSelectionToFront(false));
        }
        bool alreadyGroupedTogether = selectedNodes.Count > 1 && selectedNodes[0].GroupId.HasValue && selectedNodes.All(item => item.GroupId == selectedNodes[0].GroupId);
        if (selectedNodes.Count > 1 && !alreadyGroupedTogether)
        {
            menu.Items.Add("成组", null, (_, _) =>
            {
                PushCanvasUndo();
                var groupId = Guid.NewGuid();
                foreach (var selected in selectedNodes) selected.GroupId = groupId;
                canvasSurface.Invalidate(); SaveCanvas();
            });
        }
        if (node?.GroupId is Guid ungroupId)
            menu.Items.Add("解组", null, (_, _) =>
            {
                PushCanvasUndo();
                foreach (var member in canvasNodes.Where(item => item.GroupId == ungroupId)) member.GroupId = null;
                canvasSurface.Invalidate(); SaveCanvas();
            });
        if (selectedNodes.Count >= 2)
        {
            var auto = new ToolStripMenuItem("自动布局"); menu.Items.Add(auto);
            auto.DropDownItems.Add("横向排列", null, (_, _) => SetCanvasAutoLayout(selectedNodes, 0));
            auto.DropDownItems.Add("纵向排列", null, (_, _) => SetCanvasAutoLayout(selectedNodes, 1));
            if (selectedNodes.Any(item => item.AutoLayout))
            {
                auto.DropDownItems.Add(new ToolStripSeparator());
                auto.DropDownItems.Add("设置对象间距…", null, (_, _) => PromptCanvasAutoLayoutSpacing(selectedNodes, false));
                auto.DropDownItems.Add("设置内边距…", null, (_, _) => PromptCanvasAutoLayoutSpacing(selectedNodes, true));
            }
        }
        if (node?.AutoLayout == true && node.GroupId is Guid layoutId)
        {
            var membersItem = new ToolStripMenuItem("添加对象到自动布局");
            foreach (var candidate in canvasNodes.Where(item => item.GroupId != layoutId).Take(80))
            {
                var label = string.IsNullOrWhiteSpace(candidate.Name) ? $"{candidate.Kind} · {candidate.Content}" : candidate.Name;
                if (label.Length > 28) label = label[..28] + "…";
                membersItem.DropDownItems.Add(label, null, (_, _) =>
                {
                    PushCanvasUndo(); candidate.GroupId = layoutId; candidate.AutoLayout = true;
                    candidate.AutoLayoutDirection = node.AutoLayoutDirection; candidate.AutoLayoutGap = node.AutoLayoutGap; candidate.AutoLayoutPadding = node.AutoLayoutPadding;
                    ApplyCanvasAutoLayout(layoutId); SetCanvasSelection(CanvasGroupMembers(node)); canvasSurface.Invalidate(); SaveCanvas(); RefreshCanvasLayers();
                });
            }
            if (membersItem.DropDownItems.Count > 0) menu.Items.Add(membersItem);
            menu.Items.Add("从自动布局中移除", null, (_, _) =>
            {
                PushCanvasUndo(); node.GroupId = null; node.AutoLayout = false;
                ApplyCanvasAutoLayout(layoutId); SetCanvasSelection(new[] { node }); canvasSurface.Invalidate(); SaveCanvas(); RefreshCanvasLayers();
            });
        }
        if (selectedNodes.Count > 0 && selectedNodes.All(item => item.ComponentId == null))
            menu.Items.Add("创建可复用组件", null, (_, _) => CreateCanvasComponent(selectedNodes));
        if (selectedNodes.Count > 0 && selectedNodes.All(item => item.ComponentId != null && item.ComponentInstanceId == null && item.ComponentId == selectedNodes[0].ComponentId))
            menu.Items.Add("创建组件实例", null, (_, _) => CreateCanvasComponentInstance(selectedNodes));
        if (menu.Items.Count > 0) menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("创建文本框", null, (_, _) => CreateCanvasNode("text", worldPosition, ""));
        menu.Items.Add("创建方块", null, (_, _) => CreateCanvasNode("square", worldPosition, ""));
        menu.Items.Add("创建圆形", null, (_, _) => CreateCanvasNode("circle", worldPosition, ""));
        menu.Items.Add("创建三角形", null, (_, _) => CreateCanvasNode("triangle", worldPosition, ""));
        menu.Items.Add("创建五角星", null, (_, _) => CreateCanvasNode("star", worldPosition, ""));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("导入图片…", null, (_, _) => ImportCanvasImage(point));
        menu.Items.Add("导出画布为 PNG…", null, (_, _) => ExportCanvasPng());
        if (nativeReady) ShowNativeCanvasMenu(menu, point); else menu.Show(canvasSurface, point);
    }
    void EditCanvasText(CanvasNode node)
    {
        ShowCanvasInlineEditor(node);
    }
    void CreateCanvasNode(string kind, PointF worldPosition, string content, Size? savedSize = null, Guid? savedId = null)
    {
        var isText = kind == "text";
        if (!loadingCanvas && canvasWorkspace.GridSnap) worldPosition = new PointF((float)(Math.Round(worldPosition.X / CanvasGridSize) * CanvasGridSize), (float)(Math.Round(worldPosition.Y / CanvasGridSize) * CanvasGridSize));
        var node = new CanvasNode { Id = savedId ?? Guid.NewGuid(), Kind = kind, Content = content, Position = worldPosition, Size = savedSize ?? (isText ? new Size(250, 170) : kind == "image" ? new Size(320, 220) : new Size(140, 140)), FontSize = 10f, FontAlign = isText ? FontAlignLeft : FontAlignCenter, FontVAlign = isText ? FontAlignTop : FontAlignMiddle, FontBold = !isText && kind != "image" };
        if (!loadingCanvas) PushCanvasUndo();
        canvasNodes.Add(node);
        if (kind == "image") node.Bitmap = LoadCanvasNodeImage(node.Id);
        SetCanvasSelection(new[] { node });
        canvasSurface.Invalidate();
        RefreshCanvasLayers();
        if (!loadingCanvas) SaveCanvas();
    }
    static readonly string[] canvasImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
    static bool IsCanvasImageFile(string path) => canvasImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    void CanvasSurfaceDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true && (e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>()).Any(IsCanvasImageFile))
            e.Effect = DragDropEffects.Copy;
        else
            e.Effect = DragDropEffects.None;
    }
    void CanvasSurfaceDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files) return;
        var imageFiles = files.Where(IsCanvasImageFile).ToArray();
        if (imageFiles.Length == 0) return;
        var dropPoint = canvasSurface.PointToClient(new Point(e.X, e.Y));
        PushCanvasUndo();
        int index = 0;
        foreach (var file in imageFiles)
        {
            try
            {
                using var loaded = Image.FromFile(file);
                var bitmap = new Bitmap(loaded);
                AddCanvasImageNode(bitmap, new Point(dropPoint.X + index * 24, dropPoint.Y + index * 24), false);
                index++;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"无法导入图片 {Path.GetFileName(file)}：{ex.Message}", "导入图片", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
    void ImportCanvasImage(Point surfacePoint)
    {
        try
        {
            using var dialog = new OpenFileDialog { Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|所有文件 (*.*)|*.*", Title = "导入图片", CheckFileExists = true, Multiselect = false };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            using var loaded = Image.FromFile(dialog.FileName);
            var bitmap = new Bitmap(loaded);
            AddCanvasImageNode(bitmap, surfacePoint);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"无法导入图片：{ex.Message}", "导入图片", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    void AddCanvasImageNode(Bitmap bitmap, Point surfaceCenter, bool pushUndo = true)
    {
        float scale = Math.Min(1f, Math.Min(440f / bitmap.Width, 340f / bitmap.Height));
        var size = new Size(Math.Max(60, (int)Math.Round(bitmap.Width * scale)), Math.Max(60, (int)Math.Round(bitmap.Height * scale)));
        var center = canvasSurface.ClientRectangle.Contains(surfaceCenter) ? surfaceCenter : new Point(canvasSurface.ClientSize.Width / 2, canvasSurface.ClientSize.Height / 2);
        var world = new PointF((center.X - canvasOffset.X) / canvasZoom - size.Width / 2f, (center.Y - canvasOffset.Y) / canvasZoom - size.Height / 2f);
        world = new PointF((float)(Math.Round(world.X / CanvasGridSize) * CanvasGridSize), (float)(Math.Round(world.Y / CanvasGridSize) * CanvasGridSize));
        if (pushUndo) PushCanvasUndo();
        var node = new CanvasNode { Id = Guid.NewGuid(), Kind = "image", Position = world, Size = size, Bitmap = bitmap };
        canvasNodes.Add(node);
        SaveCanvasNodeImage(node);
        SetCanvasSelection(new[] { node });
        canvasSurface.Invalidate();
        RefreshCanvasLayers();
        SaveCanvas();
    }
    void PasteCanvasImage()
    {
        try
        {
            if (!Clipboard.ContainsImage()) return;
            using var clipboardImage = Clipboard.GetImage();
            if (clipboardImage == null) return;
            var bitmap = new Bitmap(clipboardImage);
            float scale = Math.Min(1f, Math.Min(440f / bitmap.Width, 340f / bitmap.Height));
            var size = new Size(Math.Max(60, (int)Math.Round(bitmap.Width * scale)), Math.Max(60, (int)Math.Round(bitmap.Height * scale)));
            var center = canvasSurface.PointToClient(Cursor.Position);
            if (!canvasSurface.ClientRectangle.Contains(center)) center = new Point(canvasSurface.ClientSize.Width / 2, canvasSurface.ClientSize.Height / 2);
            var world = new PointF((center.X - canvasOffset.X) / canvasZoom - size.Width / 2f, (center.Y - canvasOffset.Y) / canvasZoom - size.Height / 2f);
            PushCanvasUndo();
            var node = new CanvasNode { Id = Guid.NewGuid(), Kind = "image", Position = world, Size = size, Bitmap = bitmap };
            canvasNodes.Add(node);
            SaveCanvasNodeImage(node);
            SetCanvasSelection(new[] { node });
            canvasSurface.Invalidate();
            RefreshCanvasLayers();
            SaveCanvas();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"无法粘贴剪贴板图片：{ex.Message}", "粘贴图片", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    CanvasState CaptureCanvasState() => new(canvasNodes.Select(n => new CanvasNodeState(n.Id, n.Kind, n.Content, n.Position, n.Size, n.Transparent, n.GroupId, n.VectorAnchors.ToList(), n.Rotation, n.Locked, n.Hidden, n.Name, n.FontSize, n.FontBold, n.FontItalic, n.FontStrikeout, n.FontAlign, n.FontVAlign, n.FontColor, n.FillColor, n.Opacity, n.FillOpacity, n.CornerRadius, n.ClipMask, n.StrokeWidth, n.StrokeColor, n.StrokeAlign, n.StrokeDash, n.AutoLayout, n.AutoLayoutDirection, n.AutoLayoutGap, n.AutoLayoutPadding, n.ComponentId, n.ComponentInstanceId, n.ComponentMasterNodeId, EncodeLayerData(n.Extra))).ToList(), canvasLinks.Select(link => (link.From, link.To, link.Label, link.Color, link.Arrow)).ToList());
    void PushCanvasUndo()
    {
        canvasUndo.Push(CaptureCanvasState());
        if (canvasUndo.Count > 100)
        {
            var keep = canvasUndo.Take(100).Reverse().ToArray();
            canvasUndo.Clear();
            foreach (var state in keep) canvasUndo.Push(state);
        }
        canvasRedo.Clear();
    }
    void RestoreCanvasState(CanvasState state)
    {
        canvasSaveTimer.Stop();
        DisposeCanvasImages();
        canvasNodes.Clear();
        canvasNodes.AddRange(state.Nodes.Select(n=>NodeFromState(n)));
        canvasLinks.Clear();
        canvasLinks.AddRange(state.Links.Select(link => new CanvasLink { From = link.From, To = link.To, Label = link.Label, Color = link.Color, Arrow = link.Arrow }));
        selectedCanvasNode = null;
        selectedCanvasLink = null;
        canvasSelectedIds.Clear(); canvasSelectionStartPositions.Clear();
        canvasVectorEditNodeId = null;
        CancelCanvasInlineEdit();
        canvasSurface.Invalidate();
        RefreshCanvasLayers();
        UpdateCanvasAlignBar();
        SaveCanvas();
    }
    void UndoCanvas()
    {
        if (canvasUndo.Count == 0) return;
        canvasRedo.Push(CaptureCanvasState());
        RestoreCanvasState(canvasUndo.Pop());
    }
    void RedoCanvas()
    {
        if (canvasRedo.Count == 0) return;
        canvasUndo.Push(CaptureCanvasState());
        RestoreCanvasState(canvasRedo.Pop());
    }
    static string EscapeCanvas(string value) => value.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "").Replace("\n", "\\n");
    static string EncodeCanvasAnchors(List<PointF> anchors) => string.Join(";", anchors.Select(point => $"{point.X.ToString(System.Globalization.CultureInfo.InvariantCulture)},{point.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
    static List<PointF> DecodeCanvasAnchors(string value)
    {
        var points = new List<PointF>();
        foreach (var entry in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var coordinates = entry.Split(',', 2);
            if (coordinates.Length == 2 && float.TryParse(coordinates[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) && float.TryParse(coordinates[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y))
                // 锚点允许拖到节点边界外；保存和重新载入都必须保留超出 [0, 1] 的归一化坐标。
                points.Add(new PointF(x, y));
        }
        return points.Count >= 3 ? points : new List<PointF>();
    }
    static string UnescapeCanvas(string value)
    {
        var result = new StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length) { result.Append(value[i]); continue; }
            char next = value[++i];
            result.Append(next == 'n' ? '\n' : next == 't' ? '\t' : next);
        }
        return result.ToString();
    }
    void LoadCanvas()
    {
        Directory.CreateDirectory(canvasBoardsPath);
        if (File.Exists(canvasIndexPath))
        {
            foreach (var line in File.ReadAllLines(canvasIndexPath, Encoding.UTF8))
            {
                var cells = line.Split('\t', 2);
                if (cells.Length == 2 && Guid.TryParse(cells[0], out var boardId))
                {
                    var boardName = UnescapeCanvas(cells[1]).Trim();
                    if (!string.IsNullOrWhiteSpace(boardName) && canvasBoards.All(board => board.Id != boardId)) canvasBoards.Add((boardId, boardName));
                }
            }
        }
        if (canvasBoards.Count == 0)
        {
            activeCanvasId = Guid.NewGuid();
            canvasBoards.Add((activeCanvasId, "主画布"));
            var firstBoardPath = CanvasBoardFile(activeCanvasId);
            if (File.Exists(canvasDataPath) && !File.Exists(firstBoardPath)) File.Copy(canvasDataPath, firstBoardPath);
            SaveCanvasBoardIndex();
        }
        if (File.Exists(currentCanvasPath) && Guid.TryParse(File.ReadAllText(currentCanvasPath, Encoding.UTF8).Trim(), out var savedBoard) && canvasBoards.Any(board => board.Id == savedBoard)) activeCanvasId = savedBoard;
        else activeCanvasId = canvasBoards[0].Id;
        SaveCurrentCanvas();
        UpdateCanvasPicker();
        LoadCanvasBoard(activeCanvasId);
    }
    string CanvasBoardFile(Guid id) => Path.Combine(canvasBoardsPath, $"{id:D}.tsv");
    string CanvasImageFile(Guid nodeId) => Path.Combine(canvasBoardsPath, activeCanvasId.ToString("D"), "images", $"{nodeId:D}.png");
    void SaveCanvasNodeImage(CanvasNode node)
    {
        if (node.Bitmap == null) return;
        var path = CanvasImageFile(node.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        node.Bitmap.Save(path, ImageFormat.Png);
    }
    Image? LoadCanvasNodeImage(Guid nodeId)
    {
        var path = CanvasImageFile(nodeId);
        if (!File.Exists(path)) return null;
        try { using var loaded = Image.FromFile(path); return new Bitmap(loaded); }
        catch { return null; }
    }
    void DisposeCanvasImages()
    {
        DisposeLayerRenderCache();
        foreach (var node in canvasNodes) node.Bitmap?.Dispose();
    }
    void LoadCanvasBoard(Guid id)
    {
        InvalidateCanvasPaintCache();
        DisposeCanvasImages();
        canvasNodes.Clear(); canvasLinks.Clear(); selectedCanvasNode = null; selectedCanvasLink = null; canvasSelectedIds.Clear(); canvasSelectionStartPositions.Clear(); canvasVectorEditNodeId = null;
        CancelCanvasInlineEdit();
        canvasOffset = PointF.Empty; canvasZoom = 1f;
        canvasUndo.Clear(); canvasRedo.Clear();
        string boardFile = CanvasBoardFile(id);
        var savedLines = File.Exists(boardFile) ? File.ReadAllLines(boardFile, Encoding.UTF8) : Array.Empty<string>();
        loadingCanvas = true;
        try
        {
            foreach (var line in savedLines)
            {
        var cells = line.Split('\t', 40);
                if (cells.Length == 3 && cells[0] == "view" && float.TryParse(cells[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ox) && float.TryParse(cells[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float oy)) canvasOffset = new PointF(ox, oy);
                else if (cells.Length == 2 && cells[0] == "zoom" && float.TryParse(cells[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float savedZoom)) canvasZoom = Math.Clamp(savedZoom, 0.1f, 8f);
                else if (cells.Length >= 3 && cells[0] == "link" && Guid.TryParse(cells[1], out var from) && Guid.TryParse(cells[2], out var to))
                {
                    var loadedLink = new CanvasLink { From = from, To = to, Label = cells.Length >= 4 ? UnescapeCanvas(cells[3]) : "" };
                    if (cells.Length >= 6)
                    {
                        loadedLink.Color = cells[4];
                        if (int.TryParse(cells[5], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int linkArrow)) loadedLink.Arrow = Math.Clamp(linkArrow, 0, 3);
                    }
                    canvasLinks.Add(loadedLink);
                }
                else if (cells[0] == "text" || cells[0] == "image" || cells[0]=="path" || IsCanvasShapeKind(cells[0]))
                {
                    Guid? nodeGuid = null;
                    Guid? groupId = null;
                    List<PointF> vectorAnchors = new();
                    Size? size = null;
                    float x, y, rotation = 0; string content;
                    bool locked = false, hidden = false; string nodeName = "";
                    if (cells.Length >= 14 && Guid.TryParse(cells[1], out var nodeId) && float.TryParse(cells[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x) && float.TryParse(cells[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y) && int.TryParse(cells[4], out int width) && int.TryParse(cells[5], out int height))
                    { nodeGuid = nodeId; size = new Size(width, height); content = UnescapeCanvas(cells[8]); if (Guid.TryParse(cells[7], out var parsedGroup)) groupId = parsedGroup; vectorAnchors = DecodeCanvasAnchors(cells[9]); float.TryParse(cells[10], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out rotation); locked = cells[11] == "1"; hidden = cells[12] == "1"; nodeName = UnescapeCanvas(cells[13]); }
                    else if (cells.Length == 13 && Guid.TryParse(cells[1], out nodeId) && float.TryParse(cells[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x) && float.TryParse(cells[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y) && int.TryParse(cells[4], out width) && int.TryParse(cells[5], out height))
                    { nodeGuid = nodeId; size = new Size(width, height); content = UnescapeCanvas(cells[8]); if (Guid.TryParse(cells[7], out var parsedGroup)) groupId = parsedGroup; vectorAnchors = DecodeCanvasAnchors(cells[9]); float.TryParse(cells[10], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out rotation); locked = cells[11] == "1"; hidden = cells[12] == "1"; }
                    else if (cells.Length == 11 && Guid.TryParse(cells[1], out nodeId) && float.TryParse(cells[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x) && float.TryParse(cells[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y) && int.TryParse(cells[4], out width) && int.TryParse(cells[5], out height))
                    { nodeGuid = nodeId; size = new Size(width, height); content = UnescapeCanvas(cells[8]); if (Guid.TryParse(cells[7], out var parsedGroup)) groupId = parsedGroup; vectorAnchors = DecodeCanvasAnchors(cells[9]); float.TryParse(cells[10], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out rotation); }
                    else if (cells.Length == 10 && Guid.TryParse(cells[1], out nodeId) && float.TryParse(cells[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x) && float.TryParse(cells[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y) && int.TryParse(cells[4], out width) && int.TryParse(cells[5], out height))
                    { nodeGuid = nodeId; size = new Size(width, height); content = UnescapeCanvas(cells[8]); if (Guid.TryParse(cells[7], out var parsedGroup)) groupId = parsedGroup; vectorAnchors = DecodeCanvasAnchors(cells[9]); }
                    else if (cells.Length == 9 && Guid.TryParse(cells[1], out nodeId) && float.TryParse(cells[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x) && float.TryParse(cells[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y) && int.TryParse(cells[4], out width) && int.TryParse(cells[5], out height))
                    { nodeGuid = nodeId; size = new Size(width, height); content = UnescapeCanvas(cells[8]); if (Guid.TryParse(cells[7], out var parsedGroupLegacy)) groupId = parsedGroupLegacy; }
                    else if (cells.Length == 8 && Guid.TryParse(cells[1], out nodeId) && float.TryParse(cells[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x) && float.TryParse(cells[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y) && int.TryParse(cells[4], out width) && int.TryParse(cells[5], out height))
                    { nodeGuid = nodeId; size = new Size(width, height); content = UnescapeCanvas(cells[7]); }
                    else if (cells.Length == 7 && Guid.TryParse(cells[1], out nodeId) && float.TryParse(cells[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x) && float.TryParse(cells[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y) && int.TryParse(cells[4], out int oldWidth) && int.TryParse(cells[5], out int oldHeight))
                    { nodeGuid = nodeId; size = new Size(oldWidth, oldHeight); content = UnescapeCanvas(cells[6]); }
                    else if (cells.Length == 6 && float.TryParse(cells[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x) && float.TryParse(cells[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y) && int.TryParse(cells[3], out width) && int.TryParse(cells[4], out height))
                    { size = new Size(width, height); content = UnescapeCanvas(cells[5]); }
                    else if (cells.Length == 4 && float.TryParse(cells[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x) && float.TryParse(cells[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y)) content = UnescapeCanvas(cells[3]);
                    else continue;
                    CreateCanvasNode(cells[0], new PointF(x, y), content, size, nodeGuid);
                    if (cells.Length >= 8) canvasNodes[^1].Transparent = cells[6] == "1";
                    canvasNodes[^1].GroupId = groupId;
                    canvasNodes[^1].VectorAnchors = vectorAnchors;
                    canvasNodes[^1].Rotation = rotation;
                    canvasNodes[^1].Locked = locked;
                    canvasNodes[^1].Hidden = hidden;
                    canvasNodes[^1].Name = nodeName;
                    if(cells.Length>=38)canvasNodes[^1].Extra=DecodeLayerData(cells[37]);
                    if (cells.Length >= 20)
                    {
                        float.TryParse(cells[14], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fontSize);
                        canvasNodes[^1].FontSize = fontSize;
                        canvasNodes[^1].FontBold = cells[15] == "1";
                        canvasNodes[^1].FontItalic = cells[16] == "1";
                        canvasNodes[^1].FontStrikeout = cells[17] == "1";
                        int.TryParse(cells[18], out int fontAlign);
                        canvasNodes[^1].FontAlign = fontAlign;
                        canvasNodes[^1].FontColor = UnescapeCanvas(cells[19]);
                        if (cells.Length >= 21) canvasNodes[^1].FillColor = UnescapeCanvas(cells[20]);
                        if (cells.Length >= 22 && float.TryParse(cells[21], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float nodeOpacity)) canvasNodes[^1].Opacity = Math.Clamp(nodeOpacity, 0f, 1f);
                        if (cells.Length >= 23 && float.TryParse(cells[22], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float nodeRadius)) canvasNodes[^1].CornerRadius = Math.Max(0f, nodeRadius);
                        if (cells.Length >= 24 && int.TryParse(cells[23], out int fontVAlign)) canvasNodes[^1].FontVAlign = fontVAlign;
                        if (cells.Length >= 25) canvasNodes[^1].ClipMask = cells[24] == "1";
                    if (cells.Length >= 29)
                    {
                            if (float.TryParse(cells[25], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float strokeWidth)) canvasNodes[^1].StrokeWidth = Math.Max(0f, strokeWidth);
                            canvasNodes[^1].StrokeColor = UnescapeCanvas(cells[26]);
                            if (int.TryParse(cells[27], out int strokeAlign)) canvasNodes[^1].StrokeAlign = Math.Clamp(strokeAlign, 0, 2);
                        canvasNodes[^1].StrokeDash = cells[28] == "1";
                    }
                        if (cells.Length >= 37 && float.TryParse(cells[36], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fillOpacity)) canvasNodes[^1].FillOpacity = Math.Clamp(fillOpacity, 0f, 1f);
                        if (cells.Length >= 36)
                    {
                        var loadedNode = canvasNodes[^1]; loadedNode.AutoLayout = cells[29] == "1";
                        if (int.TryParse(cells[30], out int layoutDirection)) loadedNode.AutoLayoutDirection = Math.Clamp(layoutDirection, 0, 1);
                        if (float.TryParse(cells[31], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float layoutGap)) loadedNode.AutoLayoutGap = Math.Max(0, layoutGap);
                        if (float.TryParse(cells[32], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float layoutPadding)) loadedNode.AutoLayoutPadding = Math.Max(0, layoutPadding);
                        if (Guid.TryParse(cells[33], out var componentId)) loadedNode.ComponentId = componentId;
                        if (Guid.TryParse(cells[34], out var componentInstanceId)) loadedNode.ComponentInstanceId = componentInstanceId;
                        if (Guid.TryParse(cells[35], out var componentMasterNodeId)) loadedNode.ComponentMasterNodeId = componentMasterNodeId;
                    }
                }
                }
            }
        }
        finally { loadingCanvas = false; }
        canvasSurface.Invalidate();
        RefreshCanvasLayers();
        UpdateCanvasProps();
    }
    void SaveCanvasBoardIndex()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(canvasIndexPath)!);
        File.WriteAllLines(canvasIndexPath, canvasBoards.Select(board => $"{board.Id:D}\t{EscapeCanvas(board.Name)}"), Encoding.UTF8);
    }
    void SaveCurrentCanvas() => File.WriteAllText(currentCanvasPath, activeCanvasId.ToString("D"), Encoding.UTF8);
    void UpdateCanvasPicker()
    {
        var activeBoard = canvasBoards.FirstOrDefault(board => board.Id == activeCanvasId);
        var name = string.IsNullOrEmpty(activeBoard.Name) ? "主画布" : activeBoard.Name;
        canvasPicker.Text = name.Length > 10 ? $"画布：{name[..10]}…  ▾" : $"画布：{name}  ▾";
    }
    void ShowCanvasBoardMenu()
    {
        var menu = new ContextMenuStrip();
        foreach (var board in canvasBoards)
        {
            var item = menu.Items.Add(board.Id == activeCanvasId ? $"✓  {board.Name}" : board.Name);
            item.Click += (_, _) => SwitchCanvasBoard(board.Id);
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("＋ 新建画布", null, (_, _) => PromptCreateCanvasBoard());
        menu.Items.Add("重命名画布", null, (_, _) => PromptRenameCanvasBoard());
        menu.Items.Add("复制画布", null, (_, _) => DuplicateCanvasBoard());
        menu.Items.Add("导出当前画布为 PNG…", null, (_, _) => ExportCanvasPng());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("删除当前画布", null, (_, _) => DeleteActiveCanvasBoard());
        menu.Show(canvasPicker, 0, -menu.Height);
    }
    void PromptCreateCanvasBoard()
    {
        using var dialog = new Form { Text = "新建画布", StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(340, 132), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false };
        var input = new TextBox { Location = new Point(16, 16), Size = new Size(308, 30), Font = new Font(UiFontName(), 10), PlaceholderText = "输入画布名称" };
        var create = Button("创建", Color.FromArgb(39, 37, 43), Color.White); create.Location = new Point(16, 68); create.Size = new Size(308, 42); create.Region = RoundedRegion(create, 10);
        create.Click += (_, _) =>
        {
            var name = input.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) { MessageBox.Show(dialog, "请输入画布名称。", "新建画布", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (canvasBoards.Any(board => board.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { MessageBox.Show(dialog, "这个画布名称已存在。", "新建画布", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            dialog.Tag = name; dialog.DialogResult = DialogResult.OK; dialog.Close();
        };
        dialog.Controls.Add(input); dialog.Controls.Add(create); dialog.AcceptButton = create;
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Tag is string newName)
        {
            SaveCanvas();
            var id = Guid.NewGuid();
            canvasBoards.Add((id, newName));
            SaveCanvasBoardIndex();
            SwitchCanvasBoard(id);
        }
    }
    void PromptRenameCanvasBoard()
    {
        var activeBoard = canvasBoards.FirstOrDefault(board => board.Id == activeCanvasId);
        using var dialog = new Form { Text = "重命名画布", StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(340, 132), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false };
        var input = new TextBox { Location = new Point(16, 16), Size = new Size(308, 30), Font = new Font(UiFontName(), 10), Text = activeBoard.Name, PlaceholderText = "输入画布名称" };
        var save = Button("保存", Color.FromArgb(39, 37, 43), Color.White); save.Location = new Point(16, 68); save.Size = new Size(308, 42); save.Region = RoundedRegion(save, 10);
        save.Click += (_, _) =>
        {
            var name = input.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) { MessageBox.Show(dialog, "请输入画布名称。", "重命名画布", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (canvasBoards.Any(board => board.Id != activeCanvasId && board.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { MessageBox.Show(dialog, "这个画布名称已存在。", "重命名画布", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            dialog.Tag = name; dialog.DialogResult = DialogResult.OK; dialog.Close();
        };
        dialog.Controls.Add(input); dialog.Controls.Add(save); dialog.AcceptButton = save;
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Tag is string newName)
        {
            int index = canvasBoards.FindIndex(board => board.Id == activeCanvasId);
            if (index >= 0)
            {
                canvasBoards[index] = (activeCanvasId, newName);
                SaveCanvasBoardIndex();
                UpdateCanvasPicker();
            }
        }
    }
    void DuplicateCanvasBoard()
    {
        var activeBoard = canvasBoards.FirstOrDefault(board => board.Id == activeCanvasId);
        SaveCanvas();
        var newId = Guid.NewGuid();
        var newName = $"{activeBoard.Name} 副本";
        int suffix = 1;
        while (canvasBoards.Any(board => board.Name == newName)) { suffix++; newName = $"{activeBoard.Name} 副本 {suffix}"; }
        canvasBoards.Add((newId, newName));
        SaveCanvasBoardIndex();
        var sourceFile = CanvasBoardFile(activeCanvasId);
        var targetFile = CanvasBoardFile(newId);
        if (File.Exists(sourceFile)) File.Copy(sourceFile, targetFile);
        var sourceImageFolder = Path.Combine(canvasBoardsPath, activeCanvasId.ToString("D"), "images");
        var targetImageFolder = Path.Combine(canvasBoardsPath, newId.ToString("D"), "images");
        if (Directory.Exists(sourceImageFolder))
        {
            Directory.CreateDirectory(targetImageFolder);
            foreach (var file in Directory.GetFiles(sourceImageFolder))
                File.Copy(file, Path.Combine(targetImageFolder, Path.GetFileName(file)));
        }
        SwitchCanvasBoard(newId);
    }
    void SwitchCanvasBoard(Guid id)
    {
        if (id == activeCanvasId || !canvasBoards.Any(board => board.Id == id)) return;
        canvasSaveTimer.Stop();
        SaveCanvas();
        activeCanvasId = id;
        SaveCurrentCanvas();
        LoadCanvasBoard(id);
        UpdateCanvasPicker();
    }
    void DeleteActiveCanvasBoard()
    {
        if (canvasBoards.Count == 0) return;
        var board = canvasBoards.FirstOrDefault(item => item.Id == activeCanvasId);
        var boardName = string.IsNullOrWhiteSpace(board.Name) ? "当前画布" : board.Name;
        if (MessageBox.Show(this, $"确定删除画布“{boardName}”及其内容吗？此操作无法撤销。", "删除画布", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        canvasSaveTimer.Stop();
        SaveCanvas();
        var deletedId = activeCanvasId;
        canvasBoards.RemoveAll(item => item.Id == deletedId);
        var boardFile = CanvasBoardFile(deletedId);
        if (File.Exists(boardFile)) File.Delete(boardFile);
        var imageFolder = Path.Combine(canvasBoardsPath, deletedId.ToString("D"), "images");
        if (Directory.Exists(imageFolder)) Directory.Delete(imageFolder, true);

        if (canvasBoards.Count == 0)
        {
            activeCanvasId = Guid.NewGuid();
            canvasBoards.Add((activeCanvasId, "主画布"));
        }
        else activeCanvasId = canvasBoards[0].Id;

        SaveCanvasBoardIndex();
        SaveCurrentCanvas();
        LoadCanvasBoard(activeCanvasId);
        UpdateCanvasPicker();
    }
    void SaveCanvas()
    {
        InvalidateCanvasPaintCache();
        SyncCanvasComponentInstances();
        Directory.CreateDirectory(canvasBoardsPath);
        var lines = new List<string> { $"view\t{canvasOffset.X.ToString(System.Globalization.CultureInfo.InvariantCulture)}\t{canvasOffset.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)}", $"zoom\t{canvasZoom.ToString(System.Globalization.CultureInfo.InvariantCulture)}" };
        lines.AddRange(canvasNodes.Select(n => $"{n.Kind}\t{n.Id:D}\t{n.Position.X.ToString(System.Globalization.CultureInfo.InvariantCulture)}\t{n.Position.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)}\t{n.Size.Width}\t{n.Size.Height}\t{(n.Transparent ? 1 : 0)}\t{n.GroupId?.ToString("D") ?? ""}\t{EscapeCanvas(n.Content)}\t{EncodeCanvasAnchors(n.VectorAnchors)}\t{n.Rotation.ToString(System.Globalization.CultureInfo.InvariantCulture)}\t{(n.Locked ? 1 : 0)}\t{(n.Hidden ? 1 : 0)}\t{EscapeCanvas(n.Name)}\t{n.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}\t{(n.FontBold ? 1 : 0)}\t{(n.FontItalic ? 1 : 0)}\t{(n.FontStrikeout ? 1 : 0)}\t{n.FontAlign}\t{EscapeCanvas(n.FontColor)}\t{EscapeCanvas(n.FillColor)}\t{n.Opacity.ToString(System.Globalization.CultureInfo.InvariantCulture)}\t{n.CornerRadius.ToString(System.Globalization.CultureInfo.InvariantCulture)}\t{n.FontVAlign}\t{(n.ClipMask ? 1 : 0)}\t{n.StrokeWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)}\t{EscapeCanvas(n.StrokeColor)}\t{n.StrokeAlign}\t{(n.StrokeDash ? 1 : 0)}\t{(n.AutoLayout ? 1 : 0)}\t{n.AutoLayoutDirection}\t{n.AutoLayoutGap.ToString(System.Globalization.CultureInfo.InvariantCulture)}\t{n.AutoLayoutPadding.ToString(System.Globalization.CultureInfo.InvariantCulture)}\t{n.ComponentId?.ToString("D") ?? ""}\t{n.ComponentInstanceId?.ToString("D") ?? ""}\t{n.ComponentMasterNodeId?.ToString("D") ?? ""}\t{n.FillOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture)}\t{EncodeLayerData(n.Extra)}"));
        lines.AddRange(canvasLinks.Where(link => canvasNodes.Any(n => n.Id == link.From) && canvasNodes.Any(n => n.Id == link.To)).Select(link => $"link\t{link.From:D}\t{link.To:D}\t{EscapeCanvas(link.Label)}\t{link.Color}\t{link.Arrow}"));
        File.WriteAllLines(CanvasBoardFile(activeCanvasId), lines, Encoding.UTF8);
    }
    static RectangleF UnionCanvasBounds(RectangleF a, RectangleF b) => a.IsEmpty ? b : RectangleF.Union(a, b);
    void ExportCanvasPng() => ExportCanvasPng(null);
    void ExportSelectedCanvasControls()
    {
        var selected = CanvasSelectedNodes().Where(node => !node.Hidden).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "请先选择要导出的控件。", "导出选中控件", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ExportCanvasPng(selected.Select(node => node.Id).ToHashSet());
    }
    void ExportCanvasPng(HashSet<Guid>? includedNodeIds)
    {
        var exportNodes = includedNodeIds == null
            ? canvasNodes.Where(node => !node.Hidden).ToList()
            : canvasNodes.Where(node => includedNodeIds.Contains(node.Id) && !node.Hidden).ToList();
        if (exportNodes.Count == 0)
        {
            var selectedOnly = includedNodeIds != null;
            MessageBox.Show(this, selectedOnly ? "当前没有可导出的选中控件。" : "当前画布是空的，还没有可以导出的内容。",
                selectedOnly ? "导出选中控件" : "导出画布", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var board = canvasBoards.FirstOrDefault(item => item.Id == activeCanvasId);
        var fileName = includedNodeIds == null
            ? (string.IsNullOrWhiteSpace(board.Name) ? "画布" : board.Name)
            : exportNodes.Count == 1 && !string.IsNullOrWhiteSpace(exportNodes[0].Name) ? exportNodes[0].Name : "选中控件";
        foreach (var invalid in Path.GetInvalidFileNameChars()) fileName = fileName.Replace(invalid, '_');
        var exportTitle = includedNodeIds == null ? "导出画布为 PNG" : "导出选中控件为 PNG";
        using var dialog = new SaveFileDialog { Filter = "PNG 图片 (*.png)|*.png", Title = exportTitle, FileName = fileName + ".png", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        const float exportScale = 2f;
        const int maxDimension = 16000;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        float outerStrokePad = 0f;
        foreach (var node in exportNodes)
        {
            if (IsCanvasShapeKind(node) && node.StrokeWidth > 0.01f)
            {
                float half = node.StrokeAlign == 1 ? node.StrokeWidth / 2f : node.StrokeAlign == 2 ? node.StrokeWidth : 0f;
                outerStrokePad = Math.Max(outerStrokePad, half);
            }
            var center = new PointF(node.Position.X + node.Size.Width / 2f, node.Position.Y + node.Size.Height / 2f);
            var corners = new[]
            {
                new PointF(node.Position.X, node.Position.Y),
                new PointF(node.Position.X + node.Size.Width, node.Position.Y),
                new PointF(node.Position.X + node.Size.Width, node.Position.Y + node.Size.Height),
                new PointF(node.Position.X, node.Position.Y + node.Size.Height)
            };
            foreach (var corner in corners)
            {
                var point = IsCanvasRotatable(node) ? RotateCanvasPoint(corner, center, node.Rotation) : corner;
                minX = Math.Min(minX, point.X); minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X); maxY = Math.Max(maxY, point.Y);
            }
            if (IsCanvasShapeKind(node) && node.VectorAnchors.Count >= 3)
            {
                foreach (var anchor in CanvasAnchors(node))
                {
                    var point = new PointF(node.Position.X + anchor.X * node.Size.Width, node.Position.Y + anchor.Y * node.Size.Height);
                    if (IsCanvasRotatable(node)) point = RotateCanvasPoint(point, center, node.Rotation);
                    minX = Math.Min(minX, point.X); minY = Math.Min(minY, point.Y);
                    maxX = Math.Max(maxX, point.X); maxY = Math.Max(maxY, point.Y);
                }
            }
        }
        float worldWidth = Math.Max(1, maxX - minX), worldHeight = Math.Max(1, maxY - minY);
        float scale = exportScale;
        int padding = 48 + (int)Math.Ceiling(outerStrokePad * exportScale) + 4;
        if (worldWidth * scale + padding * 2 > maxDimension || worldHeight * scale + padding * 2 > maxDimension)
            scale = Math.Max(0.25f, (maxDimension - padding * 2) / Math.Max(worldWidth, worldHeight));

        var savedZoom = canvasZoom;
        var savedOffset = canvasOffset;
        canvasSaveTimer.Stop();
        canvasZoom = scale;
        canvasOffset = new PointF(padding - minX * scale, padding - minY * scale);
        var union = RectangleF.Empty;
        foreach (var node in exportNodes)
        {
            var nodeBounds = (RectangleF)CanvasVisualBounds(node);
            nodeBounds.Inflate(6, 6);
            union = UnionCanvasBounds(union, nodeBounds);
        }
        var exportLinks = includedNodeIds == null
            ? canvasLinks
            : canvasLinks.Where(link => includedNodeIds.Contains(link.From) && includedNodeIds.Contains(link.To)).ToList();
        foreach (var link in exportLinks)
        {
            var linkBounds = (RectangleF)CanvasLinkBounds(link);
            if (!linkBounds.IsEmpty) union = UnionCanvasBounds(union, linkBounds);
        }
        canvasOffset = new PointF(canvasOffset.X + padding - union.Left, canvasOffset.Y + padding - union.Top);
        int width = Math.Max(1, (int)Math.Ceiling(union.Width) + padding * 2);
        int height = Math.Max(1, (int)Math.Ceiling(union.Height) + padding * 2);

        Bitmap? bitmap = null;
        try
        {
            canvasExporting = true;
            canvasExportNodeIds = includedNodeIds;
            bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                DrawCanvas(graphics, new Rectangle(0, 0, width, height));
            }
            bitmap.Save(dialog.FileName, ImageFormat.Png);
            MessageBox.Show(this, $"PNG 已导出：\n{dialog.FileName}", "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"导出失败：{ex.Message}", "导出画布", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            canvasExporting = false;
            canvasExportNodeIds = null;
            canvasZoom = savedZoom;
            canvasOffset = savedOffset;
            bitmap?.Dispose();
            canvasSurface.Invalidate();
        }
    }
    static string UiFontName()=>"Microsoft YaHei UI";
    static Label Label(string text,int size,FontStyle style,Color color)=>new(){Text=text,Font=new Font(UiFontName(),size,style),ForeColor=color};
    static Color Blend(Color from, Color to, float amount) => Color.FromArgb(from.A, (int)(from.R + (to.R - from.R) * amount), (int)(from.G + (to.G - from.G) * amount), (int)(from.B + (to.B - from.B) * amount));
    static Button Button(string text, Color back, Color fore)
    {
        var button = new Button { Text = text, BackColor = back, ForeColor = fore, FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 }, Font = new Font(UiFontName(), 11, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter, UseCompatibleTextRendering = true, Padding = Padding.Empty };
        Color resting = back; bool hovered = false, pressed = false;
        void UpdateButtonState() => button.BackColor = pressed ? Blend(resting, Color.Black, 0.08f) : hovered ? Blend(resting, Color.White, 0.09f) : resting;
        button.MouseEnter += (_, _) => { resting = button.BackColor; hovered = true; UpdateButtonState(); };
        button.MouseLeave += (_, _) => { hovered = false; UpdateButtonState(); };
        button.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { pressed = true; UpdateButtonState(); } };
        button.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) { pressed = false; hovered = button.ClientRectangle.Contains(button.PointToClient(Cursor.Position)); UpdateButtonState(); } };
        return button;
    }
    Region RoundedRegion(Control control, int radius)
    {
        if (control.Width <= 0 || control.Height <= 0) return new Region(Rectangle.Empty);
        // WinUI corner tokens: 4 px for controls, 8 px for containers. Search remains pill shaped.
        int token = control is Button ? 4 : control is Form ? 8 : Math.Min(radius, 8);
        int r = Math.Clamp(token, 0, Math.Min(control.Width, control.Height) / 2);
        if (r == 0) return new Region(new Rectangle(0, 0, control.Width, control.Height));
        using var path = new GraphicsPath();
        int d = r * 2;
        var rect = new Rectangle(0, 0, control.Width, control.Height);
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return new Region(path);
    }
    void UpdateClock(){var elapsed=DateTime.Now-openedAt;timerLabel.Text=$"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";}
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (sheetSelected && sheetGrid.ContainsFocus && !sheetGrid.IsCurrentCellInEditMode)
        {
            var operation = keyData switch { Keys.F1 => 1, Keys.F2 => 2, Keys.F3 => 3, Keys.F4 => 4, _ => 0 };
            if (operation != 0 && sheetGrid.SelectedCells.Count > 0) { ApplySheetShortcut(operation); return true; }
        }
        if (sheetSelected) return base.ProcessCmdKey(ref msg, keyData);
        if (canvasInlineEditor.Visible)
        {
            if (keyData == Keys.Escape) { CancelCanvasInlineEdit(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        bool canvasEditingFocus = ActiveControl is TextBoxBase or NumericUpDown or ComboBox || (!canvasSurface.ContainsFocus && NativeTextInputHasFocus());
        if(canvasEditingFocus)return base.ProcessCmdKey(ref msg,keyData);
        if(CanvasWorkspaceKey(keyData))return true;
        if (organizeSelected && !canvasEditingFocus && keyData == (Keys.Control | Keys.Z)) { UndoCanvas(); return true; }
        if (organizeSelected && !canvasEditingFocus && keyData == (Keys.Control | Keys.Shift | Keys.Z)) { RedoCanvas(); return true; }
        if (organizeSelected && !canvasEditingFocus && keyData == (Keys.Control | Keys.C)) { CopyCanvasSelection(); return true; }
        if (organizeSelected && !canvasEditingFocus && keyData == (Keys.Control | Keys.X)) { CutCanvasSelection(); return true; }
        if (organizeSelected && !canvasEditingFocus && keyData == (Keys.Control | Keys.A)) { SelectAllCanvasNodes(); return true; }
        if (organizeSelected && !canvasEditingFocus && keyData == (Keys.Control | Keys.D)) { DuplicateCanvasSelection(); return true; }
        if (organizeSelected && !canvasEditingFocus && keyData == (Keys.Control | Keys.V))
        {
            int sequence = GetSystemClipboardSequence();
            // 复制控件之后若系统剪贴板出现新图片（如刚截图），优先贴图片，否则贴画布控件
            if (sequence >= 0 && sequence != canvasClipboardSequence && Clipboard.ContainsImage()) PasteCanvasImage();
            else if (canvasClipboardNodes != null) PasteCanvasClipboard();
            else PasteCanvasImage();
            return true;
        }
        if (organizeSelected && !canvasEditingFocus && nativePopupDepth == 0 && (keyData == Keys.Delete || keyData == Keys.Back))
        {
            if (selectedCanvasLink != null) DeleteSelectedCanvasLink();
            else DeleteCanvasSelection();
            return true;
        }
        if (organizeSelected && !canvasEditingFocus && keyData == Keys.Escape)
        {
            if (selectedCanvasLink != null) SetCanvasLinkSelection(null);
            else if (canvasVectorEditNodeId.HasValue) canvasVectorEditNodeId = null;
            else SetCanvasSelection(Array.Empty<CanvasNode>());
            UpdateCanvasCursor();
            canvasSurface.Invalidate();
            if (canvasLayersPanel.Visible) RefreshCanvasLayers();
            return true;
        }
        if (keyData == Keys.Space) { if (organizeSelected) { canvasSpaceDown = true; UpdateCanvasCursor(); return true; } if(!nativeReady||nativePage==0){Advance(false);return true;} }
        if (!organizeSelected && (!nativeReady || nativePage == 0) && !canvasEditingFocus && keyData == Keys.Enter) { Advance(true); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    void LoadTitle(){var titlePath=Path.Combine(AppDataStorage.Root,"cardtitle.txt");if(File.Exists(titlePath)){var value=File.ReadAllText(titlePath,Encoding.UTF8).Trim();if(!string.IsNullOrWhiteSpace(value))typeLabel.Text=value;}}
    void SaveTitle(string value){var titlePath=Path.Combine(AppDataStorage.Root,"cardtitle.txt");Directory.CreateDirectory(Path.GetDirectoryName(titlePath)!);File.WriteAllText(titlePath,value,Encoding.UTF8);}
    void EditCardTitle(){using var editor=new Form{Text="修改卡牌文字",StartPosition=FormStartPosition.CenterParent,ClientSize=new Size(360,135),FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,ShowInTaskbar=false};var input=new TextBox{Location=new Point(20,20),Size=new Size(320,32),Text=typeLabel.Text};var save=Button("保存",Color.FromArgb(39,37,43),Color.White);save.Location=new Point(20,75);save.Size=new Size(320,40);save.TextAlign=ContentAlignment.MiddleCenter;save.Click+=(_,_)=>{var value=input.Text.Trim();if(!string.IsNullOrWhiteSpace(value)){typeLabel.Text=value;SaveTitle(value);typeLabel.Visible=true;editor.DialogResult=DialogResult.OK;editor.Close();}};editor.Controls.Add(input);editor.Controls.Add(save);editor.AcceptButton=save;editor.Shown+=(_,_)=>input.SelectAll();editor.ShowDialog(this);}
    void SetCardFont(Font next)
    {
        // WinForms keeps the existing Font instance when the new value compares equal.
        if (ReferenceEquals(word.Font, next)) return;
        if (word.Font.Equals(next)) { next.Dispose(); return; }
        var previous = currentCardFont; currentCardFont = next; word.Font = next;
        if (previous != null && !ReferenceEquals(previous, next)) previous.Dispose();
    }
    void RenderCard()
    {
        wordSheetIndicator.Visible = wordSheetId != Guid.Empty && wordSheetActive;
        string nextKey;
        if (cards.Count == 0)
        {
            index.Text = "";
            string emptyText = wordSheetId == Guid.Empty || !wordSheetActive ? "使用EXCEL或者添加词库" : "在表格词库的 A/B/C 列填写顶部、中间和底部文字";
            SetCardFont(new Font(UiFontName(), 24, FontStyle.Bold)); word.Text = emptyText; hint.Text = ""; tag.Text = ""; typeLabel.Visible = true;
            nextKey = emptyText;
        }
        else
        {
            int deckPosition = current % cards.Count; var item = cards[order[deckPosition % order.Count]];
            string details = showingAnswer ? (string.IsNullOrWhiteSpace(item.Note) ? item.Meaning : $"{item.Meaning}\n{item.Note}") : "";
            SetCardFont(FitFont(item.Word)); word.Text = WrapText(item.Word, word.Font, 570);
            hint.Text = string.IsNullOrWhiteSpace(details) ? "" : WrapText(details, hint.Font, 570);
            typeLabel.Visible = true; tag.Text = ""; index.Text = "";
            nextKey = $"{item.Word}\u001f{details}";
        }
        if (lastRenderedCardKey != null && !string.Equals(lastRenderedCardKey, nextKey, StringComparison.Ordinal)) StartCardReveal();
        lastRenderedCardKey = nextKey;
    }
    void StartCardReveal()
    {
        if (nativeReady) return;
        if (cardTransitionTimer.Enabled)
        {
            card.Top = cardRestingTop;
            cardShadow.Top = cardShadowRestingTop;
        }
        cardRestingTop = card.Top;
        cardShadowRestingTop = cardShadow.Top;
        int lift = Math.Max(6, (int)Math.Round(DeviceDpi / 12f));
        card.Top = cardRestingTop + lift;
        cardShadow.Top = cardShadowRestingTop + lift;
        cardTransitionStarted = Environment.TickCount64; cardCurtain.Alpha = 230; cardCurtain.Visible = true; cardCurtain.BringToFront(); cardTransitionTimer.Stop(); cardTransitionTimer.Start();
    }
    void AnimateCardReveal()
    {
        float t = Math.Clamp((Environment.TickCount64 - cardTransitionStarted) / 190f, 0f, 1f);
        float eased = 1f - MathF.Pow(1f - t, 3f);
        int lift = Math.Max(6, (int)Math.Round(DeviceDpi / 12f));
        int remainingLift = (int)MathF.Round(lift * (1f - eased));
        card.Top = cardRestingTop + remainingLift;
        cardShadow.Top = cardShadowRestingTop + remainingLift;
        cardCurtain.Alpha = (int)MathF.Round(230f * (1f - eased)); cardCurtain.Invalidate();
        if (t >= 1f) { card.Top = cardRestingTop; cardShadow.Top = cardShadowRestingTop; cardCurtain.Alpha = 0; cardCurtain.Visible = false; cardTransitionTimer.Stop(); }
    }
    Font FitFont(string text){int size=text.Length>24?16:text.Length>16?20:text.Length>10?24:28;return new Font(UiFontName(),size,FontStyle.Bold);}
    string WrapText(string text, Font font, int maxWidth){using var g=CreateGraphics();var result=new StringBuilder();var line=new StringBuilder();foreach(var ch in text){var candidate=line.ToString()+ch;if(g.MeasureString(candidate,font).Width>maxWidth&&line.Length>0){result.AppendLine(line.ToString());line.Clear();}line.Append(ch);}result.Append(line);return result.ToString();}
    void Flip(){showingAnswer=!showingAnswer;RenderCard();}
    void Advance(bool remembered){if(cards.Count==0)return;var c=cards[order[current%order.Count]];if(remembered&&!showingAnswer){showingAnswer=true;instruction.Visible=false;RenderCard();return;}if(remembered)known++;else{repeats++;var existing=wrongCards.FindIndex(x=>x.Word.Equals(c.Word,StringComparison.OrdinalIgnoreCase));if(existing>=0){var old=wrongCards[existing];wrongCards[existing]=(old.Word,old.Meaning,old.Count+1);}else wrongCards.Add((c.Word,c.Meaning,1));SaveWrongBook();RefreshWrongBook();}current++;showingAnswer=false;RenderCard();}
    void ResetOrder(){order.Clear();for(int i=0;i<cards.Count;i++)order.Add(i);if(randomMode){var rng=new Random();for(int i=order.Count-1;i>0;i--){int j=rng.Next(i+1);(order[i],order[j])=(order[j],order[i]);}}}
    void ShowDifficultyFilter(Button owner){var menu=new ContextMenuStrip();menu.Items.Add("全部单词",null,(_,_)=>SetDifficultyFilter(-1));menu.Items.Add("只出现 1 个对勾的单词",null,(_,_)=>SetDifficultyFilter(1));menu.Items.Add("只出现 3 个对勾的单词",null,(_,_)=>SetDifficultyFilter(3));menu.Show(owner,0,owner.Height);}
    void SetDifficultyFilter(int difficulty){cards.Clear();cards.AddRange(difficulty<0?reviewSource:reviewSource.Where(c=>c.Difficulty==difficulty));current=0;showingAnswer=false;instruction.Visible=true;ResetOrder();RenderCard();}
    void LoadWordBank(){if(!File.Exists(dataPath)){reviewSource.Clear();RefreshWordBank();return;}savedCards.Clear();foreach(var line in File.ReadLines(dataPath,Encoding.UTF8)){var cells=line.Split('\t');if(cells.Length>=2&&!string.IsNullOrWhiteSpace(cells[0])&&!string.IsNullOrWhiteSpace(cells[1])){int.TryParse(cells.Length>=4?cells[3]:"0",out var difficulty);savedCards.Add((cells[0].Trim(),"本地词库",cells[1].Trim(),"自定义",cells.Length>=3?cells[2].Trim():"",difficulty));}}reviewSource.Clear();reviewSource.AddRange(savedCards);cards.Clear();cards.AddRange(reviewSource);RefreshWordBank();}
    void SaveWordBank(){var folder=Path.GetDirectoryName(dataPath)!;Directory.CreateDirectory(folder);File.WriteAllLines(dataPath,savedCards.Select(c=>$"{c.Word.Replace("\t"," ").Replace("\r"," ").Replace("\n"," ")}\t{c.Meaning.Replace("\t"," ").Replace("\r"," ").Replace("\n"," ")}\t{c.Note.Replace("\t"," ").Replace("\r"," ").Replace("\n"," ")}\t{c.Difficulty}"),Encoding.UTF8);}
    void AddWord(){var english=englishInput.Text.Trim();var chinese=chineseInput.Text.Trim();var note=noteInput.Text.Trim();if(string.IsNullOrWhiteSpace(english)||string.IsNullOrWhiteSpace(chinese)){MessageBox.Show("请同时输入英语单词和中文翻译。","添加词库",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}int difficulty=(difficulty1.Checked?1:0)+(difficulty2.Checked?1:0)+(difficulty3.Checked?1:0);var item=(english,"本地词库",chinese,"自定义",note,difficulty);savedCards.Add(item);reviewSource.Add(item);cards.Add(item);SaveWordBank();ResetOrder();current=0;RefreshWordBank();RenderCard();englishInput.Clear();chineseInput.Clear();noteInput.Clear();difficulty1.Checked=difficulty2.Checked=difficulty3.Checked=false;}
    void RefreshWordBank(){wordBankList.Controls.Clear();for(int n=0;n<savedCards.Count;n++){var item=savedCards[n];if(!string.IsNullOrWhiteSpace(wordBankSearch)&&!$"{item.Word}\n{item.Meaning}\n{item.Note}".Contains(wordBankSearch,StringComparison.CurrentCultureIgnoreCase))continue;var difficulty=item.Difficulty>0?$"难度：{new string('✓',item.Difficulty)}":"";var display=$"{item.Word}\n{item.Meaning}\n{item.Note}\n{difficulty}";var row=new Panel{Width=280,Height=104,BackColor=Color.White,Margin=new Padding(0,0,0,7)};var text=Label(display,8,FontStyle.Regular,Color.FromArgb(70,67,65));text.Location=new(8,5);text.Size=new(220,92);var remove=Button("×",Color.White,Color.FromArgb(190,105,80));remove.Font=new Font(UiFontName(),10,FontStyle.Bold);remove.Location=new(240,39);remove.Size=new(25,26);remove.TextAlign=ContentAlignment.MiddleCenter;int itemIndex=n;remove.Click+=(_,_)=>{var removed=savedCards[itemIndex];savedCards.RemoveAt(itemIndex);reviewSource.RemoveAll(x=>x.Word.Equals(removed.Word,StringComparison.OrdinalIgnoreCase)&&x.Meaning==removed.Meaning);cards.RemoveAll(x=>x.Word.Equals(removed.Word,StringComparison.OrdinalIgnoreCase)&&x.Meaning==removed.Meaning);SaveWordBank();current=Math.Min(current,Math.Max(0,cards.Count-1));ResetOrder();RefreshWordBank();RenderCard();};row.Controls.Add(text);row.Controls.Add(remove);wordBankList.Controls.Add(row);WireDrag(row,text,wordBankList,itemIndex,true);WireContextMenu(row,wordBankList,itemIndex,true);}}
    void RefreshWrongBook(){wrongList.Controls.Clear();for(int n=0;n<wrongCards.Count;n++){var item=wrongCards[n];var row=new Panel{Width=255,Height=72,BackColor=Color.White,Margin=new Padding(0,0,0,7)};var remove=Button("×",Color.White,Color.FromArgb(190,105,80));remove.Font=new Font(UiFontName(),10,FontStyle.Bold);remove.Location=new(5,23);remove.Size=new(24,26);remove.TextAlign=ContentAlignment.MiddleCenter;int itemIndex=n;remove.Click+=(_,_)=>{wrongCards.RemoveAt(itemIndex);SaveWrongBook();RefreshWrongBook();};var meaning=hideWrongMeaning?"••••••":item.Meaning;var text=Label($"{item.Word}\n{meaning}",9,FontStyle.Regular,Color.FromArgb(70,67,65));text.Location=new(35,6);text.Size=new(140,58);var count=Label($"×{item.Count}",9,FontStyle.Bold,Color.FromArgb(190,105,80));count.Location=new(170,23);count.Size=new(40,26);count.TextAlign=ContentAlignment.MiddleCenter;var check=new CheckBox{Location=new(215,23),AutoSize=true,Checked=false};row.Controls.Add(remove);row.Controls.Add(text);row.Controls.Add(count);row.Controls.Add(check);wrongList.Controls.Add(row);WireDrag(row,text,wrongList,itemIndex,false);WireContextMenu(row,wrongList,itemIndex,false);}}
    void WireContextMenu(Control row, FlowLayoutPanel list, int index, bool bank){var menu=new ContextMenuStrip();var top=menu.Items.Add("移到最顶部");var bottom=menu.Items.Add("移到最底部");top.Click+=(_,_)=>MoveItem(index,true);bottom.Click+=(_,_)=>MoveItem(index,false);row.ContextMenuStrip=menu;void MoveItem(int itemIndex,bool toTop){if(bank){if(itemIndex<0||itemIndex>=savedCards.Count)return;var item=savedCards[itemIndex];savedCards.RemoveAt(itemIndex);savedCards.Insert(toTop?0:savedCards.Count,item);SaveWordBank();RefreshWordBank();}else{if(itemIndex<0||itemIndex>=wrongCards.Count)return;var item=wrongCards[itemIndex];wrongCards.RemoveAt(itemIndex);wrongCards.Insert(toTop?0:wrongCards.Count,item);SaveWrongBook();RefreshWrongBook();}}}
    void WireDrag(Control row, Control text, FlowLayoutPanel list, int index, bool bank){Point start=Point.Empty;bool moving=false;MouseEventHandler down=(_,e)=>{if(e.Button==MouseButtons.Left){start=Cursor.Position;moving=false;}};MouseEventHandler move=(_,e)=>{if(e.Button==MouseButtons.Left&&Math.Abs(Cursor.Position.Y-start.Y)>8)moving=true;};MouseEventHandler up=(_,e)=>{if(!moving)return;var target=list.GetChildAtPoint(list.PointToClient(Cursor.Position));if(target==null||target==row)return;int targetIndex=list.Controls.IndexOf(target);if(targetIndex<0||targetIndex==index)return;if(bank&&index<savedCards.Count&&targetIndex<savedCards.Count){(savedCards[index],savedCards[targetIndex])=(savedCards[targetIndex],savedCards[index]);SaveWordBank();RefreshWordBank();}else if(!bank&&index<wrongCards.Count&&targetIndex<wrongCards.Count){(wrongCards[index],wrongCards[targetIndex])=(wrongCards[targetIndex],wrongCards[index]);SaveWrongBook();RefreshWrongBook();}};foreach(var control in row.Controls.Cast<Control>().Where(c=>c!=row.Controls[0]).Prepend(row).Append(text).Distinct()){control.MouseDown+=down;control.MouseMove+=move;control.MouseUp+=up;} }
    void LoadWrongBook(){if(!File.Exists(wrongDataPath)){RefreshWrongBook();return;}wrongCards.Clear();foreach(var line in File.ReadLines(wrongDataPath,Encoding.UTF8)){var cells=line.Split('\t');if(cells.Length>=3&&int.TryParse(cells[2],out var count)&&count>0)wrongCards.Add((cells[0],cells[1],count));}RefreshWrongBook();}
    void SaveWrongBook(){var folder=Path.GetDirectoryName(wrongDataPath)!;Directory.CreateDirectory(folder);File.WriteAllLines(wrongDataPath,wrongCards.Select(x=>$"{x.Word.Replace("\t"," ").Replace("\r"," ").Replace("\n"," ")}\t{x.Meaning.Replace("\t"," ").Replace("\r"," ").Replace("\n"," ")}\t{x.Count}"),Encoding.UTF8);}
    void ResetAll(){if(MessageBox.Show("确定要删除全部单词库、进度和错题本吗？此操作无法撤销。","确认重置",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;cards.Clear();savedCards.Clear();reviewSource.Clear();wrongCards.Clear();order.Clear();current=0;known=0;repeats=0;showingAnswer=false;instruction.Visible=true;SaveWordBank();SaveWrongBook();RefreshWordBank();RefreshWrongBook();RenderCard();}
    void ImportFile(){using var dialog=new OpenFileDialog{Filter="Excel 文件 (*.xlsx;*.csv)|*.xlsx;*.csv|所有文件 (*.*)|*.*",Title="选择单词卡片文件"};if(dialog.ShowDialog()!=DialogResult.OK)return;try{var imported=Path.GetExtension(dialog.FileName).Equals(".csv",StringComparison.OrdinalIgnoreCase)?ReadCsv(dialog.FileName):ReadXlsx(dialog.FileName);if(imported.Count==0){MessageBox.Show("没有读取到有效的 A/B 列数据。\n请确认 A 列是单词，B 列是中文翻译。","导入提示",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}reviewSource.Clear();reviewSource.AddRange(imported);cards.Clear();cards.AddRange(reviewSource);wrongCards.Clear();SaveWrongBook();RefreshWrongBook();current=0;known=0;repeats=0;showingAnswer=false;instruction.Visible=true;ResetOrder();RenderCard();}catch(Exception ex){MessageBox.Show($"导入失败：{ex.Message}","导入错误",MessageBoxButtons.OK,MessageBoxIcon.Error);}}
    static List<(string,string,string,string,string,int)> ReadCsv(string path){var result=new List<(string,string,string,string,string,int)>();foreach(var line in File.ReadLines(path,Encoding.UTF8)){var cells=line.Split(',');if(cells.Length>=2&&!string.IsNullOrWhiteSpace(cells[0])&&!cells[0].Trim().Equals("单词"))result.Add((cells[0].Trim(),"Excel 导入",cells[1].Trim(),"自定义","",0));}return result;}
    static List<(string,string,string,string,string,int)> ReadXlsx(string path){using var zip=ZipFile.OpenRead(path);var shared=new List<string>();var sharedEntry=zip.GetEntry("xl/sharedStrings.xml");if(sharedEntry!=null){using var sr=new StreamReader(sharedEntry.Open());var xml=XDocument.Load(sr);shared=xml.Descendants().Where(x=>x.Name.LocalName=="si").Select(x=>string.Concat(x.Descendants().Where(t=>t.Name.LocalName=="t").Select(t=>t.Value))).ToList();}var sheet=zip.GetEntry("xl/worksheets/sheet1.xml")??throw new InvalidDataException("找不到第一张工作表");using var reader=new StreamReader(sheet.Open());var doc=XDocument.Load(reader);var rows=new List<(string,string,string,string,string,int)>();foreach(var row in doc.Descendants().Where(x=>x.Name.LocalName=="row")){var cells=new Dictionary<string,string>();foreach(var cell in row.Descendants().Where(x=>x.Name.LocalName=="c")){var reference=(string?)cell.Attribute("r")??"";var value=cell.Descendants().FirstOrDefault(x=>x.Name.LocalName=="v")?.Value??"";if(cell.Attribute("t")?.Value=="s"&&int.TryParse(value,out var i)&&i<shared.Count)value=shared[i];cells[reference]=value;}var a=cells.FirstOrDefault(x=>x.Key.StartsWith("A",StringComparison.OrdinalIgnoreCase)).Value;var b=cells.FirstOrDefault(x=>x.Key.StartsWith("B",StringComparison.OrdinalIgnoreCase)).Value;if(!string.IsNullOrWhiteSpace(a)&&!string.IsNullOrWhiteSpace(b)&&!a.Trim().Equals("单词"))rows.Add((a.Trim(),"Excel 导入",b.Trim(),"自定义","",0));}return rows;}
}