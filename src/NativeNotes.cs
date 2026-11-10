using System.Drawing;
using X = Microsoft.UI.Xaml;
using C = Microsoft.UI.Xaml.Controls;
using M = Microsoft.UI.Xaml.Media;

namespace CardReview;

public sealed partial class ReviewForm
{
    C.Grid nativeNotesLayout = null!;
    C.Border nativeNotesSidebar = null!, nativeNotesEditorPanel = null!;
    C.ListView nativeNotesList = null!;
    C.TextBlock nativeNotesTitle = null!, nativeNotesStatus = null!;
    C.TextBox nativeNotesEditor = null!;
    readonly List<TextNoteInfo> nativeNotes = new();
    readonly System.Windows.Forms.Timer nativeNotesSaveTimer = new() { Interval = 450 };
    int nativeNotesSelectedIndex;
    Guid nativeNotesDragSourceId;
    C.Border? nativeNotesPointerSurface;
    X.Input.Pointer? nativeNotesPointer;
    Windows.Foundation.Point nativeNotesPointerStart;
    bool nativeNotesPointerMoved;
    C.TextBlock nativeNotesDragHint=null!;
    const string NativeNotesDragFormat="Ahastation.TextNotes.Entry";
    C.ListViewItem? nativeNotesDropTarget;
    int nativeNotesDropPosition;
    readonly Dictionary<Guid,C.Border> nativeNotesInsertionLines=new();
    bool nativeNotesSyncing, nativeNotesListSyncing;
    bool? nativeNotesWide;

    string NativeNotesFolder => Path.Combine(AppDataStorage.Root,"text-notes");
    string NativeNotesIndexPath => Path.Combine(NativeNotesFolder,"index.json");
    string NativeNotesSelectedPath => Path.Combine(NativeNotesFolder,"selected.txt");

    C.Grid BuildNativeNotes()
    {
        LoadNativeNotes();
        var page=GridRows(Star);page.Padding=new X.Thickness(20);
        nativeNotesLayout=new C.Grid{ColumnSpacing=16,RowSpacing=16};
        nativeNotesSidebar=BuildNativeNotesSidebar();
        nativeNotesEditorPanel=BuildNativeNotesEditor();
        nativeNotesLayout.Children.Add(nativeNotesSidebar);
        nativeNotesLayout.Children.Add(nativeNotesEditorPanel);
        Put(page,nativeNotesLayout);
        nativeNotesSaveTimer.Tick+=(_,_)=>{nativeNotesSaveTimer.Stop();SaveActiveNativeNoteText();};
        FormClosing+=(_,_)=>{nativeNotesSaveTimer.Stop();SaveActiveNativeNoteText();};
        RefreshNativeNotesList();
        LoadActiveNativeNote();
        return page;
    }

    C.Border BuildNativeNotesSidebar()
    {
        var layout=GridRows(Auto,Star);
        var header=new C.Grid{ColumnSpacing=8};
        header.ColumnDefinitions.Add(new C.ColumnDefinition{Width=Star});
        header.ColumnDefinitions.Add(new C.ColumnDefinition{Width=Auto});
        var heading=new C.StackPanel{Spacing=2,VerticalAlignment=X.VerticalAlignment.Center};
        var label=TextBlock("文本便签",20);label.VerticalAlignment=X.VerticalAlignment.Center;
        var hint=TextBlock("拖到上下边缘排序，拖到文件中间设为子级",10);hint.Opacity=.72;hint.TextWrapping=X.TextWrapping.Wrap;hint.Height=36;
        nativeNotesDragHint=hint;
        heading.Children.Add(label);heading.Children.Add(hint);
        Put(header,heading);
        Put(header,NativeButton("＋ 新建",CreateNativeNote,true),0,1);
        Put(layout,header);
        nativeNotesList=new C.ListView{SelectionMode=C.ListViewSelectionMode.Single,IsItemClickEnabled=true,Background=Brush(Color.Transparent)};
        var itemStyle=new X.Style{TargetType=typeof(C.ListViewItem)};
        itemStyle.Setters.Add(new X.Setter(C.Control.HorizontalContentAlignmentProperty,X.HorizontalAlignment.Stretch));
        itemStyle.Setters.Add(new X.Setter(C.Control.PaddingProperty,new X.Thickness(0)));
        itemStyle.Setters.Add(new X.Setter(X.FrameworkElement.MarginProperty,new X.Thickness(0,0,0,4)));
        nativeNotesList.ItemContainerStyle=itemStyle;
        nativeNotesList.CanDragItems=false;
        nativeNotesList.KeyDown+=(_,e)=>{if(e.Key==Windows.System.VirtualKey.Escape){FinishNativeNotesPointerDrag(false);e.Handled=true;}};
        nativeNotesList.Unloaded+=(_,_)=>FinishNativeNotesPointerDrag(false);
        nativeNotesList.SelectionChanged+=(_,_)=>
        {
            if(nativeNotesListSyncing||nativeNotesList.SelectedItem is not C.ListViewItem item||item.Tag is not Guid id)return;
            int noteIndex=nativeNotes.FindIndex(note=>note.Id==id);
            if(noteIndex<0)return;
            SaveActiveNativeNoteText();
            nativeNotesSelectedIndex=noteIndex;
            SaveNativeNotesSelectedId();
            LoadActiveNativeNote();
        };
        Put(layout,nativeNotesList,1);
        return Surface(layout);
    }

