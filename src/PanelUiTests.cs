using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BalancePanel;

internal static class PanelUiTests
{
    static int passed,failed;
    static IEnumerable<DependencyObject> All(DependencyObject root) {
        yield return root;
        foreach(object item in LogicalTreeHelper.GetChildren(root)) {
            DependencyObject child=item as DependencyObject;
            if(child!=null) foreach(var nested in All(child)) yield return nested;
        }
    }
    static void Check(string name,bool ok) { if(ok) passed++; else failed++; Console.WriteLine((ok?"PASS ":"FAIL ")+name); }
    static bool IconFits(Button button) {
        var path=button.Content as System.Windows.Shapes.Path;if(path==null)return true;
        Rect ink=path.RenderedGeometry.GetRenderBounds(new Pen(path.Stroke,path.StrokeThickness));
        DependencyObject cursor=path;
        while(cursor!=null) {
            var element=cursor as FrameworkElement;
            if(element!=null) {
                Geometry clip=System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(element);
                Rect local=element==path?ink:path.TransformToAncestor(element).TransformBounds(ink);
                if(clip!=null && !clip.Bounds.Contains(local)) {Console.WriteLine("ICON_CLIP "+System.Windows.Automation.AutomationProperties.GetName(button)+" ink="+local+" clip="+clip.Bounds);return false;}
            }
            if(cursor==button)break;cursor=VisualTreeHelper.GetParent(cursor);
        }
        return true;
    }
    static void Capture(Window window,string path) {
        window.UpdateLayout(); var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var stream=File.Create(path)) encoder.Save(stream);
    }
    static Panel Board(Window w) { return All(w).OfType<Panel>().FirstOrDefault(x=>x.Name=="CardBoard"); }
    static string[] Order(Window w) { var board=Board(w); return board==null?new string[0]:board.Children.OfType<FrameworkElement>().Select(x=>x.Tag as string).Where(x=>x!=null).ToArray(); }
    static Dictionary<string,RowState> Rows(PanelWindow w) { return (Dictionary<string,RowState>)typeof(PanelWindow).GetField("rows",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(w); }
    static void Click(Window window,string label) {All(window).OfType<Button>().First(x=>(x.Content as string)==label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
    static void EditSettings(PanelWindow panel,Action<SettingsWindow> edit) {
        panel.Dispatcher.BeginInvoke(new Action(delegate {edit(Application.Current.Windows.OfType<SettingsWindow>().Single());}),DispatcherPriority.ApplicationIdle);
        typeof(PanelWindow).GetMethod("OpenSettings",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(panel,null);
    }
    static void ArmDrag(Panel board) {
        // Drive production pointer handlers with deterministic coordinates, without faking an OS button press.
        BeginLiveDrag(board,"deepseek");Call(board,"UpdateDrag",Slot(board,3));
    }
    static void Seed(string root,PanelKind kind) {
        var store=new SecureSettings(Path.Combine(root,"settings"));
        if(kind==PanelKind.Vpn) store.Save("vpn",new AccountSettings {VpnUrl="http://127.0.0.1/fixture"});
        else {
            foreach(string id in new [] {"deepseek","xai","minimax"}) store.Save(id,new AccountSettings {ApiKey="synthetic-fixture",TeamId="fixture",Region="cn"});
            foreach(string id in new [] {"mimo","openai"}) {string dir=Path.Combine(root,"browser",id); Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir,"enabled"),"fixture only");}
        }
    }
    static object Call(Panel board,string method,params object[] args) {return board.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(board,args);}
    static FrameworkElement Cell(Panel board,string id) {return board.Children.OfType<FrameworkElement>().Single(x=>(x.Tag as string)==id);}
    static Point Slot(Panel board,int index) {var first=Cell(board,"deepseek");return new Point((index%2)*(first.Width+8)+first.Width/2,(index/2)*(first.Height+8)+first.Height/2);}
    static string[] Slots(Panel board) {return board.Children.OfType<FrameworkElement>().OrderBy(Canvas.GetTop).ThenBy(Canvas.GetLeft).Select(x=>x.Tag as string).ToArray();}
    static void BeginLiveDrag(Panel board,string id) {
        var cards=(System.Collections.IList)board.GetType().GetField("cards",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(board);
        object card=cards.Cast<object>().Single(x=>(string)x.GetType().GetField("Id",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(x)==id);
        board.GetType().GetField("armed",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(board,card);
        var view=Cell(board,id);board.GetType().GetField("pressed",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(board,new Point(Canvas.GetLeft(view)+view.Width/2,Canvas.GetTop(view)+view.Height/2));
        Call(board,"StartDrag");
    }
    static async Task LiveDragCases(string root) {
        string dir=Path.Combine(root,"live-drag");Seed(dir,PanelKind.Api);var w=new PanelWindow(PanelKind.Api,dir);w.Show();w.UpdateLayout();
        Panel board=Board(w);var original=board.Children.Cast<UIElement>().ToArray();string[] initial={"deepseek","openai","xai","mimo","minimax"};
        if(board.GetType().GetMethod("UpdateDrag",BindingFlags.Instance|BindingFlags.NonPublic)==null) {Check("dragging previews insertion before release",false);w.Close();return;}
        int commits=0;((CardBoard)board).OrderChanged+=delegate {commits++;};
        Theme.Apply(w,false);foreach(var row in Rows(w).Values) row.Accept("CNY 12.34","synthetic drag preview",new DateTime(2026,10,19,13,42,0));
        string animation=Path.Combine(root,"drag-frames");Directory.CreateDirectory(animation);Capture(w,Path.Combine(animation,"00.png"));
        BeginLiveDrag(board,"deepseek");Point lastVisible=Slot(board,3);Call(board,"UpdateDrag",lastVisible);
        Check("other cards vacate their slots before release",Canvas.GetLeft(Cell(board,"openai"))==0 && Canvas.GetTop(Cell(board,"xai"))==0 && Canvas.GetLeft(Cell(board,"mimo"))==0);
        Check("preview neither commits nor persists order",commits==0 && Order(w).SequenceEqual(initial) && !File.Exists(Path.Combine(dir,"panel.json")));
        Check("drag keeps the exact original card objects",board.Children.Cast<UIElement>().SequenceEqual(original));
        var follow=Cell(board,"deepseek");Point shown=follow.TranslatePoint(new Point(follow.Width/2,follow.Height/2),board);
        Check("dragged card remains at pointer while neighbors animate",(shown-lastVisible).Length<2);
        Rows(w)["deepseek"].Accept("CNY 9.87","synthetic update during drag",DateTime.Now);await Dispatcher.Yield(DispatcherPriority.Render);
        Check("dragged entity continues showing live data",All(follow).OfType<TextBlock>().Any(x=>x.Text.Contains("9.87")));
        for(int frame=1;frame<=8;frame++) {Capture(w,Path.Combine(animation,frame.ToString("00")+".png"));await Task.Delay(32);}
        Check("neighbors finish their give-way animation",!((TransformGroup)Cell(board,"openai").RenderTransform).Children.OfType<TranslateTransform>().Any(x=>Math.Abs(x.X)>0.1 || Math.Abs(x.Y)>0.1));
        Call(board,"UpdateDrag",new Point(follow.Width+4,follow.Height+4));
        Call(board,"FinishAt",new Point(150,150));await Task.Delay(210);w.UpdateLayout();
        Check("gap keeps preview insertion on release",Order(w).SequenceEqual(new [] {"openai","xai","mimo","deepseek","minimax"}) && commits==1);
        Check("release settles into the preview slots",Slots(board).SequenceEqual(Order(w)));
        Capture(w,Path.Combine(animation,"09.png"));
        w.Close();var again=new PanelWindow(PanelKind.Api,dir);again.Show();again.UpdateLayout();
        Check("live insertion order survives a restart",Order(again).SequenceEqual(new [] {"openai","xai","mimo","deepseek","minimax"}));again.Close();
        foreach(string cancel in new [] {"outside","escape","capture"}) {
            string cancelDir=Path.Combine(root,"cancel-"+cancel);Seed(cancelDir,PanelKind.Api);var panel=new PanelWindow(PanelKind.Api,cancelDir);panel.Show();panel.UpdateLayout();Panel b=Board(panel);int saved=0;((CardBoard)b).OrderChanged+=delegate {saved++;};
            BeginLiveDrag(b,"deepseek");Call(b,"UpdateDrag",Slot(b,3));
            if(cancel=="outside") Call(b,"FinishAt",new Point(301,125));
            else if(cancel=="escape") b.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(panel),0,Key.Escape) {RoutedEvent=Keyboard.KeyDownEvent});
            else All(Cell(b,"deepseek")).OfType<Border>().First().RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0) {RoutedEvent=Mouse.LostMouseCaptureEvent});
            await Task.Delay(210);panel.UpdateLayout();
            Check(cancel+" restores original layout without saving",Order(panel).SequenceEqual(initial) && Slots(b).SequenceEqual(initial) && saved==0 && !File.Exists(Path.Combine(cancelDir,"panel.json")));
            panel.Close();
        }
        string thresholdDir=Path.Combine(root,"drag-threshold");Seed(thresholdDir,PanelKind.Api);var threshold=new PanelWindow(PanelKind.Api,thresholdDir);threshold.Show();threshold.UpdateLayout();Panel surface=Board(threshold);
        BeginLiveDrag(surface,"deepseek");Point next=Slot(surface,1);Call(surface,"UpdateDrag",new Point(next.X-2,next.Y));
        Check("rightward insertion waits until the slot midpoint",Slots(surface).SequenceEqual(initial));
        Call(surface,"UpdateDrag",next);Check("crossing midpoint previews the adjacent insertion",Canvas.GetLeft(Cell(surface,"openai"))==0);
        Call(surface,"UpdateDrag",new Point(next.X-2,next.Y));Check("midpoint jitter does not reverse the insertion",Canvas.GetLeft(Cell(surface,"openai"))==0);
        Call(surface,"UpdateDrag",Slot(surface,0));Call(surface,"FinishAt",new Point(150,150));
        Check("returning to original slot avoids unnecessary save",Order(threshold).SequenceEqual(initial) && !File.Exists(Path.Combine(thresholdDir,"panel.json")));
        BeginLiveDrag(surface,"deepseek");var viewer=All(threshold).OfType<ScrollViewer>().Single(x=>x.Content==surface);viewer.ScrollToEnd();threshold.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.Render);Call(surface,"UpdateDrag",Slot(surface,4));Call(surface,"FinishAt",new Point(150,150));
        Check("scrolling during drag permits insertion at the fifth card",Order(threshold).SequenceEqual(new [] {"openai","xai","mimo","minimax","deepseek"}));threshold.Close();
    }
    static bool EqualGutters(Window panel) {
        var cells=Board(panel).Children.OfType<FrameworkElement>().Take(2).ToArray();
        if(cells.Length<2) return false;
        double left=cells[0].TranslatePoint(new Point(0,0),panel).X;
        double right=panel.ActualWidth-cells[1].TranslatePoint(new Point(cells[1].ActualWidth,0),panel).X;
        Console.WriteLine("GUTTERS left="+left+" right="+right);
        return Math.Abs(left-right)<=1;
    }
    static async Task ScrollCases(PanelWindow panel,string root) {
        var board=Board(panel);var viewer=All(panel).OfType<ScrollViewer>().Single(x=>x.Content==board);viewer.ApplyTemplate();
        var bar=viewer.Template.FindName("PART_VerticalScrollBar",viewer) as System.Windows.Controls.Primitives.ScrollBar;
        Check("scrollbar starts hidden despite overflowing content",bar!=null && bar.Opacity<0.01 && !bar.IsHitTestVisible);
        Check("scrollbar does not reserve any card layout width",Math.Abs(viewer.ViewportWidth-viewer.ActualWidth)<1);
        Check("two-column grid has equal outer left and right gaps",EqualGutters(panel));
        double[] sizes=board.Children.OfType<FrameworkElement>().Select(x=>x.ActualWidth).ToArray();
        viewer.ScrollToVerticalOffset(35);await Dispatcher.Yield(DispatcherPriority.Render);panel.UpdateLayout();
        Check("scroll movement reveals the scrollbar",bar!=null && bar.Opacity>0.9 && bar.IsHitTestVisible);
        if(bar!=null) Check("scrollbar floats entirely in the outer right gutter",bar.TranslatePoint(new Point(0,0),panel).X>=board.TranslatePoint(new Point(board.ActualWidth,0),panel).X);
        if(bar!=null) {
            Point thumb=bar.TranslatePoint(new Point(bar.ActualWidth/2,35),panel);DependencyObject hit=panel.InputHitTest(thumb) as DependencyObject;
            while(hit!=null && hit!=bar) hit=VisualTreeHelper.GetParent(hit);
            Check("floating scrollbar remains clickable outside viewport bounds",hit==bar);
            bar.ApplyTemplate();var track=bar.Template.FindName("PART_Track",bar) as System.Windows.Controls.Primitives.Track;
            if(track!=null && track.Thumb!=null) {
                track.Thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0,0) {RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragStartedEvent});
                track.Thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(0,12) {RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragDeltaEvent});
                track.Thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0,12,false) {RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragCompletedEvent});
            }
            await Dispatcher.Yield(DispatcherPriority.Render);Check("scroll thumb routes changes back to the viewport",viewer.VerticalOffset>35);
        }
        Capture(panel,Path.Combine(root,"Scroll-active.png"));
        await Task.Delay(350);Check("scrollbar remains visible briefly after scrolling",bar!=null && bar.Opacity>0.9);
        viewer.ScrollToVerticalOffset(65);await Dispatcher.Yield(DispatcherPriority.Render);await Task.Delay(750);
        Check("further scrolling restarts the idle countdown",bar!=null && bar.Opacity>0.9);
        await Task.Delay(1000);Check("idle scrollbar fades away after about one second",bar!=null && bar.Opacity<0.01 && !bar.IsHitTestVisible);
        Check("scrollbar activity never changes card widths",board.Children.OfType<FrameworkElement>().Select(x=>x.ActualWidth).SequenceEqual(sizes));
        Capture(panel,Path.Combine(root,"Scroll-idle.png"));
        viewer.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0) {RoutedEvent=Mouse.MouseEnterEvent});
        Check("hover alone does not reveal scrollbar",bar!=null && bar.Opacity<0.01);
        panel.Width=360;panel.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.Render);
        Check("resize alone neither reveals scrollbar nor unbalances gutters",bar!=null && bar.Opacity<0.01 && EqualGutters(panel));
        panel.Width=300;panel.UpdateLayout();viewer.ScrollToHome();await Dispatcher.Yield(DispatcherPriority.Render);await Task.Delay(1350);
    }
    sealed class Transport:HttpMessageHandler {
        internal readonly List<string> Hosts=new List<string>();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
            Hosts.Add(request.RequestUri.Host);
            string body=request.RequestUri.Host.Contains("deepseek")?"{\"balance_infos\":[{\"currency\":\"CNY\",\"total_balance\":\"7.25\"}]}":"{\"total\":{\"val\":\"-100\"}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content=new StringContent(body)});
        }
    }
    static async Task Run(string root) {
        Directory.CreateDirectory(root);
        Check("safe authorization feedback",PanelWindow.SafeError(new ProviderException("deepseek","sensitive",401))=="授权失败，请检查密钥或登录");
        Check("MiniMax ignores removed web login mode",!PanelWindow.UsesBrowser("minimax",new AccountSettings {UseWebLogin=true}));
        Check("Gemini uses browser login",PanelWindow.UsesBrowser("gemini",null));
        foreach(var kind in new [] {PanelKind.Api,PanelKind.Vpn}) {
            string dir=Path.Combine(root,kind.ToString()); Seed(dir,kind); var w=new PanelWindow(kind,dir); w.Show(); w.UpdateLayout();
            Check(kind+" compact opaque resizable non-topmost window",w.Width==300 && w.Height==250 && !w.Topmost && !w.AllowsTransparency && w.WindowStyle==WindowStyle.None && w.ResizeMode==ResizeMode.CanResize);
            var controls=All(w).ToList(); var names=controls.OfType<Button>().Select(x=>System.Windows.Automation.AutomationProperties.GetName(x)).ToList();
            Check(kind+" accessible four title actions",new [] {"刷新","设置","最小化","关闭"}.All(names.Contains));
            Check(kind+" title icon strokes fit without clipping",controls.OfType<Button>().All(IconFits));
            Check(kind+" no hover-dependent information",controls.OfType<FrameworkElement>().All(x=>x.ToolTip==null));
            Check(kind+" no fabricated startup reading",controls.OfType<TextBlock>().Any(x=>x.Text=="—"));
            Check(kind+" configured providers appear as cards",Order(w).SequenceEqual(kind==PanelKind.Api?new [] {"deepseek","openai","xai","mimo","minimax"}:new [] {"vpn"}));
            var first=Board(w)==null?null:Board(w).Children.OfType<FrameworkElement>().FirstOrDefault();
            Check(kind+" first card stays at the top even with one source",first!=null && first.TranslatePoint(new Point(0,0),w).Y<60);
            if(kind==PanelKind.Vpn) {
                Check("VPN single card retains its original left column slot",first!=null && Canvas.GetLeft(first)==0 && Canvas.GetTop(first)==0 && Math.Abs(first.Width*2+8-Board(w).ActualWidth)<1);
                var vpnView=controls.OfType<ScrollViewer>().Single(x=>x.Content==Board(w));vpnView.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,0,-120) {RoutedEvent=Mouse.PreviewMouseWheelEvent});
                var vpnBar=vpnView.Template.FindName("PART_VerticalScrollBar",vpnView) as System.Windows.Controls.Primitives.ScrollBar;
                Check("VPN without overflow never shows a scrollbar",vpnBar!=null && vpnBar.Visibility==Visibility.Collapsed && vpnBar.Opacity<0.01);
            }
            if(kind==PanelKind.Api) {
                var viewport=controls.OfType<ScrollViewer>().First(x=>x.Content==Board(w));
                var cells=Board(w).Children.OfType<FrameworkElement>().ToArray();
                Check("first viewport contains four whole cards and no fifth edge",Canvas.GetTop(cells[4])>=viewport.ViewportHeight && Canvas.GetTop(cells[2])+cells[2].ActualHeight<=viewport.ViewportHeight);
                foreach(Size size in new [] {new Size(280,230),new Size(360,310)}) {
                    w.Width=size.Width;w.Height=size.Height;w.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.Render);w.UpdateLayout();
                    Check("four-card viewport survives resize "+size,Canvas.GetTop(cells[4])>=viewport.ViewportHeight && Canvas.GetTop(cells[2])+cells[2].ActualHeight<=viewport.ViewportHeight);
                }
                w.Width=300;w.Height=250;w.UpdateLayout();
                await ScrollCases(w,root);
                Check("fifth card can be scrolled into view",controls.OfType<ScrollViewer>().Any(x=>x.ScrollableHeight>0));
                Rows(w)["deepseek"].Accept("CNY 123.45","fixture",new DateTime(2026,10,19,13,42,0)); await Dispatcher.Yield(DispatcherPriority.Render);
                Check("success time appears directly on panel",All(w).OfType<TextBlock>().Any(x=>x.Text=="13:42"));
                Rows(w)["deepseek"].Fail("synthetic timeout"); await Dispatcher.Yield(DispatcherPriority.Render);
                Check("failed refresh keeps visible value and time",All(w).OfType<TextBlock>().Any(x=>x.Text.Contains("123.45")) && All(w).OfType<TextBlock>().Any(x=>x.Text=="13:42"));
                Capture(w,Path.Combine(root,"Api.png"));
                Theme.Apply(w,false); await Dispatcher.Yield(DispatcherPriority.Render);
                var sourceCard=Board(w)==null?null:Board(w).Children.OfType<FrameworkElement>().FirstOrDefault(x=>(x.Tag as string)=="deepseek");
                var statusDot=sourceCard==null?null:All(sourceCard).OfType<System.Windows.Shapes.Ellipse>().FirstOrDefault();
                Check("status color follows a live dark theme change",statusDot!=null && object.Equals(statusDot.Fill,w.TryFindResource("Stale")));
                Check("header keeps stale status when the theme changes",object.Equals(w.Resources["StatusLight"],w.TryFindResource("Stale")));
                Capture(w,Path.Combine(root,"Api-dark.png")); Theme.Apply(w,true);
                Panel board=Board(w);ArmDrag(board);
                Rows(w)["mimo"].Accept("CNY 8.00","refresh during drag",DateTime.Now);
                Check("refresh does not commit preview order before release",Order(w).SequenceEqual(new [] {"deepseek","openai","xai","mimo","minimax"}));
                board.GetType().GetMethod("FinishDrag",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(board,new object[] {false});
                Check("canceling a drag preserves order",Order(w).SequenceEqual(new [] {"deepseek","openai","xai","mimo","minimax"}));
                var pressCard=All(board.Children[0]).OfType<Border>().First();
                pressCard.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left) {RoutedEvent=Mouse.MouseDownEvent});
                var armedField=board.GetType().GetField("armed",BindingFlags.Instance|BindingFlags.NonPublic);
                Check("routed press arms drag",armedField.GetValue(board)!=null);
                board.GetType().GetField("pressed",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(board,new Point(-1000,-1000));
                pressCard.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0) {RoutedEvent=Mouse.MouseMoveEvent});
                Check("movement before long press does not permanently cancel drag",armedField.GetValue(board)!=null);
                board.GetType().GetMethod("FinishDrag",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(board,new object[] {false});
                ArmDrag(board);
                pressCard.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0) {RoutedEvent=Mouse.MouseMoveEvent});
                Call(board,"FinishAt",new Point(150,150));
                Check("releasing a drag commits exactly the target position",Order(w).SequenceEqual(new [] {"openai","xai","mimo","deepseek","minimax"}));
                w.Width=360; w.Height=310; w.Left=140; w.Top=120; w.Close();
                var again=new PanelWindow(kind,dir); again.Show(); again.UpdateLayout();
                Check("drop order survives restart",Order(again).SequenceEqual(new [] {"openai","xai","mimo","deepseek","minimax"}));
                Check("size and location survive restart",again.Width==360 && again.Height==310 && again.Left==140 && again.Top==120); again.Close();
            } else {Capture(w,Path.Combine(root,"Vpn.png")); w.Close();}
        }
        var empty=new PanelWindow(PanelKind.Api,Path.Combine(root,"empty")); empty.Show(); empty.UpdateLayout();
        Check("unconfigured providers hidden with a settings hint",Order(empty).Length==0 && All(empty).OfType<TextBlock>().Any(x=>x.Text.Contains("设置"))); empty.Close();
        string hiddenDir=Path.Combine(root,"hidden"); var store=new SecureSettings(Path.Combine(hiddenDir,"settings"));
        store.Save("deepseek",new AccountSettings {ApiKey="fixture"}); store.Save("xai",new AccountSettings {ApiKey="fixture",TeamId="fixture"});
        File.WriteAllText(Path.Combine(hiddenDir,"panel.json"),"{\"Hidden\":[\"xai\"]}");
        var hidden=new PanelWindow(PanelKind.Api,hiddenDir); hidden.Show(); hidden.UpdateLayout(); var transport=new Transport();
        FieldInfo client=typeof(PanelWindow).GetField("client",BindingFlags.Instance|BindingFlags.NonPublic); ((ProviderClient)client.GetValue(hidden)).Dispose(); client.SetValue(hidden,new ProviderClient(transport));
        await (Task)typeof(PanelWindow).GetMethod("RefreshAllAsync",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(hidden,new object[] {true});
        Check("hidden providers never request while visible provider refreshes",transport.Hosts.SequenceEqual(new [] {"api.deepseek.com"}) && Rows(hidden)["deepseek"].Value=="CNY 7.25");
        Check("hidden provider removed from board",Order(hidden).SequenceEqual(new [] {"deepseek"}));
        typeof(PanelWindow).GetMethod("Browser",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(hidden,new object[] {"mimo","cn"});
        typeof(PanelWindow).GetMethod("RebuildVisible",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(hidden,null);
        var sessions=(Dictionary<string,BrowserSession>)typeof(PanelWindow).GetField("browsers",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(hidden);
        Check("hidden browser pages are released rather than left refreshing in background",sessions.Count==0);
        int before=transport.Hosts.Count;
        EditSettings(hidden,delegate(SettingsWindow d) {All(d).OfType<CheckBox>().Single().IsChecked=false;Click(d,"取消");});
        Check("cancel changes neither visibility nor requests",Order(hidden).SequenceEqual(new [] {"deepseek"}) && transport.Hosts.Count==before);
        EditSettings(hidden,delegate(SettingsWindow d) {All(d).OfType<CheckBox>().Single().IsChecked=false;Click(d,"保存");});
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check("saving hidden state stops requests without deleting account",Order(hidden).Length==0 && transport.Hosts.Count==before && store.Load("deepseek").ApiKey=="fixture");
        EditSettings(hidden,delegate(SettingsWindow d) {All(d).OfType<CheckBox>().Single().IsChecked=true;Click(d,"保存");});
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check("restoring visibility resumes real provider pipeline",Order(hidden).SequenceEqual(new [] {"deepseek"}) && transport.Hosts.Count==before+1);
        hidden.Width=360;hidden.Height=310;
        hidden.Left=140;hidden.Top=120;
        EditSettings(hidden,delegate(SettingsWindow d) {Check("settings opens at panel position",Math.Abs(d.Left-140)<1 && Math.Abs(d.Top-120)<1);Click(d,"恢复默认窗口大小");Click(d,"取消");});
        Check("reset size restores main panel geometry",hidden.Width==300 && hidden.Height==250);
        hidden.Left=SystemParameters.WorkArea.Right-100;hidden.Top=SystemParameters.WorkArea.Bottom-100;
        EditSettings(hidden,delegate(SettingsWindow d) {Check("settings stays inside the screen edge",d.Left+d.Width<=SystemParameters.WorkArea.Right+1 && d.Top+d.Height<=SystemParameters.WorkArea.Bottom+1);Click(d,"取消");});
        hidden.Close();
        var settings=new SettingsWindow(PanelKind.Api,store,delegate(string id) {},delegate(string id,string site) {return Task.FromResult(true);});
        settings.Show(); settings.UpdateLayout(); var fields=All(settings).ToList();
        Check("settings uses approved compact footprint",settings.Width==300 && settings.Height==250);
        Check("settings removes explanatory paragraphs",!fields.OfType<TextBlock>().Any(x=>x.Text.Contains("显示充值") || x.Text.Contains("留空保留")));
        var visibility=fields.OfType<CheckBox>().Single();visibility.ApplyTemplate();
        Check("visibility uses a sliding switch",visibility.Template.FindName("Knob",visibility)!=null);
        var knob=visibility.Template.FindName("Knob",visibility) as FrameworkElement;
        visibility.IsChecked=false;await Task.Delay(160);double off=knob==null?0:((TranslateTransform)knob.RenderTransform).X;
        visibility.IsChecked=true;await Task.Delay(160);
        Check("switch knob moves between off and on",knob!=null && ((TranslateTransform)knob.RenderTransform).X-off>=13);
        Check("settings have provider list and visibility switch",fields.OfType<ListBox>().Any() && fields.OfType<CheckBox>().Any(x=>(x.Content as string)=="显示在面板上"));
        Check("settings offer save cancel reset",new [] {"保存","取消","恢复默认窗口大小"}.All(label=>fields.OfType<Button>().Any(x=>(x.Content as string)==label)));
        Check("saved secrets never prefilled",fields.OfType<PasswordBox>().All(x=>x.Password.Length==0));
        Check("secret fields have accessible names",fields.OfType<PasswordBox>().All(x=>!string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(x))));
        var list=fields.OfType<ListBox>().FirstOrDefault();
        list.ApplyTemplate();await Dispatcher.Yield(DispatcherPriority.Loaded);settings.UpdateLayout();
        var providerScroll=list.Template.FindName("PART_ProviderScroll",list) as ScrollViewer;
        Check("provider list exposes overlay scroll viewer",providerScroll!=null);
        if(providerScroll!=null) {
            Check("provider list disables horizontal scrollbar",providerScroll.HorizontalScrollBarVisibility==ScrollBarVisibility.Disabled);
            providerScroll.ApplyTemplate();
            var providerBar=providerScroll.Template.FindName("PART_VerticalScrollBar",providerScroll) as System.Windows.Controls.Primitives.ScrollBar;
            Check("provider list uses floating vertical scrollbar",providerBar!=null);
            Check("provider list scrollbar starts hidden",providerBar!=null && providerBar.Opacity<0.01 && !providerBar.IsHitTestVisible);
            Check("provider list scrollbar does not reserve layout width",Math.Abs(providerScroll.ViewportWidth-providerScroll.ActualWidth)<1);
            if(providerScroll.ScrollableHeight>0) {
                providerScroll.ScrollToVerticalOffset(Math.Min(40,providerScroll.ScrollableHeight));
                await Dispatcher.Yield(DispatcherPriority.Render);settings.UpdateLayout();
                Check("provider list scroll reveals thin scrollbar",providerBar!=null && providerBar.Opacity>0.9 && providerBar.IsHitTestVisible);
                await Task.Delay(350);Check("provider list scrollbar stays briefly after scroll",providerBar!=null && providerBar.Opacity>0.9);
                await Task.Delay(1000);Check("provider list scrollbar auto-hides after idle",providerBar!=null && providerBar.Opacity<0.01 && !providerBar.IsHitTestVisible);
            }
        }
        var formScroll=fields.OfType<ScrollViewer>().FirstOrDefault(x=>x!=providerScroll);
        Check("settings form uses overlay scroll viewer",formScroll!=null);
        if(formScroll!=null) {
            Check("settings form disables horizontal scrollbar",formScroll.HorizontalScrollBarVisibility==ScrollBarVisibility.Disabled);
            formScroll.ApplyTemplate();
            var formBar=formScroll.Template.FindName("PART_VerticalScrollBar",formScroll) as System.Windows.Controls.Primitives.ScrollBar;
            Check("settings form uses floating vertical scrollbar",formBar!=null);
            Check("settings form scrollbar starts hidden",formBar!=null && formBar.Opacity<0.01 && !formBar.IsHitTestVisible);
        }
        for(int i=0;i<9;i++) {
            list.SelectedIndex=i;settings.UpdateLayout();
            var inputs=All(settings).OfType<Control>().Where(x=>x.IsVisible && (x is PasswordBox || x is TextBox || x is ComboBox));
            Check("provider fields fit without scrolling "+i,inputs.All(x=>x.TranslatePoint(new Point(0,x.ActualHeight),settings).Y<=205));
        }
        settings.UpdateLayout();await Task.Delay(160);
        Check("MiniMax removed instruction stays removed",!All(settings).OfType<TextBlock>().Any(x=>x.Text.Contains("sk-api-")));
        Capture(settings,Path.Combine(root,"Settings.png"));Theme.Apply(settings,false);Capture(settings,Path.Combine(root,"Settings-dark.png")); settings.Close();
        await LiveDragCases(root);
    }
    static void Preview(string root) {
        Seed(root,PanelKind.Api);var app=new Application();var panel=new PanelWindow(PanelKind.Api,root);panel.Title="余额面板验收 · 示例数据";
        var rows=Rows(panel);DateTime now=new DateTime(2026,10,19,13,42,0);
        rows["deepseek"].Accept("CNY 1284.60","合成数据",now);rows["openai"].Accept("USD 42.15","合成数据",now);rows["gemini"].Accept("USD 18.00","合成数据",now);rows["xai"].Accept("USD 120.00","合成数据",now);rows["mimo"].Accept("CNY 86.20","合成数据",now);rows["qwen"].Accept("CNY 55.00","合成数据",now);rows["glm"].Accept("CNY 30.00","合成数据",now);rows["kimi"].Accept("CNY 22.00","合成数据",now);rows["minimax"].Accept("CNY 12.30","合成数据",now);
        var toggle=new System.Windows.Input.RoutedCommand();bool dark=false;
        panel.CommandBindings.Add(new System.Windows.Input.CommandBinding(toggle,delegate {dark=!dark;Theme.Apply(panel,!dark);}));
        panel.InputBindings.Add(new System.Windows.Input.KeyBinding(toggle,System.Windows.Input.Key.F6,System.Windows.Input.ModifierKeys.None));
        app.Run(panel);
    }
    [STAThread] public static int Main(string[] args) {
        if(args[0]=="--preview") {Preview(args[1]);return 0;}
        var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        app.Dispatcher.BeginInvoke(new Action(async delegate {
            try {await Run(Path.Combine(args[0],Guid.NewGuid().ToString("N")));}
            catch(Exception ex) {failed++;Exception cause=ex.GetBaseException(); Console.WriteLine("FAIL execution "+cause.GetType().Name+": "+cause.Message+"\n"+cause.StackTrace);}
            Console.WriteLine("UI: "+passed+" passed, "+failed+" failed"); app.Shutdown();
        })); app.Run(); return failed==0?0:1;
    }
}
