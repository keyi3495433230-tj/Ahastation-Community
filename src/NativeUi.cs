using X = Microsoft.UI.Xaml;
using C = Microsoft.UI.Xaml.Controls;
using M = Microsoft.UI.Xaml.Media;
using A = Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace CardReview;

public sealed partial class ReviewForm
{
    NativeIslandHost? nativeHost;
    C.NavigationView nativeNav = null!;
    C.Grid nativeRoot = null!, nativeReviewLayout = null!, nativeCanvasLayout = null!;
    readonly List<X.FrameworkElement> nativePages = new();
    readonly List<WeakReference<C.Button>> nativeAccentButtons = new();
    readonly List<Action> nativePropertySync = new();
    readonly System.Windows.Forms.Timer nativeSyncTimer = new() { Interval = 160 };
    C.Border nativeCanvasSlot = null!, nativeSheetSlot = null!, nativeCard = null!, nativeProps = null!, nativeLayers = null!;
    C.StackPanel nativePropsBody = null!, nativeLayersBody = null!;
    C.TextBlock nativeWord = null!, nativeMeaning = null!, nativeProgress = null!, nativeSource = null!, nativeClock = null!;
    C.ListView nativeBankList = null!, nativeWrongList = null!;
    C.ComboBox nativeSheets = null!, nativeBoards = null!;
    C.Button nativeUseSheet = null!;
    C.AppBarToggleButton nativeBankToggle = null!, nativeWrongToggle = null!;
    C.Border nativeEbbinghausTableHost = null!;
    C.TextBlock nativeEbbinghausPageLabel = null!;
    C.Button nativeEbbinghausPrevious = null!, nativeEbbinghausNext = null!, nativeEbbinghausMeaningToggle = null!;
    C.Border nativeBankPanel = null!, nativeWrongPanel = null!;
    C.Button nativePropsToggle = null!, nativeLayersToggle = null!;
    bool nativePropertiesOpen, nativeLayersOpen;
    C.Grid? nativeLayerDraggingRow;
    C.ScrollViewer nativeLayerScroll = null!;
    X.UIElement? nativeLayerDragSurface;
    Microsoft.UI.Xaml.Input.Pointer? nativeLayerPointer;
    readonly List<C.Border> nativeLayerDropLines = new();
    readonly System.Windows.Forms.Timer nativeLayerScrollTimer = new() { Interval = 30 };
    Windows.Foundation.Point nativeLayerPointerPosition;
    Windows.Foundation.Point nativeLayerDragStart;
    int nativeLayerDragTargetIndex = -1;
    bool nativeLayerDragMoved;
    bool nativeTransparencyEnabled = true;
    string NativeTransparencyPath => Path.Combine(Path.GetDirectoryName(themeModePath)!, "transparency.txt");
    string NativeReviewPanelPath(string panel) => Path.Combine(AppDataStorage.Root, $"review-{panel}-open.txt");
    string NativeEbbinghausPath => Path.Combine(AppDataStorage.Root, "ebbinghaus-notebook.json");
    C.Grid nativeStudyPanel = null!;
    bool nativeReady, nativeSyncing, nativeSelecting, nativeAnimations = true;
    (bool Wide, bool BankOpen, bool WrongOpen)? nativeReviewLayoutState;
    bool nativeBankOpen, nativeWrongOpen;
    readonly List<EbbinghausPageData> nativeEbbinghausPages = new();
    int nativeEbbinghausPage;
    bool nativeEbbinghausMeaningHidden;
    bool nativeResourcesInstalled;
    int nativePage, nativePopupDepth;
    string nativeDeckKey = "", nativeBankKey = "", nativeWrongKey = "", nativeLayerKey = "", nativeSheetKey = "", nativeBoardKey = "";
    readonly M.SolidColorBrush nativeBackground = new(), nativeSurface = new(), nativePanel = new(), nativeLayerSurface = new(), nativeStroke = new(), nativeAccent = new();
    readonly M.SolidColorBrush nativeAccentHover = new(), nativeAccentPressed = new(), nativeAccentForeground = new();