    C.Border BuildNativeNotesEditor()
    {
        var layout=GridRows(Auto,Star,Auto);
        nativeNotesTitle=TextBlock("文本便签",20);nativeNotesTitle.VerticalAlignment=X.VerticalAlignment.Center;
        Put(layout,nativeNotesTitle);
        nativeNotesEditor=new C.TextBox
        {
            AcceptsReturn=true,
            TextWrapping=X.TextWrapping.Wrap,
            FontFamily=new M.FontFamily("Cascadia Mono"),
            FontSize=15,
            Padding=new X.Thickness(16),
            HorizontalAlignment=X.HorizontalAlignment.Stretch,
            VerticalAlignment=X.VerticalAlignment.Stretch,
            IsSpellCheckEnabled=true
        };
        nativeNotesEditor.TextChanged+=(_,_)=>
        {
            if(nativeNotesSyncing||nativeNotesSelectedIndex<0||nativeNotesSelectedIndex>=nativeNotes.Count)return;
            nativeNotes[nativeNotesSelectedIndex].Content=nativeNotesEditor.Text;
            nativeNotesStatus.Text=$"{nativeNotesEditor.Text.Length} 个字符 · 正在保存到本机";
            nativeNotesSaveTimer.Stop();nativeNotesSaveTimer.Start();
        };
        nativeNotesEditor.SelectionChanged+=(_,_)=>UpdateNativeNoteStatus();
        Put(layout,nativeNotesEditor,1);
        nativeNotesStatus=TextBlock("本地自动保存",12);nativeNotesStatus.HorizontalAlignment=X.HorizontalAlignment.Right;
        Put(layout,nativeNotesStatus,2);
        return Surface(layout);
    }

    void NativeNotesResponsiveLayout(double width)
    {
        if(nativeNotesLayout==null)return;
        bool wide=width>=850;
        if(nativeNotesWide==wide)return;
        nativeNotesWide=wide;
        nativeNotesLayout.ColumnDefinitions.Clear();nativeNotesLayout.RowDefinitions.Clear();
        if(wide)
        {
            nativeNotesLayout.ColumnDefinitions.Add(new C.ColumnDefinition{Width=new X.GridLength(250)});
            nativeNotesLayout.ColumnDefinitions.Add(new C.ColumnDefinition{Width=Star});
            nativeNotesLayout.RowDefinitions.Add(new C.RowDefinition{Height=Star});
            nativeNotesSidebar.Height=double.NaN;
            C.Grid.SetRow(nativeNotesSidebar,0);C.Grid.SetColumn(nativeNotesSidebar,0);
            C.Grid.SetRow(nativeNotesEditorPanel,0);C.Grid.SetColumn(nativeNotesEditorPanel,1);
        }
        else
        {
            nativeNotesLayout.ColumnDefinitions.Add(new C.ColumnDefinition{Width=Star});
            nativeNotesLayout.RowDefinitions.Add(new C.RowDefinition{Height=Auto});
            nativeNotesLayout.RowDefinitions.Add(new C.RowDefinition{Height=Star});
            nativeNotesSidebar.Height=190;
            C.Grid.SetRow(nativeNotesSidebar,0);C.Grid.SetColumn(nativeNotesSidebar,0);
            C.Grid.SetRow(nativeNotesEditorPanel,1);C.Grid.SetColumn(nativeNotesEditorPanel,0);
        }
    }

