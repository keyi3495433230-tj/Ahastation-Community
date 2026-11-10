using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace CardReview;

public sealed partial class ReviewForm
{
    readonly List<PointF> canvasPaintPoints=new();
    readonly List<LayerPathPoint> canvasPenPoints=new();
    bool canvasPaintDown,canvasPenDragging;
    PointF canvasPenPreview;
    Color canvasBrushColor=Color.FromArgb(55,111,218);
    float canvasBrushSize=12,canvasBrushOpacity=1,canvasBrushHardness=1;
    Guid? canvasPathEditId;
    int canvasPathEditIndex=-1,canvasPathEditControl;
    bool canvasPathEditChanged;
    string canvasPathEditOriginal="";
    CanvasState[]? canvasPathEditRedo;
    bool IsCanvasPaintTool=>canvasTool is CanvasTool.Brush or CanvasTool.PixelBrush or CanvasTool.VectorBrush or CanvasTool.Pen or CanvasTool.Eraser;

    void CancelCanvasPaint()
    {
        canvasPaintDown=canvasPenDragging=false;canvasPaintPoints.Clear();canvasPenPoints.Clear();
        if(canvasPathEditChanged && canvasPathEditId is Guid id && canvasNodes.FirstOrDefault(n=>n.Id==id) is { } node){node.Extra=DecodeLayerData(canvasPathEditOriginal);if(canvasUndo.Count>0)canvasUndo.Pop();canvasRedo.Clear();if(canvasPathEditRedo!=null)foreach(var state in canvasPathEditRedo.Reverse())canvasRedo.Push(state);SaveCanvas();}
        canvasPathEditRedo=null;canvasPathEditIndex=-1;canvasPathEditChanged=false;canvasPathEditId=null;canvasSurface.Capture=false;canvasSurface.Invalidate();
    }

    bool CanvasPaintKey(Keys key)
    {
        if(key==Keys.Enter && canvasTool==CanvasTool.Pen && canvasPenPoints.Count>0){FinishCanvasPen(false);return true;}
        if(key==Keys.Escape && (IsCanvasPaintTool||canvasPathEditId.HasValue)){CancelCanvasPaint();SetCanvasTool(CanvasTool.Select);return true;}
        if(key==Keys.B){SetCanvasTool(CanvasTool.Brush);return true;}
        if(key==Keys.N){SetCanvasTool(CanvasTool.PixelBrush);return true;}
        if(key==(Keys.Shift|Keys.B)){SetCanvasTool(CanvasTool.VectorBrush);return true;}
        if(key==Keys.P){SetCanvasTool(CanvasTool.Pen);return true;}
        if(key==Keys.E){SetCanvasTool(CanvasTool.Eraser);return true;}
        // Each native key-repeat message is handled, so holding a bracket keeps resizing.
        if(IsCanvasPaintTool&&key is Keys.OemOpenBrackets or Keys.OemCloseBrackets)
        {
            float step=canvasTool==CanvasTool.PixelBrush?1:2;
            canvasBrushSize=Math.Clamp(canvasBrushSize+(key==Keys.OemOpenBrackets?-step:step),1,300);
            SyncCanvasPaintUi();SyncCanvasWorkspaceUi();canvasSurface.Invalidate();return true;
        }
        if(key==(Keys.Control|Keys.E)){RasterizeCanvasLayers(true);return true;}
        if(key==(Keys.Control|Keys.Alt|Keys.G)&&selectedCanvasNode!=null){ToggleLayerClipMask(selectedCanvasNode);return true;}
        return false;
    }

