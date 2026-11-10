using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;

namespace CardReview;

public sealed partial class ReviewForm
{
    Bitmap? canvasComposite;
    int canvasCompositeKey;
    bool layerRenderActive;
    sealed record SmartRenderCache(string Source,Size Pixels,bool Dark,int Dpi,Bitmap Image);
    readonly Dictionary<Guid,SmartRenderCache> smartRenderCache=new();
    void DisposeLayerRenderCache(){canvasComposite?.Dispose();canvasComposite=null;foreach(var item in smartRenderCache.Values)item.Image.Dispose();smartRenderCache.Clear();}
    sealed record LayerRaster(Bitmap Image, Rectangle Bounds) : IDisposable { public void Dispose()=>Image.Dispose(); }

    int CanvasCompositeSignature(Size size)
    {
        var hash=new HashCode();hash.Add(size);hash.Add(canvasZoom);hash.Add(canvasOffset);hash.Add(darkTheme);hash.Add(DeviceDpi);hash.Add(canvasInlineEditNodeId);
        foreach(var n in canvasNodes)
        {
            hash.Add(n.Id);hash.Add(n.Kind);hash.Add(n.Content);hash.Add(n.Position);hash.Add(n.Size);hash.Add(n.Rotation);hash.Add(n.Hidden);hash.Add(n.Transparent);hash.Add(n.ClipMask);
            hash.Add(n.FontSize);hash.Add(n.FontBold);hash.Add(n.FontItalic);hash.Add(n.FontStrikeout);hash.Add(n.FontAlign);hash.Add(n.FontVAlign);hash.Add(n.FontColor);hash.Add(n.FillColor);hash.Add(n.Opacity);hash.Add(n.FillOpacity);hash.Add(n.CornerRadius);hash.Add(n.StrokeWidth);hash.Add(n.StrokeColor);hash.Add(n.StrokeAlign);hash.Add(n.StrokeDash);hash.Add(n.Bitmap);
            foreach(var p in n.VectorAnchors)hash.Add(p);
            var e=n.Extra;hash.Add(e.FontFamily);hash.Add(e.Blend);hash.Add(e.Brightness);hash.Add(e.Contrast);hash.Add(e.Saturation);hash.Add(e.Hue);hash.Add(e.Red);hash.Add(e.Green);hash.Add(e.Blue);hash.Add(e.SmartSource);hash.Add(e.Closed);hash.Add(e.FillPath);hash.Add(e.Smooth);hash.Add(e.PixelArt);
            foreach(var p in e.Points){hash.Add(p.X);hash.Add(p.Y);hash.Add(p.InX);hash.Add(p.InY);hash.Add(p.OutX);hash.Add(p.OutY);}
        }
        foreach(var l in canvasLinks){hash.Add(l.From);hash.Add(l.To);hash.Add(l.Color);hash.Add(l.Label);hash.Add(l.Arrow);}
        return hash.ToHashCode();
    }