    void LoadNativeNotes()
    {
        nativeNotes.Clear();
        Directory.CreateDirectory(NativeNotesFolder);
        try
        {
            if(File.Exists(NativeNotesIndexPath))nativeNotes.AddRange(System.Text.Json.JsonSerializer.Deserialize<List<TextNoteInfo>>(File.ReadAllText(NativeNotesIndexPath))??new());
        }
        catch{nativeNotes.Clear();}
        nativeNotes.RemoveAll(note=>note==null||note.Id==Guid.Empty||string.IsNullOrWhiteSpace(note.Title));
        foreach(var note in nativeNotes)
        {
            var path=NativeNoteTextPath(note);
            note.Content=File.Exists(path)?File.ReadAllText(path):"";
        }
        NormalizeNativeNoteHierarchy();
        if(nativeNotes.Count==0)nativeNotes.Add(new TextNoteInfo());
        var selectedId=Guid.Empty;
        try{if(File.Exists(NativeNotesSelectedPath))Guid.TryParse(File.ReadAllText(NativeNotesSelectedPath),out selectedId);}catch{}
        nativeNotesSelectedIndex=nativeNotes.FindIndex(note=>note.Id==selectedId);
        if(nativeNotesSelectedIndex<0)nativeNotesSelectedIndex=0;
        SaveNativeNotesIndex();
        SaveNativeNotesSelectedId();
    }

    string NativeNoteTextPath(TextNoteInfo note)=>Path.Combine(NativeNotesFolder,$"{note.Id:N}.txt");

    void SaveNativeNotesIndex()
    {
        try
        {
            Directory.CreateDirectory(NativeNotesFolder);
            File.WriteAllText(NativeNotesIndexPath,System.Text.Json.JsonSerializer.Serialize(nativeNotes));
        }
        catch(IOException){}
        catch(UnauthorizedAccessException){}
    }

    void SaveNativeNotesSelectedId()
    {
        try
        {
            if((uint)nativeNotesSelectedIndex>=(uint)nativeNotes.Count)return;
            File.WriteAllText(NativeNotesSelectedPath,nativeNotes[nativeNotesSelectedIndex].Id.ToString("D"));
        }
        catch(IOException){}
        catch(UnauthorizedAccessException){}
    }

    void SaveActiveNativeNoteText()
    {
        nativeNotesSaveTimer.Stop();
        if((uint)nativeNotesSelectedIndex>=(uint)nativeNotes.Count)return;
        var note=nativeNotes[nativeNotesSelectedIndex];
        try
        {
            Directory.CreateDirectory(NativeNotesFolder);
            File.WriteAllText(NativeNoteTextPath(note),note.Content);
            if(nativeNotesStatus!=null)nativeNotesStatus.Text=$"{note.Content.Length} 个字符 · 已保存到本机";
        }
        catch(IOException){if(nativeNotesStatus!=null)nativeNotesStatus.Text="保存失败，请检查存储空间";}
        catch(UnauthorizedAccessException){if(nativeNotesStatus!=null)nativeNotesStatus.Text="保存失败，请检查文件权限";}
    }

