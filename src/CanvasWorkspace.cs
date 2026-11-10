using System.Drawing.Drawing2D;
using System.Text.Json;

namespace CardReview;

public sealed partial class ReviewForm
{
    enum CanvasTool { Select, Hand, Rectangle, Ellipse, Text, Connect, Brush, VectorBrush, Pen, Eraser, PixelBrush }
    sealed class CanvasWorkspacePreferences
    {
        public bool Layers { get; set; } = true;
        public bool Properties { get; set; } = true;
        public double LayersWidth { get; set; } = 204;
        public double PropertiesWidth { get; set; } = 260;
        public bool Grid { get; set; } = true;
        public bool SmartSnap { get; set; } = true;
        public bool GridSnap { get; set; }
        public float BrushSize { get; set; }=12;
        public float BrushOpacity { get; set; }=1;
        public float BrushHardness { get; set; }=1;
        public int BrushColor { get; set; }=unchecked((int)0xff376fda);
        public int PixelBrushMode { get; set; }
        public float PixelDensity { get; set; }=50;
    }
    CanvasWorkspacePreferences canvasWorkspace = new();
    CanvasTool canvasTool;
    bool canvasDrawing, canvasFocusMode, canvasPreferProperties;
    Point canvasDrawStart, canvasDrawEnd;
    CanvasNode? canvasResizeNode;
    int canvasResizeHandle = -1;
    PointF canvasResizeStartPosition;
    Size canvasResizeStartSize;
    bool canvasResizeChanged;
    CanvasState[]? canvasResizeRedo;
    readonly Color canvasSelectionColor = Color.FromArgb(13, 153, 255);
    string CanvasWorkspacePath => Path.Combine(AppDataStorage.Root, "canvas-workspace.json");

