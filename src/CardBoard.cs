using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace BalancePanel
{
    internal sealed class CardBoard : Canvas
    {
        private sealed class Card
        {
            internal string Id;
            internal Grid Cell;
            internal Border View;
            internal TranslateTransform Move;
            internal ScaleTransform Scale;
        }
        private readonly List<Card> cards=new List<Card>();
        private readonly ScrollViewer scroll;
        private readonly DispatcherTimer hold=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(260)};
        private Card armed,dragging;
        private Point pressed,grab;
        private List<Card> preview;
        internal event Action<string[]> OrderChanged;

        internal CardBoard(ScrollViewer viewport)
        {
            scroll=viewport; Name="CardBoard"; Focusable=true; Background=Brushes.Transparent; VerticalAlignment=VerticalAlignment.Top;
            hold.Tick+=delegate {hold.Stop(); if(armed!=null && Mouse.LeftButton==MouseButtonState.Pressed) StartDrag();};
            SizeChanged+=delegate {if(dragging==null) LayoutCards(false);};
            scroll.SizeChanged+=delegate {if(dragging==null) LayoutCards(false);};
            scroll.ScrollChanged+=delegate(object sender,ScrollChangedEventArgs e) {if(dragging==null && e.ViewportHeightChange!=0) LayoutCards(false);};
            KeyDown+=delegate(object sender,KeyEventArgs e) {if(e.Key==Key.Escape) FinishDrag(false);};
            Unloaded+=delegate {FinishDrag(false); hold.Stop();};
        }
        internal void Bind(IEnumerable<string> ids,Dictionary<string,RowState> rows)
        {
            FinishDrag(false); cards.Clear(); Children.Clear();
            foreach(string id in ids) {Card card=Create(id,rows[id]); cards.Add(card); Children.Add(card.Cell);}
            LayoutCards(false);
        }
        private Card Create(string id,RowState row)
        {
            var card=new Card {Id=id,Cell=new Grid {Tag=id,DataContext=row},Move=new TranslateTransform(),Scale=new ScaleTransform(1,1)};
            var transforms=new TransformGroup(); transforms.Children.Add(card.Scale); transforms.Children.Add(card.Move);
            card.Cell.RenderTransform=transforms; card.Cell.RenderTransformOrigin=new Point(0.5,0.5);
            card.View=new Border {CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(0.5),Padding=new Thickness(10,7,10,7)};
            card.View.SetResourceReference(Border.BackgroundProperty,"CardFill"); card.View.SetResourceReference(Border.BorderBrushProperty,"CardStroke");
            card.View.Effect=Shadow(0.10); var content=new StackPanel {VerticalAlignment=VerticalAlignment.Center}; content.Children.Add(Theme.Text(SourceCatalog.Name(id),12.5,"Dim"));
            var amount=Theme.Text("—",18.5,"Ink"); amount.FontWeight=FontWeights.SemiBold;
            System.Windows.Documents.Typography.SetNumeralAlignment(amount,FontNumeralAlignment.Tabular);
            amount.SetBinding(TextBlock.TextProperty,new Binding("Value") {Converter=new AmountConverter()});
            var fit=new Viewbox {Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,HorizontalAlignment=HorizontalAlignment.Left,Height=26,Margin=new Thickness(0,3,0,0),Child=amount}; content.Children.Add(fit);
            var foot=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,4,0,0)};
            var dot=new Ellipse {Width=5,Height=5,VerticalAlignment=VerticalAlignment.Center};
            var stateStyle=new Style(typeof(Ellipse)); stateStyle.Setters.Add(new Setter(Shape.FillProperty,new DynamicResourceExtension("Stale")));
            var fresh=new DataTrigger {Binding=new Binding("IsFresh"),Value=true}; fresh.Setters.Add(new Setter(Shape.FillProperty,new DynamicResourceExtension("Ok")));stateStyle.Triggers.Add(fresh);dot.Style=stateStyle;
            dot.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty,new Binding("Status")); foot.Children.Add(dot);
            var time=Theme.Text("—",11,"Faint"); time.Margin=new Thickness(5,0,0,0); time.SetBinding(TextBlock.TextProperty,new Binding("Time")); foot.Children.Add(time); content.Children.Add(foot);
            card.View.Child=content; card.Cell.Children.Add(card.View);
            card.View.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e) {
                if(dragging!=null) return; armed=card; pressed=e.GetPosition(this); hold.Stop(); hold.Start(); card.View.CaptureMouse(); Focus(); e.Handled=true;
            };
            card.View.MouseMove+=delegate(object sender,MouseEventArgs e) {
                // Capture can synchronously raise a move; keep the pending long press alive.
                if(dragging==null) return;
                // MouseUp owns the commit, including when a released move is delivered first.
                if(e.LeftButton!=MouseButtonState.Pressed) return;
                UpdateDrag(e.GetPosition(this));
            };
            card.View.MouseLeftButtonUp+=delegate(object sender,MouseButtonEventArgs e) {Window owner=Window.GetWindow(this);if(owner==null) FinishDrag(false);else FinishAt(e.GetPosition(owner));e.Handled=true;};
            card.View.LostMouseCapture+=delegate {if(armed==card || dragging==card) FinishDrag(false);};
            return card;
        }
        private static DropShadowEffect Shadow(double opacity) {return new DropShadowEffect {BlurRadius=8,ShadowDepth=1,Direction=270,Color=Colors.Black,Opacity=opacity};}
        private void StartDrag()
        {
            dragging=armed; preview=new List<Card>(cards);
            grab=new Point(pressed.X-GetLeft(dragging.Cell),pressed.Y-GetTop(dragging.Cell));
            SetZIndex(dragging.Cell,100); dragging.View.Effect=Shadow(0.22); dragging.View.SetResourceReference(Border.BorderBrushProperty,"Accent");
            Scale(dragging,1.02); Cursor=Cursors.Hand;
        }
        private void UpdateDrag(Point now)
        {
            if(dragging==null) return;
            dragging.Move.BeginAnimation(TranslateTransform.XProperty,null);dragging.Move.BeginAnimation(TranslateTransform.YProperty,null);
            dragging.Move.X=now.X-GetLeft(dragging.Cell)-grab.X;dragging.Move.Y=now.Y-GetTop(dragging.Cell)-grab.Y;
            Point inView=TranslatePoint(now,scroll);
            if(inView.X<0 || inView.X>=scroll.ViewportWidth || inView.Y<0 || inView.Y>=scroll.ViewportHeight) return;
            PreviewInsertion(now);
            if(inView.Y<12) scroll.ScrollToVerticalOffset(scroll.VerticalOffset-6);
            else if(inView.Y>scroll.ViewportHeight-12) scroll.ScrollToVerticalOffset(scroll.VerticalOffset+6);
        }
        private void PreviewInsertion(Point point)
        {
            double width=dragging.Cell.Width,height=dragging.Cell.Height;
            if(point.X<0 || point.Y<0 || width<=0 || height<=0) return;
            int column=(int)(point.X/(width+8)),row=(int)(point.Y/(height+8)),index=row*2+column;
            if(column>1 || index>=preview.Count || point.X-column*(width+8)>width || point.Y-row*(height+8)>height) return;
            int current=preview.IndexOf(dragging);
            if(index==current) return;
            // Test fixed slot centers, never animated card bounds, to avoid oscillation.
            double position=current/2==row?point.X:point.Y;
            double middle=current/2==row?column*(width+8)+width/2:row*(height+8)+height/2;
            if(index>current?position<middle:position>middle) return;
            preview.Remove(dragging);preview.Insert(index,dragging);LayoutCards(true);
        }
        private void FinishAt(Point inWindow)
        {
            Window owner=Window.GetWindow(this);
            FinishDrag(owner!=null && inWindow.X>=0 && inWindow.Y>=0 && inWindow.X<owner.ActualWidth && inWindow.Y<owner.ActualHeight);
        }
        private void FinishDrag(bool commit)
        {
            hold.Stop(); Card card=dragging??armed;
            bool changed=commit && preview!=null && !preview.SequenceEqual(cards);
            if(changed) {cards.Clear();cards.AddRange(preview);}
            dragging=null;armed=null;preview=null;Cursor=null;
            if(card==null) return;
            card.View.ReleaseMouseCapture(); SetZIndex(card.Cell,0); card.View.Effect=Shadow(0.10); card.View.SetResourceReference(Border.BorderBrushProperty,"CardStroke"); Scale(card,1);
            if(changed) for(int i=0;i<cards.Count;i++) if(Children[i]!=cards[i].Cell) {Children.Remove(cards[i].Cell);Children.Insert(i,cards[i].Cell);}
            LayoutCards(true,card);
            if(changed) {Action<string[]> saved=OrderChanged;if(saved!=null) saved(cards.Select(x=>x.Id).ToArray());}
        }
        private void LayoutCards(bool animate,Card settling=null)
        {
            if(ActualWidth<=0) return;
            double width=Math.Max(0,(ActualWidth-8)/2),height=Math.Max(78,Math.Floor((scroll.ViewportHeight-8)/2));
            Height=Math.Max(0,Math.Ceiling(cards.Count/2.0)*(height+8)-8);
            IList<Card> ordered=preview??cards;
            for(int i=0;i<ordered.Count;i++) {
                Card card=ordered[i];if(card==dragging) continue;
                double left=(i%2)*(width+8),top=(i/2)*(height+8);
                double oldX=double.IsNaN(GetLeft(card.Cell))?left:GetLeft(card.Cell)+card.Move.X;
                double oldY=double.IsNaN(GetTop(card.Cell))?top:GetTop(card.Cell)+card.Move.Y;
                card.Cell.Width=width; card.Cell.Height=height; SetLeft(card.Cell,left); SetTop(card.Cell,top);
                card.Move.BeginAnimation(TranslateTransform.XProperty,null); card.Move.BeginAnimation(TranslateTransform.YProperty,null);
                card.Move.X=animate?oldX-left:0; card.Move.Y=animate?oldY-top:0;
                if(animate) {
                    int duration=card==settling?140:180;
                    card.Move.BeginAnimation(TranslateTransform.XProperty,Animation(0,duration));
                    card.Move.BeginAnimation(TranslateTransform.YProperty,Animation(0,duration));
                }
            }
        }
        private static DoubleAnimation Animation(double value,int duration) {return new DoubleAnimation(value,TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation?duration:0)) {EasingFunction=new CubicEase {EasingMode=EasingMode.EaseOut}};}
        private static void Scale(Card card,double value) {card.Scale.BeginAnimation(ScaleTransform.ScaleXProperty,Animation(value,90));card.Scale.BeginAnimation(ScaleTransform.ScaleYProperty,Animation(value,90));}
        private sealed class AmountConverter:IValueConverter
        {
            public object Convert(object value,Type targetType,object parameter,CultureInfo culture) {return ((string)value??"—").Replace("CNY ","¥").Replace("USD ","$");}
            public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture) {throw new NotSupportedException();}
        }
    }
}