    bool CanvasPaintMouseDown(MouseEventArgs e)
    {
        if(canvasSpaceDown||e.Button!=MouseButtons.Left)return false;
        var world=CanvasWorldPoint(e.Location);
        if((ModifierKeys&Keys.Control)!=0&&RemoveCanvasPenHandle(e.Location))return true;
        if(canvasTool==CanvasTool.Pen)
        {
            if(e.Clicks>1){FinishCanvasPen(false);return true;}
            if(canvasPenPoints.Count>=3)
            {
                var first=canvasPenPoints[0];if(Math.Abs((world.X-first.X)*canvasZoom)<9&&Math.Abs((world.Y-first.Y)*canvasZoom)<9){FinishCanvasPen(true);return true;}
            }
            canvasPenPoints.Add(new(){X=world.X,Y=world.Y});canvasPenDragging=true;canvasPenPreview=world;canvasSurface.Capture=true;canvasSurface.Invalidate();return true;
        }
        if(canvasTool is CanvasTool.Brush or CanvasTool.PixelBrush or CanvasTool.VectorBrush or CanvasTool.Eraser)
        {
            if(canvasTool==CanvasTool.PixelBrush)canvasPixelStrokeSeed=Random.Shared.Next();
            CommitCanvasInlineEdit();canvasPaintPoints.Clear();canvasPaintPoints.Add(world);canvasPaintDown=true;canvasSurface.Capture=true;canvasSurface.Invalidate();return true;
        }
        if(canvasPathEditId is Guid id && canvasNodes.FirstOrDefault(n=>n.Id==id) is {Locked:false} node)
        {
            var local=CanvasPathLocalPoint(node,e.Location);float threshold=8/canvasZoom;
            for(int i=0;i<node.Extra.Points.Count;i++)
            {
                var p=node.Extra.Points[i];var positions=new[]{new PointF((p.X+p.InX)*node.Size.Width,(p.Y+p.InY)*node.Size.Height),new PointF((p.X+p.OutX)*node.Size.Width,(p.Y+p.OutY)*node.Size.Height),new PointF(p.X*node.Size.Width,p.Y*node.Size.Height)};
                for(int j=2;j>=0;j--)if(Math.Abs(local.X-positions[j].X)<=threshold&&Math.Abs(local.Y-positions[j].Y)<=threshold)
                {canvasPathEditIndex=i;canvasPathEditControl=j;canvasPathEditChanged=false;canvasPathEditOriginal=EncodeLayerData(node.Extra);canvasSurface.Capture=true;return true;}
            }
            canvasPathEditId=null;
        }
        return false;
    }

    bool CanvasPaintMouseMove(MouseEventArgs e)
    {
        var world=CanvasWorldPoint(e.Location);
        if(IsCanvasPaintTool)canvasSurface.Invalidate();
        if(canvasTool==CanvasTool.Pen)
        {
            canvasPenPreview=world;
            if(canvasPenDragging && canvasPenPoints.Count>0){var p=canvasPenPoints[^1];p.OutX=world.X-p.X;p.OutY=world.Y-p.Y;p.InX=-p.OutX;p.InY=-p.OutY;}
            canvasSurface.Invalidate();return !canvasPanning;
        }
        if(canvasPaintDown)
        {
            var last=canvasPaintPoints[^1];if(Math.Abs(world.X-last.X)+Math.Abs(world.Y-last.Y)>.6f/canvasZoom)canvasPaintPoints.Add(world);
            canvasSurface.Invalidate();return true;
        }
        if(canvasPathEditIndex>=0 && canvasPathEditId is Guid id && canvasNodes.FirstOrDefault(n=>n.Id==id) is { } node)
        {
            if(!canvasPathEditChanged){canvasPathEditRedo=canvasRedo.ToArray();PushCanvasUndo();canvasPathEditChanged=true;}
            var local=CanvasPathLocalPoint(node,e.Location);var p=node.Extra.Points[canvasPathEditIndex];float x=local.X/node.Size.Width,y=local.Y/node.Size.Height;
            if(canvasPathEditControl==2){p.X=x;p.Y=y;}
            else if(canvasPathEditControl==0){p.InX=x-p.X;p.InY=y-p.Y;if((ModifierKeys&Keys.Alt)!=0)p.Broken=true;if(!p.Broken){p.OutX=-p.InX;p.OutY=-p.InY;}}
            else{p.OutX=x-p.X;p.OutY=y-p.Y;if((ModifierKeys&Keys.Alt)!=0)p.Broken=true;if(!p.Broken){p.InX=-p.OutX;p.InY=-p.OutY;}}
            canvasSurface.Invalidate();return true;
        }
        return false;
    }

