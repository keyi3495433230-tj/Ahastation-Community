using X = Microsoft.UI.Xaml;
using C = Microsoft.UI.Xaml.Controls;
using M = Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace CardReview;

public sealed partial class ReviewForm
{
    C.TextBlock canvasWorkspaceStatus = null!, canvasWorkspaceSelection = null!;
    C.Button canvasZoomButton = null!, canvasUndoButton = null!, canvasRedoButton = null!;
    C.Grid canvasWorkspaceHeader = null!;
    readonly Dictionary<CanvasTool, C.Button> canvasToolButtons = new();
    readonly M.SolidColorBrush canvasToolAccent = Brush(Color.FromArgb(13, 153, 255));
    ChatHistoryDivider canvasLayersDivider = null!, canvasPropertiesDivider = null!;

    C.Grid BuildCanvasWorkspace()
    {
        LoadCanvasWorkspace();
        var page = GridRows(Auto, Auto, Star, Auto); page.RowSpacing = 0;
        canvasWorkspaceHeader = new C.Grid { Padding = new X.Thickness(12, 8, 12, 8), ColumnSpacing = 8, Background = nativeSurface };
        canvasWorkspaceHeader.ColumnDefinitions.Add(new C.ColumnDefinition { Width = Star });
        canvasWorkspaceHeader.ColumnDefinitions.Add(new C.ColumnDefinition { Width = Auto });
        var name = Stack(2); var title = TextBlock("梳理画布", 15); title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; name.Children.Add(title);
        canvasWorkspaceStatus = TextBlock("自动保存到本地", 11); canvasWorkspaceStatus.Opacity = .6; name.Children.Add(canvasWorkspaceStatus); Put(canvasWorkspaceHeader, name);
        var actions = new C.StackPanel { Orientation = C.Orientation.Horizontal, Spacing = 4 };
        canvasUndoButton = CanvasIconButton("撤销 · Ctrl+Z", "\uE7A7", UndoCanvas);
        canvasRedoButton = CanvasIconButton("重做 · Ctrl+Shift+Z", "\uE7A6", RedoCanvas);
        actions.Children.Add(canvasUndoButton); actions.Children.Add(canvasRedoButton);
        nativeLayersToggle = CanvasIconButton("图层面板", "\uE81E", () => OpenNativeInspector(true));
        nativePropsToggle = CanvasIconButton("属性面板", "\uE713", () => OpenNativeInspector(false));
        actions.Children.Add(nativeLayersToggle); actions.Children.Add(nativePropsToggle);
        var more = CanvasIconButton("画布菜单", "\uE712", () => { });
        var menu = CanvasMenu(
            ("新建画布", () => NativePrompt("新建画布", "", CreateNativeBoard)),
            ("重命名画布", () => NativePrompt("重命名画布", canvasBoards.FirstOrDefault(b => b.Id == activeCanvasId).Name ?? "", RenameNativeBoard)),
            ("复制画布", DuplicateCanvasBoard), ("导出 PNG", ExportCanvasPng), ("导出选中控件", ExportSelectedCanvasControls),
            ("快捷键", ShowCanvasWorkspaceHelp), ("删除画布", DeleteActiveCanvasBoard));
        more.Flyout = menu; actions.Children.Add(more); Put(canvasWorkspaceHeader, actions, 0, 1); Put(page, canvasWorkspaceHeader);

        var tools = new C.StackPanel { Orientation = C.Orientation.Horizontal, Spacing = 4, Padding = new X.Thickness(12, 6, 12, 6), Background = nativeSurface };
        foreach (var (tool, label, glyph) in new[] {
            (CanvasTool.Select,"选择 · V","\uE8B0"), (CanvasTool.Hand,"抓手 · H","\uE7C9"),
            (CanvasTool.Rectangle,"矩形 · R","\uE739"), (CanvasTool.Ellipse,"椭圆 · O","\uEA3A"),
            (CanvasTool.Text,"文字 · T","\uE8D2"), (CanvasTool.Connect,"连接 · L","\uE71B"),
            (CanvasTool.Brush,"普通画笔 · B","\uE70F"),(CanvasTool.PixelBrush,"像素笔刷 · N","\uE80A"),(CanvasTool.VectorBrush,"矢量画笔 · Shift+B","\uED63"),(CanvasTool.Pen,"钢笔 · P","\uEDFB"),(CanvasTool.Eraser,"橡皮擦 · E","\uE75C") })
        {
            var button = CanvasIconButton(label, glyph, () => SetCanvasTool(tool));
            if(tool==CanvasTool.Text)button.Content=new C.TextBlock{Text="T",FontSize=20,FontFamily=new M.FontFamily("Segoe UI")};
            canvasToolButtons.Add(tool, button); tools.Children.Add(button);
        }
        tools.Children.Add(new C.Border { Width = 1, Margin = new X.Thickness(4, 5, 4, 5), Background = nativeStroke });
        tools.Children.Add(BuildCanvasPaintSettings());
        tools.Children.Add(CanvasIconButton("复制与重复排列", "\uE8A4", ShowCanvasRepeatDialog));
        var insert = CanvasIconButton("插入更多图形或图片", "\uE710", () => { }); insert.Flyout = BuildNativeCreateMenu(); tools.Children.Add(insert);
        var toolbarScroll = new C.ScrollViewer { Content = tools, HorizontalScrollBarVisibility = C.ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = C.ScrollBarVisibility.Disabled, HorizontalScrollMode = C.ScrollMode.Enabled };
        Put(page, toolbarScroll, 1);

        nativeCanvasLayout = new C.Grid();
        foreach (double width in new[] { canvasWorkspace.LayersWidth, 5d, -1d, 5d, canvasWorkspace.PropertiesWidth })
            nativeCanvasLayout.ColumnDefinitions.Add(new C.ColumnDefinition { Width = width < 0 ? Star : new X.GridLength(width) });
        var layers = GridRows(Auto, Star); layers.RowSpacing = 0;
        nativeBoards = new C.ComboBox { HorizontalAlignment = X.HorizontalAlignment.Stretch, Margin = new X.Thickness(12), PlaceholderText = "选择画布", MinWidth = 0 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(nativeBoards, "画布文件");
        nativeBoards.SelectionChanged += (_, _) => { if (!nativeSyncing && nativeBoards.SelectedItem is C.ComboBoxItem item) { SetCanvasTool(CanvasTool.Select); SwitchCanvasBoard((Guid)item.Tag); } };
        nativeBoards.DropDownOpened += (_, _) => NativePopup(true); nativeBoards.DropDownClosed += (_, _) => NativePopup(false); Put(layers, nativeBoards);
        nativeLayersBody = Stack(2); nativeLayersBody.Padding = new X.Thickness(6, 0, 6, 12); nativeLayerScroll = Scroll(nativeLayersBody); Put(layers, nativeLayerScroll, 1);
        nativeLayers = new C.Border { Child = layers, Background = nativeSurface, BorderBrush = nativeStroke, BorderThickness = new X.Thickness(0, 1, 0, 0) }; Put(nativeCanvasLayout, nativeLayers);
        nativeCanvasSlot = new C.Border { Background = nativeBackground }; Put(nativeCanvasLayout, nativeCanvasSlot, 0, 2);
        nativePropsBody = BuildNativeProperties();
        OrganizeCanvasProperties(nativePropsBody);
        BuildCanvasLayerControls(nativePropsBody);
        nativeProps = new C.Border { Child = Scroll(nativePropsBody), Padding = new X.Thickness(14, 14, 14, 10), Background = nativeSurface, BorderBrush = nativeStroke, BorderThickness = new X.Thickness(0, 1, 0, 0) }; Put(nativeCanvasLayout, nativeProps, 0, 4);
        canvasLayersDivider = BuildCanvasDivider(true); canvasPropertiesDivider = BuildCanvasDivider(false);
        Put(nativeCanvasLayout, canvasLayersDivider, 0, 1); Put(nativeCanvasLayout, canvasPropertiesDivider, 0, 3);
        nativeLayerScrollTimer.Tick += (_, _) => ScrollNativeLayerDrag();
        Deactivate += (_, _) => { FinishNativeLayerDrag(false); canvasSpaceDown = false; if (canvasDrawing || canvasResizeNode != null) CancelCanvasWorkspaceGesture(); UpdateCanvasCursor(); };
        FormClosing += (_, _) => { FinishNativeLayerDrag(false); SaveCanvasWorkspace(); nativeLayerScrollTimer.Dispose(); DisposeLayerRenderCache(); };
        Put(page, nativeCanvasLayout, 2);

        var footer = new C.Grid { Padding = new X.Thickness(12, 6, 12, 6), Background = nativeSurface, ColumnSpacing = 8 };
        footer.ColumnDefinitions.Add(new C.ColumnDefinition { Width = Star }); footer.ColumnDefinitions.Add(new C.ColumnDefinition { Width = Auto });
        canvasWorkspaceSelection = TextBlock("空格拖动 · Alt / Ctrl + 滚轮缩放", 11); canvasWorkspaceSelection.Opacity = .65; canvasWorkspaceSelection.VerticalAlignment = X.VerticalAlignment.Center; canvasWorkspaceSelection.MaxLines = 1; canvasWorkspaceSelection.TextTrimming = X.TextTrimming.CharacterEllipsis; Put(footer, canvasWorkspaceSelection);
        var zoom = new C.StackPanel { Orientation = C.Orientation.Horizontal, Spacing = 2 };
        zoom.Children.Add(CanvasIconButton("缩小", "\uE738", () => ZoomCanvasAt(canvasZoom / 1.2f, CanvasViewportCenter)));
        canvasZoomButton = NativeButton("100%", () => { }); canvasZoomButton.Width = 74; canvasZoomButton.MinWidth = 0; canvasZoomButton.Background = Brush(Color.Transparent); canvasZoomButton.BorderThickness = new X.Thickness(0);
        var view = CanvasMenu(("缩放到全部 · Shift+1", () => FitCanvasView(false)), ("缩放到选中 · Shift+2", () => FitCanvasView(true)), ("实际大小 · Shift+0", () => ZoomCanvasAt(1, CanvasViewportCenter)));
        view.Items.Add(new C.MenuFlyoutSeparator());
        void ViewOption(string label, Func<bool> get, Action<bool> set)
        {
            var option = new C.ToggleMenuFlyoutItem { Text = label, IsChecked = get() };
            option.Click += (_, _) => { set(option.IsChecked); SaveCanvasWorkspace(); canvasSurface.Invalidate(); };
            view.Items.Add(option);
        }
        ViewOption("显示网格", () => canvasWorkspace.Grid, v => canvasWorkspace.Grid = v);
        ViewOption("智能对齐参考线", () => canvasWorkspace.SmartSnap, v => canvasWorkspace.SmartSnap = v);
        ViewOption("吸附到网格", () => canvasWorkspace.GridSnap, v => canvasWorkspace.GridSnap = v);
        canvasZoomButton.Flyout = view; zoom.Children.Add(canvasZoomButton);
        zoom.Children.Add(CanvasIconButton("放大", "\uE710", () => ZoomCanvasAt(canvasZoom * 1.2f, CanvasViewportCenter)));
        zoom.Children.Add(CanvasIconButton("缩放到全部", "\uE9A6", () => FitCanvasView(false)));
        Put(footer, zoom, 0, 1); Put(page, footer, 3);
        return page;
    }

    C.Button CanvasIconButton(string label, string glyph, Action action)
    {
        var button = NativeButton(label, action); button.Content = new C.FontIcon { Glyph = glyph, FontSize = 17 };
        button.Width = button.Height = 34; button.MinWidth = 0; button.Padding = new X.Thickness(0); button.CornerRadius = new X.CornerRadius(6);
        button.BorderThickness = new X.Thickness(0); button.Background = Brush(Color.Transparent); C.ToolTipService.SetToolTip(button, label); return button;
    }

    C.MenuFlyout CanvasMenu(params (string label, Action action)[] items)
    {
        var menu = new C.MenuFlyout();
        foreach (var (label, action) in items) { var item = new C.MenuFlyoutItem { Text = label }; item.Click += (_, _) => { action(); NativeSync(); }; menu.Items.Add(item); }
        menu.Opened += (_, _) => NativePopup(true); menu.Closed += (_, _) => NativePopup(false); return menu;
    }

    void LayoutCanvasWorkspace(double width)
    {
        bool layers = nativeLayersOpen && !canvasFocusMode, properties = nativePropertiesOpen && !canvasFocusMode;
        if (width < 850 && layers && properties) { if (canvasPreferProperties) layers = false; else properties = false; }
        if (width < 550) { layers = false; properties = false; }
        double left = layers ? canvasWorkspace.LayersWidth : 0;
        double right = properties ? canvasWorkspace.PropertiesWidth : 0;
        double budget=Math.Max(240,width-310);
        if(left+right>budget && layers)left=Math.Max(170,Math.Min(left,budget-right));
        if(left+right>budget && properties)right=Math.Max(240,Math.Min(right,budget-left));
        nativeCanvasLayout.ColumnDefinitions[0].Width = new X.GridLength(left);
        nativeCanvasLayout.ColumnDefinitions[1].Width = new X.GridLength(layers ? 5 : 0);
        nativeCanvasLayout.ColumnDefinitions[3].Width = new X.GridLength(properties ? 5 : 0);
        nativeCanvasLayout.ColumnDefinitions[4].Width = new X.GridLength(right);
        nativeLayers.Visibility = canvasLayersDivider.Visibility = layers ? X.Visibility.Visible : X.Visibility.Collapsed;
        nativeProps.Visibility = canvasPropertiesDivider.Visibility = properties ? X.Visibility.Visible : X.Visibility.Collapsed;
        nativeLayersToggle.Background = layers ? nativePanel : Brush(Color.Transparent); nativePropsToggle.Background = properties ? nativePanel : Brush(Color.Transparent);
        SyncCanvasWorkspaceUi();
    }

    ChatHistoryDivider BuildCanvasDivider(bool left)
    {
        var divider = new ChatHistoryDivider { HorizontalContentAlignment = X.HorizontalAlignment.Stretch, VerticalContentAlignment = X.VerticalAlignment.Stretch };
        var line = new C.Border { Width = 1, HorizontalAlignment = X.HorizontalAlignment.Center, Background = nativeStroke };
        divider.Content = new C.Grid { Background = Brush(Color.Transparent), Children = { line } };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(divider, left ? "调整图层面板宽度" : "调整属性面板宽度");
        bool dragging = false; double startX = 0, startWidth = 0;
        divider.PointerEntered += (_, _) => line.Background = canvasToolAccent;
        divider.PointerExited += (_, _) => { if (!dragging) line.Background = nativeStroke; };
        divider.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(divider).Properties.IsLeftButtonPressed) return;
            startX = e.GetCurrentPoint(nativeCanvasLayout).Position.X; startWidth = left ? nativeCanvasLayout.ColumnDefinitions[0].Width.Value : nativeCanvasLayout.ColumnDefinitions[4].Width.Value;
            dragging = divider.CapturePointer(e.Pointer); e.Handled = true;
        };
        divider.PointerMoved += (_, e) =>
        {
            if (!dragging) return;
            double delta = e.GetCurrentPoint(nativeCanvasLayout).Position.X - startX;
            if (left) canvasWorkspace.LayersWidth = Math.Clamp(startWidth + delta, 170, 340);
            else canvasWorkspace.PropertiesWidth = Math.Clamp(startWidth - delta, 240, 380);
            NativeResponsiveLayout(); e.Handled = true;
        };
        divider.PointerReleased += (_, e) => { if (!dragging) return; dragging = false; divider.ReleasePointerCapture(e.Pointer); line.Background = nativeStroke; SaveCanvasWorkspace(); e.Handled = true; };
        divider.PointerCaptureLost += (_, _) => { if (dragging) SaveCanvasWorkspace(); dragging = false; line.Background = nativeStroke; };
        return divider;
    }

    void SyncCanvasWorkspaceUi()
    {
        if (canvasZoomButton == null) return;
        canvasZoomButton.Content = $"{canvasZoom * 100:0}%";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(canvasZoomButton, $"缩放 {canvasZoom * 100:0}%");
        canvasUndoButton.IsEnabled = canvasUndo.Count > 0; canvasRedoButton.IsEnabled = canvasRedo.Count > 0;
        foreach (var (tool, button) in canvasToolButtons)
        {
            button.Background = tool == canvasTool ? canvasToolAccent : Brush(Color.Transparent);
            if (tool == canvasTool) button.Foreground = Brush(Color.White); else button.ClearValue(C.Control.ForegroundProperty);
        }
        string toolName = canvasTool switch { CanvasTool.Hand => "抓手 · 拖动画布", CanvasTool.Rectangle => "拖动绘制矩形 · Shift 保持正方形", CanvasTool.Ellipse => "拖动绘制椭圆 · Shift 保持正圆", CanvasTool.Text => "点击或拖动添加文字", CanvasTool.Connect => "从一个对象拖到另一个对象进行连接", _ => "空格拖动 · Alt / Ctrl + 滚轮缩放" };
        if(IsCanvasPaintTool)toolName=canvasTool switch{CanvasTool.Pen=>"钢笔：点击加点，拖动出曲线，Enter 结束，点起点闭合",CanvasTool.Eraser=>"橡皮擦：擦除选中的像素图层",CanvasTool.PixelBrush=>$"像素笔刷 · {pixelBrushModes[canvasPixelBrushMode]}"+(canvasPixelBrushMode>=3?$" · 密度 {canvasPixelDensity:0}%":"")+" · 调色盘切换模式",CanvasTool.VectorBrush=>"矢量画笔：拖动绘制，双击路径编辑锚点",_=>"普通画笔：像素图层直接绘画，其他类型会新建画笔图层"};
        canvasWorkspaceSelection.Text = canvasSelectedIds.Count > 0 && canvasTool == CanvasTool.Select ? $"已选 {canvasSelectedIds.Count} 个对象" + (canvasSelectedIds.Count == 1 && selectedCanvasNode != null ? $"   ·   {selectedCanvasNode.Size.Width} × {selectedCanvasNode.Size.Height}" : "   ·   Shift 点击多选") : toolName;
        canvasWorkspaceStatus.Text = $"{canvasNodes.Count} 个对象   ·   自动保存到本地";
        if(IsCanvasPaintTool)canvasWorkspaceSelection.Text=$"笔刷 {canvasBrushSize:0} px   ·   按住 [ 缩小 / ] 放大   ·   "+toolName;
        if(canvasPathEditId.HasValue)canvasWorkspaceSelection.Text="锚点编辑 · 拖动方点改变形状，圆柄调整曲率 · Ctrl 点去掉单侧控制柄 · Esc 完成 · P 画新路径";
    }

    void OrganizeCanvasProperties(C.StackPanel body)
    {
        // Keep the existing property bindings and undo behavior; only regroup their presentation.
        var controls = body.Children.OfType<X.FrameworkElement>().Skip(2).ToList();
        var status = body.Children[1]; body.Children.Clear();
        var heading = TextBlock("设计", 16); heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; body.Children.Add(heading); body.Children.Add(status);
        var empty = Stack(10); var hint = TextBlock("选择对象以调整外观", 14); empty.Children.Add(hint);
        var detail = TextBlock("位置、尺寸、填充和文字样式都会显示在这里。\n\nShift 点击可多选；拖动空白区域可框选。", 12); detail.Opacity = .6; empty.Children.Add(detail);
        empty.Children.Add(NativeButton("查看画布快捷键", ShowCanvasWorkspaceHelp)); body.Children.Add(empty);
        nativePropertySync.Add(() => empty.Visibility = canvasSelectedIds.Count == 0 && selectedCanvasLink == null ? X.Visibility.Visible : X.Visibility.Collapsed);
        var buckets = new Dictionary<string, List<X.FrameworkElement>>();
        foreach (var element in controls)
        {
            string label = element switch { C.NumberBox n => n.Header?.ToString() ?? "", C.TextBox t => t.Header?.ToString() ?? "", C.ComboBox c => c.Header?.ToString() ?? "", C.ContentControl c => c.Content?.ToString() ?? "", _ => "" };
            string group = label switch
            {
                "填充颜色" or "恢复默认填充" or "圆角" => "填充",
                "描边宽度" or "描边颜色" or "恢复默认描边" or "描边位置" or "虚线" => "描边",
                "文字内容" or "字号" or "加粗" or "斜体" or "删除线" or "水平对齐" or "垂直对齐" or "文字颜色" or "恢复默认文字颜色" => "文字",
                "连线文字" or "连线箭头" or "连线颜色" or "恢复默认连线颜色" => "连接线",
                "多选控件对齐" => "对齐与布局", _ => "位置与尺寸"
            };
            if (!buckets.TryGetValue(group, out var bucket)) buckets[group] = bucket = new(); bucket.Add(element);
        }
        foreach (var (name, elements) in buckets)
        {
            var content = Stack(10); foreach (var element in elements) content.Children.Add(element);
            var section = new C.Expander { Header = name, Content = content, IsExpanded = true, HorizontalAlignment = X.HorizontalAlignment.Stretch, HorizontalContentAlignment = X.HorizontalAlignment.Stretch, Padding = new X.Thickness(0), BorderThickness=new X.Thickness(0), Background=Brush(Color.Transparent) };
            body.Children.Add(section); nativePropertySync.Add(() => section.Visibility = elements.Any(e => e.Visibility == X.Visibility.Visible) ? X.Visibility.Visible : X.Visibility.Collapsed);
        }
        var multi = Stack(8); var commands = new C.Grid { ColumnSpacing = 6 };
        commands.ColumnDefinitions.Add(new C.ColumnDefinition { Width = Star }); commands.ColumnDefinitions.Add(new C.ColumnDefinition { Width = Star });
        Put(commands, NativeButton("编组", () => GroupCanvasWorkspace(false))); Put(commands, NativeButton("取消编组", () => GroupCanvasWorkspace(true)), 0, 1); multi.Children.Add(commands);
        multi.Children.Add(NativeButton("水平自动布局", () => SetCanvasAutoLayout(CanvasSelectedNodes().Where(n => !n.Locked).ToList(), 0)));
        multi.Children.Add(NativeButton("垂直自动布局", () => SetCanvasAutoLayout(CanvasSelectedNodes().Where(n => !n.Locked).ToList(), 1)));
        multi.Children.Add(NativeButton("调整布局间距", () => PromptCanvasAutoLayoutSpacing(CanvasSelectedNodes(), false)));
        body.Children.Add(multi); nativePropertySync.Add(() => multi.Visibility = canvasSelectedIds.Count >= 2 ? X.Visibility.Visible : X.Visibility.Collapsed);
    }

    void RenameCanvasLayer(CanvasNode node) => NativePrompt("重命名图层", node.Name, name => { if (!canvasNodes.Contains(node) || node.Name == name) return; PushCanvasUndo(); node.Name = name; nativeLayerKey = ""; SaveCanvas(); });
    void ShowCanvasWorkspaceHelp() => NativeMessage("画布快捷键", "V 选择    H 抓手    R 矩形    O 椭圆    T 文字    L 连接\nB 普通画笔    N 像素笔刷    Shift + B 矢量画笔    P 钢笔    E 橡皮擦\n[ / ]：调整画笔大小；调色盘：颜色、不透明度与硬度\n钢笔：点击加点，拖动出曲线，Enter 结束，点起点闭合\nCtrl + 点击控制柄：移除单侧；Ctrl + 点击锚点：移除向外控制柄\n\n空格 + 拖动 / 鼠标中键：平移\n滚轮：上下平移；Shift + 滚轮：左右平移\nAlt / Ctrl + 滚轮：以指针为中心缩放\nShift + 1：显示全部；Shift + 2：定位选中；Shift + 0：100%\n\nShift + 点击：多选；拖动空白区域：框选\n拖动边框手柄：调整大小；Shift + 拖动角点：锁定比例\n顶部圆点：旋转；Alt + 拖动：复制\nCtrl + G：编组；Ctrl + Shift + G：取消编组\nShift + A：水平自动布局；Ctrl + D：再次变换；Alt + 拖动：复制后可连续再次变换\nCtrl + C / V：复制 / 粘贴；Ctrl + F / B：原位粘贴到前面 / 后面\n顶部重复排列按钮：连续复制、网格、径向圆环\nCtrl + E：合并图层；Ctrl + Alt + G：创建 / 释放剪贴蒙版\n右键图层：智能图层、栅格化、合并与颜色标签\nCtrl + Z：撤销；Ctrl + Shift + Z：重做\nF2：重命名图层；Tab：隐藏 / 显示两侧面板\nEsc：取消绘制 / 回到选择工具");
}
