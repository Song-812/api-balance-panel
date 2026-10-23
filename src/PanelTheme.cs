using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace BalancePanel
{
    internal static class Theme
    {
        internal static bool IsLight() {
            try {using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) return key==null || !object.Equals(key.GetValue("AppsUseLightTheme"),0);}
            catch {return true;}
        }
        internal static void Apply(Window window,bool light) {
            bool wasStale=window.Resources.Contains("Stale") && object.ReferenceEquals(window.Resources["StatusLight"],window.Resources["Stale"]);
            string[] names={"Ink","Dim","Faint","CardFill","CardStroke","Line","Input","Hover","Accent","Ok","Stale"};
            string[] values=light ? new [] {"#16202B","#5B6573","#77818F","#FFFFFF","#14000000","#22000000","#FFFFFF","#16000000","#24745A","#34C759","#E99300"}
                : new [] {"#F7F7FA","#B9B9BF","#A0A0AC","#343438","#26FFFFFF","#30FFFFFF","#303035","#24FFFFFF","#70CDA7","#32D74B","#FFD60A"};
            for(int i=0;i<names.Length;i++) window.Resources[names[i]]=(Brush)new BrushConverter().ConvertFromString(values[i]);
            window.Resources["WindowBackground"]=new LinearGradientBrush((Color)ColorConverter.ConvertFromString(light?"#F8FBFF":"#26262A"),(Color)ColorConverter.ConvertFromString(light?"#E9EFF8":"#151518"),168);
            window.Resources["StatusLight"]=window.Resources[wasStale?"Stale":"Ok"];
            window.SetResourceReference(Control.ForegroundProperty,"Ink"); window.SetResourceReference(Control.BackgroundProperty,"WindowBackground");
            if(new WindowInteropHelper(window).Handle!=IntPtr.Zero) SetDwm(window,!light);
        }
        internal static void Window(Window window) {
            window.WindowStyle=WindowStyle.None; window.FontFamily=new FontFamily("Segoe UI, Microsoft YaHei UI"); window.FontSize=13;
            window.WindowStartupLocation=WindowStartupLocation.CenterScreen;
            window.UseLayoutRounding=true; window.SnapsToDevicePixels=true;
            TextOptions.SetTextFormattingMode(window,TextFormattingMode.Display); TextOptions.SetTextRenderingMode(window,TextRenderingMode.ClearType);
            WindowChrome.SetWindowChrome(window,new WindowChrome {CaptionHeight=0,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(0)});
            Apply(window,IsLight());
            window.SourceInitialized+=delegate {
                var source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
                source.AddHook(delegate(IntPtr handle,int message,IntPtr w,IntPtr l,ref bool handled) {if(message==0x001A || message==0x031A) Apply(window,IsLight()); return IntPtr.Zero;});
                SetDwm(window,!IsLight());
            };
        }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
        private static void SetDwm(Window window,bool dark) {
            try {IntPtr handle=new WindowInteropHelper(window).Handle; int rounded=2,mode=dark?1:0; DwmSetWindowAttribute(handle,33,ref rounded,4); DwmSetWindowAttribute(handle,20,ref mode,4);} catch(DllNotFoundException) {} catch(EntryPointNotFoundException) {}
        }
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect {internal int Left,Top,Right,Bottom;}
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo {internal int Size;internal NativeRect Monitor,Work;internal int Flags;}
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
        [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
        internal static void PlaceAtOwner(Window dialog,Window owner) {
            Rect area=SystemParameters.WorkArea;
            var source=PresentationSource.FromVisual(owner);var info=new MonitorInfo {Size=Marshal.SizeOf(typeof(MonitorInfo))};
            if(source!=null && GetMonitorInfo(MonitorFromWindow(new WindowInteropHelper(owner).Handle,2),ref info)) {
                Point screen=owner.PointToScreen(new Point(0,0));Matrix scale=source.CompositionTarget.TransformFromDevice;
                area=new Rect(owner.Left+(info.Work.Left-screen.X)*scale.M11,owner.Top+(info.Work.Top-screen.Y)*scale.M22,(info.Work.Right-info.Work.Left)*scale.M11,(info.Work.Bottom-info.Work.Top)*scale.M22);
            }
            dialog.WindowStartupLocation=WindowStartupLocation.Manual;
            dialog.Left=Math.Max(area.Left,Math.Min(owner.Left,area.Right-dialog.Width));
            dialog.Top=Math.Max(area.Top,Math.Min(owner.Top,area.Bottom-dialog.Height));
        }
        internal static TextBlock Text(string text,double size,string color) {
            var block=new TextBlock {Text=text,FontSize=size,VerticalAlignment=VerticalAlignment.Center}; block.SetResourceReference(TextBlock.ForegroundProperty,color); return block;
        }
        internal static Button Button(string label) {
            var button=new Button {Content=label,Padding=new Thickness(12,6,12,6),MinHeight=30,Cursor=Cursors.Hand,FontSize=12};
            button.SetResourceReference(Control.BackgroundProperty,"Input"); button.SetResourceReference(Control.ForegroundProperty,"Ink"); button.SetResourceReference(Control.BorderBrushProperty,"Line");
            var border=new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty,new CornerRadius(6)); border.SetValue(Border.BorderThicknessProperty,new Thickness(0.5));
            foreach(var pair in new [] {new {Property=Border.BackgroundProperty,Path="Background"},new {Property=Border.BorderBrushProperty,Path="BorderBrush"},new {Property=Border.PaddingProperty,Path="Padding"}})
                border.SetBinding(pair.Property,new Binding(pair.Path) {RelativeSource=RelativeSource.TemplatedParent});
            var content=new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center); content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center); border.AppendChild(content);
            var template=new ControlTemplate(typeof(Button)) {VisualTree=border};
            var hover=new Trigger {Property=UIElement.IsMouseOverProperty,Value=true}; hover.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension("Hover"))); template.Triggers.Add(hover);
            var focus=new Trigger {Property=UIElement.IsKeyboardFocusedProperty,Value=true}; focus.Setters.Add(new Setter(Control.BorderBrushProperty,new DynamicResourceExtension("Accent"))); template.Triggers.Add(focus);
            var disabled=new Trigger {Property=UIElement.IsEnabledProperty,Value=false}; disabled.Setters.Add(new Setter(UIElement.OpacityProperty,0.45)); template.Triggers.Add(disabled);
            button.Template=template; AutomationProperties.SetName(button,label); return button;
        }
        internal static Button Icon(string label,string geometry,Action action) {
            var path=new Path {Data=Geometry.Parse(geometry),StrokeThickness=1.25,Width=12,Height=12,Stretch=Stretch.Uniform,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round};
            path.SetResourceReference(Shape.StrokeProperty,"Faint"); var button=Button(label); button.Content=path; button.Width=24; button.Height=24; button.MinHeight=24; button.Padding=new Thickness(3); button.BorderThickness=new Thickness(0); button.BorderBrush=Brushes.Transparent; button.Background=Brushes.Transparent;
            button.Click+=delegate {action();}; return button;
        }
        internal static Grid Header(Window window,string title,params Button[] actions) {
            var bar=new Grid {Height=38,Background=Brushes.Transparent}; bar.ColumnDefinitions.Add(new ColumnDefinition()); bar.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            var left=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(13,0,0,0),VerticalAlignment=VerticalAlignment.Center};
            var dot=new Ellipse {Width=7,Height=7,Margin=new Thickness(0,0,7,0)}; dot.SetResourceReference(Shape.FillProperty,"StatusLight"); left.Children.Add(dot); left.Children.Add(Text(title,12.5,"Dim")); bar.Children.Add(left);
            var right=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,6,0),VerticalAlignment=VerticalAlignment.Center}; foreach(var button in actions) right.Children.Add(button); Grid.SetColumn(right,1); bar.Children.Add(right);
            bar.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e) {
                DependencyObject hit=e.OriginalSource as DependencyObject;
                while(hit!=null && hit!=bar) {if(hit is Button) return; hit=VisualTreeHelper.GetParent(hit);}
                if(e.ClickCount==2 && window.ResizeMode==ResizeMode.CanResize) window.WindowState=window.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
                else try {window.DragMove();} catch(InvalidOperationException) {}
            }; return bar;
        }
        internal static Border Shell(UIElement child) {
            var shell=new Border {BorderThickness=new Thickness(0.5),Child=child}; shell.SetResourceReference(Border.BackgroundProperty,"WindowBackground"); shell.SetResourceReference(Border.BorderBrushProperty,"Line"); return shell;
        }
        internal static void CompactScroll(ScrollViewer viewer) {
            viewer.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)]=System.Windows.Markup.XamlReader.Parse(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ScrollBar}'>
 <Setter Property='Width' Value='5'/><Setter Property='MinWidth' Value='5'/><Setter Property='Background' Value='Transparent'/>
 <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ScrollBar}'>
  <Track x:Name='PART_Track' IsDirectionReversed='True' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}' ViewportSize='{TemplateBinding ViewportSize}'>
   <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'><Border Background='{DynamicResource Faint}' CornerRadius='3' Opacity='.35'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
  </Track>
 </ControlTemplate></Setter.Value></Setter>