    bool CanvasPaintMouseUp(MouseEventArgs e)
    {
        if(e.Button!=MouseButtons.Left)return false;
        if(canvasPenDragging){canvasPenDragging=false;canvasSurface.Capture=false;return true;}
        if(canvasPathEditIndex>=0){canvasPathEditIndex=-1;canvasSurface.Capture=false;if(canvasPathEditChanged)SaveCanvas();canvasPathEditChanged=false;canvasPathEditRedo=null;return true;}
        if(!canvasPaintDown)return false;
        canvasPaintDown=false;canvasSurface.Capture=false;
        if(canvasTool==CanvasTool.VectorBrush)CreateCanvasVectorStroke();else PaintCanvasRasterStroke(canvasTool==CanvasTool.Eraser);
        canvasPaintPoints.Clear();canvasSurface.Invalidate();return true;
    }

    PointF CanvasPathLocalPoint(CanvasNode node,Point screen)
    {
        var b=CanvasBounds(node);var p=RotateCanvasPoint(screen,CanvasNodeCenter(b),-node.Rotation);
        return new((p.X-b.Left)/canvasZoom,(p.Y-b.Top)/canvasZoom);
    }

    bool RemoveCanvasPenHandle(Point screen)
    {
        if(canvasTool==CanvasTool.Pen&&canvasPenPoints.Count>0)
        {
            var p=CanvasWorldPoint(screen);if(RemovePathHandle(canvasPenPoints,p,1,1,8/canvasZoom)){canvasSurface.Invalidate();return true;}
        }
        var node=canvasNodes.FirstOrDefault(n=>n.Id==canvasPathEditId)??selectedCanvasNode;
        if(node is not {Kind:"path",Locked:false})return false;
        var points=node.Extra.Points.Select(p=>new LayerPathPoint{X=p.X,Y=p.Y,InX=p.InX,InY=p.InY,OutX=p.OutX,OutY=p.OutY,Broken=p.Broken}).ToList();
        if(!RemovePathHandle(points,CanvasPathLocalPoint(node,screen),node.Size.Width,node.Size.Height,8/canvasZoom))return false;
        PushCanvasUndo();node.Extra.Points=points;node.Extra.Smooth=false;canvasPathEditId=node.Id;SaveCanvas();canvasSurface.Invalidate();return true;
    }
    static bool RemovePathHandle(List<LayerPathPoint> points,PointF click,float width,float height,float tolerance)
    {
        bool Near(float x,float y)=>Math.Abs(click.X-x*width)<=tolerance&&Math.Abs(click.Y-y*height)<=tolerance;
        foreach(var p in points)
        {
            bool outgoing=p.OutX!=0||p.OutY!=0,incoming=p.InX!=0||p.InY!=0;
            if(outgoing&&Near(p.X+p.OutX,p.Y+p.OutY)){p.OutX=p.OutY=0;p.Broken=true;return true;}
            if(incoming&&Near(p.X+p.InX,p.Y+p.InY)){p.InX=p.InY=0;p.Broken=true;return true;}
            if(Near(p.X,p.Y)){if(outgoing){p.OutX=p.OutY=0;p.Broken=true;return true;}if(incoming){p.InX=p.InY=0;p.Broken=true;return true;}return true;}
        }
        return false;
    }

    void CreateCanvasVectorStroke()
    {
        if(canvasPaintPoints.Count==0)return;
        // Simplify samples in world space so long strokes remain inexpensive to edit and save.
        var points=SimplifyPaintPoints(canvasPaintPoints,Math.Max(.3f,.6f/canvasZoom));
        CreateCanvasPath(points.Select(p=>new LayerPathPoint{X=p.X,Y=p.Y}).ToList(),false,true,"矢量画笔");
    }
    static List<PointF> SimplifyPaintPoints(IReadOnlyList<PointF> points,float tolerance)
    {
        if(points.Count<3)return points.ToList();var keep=new bool[points.Count];keep[0]=keep[^1]=true;var stack=new Stack<(int,int)>();stack.Push((0,points.Count-1));
        while(stack.Count>0){var(a,b)=stack.Pop();float max=0;int index=-1;var p=points[a];var q=points[b];float dx=q.X-p.X,dy=q.Y-p.Y,den=dx*dx+dy*dy;
            for(int i=a+1;i<b;i++){float t=den==0?0:Math.Clamp(((points[i].X-p.X)*dx+(points[i].Y-p.Y)*dy)/den,0,1);float ex=points[i].X-p.X-t*dx,ey=points[i].Y-p.Y-t*dy,dist=ex*ex+ey*ey;if(dist>max){max=dist;index=i;}}
            if(index>=0&&max>tolerance*tolerance){keep[index]=true;stack.Push((a,index));stack.Push((index,b));}}
        return points.Where((_,i)=>keep[i]).ToList();
    }