    void RefreshNativeNotesList()
    {
        nativeNotesListSyncing=true;
        nativeNotesList.Items.Clear();
        nativeNotesInsertionLines.Clear();
        var visible=new List<(TextNoteInfo Note,int Depth)>();
        foreach(var root in nativeNotes.Where(note=>note.ParentId==null))AppendVisibleNativeNotes(root,0,visible,new HashSet<Guid>());
        foreach(var entry in visible)
        {
            var note=entry.Note;
            var children=nativeNotes.Any(child=>child.ParentId==note.Id);
            X.FrameworkElement expander;
            if(children)
            {
                var toggle=new C.Button
                {
                    Content=TextBlock(note.IsExpanded?"▾":"▸",16),
                    Width=26,Height=36,MinWidth=26,Padding=new X.Thickness(0),
                    HorizontalAlignment=X.HorizontalAlignment.Center,
                    VerticalAlignment=X.VerticalAlignment.Center,
                    Background=Brush(Color.Transparent),BorderThickness=new X.Thickness(0)
                };
                toggle.Click+=(_,_)=>ToggleNativeNoteExpanded(note);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle,note.IsExpanded?$"折叠 {note.Title} 的子级":$"展开 {note.Title} 的子级");
                C.ToolTipService.SetToolTip(toggle,note.IsExpanded?"收起子级便签":"展开子级便签");
                expander=toggle;
            }
            else expander=TextBlock("",12);
            var title=TextBlock(note.Title,14);title.VerticalAlignment=X.VerticalAlignment.Center;
            title.TextTrimming=X.TextTrimming.CharacterEllipsis;
            var card=new C.Border
            {
                Padding=new X.Thickness(10,8,10,8),
                MinHeight=40,
                CornerRadius=new X.CornerRadius(4),
                BorderThickness=new X.Thickness(0),
                Background=Brush(Color.Transparent),
                Child=title
            };
            var nestedRow=new C.Grid{VerticalAlignment=X.VerticalAlignment.Center};
            nestedRow.ColumnDefinitions.Add(new C.ColumnDefinition{Width=new X.GridLength(Math.Min(entry.Depth,5)*28)});
            nestedRow.ColumnDefinitions.Add(new C.ColumnDefinition{Width=new X.GridLength(28)});
            nestedRow.ColumnDefinitions.Add(new C.ColumnDefinition{Width=Star});
            Put(nestedRow,expander,0,1);
            Put(nestedRow,card,0,2);
            var insertionLine=new C.Border{Height=2,Background=nativeAccent,Visibility=X.Visibility.Collapsed,IsHitTestVisible=false,VerticalAlignment=X.VerticalAlignment.Top};
            Put(nestedRow,insertionLine,0,2);
            nativeNotesInsertionLines[note.Id]=insertionLine;
            var item=new C.ListViewItem{Content=nestedRow,Tag=note.Id};
            card.PointerPressed+=(_,e)=>
            {
                if(!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed||nativeNotesPointerSurface!=null)return;
                nativeNotesPointerSurface=card;nativeNotesPointer=e.Pointer;nativeNotesDragSourceId=note.Id;
                nativeNotesPointerStart=e.GetCurrentPoint(nativeNotesList).Position;
                nativeNotesPointerMoved=false;
                if(!card.CapturePointer(e.Pointer)){FinishNativeNotesPointerDrag(false);return;}
                SelectNativeNoteById(note.Id);
                nativeNotesList.Focus(X.FocusState.Pointer);
                e.Handled=true;
            };
            card.PointerMoved+=(_,e)=>
            {
                if(nativeNotesPointerSurface!=card||e.Pointer.PointerId!=nativeNotesPointer?.PointerId)return;
                if(!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed){FinishNativeNotesPointerDrag(false);return;}
                var point=e.GetCurrentPoint(nativeNotesList).Position;
                if(!nativeNotesPointerMoved&&Math.Abs(point.X-nativeNotesPointerStart.X)<5&&Math.Abs(point.Y-nativeNotesPointerStart.Y)<5)return;
                nativeNotesPointerMoved=true;card.Opacity=.55;
                UpdateNativeNotesPointerTarget(point);
                e.Handled=true;
            };
            card.PointerReleased+=(_,e)=>
            {
                if(nativeNotesPointerSurface!=card||e.Pointer.PointerId!=nativeNotesPointer?.PointerId)return;
                var point=e.GetCurrentPoint(nativeNotesList).Position;
                bool inside=point.X>=0&&point.X<nativeNotesList.ActualWidth&&point.Y>=0&&point.Y<nativeNotesList.ActualHeight;
                if(nativeNotesPointerMoved&&inside)UpdateNativeNotesPointerTarget(point);
                FinishNativeNotesPointerDrag(inside);
                e.Handled=true;
            };
            card.PointerCanceled+=(_,_)=>{if(nativeNotesPointerSurface==card)FinishNativeNotesPointerDrag(false);};
            card.PointerCaptureLost+=(_,_)=>{if(nativeNotesPointerSurface==card)FinishNativeNotesPointerDrag(false);};
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item,note.Title);
            item.RightTapped+=(_,_)=>SelectNativeNoteById(note.Id);
            var contextMenu=new C.MenuFlyout();
            var renameItem=new C.MenuFlyoutItem{Text="重命名"};
            renameItem.Click+=(_,_)=>{SelectNativeNoteById(note.Id);RenameActiveNativeNote();};
            var deleteItem=new C.MenuFlyoutItem{Text="删除"};
            deleteItem.Click+=(_,_)=>{SelectNativeNoteById(note.Id);DeleteActiveNativeNote();};
            contextMenu.Items.Add(renameItem);
            if(note.ParentId!=null)
            {
                var moveToTop=new C.MenuFlyoutItem{Text="移到顶层"};
                moveToTop.Click+=(_,_)=>ReparentNativeNote(note.Id,null);
                contextMenu.Items.Add(moveToTop);
            }
            contextMenu.Items.Add(new C.MenuFlyoutSeparator());
            contextMenu.Items.Add(deleteItem);
            item.ContextFlyout=contextMenu;
            nativeNotesList.Items.Add(item);
        }
        nativeNotesList.SelectedItem=nativeNotesList.Items.OfType<C.ListViewItem>().FirstOrDefault(item=>item.Tag is Guid id&&id==SelectedNativeNoteId());
        nativeNotesListSyncing=false;
    }

    void SelectNativeNoteById(Guid id)
    {
        int index=nativeNotes.FindIndex(note=>note.Id==id);
        if(index<0)return;
        var item=nativeNotesList.Items.OfType<C.ListViewItem>().FirstOrDefault(row=>row.Tag is Guid rowId&&rowId==id);
        if(item!=null&&!ReferenceEquals(nativeNotesList.SelectedItem,item))
        {
            nativeNotesList.SelectedItem=item;
            return;
        }
        if(nativeNotesSelectedIndex==index)return;
        SaveActiveNativeNoteText();
        nativeNotesSelectedIndex=index;
        SaveNativeNotesSelectedId();
        LoadActiveNativeNote();
    }

    Guid SelectedNativeNoteId()=>(uint)nativeNotesSelectedIndex<(uint)nativeNotes.Count?nativeNotes[nativeNotesSelectedIndex].Id:Guid.Empty;

    void AppendVisibleNativeNotes(TextNoteInfo note,int depth,List<(TextNoteInfo Note,int Depth)> visible,HashSet<Guid> visited)
    {
        if(!visited.Add(note.Id))return;
        visible.Add((note,depth));
        if(!note.IsExpanded)return;
        foreach(var child in nativeNotes.Where(item=>item.ParentId==note.Id))AppendVisibleNativeNotes(child,depth+1,visible,visited);
    }

    void NormalizeNativeNoteHierarchy()
    {
        var byId=nativeNotes.ToDictionary(note=>note.Id);
        foreach(var note in nativeNotes)
        {
            if(note.ParentId is Guid parentId&&!byId.ContainsKey(parentId))note.ParentId=null;
            var visited=new HashSet<Guid>{note.Id};
            var cursor=note.ParentId;
            while(cursor is Guid id&&byId.TryGetValue(id,out var parent))
            {
                if(!visited.Add(id)){note.ParentId=null;break;}
                cursor=parent.ParentId;
            }
        }
    }

    void ToggleNativeNoteExpanded(TextNoteInfo note)
    {
        if(!nativeNotes.Any(child=>child.ParentId==note.Id))return;
        note.IsExpanded=!note.IsExpanded;
        if(!note.IsExpanded&&IsNativeNoteDescendant(SelectedNativeNoteId(),note.Id))
        {
            SaveActiveNativeNoteText();
            nativeNotesSelectedIndex=nativeNotes.FindIndex(item=>item.Id==note.Id);
            SaveNativeNotesSelectedId();
            LoadActiveNativeNote();
        }
        SaveNativeNotesIndex();
        RefreshNativeNotesList();
    }

    bool IsNativeNoteDescendant(Guid noteId,Guid potentialAncestorId)
    {
        if(noteId==Guid.Empty)return false;
        var byId=nativeNotes.ToDictionary(note=>note.Id);
        var visited=new HashSet<Guid>();
        while(byId.TryGetValue(noteId,out var note)&&note.ParentId is Guid parentId&&visited.Add(parentId))
        {
            if(parentId==potentialAncestorId)return true;
            noteId=parentId;
        }
        return false;
    }

    C.ListViewItem? NativeNotesItemFromSource(object? source)
    {
        var current=source as X.DependencyObject;
        while(current!=null)
        {
            if(current is C.ListViewItem item&&item.Tag is Guid)return item;
            current=M.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    bool CanDropNativeNote(Guid sourceId,Guid? targetId)
    {
        if(sourceId==Guid.Empty||!nativeNotes.Any(note=>note.Id==sourceId))return false;
        if(targetId==sourceId)return false;
        if(targetId is Guid target&&!nativeNotes.Any(note=>note.Id==target))return false;
        return targetId==null||!IsNativeNoteDescendant(targetId.Value,sourceId);
    }

    C.ListViewItem? NativeNotesDropItem(X.DragEventArgs e)
    {
        return NativeNotesItemAtPoint(e.GetPosition(nativeNotesList));
    }

    C.ListViewItem? NativeNotesItemAtPoint(Windows.Foundation.Point point)
    {
        foreach(var item in nativeNotesList.Items.OfType<C.ListViewItem>())
        {
            if(item.Tag is not Guid||item.ActualHeight<=0||item.XamlRoot==null)continue;
            var origin=item.TransformToVisual(nativeNotesList).TransformPoint(new Windows.Foundation.Point(0,0));
            if(point.X>=origin.X&&point.X<origin.X+item.ActualWidth&&
               point.Y>=origin.Y&&point.Y<origin.Y+item.ActualHeight)return item;
        }
        return null;
    }

    void UpdateNativeNotesPointerTarget(Windows.Foundation.Point point)
    {
        var target=NativeNotesItemAtPoint(point);
        int position=0;
        if(target!=null)
        {
            var origin=target.TransformToVisual(nativeNotesList).TransformPoint(new Windows.Foundation.Point(0,0));
            double relativeY=point.Y-origin.Y;
            double edge=Math.Min(12,target.ActualHeight*.25);
            position=relativeY<edge?-1:relativeY>target.ActualHeight-edge?1:0;
        }
        if(target!=nativeNotesDropTarget||position!=nativeNotesDropPosition)
        {
            ClearNativeNotesDropTarget();
            nativeNotesDropTarget=target;
        }
        nativeNotesDropPosition=position;
        Guid? targetId=target?.Tag is Guid id?id:null;
        var targetNote=nativeNotes.FirstOrDefault(note=>note.Id==targetId);
        Guid? parentId=position==0?targetId:targetNote?.ParentId;
        bool valid=CanDropNativeNote(nativeNotesDragSourceId,parentId)&&targetId!=nativeNotesDragSourceId&&
            (targetId==null||!IsNativeNoteDescendant(targetId.Value,nativeNotesDragSourceId));
        if(target!=null)
        {
            target.BorderBrush=valid?nativeAccent:nativeStroke;
            target.BorderThickness=new X.Thickness(position==0?2:0);
            if(targetId is Guid lineId&&nativeNotesInsertionLines.TryGetValue(lineId,out var line))
            {
                line.VerticalAlignment=position<0?X.VerticalAlignment.Top:X.VerticalAlignment.Bottom;
                line.Visibility=valid&&position!=0?X.Visibility.Visible:X.Visibility.Collapsed;
            }
        }
        nativeNotesDragHint.Text=!valid?"不能拖到自身或自己的子级下":targetNote==null?"松开后移到顶层":
            position<0?$"插入到“{targetNote.Title}”上方":position>0?$"插入到“{targetNote.Title}”下方":$"成为“{targetNote.Title}”的子级";
    }

    void FinishNativeNotesPointerDrag(bool commit)
    {
        var surface=nativeNotesPointerSurface;
        if(surface==null)return;
        var pointer=nativeNotesPointer;
        var sourceId=nativeNotesDragSourceId;
        Guid? targetId=nativeNotesDropTarget?.Tag is Guid id?id:null;
        int position=nativeNotesDropPosition;
        Guid? parentId=position==0?targetId:nativeNotes.FirstOrDefault(note=>note.Id==targetId)?.ParentId;
        bool move=commit&&nativeNotesPointerMoved&&CanDropNativeNote(sourceId,parentId)&&targetId!=sourceId&&
            (targetId==null||!IsNativeNoteDescendant(targetId.Value,sourceId));
        nativeNotesPointerSurface=null;nativeNotesPointer=null;nativeNotesDragSourceId=Guid.Empty;nativeNotesPointerMoved=false;
        surface.Opacity=1;
        ClearNativeNotesDropTarget();
        nativeNotesDragHint.Text="拖到上下边缘排序，拖到文件中间设为子级";
        if(pointer!=null)surface.ReleasePointerCapture(pointer);
        if(move)ReparentNativeNote(sourceId,parentId,position==0?null:targetId,position>0);
    }

    void NativeNotesDragStarting(object? sender,C.DragItemsStartingEventArgs e)
    {
        var item=e.Items.OfType<C.ListViewItem>().FirstOrDefault();
        if(item?.Tag is not Guid id)
        {
            e.Cancel=true;
            return;
        }
        nativeNotesDragSourceId=id;
        e.Data.SetText(id.ToString("D"));
        e.Data.SetData(NativeNotesDragFormat,"1");
        e.Data.RequestedOperation=Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
    }

    void NativeNotesDragOver(object? sender,X.DragEventArgs e)
    {
        var target=NativeNotesDropItem(e);
        Guid? targetId=target?.Tag is Guid id?id:null;
        if(e.DataView.Contains(NativeNotesDragFormat)&&CanDropNativeNote(nativeNotesDragSourceId,targetId))
        {
            e.AcceptedOperation=Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
            var parent=targetId is Guid parentId?nativeNotes.FirstOrDefault(note=>note.Id==parentId):null;
            e.DragUIOverride.Caption=parent==null?"移到顶层":$"成为“{parent.Title}”的子级";
            e.DragUIOverride.IsCaptionVisible=true;
            if(target!=nativeNotesDropTarget)
            {
                ClearNativeNotesDropTarget();
                nativeNotesDropTarget=target;
                if(target!=null){target.BorderBrush=nativeAccent;target.BorderThickness=new X.Thickness(2);}
            }
        }
        else {e.AcceptedOperation=Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;ClearNativeNotesDropTarget();}
        e.Handled=true;
    }

    async void NativeNotesDrop(object? sender,X.DragEventArgs e)
    {
        e.Handled=true;
        if(!e.DataView.Contains(NativeNotesDragFormat))return;
        var target=NativeNotesDropItem(e);
        Guid? targetId=target?.Tag is Guid id?id:null;
        ClearNativeNotesDropTarget();
        var deferral=e.GetDeferral();
        try
        {
            var text=await e.DataView.GetTextAsync();
            if(!Guid.TryParse(text,out var sourceId)||!CanDropNativeNote(sourceId,targetId))return;
            ReparentNativeNote(sourceId,targetId);
        }
        catch(InvalidOperationException){}
        finally{nativeNotesDragSourceId=Guid.Empty;deferral.Complete();}
        e.Handled=true;
    }

    void ClearNativeNotesDropTarget()
    {
        foreach(var line in nativeNotesInsertionLines.Values)line.Visibility=X.Visibility.Collapsed;
        nativeNotesDropPosition=0;
        if(nativeNotesDropTarget!=null)
        {
            nativeNotesDropTarget.ClearValue(C.Control.BorderBrushProperty);
            nativeNotesDropTarget.ClearValue(C.Control.BorderThicknessProperty);
            nativeNotesDropTarget=null;
        }
    }

    void ReparentNativeNote(Guid sourceId,Guid? targetId,Guid? adjacentId=null,bool after=false)
    {
        if(!CanDropNativeNote(sourceId,targetId))return;
        var note=nativeNotes.FirstOrDefault(item=>item.Id==sourceId);
        var parent=targetId is Guid id?nativeNotes.FirstOrDefault(item=>item.Id==id):null;
        if(note==null||(targetId!=null&&parent==null))return;
        var adjacent=adjacentId is Guid siblingId?nativeNotes.FirstOrDefault(item=>item.Id==siblingId):null;
        if(adjacentId!=null&&(adjacent==null||adjacent.ParentId!=targetId||adjacent.Id==sourceId||IsNativeNoteDescendant(adjacent.Id,sourceId)))return;
        var selectedId=SelectedNativeNoteId();
        var subtree=new List<TextNoteInfo>();
        CollectNativeNoteSubtree(note,subtree,new HashSet<Guid>());
        foreach(var entry in subtree)nativeNotes.Remove(entry);
        note.ParentId=parent?.Id;
        if(parent!=null)parent.IsExpanded=true;
        int insertAt=adjacent==null?nativeNotes.Count:nativeNotes.IndexOf(adjacent)+(after?1:0);
        nativeNotes.InsertRange(insertAt,subtree);
        nativeNotesSelectedIndex=nativeNotes.FindIndex(item=>item.Id==selectedId);
        if(nativeNotesSelectedIndex<0)nativeNotesSelectedIndex=nativeNotes.FindIndex(item=>item.Id==sourceId);
        SaveNativeNotesIndex();SaveNativeNotesSelectedId();RefreshNativeNotesList();
        var movedItem=nativeNotesList.Items.OfType<C.ListViewItem>().FirstOrDefault(item=>item.Tag is Guid itemId&&itemId==sourceId);
        if(movedItem!=null)nativeNotesList.ScrollIntoView(movedItem);
    }

    void CollectNativeNoteSubtree(TextNoteInfo note,List<TextNoteInfo> result,HashSet<Guid> visited)
    {
        if(!visited.Add(note.Id))return;
        result.Add(note);
        foreach(var child in nativeNotes.Where(item=>item.ParentId==note.Id).ToList())CollectNativeNoteSubtree(child,result,visited);
    }

    void LoadActiveNativeNote()
    {
        nativeNotesSyncing=true;
        bool hasNote=(uint)nativeNotesSelectedIndex<(uint)nativeNotes.Count;
        nativeNotesEditor.IsEnabled=hasNote;
        if(hasNote)
        {
            var note=nativeNotes[nativeNotesSelectedIndex];
            nativeNotesTitle.Text=note.Title;
            nativeNotesEditor.Text=note.Content;
        }
        else
        {
            nativeNotesTitle.Text="没有打开的便签";
            nativeNotesEditor.Text="";
        }
        nativeNotesSyncing=false;
        UpdateNativeNoteStatus();
    }

    void UpdateNativeNoteStatus()
    {
        if(nativeNotesStatus==null||nativeNotesEditor==null)return;
        int position=Math.Clamp(nativeNotesEditor.SelectionStart,0,nativeNotesEditor.Text.Length);
        int line=1,column=1;
        for(int i=0;i<position;i++)
        {
            if(nativeNotesEditor.Text[i]=='\r'){line++;column=1;}
            else if(nativeNotesEditor.Text[i]=='\n'){if(i==0||nativeNotesEditor.Text[i-1]!='\r')line++;column=1;}
            else column++;
        }
        nativeNotesStatus.Text=$"行 {line}，列 {column} · {nativeNotesEditor.Text.Length} 个字符 · 本地自动保存";
    }

    void CreateNativeNote()
    {
        SaveActiveNativeNoteText();
        int suffix=1;string title;
        do{title=suffix==1?"新建 文本文档.txt":$"新建 文本文档 ({suffix}).txt";suffix++;}
        while(nativeNotes.Any(note=>note.Title.Equals(title,StringComparison.OrdinalIgnoreCase)));
        nativeNotes.Add(new TextNoteInfo{Title=title});
        nativeNotesSelectedIndex=nativeNotes.Count-1;
        SaveNativeNotesIndex();SaveNativeNotesSelectedId();
        RefreshNativeNotesList();LoadActiveNativeNote();
        nativeNotesEditor.Focus(X.FocusState.Programmatic);
    }

    void RenameActiveNativeNote()
    {
        if((uint)nativeNotesSelectedIndex>=(uint)nativeNotes.Count)return;
        var note=nativeNotes[nativeNotesSelectedIndex];
        NativePrompt("重命名便签",note.Title,value=>
        {
            note.Title=value;
            nativeNotesTitle.Text=value;
            SaveNativeNotesIndex();RefreshNativeNotesList();
        });
    }

    async void DeleteActiveNativeNote()
    {
        if((uint)nativeNotesSelectedIndex>=(uint)nativeNotes.Count)return;
        var note=nativeNotes[nativeNotesSelectedIndex];
        int childCount=nativeNotes.Count(item=>item.ParentId==note.Id);
        var dialog=new C.ContentDialog
        {
            Title="删除文本便签？",
            Content=childCount>0?$"“{note.Title}”及其中的文本将被删除。它的 {childCount} 个直接子级会移到原上一级。":$"“{note.Title}”及其中的文本将被删除。",
            PrimaryButtonText="删除",CloseButtonText="取消",DefaultButton=C.ContentDialogButton.Close,
            XamlRoot=nativeRoot.XamlRoot,RequestedTheme=nativeRoot.RequestedTheme
        };
        C.ContentDialogResult result;
        NativePopup(true);try{result=await dialog.ShowAsync();}finally{NativePopup(false);}
        if(result!=C.ContentDialogResult.Primary)return;
        nativeNotesSaveTimer.Stop();
        try{var path=NativeNoteTextPath(note);if(File.Exists(path))File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}
        foreach(var child in nativeNotes.Where(item=>item.ParentId==note.Id))child.ParentId=note.ParentId;
        nativeNotes.RemoveAt(nativeNotesSelectedIndex);
        if(nativeNotes.Count==0)nativeNotes.Add(new TextNoteInfo());
        nativeNotesSelectedIndex=Math.Clamp(nativeNotesSelectedIndex,0,nativeNotes.Count-1);
        SaveNativeNotesIndex();SaveNativeNotesSelectedId();RefreshNativeNotesList();LoadActiveNativeNote();
    }
}