</Style>");
        }
        internal static void PanelScroll(ScrollViewer viewer) {
            CompactScroll(viewer);
            viewer.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ScrollViewer}'>
 <Grid Background='Transparent' ClipToBounds='False'>
  <ScrollContentPresenter x:Name='PART_ScrollContentPresenter' Content='{TemplateBinding Content}' ContentTemplate='{TemplateBinding ContentTemplate}' CanContentScroll='{TemplateBinding CanContentScroll}' Margin='{TemplateBinding Padding}' ClipToBounds='True'/>
  <ScrollBar x:Name='PART_VerticalScrollBar' Orientation='Vertical' HorizontalAlignment='Right' Minimum='0' Maximum='{TemplateBinding ScrollableHeight}' ViewportSize='{TemplateBinding ViewportHeight}' Value='{TemplateBinding VerticalOffset}' Visibility='{TemplateBinding ComputedVerticalScrollBarVisibility}' Opacity='0' IsHitTestVisible='False' Focusable='False'>
   <ScrollBar.RenderTransform><TranslateTransform X='7'/></ScrollBar.RenderTransform>
  </ScrollBar>
 </Grid>
</ControlTemplate>");
            var idle=new DispatcherTimer {Interval=TimeSpan.FromSeconds(1)};
            Func<System.Windows.Controls.Primitives.ScrollBar> scrollbar=delegate {return viewer.Template.FindName("PART_VerticalScrollBar",viewer) as System.Windows.Controls.Primitives.ScrollBar;};
            Action reset=delegate {
                idle.Stop();var bar=scrollbar();if(bar==null) return;
                bar.BeginAnimation(UIElement.OpacityProperty,null);bar.Opacity=0;bar.IsHitTestVisible=false;
            };
            Action reveal=delegate {
                if(!viewer.IsLoaded || viewer.ScrollableHeight<=0) return;
                var bar=scrollbar();if(bar==null) return;
                bar.BeginAnimation(UIElement.OpacityProperty,null);bar.Opacity=1;bar.IsHitTestVisible=true;idle.Stop();idle.Start();
            };
            idle.Tick+=delegate {
                var bar=scrollbar();if(bar==null) {idle.Stop();return;}
                if(bar.IsMouseCaptureWithin) return;
                idle.Stop();bar.IsHitTestVisible=false;
                bar.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation?160:0)));
            };
            viewer.ScrollChanged+=delegate(object sender,ScrollChangedEventArgs e) {
                if(e.OriginalSource==viewer && e.VerticalChange!=0 && e.ViewportHeightChange==0 && e.ExtentHeightChange==0) reveal();
            };
            viewer.PreviewMouseWheel+=delegate(object sender,MouseWheelEventArgs e) {if(e.Delta!=0) reveal();};
            viewer.Loaded+=delegate {reset();};viewer.Unloaded+=delegate {reset();};
        }
        internal static void ProviderList(ListBox list) {
            ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(list,ScrollBarVisibility.Auto);
            list.ItemContainerStyle=(Style)System.Windows.Markup.XamlReader.Parse(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ListBoxItem}'>
 <Setter Property='Foreground' Value='{DynamicResource Ink}'/><Setter Property='Background' Value='Transparent'/>
 <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ListBoxItem}'>
  <Border x:Name='Row' CornerRadius='6' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'><ContentPresenter/></Border>
  <ControlTemplate.Triggers><Trigger Property='IsSelected' Value='True'><Setter TargetName='Row' Property='Background' Value='{DynamicResource Hover}'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Row' Property='Background' Value='{DynamicResource Hover}'/></Trigger></ControlTemplate.Triggers>
 </ControlTemplate></Setter.Value></Setter>