    void FinishCanvasPen(bool closed,bool edit=true)
    {
        if(canvasPenPoints.Count<2){canvasPenPoints.Clear();canvasSurface.Invalidate();return;}
        var points=canvasPenPoints.Select(p=>new LayerPathPoint{X=p.X,Y=p.Y,InX=p.InX,InY=p.InY,OutX=p.OutX,OutY=p.OutY,Broken=p.Broken}).ToList();
        canvasPenPoints.Clear();canvasPenDragging=false;canvasSurface.Capture=false;CreateCanvasPath(points,closed,false,"钢笔路径");canvasSurface.Invalidate();
        if(edit&&selectedCanvasNode is {Kind:"path"} node)EditCanvasPath(node);
    }

    void CreateCanvasPath(List<LayerPathPoint> points,bool closed,bool smooth,string name)
    {
        if(points.Count==0)return;
        float left=points.Min(p=>p.X),top=points.Min(p=>p.Y),width=Math.Max(1,points.Max(p=>p.X)-left),height=Math.Max(1,points.Max(p=>p.Y)-top);
        var size=new Size(Math.Max(1,(int)Math.Ceiling(width)),Math.Max(1,(int)Math.Ceiling(height)));
        foreach(var p in points){p.X=(p.X-left)/size.Width;p.Y=(p.Y-top)/size.Height;p.InX/=size.Width;p.OutX/=size.Width;p.InY/=size.Height;p.OutY/=size.Height;}
        var node=new CanvasNode{Kind="path",Name=name,Position=new(left,top),Size=size,StrokeWidth=canvasBrushSize,StrokeColor=canvasBrushColor.ToArgb().ToString(),Opacity=canvasBrushOpacity,Extra=new LayerData{Points=points,Closed=closed,Smooth=smooth}};
        PushCanvasUndo();InsertPaintLayer(node);SaveCanvas();canvasSurface.Invalidate();
    }

    void InsertPaintLayer(CanvasNode node)
    {
        int at=selectedCanvasNode==null?canvasNodes.Count:canvasNodes.IndexOf(selectedCanvasNode)+1;
        canvasNodes.Insert(Math.Clamp(at,0,canvasNodes.Count),node);SetCanvasSelection(new[]{node});nativeLayerKey="";
    }

