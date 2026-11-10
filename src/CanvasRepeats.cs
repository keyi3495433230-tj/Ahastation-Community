using X=Microsoft.UI.Xaml;
using C=Microsoft.UI.Xaml.Controls;
namespace CardReview;
public sealed partial class ReviewForm
{
    sealed record CanvasRepeat(float X,float Y,float ScaleX,float ScaleY,float Angle,bool Copy,PointF? Pivot=null);
    CanvasRepeat? canvasRepeat;
    List<CanvasNode>? transformBefore;
    bool transformCopy;
    static PointF RepeatCenter(IEnumerable<CanvasNode> nodes){var b=nodes.Select(n=>new RectangleF(n.Position,n.Size)).Aggregate(RectangleF.Union);return new(b.Left+b.Width/2,b.Top+b.Height/2);}
    void CanvasMouseDown(object? sender,MouseEventArgs e)
    {
        CanvasMouseDownCore(sender,e);
        transformBefore=e.Button==MouseButtons.Left&&(canvasNodeDragging||canvasNodeRotating||canvasResizeNode!=null)?CanvasSelectedNodes().Select(CloneCanvasNodeTemplate).ToList():null;
        transformCopy=canvasAltClonePending;
    }
    void CanvasMouseUp(object? sender,MouseEventArgs e)
    {
        CanvasMouseUpCore(sender,e);
        if(e.Button!=MouseButtons.Left)return;
        if(transformBefore is {Count:>0} before)
        {
            var after=CanvasSelectedNodes();
            if(after.Count==before.Count)
            {
                var a=RepeatCenter(before);var b=RepeatCenter(after);var first=before[0];var last=after[0];
                var next=new CanvasRepeat(b.X-a.X,b.Y-a.Y,(float)last.Size.Width/first.Size.Width,(float)last.Size.Height/first.Size.Height,last.Rotation-first.Rotation,transformCopy);
                if(Math.Abs(next.X)+Math.Abs(next.Y)+Math.Abs(next.ScaleX-1)+Math.Abs(next.ScaleY-1)+Math.Abs(next.Angle)>.001f)canvasRepeat=next;
            }
        }
        transformBefore=null;
    }
    void ApplyRepeatGeometry(List<CanvasNode> nodes,CanvasRepeat step)
    {
        var center=step.Pivot??RepeatCenter(nodes);
        foreach(var n in nodes)
        {
            var v=new PointF((n.Position.X+n.Size.Width/2f-center.X)*step.ScaleX,(n.Position.Y+n.Size.Height/2f-center.Y)*step.ScaleY);
            v=RotateCanvasVector(v,step.Angle);
            n.Size=new(Math.Clamp((int)Math.Round(n.Size.Width*step.ScaleX),1,100000),Math.Clamp((int)Math.Round(n.Size.Height*step.ScaleY),1,100000));
            n.Position=new(center.X+v.X+step.X-n.Size.Width/2f,center.Y+v.Y+step.Y-n.Size.Height/2f);n.Rotation+=step.Angle;
        }
    }
    void RepeatCanvasTransform()
    {
        if(canvasRepeat==null){NativeMessage("再次变换","先移动、缩放、旋转对象，或按住 Alt 拖出副本，再按 Ctrl+D 重复相同变换。");return;}
        var source=CanvasSelectedNodes().Where(n=>!n.Locked&&!n.Hidden).ToList();if(source.Count==0)return;
        PushCanvasUndo();var target=canvasRepeat.Copy?CloneRepeatNodes(source):source;
        ApplyRepeatGeometry(target,canvasRepeat);FinishCanvasClones(target);
    }
    List<CanvasNode> CloneRepeatNodes(List<CanvasNode> source)
    {
        var ids=source.Select(n=>n.Id).ToHashSet();var templates=source.Select(CloneCanvasNodeTemplate).ToList();
        foreach(var n in templates){n.ComponentId=n.ComponentInstanceId=n.ComponentMasterNodeId=null;if(n.GroupId is Guid group&&canvasNodes.Any(x=>x.GroupId==group&&!ids.Contains(x.Id)))n.GroupId=null;}
        var links=canvasLinks.Where(l=>ids.Contains(l.From)&&ids.Contains(l.To)).Select(l=>(l.From,l.To,l.Label,l.Color,l.Arrow)).ToList();
        return InstantiateCanvasTemplates(templates,links,PointF.Empty,activeCanvasId);
    }
    void PasteCanvasInPlace(bool behind)
    {
        if(canvasClipboardNodes is not {Count:>0})return;PushCanvasUndo();
        int index=behind?canvasNodes.FindIndex(n=>canvasSelectedIds.Contains(n.Id)):canvasNodes.FindLastIndex(n=>canvasSelectedIds.Contains(n.Id))+1;
        if(canvasSelectedIds.Count==0)index=behind?0:canvasNodes.Count;
        var copies=InstantiateCanvasTemplates(canvasClipboardNodes,canvasClipboardLinks??new(),PointF.Empty,canvasClipboardBoardId);
        foreach(var n in copies)canvasNodes.Remove(n);canvasNodes.InsertRange(Math.Clamp(index,0,canvasNodes.Count),copies);FinishCanvasClones(copies);
    }
    void CreateCanvasRepeat(int mode,int count,int rows,int columns,float dx,float dy,float radius,float sweep,float start,bool orient,float scale,float rotation)
    {
        var source=CanvasSelectedNodes().Where(n=>!n.Locked&&!n.Hidden).ToList();if(source.Count==0)return;
        count=Math.Clamp(count,2,100);rows=Math.Clamp(rows,1,20);columns=Math.Clamp(columns,1,20);
        int total=mode==1?rows*columns:count;if(source.Count*total>2000){NativeMessage("重复排列","本次排列超过 2000 个对象，请减少数量。");return;}
        PushCanvasUndo();var all=new List<CanvasNode>(source);var center=RepeatCenter(source);var previous=source;
        double radians=start*Math.PI/180;var pivot=new PointF(center.X-radius*(float)Math.Cos(radians),center.Y-radius*(float)Math.Sin(radians));
        float angle=sweep/(Math.Abs(sweep)>=359.99f?count:count-1);
        for(int i=1;i<total;i++)
        {
            var copies=CloneRepeatNodes(mode==0?previous:source);
            if(mode==0)ApplyRepeatGeometry(copies,new(dx,dy,scale,scale,rotation,true));
            else if(mode==1)ApplyRepeatGeometry(copies,new((i%columns)*dx,(i/columns)*dy,1,1,0,true));
            else
            {
                var target=RotateCanvasPoint(center,pivot,i*angle);
                ApplyRepeatGeometry(copies,new(target.X-center.X,target.Y-center.Y,1,1,orient?i*angle:0,true));
            }
            all.AddRange(copies);previous=copies;
        }
        canvasRepeat=mode==0?new(dx,dy,scale,scale,rotation,true):null;
        FinishCanvasClones(mode==0?previous:all);
    }
    async void ShowCanvasRepeatDialog()
    {
        if(!CanvasSelectedNodes().Any(n=>!n.Locked&&!n.Hidden)){NativeMessage("重复排列","请先选中要排列的对象。");return;}
        var body=Stack(10);body.Width=340;
        var mode=new C.ComboBox{Header="排列方式",HorizontalAlignment=X.HorizontalAlignment.Stretch,SelectedIndex=0};foreach(var s in new[]{"连续变换复制","网格排列","径向 / 圆环排列"})mode.Items.Add(s);body.Children.Add(mode);
        C.NumberBox Number(string title,double value,double min,double max){var n=new C.NumberBox{Header=title,Value=value,Minimum=min,Maximum=max,SpinButtonPlacementMode=C.NumberBoxSpinButtonPlacementMode.Compact};body.Children.Add(n);return n;}
        var count=Number("总数量（包含原对象）",8,2,100);var rows=Number("行数",3,1,20);var cols=Number("列数",4,1,20);
        var dx=Number("水平间距（对象中心 / px）",160,-10000,10000);var dy=Number("垂直间距（对象中心 / px）",120,-10000,10000);
        var scale=Number("每次缩放 %",100,10,200);var rotation=Number("每次旋转角度",0,-360,360);
        var radius=Number("圆环半径 / px",200,1,10000);var sweep=Number("分布角度（360 为完整圆环）",360,1,360);var start=Number("起始角度",0,-360,360);
        var orient=new C.CheckBox{Content="随圆环旋转对象",IsChecked=true};body.Children.Add(orient);
        body.Children.Add(TextBlock("原对象保持在原位；生成的副本可分别编辑，Ctrl+Z 可整体撤销。",12));
        void Visibility(){int m=mode.SelectedIndex;foreach(var n in new X.FrameworkElement[]{count})n.Visibility=m==1?X.Visibility.Collapsed:X.Visibility.Visible;foreach(var n in new X.FrameworkElement[]{rows,cols})n.Visibility=m==1?X.Visibility.Visible:X.Visibility.Collapsed;foreach(var n in new X.FrameworkElement[]{dx,dy})n.Visibility=m<2?X.Visibility.Visible:X.Visibility.Collapsed;foreach(var n in new X.FrameworkElement[]{scale,rotation})n.Visibility=m==0?X.Visibility.Visible:X.Visibility.Collapsed;foreach(var n in new X.FrameworkElement[]{radius,sweep,start,orient})n.Visibility=m==2?X.Visibility.Visible:X.Visibility.Collapsed;}
        mode.SelectionChanged+=(_,_)=>Visibility();Visibility();var scroll=Scroll(body);scroll.MaxHeight=520;
        var dialog=new C.ContentDialog{Title="复制与重复排列",Content=scroll,PrimaryButtonText="生成排列",CloseButtonText="取消",XamlRoot=nativeRoot.XamlRoot,RequestedTheme=nativeRoot.RequestedTheme};
        NativePopup(true);try{if(await dialog.ShowAsync()==C.ContentDialogResult.Primary){var numbers=new[]{count,rows,cols,dx,dy,scale,rotation,radius,sweep,start};if(numbers.All(n=>double.IsFinite(n.Value)))CreateCanvasRepeat(mode.SelectedIndex,(int)count.Value,(int)rows.Value,(int)cols.Value,(float)dx.Value,(float)dy.Value,(float)radius.Value,(float)sweep.Value,(float)start.Value,orient.IsChecked==true,(float)scale.Value/100,(float)rotation.Value);}}finally{NativePopup(false);NativeSync();}
    }
}
