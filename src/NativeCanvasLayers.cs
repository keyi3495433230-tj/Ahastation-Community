using X=Microsoft.UI.Xaml;
using C=Microsoft.UI.Xaml.Controls;
using M=Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace CardReview;

public sealed partial class ReviewForm
{
    C.NumberBox? nativeBrushSize,nativeBrushOpacity,nativeBrushHardness;
    C.Button? nativeBrushColor;
    bool canvasPaintUiSync;
    C.ComboBox? nativePixelBrushMode;
    C.NumberBox? nativePixelDensity;
    C.TextBlock? nativePixelBrushHint;

    void BuildCanvasLayerControls(C.StackPanel body)
    {
        var layer=Stack(8);
        var blend=new C.ComboBox{Header="混合模式",HorizontalAlignment=X.HorizontalAlignment.Stretch};foreach(var mode in layerBlendModes)blend.Items.Add(mode.Name);
        blend.SelectionChanged+=(_,_)=>{if(!nativeSyncing&&blend.SelectedIndex>=0)ApplyLayerDataChange(n=>n.Extra.Blend=layerBlendModes[blend.SelectedIndex].Id);};
        blend.DropDownOpened+=(_,_)=>NativePopup(true);blend.DropDownClosed+=(_,_)=>NativePopup(false);layer.Children.Add(blend);
        nativePropertySync.Add(()=>{var selected=CanvasSelectedNodes();var value=selected.FirstOrDefault()?.Extra.Blend??"normal";int at=Array.FindIndex(layerBlendModes,m=>m.Id==value);blend.SelectedIndex=selected.All(n=>n.Extra.Blend==value)?Math.Max(0,at):-1;blend.IsEnabled=selected.Any(n=>!n.Locked);});
        var clip=new C.CheckBox{Content="剪贴到下方基础图层",IsThreeState=true};clip.Click+=(_,_)=>{if(nativeSyncing)return;bool enabled=clip.IsChecked==true;ApplyLayerDataChange(n=>{if(!enabled||canvasNodes.IndexOf(n)>0)n.ClipMask=enabled;});};layer.Children.Add(clip);
        nativePropertySync.Add(()=>{var selected=CanvasSelectedNodes();clip.IsChecked=selected.All(n=>n.ClipMask)?true:selected.All(n=>!n.ClipMask)?false:null;clip.IsEnabled=selected.Any(n=>!n.Locked&&canvasNodes.IndexOf(n)>0);});
        var label=new C.ComboBox{Header="图层颜色标签",HorizontalAlignment=X.HorizontalAlignment.Stretch};foreach(var name in new[]{"无标签","红色","橙色","黄色","绿色"})label.Items.Add(name);
        label.SelectionChanged+=(_,_)=>{if(!nativeSyncing&&label.SelectedIndex>=0)ApplyLayerDataChange(n=>n.Extra.Label=label.SelectedIndex,true);};label.DropDownOpened+=(_,_)=>NativePopup(true);label.DropDownClosed+=(_,_)=>NativePopup(false);layer.Children.Add(label);
        nativePropertySync.Add(()=>{var nodes=CanvasSelectedNodes();int value=nodes.FirstOrDefault()?.Extra.Label??0;label.SelectedIndex=nodes.All(n=>n.Extra.Label==value)?value:-1;});
        var section=new C.Expander{Header="图层",Content=layer,IsExpanded=true,HorizontalAlignment=X.HorizontalAlignment.Stretch,HorizontalContentAlignment=X.HorizontalAlignment.Stretch};body.Children.Insert(Math.Min(2,body.Children.Count),section);
        nativePropertySync.Add(()=>section.Visibility=canvasSelectedIds.Count>0?X.Visibility.Visible:X.Visibility.Collapsed);

        var typography=Stack(8);var fonts=new C.ComboBox{Header="字体",IsEditable=true,IsTextSearchEnabled=true,HorizontalAlignment=X.HorizontalAlignment.Stretch,MaxDropDownHeight=320,PlaceholderText="搜索已安装的字体"};
        var names=System.Drawing.FontFamily.Families.Select(f=>{string name=f.Name;f.Dispose();return name;}).Distinct().OrderBy(n=>n,StringComparer.CurrentCultureIgnoreCase).ToArray();foreach(var font in names)fonts.Items.Add(font);
        void ApplyFont(string name){if(nativeSyncing||!names.Contains(name,StringComparer.CurrentCultureIgnoreCase))return;var text=CanvasStyleNodes().Where(n=>!n.Locked).ToList();if(text.Count==0||text.All(n=>n.Extra.FontFamily==name))return;PushCanvasUndo();foreach(var node in text)node.Extra.FontFamily=name;SaveCanvas();canvasSurface.Invalidate();}
        fonts.SelectionChanged+=(_,_)=>{if(fonts.SelectedItem is string name)ApplyFont(name);};fonts.TextSubmitted+=(_,e)=>ApplyFont(e.Text);
        fonts.DropDownOpened+=(_,_)=>NativePopup(true);fonts.DropDownClosed+=(_,_)=>NativePopup(false);typography.Children.Add(fonts);
        nativePropertySync.Add(()=>{if(fonts.FocusState!=X.FocusState.Unfocused)return;var text=CanvasStyleNodes();string value=text.FirstOrDefault()?.Extra.FontFamily??"";if(value=="")value=UiFontName();if(fonts.Text!=value)fonts.Text=value;});
        var textSection=new C.Expander{Header="字体选择",Content=typography,IsExpanded=true,HorizontalAlignment=X.HorizontalAlignment.Stretch,HorizontalContentAlignment=X.HorizontalAlignment.Stretch};body.Children.Insert(Math.Min(3,body.Children.Count),textSection);nativePropertySync.Add(()=>textSection.Visibility=CanvasStyleNodes().Count>0?X.Visibility.Visible:X.Visibility.Collapsed);

        var colors=Stack(8);
        void Adjust(string name,double min,double max,Func<LayerData,float> get,Action<LayerData,float> set)
        {
            var input=new C.NumberBox{Header=name,Minimum=min,Maximum=max,SmallChange=5,LargeChange=10,SpinButtonPlacementMode=C.NumberBoxSpinButtonPlacementMode.Compact,FontSize=13};
            input.ValueChanged+=(_,_)=>{if(!nativeSyncing&&double.IsFinite(input.Value))ApplyLayerDataChange(n=>set(n.Extra,(float)input.Value));};
            nativePropertySync.Add(()=>{var nodes=CanvasSelectedNodes();input.IsEnabled=nodes.Any(n=>!n.Locked);if(input.FocusState==X.FocusState.Unfocused){float value=nodes.Count==0?0:get(nodes[0].Extra);input.Value=nodes.All(n=>get(n.Extra)==value)?value:double.NaN;}});colors.Children.Add(input);
        }
        Adjust("亮度",-100,100,e=>e.Brightness,(e,v)=>e.Brightness=v);Adjust("对比度",-100,100,e=>e.Contrast,(e,v)=>e.Contrast=v);
        Adjust("色相",-180,180,e=>e.Hue,(e,v)=>e.Hue=v);Adjust("饱和度",-100,100,e=>e.Saturation,(e,v)=>e.Saturation=v);
        Adjust("红色平衡",-100,100,e=>e.Red,(e,v)=>e.Red=v);Adjust("绿色平衡",-100,100,e=>e.Green,(e,v)=>e.Green=v);Adjust("蓝色平衡",-100,100,e=>e.Blue,(e,v)=>e.Blue=v);
        colors.Children.Add(NativeButton("重置颜色调整",()=>ApplyLayerDataChange(n=>{n.Extra.Brightness=n.Extra.Contrast=n.Extra.Hue=n.Extra.Saturation=n.Extra.Red=n.Extra.Green=n.Extra.Blue=0;})));
        colors.Children.Add(TextBlock("调整保留原始内容，可随时重置。",11));
        var colorSection=new C.Expander{Header="颜色调整",Content=colors,IsExpanded=false,HorizontalAlignment=X.HorizontalAlignment.Stretch,HorizontalContentAlignment=X.HorizontalAlignment.Stretch};body.Children.Add(colorSection);nativePropertySync.Add(()=>colorSection.Visibility=canvasSelectedIds.Count>0?X.Visibility.Visible:X.Visibility.Collapsed);

        var path=Stack(8);var fill=new C.CheckBox{Content="填充闭合路径"};fill.Click+=(_,_)=>{if(!nativeSyncing)ApplyLayerDataChange(n=>{if(n.Kind=="path")n.Extra.FillPath=fill.IsChecked==true;});};path.Children.Add(fill);
        nativePropertySync.Add(()=>fill.IsChecked=selectedCanvasNode?.Extra.FillPath==true);
        var width=new C.NumberBox{Header="路径线宽",Minimum=.2,Maximum=300,SmallChange=1,SpinButtonPlacementMode=C.NumberBoxSpinButtonPlacementMode.Compact};width.ValueChanged+=(_,_)=>{if(!nativeSyncing&&double.IsFinite(width.Value))ApplyLayerDataChange(n=>{if(n.Kind=="path")n.StrokeWidth=(float)width.Value;});};path.Children.Add(width);
        nativePropertySync.Add(()=>{if(width.FocusState==X.FocusState.Unfocused)width.Value=selectedCanvasNode?.StrokeWidth??1;});
        path.Children.Add(NativeButton("路径描边颜色",()=>NativePickColor("路径描边颜色",selectedCanvasNode==null?canvasBrushColor:CanvasNodeStrokeColor(selectedCanvasNode),c=>ApplyLayerDataChange(n=>{if(n.Kind=="path")n.StrokeColor=c.ToArgb().ToString();}))));
        path.Children.Add(NativeButton("路径填充颜色",()=>NativePickColor("路径填充颜色",selectedCanvasNode==null?canvasBrushColor:CanvasNodeFillColor(selectedCanvasNode),c=>ApplyLayerDataChange(n=>{if(n.Kind=="path"){n.FillColor=c.ToArgb().ToString();n.Extra.FillPath=true;}}))));
        path.Children.Add(NativeButton("编辑路径锚点",()=>{if(selectedCanvasNode!=null)EditCanvasPath(selectedCanvasNode);}));
        path.Children.Add(TextBlock("双击路径也可编辑锚点。拖动圆形控制柄调整曲线，Alt 可单独调整一侧；Ctrl 点控制柄移除单侧，Ctrl 点锚点移除向外一侧。Esc 完成编辑。",11));
        var pathSection=new C.Expander{Header="矢量路径",Content=path,IsExpanded=true,HorizontalAlignment=X.HorizontalAlignment.Stretch,HorizontalContentAlignment=X.HorizontalAlignment.Stretch};body.Children.Insert(Math.Min(3,body.Children.Count),pathSection);nativePropertySync.Add(()=>pathSection.Visibility=selectedCanvasNode?.Kind=="path"?X.Visibility.Visible:X.Visibility.Collapsed);
        var smart=Stack(8);smart.Children.Add(TextBlock("智能图层保留原始文字、矢量与图片；缩放时从源内容重新绘制。还原后可继续编辑源图层。",12));smart.Children.Add(NativeButton("还原智能图层源内容",()=>{if(selectedCanvasNode!=null)RestoreCanvasSmartSource(selectedCanvasNode);}));
        var smartSection=new C.Expander{Header="智能图层",Content=smart,IsExpanded=true,HorizontalAlignment=X.HorizontalAlignment.Stretch,HorizontalContentAlignment=X.HorizontalAlignment.Stretch};body.Children.Insert(Math.Min(3,body.Children.Count),smartSection);nativePropertySync.Add(()=>smartSection.Visibility=selectedCanvasNode!=null&&IsSmartLayer(selectedCanvasNode)?X.Visibility.Visible:X.Visibility.Collapsed);
    }