    void PaintCanvasRasterStroke(bool erase)
    {
        if(canvasPaintPoints.Count==0)return;
        var target=canvasSelectedIds.Count==1 && selectedCanvasNode is {Kind:"image",Locked:false,Hidden:false} n && !IsSmartLayer(n)?n:null;
        if(erase && target==null){NativeMessage("橡皮擦","请先选择一个像素图层。文字、矢量或智能图层可以右键栅格化后再擦除。");return;}
        var stroke=RectangleF.FromLTRB(canvasPaintPoints.Min(p=>p.X)-canvasBrushSize,canvasPaintPoints.Min(p=>p.Y)-canvasBrushSize,canvasPaintPoints.Max(p=>p.X)+canvasBrushSize,canvasPaintPoints.Max(p=>p.Y)+canvasBrushSize);
        var targetBounds=target==null?RectangleF.Empty:LayerWorldBounds(target);if(target!=null){float pad=Math.Max(2,target.StrokeWidth+2);targetBounds.Inflate(-pad,-pad);}
        RectangleF bounds=target==null?stroke:erase?targetBounds:RectangleF.Union(targetBounds,stroke);
        bounds=RectangleF.FromLTRB((float)Math.Floor(bounds.Left),(float)Math.Floor(bounds.Top),(float)Math.Ceiling(bounds.Right),(float)Math.Ceiling(bounds.Bottom));
        if(bounds.Width>8192||bounds.Height>8192||bounds.Width*bounds.Height>16000000){NativeMessage("画笔区域过大","单个像素图层支持最多 1600 万像素。请在新的图层继续绘制，或先缩小图层。 ");return;}
        var bitmap=new Bitmap(Math.Max(1,(int)bounds.Width),Math.Max(1,(int)bounds.Height),PixelFormat.Format32bppArgb);bitmap.SetResolution(DeviceDpi,DeviceDpi);
        if(target!=null)
        {
            float oldZoom=canvasZoom;var oldOffset=canvasOffset;float opacity=target.Opacity;var extra=target.Extra;float fill=target.FillOpacity;
            try
            {
                canvasZoom=1;canvasOffset=new(-bounds.X,-bounds.Y);target.Opacity=1;target.FillOpacity=1;target.Extra=new LayerData{PixelArt=extra.PixelArt};
                using var raster=RenderCanvasLayer(target,new Rectangle(Point.Empty,bitmap.Size),0);if(raster!=null){using var g=Graphics.FromImage(bitmap);g.DrawImageUnscaled(raster.Image,raster.Bounds.Location);}
            }
            finally{canvasZoom=oldZoom;canvasOffset=oldOffset;target.Opacity=opacity;target.Extra=extra;target.FillOpacity=fill;}
        }
        bool pixel=canvasTool==CanvasTool.PixelBrush||(erase&&target?.Extra.PixelArt==true);
        ApplyRasterBrush(bitmap,bounds,canvasPaintPoints,canvasBrushSize,canvasBrushColor,canvasBrushOpacity,canvasBrushHardness,erase,pixel,erase?0:canvasPixelBrushMode,canvasPixelDensity,canvasPixelStrokeSeed);
        var result=new CanvasNode{Kind="image",Name=target?.Name??"画笔图层",Position=bounds.Location,Size=bitmap.Size,Bitmap=bitmap,Opacity=target?.Opacity??1,FillOpacity=target?.FillOpacity??1,ClipMask=target?.ClipMask??false,Extra=target==null?new():CopyLayerData(target.Extra)};
        if(pixel){result.Extra.PixelArt=true;if(target==null)result.Name="像素笔刷图层";}
        PushCanvasUndo();if(target!=null)ReplaceCanvasLayers(new[]{target},result);else{InsertPaintLayer(result);SaveCanvasNodeImage(result);SaveCanvas();}
    }