    void LoadCanvasWorkspace()
    {
        try { if (File.Exists(CanvasWorkspacePath)) canvasWorkspace = JsonSerializer.Deserialize<CanvasWorkspacePreferences>(File.ReadAllText(CanvasWorkspacePath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        canvasWorkspace.LayersWidth = double.IsFinite(canvasWorkspace.LayersWidth) ? Math.Clamp(canvasWorkspace.LayersWidth, 170, 340) : 204;
        canvasWorkspace.PropertiesWidth = double.IsFinite(canvasWorkspace.PropertiesWidth) ? Math.Clamp(canvasWorkspace.PropertiesWidth, 240, 380) : 260;
        nativeLayersOpen = canvasWorkspace.Layers; nativePropertiesOpen = canvasWorkspace.Properties;
        canvasBrushSize=float.IsFinite(canvasWorkspace.BrushSize)?Math.Clamp(canvasWorkspace.BrushSize,1,300):12;
        canvasBrushOpacity=float.IsFinite(canvasWorkspace.BrushOpacity)?Math.Clamp(canvasWorkspace.BrushOpacity,.01f,1):1;
        canvasBrushHardness=float.IsFinite(canvasWorkspace.BrushHardness)?Math.Clamp(canvasWorkspace.BrushHardness,0,1):1;
        canvasBrushColor=Color.FromArgb(canvasWorkspace.BrushColor);
        canvasPixelBrushMode=Math.Clamp(canvasWorkspace.PixelBrushMode,0,pixelBrushModes.Length-1);
        canvasPixelDensity=float.IsFinite(canvasWorkspace.PixelDensity)?Math.Clamp(canvasWorkspace.PixelDensity,1,100):50;
    }

    void SaveCanvasWorkspace()
    {
        canvasWorkspace.Layers = nativeLayersOpen; canvasWorkspace.Properties = nativePropertiesOpen;
        canvasWorkspace.PixelBrushMode=canvasPixelBrushMode;canvasWorkspace.PixelDensity=canvasPixelDensity;
        canvasWorkspace.BrushSize=canvasBrushSize;canvasWorkspace.BrushOpacity=canvasBrushOpacity;canvasWorkspace.BrushHardness=canvasBrushHardness;canvasWorkspace.BrushColor=canvasBrushColor.ToArgb();
        try
        {
            Directory.CreateDirectory(AppDataStorage.Root);
            File.WriteAllText(CanvasWorkspacePath + ".tmp", JsonSerializer.Serialize(canvasWorkspace));
            File.Move(CanvasWorkspacePath + ".tmp", CanvasWorkspacePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    void SetCanvasTool(CanvasTool tool)
    {
        if(tool!=canvasTool && canvasTool==CanvasTool.Pen && canvasPenPoints.Count>=2)FinishCanvasPen(false,false);
        CommitCanvasInlineEdit(); CancelCanvasWorkspaceGesture();CancelCanvasPaint();
        canvasTool = tool; canvasVectorEditNodeId = null;
        UpdateCanvasCursor(); SyncCanvasWorkspaceUi(); canvasSurface.Focus(); canvasSurface.Invalidate();
    }

    void CancelCanvasWorkspaceGesture()
    {
        canvasDrawing = false;
        if (canvasResizeNode != null && canvasResizeChanged)
        {
            canvasResizeNode.Position = canvasResizeStartPosition; canvasResizeNode.Size = canvasResizeStartSize;
            if (canvasUndo.Count > 0) canvasUndo.Pop();
            canvasRedo.Clear();if(canvasResizeRedo!=null)foreach(var state in canvasResizeRedo.Reverse())canvasRedo.Push(state);
            InvalidateCanvasPaintCache(); SaveCanvas();
        }
        canvasResizeNode = null; canvasResizeHandle = -1; canvasResizeChanged = false;
        canvasResizeRedo = null;
        canvasPanning = false;
        canvasSurface.Capture = false;
    }

    PointF CanvasWorldPoint(Point point) => new((point.X - canvasOffset.X) / canvasZoom, (point.Y - canvasOffset.Y) / canvasZoom);
    Point CanvasViewportCenter => new(canvasSurface.Width / 2, canvasSurface.Height / 2);

    void ZoomCanvasAt(float zoom, Point anchor)
    {
        zoom = Math.Clamp(zoom, .1f, 8f);
        if (Math.Abs(zoom - canvasZoom) < .0001f) return;
        CommitCanvasInlineEdit();
        var world = CanvasWorldPoint(anchor);
        canvasZoom = zoom; canvasOffset = new(anchor.X - world.X * zoom, anchor.Y - world.Y * zoom);
        CanvasViewportChanged();
    }

    void CanvasViewportChanged()
    {
        InvalidateCanvasPaintCache(); canvasSurface.Invalidate();
        canvasSaveTimer.Stop(); canvasSaveTimer.Start(); SyncCanvasWorkspaceUi();
    }

    void FitCanvasView(bool selection)
    {
        CommitCanvasInlineEdit();
        var nodes = (selection ? CanvasSelectedNodes() : canvasNodes).Where(n => !n.Hidden).ToList();
        if (nodes.Count == 0) { if (!selection) { canvasZoom = 1; canvasOffset = PointF.Empty; CanvasViewportChanged(); } return; }
        // Use visual bounds, including rotated nodes, in the current viewport before changing zoom.
        var bounds = nodes.Select(CanvasVisualBounds).Aggregate(Rectangle.Union);
        var worldCenter = CanvasWorldPoint(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2));
        float zoom = Math.Clamp(Math.Min(Math.Max(80, canvasSurface.Width - 96) / (bounds.Width / canvasZoom), Math.Max(80, canvasSurface.Height - 96) / (bounds.Height / canvasZoom)), .1f, 4f);
        canvasZoom = zoom;
        canvasOffset = new(canvasSurface.Width / 2f - worldCenter.X * zoom, canvasSurface.Height / 2f - worldCenter.Y * zoom);
        CanvasViewportChanged();
    }

    bool CanvasWorkspaceKey(Keys key)
    {
        if (!organizeSelected || nativePopupDepth > 0 || canvasInlineEditor.Visible) return false;
        if(CanvasPaintKey(key))return true;
        if(key==(Keys.Control|Keys.D)){RepeatCanvasTransform();return true;}
        if(key==(Keys.Control|Keys.F)){PasteCanvasInPlace(false);return true;}
        if(key==(Keys.Control|Keys.B)){PasteCanvasInPlace(true);return true;}
        if(key==(Keys.Control|Keys.Shift|Keys.V)){PasteCanvasInPlace(false);return true;}
        if (key == Keys.Escape && (canvasDrawing || canvasResizeNode != null || canvasTool != CanvasTool.Select)) { SetCanvasTool(CanvasTool.Select); return true; }
        if (key == (Keys.Shift | Keys.D1)) { FitCanvasView(false); return true; }
        if (key == (Keys.Shift | Keys.D2)) { FitCanvasView(true); return true; }
        if (key == (Keys.Shift | Keys.D0) || key == (Keys.Control | Keys.D0)) { ZoomCanvasAt(1, CanvasViewportCenter); return true; }
        if (key is Keys.Oemplus or Keys.Add || key == (Keys.Control | Keys.Oemplus) || key == (Keys.Control | Keys.Add)) { ZoomCanvasAt(canvasZoom * 1.2f, CanvasViewportCenter); return true; }
        if (key is Keys.OemMinus or Keys.Subtract || key == (Keys.Control | Keys.OemMinus) || key == (Keys.Control | Keys.Subtract)) { ZoomCanvasAt(canvasZoom / 1.2f, CanvasViewportCenter); return true; }
        if (key == (Keys.Control | Keys.G)) { GroupCanvasWorkspace(false); return true; }
        if (key == (Keys.Control | Keys.Shift | Keys.G)) { GroupCanvasWorkspace(true); return true; }
        if (key == (Keys.Shift | Keys.A)) { SetCanvasAutoLayout(CanvasSelectedNodes().Where(n => !n.Locked).ToList(), 0); return true; }
        if (key == Keys.Tab) { canvasFocusMode = !canvasFocusMode; NativeResponsiveLayout(); return true; }
        if (key == Keys.F2 && selectedCanvasNode != null) { RenameCanvasLayer(selectedCanvasNode); return true; }
        if (key == (Keys.Control | Keys.S)) { CommitCanvasInlineEdit(); SaveCanvas(); return true; }
        CanvasTool? tool = key switch { Keys.V => CanvasTool.Select, Keys.H => CanvasTool.Hand, Keys.R => CanvasTool.Rectangle, Keys.O => CanvasTool.Ellipse, Keys.T => CanvasTool.Text, Keys.L => CanvasTool.Connect, _ => null };
        if (tool.HasValue) { SetCanvasTool(tool.Value); return true; }
        return false;
    }

    void GroupCanvasWorkspace(bool ungroup)
    {
        var nodes = CanvasSelectedNodes().Where(n => !n.Locked).ToList();
        if (nodes.Count == 0 || (!ungroup && nodes.Count < 2)) return;
        PushCanvasUndo(); Guid? group = ungroup ? null : Guid.NewGuid();
        foreach (var node in nodes) { node.GroupId = group; if (ungroup) node.AutoLayout = false; }
        SaveCanvas(); nativeLayerKey = ""; canvasSurface.Invalidate();
    }

    bool CanvasWorkspaceMouseDown(MouseEventArgs e)
    {
        if(canvasInlineEditor.Visible && !canvasInlineEditor.Bounds.Contains(e.Location))CommitCanvasInlineEdit();
        canvasSurface.Focus();
        if (e.Button != MouseButtons.Left) return false;
        if (canvasSpaceDown || canvasTool == CanvasTool.Hand)
        {
            canvasPanning = true; canvasPanLast = e.Location; canvasSurface.Capture = true; UpdateCanvasCursor(); return true;
        }
        if(CanvasPaintMouseDown(e))return true;
        if (canvasTool is CanvasTool.Rectangle or CanvasTool.Ellipse or CanvasTool.Text)
        {
            canvasDrawing = true; canvasDrawStart = canvasDrawEnd = e.Location; canvasSurface.Capture = true; canvasSurface.Invalidate(); return true;
        }
        if (canvasTool == CanvasTool.Connect)
        {
            var node = CanvasNodeAt(e.Location);
            if (node != null)
            {
                SetCanvasSelection(new[] { node }); wireStartNode = node; wireStartPoint = e.Location;
                wireEnd = e.Location; wireTargetNode = null; wireStartedAtOutput = true;
                wireButton = MouseButtons.Left; canvasWiring = true; canvasSurface.Capture = true;
            }
            return true;
        }
        if (canvasSelectedIds.Count != 1 || selectedCanvasNode is not { Locked: false } selected || canvasVectorEditNodeId.HasValue) return false;
        int handle = CanvasWorkspaceHandleAt(selected, e.Location);
        if (handle < 0) return false;
        if (handle == 8)
        {
            var center = CanvasNodeCenter(CanvasBounds(selected));
            canvasRotationStartAngle = (float)(Math.Atan2(e.Y - center.Y, e.X - center.X) * 180 / Math.PI);
            canvasNodeStartRotation = selected.Rotation; canvasNodeRotating = true; canvasNodeChanged = canvasNodeHistoryPushed = false;
        }
        else
        {
            canvasResizeNode = selected; canvasResizeHandle = handle; canvasMouseDown = e.Location;
            canvasResizeStartPosition = selected.Position; canvasResizeStartSize = selected.Size; canvasResizeChanged = false;
        }
        canvasSurface.Capture = true; return true;
    }

    Rectangle CanvasDrawingBounds()
    {
        int dx = canvasDrawEnd.X - canvasDrawStart.X, dy = canvasDrawEnd.Y - canvasDrawStart.Y;
        if ((ModifierKeys & Keys.Shift) != 0 && canvasTool != CanvasTool.Text)
        {
            int side = Math.Max(Math.Abs(dx), Math.Abs(dy)); dx = (dx < 0 ? -1 : 1) * side; dy = (dy < 0 ? -1 : 1) * side;
        }
        return new(Math.Min(canvasDrawStart.X, canvasDrawStart.X + dx), Math.Min(canvasDrawStart.Y, canvasDrawStart.Y + dy), Math.Abs(dx), Math.Abs(dy));
    }

    bool CanvasWorkspaceMouseMove(MouseEventArgs e)
    {
        if(CanvasPaintMouseMove(e))return true;
        if (canvasDrawing) { canvasDrawEnd = e.Location; canvasSurface.Invalidate(); return true; }
        if (canvasResizeNode is { } node)
        {
            var delta = RotateCanvasVector(new PointF((e.X - canvasMouseDown.X) / canvasZoom, (e.Y - canvasMouseDown.Y) / canvasZoom), -node.Rotation);
            bool left = canvasResizeHandle is 0 or 6 or 7, right = canvasResizeHandle is 2 or 3 or 4;
            bool top = canvasResizeHandle is 0 or 1 or 2, bottom = canvasResizeHandle is 4 or 5 or 6;
            float width = Math.Max(20, canvasResizeStartSize.Width + (left ? -delta.X : right ? delta.X : 0));
            float height = Math.Max(20, canvasResizeStartSize.Height + (top ? -delta.Y : bottom ? delta.Y : 0));
            if ((ModifierKeys & Keys.Shift) != 0 && (left || right) && (top || bottom))
            {
                float scale = Math.Abs(width / canvasResizeStartSize.Width - 1) > Math.Abs(height / canvasResizeStartSize.Height - 1) ? width / canvasResizeStartSize.Width : height / canvasResizeStartSize.Height;
                scale = Math.Max(scale, Math.Max(20f / canvasResizeStartSize.Width, 20f / canvasResizeStartSize.Height));
                width = canvasResizeStartSize.Width * scale; height = canvasResizeStartSize.Height * scale;
            }
            var size = new Size((int)Math.Round(width), (int)Math.Round(height));
            if (!canvasResizeChanged && size == canvasResizeStartSize) return true;
            if (!canvasResizeChanged) { canvasResizeRedo=canvasRedo.ToArray(); PushCanvasUndo(); canvasResizeChanged = true; }
            var centerDelta = RotateCanvasVector(new PointF((size.Width - canvasResizeStartSize.Width) * (left ? -.5f : right ? .5f : 0), (size.Height - canvasResizeStartSize.Height) * (top ? -.5f : bottom ? .5f : 0)), node.Rotation);
            node.Size = size;
            node.Position = new(canvasResizeStartPosition.X + canvasResizeStartSize.Width / 2f + centerDelta.X - size.Width / 2f, canvasResizeStartPosition.Y + canvasResizeStartSize.Height / 2f + centerDelta.Y - size.Height / 2f);
            InvalidateCanvasPaintCache(); canvasSurface.Invalidate(); return true;
        }
        if (!canvasPanning && !canvasNodeDragging && !canvasNodeRotating && !canvasWiring && !canvasMarqueeSelecting && !canvasCutting && !canvasAnchorDragging)
        {
            if (canvasTool != CanvasTool.Select) { UpdateCanvasCursor(); return true; }
            if (canvasSelectedIds.Count == 1 && selectedCanvasNode is { Locked: false } selected && !canvasVectorEditNodeId.HasValue && !canvasPathEditId.HasValue)
            {
                int handle = CanvasWorkspaceHandleAt(selected, e.Location);
                if (handle >= 0) { canvasSurface.Cursor = handle == 8 ? rotateCursor : handle is 1 or 5 ? Cursors.SizeNS : handle is 3 or 7 ? Cursors.SizeWE : handle is 0 or 4 ? Cursors.SizeNWSE : Cursors.SizeNESW; return true; }
            }
        }
        return false;
    }

    bool CanvasWorkspaceMouseUp(MouseEventArgs e)
    {
        if(CanvasPaintMouseUp(e))return true;
        if (e.Button != MouseButtons.Left) return false;
        if (canvasResizeNode != null)
        {
            bool changed = canvasResizeChanged; canvasResizeNode = null; canvasResizeHandle = -1; canvasResizeChanged = false;
            canvasResizeRedo=null;
            canvasSurface.Capture = false; if (changed) SaveCanvas(); UpdateCanvasCursor(); return true;
        }
        if (!canvasDrawing) return false;
        canvasDrawEnd = e.Location; var bounds = CanvasDrawingBounds(); var kind = canvasTool switch { CanvasTool.Text => "text", CanvasTool.Ellipse => "circle", _ => "square" };
        var position = CanvasWorldPoint(new Point(bounds.Left, bounds.Top));
        bool dragged = bounds.Width >= 4 || bounds.Height >= 4;
        var size = dragged ? new Size(Math.Max(20, (int)(bounds.Width / canvasZoom)), Math.Max(20, (int)(bounds.Height / canvasZoom))) : kind == "text" ? new Size(240, 80) : new Size(160, 120);
        canvasDrawing = false; canvasSurface.Capture = false;
        CreateCanvasNode(kind, position, "", size);
        var node = canvasNodes[^1]; node.Position = position;
        if (kind == "text") { node.Transparent = true; node.FontSize = 16; }
        else node.FillColor = Color.FromArgb(218, 232, 247).ToArgb().ToString(System.Globalization.CultureInfo.InvariantCulture);
        SaveCanvas(); SetCanvasTool(CanvasTool.Select);
        if (kind == "text") ShowCanvasInlineEditor(node);
        return true;
    }

    static PointF RotateCanvasVector(PointF point, float degrees)
    {
        double angle = degrees * Math.PI / 180, c = Math.Cos(angle), s = Math.Sin(angle);
        return new((float)(point.X * c - point.Y * s), (float)(point.X * s + point.Y * c));
    }
    static PointF[] CanvasWorkspaceHandles(Rectangle b) => new[] {
        new PointF(b.Left,b.Top), new PointF(b.Left+b.Width/2f,b.Top), new PointF(b.Right,b.Top), new PointF(b.Right,b.Top+b.Height/2f),
        new PointF(b.Right,b.Bottom), new PointF(b.Left+b.Width/2f,b.Bottom), new PointF(b.Left,b.Bottom), new PointF(b.Left,b.Top+b.Height/2f), new PointF(b.Left+b.Width/2f,b.Top-26) };

    int CanvasWorkspaceHandleAt(CanvasNode node, Point point)
    {
        var b = CanvasBounds(node); var center = CanvasNodeCenter(b);
        var local = RotateCanvasVector(new PointF(point.X - center.X, point.Y - center.Y), -node.Rotation);
        local = new(local.X + center.X, local.Y + center.Y);
        var handles = CanvasWorkspaceHandles(b);
        // Tiny nodes omit middle handles visually and in hit testing.
        for (int i = 0; i < handles.Length; i++)
        {
            if (i % 2 == 1 && (b.Width < 44 || b.Height < 44)) continue;
            if (Math.Abs(local.X - handles[i].X) <= 7 && Math.Abs(local.Y - handles[i].Y) <= 7) return i;
        }
        return -1;
    }

    void DrawCanvasWorkspaceSelection(Graphics g, CanvasNode node, Rectangle bounds)
    {
        using var pen = new Pen(canvasSelectionColor, 1.4f); using var fill = new SolidBrush(Color.White);
        g.DrawRectangle(pen, bounds);
        if (node.Locked || canvasSelectedIds.Count != 1) return;
        var handles = CanvasWorkspaceHandles(bounds);
        for (int i = 0; i < 8; i++)
        {
            if (i % 2 == 1 && (bounds.Width < 44 || bounds.Height < 44)) continue;
            var rect = new RectangleF(handles[i].X - 3.5f, handles[i].Y - 3.5f, 7, 7);
            g.FillRectangle(fill, rect); g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
        }
        g.DrawLine(pen, handles[1], handles[8]);
        g.FillEllipse(fill, handles[8].X - 4, handles[8].Y - 4, 8, 8); g.DrawEllipse(pen, handles[8].X - 4, handles[8].Y - 4, 8, 8);
    }

    void DrawCanvasWorkspaceOverlay(Graphics g)
    {
        if (canvasExporting) return;
        using var pen = new Pen(canvasSelectionColor, 1.4f);
        if (canvasDrawing)
        {
            var bounds = CanvasDrawingBounds(); using var fill = new SolidBrush(Color.FromArgb(35, canvasSelectionColor));
            if (canvasTool == CanvasTool.Ellipse) { g.FillEllipse(fill, bounds); g.DrawEllipse(pen, bounds); }
            else { g.FillRectangle(fill, bounds); g.DrawRectangle(pen, bounds); }
        }
        var nodes = CanvasSelectedNodes().Where(n => !n.Hidden).ToList();
        if (nodes.Count > 1)
        {
            var bounds = nodes.Select(CanvasBounds).Aggregate(Rectangle.Union); pen.DashStyle = DashStyle.Dash; g.DrawRectangle(pen, bounds);
        }
        if (canvasNodes.Count == 0 && !canvasDrawing)
        {
            using var title = new Font(UiFontName(), 17, FontStyle.Bold); using var hint = new Font(UiFontName(), 10);
            using var color = new SolidBrush(darkTheme ? Color.FromArgb(210, 215, 224) : Color.FromArgb(78, 86, 102));
            using var format = new StringFormat { Alignment = StringAlignment.Center };
            float y = Math.Max(40, canvasSurface.Height / 2f - 60);
            g.DrawString("把想法放到画布上", title, color, new RectangleF(12, y, canvasSurface.Width - 24, 40), format);
            g.DrawString("T 添加文字   ·   R 绘制矩形   ·   拖入图片\n空格拖动平移，Ctrl + 滚轮缩放", hint, color, new RectangleF(12, y + 44, canvasSurface.Width - 24, 70), format);
        }
    }
}