    C.MenuFlyout BuildCanvasLayerMenu(CanvasNode node)
    {
        void Select(){if(!canvasSelectedIds.Contains(node.Id))SetCanvasSelection(new[]{node});}
        var menu=CanvasMenu(("重命名",()=>RenameCanvasLayer(node)),("定位到对象",()=>{SetCanvasSelection(new[]{node});FitCanvasView(true);}));
        menu.Items.Add(new C.MenuFlyoutSeparator());
        void Item(string label,Action action,bool enabled=true){var item=new C.MenuFlyoutItem{Text=label,IsEnabled=enabled};item.Click+=(_,_)=>{Select();action();NativeSync();};menu.Items.Add(item);}
        Item("复制 · Ctrl+C",CopyCanvasSelection);
        Item("创建副本",DuplicateCanvasSelection);
        Item("原位粘贴到前面 · Ctrl+F",()=>PasteCanvasInPlace(false));
        Item("原位粘贴到后面 · Ctrl+B",()=>PasteCanvasInPlace(true));
        Item("再次变换 · Ctrl+D",RepeatCanvasTransform);
        Item("复制与重复排列…",ShowCanvasRepeatDialog,!node.Locked);
        menu.Items.Add(new C.MenuFlyoutSeparator());
        Item(node.ClipMask?"释放剪贴蒙版":"创建剪贴蒙版",()=>ToggleLayerClipMask(node),!node.Locked&&(node.ClipMask||canvasNodes.IndexOf(node)>0));
        Item("转换为智能图层",ConvertCanvasSmartLayer,!node.Locked);
        if(IsSmartLayer(node))Item("还原智能图层源内容",()=>RestoreCanvasSmartSource(node),!node.Locked);
        Item("栅格化图层",()=>RasterizeCanvasLayers(false),!node.Locked);
        Item("合并选中图层 · Ctrl+E",()=>RasterizeCanvasLayers(true),canvasSelectedIds.Count>1&&!node.Locked);
        var tags=new C.MenuFlyoutSubItem{Text="颜色标签"};
        foreach(var (name,index) in new[]{("无标签",0),("红色",1),("橙色",2),("黄色",3),("绿色",4)})
        {
            var item=new C.MenuFlyoutItem{Text=name,Icon=new C.FontIcon{Glyph="\uEA3A",Foreground=Brush(index==0?Color.Gray:LayerLabelColor(index))}};
            item.Click+=(_,_)=>{Select();ApplyLayerDataChange(n=>n.Extra.Label=index,true);NativeSync();};tags.Items.Add(item);
        }
        menu.Items.Add(tags);menu.Items.Add(new C.MenuFlyoutSeparator());
        Item("置于顶层",()=>BringCanvasSelectionToFront(true));Item("置于底层",()=>BringCanvasSelectionToFront(false));Item("删除图层",DeleteCanvasSelection,!node.Locked);
        return menu;
    }