</Style>");
            list.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ListBox}'>
 <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}' SnapsToDevicePixels='True' ClipToBounds='False'>
  <ScrollViewer x:Name='PART_ProviderScroll' Focusable='False' HorizontalScrollBarVisibility='Disabled' VerticalScrollBarVisibility='Auto' CanContentScroll='True' Padding='0'>
   <ItemsPresenter SnapsToDevicePixels='{TemplateBinding SnapsToDevicePixels}'/>
  </ScrollViewer>
 </Border>
</ControlTemplate>");
            RoutedEventHandler wire=null;
            wire=delegate {
                list.Loaded-=wire;
                list.ApplyTemplate();
                var viewer=list.Template.FindName("PART_ProviderScroll",list) as ScrollViewer;
                if(viewer!=null) PanelScroll(viewer);
            };
            if(list.IsLoaded) wire(list,new RoutedEventArgs());
            else list.Loaded+=wire;
        }
        internal static void SlidingSwitch(CheckBox control) {
            control.Cursor=Cursors.Hand;
            control.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type CheckBox}'>
 <Grid Background='Transparent' Height='22'>
  <Grid.ColumnDefinitions><ColumnDefinition Width='32'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
  <Border x:Name='Track' Height='18' CornerRadius='9' Background='{DynamicResource Faint}' VerticalAlignment='Center'>
   <Ellipse x:Name='Knob' Width='14' Height='14' Fill='White' HorizontalAlignment='Left' Margin='2,0,0,0'><Ellipse.RenderTransform><TranslateTransform/></Ellipse.RenderTransform></Ellipse>
  </Border>
  <ContentPresenter Grid.Column='1' Margin='7,0,0,0' VerticalAlignment='Center'/>
 </Grid>
 <ControlTemplate.Triggers>
  <Trigger Property='IsChecked' Value='True'>
   <Setter TargetName='Track' Property='Background' Value='{DynamicResource Accent}'/>
   <Trigger.EnterActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='Knob' Storyboard.TargetProperty='(UIElement.RenderTransform).(TranslateTransform.X)' To='14' Duration='0:0:0.12'/></Storyboard></BeginStoryboard></Trigger.EnterActions>
   <Trigger.ExitActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='Knob' Storyboard.TargetProperty='(UIElement.RenderTransform).(TranslateTransform.X)' To='0' Duration='0:0:0.12'/></Storyboard></BeginStoryboard></Trigger.ExitActions>
  </Trigger>
  <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Track' Property='BorderBrush' Value='{DynamicResource Ink}'/><Setter TargetName='Track' Property='BorderThickness' Value='1'/></Trigger>
  <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.45'/></Trigger>
 </ControlTemplate.Triggers>
