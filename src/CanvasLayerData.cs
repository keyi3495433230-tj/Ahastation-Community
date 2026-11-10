using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;

namespace CardReview;

public sealed partial class ReviewForm
{
    sealed class LayerPathPoint
    {
        public float X { get; set; } public float Y { get; set; }
        public float InX { get; set; } public float InY { get; set; }
        public float OutX { get; set; } public float OutY { get; set; }
        public bool Broken { get; set; }
    }
    sealed class LayerData
    {
        public string FontFamily { get; set; } = "";
        public string Blend { get; set; } = "normal";
        public int Label { get; set; }
        public float Brightness { get; set; } public float Contrast { get; set; }
        public float Saturation { get; set; } public float Hue { get; set; }
        public float Red { get; set; } public float Green { get; set; } public float Blue { get; set; }
        public List<LayerPathPoint> Points { get; set; } = new();
        public bool Closed { get; set; } public bool FillPath { get; set; }
        public bool Smooth { get; set; }
        public bool PixelArt { get; set; }
        public string SmartSource { get; set; } = "";
    }
    sealed record SmartLayerSource(CanvasNodeState State, string Image);
    sealed record SmartSourceLink(Guid From,Guid To,string Label,string Color,int Arrow);
    sealed record SmartDocument(float Width, float Height, List<SmartLayerSource> Layers,List<SmartSourceLink>? Links=null);
    static readonly JsonSerializerOptions layerJsonOptions = new() { IncludeFields = true, MaxDepth = 64 };
    static string EncodeLayerData(LayerData data) => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data, layerJsonOptions)));
    static LayerData DecodeLayerData(string? value)
    {
        try
        {
            var data = string.IsNullOrEmpty(value) ? new LayerData() : JsonSerializer.Deserialize<LayerData>(Encoding.UTF8.GetString(Convert.FromBase64String(value)), layerJsonOptions) ?? new();
            data.Points ??= new(); data.Blend ??= "normal"; data.FontFamily ??= ""; data.SmartSource ??= "";
            data.Label=Math.Clamp(data.Label,0,4); return data;
        }
        catch(Exception ex) when(ex is JsonException or FormatException or ArgumentException){return new();}
    }
    static LayerData CopyLayerData(LayerData data) => DecodeLayerData(EncodeLayerData(data));
    static bool IsSmartLayer(CanvasNode node) => !string.IsNullOrEmpty(node.Extra.SmartSource);
    static Color LayerLabelColor(int label) => label switch { 1=>Color.FromArgb(239,83,80),2=>Color.FromArgb(255,152,0),3=>Color.FromArgb(234,196,48),4=>Color.FromArgb(76,175,80),_=>Color.Transparent };
    static readonly (string Id,string Name)[] layerBlendModes = {
        ("normal","正常"),("multiply","正片叠底"),("screen","滤色"),("overlay","叠加"),("darken","变暗"),("lighten","变亮"),
        ("soft-light","柔光"),("hard-light","强光"),("color-dodge","颜色减淡"),("color-burn","颜色加深"),("difference","差值"),("exclusion","排除"),("add","线性减淡（添加）"),("subtract","减去") };

    void ApplyLayerDataChange(Action<CanvasNode> change, bool includeLocked=false)
    {
        var nodes=CanvasSelectedNodes().Where(n=>includeLocked || !n.Locked).ToList();if(nodes.Count==0)return;
        PushCanvasUndo();foreach(var node in nodes)change(node);SaveCanvas();canvasSurface.Invalidate();nativeLayerKey="";
    }

    CanvasNode NodeFromState(CanvasNodeState n, bool image=true) => new() {
        Id=n.Id,Kind=n.Kind,Content=n.Content,Position=n.Position,Size=n.Size,Transparent=n.Transparent,GroupId=n.GroupId,VectorAnchors=n.VectorAnchors.ToList(),Rotation=n.Rotation,
        Locked=n.Locked,Hidden=n.Hidden,Name=n.Name,FontSize=n.FontSize,FontBold=n.FontBold,FontItalic=n.FontItalic,FontStrikeout=n.FontStrikeout,FontAlign=n.FontAlign,FontVAlign=n.FontVAlign,
        FontColor=n.FontColor,FillColor=n.FillColor,Opacity=n.Opacity,FillOpacity=n.FillOpacity,CornerRadius=n.CornerRadius,ClipMask=n.ClipMask,StrokeWidth=n.StrokeWidth,StrokeColor=n.StrokeColor,
        StrokeAlign=n.StrokeAlign,StrokeDash=n.StrokeDash,AutoLayout=n.AutoLayout,AutoLayoutDirection=n.AutoLayoutDirection,AutoLayoutGap=n.AutoLayoutGap,AutoLayoutPadding=n.AutoLayoutPadding,
        ComponentId=n.ComponentId,ComponentInstanceId=n.ComponentInstanceId,ComponentMasterNodeId=n.ComponentMasterNodeId,Extra=DecodeLayerData(n.Extra),Bitmap=image && n.Kind=="image"?LoadCanvasNodeImage(n.Id):null
    };

    static string ImageToBase64(Image? bitmap)
    {
        if(bitmap==null)return "";using var stream=new MemoryStream();bitmap.Save(stream,ImageFormat.Png);return Convert.ToBase64String(stream.ToArray());
    }
    static Bitmap? ImageFromBase64(string data)
    {
        if(string.IsNullOrEmpty(data))return null;
        using var stream=new MemoryStream(Convert.FromBase64String(data));using var image=Image.FromStream(stream);return new Bitmap(image);
    }

    List<CanvasNode> DecodeSmartLayers(SmartDocument document)
    {
        var nodes=new List<CanvasNode>();
        try{foreach(var source in document.Layers){var node=NodeFromState(source.State,false);node.Bitmap=ImageFromBase64(source.Image);nodes.Add(node);}return nodes;}
        catch{foreach(var node in nodes)node.Bitmap?.Dispose();throw;}
    }

    RectangleF LayerWorldBounds(CanvasNode node)
    {
        var center=new PointF(node.Position.X+node.Size.Width/2f,node.Position.Y+node.Size.Height/2f);
        var points=new List<PointF>{node.Position,new(node.Position.X+node.Size.Width,node.Position.Y),new(node.Position.X+node.Size.Width,node.Position.Y+node.Size.Height),new(node.Position.X,node.Position.Y+node.Size.Height)};
        foreach(var p in node.Extra.Points)
        {
            points.Add(new(node.Position.X+p.X*node.Size.Width,node.Position.Y+p.Y*node.Size.Height));
            points.Add(new(node.Position.X+(p.X+p.InX)*node.Size.Width,node.Position.Y+(p.Y+p.InY)*node.Size.Height));
            points.Add(new(node.Position.X+(p.X+p.OutX)*node.Size.Width,node.Position.Y+(p.Y+p.OutY)*node.Size.Height));
        }
        if(node.VectorAnchors.Count>=3)points.AddRange(node.VectorAnchors.Select(p=>new PointF(node.Position.X+p.X*node.Size.Width,node.Position.Y+p.Y*node.Size.Height)));
        if(Math.Abs(node.Rotation)>.01f)points=points.Select(p=>RotateCanvasPoint(p,center,node.Rotation)).ToList();
        var result=RectangleF.FromLTRB(points.Min(p=>p.X),points.Min(p=>p.Y),points.Max(p=>p.X),points.Max(p=>p.Y));
        float pad=Math.Max(2,node.StrokeWidth+2);result.Inflate(pad,pad);return result;
    }

    (Bitmap Image,RectangleF Bounds) BakeCanvasLayers(IReadOnlyList<CanvasNode> nodes)
    {
        float oldZoom=canvasZoom;var oldOffset=canvasOffset;
        bool clip=nodes[0].ClipMask;
        try
        {
            canvasZoom=1;canvasOffset=PointF.Empty;
            var bounds=nodes.Select(LayerWorldBounds).Aggregate(RectangleF.Union);
            var map=nodes.ToDictionary(n=>n.Id);
            var links=canvasLinks.Where(l=>map.ContainsKey(l.From)&&map.ContainsKey(l.To)).ToList();
            foreach(var link in links)bounds=RectangleF.Union(bounds,CanvasLinkBounds(link,map[link.From],map[link.To]));
            bounds=RectangleF.FromLTRB((float)Math.Floor(bounds.Left),(float)Math.Floor(bounds.Top),(float)Math.Ceiling(bounds.Right),(float)Math.Ceiling(bounds.Bottom));
            float scale=Math.Min(1,Math.Min(8192/Math.Max(1,bounds.Width),8192/Math.Max(1,bounds.Height)));
            scale=Math.Min(scale,(float)Math.Sqrt(16000000/Math.Max(1,bounds.Width*bounds.Height)));
            nodes[0].ClipMask=false;canvasZoom=scale;canvasOffset=new(-bounds.X*scale,-bounds.Y*scale);
            return(RenderLayerComposition(new Size(Math.Max(1,(int)Math.Ceiling(bounds.Width*scale)),Math.Max(1,(int)Math.Ceiling(bounds.Height*scale))),nodes,links,0),bounds);
        }
        finally{nodes[0].ClipMask=clip;canvasZoom=oldZoom;canvasOffset=oldOffset;}
    }

    void ReplaceCanvasLayers(IReadOnlyList<CanvasNode> old,CanvasNode replacement)
    {
        var ids=old.Select(n=>n.Id).ToHashSet();int at=canvasNodes.FindLastIndex(n=>ids.Contains(n.Id));
        int insert=canvasNodes.Take(at+1).Count(n=>!ids.Contains(n.Id));
        canvasNodes.RemoveAll(n=>ids.Contains(n.Id));canvasNodes.Insert(Math.Clamp(insert,0,canvasNodes.Count),replacement);
        foreach(var link in canvasLinks){if(ids.Contains(link.From))link.From=replacement.Id;if(ids.Contains(link.To))link.To=replacement.Id;}
        canvasLinks.RemoveAll(l=>l.From==l.To);
        foreach(var node in old)if(!ReferenceEquals(node.Bitmap,replacement.Bitmap))node.Bitmap?.Dispose();
        if(replacement.Bitmap!=null)SaveCanvasNodeImage(replacement);
        SetCanvasSelection(new[]{replacement});SaveCanvas();canvasSurface.Invalidate();nativeLayerKey="";
    }

    void RasterizeCanvasLayers(bool merge)
    {
        CommitCanvasInlineEdit();var selected=canvasNodes.Where(n=>canvasSelectedIds.Contains(n.Id)&&!n.Locked&&!n.Hidden).ToList();if(selected.Count==0)return;
        var batches=merge?new List<List<CanvasNode>>{selected}:selected.Select(n=>new List<CanvasNode>{n}).ToList();
        // Prepare every raster before modifying the document; an allocation failure leaves the source untouched.
        var prepared=new List<(List<CanvasNode> Old,CanvasNode New)>();
        try
        {
            foreach(var batch in batches)
            {
                var (image,bounds)=BakeCanvasLayers(batch);var first=batch[0];
                var node=new CanvasNode{Kind="image",Name=merge?"合并图层":(string.IsNullOrWhiteSpace(first.Name)?"像素图层":first.Name),Position=bounds.Location,Size=new Size((int)bounds.Width,(int)bounds.Height),Bitmap=image,ClipMask=first.ClipMask,Extra=new LayerData{Label=first.Extra.Label,Blend=first.Extra.Blend}};
                prepared.Add((batch,node));
            }
            PushCanvasUndo();foreach(var item in prepared)ReplaceCanvasLayers(item.Old,item.New);
            SetCanvasSelection(prepared.Select(p=>p.New));
        }
        catch{foreach(var item in prepared)if(!canvasNodes.Contains(item.New))item.New.Bitmap?.Dispose();throw;}
    }

    void ConvertCanvasSmartLayer()
    {
        CommitCanvasInlineEdit();var nodes=canvasNodes.Where(n=>canvasSelectedIds.Contains(n.Id)&&!n.Locked&&!n.Hidden).ToList();if(nodes.Count==0)return;
        if(nodes.Count==1&&IsSmartLayer(nodes[0]))return;
        var (bitmap,bounds)=BakeCanvasLayers(nodes);
        var states=CaptureCanvasState().Nodes.Where(n=>nodes.Any(x=>x.Id==n.Id)).Select(n=>new SmartLayerSource(n with{Position=new PointF(n.Position.X-bounds.X,n.Position.Y-bounds.Y)},ImageToBase64(nodes.First(x=>x.Id==n.Id).Bitmap))).ToList();
        var ids=nodes.Select(n=>n.Id).ToHashSet();var links=canvasLinks.Where(l=>ids.Contains(l.From)&&ids.Contains(l.To)).Select(l=>new SmartSourceLink(l.From,l.To,l.Label,l.Color,l.Arrow)).ToList();
        var document=new SmartDocument(bounds.Width,bounds.Height,states,links);
        var smart=new CanvasNode{Kind="image",Name="智能图层",Position=bounds.Location,Size=new Size((int)bounds.Width,(int)bounds.Height),Bitmap=bitmap,ClipMask=nodes[0].ClipMask,Extra=new LayerData{SmartSource=JsonSerializer.Serialize(document,layerJsonOptions),Label=nodes[0].Extra.Label,Blend=nodes.Count==1?nodes[0].Extra.Blend:"normal"}};
        PushCanvasUndo();ReplaceCanvasLayers(nodes,smart);
    }

    void RestoreCanvasSmartSource(CanvasNode node)
    {
        if(node.Locked||!IsSmartLayer(node))return;
        var document=JsonSerializer.Deserialize<SmartDocument>(node.Extra.SmartSource,layerJsonOptions);if(document==null)return;
        var restored=DecodeSmartLayers(document);if(restored.Count==0)return;
        float sx=node.Size.Width/Math.Max(1,document.Width),sy=node.Size.Height/Math.Max(1,document.Height);
        var center=new PointF(node.Position.X+node.Size.Width/2f,node.Position.Y+node.Size.Height/2f);var groups=new Dictionary<Guid,Guid>();var ids=new Dictionary<Guid,Guid>();
        foreach(var child in restored)
        {
            var oldId=child.Id;child.Id=Guid.NewGuid();ids[oldId]=child.Id;child.Locked=false;
            var localCenter=new PointF(node.Position.X+(child.Position.X+child.Size.Width/2f)*sx,node.Position.Y+(child.Position.Y+child.Size.Height/2f)*sy);
            var mapped=RotateCanvasPoint(localCenter,center,node.Rotation);child.Size=new Size(Math.Max(1,(int)Math.Round(child.Size.Width*sx)),Math.Max(1,(int)Math.Round(child.Size.Height*sy)));
            child.Position=new(mapped.X-child.Size.Width/2f,mapped.Y-child.Size.Height/2f);child.Rotation+=node.Rotation;child.Opacity*=node.Opacity;
            float styleScale=Math.Min(sx,sy);if(child.FontSize<=0)child.FontSize=10;
            child.FontSize*=styleScale;child.StrokeWidth*=styleScale;child.CornerRadius*=styleScale;
            if(child.GroupId is Guid group){if(!groups.ContainsKey(group))groups[group]=Guid.NewGuid();child.GroupId=groups[group];}
            if(child.Bitmap!=null)SaveCanvasNodeImage(child);
        }
        PushCanvasUndo();int at=canvasNodes.IndexOf(node);canvasNodes.Remove(node);canvasNodes.InsertRange(at,restored);
        foreach(var link in canvasLinks){if(link.From==node.Id)link.From=restored[0].Id;if(link.To==node.Id)link.To=restored[0].Id;}
        if(document.Links!=null)foreach(var link in document.Links)if(ids.TryGetValue(link.From,out var from)&&ids.TryGetValue(link.To,out var to))canvasLinks.Add(new CanvasLink{From=from,To=to,Label=link.Label,Color=link.Color,Arrow=link.Arrow});
        node.Bitmap?.Dispose();SetCanvasSelection(restored);SaveCanvas();nativeLayerKey="";canvasSurface.Invalidate();
    }
}