    C.Button BuildCanvasPaintSettings()
    {
        var body=Stack(10);body.Width=260;
        nativePixelBrushMode=new C.ComboBox{Header="像素笔刷模式",HorizontalAlignment=X.HorizontalAlignment.Stretch};foreach(var name in pixelBrushModes)nativePixelBrushMode.Items.Add(name);
        nativePixelDensity=new C.NumberBox{Header="抖动 / 喷点密度 %",Minimum=1,Maximum=100,SmallChange=5,SpinButtonPlacementMode=C.NumberBoxSpinButtonPlacementMode.Compact};
        nativePixelBrushHint=TextBlock("",12);
        nativePixelBrushMode.SelectionChanged+=(_,_)=>{if(canvasPaintUiSync||nativePixelBrushMode.SelectedIndex<0)return;canvasPixelBrushMode=nativePixelBrushMode.SelectedIndex;SetCanvasTool(CanvasTool.PixelBrush);SyncCanvasPaintUi();};
        nativePixelDensity.ValueChanged+=(_,_)=>{if(!canvasPaintUiSync&&double.IsFinite(nativePixelDensity.Value)){canvasPixelDensity=(float)nativePixelDensity.Value;SyncCanvasWorkspaceUi();canvasSurface.Invalidate();}};
        body.Children.Add(nativePixelBrushMode);body.Children.Add(nativePixelDensity);body.Children.Add(nativePixelBrushHint);
        nativeBrushSize=new C.NumberBox{Header="画笔大小",Minimum=1,Maximum=300,Value=canvasBrushSize,SmallChange=1,SpinButtonPlacementMode=C.NumberBoxSpinButtonPlacementMode.Compact};
        nativeBrushOpacity=new C.NumberBox{Header="画笔不透明度 %",Minimum=1,Maximum=100,Value=canvasBrushOpacity*100,SmallChange=5,SpinButtonPlacementMode=C.NumberBoxSpinButtonPlacementMode.Compact};
        nativeBrushHardness=new C.NumberBox{Header="普通画笔硬度 %",Minimum=0,Maximum=100,Value=canvasBrushHardness*100,SmallChange=10,SpinButtonPlacementMode=C.NumberBoxSpinButtonPlacementMode.Compact};
        nativeBrushSize.ValueChanged+=(_,_)=>{if(!canvasPaintUiSync&&double.IsFinite(nativeBrushSize.Value))canvasBrushSize=(float)nativeBrushSize.Value;};
        nativeBrushOpacity.ValueChanged+=(_,_)=>{if(!canvasPaintUiSync&&double.IsFinite(nativeBrushOpacity.Value))canvasBrushOpacity=(float)nativeBrushOpacity.Value/100;};
        nativeBrushHardness.ValueChanged+=(_,_)=>{if(!canvasPaintUiSync&&double.IsFinite(nativeBrushHardness.Value))canvasBrushHardness=(float)nativeBrushHardness.Value/100;};
        body.Children.Add(nativeBrushSize);body.Children.Add(nativeBrushOpacity);body.Children.Add(nativeBrushHardness);
        var picker=new C.ColorPicker{Color=WinColor(canvasBrushColor),IsAlphaEnabled=false,IsHexInputVisible=true,IsMoreButtonVisible=true,MaxWidth=260};picker.ColorChanged+=(_,e)=>canvasBrushColor=Color.FromArgb(e.NewColor.R,e.NewColor.G,e.NewColor.B);body.Children.Add(picker);
        body.Children.Add(TextBlock("B 普通画笔 · N 像素笔刷 · Shift+B 矢量画笔 · P 钢笔\n按住 [ / ] 连续缩小 / 放大 · E 橡皮擦\n钢笔：点击加点，拖动出曲线，Enter 结束，点起点闭合。",12));
        var scroller=Scroll(body);scroller.MaxHeight=560;
        var flyout=new C.Flyout{Content=scroller,Placement=FlyoutPlacementMode.BottomEdgeAlignedLeft};flyout.Opening+=(_,_)=>SyncCanvasPaintUi();flyout.Opened+=(_,_)=>NativePopup(true);flyout.Closed+=(_,_)=>{NativePopup(false);SaveCanvasWorkspace();};
        nativeBrushColor=CanvasIconButton("画笔设置与颜色","\uE790",()=>{});nativeBrushColor.Flyout=flyout;return nativeBrushColor;
    }
    void SyncCanvasPaintUi()
    {
        if(nativeBrushSize==null)return;canvasPaintUiSync=true;
        nativePixelBrushMode!.SelectedIndex=canvasPixelBrushMode;nativePixelDensity!.Value=canvasPixelDensity;nativePixelDensity.Visibility=canvasPixelBrushMode>=3?X.Visibility.Visible:X.Visibility.Collapsed;
        nativePixelBrushHint!.Text=canvasPixelBrushMode switch{0=>"方形实心笔尖，适合铺色和方块细节。",1=>"圆形硬边笔尖，适合像素轮廓。",2=>"菱形笔尖，适合斜线和几何纹理。",3=>"规则抖动图案，用密度控制疏密；重复涂过保持图案。",_=>"随机喷点纹理，用密度控制疏密；每一笔生成新的点阵。"};
        nativeBrushHardness!.IsEnabled=canvasTool!=CanvasTool.PixelBrush;nativeBrushSize.Value=canvasBrushSize;nativeBrushOpacity!.Value=canvasBrushOpacity*100;nativeBrushHardness!.Value=canvasBrushHardness*100;canvasPaintUiSync=false;
    }
}