</ControlTemplate>");
        }
        internal static void SiteChoice(ComboBox combo) {
            combo.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ComboBox}'>
 <Grid>
  <ToggleButton Focusable='False' IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
   <ToggleButton.Template><ControlTemplate TargetType='{x:Type ToggleButton}'>
    <Border Background='{DynamicResource Input}' BorderBrush='{DynamicResource Line}' BorderThickness='.5' CornerRadius='6'><Path Data='M 0 0 L 4 4 L 8 0' Stroke='{DynamicResource Dim}' StrokeThickness='1' Width='8' Height='4' HorizontalAlignment='Right' Margin='0,0,9,0'/></Border>
   </ControlTemplate></ToggleButton.Template>
  </ToggleButton>
  <ContentPresenter Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' Margin='8,0,28,0' VerticalAlignment='Center' IsHitTestVisible='False'/>
  <Popup x:Name='PART_Popup' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}' Focusable='False'>
   <Border Background='{DynamicResource Input}' BorderBrush='{DynamicResource Line}' BorderThickness='1' MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}'><ScrollViewer><ItemsPresenter/></ScrollViewer></Border>
  </Popup>
 </Grid>
</ControlTemplate>");
            combo.ItemContainerStyle=new Style(typeof(ComboBoxItem));
            combo.ItemContainerStyle.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension("Ink")));
            combo.ItemContainerStyle.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension("Input")));
        }
    }
}