    void DrawLayerWorkspace(Graphics g,Rectangle clip)
    {
        bool export=canvasExporting;
        g.Clear(export?Color.Transparent:canvasSurface.BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
        if(!export && canvasWorkspace.Grid)
        {
            float spacing=28*canvasZoom;while(spacing<14)spacing*=2;
            float ox=((canvasOffset.X%spacing)+spacing)%spacing,oy=((canvasOffset.Y%spacing)+spacing)%spacing;
            using var dots=new SolidBrush(darkTheme?Color.FromArgb(65,65,69):Color.FromArgb(218,218,221));
            for(float y=oy+(float)Math.Floor((clip.Top-oy)/spacing)*spacing;y<clip.Bottom;y+=spacing)
                for(float x=ox+(float)Math.Floor((clip.Left-ox)/spacing)*spacing;x<clip.Right;x+=spacing)g.FillEllipse(dots,x-1,y-1,2,2);
        }
        if(export)
        {
            var nodes=canvasNodes.Where(n=>canvasExportNodeIds==null||canvasExportNodeIds.Contains(n.Id)).ToList();
            using var image=RenderLayerComposition(clip.Size,nodes,canvasLinks,0);g.DrawImageUnscaled(image,clip.Location);return;
        }
        var size=canvasSurface.ClientSize;if(size.Width<1||size.Height<1)return;
        int key=CanvasCompositeSignature(size);
        if(canvasComposite==null||key!=canvasCompositeKey)
        {
            var next=RenderLayerComposition(size,canvasNodes,canvasLinks,0);canvasComposite?.Dispose();canvasComposite=next;canvasCompositeKey=key;
        }
        g.DrawImageUnscaled(canvasComposite,Point.Empty);
        if(selectedCanvasLink is { } selected && canvasNodes.FirstOrDefault(n=>n.Id==selected.From) is { } from && canvasNodes.FirstOrDefault(n=>n.Id==selected.To) is { } to)DrawCanvasLink(g,selected,from,to,false,PointF.Empty,selected.Label);
        if(canvasWiring&&wireStartNode!=null)DrawCanvasLink(g,null,wireStartNode,wireTargetNode,true,wireEnd);
        foreach(var node in canvasNodes.Where(n=>!n.Hidden && (canvasSelectedIds.Contains(n.Id)||n==canvasHoveredNode||canvasWiring)))
        {
            var bounds=CanvasBounds(node);var visual=LayerScreenBounds(node);if(!visual.IntersectsWith(clip))continue;
            if(canvasPathEditId==node.Id)continue;
            var saved=g.Save();var center=CanvasNodeCenter(bounds);g.TranslateTransform(center.X,center.Y);g.RotateTransform(node.Rotation);g.TranslateTransform(-center.X,-center.Y);
            if(canvasSelectedIds.Contains(node.Id))DrawCanvasWorkspaceSelection(g,node,bounds);
            else if(node==canvasHoveredNode){using var hover=new Pen(Color.FromArgb(130,canvasSelectionColor),1);g.DrawRectangle(hover,bounds);}
            if(canvasVectorEditNodeId==node.Id && IsCanvasShapeKind(node))
            {
                using var pen=new Pen(canvasSelectionColor,1.5f);using var fill=new SolidBrush(Color.White);
                foreach(var point in CanvasAnchorHandles(node,bounds)){var r=new RectangleF(point.Point.X-4,point.Point.Y-4,8,8);g.FillRectangle(fill,r);g.DrawRectangle(pen,r.X,r.Y,r.Width,r.Height);}
                if(canvasAnchorPreviewInsertIndex>=0){using var preview=new SolidBrush(canvasSelectionColor);g.FillEllipse(preview,canvasAnchorPreviewPoint.X-6,canvasAnchorPreviewPoint.Y-6,12,12);}
            }
            if(node.Kind=="text"&&string.IsNullOrEmpty(node.Content)&&canvasInlineEditNodeId!=node.Id&&canvasSelectedIds.Contains(node.Id)){using var font=new Font(UiFontName(),Math.Clamp(9*canvasZoom,6,18));using var brush=new SolidBrush(Color.Gray);g.DrawString("双击编辑文字",font,brush,bounds);}
            foreach(bool output in new[]{false,true})
            {
                float x=output?bounds.Right+18:bounds.Left-18,y=bounds.Top+bounds.Height/2f;
                using var fill=new SolidBrush(output?Color.FromArgb(190,105,80):Color.FromArgb(83,121,99));using var border=new Pen(Color.White,1.5f);
                g.FillEllipse(fill,x-4,y-4,8,8);g.DrawEllipse(border,x-4,y-4,8,8);
            }
            g.Restore(saved);
        }
        if(canvasMarqueeSelecting&&!canvasMarqueeBounds.IsEmpty){using var fill=new SolidBrush(Color.FromArgb(28,canvasSelectionColor));using var pen=new Pen(canvasSelectionColor,1);g.FillRectangle(fill,canvasMarqueeBounds);g.DrawRectangle(pen,canvasMarqueeBounds);}
        using(var guide=new Pen(Color.FromArgb(242,74,120),1))foreach(var (a,b) in canvasSnapGuides)g.DrawLine(guide,new PointF(a.X*canvasZoom+canvasOffset.X,a.Y*canvasZoom+canvasOffset.Y),new PointF(b.X*canvasZoom+canvasOffset.X,b.Y*canvasZoom+canvasOffset.Y));
        DrawCanvasWorkspaceOverlay(g);DrawCanvasPaintOverlay(g);DrawCanvasBrushCursor(g);
    }

    Rectangle LayerScreenBounds(CanvasNode node)
    {
        var world=LayerWorldBounds(node);
        return Rectangle.FromLTRB((int)Math.Floor(world.Left*canvasZoom+canvasOffset.X),(int)Math.Floor(world.Top*canvasZoom+canvasOffset.Y),(int)Math.Ceiling(world.Right*canvasZoom+canvasOffset.X),(int)Math.Ceiling(world.Bottom*canvasZoom+canvasOffset.Y));
    }

    Bitmap RenderLayerComposition(Size size,IReadOnlyList<CanvasNode> nodes,IReadOnlyList<CanvasLink>? links,int depth)
    {
        var output=new Bitmap(Math.Max(1,size.Width),Math.Max(1,size.Height),PixelFormat.Format32bppArgb);output.SetResolution(DeviceDpi,DeviceDpi);
        LayerRaster? baseRaster=null;bool previousRender=layerRenderActive;layerRenderActive=true;
        try
        {
            if(links!=null)
            {
                using var g=Graphics.FromImage(output);g.SmoothingMode=SmoothingMode.AntiAlias;var map=nodes.ToDictionary(n=>n.Id);bool oldExport=canvasExporting;canvasExporting=true;
                try{foreach(var link in links)if(map.TryGetValue(link.From,out var a)&&map.TryGetValue(link.To,out var b)&&!a.Hidden&&!b.Hidden)DrawCanvasLink(g,link,a,b,false,PointF.Empty,link.Label);}finally{canvasExporting=oldExport;}
            }
            foreach(var node in nodes)
            {
                if(!node.ClipMask){baseRaster?.Dispose();baseRaster=null;}
                if(node.Hidden)continue;
                var raster=RenderCanvasLayer(node,new Rectangle(Point.Empty,size),depth);if(raster==null)continue;
                if(node.ClipMask && baseRaster==null){raster.Dispose();continue;}
                if(node.ClipMask)ApplyLayerMask(raster,baseRaster!);
                if(node.Extra.Blend=="normal"){using var g=Graphics.FromImage(output);g.DrawImageUnscaled(raster.Image,raster.Bounds.Location);}
                else BlendLayerRaster(output,raster,node.Extra.Blend);
                if(node.ClipMask)raster.Dispose();else baseRaster=raster;
            }
            return output;
        }
        catch{output.Dispose();throw;}
        finally{baseRaster?.Dispose();layerRenderActive=previousRender;}
    }

    LayerRaster? RenderCanvasLayer(CanvasNode node,Rectangle viewport,int depth)
    {
        var rect=Rectangle.Intersect(LayerScreenBounds(node),viewport);if(rect.Width<1||rect.Height<1)return null;
        var bitmap=new Bitmap(rect.Width,rect.Height,PixelFormat.Format32bppArgb);bitmap.SetResolution(DeviceDpi,DeviceDpi);
        try
        {
            using(var g=Graphics.FromImage(bitmap))
            {
                g.SmoothingMode=SmoothingMode.AntiAlias;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                g.TranslateTransform(-rect.X,-rect.Y);var bounds=CanvasBounds(node);var center=CanvasNodeCenter(bounds);g.TranslateTransform(center.X,center.Y);g.RotateTransform(node.Rotation);g.TranslateTransform(-center.X,-center.Y);
                Color FillAlpha(Color c)=>Color.FromArgb((int)(c.A*Math.Clamp(node.FillOpacity,0,1)),c);
                if(node.Kind=="image")
                {
                    if(node.Extra.PixelArt){g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;}
                    Bitmap? smart=null;
                    try
                    {
                        if(IsSmartLayer(node)&&depth<8)smart=RenderSmartLayer(node,bounds.Size,depth+1);
                        var image=(Image?)smart??node.Bitmap;
                        if(image!=null)
                        {
                            using var attr=new ImageAttributes();attr.SetWrapMode(WrapMode.TileFlipXY);attr.SetColorMatrix(new ColorMatrix{Matrix33=node.FillOpacity});g.DrawImage(image,bounds,0,0,image.Width,image.Height,GraphicsUnit.Pixel,attr);
                        }
                    }
                    finally{ /* Smart previews belong to the bounded render cache. */ }
                }
                else if(node.Kind=="path")
                {
                    using var path=LayerVectorPath(node,bounds);
                    if(node.Extra.Closed && node.Extra.FillPath){using var fill=new SolidBrush(FillAlpha(CanvasNodeFillColor(node)));g.FillPath(fill,path);}
                    using var pen=new Pen(CanvasNodeStrokeColor(node),Math.Max(.2f,node.StrokeWidth*canvasZoom)){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round,DashStyle=node.StrokeDash?DashStyle.Dash:DashStyle.Solid};g.DrawPath(pen,path);
                }
                else if(IsCanvasShapeKind(node))
                {
                    using var path=CanvasShapePath(node,bounds);using var fill=new SolidBrush(FillAlpha(CanvasNodeFillColor(node)));g.FillPath(fill,path);
                    if(node.StrokeWidth>.01f)
                    {
                        using var pen=new Pen(CanvasNodeStrokeColor(node),Math.Max(.1f,node.StrokeWidth*canvasZoom)){LineJoin=LineJoin.Round,DashStyle=node.StrokeDash?DashStyle.Dash:DashStyle.Solid,Alignment=node.StrokeAlign==0?PenAlignment.Inset:PenAlignment.Center};
                        if(node.StrokeAlign==2){var state=g.Save();g.SetClip(path,CombineMode.Exclude);pen.Width*=2;g.DrawPath(pen,path);g.Restore(state);}else g.DrawPath(pen,path);
                    }
                }
                else if(node.Kind=="text")
                {
                    if(!node.Transparent){using var fill=new SolidBrush(FillAlpha(CanvasNodeFillColor(node)));g.FillRectangle(fill,bounds);}
                    if(canvasInlineEditNodeId!=node.Id || canvasExporting)
                    {
                        int pad=Math.Max(1,(int)Math.Round(12*canvasZoom));var textBounds=new Rectangle(bounds.X+pad,bounds.Y+pad,Math.Max(1,bounds.Width-2*pad),Math.Max(1,bounds.Height-2*pad));
                        using var font=CanvasNodeFont(node,canvasZoom);using var brush=new SolidBrush(FillAlpha(CanvasNodeTextColor(node,string.IsNullOrEmpty(node.Content))));
                        using var format=new StringFormat{Alignment=CanvasNodeStringAlignment(node),LineAlignment=CanvasNodeVStringAlignment(node),Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.LineLimit};
                        g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        // Empty labels are editor hints, not pixels in exported or rasterized layers.
                        if(!string.IsNullOrEmpty(node.Content))g.DrawString(node.Content,font,brush,textBounds,format);
                    }
                }
            }
            AdjustLayerPixels(bitmap,node.Extra,node.Opacity);
            return new(bitmap,rect);
        }
        catch{bitmap.Dispose();throw;}
    }

    Bitmap? RenderSmartLayer(CanvasNode node,Size pixels,int depth)
    {
        if(smartRenderCache.TryGetValue(node.Id,out var cached)&&cached.Source==node.Extra.SmartSource&&cached.Pixels==pixels&&cached.Dark==darkTheme&&cached.Dpi==DeviceDpi)return cached.Image;
        var document=JsonSerializer.Deserialize<SmartDocument>(node.Extra.SmartSource,layerJsonOptions);if(document==null)return null;
        var nodes=DecodeSmartLayers(document);if(nodes.Count>0)nodes[0].ClipMask=false;float oldZoom=canvasZoom;var oldOffset=canvasOffset;
        try
        {
            float scale=Math.Min(Math.Max(1,pixels.Width)/Math.Max(1,document.Width),Math.Max(1,pixels.Height)/Math.Max(1,document.Height));
            scale=Math.Min(scale,(float)Math.Sqrt(16000000/Math.Max(1,document.Width*document.Height)));
            scale=Math.Min(scale,Math.Min(8192/Math.Max(1,document.Width),8192/Math.Max(1,document.Height)));
            canvasZoom=scale;canvasOffset=PointF.Empty;
            var links=document.Links?.Select(l=>new CanvasLink{From=l.From,To=l.To,Label=l.Label,Color=l.Color,Arrow=l.Arrow}).ToList();
            var image=RenderLayerComposition(new Size(Math.Max(1,(int)(document.Width*scale)),Math.Max(1,(int)(document.Height*scale))),nodes,links,depth);
            if(smartRenderCache.Remove(node.Id,out var old))old.Image.Dispose();
            while(smartRenderCache.Count>0 && (smartRenderCache.Count>=24 || smartRenderCache.Values.Sum(c=>(long)c.Image.Width*c.Image.Height)+(long)image.Width*image.Height>32000000)){var key=smartRenderCache.Keys.First();smartRenderCache[key].Image.Dispose();smartRenderCache.Remove(key);}
            smartRenderCache[node.Id]=new(node.Extra.SmartSource,pixels,darkTheme,DeviceDpi,image);return image;
        }
        finally{canvasZoom=oldZoom;canvasOffset=oldOffset;foreach(var child in nodes)child.Bitmap?.Dispose();}
    }

    GraphicsPath LayerVectorPath(CanvasNode node,Rectangle bounds)
    {
        var path=new GraphicsPath();var points=node.Extra.Points;if(points.Count==0)return path;
        PointF P(LayerPathPoint p)=>new(bounds.Left+p.X*bounds.Width,bounds.Top+p.Y*bounds.Height);
        if(points.Count==1){var p=P(points[0]);path.AddLine(p,new PointF(p.X+.01f,p.Y));return path;}
        if(node.Extra.Smooth){path.AddCurve(points.Select(P).ToArray(),.35f);}
        else
        {
            for(int i=1;i<points.Count;i++)Add(points[i-1],points[i]);
            if(node.Extra.Closed)Add(points[^1],points[0]);
        }
        if(node.Extra.Closed)path.CloseFigure();return path;
        void Add(LayerPathPoint a,LayerPathPoint b)=>path.AddBezier(P(a),new PointF(bounds.Left+(a.X+a.OutX)*bounds.Width,bounds.Top+(a.Y+a.OutY)*bounds.Height),new PointF(bounds.Left+(b.X+b.InX)*bounds.Width,bounds.Top+(b.Y+b.InY)*bounds.Height),P(b));
    }

    static float BlendChannel(float b,float s,string mode)=>mode switch {
        "multiply"=>b*s,"screen"=>b+s-b*s,"overlay"=>b<=.5f?2*b*s:1-2*(1-b)*(1-s),"darken"=>Math.Min(b,s),"lighten"=>Math.Max(b,s),
        "hard-light"=>s<=.5f?2*b*s:1-2*(1-b)*(1-s),"soft-light"=>s<=.5f?b-(1-2*s)*b*(1-b):b+(2*s-1)*((b<=.25f?((16*b-12)*b+4)*b:(float)Math.Sqrt(b))-b),
        "color-dodge"=>b<=0?0:s>=1?1:Math.Min(1,b/(1-s)),"color-burn"=>b>=1?1:s<=0?0:1-Math.Min(1,(1-b)/s),"difference"=>Math.Abs(b-s),"exclusion"=>b+s-2*b*s,"add"=>Math.Min(1,b+s),"subtract"=>Math.Max(0,b-s),_=>s };
    static byte PixelByte(float x)=>(byte)Math.Clamp((int)Math.Round(x*255),0,255);

    static unsafe void BlendLayerRaster(Bitmap destination,LayerRaster source,string mode)
    {
        var dst=destination.LockBits(source.Bounds,ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);var src=source.Image.LockBits(new Rectangle(Point.Empty,source.Image.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try
        {
            for(int y=0;y<source.Image.Height;y++)
            {
                byte* d=(byte*)dst.Scan0+y*dst.Stride;byte* s=(byte*)src.Scan0+y*src.Stride;
                for(int x=0;x<source.Image.Width;x++,d+=4,s+=4)
                {
                    float a=s[3]/255f;if(a==0)continue;float b=d[3]/255f,outA=a+b*(1-a);
                    for(int c=0;c<3;c++){float sc=s[c]/255f,bc=d[c]/255f;d[c]=PixelByte(((1-a)*b*bc+(1-b)*a*sc+a*b*BlendChannel(bc,sc,mode))/outA);}d[3]=PixelByte(outA);
                }
            }
        }
        finally{destination.UnlockBits(dst);source.Image.UnlockBits(src);}
    }

    static unsafe void ApplyLayerMask(LayerRaster layer,LayerRaster mask)
    {
        var data=layer.Image.LockBits(new Rectangle(Point.Empty,layer.Image.Size),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);var baseData=mask.Image.LockBits(new Rectangle(Point.Empty,mask.Image.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try
        {
            for(int y=0;y<layer.Image.Height;y++)
            {
                byte* p=(byte*)data.Scan0+y*data.Stride;int my=layer.Bounds.Y+y-mask.Bounds.Y;
                for(int x=0;x<layer.Image.Width;x++,p+=4){int mx=layer.Bounds.X+x-mask.Bounds.X;int alpha=mx<0||my<0||mx>=mask.Image.Width||my>=mask.Image.Height?0:*((byte*)baseData.Scan0+my*baseData.Stride+mx*4+3);p[3]=(byte)(p[3]*alpha/255);}
            }
        }
        finally{layer.Image.UnlockBits(data);mask.Image.UnlockBits(baseData);}
    }

    static unsafe void AdjustLayerPixels(Bitmap bitmap,LayerData extra,float opacity)
    {
        bool adjust=extra.Brightness!=0||extra.Contrast!=0||extra.Saturation!=0||extra.Hue!=0||extra.Red!=0||extra.Green!=0||extra.Blue!=0;
        if(!adjust&&opacity>=1)return;
        var data=bitmap.LockBits(new Rectangle(Point.Empty,bitmap.Size),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
        try
        {
            float contrast=(100+extra.Contrast)/100f;contrast*=contrast;
            for(int y=0;y<bitmap.Height;y++)
            {
                byte* p=(byte*)data.Scan0+y*data.Stride;
                for(int x=0;x<bitmap.Width;x++,p+=4)
                {
                    if(p[3]==0)continue;p[3]=PixelByte(p[3]/255f*Math.Clamp(opacity,0,1));if(!adjust)continue;
                    float r=p[2]/255f,g=p[1]/255f,b=p[0]/255f;
                    if(extra.Hue!=0||extra.Saturation!=0)AdjustLayerHsl(ref r,ref g,ref b,extra.Hue,extra.Saturation);
                    float bright=extra.Brightness/100f;
                    p[2]=PixelByte((r-.5f)*contrast+.5f+bright+extra.Red/100);p[1]=PixelByte((g-.5f)*contrast+.5f+bright+extra.Green/100);p[0]=PixelByte((b-.5f)*contrast+.5f+bright+extra.Blue/100);
                }
            }
        }
        finally{bitmap.UnlockBits(data);}
    }

    static void AdjustLayerHsl(ref float r,ref float g,ref float b,float hue,float saturation)
    {
        float max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),delta=max-min,l=(max+min)/2,s=delta==0?0:delta/(1-Math.Abs(2*l-1)),h=0;
        if(delta>0)h=(max==r?(g-b)/delta+(g<b?6:0):max==g?(b-r)/delta+2:(r-g)/delta+4)/6;
        h=((h+hue/360)%1+1)%1;s=Math.Clamp(saturation<0?s*(1+saturation/100):s+(1-s)*saturation/100,0,1);
        if(s==0){r=g=b=l;return;}
        float q=l<.5f?l*(1+s):l+s-l*s,p=2*l-q;
        r=Channel(h+1/3f);g=Channel(h);b=Channel(h-1/3f);
        float Channel(float t){t=(t+1)%1;return t<1/6f?p+(q-p)*6*t:t<.5f?q:t<2/3f?p+(q-p)*(2/3f-t)*6:p;}
    }
}