    static C.TextBlock TextBlock(string text, double size = 14) => new() { Text = text, FontSize = size, TextWrapping = X.TextWrapping.Wrap };
    static C.StackPanel Stack(double gap = 12) => new() { Spacing = gap };
    new static C.ScrollViewer Scroll(X.UIElement child) => new() { Content = child, HorizontalScrollBarVisibility = C.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = C.ScrollBarVisibility.Auto, HorizontalContentAlignment = X.HorizontalAlignment.Stretch };
    static C.Grid GridRows(params X.GridLength[] heights)
    {
        var grid = new C.Grid { RowSpacing = 12 };
        foreach (var height in heights) grid.RowDefinitions.Add(new C.RowDefinition { Height = height });
        return grid;
    }
    static readonly X.GridLength Auto = X.GridLength.Auto;
    static readonly X.GridLength Star = new(1, X.GridUnitType.Star);
    static void Put(C.Grid grid, X.FrameworkElement child, int row = 0, int column = 0) { C.Grid.SetRow(child, row); C.Grid.SetColumn(child, column); grid.Children.Add(child); }
    C.Button NativeButton(string label, Action action, bool accent = false)
    {
        var button = new C.Button { Content = label, MinHeight = 32, HorizontalAlignment = X.HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        if (accent) { button.Style = (X.Style)X.Application.Current.Resources["AccentButtonStyle"]; nativeAccentButtons.Add(new(button)); button.Background=nativeAccent; button.Foreground=Brush(Contrast(activeThemeColor)); }
        button.Click += (_, _) => { action(); NativeSync(); };
        MotionButton(button);
        return button;
    }
    C.Border Surface(X.UIElement content, bool inset = false) => new() { Child = content, Padding = new X.Thickness(16), CornerRadius = new X.CornerRadius(8), Background = inset ? nativePanel : nativeSurface, BorderBrush = nativeStroke, BorderThickness = new X.Thickness(1) };
    C.CommandBar Commands(params (string label, string glyph, Action action)[] actions)
    {
        var bar = new C.CommandBar { DefaultLabelPosition = C.CommandBarDefaultLabelPosition.Right, HorizontalContentAlignment = X.HorizontalAlignment.Stretch, IsDynamicOverflowEnabled = true, Background = nativeSurface };
        foreach (var (label, glyph, action) in actions)
        {
            var button = new C.AppBarButton { Label = label, Icon = new C.FontIcon { Glyph = glyph }, IsCompact = true };
            button.Click += (_, _) => { action(); NativeSync(); };
            bar.PrimaryCommands.Add(button);
        }
        bar.Opened += (_, _) => NativePopup(true);
        bar.Closed += (_, _) => NativePopup(false);
        return bar;
    }

    void InitializeNativeUi()
    {
        try { if(File.Exists(NativeTransparencyPath))nativeTransparencyEnabled=File.ReadAllText(NativeTransparencyPath).Trim()!="off"; } catch(IOException) { }
        SuspendLayout();
        foreach (Control child in Controls) child.Visible = false;
        cardTransitionTimer.Stop(); navigationMotionTimer.Stop(); sidebarCollapseTimer.Stop(); tabTransitionTimer.Stop();
        MinimumSize = new Size(480, 360);
        AutoScaleMode = AutoScaleMode.None;
        Region = null;
        nativeHost = new NativeIslandHost { Dock = DockStyle.Fill, TabStop = true };
        Controls.Add(nativeHost);
        nativeRoot = new C.Grid { Background = nativeBackground };
        nativeRoot.KeyDown+=(_,e)=>
        {
            if(nativePopupDepth>0)return;
            for(var source=e.OriginalSource as X.DependencyObject;source!=null;source=M.VisualTreeHelper.GetParent(source))if(source is C.TextBox or C.NumberBox or C.ComboBox)return;
            if(nativePage==1 && CanvasWorkspaceKey((Keys)(int)e.Key | ModifierKeys)){e.Handled=true;return;}
            if(nativePage==1 && ModifierKeys==Keys.None && e.Key is Windows.System.VirtualKey.Back or Windows.System.VirtualKey.Delete)
            {
                if(selectedCanvasLink!=null)DeleteSelectedCanvasLink();else DeleteCanvasSelection();
                NativeSync();e.Handled=true;return;
            }
            if(nativePage==0 && e.Key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right){Advance(e.Key==Windows.System.VirtualKey.Right);NativeSync();e.Handled=true;}
        };
        InstallNativeAccentResources();
        nativeNav = new C.NavigationView {
            PaneDisplayMode = C.NavigationViewPaneDisplayMode.Auto, OpenPaneLength = 232, CompactPaneLength = 48,
            ExpandedModeThresholdWidth = 1180, CompactModeThresholdWidth = 680, IsBackButtonVisible = C.NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = true, IsPaneOpen = ClientSize.Width >= 1180, IsTitleBarAutoPaddingEnabled = false,
            PaneTitle = "工作区", Background = nativeBackground
        };
        nativeNav.HorizontalContentAlignment=X.HorizontalAlignment.Stretch;nativeNav.VerticalContentAlignment=X.VerticalAlignment.Stretch;
        nativeNav.FontFamily = new M.FontFamily("Microsoft YaHei UI");
        nativeNav.Resources["NavigationViewDefaultPaneBackground"]=nativePanel;
        nativeNav.Resources["NavigationViewContentBackground"]=Brush(Color.Transparent);
        nativeNav.ContentTransitions = new A.TransitionCollection();
        var labels = new[] { "单词复习", "梳理画布", "表格词库", "专注模式", "艾宾浩斯", "文本便签" };
        var symbols = new[] { C.Symbol.Read, C.Symbol.Edit, C.Symbol.List, C.Symbol.Clock, C.Symbol.Calendar, C.Symbol.Document };
        nativeNav.MenuItems.Add(new C.NavigationViewItem { Content = "聊天", Icon = new C.SymbolIcon(C.Symbol.Message), Tag = 7 });
        for (int i = 0; i < labels.Length; i++) nativeNav.MenuItems.Add(new C.NavigationViewItem { Content = labels[i], Icon = new C.SymbolIcon(symbols[i]), Tag = i });
        nativeNav.SelectionChanged += (_, args) => { if (!nativeSelecting) NativeSelectPage(args.IsSettingsSelected ? 6 : (int)((C.NavigationViewItem)args.SelectedItem).Tag); };
        nativeNav.PaneOpening += (_, _) => { if (nativeNav.DisplayMode != C.NavigationViewDisplayMode.Expanded) NativePopup(true); };
        nativeNav.PaneClosed += (_, _) => { if (nativeNav.DisplayMode != C.NavigationViewDisplayMode.Expanded) NativePopup(false); };
        nativeRoot.Children.Add(nativeNav);
        nativeHost.Content = nativeRoot;
        nativePages.Add(BuildNativeReview());
        nativePages.Add(BuildNativeCanvas());
        nativePages.Add(BuildNativeSheet());
        nativePages.Add(BuildNativeFocus());
        nativePages.Add(BuildNativeEbbinghaus());
        nativePages.Add(BuildNativeNotes());
        nativePages.Add(BuildNativeSettings());
        nativePages.Add(BuildNativeChat());
        foreach (var page in nativePages) page.SizeChanged += (_, _) => NativeResponsiveLayout();
        nativeReady = true;
        canvasSurface.Dock = DockStyle.None; sheetGrid.Dock = DockStyle.None;
        canvasSurface.Parent = this; sheetGrid.Parent = this;
        canvasSurface.Visible = sheetGrid.Visible = false;
        nativeCanvasSlot.Child=null;nativeSheetSlot.Child=null;
        nativeRoot.SizeChanged += (_, _) => NativeResponsiveLayout();
        nativeRoot.LayoutUpdated += (_, _) => PositionNativeSurfaces();
        nativeSyncTimer.Tick += (_, _) => NativeSync();
        nativeSyncTimer.Start();
        nativeFocusTimer.Tick += NativeFocusTick;
        FormClosing += (_, _) => { nativeSyncTimer.Stop(); nativeSyncTimer.Dispose(); nativeFocusTimer.Stop(); nativeFocusTimer.Dispose(); _ = DisposeNativeFocusCameraAsync(); };
        ApplyNativeTheme(); NativeSelectPage(0);
        nativeHost.BringToFront(); ResumeLayout();InitializeBackground();
    }

    void NativeSelectPage(int page)
    {
        if (!nativeReady) return;
        if(nativePage==1 && page!=1){if(canvasTool==CanvasTool.Pen&&canvasPenPoints.Count>=2)FinishCanvasPen(false,false);CancelCanvasPaint();if(canvasDrawing || canvasResizeNode!=null)CancelCanvasWorkspaceGesture();canvasSpaceDown=false;SaveCanvas();}
        FinishNativeLayerDrag(false);
        if(nativePage==5&&page!=5)SaveActiveNativeNoteText();
        CommitCanvasInlineEdit(); sheetGrid.EndEdit();
        if (sheetSelected) SaveSheet();
        nativePage = page; activeWorkspaceTab = page;
        organizeSelected = page == 1; sheetSelected = page == 2; settingsSelected = page == 6;
        canvasSurface.Visible = sheetGrid.Visible = false;
        nativeCanvasSlot.Child=null;nativeSheetSlot.Child=null;
        if(page is not 1 and not 2){nativeHost?.BringToFront();Invalidate(true);}
        nativeSelecting = true;
        nativeNav.SelectedItem = page == 6 ? nativeNav.SettingsItem : nativeNav.MenuItems.OfType<C.NavigationViewItem>().First(item=>(int)item.Tag==page);
        nativeSelecting = false;
        nativeNav.Content = nativePages[page];
        MotionEnter(nativePages[page]);
        if (nativeNav.DisplayMode != C.NavigationViewDisplayMode.Expanded) nativeNav.IsPaneOpen = false;
        nativePopupDepth = 0;
        NativeResponsiveLayout(); NativeSync();
    }

    bool LoadNativeReviewPanelPreference(string panel)
    {
        try { return File.Exists(NativeReviewPanelPath(panel)) && File.ReadAllText(NativeReviewPanelPath(panel)).Trim() == "open"; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    void SetNativeReviewPanelOpen(bool bank, bool open)
    {
        if (bank)
        {
            nativeBankOpen = open;
            nativeBankToggle.IsChecked = open;
            nativeBankToggle.Label = open ? "收起词库" : "显示词库";
        }
        else
        {
            nativeWrongOpen = open;
            nativeWrongToggle.IsChecked = open;
            nativeWrongToggle.Label = open ? "收起错题本" : "显示错题本";
        }
        try
        {
            Directory.CreateDirectory(AppDataStorage.Root);
            File.WriteAllText(NativeReviewPanelPath(bank ? "bank" : "wrong"), open ? "open" : "closed");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        nativeReviewLayoutState = null;
        NativeResponsiveLayout();
    }

    void NativeResponsiveLayout()
    {
        if (!nativeReady) return;
        // Keep the dashboard beside the workspace pane whenever both fit.
        var paneMode=nativePage==9&&nativeNav.ActualWidth>=720?C.NavigationViewPaneDisplayMode.Left:C.NavigationViewPaneDisplayMode.Auto;
        if(nativeNav.PaneDisplayMode!=paneMode){bool open=nativeNav.IsPaneOpen;nativeNav.PaneDisplayMode=paneMode;nativeNav.IsPaneOpen=open;}
        double width = nativePages[nativePage].ActualWidth;
        if (width < 1) width = ClientSize.Width - (nativeNav.IsPaneOpen ? 232 : 48);
        if (nativePage == 0)
        {
            bool wide = width >= 980;
            var layoutState = (wide, nativeBankOpen, nativeWrongOpen);
            if (nativeReviewLayoutState != layoutState)
            {
                nativeReviewLayout.ColumnDefinitions.Clear(); nativeReviewLayout.RowDefinitions.Clear();
                nativeBankPanel.Visibility = nativeBankOpen ? X.Visibility.Visible : X.Visibility.Collapsed;
                nativeWrongPanel.Visibility = nativeWrongOpen ? X.Visibility.Visible : X.Visibility.Collapsed;
                if (wide)
                {
                    int column = 0;
                    if (nativeBankOpen) nativeReviewLayout.ColumnDefinitions.Add(new C.ColumnDefinition { Width = new X.GridLength(240) });
                    nativeReviewLayout.ColumnDefinitions.Add(new C.ColumnDefinition { Width = Star });
                    if (nativeWrongOpen) nativeReviewLayout.ColumnDefinitions.Add(new C.ColumnDefinition { Width = new X.GridLength(220) });
                    nativeReviewLayout.RowDefinitions.Add(new C.RowDefinition { Height = Star });
                    if (nativeBankOpen) { C.Grid.SetRow(nativeBankPanel, 0); C.Grid.SetColumn(nativeBankPanel, column++); }
                    C.Grid.SetRow(nativeStudyPanel, 0); C.Grid.SetColumn(nativeStudyPanel, column++);
                    if (nativeWrongOpen) { C.Grid.SetRow(nativeWrongPanel, 0); C.Grid.SetColumn(nativeWrongPanel, column); }
                }
                else
                {
                    nativeReviewLayout.ColumnDefinitions.Add(new C.ColumnDefinition { Width = Star });
                    nativeReviewLayout.RowDefinitions.Add(new C.RowDefinition { Height = Auto });
                    C.Grid.SetRow(nativeStudyPanel, 0); C.Grid.SetColumn(nativeStudyPanel, 0);
                    int row = 1;
                    if (nativeBankOpen)
                    {
                        nativeReviewLayout.RowDefinitions.Add(new C.RowDefinition { Height = Auto });
                        C.Grid.SetRow(nativeBankPanel, row++); C.Grid.SetColumn(nativeBankPanel, 0);
                    }
                    if (nativeWrongOpen)
                    {
                        nativeReviewLayout.RowDefinitions.Add(new C.RowDefinition { Height = Auto });
                        C.Grid.SetRow(nativeWrongPanel, row); C.Grid.SetColumn(nativeWrongPanel, 0);
                    }
                }
                nativeReviewLayoutState = layoutState;
            }
            double available=Math.Max(240,nativePages[0].ActualHeight-108);
            nativeBankPanel.Height = wide ? Math.Max(450,available) : 560;
            nativeWrongPanel.Height = wide ? Math.Max(450,available) : 360;
            double layoutWidth = nativeReviewLayout.ActualWidth > 1 ? nativeReviewLayout.ActualWidth : Math.Max(1, width - 40);
            double sideWidth = wide ? (nativeBankOpen ? 260 : 0) + (nativeWrongOpen ? 240 : 0) : 0;
            double cardAvailableWidth = Math.Max(1, layoutWidth - sideWidth);
            double maxStudyWidth = Math.Min(960, cardAvailableWidth);
            nativeStudyPanel.Width = Math.Max(Math.Min(320, maxStudyWidth), Math.Min(maxStudyWidth, cardAvailableWidth * .72));
            double cardAvailableHeight = Math.Max(200, nativePages[0].ActualHeight - 360);
            nativeCard.MinHeight = Math.Clamp(cardAvailableHeight * .6, 160, 500);
            nativeWord.FontSize = width < 500 ? 30 : 44;
        }
        if (nativePage == 1) LayoutCanvasWorkspace(width);
        if(nativePage==5)NativeNotesResponsiveLayout(width);
        PositionNativeSurfaces();
    }

    void PositionNativeSurfaces()
    {
        if (!nativeReady || nativeHost == null || IsDisposed) return;
        Place(canvasSurface, nativeCanvasSlot, nativePage == 1);
        Place(sheetGrid, nativeSheetSlot, nativePage == 2);
        void Place(Control control, X.FrameworkElement slot, bool show)
        {
            show = show && nativePopupDepth == 0 && WindowState != FormWindowState.Minimized && slot.ActualWidth > 1 && slot.ActualHeight > 1;
            if (!show) { control.Visible = false; return; }
            var p = slot.TransformToVisual(nativeRoot).TransformPoint(new Windows.Foundation.Point(0,0));
            double scale = nativeRoot.XamlRoot?.RasterizationScale ?? DeviceDpi / 96.0;
            var bounds = new Rectangle((int)Math.Round(p.X*scale), (int)Math.Round(p.Y*scale), Math.Max(1,(int)Math.Round(slot.ActualWidth*scale)),Math.Max(1,(int)Math.Round(slot.ActualHeight*scale)));
            if (control.Bounds != bounds) control.Bounds = bounds;
            if (!control.Visible) { control.Visible = true; control.BringToFront(); }
        }
    }

    void NativePopup(bool opening)
    {
        if(opening&&nativePopupDepth==0)
        {
            var control=nativePage==1?(Control)canvasSurface:sheetGrid;
            var slot=nativePage==1?nativeCanvasSlot:nativeSheetSlot;
            if(control.Visible&&control.Width>0&&control.Height>0)
            {
                using var bitmap=new Bitmap(control.Width,control.Height);control.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                using var buffer=new MemoryStream();bitmap.Save(buffer,System.Drawing.Imaging.ImageFormat.Png);buffer.Position=0;
                var source=new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();source.SetSource(buffer.AsRandomAccessStream());
                slot.Child=new C.Image{Source=source,Stretch=M.Stretch.Fill};
            }
        }
        nativePopupDepth = Math.Max(0, nativePopupDepth + (opening ? 1 : -1));
        if(nativePopupDepth==0){nativeCanvasSlot.Child=null;nativeSheetSlot.Child=null;}
        PositionNativeSurfaces();
    }

    void ShowNativeCanvasMenu(ContextMenuStrip legacy,Point point)
    {
        var flyout=new C.MenuFlyout();
        foreach(ToolStripItem item in legacy.Items)flyout.Items.Add(Convert(item));
        flyout.Opened+=(_,_)=>NativePopup(true);
        flyout.Closed+=(_,_)=>{NativePopup(false);legacy.Dispose();NativeSync();};
        double scale=nativeRoot.XamlRoot?.RasterizationScale??1;
        flyout.ShowAt(nativeCanvasSlot,new FlyoutShowOptions{Position=new Windows.Foundation.Point(point.X/scale,point.Y/scale)});
        C.MenuFlyoutItemBase Convert(ToolStripItem item)
        {
            if(item is ToolStripSeparator)return new C.MenuFlyoutSeparator();
            if(item is ToolStripMenuItem parent&&parent.DropDownItems.Count>0){var sub=new C.MenuFlyoutSubItem{Text=parent.Text,IsEnabled=parent.Enabled};foreach(ToolStripItem child in parent.DropDownItems)sub.Items.Add(Convert(child));return sub;}
            var result=new C.MenuFlyoutItem{Text=item.Text,IsEnabled=item.Enabled};result.Click+=(_,_)=>item.PerformClick();return result;
        }
    }

    C.Grid BuildNativeReview()
    {
        nativeBankOpen = LoadNativeReviewPanelPreference("bank");
        nativeWrongOpen = LoadNativeReviewPanelPreference("wrong");
        var page = GridRows(Auto, Star); page.Padding = new X.Thickness(20);
        var toolbar = Commands(("使用表格词库", "\uE8A5", () => { if(wordSheetId==Guid.Empty){NativeSelectPage(2);return;}wordSheetActive = true; RefreshCardsFromWordSheet(); ResetOrder(); RenderCard(); }),
            ("本地词库", "\uE8F1", () => { wordSheetActive = false; reviewSource.Clear(); reviewSource.AddRange(savedCards); cards.Clear(); cards.AddRange(reviewSource); current=0; ResetOrder(); RenderCard(); }),
            ("顺序 / 随机", "\uE8B1", () => { randomMode = !randomMode; current=0; ResetOrder(); RenderCard(); }),
            ("导入单词", "\uE8B5", ImportFile), ("重置全部", "\uE74D", ResetAll),
            ("使用说明", "\uE897", () => NativeMessage("使用说明", "右方向键：显示释义 / 记住并继续。左方向键：加入错题本。\n\n表格的 A 列为卡牌正面，B 列为释义，C 列为备注。编辑后点击“作为表格词库”。\n\n画布支持右键菜单、拖放图片、Ctrl+C/V、Ctrl+Z、Ctrl+滚轮缩放。")));
        nativeBankToggle = new C.AppBarToggleButton { Label = nativeBankOpen ? "收起词库" : "显示词库", Icon = new C.FontIcon { Glyph = "\uE8F1" }, IsCompact = true, IsChecked = nativeBankOpen };
        nativeWrongToggle = new C.AppBarToggleButton { Label = nativeWrongOpen ? "收起错题本" : "显示错题本", Icon = new C.FontIcon { Glyph = "\uE7C1" }, IsCompact = true, IsChecked = nativeWrongOpen };
        nativeBankToggle.Click += (_, _) => { SetNativeReviewPanelOpen(true, nativeBankToggle.IsChecked == true); NativeSync(); };
        nativeWrongToggle.Click += (_, _) => { SetNativeReviewPanelOpen(false, nativeWrongToggle.IsChecked == true); NativeSync(); };
        toolbar.PrimaryCommands.Insert(0, nativeBankToggle);
        toolbar.PrimaryCommands.Insert(1, nativeWrongToggle);
        Put(page, toolbar);
        nativeReviewLayout = new C.Grid { ColumnSpacing = 20, RowSpacing = 20 };
        nativeStudyPanel = GridRows(Auto, Auto, Auto, Auto);
        nativeStudyPanel.VerticalAlignment = X.VerticalAlignment.Center;
        nativeStudyPanel.HorizontalAlignment = X.HorizontalAlignment.Center;
        nativeStudyPanel.MaxWidth = 960;
        var cardBody = Stack(24); cardBody.VerticalAlignment = X.VerticalAlignment.Center;
        nativeWord = TextBlock("", 44); nativeWord.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; nativeWord.TextAlignment = X.TextAlignment.Center;
        nativeMeaning = TextBlock("", 20); nativeMeaning.TextAlignment = X.TextAlignment.Center;
        cardBody.Children.Add(nativeWord); cardBody.Children.Add(nativeMeaning);
        nativeCard = Surface(cardBody); nativeCard.MinHeight = 300;
        nativeCard.Tapped += (_, _) => { nativeCard.Focus(X.FocusState.Programmatic); if (cards.Count == 0) return; if (showingAnswer) Advance(true); else { showingAnswer=true; RenderCard(); } NativeSync(); };
        nativeCard.IsTabStop = true;
        nativeCard.KeyDown += (_, e) => { if(e.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space) { Advance(true); NativeSync(); e.Handled=true; } };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(nativeCard, "单词卡，点击显示释义或下一张");
        Put(nativeStudyPanel, nativeCard);
        var grade = new C.Grid { ColumnSpacing=12 }; grade.ColumnDefinitions.Add(new C.ColumnDefinition { Width=Star }); grade.ColumnDefinitions.Add(new C.ColumnDefinition { Width=Star });
        Put(grade, NativeButton("没背过", () => Advance(false))); Put(grade, NativeButton("背过", () => Advance(true), true), 0, 1);
        Put(nativeStudyPanel, grade, 1);
        nativeProgress=TextBlock("",12); nativeProgress.HorizontalAlignment=X.HorizontalAlignment.Center; Put(nativeStudyPanel,nativeProgress,2);
        nativeSource=TextBlock("",12); nativeSource.HorizontalAlignment=X.HorizontalAlignment.Center; Put(nativeStudyPanel,nativeSource,3);
        var bankHeader=Stack(4);bankHeader.Children.Add(TextBlock("词库",24));bankHeader.Children.Add(TextBlock("本地保存 · 右键编辑",12));var bank=Stack(6);
        nativeBankList=new C.ListView { SelectionMode=C.ListViewSelectionMode.None, IsItemClickEnabled=false,CanDragItems=true,CanReorderItems=true,AllowDrop=true };
        var wordRowStyle=new X.Style{TargetType=typeof(C.ListViewItem)};wordRowStyle.Setters.Add(new X.Setter(C.Control.HorizontalContentAlignmentProperty,X.HorizontalAlignment.Stretch));wordRowStyle.Setters.Add(new X.Setter(C.Control.PaddingProperty,new X.Thickness(0)));wordRowStyle.Setters.Add(new X.Setter(X.FrameworkElement.MarginProperty,new X.Thickness(0,0,0,6)));nativeBankList.ItemContainerStyle=wordRowStyle;
        nativeBankList.DragItemsCompleted+=(_,_)=>{var reordered=nativeBankList.Items.Cast<C.StackPanel>().Select(p=>((string Word,string Type,string Meaning,string Tag,string Note,int Difficulty))p.Tag).ToList();savedCards.Clear();savedCards.AddRange(reordered);SaveWordBank();nativeBankKey="";NativeSync();};
        var en=new C.TextBox { PlaceholderText="英语单词" }; var cn=new C.TextBox { PlaceholderText="中文翻译" }; var note=new C.TextBox { PlaceholderText="备注" };
        bank.Children.Add(en); bank.Children.Add(cn); bank.Children.Add(note);
        var difficulty=new C.ComboBox { Header="难度", HorizontalAlignment=X.HorizontalAlignment.Stretch }; foreach(var text in new[]{"未标记","✓","✓✓","✓✓✓"})difficulty.Items.Add(text); difficulty.SelectedIndex=0; bank.Children.Add(difficulty);
        bank.Children.Add(NativeButton("添加单词",()=>{englishInput.Text=en.Text;chineseInput.Text=cn.Text;noteInput.Text=note.Text; difficulty1.Checked=difficulty.SelectedIndex>=1;difficulty2.Checked=difficulty.SelectedIndex>=2;difficulty3.Checked=difficulty.SelectedIndex>=3;AddWord(); if(englishInput.Text.Length==0){en.Text=cn.Text=note.Text="";difficulty.SelectedIndex=0;}},true));
        var filter=new C.ComboBox { Header="筛选单词",HorizontalAlignment=X.HorizontalAlignment.Stretch }; foreach(var text in new[]{"全部单词","难度 ✓","难度 ✓✓✓"})filter.Items.Add(text);filter.SelectedIndex=0;filter.SelectionChanged+=(_,_)=>{SetDifficultyFilter(filter.SelectedIndex switch{1=>1,2=>3,_=>-1});NativeSync();};bank.Children.Add(filter);
        var bankLayout=GridRows(Auto,Star,Auto);Put(bankLayout,bankHeader);Put(bankLayout,nativeBankList,1);Put(bankLayout,bank,2);nativeBankPanel=Surface(bankLayout);
        var wrong=Stack();wrong.Children.Add(TextBlock("错题本",24));wrong.Children.Add(TextBlock("掌握后可移除",12));nativeWrongList=new C.ListView{SelectionMode=C.ListViewSelectionMode.None};
        var wrongRowStyle=new X.Style{TargetType=typeof(C.ListViewItem)};wrongRowStyle.Setters.Add(new X.Setter(C.Control.HorizontalContentAlignmentProperty,X.HorizontalAlignment.Stretch));wrongRowStyle.Setters.Add(new X.Setter(C.Control.PaddingProperty,new X.Thickness(0)));wrongRowStyle.Setters.Add(new X.Setter(X.FrameworkElement.MarginProperty,new X.Thickness(0,0,0,6)));nativeWrongList.ItemContainerStyle=wrongRowStyle;
        var hide=new C.ToggleSwitch{Header="遮住翻译",IsOn=hideWrongMeaning};hide.Toggled+=(_,_)=>{hideWrongMeaning=hide.IsOn;NativeSync();};var wrongFooter=Stack(8);wrongFooter.Children.Add(hide);var wrongLayout=GridRows(Auto,Star,Auto);Put(wrongLayout,wrong);Put(wrongLayout,nativeWrongList,1);Put(wrongLayout,wrongFooter,2);nativeWrongPanel=Surface(wrongLayout); nativeWrongList.CanDragItems=true;nativeWrongList.CanReorderItems=true;nativeWrongList.AllowDrop=true;nativeWrongList.DragItemsCompleted+=(_,_)=>{var reordered=nativeWrongList.Items.Cast<C.StackPanel>().Select(p=>((string Word,string Meaning,int Count))p.Tag).ToList();wrongCards.Clear();wrongCards.AddRange(reordered);SaveWrongBook();nativeWrongKey="";NativeSync();};
        nativeReviewLayout.Children.Add(nativeBankPanel);nativeReviewLayout.Children.Add(nativeStudyPanel);nativeReviewLayout.Children.Add(nativeWrongPanel);
        Put(page,Scroll(nativeReviewLayout),1);return page;
    }

    C.Grid BuildNativeEbbinghaus()
    {
        LoadNativeEbbinghausNotebook();
        var page=GridRows(Auto,Star);page.Padding=new X.Thickness(20);
        var heading=new C.Grid{ColumnSpacing=16};
        heading.ColumnDefinitions.Add(new C.ColumnDefinition{Width=Star});
        heading.ColumnDefinitions.Add(new C.ColumnDefinition{Width=Auto});
        var title=Stack(4);
        title.Children.Add(TextBlock("艾宾浩斯遗忘曲线表",24));
        title.Children.Add(TextBlock("每页20个单词 · 填写单词和释义，点击复习节点下的小格打勾",13));
        nativeEbbinghausMeaningToggle=NativeButton("隐藏释义",ToggleNativeEbbinghausMeaning);
        nativeEbbinghausMeaningToggle.HorizontalAlignment=X.HorizontalAlignment.Left;
        title.Children.Add(nativeEbbinghausMeaningToggle);
        Put(heading,title);
        var pager=new C.StackPanel{Orientation=C.Orientation.Horizontal,Spacing=8,VerticalAlignment=X.VerticalAlignment.Center};
        nativeEbbinghausPrevious=NativeButton("上一页",()=>ChangeNativeEbbinghausPage(-1));
        nativeEbbinghausPageLabel=TextBlock("第1页 / 共1页",13);nativeEbbinghausPageLabel.VerticalAlignment=X.VerticalAlignment.Center;nativeEbbinghausPageLabel.TextAlignment=X.TextAlignment.Center;nativeEbbinghausPageLabel.MinWidth=112;
        nativeEbbinghausNext=NativeButton("下一页",()=>ChangeNativeEbbinghausPage(1),true);
        var deletePage=NativeButton("删除本页",DeleteNativeEbbinghausPage);
        pager.Children.Add(nativeEbbinghausPrevious);pager.Children.Add(nativeEbbinghausPageLabel);pager.Children.Add(nativeEbbinghausNext);pager.Children.Add(deletePage);
        Put(heading,pager,0,1);
        Put(page,heading);
        nativeEbbinghausTableHost=new C.Border{Background=nativeSurface,BorderBrush=nativeStroke,BorderThickness=new X.Thickness(1),CornerRadius=new X.CornerRadius(8),Padding=new X.Thickness(12)};
        Put(page,nativeEbbinghausTableHost,1);
        RenderNativeEbbinghausPage();
        return page;
    }

    void ToggleNativeEbbinghausMeaning()
    {
        nativeEbbinghausMeaningHidden=!nativeEbbinghausMeaningHidden;
        nativeEbbinghausMeaningToggle.Content=nativeEbbinghausMeaningHidden?"显示释义":"隐藏释义";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(nativeEbbinghausMeaningToggle,(string)nativeEbbinghausMeaningToggle.Content);
        RenderNativeEbbinghausPage();
    }

    void LoadNativeEbbinghausNotebook()
    {
        nativeEbbinghausPages.Clear();
        try
        {
            if(File.Exists(NativeEbbinghausPath))
                nativeEbbinghausPages.AddRange(System.Text.Json.JsonSerializer.Deserialize<List<EbbinghausPageData>>(File.ReadAllText(NativeEbbinghausPath))??new());
        }
        catch{nativeEbbinghausPages.Clear();}
        nativeEbbinghausPages.RemoveAll(p=>p==null);
        if(nativeEbbinghausPages.Count==0)nativeEbbinghausPages.Add(CreateEbbinghausPage());
        for(int i=0;i<nativeEbbinghausPages.Count;i++)
        {
            var data=nativeEbbinghausPages[i];
            data.Words??=new();
            while(data.Words.Count<20)data.Words.Add(new EbbinghausWordData());
            for(int word=0;word<data.Words.Count;word++)
            {
                var entry=data.Words[word];
                if(entry.Completed==null||entry.Completed.Length!=11)
                {
                    var completed=new bool[11];
                    if(entry.Completed?.Length==5)
                    {
                        completed[3]=entry.Completed[0];
                        completed[4]=entry.Completed[1];
                        completed[6]=entry.Completed[2];
                        completed[8]=entry.Completed[3];
                        completed[9]=entry.Completed[4];
                    }
                    else if(entry.Completed!=null)Array.Copy(entry.Completed,completed,Math.Min(entry.Completed.Length,completed.Length));
                    entry.Completed=completed;
                }
                if(entry.CompletedAt==null||entry.CompletedAt.Length!=11)
                {
                    var completedAt=new DateTimeOffset?[11];
                    if(entry.CompletedAt!=null)Array.Copy(entry.CompletedAt,completedAt,Math.Min(entry.CompletedAt.Length,completedAt.Length));
                    entry.CompletedAt=completedAt;
                }
            }
        }
        SaveNativeEbbinghausNotebook();
    }

    static EbbinghausPageData CreateEbbinghausPage()=>new(){Words=Enumerable.Range(0,20).Select(_=>new EbbinghausWordData()).ToList()};

    void SaveNativeEbbinghausNotebook()
    {
        try
        {
            Directory.CreateDirectory(AppDataStorage.Root);
            // Keep the previous notebook intact if writing is interrupted or fails.
            string temporary=NativeEbbinghausPath+".tmp";
            File.WriteAllText(temporary,System.Text.Json.JsonSerializer.Serialize(nativeEbbinghausPages));
            if(File.Exists(NativeEbbinghausPath))File.Replace(temporary,NativeEbbinghausPath,NativeEbbinghausPath+".bak");
            else File.Move(temporary,NativeEbbinghausPath);
            if(nativeEbbinghausPageLabel!=null)nativeEbbinghausPageLabel.Text=$"第 {nativeEbbinghausPage+1} 页 / 共 {nativeEbbinghausPages.Count} 页 · 已保存";
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        {
            if(nativeEbbinghausPageLabel!=null)nativeEbbinghausPageLabel.Text="保存失败，请检查数据目录权限后重试";
        }
    }

    void ChangeNativeEbbinghausPage(int step)
    {
        int destination=nativeEbbinghausPage+step;
        if(destination<0)return;
        while(destination>=nativeEbbinghausPages.Count)nativeEbbinghausPages.Add(CreateEbbinghausPage());
        nativeEbbinghausPage=destination;
        SaveNativeEbbinghausNotebook();
        RenderNativeEbbinghausPage();
    }

    async void DeleteNativeEbbinghausPage()
    {
        if((uint)nativeEbbinghausPage>=(uint)nativeEbbinghausPages.Count)return;
        int pageNumber=nativeEbbinghausPage+1;
        var dialog=new C.ContentDialog
        {
            Title=$"删除第 {pageNumber} 页？",
            Content="本页填写的单词、释义和复习勾选都会被删除。",
            PrimaryButtonText="删除本页",CloseButtonText="取消",DefaultButton=C.ContentDialogButton.Close,
            XamlRoot=nativeRoot.XamlRoot,RequestedTheme=nativeRoot.RequestedTheme
        };
        C.ContentDialogResult result;
        NativePopup(true);try{result=await dialog.ShowAsync();}finally{NativePopup(false);}
        if(result!=C.ContentDialogResult.Primary)return;
        nativeEbbinghausPages.RemoveAt(nativeEbbinghausPage);
        if(nativeEbbinghausPages.Count==0)
        {
            nativeEbbinghausPages.Add(CreateEbbinghausPage());
            nativeEbbinghausPage=0;
        }
        else nativeEbbinghausPage=Math.Min(nativeEbbinghausPage,nativeEbbinghausPages.Count-1);
        SaveNativeEbbinghausNotebook();
        RenderNativeEbbinghausPage();
    }

    void RenderNativeEbbinghausPage()
    {
        if(nativeEbbinghausPages.Count==0)nativeEbbinghausPages.Add(CreateEbbinghausPage());
        nativeEbbinghausPage=Math.Clamp(nativeEbbinghausPage,0,nativeEbbinghausPages.Count-1);
        nativeEbbinghausPageLabel.Text=$"第 {nativeEbbinghausPage+1} 页 / 共 {nativeEbbinghausPages.Count} 页";
        nativeEbbinghausPrevious.IsEnabled=nativeEbbinghausPage>0;
        nativeEbbinghausNext.IsEnabled=true;
        var data=nativeEbbinghausPages[nativeEbbinghausPage];
        var table=new C.Grid{ColumnSpacing=0,RowSpacing=0,MinWidth=940};
        table.ColumnDefinitions.Add(new C.ColumnDefinition{Width=new X.GridLength(44)});
        table.ColumnDefinitions.Add(new C.ColumnDefinition{Width=new X.GridLength(1.2,X.GridUnitType.Star)});
        table.ColumnDefinitions.Add(new C.ColumnDefinition{Width=new X.GridLength(1.4,X.GridUnitType.Star)});
        for(int i=0;i<11;i++)table.ColumnDefinitions.Add(new C.ColumnDefinition{Width=new X.GridLength(50)});
        table.RowDefinitions.Add(new C.RowDefinition{Height=Auto});
        for(int i=0;i<20;i++)table.RowDefinitions.Add(new C.RowDefinition{Height=Auto});
        string[] headers={"#","单词","释义","1h","4h","12h","d1","d2","d3","d5","d6","d8","d14","d31"};
        for(int column=0;column<headers.Length;column++)
        {
            X.UIElement headerContent;
            if(column==2)
            {
                var meaningHeader=new C.Button
                {
                    Content=headers[column],Background=Brush(Color.Transparent),BorderBrush=Brush(Color.Transparent),
                    BorderThickness=new X.Thickness(0),Padding=new X.Thickness(4,0,4,0),MinWidth=0,MinHeight=28,
                    FontSize=12,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,
                    HorizontalAlignment=X.HorizontalAlignment.Stretch,HorizontalContentAlignment=X.HorizontalAlignment.Center,
                    VerticalContentAlignment=X.VerticalAlignment.Center
                };
                meaningHeader.Click+=(_,_)=>ToggleNativeEbbinghausMeaning();
                C.ToolTipService.SetToolTip(meaningHeader,nativeEbbinghausMeaningHidden?"点击显示释义":"点击隐藏释义");
                headerContent=meaningHeader;
            }
            else
            {
                var label=TextBlock(headers[column],12);label.TextAlignment=X.TextAlignment.Center;label.VerticalAlignment=X.VerticalAlignment.Center;label.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;
                if(column>=3)C.ToolTipService.SetToolTip(label,$"{headers[column]} 节点按单词首次填写时间计算；悬停下方方格可查看计划和实际打勾时间。");
                headerContent=label;
            }
            AddCell(headerContent,0,column,true,0);
        }
        for(int row=0;row<20;row++)
        {
            var entry=data.Words[row];
            var number=TextBlock((row+1).ToString("00"),12);number.TextAlignment=X.TextAlignment.Center;number.VerticalAlignment=X.VerticalAlignment.Center;
            var numberCell=AddCell(number,row+1,0,false,row);
            string rowTooltip=NativeEbbinghausWordTooltip(entry);
            C.ToolTipService.SetToolTip(number,rowTooltip);
            C.ToolTipService.SetToolTip(numberCell,rowTooltip);
            var word=new C.TextBox{Text=entry.Word,PlaceholderText="填写单词",MinHeight=34,FontSize=13,VerticalContentAlignment=X.VerticalAlignment.Center};
            var reviewControls=new List<(C.Button Button,C.Border Cell,int Checkpoint)>();
            word.TextChanged+=(_,_) =>
            {
                var value=word.Text;
                if(!string.Equals(entry.Word,value,StringComparison.Ordinal))
                {
                    var now=DateTimeOffset.Now;
                    if(entry.FirstWordEnteredAt==null&&!string.IsNullOrWhiteSpace(value))entry.FirstWordEnteredAt=now;
                    entry.LastWordModifiedAt=now;
                    entry.Word=value;
                    rowTooltip=NativeEbbinghausWordTooltip(entry);
                    C.ToolTipService.SetToolTip(number,rowTooltip);
                    C.ToolTipService.SetToolTip(numberCell,rowTooltip);
                    foreach(var review in reviewControls)
                    {
                        var tip=NativeEbbinghausReviewTooltip(entry,review.Checkpoint,headers[review.Checkpoint+3]);
                        C.ToolTipService.SetToolTip(review.Button,tip);
                        C.ToolTipService.SetToolTip(review.Cell,tip);
                    }
                    SaveNativeEbbinghausNotebook();
                }
            };
            AddCell(word,row+1,1,false,row);
            if(nativeEbbinghausMeaningHidden)
            {
                var hiddenMeaning=TextBlock(string.IsNullOrWhiteSpace(entry.Meaning)?"":"••••••",13);
                hiddenMeaning.VerticalAlignment=X.VerticalAlignment.Center;
                C.ToolTipService.SetToolTip(hiddenMeaning,"释义已隐藏，点击“释义”或上方按钮可显示。");
                AddCell(hiddenMeaning,row+1,2,false,row);
            }
            else
            {
                var meaning=new C.TextBox{Text=entry.Meaning,PlaceholderText="填写释义",MinHeight=34,FontSize=13,VerticalContentAlignment=X.VerticalAlignment.Center};
                meaning.TextChanged+=(_,_)=>{entry.Meaning=meaning.Text;SaveNativeEbbinghausNotebook();};
                AddCell(meaning,row+1,2,false,row);
            }
            for(int review=0;review<11;review++)
            {
                int checkpoint=review;
                var isChecked=entry.Completed[checkpoint];
                var mark=TextBlock(isChecked?"✓":"",13);mark.TextAlignment=X.TextAlignment.Center;mark.VerticalAlignment=X.VerticalAlignment.Center;mark.FontWeight=Microsoft.UI.Text.FontWeights.Bold;mark.Foreground=nativeAccentForeground;
                var square=new C.Border{Width=18,Height=18,CornerRadius=new X.CornerRadius(3),BorderThickness=new X.Thickness(1),BorderBrush=isChecked?nativeAccent:nativeStroke,Background=isChecked?nativeAccent:Brush(Color.Transparent),Child=mark,HorizontalAlignment=X.HorizontalAlignment.Center,VerticalAlignment=X.VerticalAlignment.Center};
                var toggle=new C.Button{Content=square,MinWidth=0,MinHeight=34,Padding=new X.Thickness(0),Background=Brush(Color.Transparent),BorderBrush=Brush(Color.Transparent),BorderThickness=new X.Thickness(0),HorizontalAlignment=X.HorizontalAlignment.Stretch,VerticalAlignment=X.VerticalAlignment.Stretch,HorizontalContentAlignment=X.HorizontalAlignment.Center,VerticalContentAlignment=X.VerticalAlignment.Center};
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle,$"{headers[review+3]} 复习完成");
                string reviewTooltip=NativeEbbinghausReviewTooltip(entry,checkpoint,headers[review+3]);
                C.ToolTipService.SetToolTip(toggle,reviewTooltip);
                var reviewCell=AddCell(toggle,row+1,review+3,false,row);
                C.ToolTipService.SetToolTip(reviewCell,reviewTooltip);
                reviewControls.Add((toggle,reviewCell,checkpoint));
                toggle.Click+=(_,_)=>
                {
                    entry.Completed[checkpoint]=!entry.Completed[checkpoint];
                    if(entry.Completed[checkpoint])entry.CompletedAt[checkpoint]=DateTimeOffset.Now;
                    mark.Text=entry.Completed[checkpoint]?"✓":"";
                    square.Background=entry.Completed[checkpoint]?nativeAccent:Brush(Color.Transparent);
                    square.BorderBrush=entry.Completed[checkpoint]?nativeAccent:nativeStroke;
                    // The for-loop variable is 11 by the time a button is clicked.
                    // Use this button's captured checkpoint, as for the stored state.
                    var updatedTooltip=NativeEbbinghausReviewTooltip(entry,checkpoint,headers[checkpoint+3]);
                    C.ToolTipService.SetToolTip(toggle,updatedTooltip);
                    C.ToolTipService.SetToolTip(reviewCell,updatedTooltip);
                    SaveNativeEbbinghausNotebook();
                };
            }
        }
        nativeEbbinghausTableHost.Child=new C.ScrollViewer{Content=table,HorizontalScrollBarVisibility=C.ScrollBarVisibility.Auto,VerticalScrollBarVisibility=C.ScrollBarVisibility.Auto,HorizontalContentAlignment=X.HorizontalAlignment.Stretch,VerticalContentAlignment=X.VerticalAlignment.Top};

        C.Border AddCell(X.UIElement content,int row,int column,bool header,int stripe)
        {
            var cell=new C.Border
            {
                Child=content,
                Background=header?nativePanel:stripe%2==0?nativePanel:nativeSurface,
                BorderBrush=nativeStroke,
                BorderThickness=new X.Thickness(0,0,1,1),
                Padding=header?new X.Thickness(6,8,6,8):new X.Thickness(6,2,6,2),
                VerticalAlignment=X.VerticalAlignment.Stretch
            };
            Put(table,cell,row,column);
            return cell;
        }
    }

    static string NativeEbbinghausFormatTime(DateTimeOffset? value)=>value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")??"未记录";

    static string NativeEbbinghausWordTooltip(EbbinghausWordData entry)
    {
        if(string.IsNullOrWhiteSpace(entry.Word))
            return "尚未填写单词。\n填写单词后会记录时间，并以首次填写时间计算复习节点。";
        if(entry.FirstWordEnteredAt==null)
            return "首次填写单词：旧记录未保存时间\n最近修改单词：未记录\n编辑单词后开始记录复习时间。";
        return $"首次填写单词：{NativeEbbinghausFormatTime(entry.FirstWordEnteredAt)}\n最近修改单词：{NativeEbbinghausFormatTime(entry.LastWordModifiedAt)}\n各复习节点按首次填写时间计算。";
    }

    static string NativeEbbinghausReviewTooltip(EbbinghausWordData entry,int checkpoint,string label)
    {
        TimeSpan offset=checkpoint switch
        {
            0=>TimeSpan.FromHours(1),
            1=>TimeSpan.FromHours(4),
            2=>TimeSpan.FromHours(12),
            3=>TimeSpan.FromDays(1),
            4=>TimeSpan.FromDays(2),
            5=>TimeSpan.FromDays(3),
            6=>TimeSpan.FromDays(5),
            7=>TimeSpan.FromDays(6),
            8=>TimeSpan.FromDays(8),
            9=>TimeSpan.FromDays(14),
            _=>TimeSpan.FromDays(31)
        };
        string planned=entry.FirstWordEnteredAt is DateTimeOffset first
            ?NativeEbbinghausFormatTime(first+offset)
            :"填写单词后开始计算";
        string actual=entry.CompletedAt[checkpoint] is DateTimeOffset completedAt
            ?NativeEbbinghausFormatTime(completedAt)
            :entry.Completed[checkpoint]?"已完成（旧记录未保存时间）":"尚未打勾";
        return $"{label} 复习节点\n计划打勾时间：{planned}\n实际打勾时间：{actual}";
    }

    C.Grid BuildNativeSheet()
    {
        var page=GridRows(Auto,Auto,Star);page.Padding=new X.Thickness(20);
        var top=new C.Grid{ColumnSpacing=12};top.ColumnDefinitions.Add(new C.ColumnDefinition{Width=Star});top.ColumnDefinitions.Add(new C.ColumnDefinition{Width=Auto});
        nativeSheets=new C.ComboBox{HorizontalAlignment=X.HorizontalAlignment.Stretch,MinWidth=100};nativeSheets.SelectionChanged+=(_,_)=>{if(!nativeSyncing && nativeSheets.SelectedItem is C.ComboBoxItem item)SwitchSheet((Guid)item.Tag);};
        nativeSheets.DropDownOpened+=(_,_)=>NativePopup(true);nativeSheets.DropDownClosed+=(_,_)=>NativePopup(false);
        Put(top,nativeSheets);nativeUseSheet=NativeButton("作为表格词库",SetWordSheet,true);Put(top,nativeUseSheet,0,1);Put(page,top);
        Put(page,Commands(("新建","\uE710",CreateSheet),("导入 Excel","\uE8B5",ImportExcelSheet),("遮挡 F1","\uE890",()=>ApplySheetShortcut(1)),("绿色 F2","\uE73E",()=>ApplySheetShortcut(2)),("红色 F3","\uE711",()=>ApplySheetShortcut(3)),("加粗 F4","\uE8DD",()=>ApplySheetShortcut(4)),("删除表格","\uE74D",()=>DeleteSheet(activeSheetId)),("说明","\uE897",()=>NativeMessage("表格词库","A 列：卡牌顶部文字\nB 列：释义\nC 列：备注\n\n支持复制粘贴 Excel 多个单元格、Ctrl+滚轮缩放。修改自动保存。点击“作为表格词库”返回复习页。"))),1);
        nativeSheetSlot=new C.Border{Background=nativeSurface};Put(page,nativeSheetSlot,2);return page;
    }

    C.Grid BuildNativeCanvas() => BuildCanvasWorkspace();

    C.MenuFlyout BuildNativeCreateMenu(Action<string>? create = null, Action? import = null)
    {
        var menu = new C.MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        foreach (var (label, kind, glyph) in new[] {
            ("文本框", "text", "T"), ("方块", "square", "□"),
            ("圆形", "circle", "○"), ("三角形", "triangle", "△"), ("五角星", "star", "☆") })
        {
            var item = new C.MenuFlyoutItem { Text = label, Icon = new C.FontIcon { Glyph = glyph, FontFamily = new M.FontFamily("Segoe UI Symbol"), FontSize = 18 } };
            item.Click += (_, _) =>
            {
                if (create != null) { create(kind); NativeSync(); return; }
                var size = kind == "text" ? new Size(250,170) : new Size(140,140);
                var position = new PointF((canvasSurface.Width / 2f - canvasOffset.X) / canvasZoom - size.Width / 2f,
                    (canvasSurface.Height / 2f - canvasOffset.Y) / canvasZoom - size.Height / 2f);
                CreateCanvasNode(kind, position, ""); NativeSync();
            };
            menu.Items.Add(item);
        }
        menu.Items.Add(new C.MenuFlyoutSeparator());
        var image = new C.MenuFlyoutItem { Text = "图片…", Icon = new C.SymbolIcon(C.Symbol.Pictures) };
        image.Click += (_, _) => { if (import != null) import(); else ImportCanvasImage(new Point(canvasSurface.Width / 2,canvasSurface.Height / 2)); NativeSync(); };
        menu.Items.Add(image);
        menu.Opened += (_, _) => NativePopup(true);
        menu.Closed += (_, _) => NativePopup(false);
        return menu;
    }

    void ShowNativeConnectedCreateMenu(CanvasNode start, Point drop, bool fromStart)
    {
        var board = activeCanvasId;
        var world = new PointF((drop.X - canvasOffset.X) / canvasZoom, (drop.Y - canvasOffset.Y) / canvasZoom);
        bool SourceExists() => board == activeCanvasId && canvasNodes.Contains(start);
        void Connect(CanvasNode node)
        {
            // Creation already saved the undo snapshot, so creation and linking undo together.
            canvasLinks.Add(new CanvasLink { From = fromStart ? start.Id : node.Id, To = fromStart ? node.Id : start.Id });
            InvalidateCanvasPaintCache(); canvasSurface.Invalidate(); SaveCanvas();
        }
        var menu = BuildNativeCreateMenu(kind =>
        {
            if (!SourceExists()) return;
            var size = kind == "text" ? new Size(250,170) : new Size(140,140);
            CreateCanvasNode(kind, new PointF(world.X-size.Width/2f,world.Y-size.Height/2f), "");
            Connect(canvasNodes[^1]);
        }, () =>
        {
            if (!SourceExists()) return;
            var existing = canvasNodes.Select(node => node.Id).ToHashSet();
            ImportCanvasImage(drop);
            var created = canvasNodes.LastOrDefault(node => !existing.Contains(node.Id));
            if (created == null || !SourceExists()) return;
            created.Position = new PointF(
                (float)Math.Round((world.X-created.Size.Width/2f)/CanvasGridSize)*CanvasGridSize,
                (float)Math.Round((world.Y-created.Size.Height/2f)/CanvasGridSize)*CanvasGridSize);
            Connect(created);
        });
        double scale = nativeRoot.XamlRoot?.RasterizationScale ?? DeviceDpi/96.0;
        menu.ShowAt(nativeCanvasSlot,new FlyoutShowOptions { Position=new Windows.Foundation.Point(drop.X/scale,drop.Y/scale) });
    }

    void CreateNativeBoard(string name)
    {
        CommitCanvasInlineEdit();SaveCanvas();var id=Guid.NewGuid();canvasBoards.Add((id,name));SaveCanvasBoardIndex();SwitchCanvasBoard(id);
    }
    void RenameNativeBoard(string name)
    {
        int i=canvasBoards.FindIndex(b=>b.Id==activeCanvasId);if(i<0)return;canvasBoards[i]=canvasBoards[i] with{Name=name};SaveCanvasBoardIndex();UpdateCanvasPicker();
    }

    void OpenNativeInspector(bool layers)
    {
        canvasFocusMode=false; canvasPreferProperties=!layers;
        if(layers)nativeLayersOpen=nativeLayers.Visibility!=X.Visibility.Visible;else nativePropertiesOpen=nativeProps.Visibility!=X.Visibility.Visible;
        SaveCanvasWorkspace();
        NativeResponsiveLayout();
    }

    void OpenNativePropertiesForSelection()
    {
        // Keep viewport coordinates stable while selecting; reveal panels with the toolbar.
    }
    C.StackPanel BuildNativeProperties()
    {
        var body=Stack(14);body.Children.Add(TextBlock("属性",22));
        var status=TextBlock("选择画布上的控件",12);body.Children.Add(status);
        nativePropertySync.Add(()=>status.Text=selectedCanvasLink!=null?"已选择连线":$"已选择 {canvasSelectedIds.Count} 个控件");
        void Entry(string label,TextBox model,Action apply,bool multiline=false)
        {
            var field=new C.TextBox{Header=label,AcceptsReturn=multiline,TextWrapping=multiline?X.TextWrapping.Wrap:X.TextWrapping.NoWrap};body.Children.Add(field);
            field.LostFocus+=(_,_)=>{if(nativeSyncing)return;model.Text=field.Text;apply();};
            nativePropertySync.Add(()=>{field.IsEnabled=selectedCanvasNode!=null||selectedCanvasLink!=null;if(field.FocusState==X.FocusState.Unfocused&&field.Text!=model.Text)field.Text=model.Text;});
        }
        Entry("名称",canvasPropName,ApplyCanvasPropName);
        void Number(string label,NumericUpDown model)
        {
            var number=new C.NumberBox{Header=label,Minimum=(double)model.Minimum,Maximum=(double)model.Maximum,SmallChange=(double)model.Increment,SpinButtonPlacementMode=C.NumberBoxSpinButtonPlacementMode.Compact,HorizontalAlignment=X.HorizontalAlignment.Stretch,FontSize=13};
            number.NumberFormatter=new Windows.Globalization.NumberFormatting.DecimalFormatter{FractionDigits=0,IntegerDigits=1,NumberRounder=new Windows.Globalization.NumberFormatting.IncrementNumberRounder{Increment=.01, RoundingAlgorithm=Windows.Globalization.NumberFormatting.RoundingAlgorithm.RoundHalfUp}};
            number.GotFocus+=(_,_)=>canvasTransformHistoryPushed=false;number.LostFocus+=(_,_)=>canvasTransformHistoryPushed=false;
            number.ValueChanged+=(_,_)=>{if(!nativeSyncing&&!double.IsNaN(number.Value))model.Value=Math.Clamp((decimal)number.Value,model.Minimum,model.Maximum);};
            nativePropertySync.Add(()=>{number.IsEnabled=selectedCanvasNode!=null||canvasSelectedIds.Count>1;if(number.FocusState==X.FocusState.Unfocused&&number.Value!=(double)model.Value)number.Value=(double)model.Value;});body.Children.Add(number);
        }
        Number("X",canvasPropX);Number("Y",canvasPropY);Number("宽度",canvasPropWidth);Number("高度",canvasPropHeight);Number("旋转角度",canvasPropRotation);Number("不透明度 %",canvasPropOpacity);Number("填充度 %",canvasPropFillOpacity);
        body.Children.Add(NativeButton("填充颜色",()=>NativePickColor("填充颜色",selectedCanvasNode==null?activeThemeColor:CanvasNodeFillColor(selectedCanvasNode),c=>ApplyStyleBatch(CanvasFillStyleNodes(),n=>n.FillColor=c.ToArgb().ToString(System.Globalization.CultureInfo.InvariantCulture)))));
        body.Children.Add(NativeButton("恢复默认填充",ResetCanvasPropFillColor));
        Number("圆角",canvasPropCornerRadius);Number("描边宽度",canvasPropStrokeWidth);
        body.Children.Add(NativeButton("描边颜色",()=>NativePickColor("描边颜色",selectedCanvasNode==null?activeThemeColor:CanvasNodeStrokeColor(selectedCanvasNode),c=>ApplyStyleBatch(CanvasShapeStyleNodes(),n=>n.StrokeColor=c.ToArgb().ToString(System.Globalization.CultureInfo.InvariantCulture)))));
        body.Children.Add(NativeButton("恢复默认描边",ResetCanvasPropStrokeColor));
        Combo("描边位置",new[]{"内部","中间","外部"},()=>selectedCanvasNode?.StrokeAlign??1,ApplyCanvasPropStrokeAlign);
        Check("虚线",canvasPropStrokeDash);
        Entry("文字内容",canvasPropContent,ApplyCanvasPropContent,true);Number("字号",canvasPropFontSize);
        Check("加粗",canvasPropBold);Check("斜体",canvasPropItalic);Check("删除线",canvasPropStrikeout);
        Combo("水平对齐",new[]{"左对齐","居中","右对齐"},()=>selectedCanvasNode?.FontAlign??0,ApplyCanvasPropFontAlign);
        Combo("垂直对齐",new[]{"顶部","居中","底部"},()=>selectedCanvasNode?.FontVAlign??0,ApplyCanvasPropFontVAlign);
        body.Children.Add(NativeButton("文字颜色",()=>NativePickColor("文字颜色",selectedCanvasNode==null?activeThemeColor:CanvasNodeTextColor(selectedCanvasNode,false),c=>ApplyStyleBatch(CanvasStyleNodes(),n=>n.FontColor=c.ToArgb().ToString(System.Globalization.CultureInfo.InvariantCulture)))));
        body.Children.Add(NativeButton("恢复默认文字颜色",ResetCanvasPropColor));
        Entry("连线文字",canvasPropLinkText,ApplyCanvasLinkLabel);
        Combo("连线箭头",new[]{"无箭头","终点","起点","双向"},()=>selectedCanvasLink?.Arrow??0,ApplyCanvasLinkArrow);
        body.Children.Add(NativeButton("连线颜色",()=>NativePickColor("连线颜色",selectedCanvasLink==null?activeThemeColor:CanvasLinkColor(selectedCanvasLink),ApplyNativeCanvasLinkColor)));
        body.Children.Add(NativeButton("恢复默认连线颜色",ResetCanvasLinkColor));
        var align=new C.ComboBox{Header="多选控件对齐",HorizontalAlignment=X.HorizontalAlignment.Stretch};foreach(var s in new[]{"左边缘","水平居中","右边缘","上边缘","垂直居中","下边缘","水平分布","垂直分布"})align.Items.Add(s);align.SelectionChanged+=(_,_)=>{if(!nativeSyncing&&align.SelectedIndex>=0)AlignCanvasSelection(align.SelectedIndex);};body.Children.Add(align);
        foreach(var child in body.Children.Skip(2).OfType<X.FrameworkElement>())
        {
            string label=child switch{C.NumberBox n=>n.Header?.ToString()??"",C.TextBox t=>t.Header?.ToString()??"",C.ComboBox c=>c.Header?.ToString()??"",C.ContentControl c=>c.Content?.ToString()??"",_=>""};
            nativePropertySync.Add(()=>
            {
                var selected=CanvasSelectedNodes();bool single=selected.Count==1, text=selected.Any(n=>n.Kind=="text"), shape=selected.Any(IsCanvasShapeKind), link=selectedCanvasLink!=null;
                bool visible=label switch
                {
                    "名称"=>single,
                    "X" or "Y" or "宽度" or "高度" or "旋转角度" or "不透明度 %" or "填充度 %"=>single,
                    "填充颜色" or "恢复默认填充"=>text||shape,
                    "圆角"=>single&&selected[0].Kind=="square",
                    "描边宽度" or "描边颜色" or "恢复默认描边" or "描边位置" or "虚线"=>shape,
                    "文字内容"=>single&&text,
                    "字号" or "加粗" or "斜体" or "删除线" or "水平对齐" or "垂直对齐" or "文字颜色" or "恢复默认文字颜色"=>text,
                    "连线文字" or "连线箭头" or "连线颜色" or "恢复默认连线颜色"=>link,
                    "多选控件对齐"=>selected.Count>=2,
                    _=>selected.Count>0
                };
                child.Visibility=visible?X.Visibility.Visible:X.Visibility.Collapsed;
            });
        }
        PairNumbers("X","Y");PairNumbers("宽度","高度");PairNumbers("不透明度 %","填充度 %");
        return body;
        void PairNumbers(string first,string second)
        {
            var a=body.Children.OfType<C.NumberBox>().First(n=>n.Header?.ToString()==first);var b=body.Children.OfType<C.NumberBox>().First(n=>n.Header?.ToString()==second);
            int at=body.Children.IndexOf(a);body.Children.Remove(a);body.Children.Remove(b);
            var pair=new C.Grid{ColumnSpacing=8};pair.ColumnDefinitions.Add(new C.ColumnDefinition{Width=Star});pair.ColumnDefinitions.Add(new C.ColumnDefinition{Width=Star});a.MinWidth=b.MinWidth=0;Put(pair,a);Put(pair,b,0,1);body.Children.Insert(at,pair);nativePropertySync.Add(()=>pair.Visibility=a.Visibility==X.Visibility.Visible||b.Visibility==X.Visibility.Visible?X.Visibility.Visible:X.Visibility.Collapsed);
        }
        void Check(string label,CheckBox model)
        {
            var check=new C.CheckBox{Content=label,IsThreeState=true};check.Click+=(_,_)=>{if(!nativeSyncing)model.CheckState=check.IsChecked==null?CheckState.Indeterminate:check.IsChecked==true?CheckState.Checked:CheckState.Unchecked;};nativePropertySync.Add(()=>check.IsChecked=model.CheckState==CheckState.Indeterminate?null:model.Checked);body.Children.Add(check);
        }
        void Combo(string label,string[] items,Func<int> get,Action<int> set)
        {
            var combo=new C.ComboBox{Header=label,HorizontalAlignment=X.HorizontalAlignment.Stretch};foreach(var item in items)combo.Items.Add(item);combo.SelectionChanged+=(_,_)=>{if(!nativeSyncing&&combo.SelectedIndex>=0)set(combo.SelectedIndex);};nativePropertySync.Add(()=>combo.SelectedIndex=Math.Clamp(get(),0,items.Length-1));body.Children.Add(combo);
        }
    }

    C.ScrollViewer BuildNativeSettings()
    {
        var body=Stack(12);body.HorizontalAlignment=X.HorizontalAlignment.Left;body.Margin=new X.Thickness(24);
        body.Children.Add(BuildBackgroundSettings());body.Children.Add(BuildDeepSeekSettings());
        body.Children.Add(TextBlock("个性化  ›  颜色",30));
        var mode=new C.ComboBox{Header="选择模式",HorizontalAlignment=X.HorizontalAlignment.Stretch};mode.Items.Add("浅色");mode.Items.Add("深色");mode.SelectedIndex=darkTheme?1:0;mode.SelectionChanged+=(_,_)=>SetThemeMode(mode.SelectedIndex==1);body.Children.Add(Surface(mode));
        var motion=new C.ToggleSwitch{Header="动画效果",IsOn=true,OnContent="开",OffContent="关"};motion.Toggled+=(_,_)=>nativeAnimations=motion.IsOn;body.Children.Add(Surface(motion));
        var transparency=new C.ToggleSwitch{Header="透明效果",IsOn=nativeTransparencyEnabled,OnContent="开",OffContent="关"};
        transparency.Toggled+=(_,_)=>
        {
            nativeTransparencyEnabled=transparency.IsOn;
            Directory.CreateDirectory(Path.GetDirectoryName(NativeTransparencyPath)!);
            File.WriteAllText(NativeTransparencyPath,nativeTransparencyEnabled?"on":"off");
            ApplyNativeTheme();
        };
        var transparencySetting=Stack(6);transparencySetting.Children.Add(transparency);transparencySetting.Children.Add(TextBlock("让窗口和侧栏呈现轻微透明的亚克力效果",12));body.Children.Add(Surface(transparencySetting));
        var theme=Stack(16);theme.Children.Add(TextBlock("主题色",22));theme.Children.Add(TextBlock("最近使用的颜色"));
        var recent=new C.Grid{ColumnSpacing=6};for(int i=0;i<4;i++)recent.ColumnDefinitions.Add(new C.ColumnDefinition{Width=Star});
        for(int i=0;i<4;i++){int n=i;var b=NativeButton("",()=>{if(n<recentThemeColors.Count)SetThemeColor(recentThemeColors[n]);});b.Height=48;nativePropertySync.Add(()=>{b.Background=Brush(n<recentThemeColors.Count?recentThemeColors[n]:activeThemeColor);});Put(recent,b,0,i);}theme.Children.Add(recent);
        theme.Children.Add(TextBlock("Windows 颜色"));
        string[] palette={"#FFB900","#FF8C00","#F7630C","#CA5010","#DA3B01","#EF6950","#D13438","#FF4343","#E74856","#E81123","#EA005E","#C30052","#E3008C","#BF0077","#C239B3","#9A0089","#0078D4","#0063B1","#8E8CD8","#6B69D6","#8764B8","#744DA9","#B146C2","#881798","#0099BC","#2D7D9A","#00B7C3","#038387","#00B294","#018574","#00CC6A","#10893E","#7A7574","#5D5A58","#68768A","#515C6B","#567C73","#486860","#498205","#107C10","#767676","#4C4A48","#69797E","#4A5459","#647C64","#525E54","#847545","#7E735F"};
        var swatches=new C.Grid{ColumnSpacing=5,RowSpacing=5};for(int i=0;i<8;i++)swatches.ColumnDefinitions.Add(new C.ColumnDefinition{Width=Star});for(int i=0;i<6;i++)swatches.RowDefinitions.Add(new C.RowDefinition{Height=new X.GridLength(44)});
        for(int i=0;i<palette.Length;i++){var color=ColorTranslator.FromHtml(palette[i]);var b=NativeButton("",()=>SetThemeColor(color));b.Background=Brush(color);b.MinWidth=0;b.Padding=new X.Thickness(0);b.BorderThickness=new X.Thickness(0);Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b,$"主题色 {palette[i]}");nativePropertySync.Add(()=>{b.Content=activeThemeColor.ToArgb()==color.ToArgb()?"✓":"";b.Foreground=Brush(Contrast(color));});Put(swatches,b,i/8,i%8);}theme.Children.Add(swatches);
        theme.Children.Add(NativeButton("自定义颜色",()=>NativePickColor("自定义主题色",activeThemeColor,SetThemeColor)));
        theme.Children.Add(NativeButton("主要操作预览",()=>{},true));body.Children.Add(Surface(theme));
        var uptime=Stack(4);uptime.Children.Add(TextBlock("软件本次打开时间",14));nativeClock=TextBlock("00:00:00",20);nativeClock.FontFamily=new M.FontFamily("Consolas");uptime.Children.Add(nativeClock);body.Children.Add(Surface(uptime));
        var settingsScroll=Scroll(body);settingsScroll.HorizontalContentAlignment=X.HorizontalAlignment.Left;settingsScroll.HorizontalScrollMode=C.ScrollMode.Disabled;settingsScroll.ZoomMode=C.ZoomMode.Disabled;
        settingsScroll.SizeChanged+=(_,_)=>body.Width=Math.Max(180,Math.Min(1000,settingsScroll.ActualWidth-48));
        body.Children.Add(TextBlock("危险操作",18));
        body.Children.Add(TextBlock("恢复出厂设置会删除本机保存的词库、错题记录、画布及导入图片、所有表格和应用设置。此操作无法撤销。",12));
        var factoryReset=NativeButton("恢复出厂设置",NativeFactoryResetClicked);
        factoryReset.Background=Brush(Color.FromArgb(196,43,28));factoryReset.Foreground=Brush(Color.White);
        body.Children.Add(factoryReset);
        var version=TextBlock("当前版本：1.2.0",12);version.Margin=new X.Thickness(0,8,0,16);body.Children.Add(version);
        return settingsScroll;
    }

    static Windows.UI.Color WinColor(Color color)=>Windows.UI.Color.FromArgb(color.A,color.R,color.G,color.B);
    bool NativeTextInputHasFocus()
    {
        if(!nativeReady || nativeRoot.XamlRoot==null)return false;
        for(var focused=FocusManager.GetFocusedElement(nativeRoot.XamlRoot) as X.DependencyObject;focused!=null;focused=M.VisualTreeHelper.GetParent(focused))
            if(focused is C.TextBox or C.RichEditBox or C.PasswordBox or C.NumberBox or C.ComboBox)return true;
        return false;
    }
    static M.SolidColorBrush Brush(Color color)=>new(WinColor(color));
    static Color Contrast(Color color)=>color.R*.2126+color.G*.7152+color.B*.0722>165?Color.FromArgb(20,20,20):Color.White;
    void InstallNativeAccentResources()
    {
        nativeAccent.Color=WinColor(activeThemeColor);nativeAccentHover.Color=WinColor(Blend(activeThemeColor,Color.White,.10f));nativeAccentPressed.Color=WinColor(Blend(activeThemeColor,Color.Black,.14f));nativeAccentForeground.Color=WinColor(Contrast(activeThemeColor));
        if(nativeResourcesInstalled)return;
        foreach(var dictionary in new[]{X.Application.Current.Resources,nativeRoot.Resources})
        {
            foreach(var (key,resource) in NativeAccentAliases)dictionary[key]=resource switch{"AccentFillColorSecondaryBrush"=>nativeAccentHover,"AccentFillColorTertiaryBrush"=>nativeAccentPressed,_=>nativeAccent};
            foreach(var key in new[]{"AccentButtonForeground","AccentButtonForegroundPointerOver","AccentButtonForegroundPressed","CheckBoxCheckGlyphForegroundChecked","ToggleSwitchKnobFillOn","TextOnAccentFillColorPrimaryBrush"})dictionary[key]=nativeAccentForeground;
            dictionary["TextControlBorderBrushFocused"]=nativeAccent;
        }
        nativeResourcesInstalled=true;
    }
    void ApplyNativeTheme()
    {
        if(!nativeReady)return;
        InstallNativeAccentResources();
        nativeRoot.RequestedTheme=darkTheme?X.ElementTheme.Dark:X.ElementTheme.Light;
        nativeBackground.Color=WinColor(darkTheme?Color.FromArgb(32,32,32):Color.FromArgb(243,243,243));
        nativeSurface.Color=WinColor(darkTheme?Color.FromArgb(43,43,43):Color.FromArgb(251,251,251));
        nativePanel.Color=WinColor(darkTheme?Color.FromArgb(37,38,41):Color.FromArgb(235,238,244));
        nativeLayerSurface.Color=WinColor(darkTheme?Color.FromArgb(45,45,48):Color.FromArgb(245,245,248));
        bool acrylic=nativeHost?.SetAcrylicEnabled(nativeTransparencyEnabled)==true;
        // Keep the content plane opaque so native HWND surfaces cannot bleed through after page changes.
        nativeRoot.Background=nativeBackground;
        if(acrylic)
        {
            var panel=nativePanel.Color;panel.A=210;nativePanel.Color=panel;
            var layers=nativeLayerSurface.Color;layers.A=215;nativeLayerSurface.Color=layers;
        }
        nativeStroke.Color=WinColor(darkTheme?Color.FromArgb(65,65,65):Color.FromArgb(225,225,225));nativeAccent.Color=WinColor(activeThemeColor);
        nativeAccentButtons.RemoveAll(reference=>!reference.TryGetTarget(out _));
        foreach(var reference in nativeAccentButtons)if(reference.TryGetTarget(out var button)){button.Background=nativeAccent;button.Foreground=Brush(Contrast(activeThemeColor));}
        BackColor=darkTheme?Color.FromArgb(32,32,32):Color.FromArgb(243,243,243);
        canvasSurface.BackColor=darkTheme?Color.FromArgb(28,28,30):Color.FromArgb(250,250,250);
        sheetGrid.BackgroundColor=sheetGrid.DefaultCellStyle.BackColor=darkTheme?Color.FromArgb(35,35,35):Color.White;
        sheetGrid.DefaultCellStyle.ForeColor=darkTheme?Color.White:Color.FromArgb(28,28,28);
        sheetGrid.DefaultCellStyle.SelectionBackColor=Blend(sheetGrid.BackgroundColor,activeThemeColor,.32f);sheetGrid.DefaultCellStyle.SelectionForeColor=sheetGrid.DefaultCellStyle.ForeColor;
        sheetGrid.ColumnHeadersDefaultCellStyle.BackColor=sheetGrid.RowHeadersDefaultCellStyle.BackColor=BackColor;
        sheetGrid.ColumnHeadersDefaultCellStyle.ForeColor=sheetGrid.RowHeadersDefaultCellStyle.ForeColor=sheetGrid.DefaultCellStyle.ForeColor;
        sheetGrid.GridColor=darkTheme?Color.FromArgb(60,60,60):Color.FromArgb(224,224,224);
        canvasInlineEditor.BackColor=sheetGrid.BackgroundColor;canvasInlineEditor.ForeColor=sheetGrid.DefaultCellStyle.ForeColor;
        ApplyNativeGazeTheme(); UpdateNativeSheetAppearance(); ConfigureWindows11TitleBar();sheetGrid.Invalidate();canvasSurface.Invalidate();
        if(nativeNotesList!=null)RefreshNativeNotesList();
        nativeSyncing=true;try{foreach(var sync in nativePropertySync)sync();}finally{nativeSyncing=false;}
    }

    void NativeSync()
    {
        if(!nativeReady||nativeSyncing||IsDisposed||WindowState==FormWindowState.Minimized)return;
        nativeSyncing=true;
        try
        {
            nativeClock.Text=timerLabel.Text;
            if(nativePage==0)
            {
                string key=$"{current}|{showingAnswer}|{cards.Count}|{word.Text}|{hint.Text}";
                if(key!=nativeDeckKey)
                {
                    nativeDeckKey=key;
                    if(cards.Count>0 && order.Count>0){var item=cards[order[current%order.Count]];nativeWord.Text=item.Word;nativeMeaning.Text=showingAnswer?string.Join("\n",new[]{item.Meaning,item.Note}.Where(s=>!string.IsNullOrEmpty(s))):"";}
                    else{nativeWord.Text="开始今日背诵";nativeMeaning.Text="添加单词，或选择表格作为词库";}
                    nativeProgress.Text=cards.Count==0?"词库为空":$"{current%cards.Count+1} / {cards.Count}   ·   已掌握 {known}   ·   待复习 {repeats}";
                    nativeSource.Text=$"{(wordSheetActive&&wordSheetId!=Guid.Empty?"正在使用表格词库":"正在使用本地词库")}  ·  {(randomMode?"随机复习":"顺序复习")}";
                    if(nativeAnimations && new Windows.UI.ViewManagement.UISettings().AnimationsEnabled){var animation=new A.DoubleAnimation{From=.65,To=1,Duration=new X.Duration(TimeSpan.FromMilliseconds(140))};A.Storyboard.SetTarget(animation,nativeCard);A.Storyboard.SetTargetProperty(animation,"Opacity");var storyboard=new A.Storyboard();storyboard.Children.Add(animation);storyboard.Begin();}
                }
                RefreshNativeWordLists();
            }
            string sheetKey=string.Join('|',sheets.Select(s=>$"{s.Id}:{s.Name}"));
            if(sheetKey!=nativeSheetKey){nativeSheetKey=sheetKey;nativeSheets.Items.Clear();foreach(var s in sheets)nativeSheets.Items.Add(new C.ComboBoxItem{Content=s.Name,Tag=s.Id});}
            int selectedSheet=sheets.FindIndex(s=>s.Id==activeSheetId);if(nativeSheets.SelectedIndex!=selectedSheet)nativeSheets.SelectedIndex=selectedSheet;
            nativeUseSheet.Content=activeSheetId==wordSheetId?"使用此词库复习":"作为表格词库";
            string boardKey=string.Join('|',canvasBoards.Select(b=>$"{b.Id}:{b.Name}"));
            if(boardKey!=nativeBoardKey){nativeBoardKey=boardKey;nativeBoards.Items.Clear();foreach(var b in canvasBoards)nativeBoards.Items.Add(new C.ComboBoxItem{Content=b.Name,Tag=b.Id});}
            int selectedBoard=canvasBoards.FindIndex(b=>b.Id==activeCanvasId);if(nativeBoards.SelectedIndex!=selectedBoard)nativeBoards.SelectedIndex=selectedBoard;
            if(nativePage==1){UpdateCanvasProps();foreach(var sync in nativePropertySync)sync();RefreshNativeLayers();SyncCanvasWorkspaceUi();}
            canvasPropsPanel.Visible=canvasLayersPanel.Visible=canvasAlignBar.Visible=false;
            if(nativePage==6)foreach(var sync in nativePropertySync)sync();
            PositionNativeSurfaces();
        }
        finally{nativeSyncing=false;}
    }

    void UpdateNativeSheetAppearance()
    {
        sheetGrid.Dock=DockStyle.None;sheetGrid.ScrollBars=ScrollBars.Both;wordSheetScrollBar.Visible=false;wordSheetGuide.Visible=false;
        bool active=wordSheetId!=Guid.Empty&&activeSheetId==wordSheetId;
        for(int c=0;c<sheetGrid.Columns.Count;c++)
        {
            var column=sheetGrid.Columns[c];column.HeaderText=active&&c<3?new[]{"顶部文字","中间文字","底部文字"}[c]:column.Name;
            column.Visible=!active||c<3;column.ReadOnly=active&&c>=3;
            column.DefaultCellStyle.BackColor=Color.Empty;column.DefaultCellStyle.ForeColor=Color.Empty;
            column.DefaultCellStyle.SelectionBackColor=Color.Empty;column.DefaultCellStyle.SelectionForeColor=Color.Empty;
            column.HeaderCell.Style.BackColor=Color.Empty;column.HeaderCell.Style.ForeColor=Color.Empty;
        }
        PositionNativeSurfaces();sheetGrid.Invalidate();
    }

    void RefreshNativeWordLists()
    {
        string bankKey=string.Join('|',savedCards);
        if(nativeBankKey!=bankKey)
        {
            nativeBankKey=bankKey;nativeBankList.Items.Clear();
            for(int i=0;i<savedCards.Count;i++)
            {
                int index=i;var item=savedCards[i];var row=Stack(3);row.Tag=item;row.Background=nativePanel;row.Padding=new X.Thickness(10,8,10,8);row.HorizontalAlignment=X.HorizontalAlignment.Stretch;var title=TextBlock(item.Word,14);title.MaxLines=1;title.TextTrimming=X.TextTrimming.CharacterEllipsis;var subtitle=TextBlock(item.Meaning,12);subtitle.MaxLines=1;subtitle.TextTrimming=X.TextTrimming.CharacterEllipsis;row.Children.Add(title);row.Children.Add(subtitle);if(!string.IsNullOrWhiteSpace(item.Note)){var noteLine=TextBlock(item.Note,12);noteLine.MaxLines=1;noteLine.TextTrimming=X.TextTrimming.CharacterEllipsis;row.Children.Add(noteLine);}C.ToolTipService.SetToolTip(row,item.Note);
                var menu=new C.MenuFlyout();Add("编辑",()=>NativeEditWord(index));Add("删除",()=>{var old=savedCards[index];savedCards.RemoveAt(index);reviewSource.RemoveAll(x=>x.Word==old.Word&&x.Meaning==old.Meaning);cards.RemoveAll(x=>x.Word==old.Word&&x.Meaning==old.Meaning);SaveWordBank();ResetOrder();RenderCard();});Add("移到顶部",()=>{var old=savedCards[index];savedCards.RemoveAt(index);savedCards.Insert(0,old);SaveWordBank();});Add("移到底部",()=>{var old=savedCards[index];savedCards.RemoveAt(index);savedCards.Add(old);SaveWordBank();});
                row.ContextFlyout=menu;nativeBankList.Items.Add(row);
                void Add(string label,Action action){var m=new C.MenuFlyoutItem{Text=label};m.Click+=(_,_)=>{action();NativeSync();};menu.Items.Add(m);}
            }
        }
        string wrongKey=$"{hideWrongMeaning}|"+string.Join('|',wrongCards);
        if(nativeWrongKey!=wrongKey)
        {
            nativeWrongKey=wrongKey;nativeWrongList.Items.Clear();
            for(int i=0;i<wrongCards.Count;i++)
            {
                int index=i;var item=wrongCards[i];var row=Stack(4);row.Tag=item;row.Background=nativePanel;row.Padding=new X.Thickness(10,8,10,8);row.HorizontalAlignment=X.HorizontalAlignment.Stretch;
                row.Children.Add(TextBlock(item.Word,16));row.Children.Add(TextBlock(hideWrongMeaning?"••••••":item.Meaning,13));
                var menu=new C.MenuFlyout();var delete=new C.MenuFlyoutItem{Text="删除"};delete.Click+=(_,_)=>RemoveWrongCard(index);menu.Items.Add(delete);row.ContextFlyout=menu;
                nativeWrongList.Items.Add(row);
            }
        }

        void RemoveWrongCard(int index)
        {
            if((uint)index>=(uint)wrongCards.Count)return;
            wrongCards.RemoveAt(index);SaveWrongBook();nativeWrongKey="";NativeSync();
        }
    }

    void RefreshNativeLayers()
    {
        if(nativeLayerDraggingRow!=null)return;
        string key=string.Join('|',canvasNodes.Select(n=>$"{n.Id}:{n.Name}:{n.Kind}:{n.Content.GetHashCode()}:{n.Extra.Label}:{n.ClipMask}:{IsSmartLayer(n)}:{n.Hidden}:{n.Locked}:{canvasSelectedIds.Contains(n.Id)}"));
        if(key==nativeLayerKey)return;
        double scrollOffset=nativeLayerScroll.VerticalOffset;
        nativeLayerKey=key;nativeLayersBody.Children.Clear();nativeLayerDropLines.Clear();nativeLayersBody.Children.Add(new C.TextBlock{Text=$"图层   ·   {canvasNodes.Count}",FontSize=12,Opacity=.65,Margin=new X.Thickness(6,8,6,10)}); if(canvasNodes.Count==0)nativeLayersBody.Children.Add(new C.TextBlock{Text="画布还是空的\n添加的对象会出现在这里",FontSize=12,Opacity=.55,Margin=new X.Thickness(6,12,6,12),TextWrapping=X.TextWrapping.Wrap});
        foreach(var node in canvasNodes.AsEnumerable().Reverse())
        {
            var row=new C.Grid{ColumnSpacing=2,MinHeight=40,Tag=node.Id,Background=canvasSelectedIds.Contains(node.Id)?nativePanel:Brush(Color.Transparent)};
            row.ColumnDefinitions.Add(new C.ColumnDefinition{Width=Star});
            row.ColumnDefinitions.Add(new C.ColumnDefinition{Width=new X.GridLength(32)});
            row.ColumnDefinitions.Add(new C.ColumnDefinition{Width=new X.GridLength(32)});
            string name=string.IsNullOrWhiteSpace(node.Name)?(node.Kind=="text"&&!string.IsNullOrWhiteSpace(node.Content)?node.Content.Split('\n')[0]:node.Kind=="square"?"矩形":node.Kind=="circle"?"椭圆":CanvasShapeDisplayName(node.Kind)):node.Name;
            var label=new C.Grid{Background=Brush(Color.Transparent),Padding=new X.Thickness(6,0,6,0),ColumnSpacing=8};
            label.ColumnDefinitions.Add(new C.ColumnDefinition{Width=new X.GridLength(18)});label.ColumnDefinitions.Add(new C.ColumnDefinition{Width=Star});
            var grip=TextBlock(IsSmartLayer(node)?"◇":node.Kind=="path"?"∿":node.Kind switch{"text"=>"T","circle"=>"○","image"=>"▧","triangle"=>"△","star"=>"☆",_=>"□"},16);grip.Opacity=.6;grip.VerticalAlignment=X.VerticalAlignment.Center;
            var title=new C.TextBlock{Text=(node.ClipMask?"↳ ":"")+name,TextTrimming=X.TextTrimming.CharacterEllipsis,MaxLines=1,VerticalAlignment=X.VerticalAlignment.Center};
            if(canvasSelectedIds.Contains(node.Id))title.Foreground=canvasToolAccent;
            row.Opacity=node.Hidden?.45:1;
            row.ContextFlyout=BuildCanvasLayerMenu(node);
            Put(label,grip);Put(label,title,0,1);
            var select=new C.ContentControl{Content=label,IsTabStop=true,HorizontalContentAlignment=X.HorizontalAlignment.Stretch,VerticalContentAlignment=X.VerticalAlignment.Stretch,MinHeight=40};
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(select,name);
            C.ToolTipService.SetToolTip(select,"点击选择；Shift 点击多选；拖动排序；右键可重命名或定位");
            select.KeyDown+=(_,e)=>
            {
                if(e.Key==Windows.System.VirtualKey.Escape&&nativeLayerDraggingRow!=null){FinishNativeLayerDrag(false);e.Handled=true;}
                else if(e.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space){SetCanvasSelection(new[]{node});canvasSurface.Invalidate();e.Handled=true;}
            };
            select.PointerPressed+=(_,e)=>
            {
                if(!e.GetCurrentPoint(select).Properties.IsLeftButtonPressed)return;
                if(nativeLayerDraggingRow!=null)return;
                nativeLayerDraggingRow=row;nativeLayerDragSurface=select;nativeLayerPointer=e.Pointer;
                nativeLayerDragStart=nativeLayerPointerPosition=e.GetCurrentPoint(nativeLayerScroll).Position;
                nativeLayerDragMoved=false;nativeLayerDragTargetIndex=-1;
                if(!select.CapturePointer(e.Pointer)){FinishNativeLayerDrag(false);return;}
                select.Focus(X.FocusState.Pointer);e.Handled=true;
            };
            select.PointerMoved+=(_,e)=>
            {
                if(nativeLayerDraggingRow!=row)return;
                if(!e.GetCurrentPoint(select).Properties.IsLeftButtonPressed){FinishNativeLayerDrag(false);return;}
                nativeLayerPointerPosition=e.GetCurrentPoint(nativeLayerScroll).Position;
                if(!nativeLayerDragMoved&&Math.Abs(nativeLayerPointerPosition.Y-nativeLayerDragStart.Y)<5)return;
                nativeLayerDragMoved=true;row.Opacity=.55;nativeLayerScrollTimer.Start();UpdateNativeLayerDrop();e.Handled=true;
            };
            select.PointerReleased+=(_,e)=>
            {
                if(nativeLayerDraggingRow!=row)return;
                nativeLayerPointerPosition=e.GetCurrentPoint(nativeLayerScroll).Position;
                if(nativeLayerDragMoved)UpdateNativeLayerDrop();
                FinishNativeLayerDrag(true);e.Handled=true;
            };
            select.PointerCanceled+=(_,_)=>{if(nativeLayerDraggingRow==row)FinishNativeLayerDrag(false);};
            select.PointerCaptureLost+=(_,_)=>{if(nativeLayerDraggingRow==row)FinishNativeLayerDrag(false);};
            var visible=NativeButton(node.Hidden?"显示图层":"隐藏图层",()=>{PushCanvasUndo();node.Hidden=!node.Hidden;SaveCanvas();InvalidateCanvasPaintCache();canvasSurface.Invalidate();});
            visible.Content=new C.FontIcon{Glyph=node.Hidden?"\uED1A":"\uE890",FontSize=14};
            var locked=NativeButton(node.Locked?"解锁图层":"锁定图层",()=>{PushCanvasUndo();node.Locked=!node.Locked;SaveCanvas();canvasSurface.Invalidate();});
            locked.Content=new C.FontIcon{Glyph=node.Locked?"\uE72E":"\uE785",FontSize=14};
            foreach(var icon in new[]{visible,locked}){icon.Background=Brush(Color.Transparent);icon.BorderThickness=new X.Thickness(0);icon.MinWidth=0;icon.Width=32;icon.Padding=new X.Thickness(0);C.ToolTipService.SetToolTip(icon,Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(icon));}
            var tagStripe=new C.Border{Width=3,Background=Brush(LayerLabelColor(node.Extra.Label)),HorizontalAlignment=X.HorizontalAlignment.Left,IsHitTestVisible=false};Put(row,select);Put(row,visible,0,1);Put(row,locked,0,2);Put(row,tagStripe);
            var line=new C.Border{Height=2,Background=nativeAccent,VerticalAlignment=X.VerticalAlignment.Top,Visibility=X.Visibility.Collapsed,IsHitTestVisible=false};
            C.Grid.SetColumnSpan(line,3);row.Children.Add(line);nativeLayerDropLines.Add(line);nativeLayersBody.Children.Add(row);
        }
        nativeLayersBody.UpdateLayout();
        nativeLayerScroll.ChangeView(null,Math.Min(scrollOffset,nativeLayerScroll.ScrollableHeight),null,true);
    }

    void UpdateNativeLayerDrop()
    {
        foreach(var line in nativeLayerDropLines)line.Visibility=X.Visibility.Collapsed;
        int count=nativeLayerDropLines.Count;
        if(count==0)return;
        var p=nativeLayerPointerPosition;
        if(p.X<0||p.X>nativeLayerScroll.ActualWidth||p.Y< -24||p.Y>nativeLayerScroll.ActualHeight+24){nativeLayerDragTargetIndex=-1;return;}
        double y=nativeLayerScroll.TransformToVisual(nativeLayersBody).TransformPoint(p).Y;
        int slot=count;
        for(int i=0;i<count;i++)
        {
            var row=(C.Grid)nativeLayersBody.Children[i+1];
            double top=row.TransformToVisual(nativeLayersBody).TransformPoint(new Windows.Foundation.Point()).Y;
            if(y<top+row.ActualHeight/2){slot=i;break;}
        }
        nativeLayerDragTargetIndex=slot;
        var indicator=nativeLayerDropLines[Math.Min(slot,count-1)];
        indicator.VerticalAlignment=slot==count?X.VerticalAlignment.Bottom:X.VerticalAlignment.Top;
        indicator.Visibility=X.Visibility.Visible;
    }

    void ScrollNativeLayerDrag()
    {
        if(nativeLayerDraggingRow==null||!nativeLayerDragMoved){nativeLayerScrollTimer.Stop();return;}
        var p=nativeLayerPointerPosition;double height=nativeLayerScroll.ActualHeight;
        if(p.X<0||p.X>nativeLayerScroll.ActualWidth||p.Y< -24||p.Y>height+24)return;
        const double edge=36;
        double delta=p.Y<edge?-Math.Min(18,(edge-p.Y)*.45):p.Y>height-edge?Math.Min(18,(p.Y-height+edge)*.45):0;
        if(delta!=0)nativeLayerScroll.ChangeView(null,Math.Clamp(nativeLayerScroll.VerticalOffset+delta,0,nativeLayerScroll.ScrollableHeight),null,true);
        UpdateNativeLayerDrop();
    }

    void FinishNativeLayerDrag(bool commit)
    {
        var row=nativeLayerDraggingRow;
        if(row==null)return;
        var surface=nativeLayerDragSurface;var pointer=nativeLayerPointer;
        bool moved=nativeLayerDragMoved;int slot=nativeLayerDragTargetIndex;
        // Release capture only after clearing state; CaptureLost can run synchronously.
        nativeLayerDraggingRow=null;nativeLayerDragSurface=null;nativeLayerPointer=null;nativeLayerDragMoved=false;nativeLayerDragTargetIndex=-1;
        nativeLayerScrollTimer.Stop();row.Opacity=1;
        foreach(var line in nativeLayerDropLines)line.Visibility=X.Visibility.Collapsed;
        if(pointer!=null)surface?.ReleasePointerCapture(pointer);
        var node=canvasNodes.FirstOrDefault(n=>n.Id==(Guid)row.Tag);
        if(commit&&node!=null&&(!moved||slot>=0))
        {
            int oldIndex=canvasNodes.IndexOf(node),oldDisplayIndex=canvasNodes.Count-1-oldIndex;
            int displayIndex=Math.Clamp(slot>oldDisplayIndex?slot-1:slot,0,Math.Max(0,canvasNodes.Count-1));
            int newIndex=canvasNodes.Count-1-displayIndex;
            if(moved&&oldIndex!=newIndex)
            {
                PushCanvasUndo();canvasNodes.RemoveAt(oldIndex);canvasNodes.Insert(newIndex,node);InvalidateCanvasPaintCache();SaveCanvas();
            }
            if(!moved && (ModifierKeys&Keys.Shift)!=0)
            {
                var chosen=CanvasSelectedNodes();if(chosen.Any(n=>n.Id==node.Id))chosen.RemoveAll(n=>n.Id==node.Id);else chosen.Add(node);SetCanvasSelection(chosen);
            }
            else SetCanvasSelection(new[]{node});canvasSurface.Invalidate();
        }
        nativeLayerKey="";
        if(nativeReady&&!IsDisposed)RefreshNativeLayers();
    }
    async void NativeMessage(string title,string message)
    {
        NativePopup(true);try{await new C.ContentDialog{Title=title,Content=TextBlock(message),CloseButtonText="知道了",XamlRoot=nativeRoot.XamlRoot,RequestedTheme=nativeRoot.RequestedTheme}.ShowAsync();}finally{NativePopup(false);}
    }
    async void NativeFactoryResetClicked()
    {
        var warning=Stack(10);
        warning.Children.Add(TextBlock("这会永久删除本机保存的所有用户内容：",14));
        warning.Children.Add(TextBlock("• 单词词库和错题本\n• 所有表格、表格词库及单元格样式\n• 所有思维画布、控件和导入图片\n• 主题、窗口偏好和视线校准等应用设置",13));
        warning.Children.Add(TextBlock("删除后软件会重启并恢复初始状态。此操作无法撤销。",13));
        var danger=Brush(Color.FromArgb(196,43,28));
        var dangerStyle=new X.Style(typeof(C.Button));
        dangerStyle.Setters.Add(new X.Setter(C.Control.BackgroundProperty,danger));
        dangerStyle.Setters.Add(new X.Setter(C.Control.ForegroundProperty,Brush(Color.White)));
        dangerStyle.Setters.Add(new X.Setter(C.Control.BorderBrushProperty,danger));
        var dialog=new C.ContentDialog
        {
            Title="确认恢复出厂设置？",Content=warning,PrimaryButtonText="删除所有数据并重启",CloseButtonText="取消",
            PrimaryButtonStyle=dangerStyle,DefaultButton=C.ContentDialogButton.Close,
            XamlRoot=nativeRoot.XamlRoot,RequestedTheme=nativeRoot.RequestedTheme
        };
        C.ContentDialogResult result;
        NativePopup(true);
        try{result=await dialog.ShowAsync();}
        finally{NativePopup(false);}
        if(result!=C.ContentDialogResult.Primary)return;
        try
        {
            var executable=Environment.ProcessPath??throw new InvalidOperationException("找不到程序启动路径。");
            var start=new System.Diagnostics.ProcessStartInfo(executable){UseShellExecute=true,WorkingDirectory=AppContext.BaseDirectory};
            start.ArgumentList.Add("--factory-reset-after-exit");
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            if(System.Diagnostics.Process.Start(start)==null)throw new InvalidOperationException("无法启动数据清理程序。");
            exitRequested=true;Close();
        }
        catch(Exception ex){NativeMessage("恢复出厂设置失败",$"未删除数据，应用仍保持打开。\n{ex.Message}");}
    }
    async void NativePrompt(string title,string initial,Action<string> accept)
    {
        var input=new C.TextBox{Text=initial,MinWidth=200};var dialog=new C.ContentDialog{Title=title,Content=input,PrimaryButtonText="保存",CloseButtonText="取消",DefaultButton=C.ContentDialogButton.Primary,XamlRoot=nativeRoot.XamlRoot,RequestedTheme=nativeRoot.RequestedTheme};
        NativePopup(true);try{if(await dialog.ShowAsync()==C.ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text))accept(input.Text.Trim());}finally{NativePopup(false);NativeSync();}
    }
    async void NativePickColor(string title,Color initial,Action<Color> accept)
    {
        var picker=new C.ColorPicker{Color=WinColor(initial),IsAlphaEnabled=false,IsMoreButtonVisible=true,IsColorSliderVisible=true,IsHexInputVisible=true,MaxWidth=360};
        var dialog=new C.ContentDialog{Title=title,Content=Scroll(picker),PrimaryButtonText="确定",CloseButtonText="取消",DefaultButton=C.ContentDialogButton.Primary,XamlRoot=nativeRoot.XamlRoot,RequestedTheme=nativeRoot.RequestedTheme};
        NativePopup(true);try{if(await dialog.ShowAsync()==C.ContentDialogResult.Primary)accept(Color.FromArgb(picker.Color.R,picker.Color.G,picker.Color.B));}finally{NativePopup(false);NativeSync();}
    }
    async void NativeEditWord(int index)
    {
        if(index>=savedCards.Count)return;var old=savedCards[index];var body=Stack();var en=new C.TextBox{Header="英语单词",Text=old.Word};var cn=new C.TextBox{Header="中文翻译",Text=old.Meaning};var note=new C.TextBox{Header="备注",Text=old.Note};body.Children.Add(en);body.Children.Add(cn);body.Children.Add(note);
        var dialog=new C.ContentDialog{Title="编辑单词",Content=body,PrimaryButtonText="保存",CloseButtonText="取消",XamlRoot=nativeRoot.XamlRoot,RequestedTheme=nativeRoot.RequestedTheme};NativePopup(true);try{if(await dialog.ShowAsync()==C.ContentDialogResult.Primary&&en.Text.Trim().Length>0&&cn.Text.Trim().Length>0){var next=(en.Text.Trim(),old.Type,cn.Text.Trim(),old.Tag,note.Text,old.Difficulty);savedCards[index]=next;for(int i=0;i<reviewSource.Count;i++)if(reviewSource[i]==old)reviewSource[i]=next;for(int i=0;i<cards.Count;i++)if(cards[i]==old)cards[i]=next;SaveWordBank();RenderCard();}}finally{NativePopup(false);NativeSync();}
    }
}