    static unsafe void ApplyRasterBrush(Bitmap bitmap,RectangleF bounds,IReadOnlyList<PointF> points,float size,Color color,float opacity,float hardness,bool erase,bool pixel=false,int pixelMode=0,float density=50,int seed=0)
    {
        int width=bitmap.Width,height=bitmap.Height;var mask=new byte[width*height];float radius=Math.Max(.5f,size/2);float step=Math.Max(.4f,radius*.25f);
        void Stamp(PointF p)
        {
            float cx=p.X-bounds.X,cy=p.Y-bounds.Y;
            if(pixel)
            {
                int side=Math.Max(1,(int)Math.Round(size));int left=(int)Math.Floor(Math.Floor(cx)-(side-1)/2f),top=(int)Math.Floor(Math.Floor(cy)-(side-1)/2f);byte alpha=PixelByte(Math.Clamp(opacity,0,1));
                for(int y=Math.Max(0,top);y<Math.Min(height,top+side);y++)for(int x=Math.Max(0,left);x<Math.Min(width,left+side);x++)
                    if(PixelBrushCovers(pixelMode,x-left,y-top,side,(int)bounds.X+x,(int)bounds.Y+y,density,seed))mask[y*width+x]=alpha;
                return;
            }
            for(int y=Math.Max(0,(int)(cy-radius-1));y<Math.Min(height,(int)(cy+radius+2));y++)for(int x=Math.Max(0,(int)(cx-radius-1));x<Math.Min(width,(int)(cx+radius+2));x++)
            {
                float distance=(float)Math.Sqrt((x+.5f-cx)*(x+.5f-cx)+(y+.5f-cy)*(y+.5f-cy));float edge=hardness>=.99f?Math.Clamp(radius+.5f-distance,0,1):Math.Clamp((radius-distance)/Math.Max(.5f,radius*(1-hardness)),0,1);
                byte a=PixelByte(edge*Math.Clamp(opacity,0,1));int index=y*width+x;if(a>mask[index])mask[index]=a;
            }
        }
        Stamp(points[0]);for(int i=1;i<points.Count;i++){var a=points[i-1];var b=points[i];float distance=(float)Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y));int count=Math.Max(1,(int)Math.Ceiling(distance/step));for(int j=1;j<=count;j++)Stamp(new(a.X+(b.X-a.X)*j/count,a.Y+(b.Y-a.Y)*j/count));}
        var data=bitmap.LockBits(new Rectangle(Point.Empty,bitmap.Size),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
        try{for(int y=0;y<height;y++){byte* p=(byte*)data.Scan0+y*data.Stride;for(int x=0;x<width;x++,p+=4){float a=mask[y*width+x]/255f;if(a==0)continue;if(erase){p[3]=PixelByte(p[3]/255f*(1-a));continue;}float b=p[3]/255f,alpha=a+b*(1-a);p[0]=PixelByte((color.B/255f*a+p[0]/255f*b*(1-a))/alpha);p[1]=PixelByte((color.G/255f*a+p[1]/255f*b*(1-a))/alpha);p[2]=PixelByte((color.R/255f*a+p[2]/255f*b*(1-a))/alpha);p[3]=PixelByte(alpha);}}}finally{bitmap.UnlockBits(data);}
    }

    void DrawCanvasPaintOverlay(Graphics g)
    {
        PointF Screen(PointF p)=>new(p.X*canvasZoom+canvasOffset.X,p.Y*canvasZoom+canvasOffset.Y);
        if(canvasPaintDown&&canvasPaintPoints.Count>0)
        {
            if(canvasTool==CanvasTool.PixelBrush){DrawPixelBrushPreview(g);return;}
            using var pen=new Pen(Color.FromArgb((int)(canvasBrushOpacity*255),canvasTool==CanvasTool.Eraser?Color.Gray:canvasBrushColor),Math.Max(1,canvasBrushSize*canvasZoom)){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round};
            if(canvasPaintPoints.Count==1){var p=Screen(canvasPaintPoints[0]);g.DrawLine(pen,p,new PointF(p.X+.1f,p.Y));}else g.DrawLines(pen,canvasPaintPoints.Select(Screen).ToArray());
        }
        if(canvasPenPoints.Count>0)
        {
            using var pen=new Pen(canvasBrushColor,Math.Max(1,canvasBrushSize*canvasZoom)){StartCap=LineCap.Round,EndCap=LineCap.Round};
            for(int i=1;i<canvasPenPoints.Count;i++){var a=canvasPenPoints[i-1];var b=canvasPenPoints[i];g.DrawBezier(pen,Screen(new(a.X,a.Y)),Screen(new(a.X+a.OutX,a.Y+a.OutY)),Screen(new(b.X+b.InX,b.Y+b.InY)),Screen(new(b.X,b.Y)));}
            if(!canvasPenDragging){var p=canvasPenPoints[^1];using var preview=new Pen(canvasSelectionColor,1){DashStyle=DashStyle.Dash};g.DrawLine(preview,Screen(new(p.X,p.Y)),Screen(canvasPenPreview));}
            foreach(var p in canvasPenPoints)DrawKnot(g,Screen(new(p.X,p.Y)),Screen(new(p.X+p.InX,p.Y+p.InY)),Screen(new(p.X+p.OutX,p.Y+p.OutY)));
        }
        if(canvasPathEditId is Guid id&&canvasNodes.FirstOrDefault(n=>n.Id==id) is { } node)
        {
            var bounds=CanvasBounds(node);var center=CanvasNodeCenter(bounds);
            PointF P(float x,float y)=>RotateCanvasPoint(new(bounds.X+x*bounds.Width,bounds.Y+y*bounds.Height),center,node.Rotation);
            foreach(var p in node.Extra.Points)DrawKnot(g,P(p.X,p.Y),P(p.X+p.InX,p.Y+p.InY),P(p.X+p.OutX,p.Y+p.OutY));
        }
        void DrawKnot(Graphics graphics,PointF p,PointF a,PointF b){using var pen=new Pen(canvasSelectionColor,1);using var fill=new SolidBrush(Color.White);graphics.DrawLine(pen,a,b);graphics.FillRectangle(fill,p.X-4,p.Y-4,8,8);graphics.DrawRectangle(pen,p.X-4,p.Y-4,8,8);foreach(var h in new[]{a,b}.Where(h=>Math.Abs(h.X-p.X)+Math.Abs(h.Y-p.Y)>.01f)){graphics.FillEllipse(fill,h.X-3,h.Y-3,6,6);graphics.DrawEllipse(pen,h.X-3,h.Y-3,6,6);}}
    }

    void DrawPixelBrushPreview(Graphics g)
    {
        var bounds=RectangleF.FromLTRB(canvasPaintPoints.Min(p=>p.X)-canvasBrushSize,canvasPaintPoints.Min(p=>p.Y)-canvasBrushSize,canvasPaintPoints.Max(p=>p.X)+canvasBrushSize,canvasPaintPoints.Max(p=>p.Y)+canvasBrushSize);
        var view=new RectangleF(-canvasOffset.X/canvasZoom,-canvasOffset.Y/canvasZoom,canvasSurface.Width/canvasZoom,canvasSurface.Height/canvasZoom);view.Inflate(canvasBrushSize,canvasBrushSize);bounds=RectangleF.Intersect(bounds,view);
        var rect=Rectangle.FromLTRB((int)Math.Floor(bounds.Left),(int)Math.Floor(bounds.Top),(int)Math.Ceiling(bounds.Right),(int)Math.Ceiling(bounds.Bottom));
        if(rect.Width<1||rect.Height<1||(long)rect.Width*rect.Height>16000000)return;
        using var preview=new Bitmap(rect.Width,rect.Height,PixelFormat.Format32bppArgb);
        ApplyRasterBrush(preview,rect,canvasPaintPoints,canvasBrushSize,canvasBrushColor,canvasBrushOpacity,1,false,true,canvasPixelBrushMode,canvasPixelDensity,canvasPixelStrokeSeed);
        var state=g.Save();g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;
        g.DrawImage(preview,new RectangleF(rect.X*canvasZoom+canvasOffset.X,rect.Y*canvasZoom+canvasOffset.Y,rect.Width*canvasZoom,rect.Height*canvasZoom),new RectangleF(0,0,preview.Width,preview.Height),GraphicsUnit.Pixel);g.Restore(state);
    }

    void DrawCanvasBrushCursor(Graphics g)
    {
        if(canvasTool is not (CanvasTool.Brush or CanvasTool.PixelBrush or CanvasTool.VectorBrush or CanvasTool.Eraser)||nativePopupDepth>0)return;
        var pointer=canvasSurface.PointToClient(Cursor.Position);if(!canvasSurface.ClientRectangle.Contains(pointer))return;
        float diameter=Math.Max(1,canvasBrushSize*canvasZoom);
        var rect=new RectangleF(pointer.X-diameter/2,pointer.Y-diameter/2,diameter,diameter);
        using var outer=new Pen(Color.FromArgb(210,Color.Black),2);using var inner=new Pen(Color.FromArgb(235,Color.White),1);
        foreach(var pen in new[]{outer,inner})
        {
            if(canvasTool==CanvasTool.PixelBrush&&canvasPixelBrushMode==2)g.DrawPolygon(pen,new[]{new PointF(pointer.X,rect.Top),new PointF(rect.Right,pointer.Y),new PointF(pointer.X,rect.Bottom),new PointF(rect.Left,pointer.Y)});
            else if(canvasTool==CanvasTool.PixelBrush&&canvasPixelBrushMode is 0 or 3)g.DrawRectangle(pen,rect.X,rect.Y,rect.Width,rect.Height);
            else g.DrawEllipse(pen,rect);
        }
    }

    void EditCanvasPath(CanvasNode node)
    {
        if(node.Kind!="path"||node.Locked)return;SetCanvasTool(CanvasTool.Select);SetCanvasSelection(new[]{node});
        if(node.Extra.Smooth)
        {
            PushCanvasUndo();var points=node.Extra.Points;
            for(int i=0;i<points.Count;i++){var before=points[Math.Max(0,i-1)];var after=points[Math.Min(points.Count-1,i+1)];var p=points[i];p.OutX=(after.X-before.X)*.35f/3;p.OutY=(after.Y-before.Y)*.35f/3;p.InX=-p.OutX;p.InY=-p.OutY;}
            node.Extra.Smooth=false;SaveCanvas();
        }
        canvasPathEditId=node.Id;SyncCanvasWorkspaceUi();canvasSurface.Focus();canvasSurface.Invalidate();
    }
}
