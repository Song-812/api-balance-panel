using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace BalancePanel
{
    public enum PanelKind {Api,Vpn}

    public sealed class PanelWindow:Window
    {
        private readonly PanelKind kind;
        private readonly string root;
        private readonly string preferencesPath;
        private readonly SecureSettings settings;
        private readonly PanelPreferences preferences;
        private readonly ProviderClient client=new ProviderClient();
        private readonly Dictionary<string,RowState> rows=new Dictionary<string,RowState>();
        private readonly Dictionary<string,BrowserSession> browsers=new Dictionary<string,BrowserSession>();
        private readonly HashSet<string> configured=new HashSet<string>();
        private readonly DispatcherTimer timer=new DispatcherTimer {Interval=TimeSpan.FromMinutes(1)};
        private readonly Button refresh;
        private readonly CardBoard board;
        private readonly TextBlock empty;
        private bool closed,running;

        public PanelWindow(PanelKind panelKind,string dataRoot)
        {
            kind=panelKind; root=dataRoot; settings=new SecureSettings(Path.Combine(root,"settings"));
            preferencesPath=Path.Combine(root,"panel.json"); preferences=PanelPreferences.Load(preferencesPath,kind);
            Theme.Window(this); Title=kind==PanelKind.Api?"API 余额面板":"VPN 流量面板";
            MinWidth=280; MinHeight=230; ResizeMode=ResizeMode.CanResize; Topmost=false;
            RestoreGeometry();
            foreach(string id in SourceCatalog.Ids(kind)) {rows[id]=new RowState(); rows[id].PropertyChanged+=delegate {UpdateHealth();};}
            var layout=new Grid(); layout.RowDefinitions.Add(new RowDefinition {Height=new GridLength(38)}); layout.RowDefinitions.Add(new RowDefinition());
            refresh=Theme.Icon("刷新","M 10 4 A 4 4 0 1 0 10 8 M 10 1 L 10 4 L 7 4",async delegate {await RefreshAllAsync(true);});
            var configure=Theme.Icon("设置","M 10.250 4.240 L 11.909 4.958 L 11.909 7.042 L 10.250 7.760 L 10.250 7.760 L 10.915 9.441 L 9.441 10.915 L 7.760 10.250 L 7.760 10.250 L 7.042 11.909 L 4.958 11.909 L 4.240 10.250 L 4.240 10.250 L 2.559 10.915 L 1.085 9.441 L 1.750 7.760 L 1.750 7.760 L 0.091 7.042 L 0.091 4.958 L 1.750 4.240 L 1.750 4.240 L 1.085 2.559 L 2.559 1.085 L 4.240 1.750 L 4.240 1.750 L 4.958 0.091 L 7.042 0.091 L 7.760 1.750 L 7.760 1.750 L 9.441 1.085 L 10.915 2.559 L 10.250 4.240 Z M 8.2 6 A 2.2 2.2 0 1 1 3.8 6 A 2.2 2.2 0 1 1 8.2 6 Z",OpenSettings);
            var minimize=Theme.Icon("最小化","M 1 6 L 11 6",delegate {WindowState=WindowState.Minimized;});
            var close=Theme.Icon("关闭","M 2 2 L 10 10 M 10 2 L 2 10",delegate {Close();});
            layout.Children.Add(Theme.Header(this,kind==PanelKind.Api?"API 余额":"VPN 流量",refresh,configure,minimize,close));
            var body=new Grid(); Grid.SetRow(body,1); layout.Children.Add(body);
            var scroll=new ScrollViewer {Margin=new Thickness(10,8,10,10),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Top};
            Theme.PanelScroll(scroll);
            board=new CardBoard(scroll); scroll.Content=board; body.Children.Add(scroll);
            board.OrderChanged+=delegate(string[] ids) {preferences.SetVisibleOrder(ids); SavePreferences();};
            empty=Theme.Text("请在设置中添加账户或开启显示",12,"Dim"); empty.TextWrapping=TextWrapping.Wrap; empty.TextAlignment=TextAlignment.Center; empty.HorizontalAlignment=HorizontalAlignment.Center; empty.Margin=new Thickness(24); body.Children.Add(empty);
            Content=Theme.Shell(layout); RebuildVisible();
            timer.Tick+=async delegate {await RefreshAllAsync(false);};
            var command=new RoutedCommand(); CommandBindings.Add(new CommandBinding(command,async delegate {await RefreshAllAsync(true);})); InputBindings.Add(new KeyBinding(command,Key.F5,ModifierKeys.None));
            Closing+=delegate {RememberGeometry(); SavePreferences();};
            Closed+=delegate {closed=true; timer.Stop(); foreach(var session in browsers.Values) session.Dispose(); client.Dispose();};
        }
        private void RestoreGeometry()
        {
            Rect area=SystemParameters.WorkArea; Width=Math.Min(preferences.Width,area.Width); Height=Math.Min(preferences.Height,area.Height);
            if(preferences.Left.HasValue && preferences.Top.HasValue) {
                Rect virtualArea=new Rect(SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenWidth,SystemParameters.VirtualScreenHeight);
                Rect saved=new Rect(preferences.Left.Value,preferences.Top.Value,Width,Height); Rect visible=Rect.Intersect(saved,virtualArea);
                if(!visible.IsEmpty && visible.Width>=80 && visible.Height>=38) {WindowStartupLocation=WindowStartupLocation.Manual;Left=saved.Left;Top=saved.Top;}
            }
        }
        private void RememberGeometry()
        {
            Rect bounds=WindowState==WindowState.Normal?new Rect(Left,Top,Width,Height):RestoreBounds;
            if(bounds.IsEmpty) return; preferences.Width=bounds.Width; preferences.Height=bounds.Height; preferences.Left=bounds.Left; preferences.Top=bounds.Top;
        }
        private void ResetSize() {WindowState=WindowState.Normal;Width=300;Height=250;RememberGeometry();SavePreferences();}
        private void SavePreferences()
        {
            try {preferences.Save(preferencesPath);}
            catch(IOException) {ReportPreferenceFailure();} catch(UnauthorizedAccessException) {ReportPreferenceFailure();}
        }
        private void ReportPreferenceFailure() {if(!closed) MessageBox.Show(this,"窗口偏好未能保存，请检查本机文件夹是否可写。","余额面板");}
        private bool Visible(string id) {return preferences.IsVisible(id) && configured.Contains(id);}
        private void RebuildVisible()
        {
            configured.Clear();
            foreach(string id in SourceCatalog.Ids(kind)) {
                try {
                    if(UsesBrowser(id,null)) {if(File.Exists(Path.Combine(root,"browser",id,"enabled"))) configured.Add(id);}
                    else {AccountSettings account=settings.Load(id); if(!string.IsNullOrWhiteSpace(id=="vpn"?account.VpnUrl:account.ApiKey)) configured.Add(id);}
                } catch {configured.Add(id);rows[id].Fail("本机配置不可读，请检查设置");}
            }
            foreach(string id in browsers.Keys.Where(id=>!Visible(id) && !browsers[id].IsLoginSurfaceOpen).ToArray()) {browsers[id].Dispose();browsers.Remove(id);}
            string[] ids=preferences.Order.Where(Visible).ToArray(); board.Bind(ids,rows); empty.Visibility=ids.Length==0?Visibility.Visible:Visibility.Collapsed; UpdateHealth();
        }
        private void UpdateHealth()
        {
            bool fresh=rows.Where(x=>Visible(x.Key)).Any() && rows.Where(x=>Visible(x.Key)).All(x=>x.Value.IsFresh);
            Resources["StatusLight"]=TryFindResource(fresh?"Ok":"Stale");
        }
        public async void Start() {await RefreshAllAsync(false);if(!closed) timer.Start();}
        private async Task RefreshAllAsync(bool manual)
        {
            if(closed || running) return; running=true;refresh.IsEnabled=false;
            try {await Task.WhenAll(rows.Keys.Where(Visible).Select(id=>RefreshOneAsync(id,manual)));}
            finally {running=false;if(!closed) refresh.IsEnabled=true;}
        }
        private async Task RefreshOneAsync(string provider,bool manual)
        {
            if(!Visible(provider)) return; RowState row=rows[provider];if(!row.BeginRefresh()) return;
            try {
                AccountSettings account=settings.Load(provider); Reading reading;
                if(UsesBrowser(provider,account)) {
                    BrowserSession session=Browser(provider,account.Region);
                    if(!session.Enabled) {row.Fail("请在设置中登录");return;}
                    if(!manual && !session.CanAutoRefresh) {row.Fail("请重新登录或手动重试");return;}
                    reading=await session.ReadAsync();
                } else {
                    if(string.IsNullOrWhiteSpace(provider=="vpn"?account.VpnUrl:account.ApiKey)) {row.Reset();return;}
                    reading=await client.FetchAsync(provider,account);
                }
                if(!closed && Visible(provider)) row.Accept(reading.DisplayValue,reading.Note,reading.Timestamp.ToLocalTime());
            } catch(Exception error) {if(!closed) row.Fail(SafeError(error));}
        }
        internal static bool UsesBrowser(string provider,AccountSettings account) {return provider=="mimo" || provider=="openai" || provider=="gemini";}
        internal static string SafeError(Exception error)
        {
            BrowserNavigationException navigation=error as BrowserNavigationException;if(navigation!=null) return navigation.Message;
            ProviderException provider=error as ProviderException;
            if(provider!=null) {
                if(provider.StatusCode==401 || provider.StatusCode==403) return "授权失败，请检查密钥或登录";
                if(provider.StatusCode==429) return "请求过于频繁，请稍后刷新";
                return "读取失败，请检查网络、设置或页面格式";
            }
            if(error is CryptographicException || error is IOException) return "本机配置不可读，请检查设置";
            if(error is TaskCanceledException || error is TimeoutException) return "查询超时，可点击重试";
            if(error is FormatException) return "数据格式变化，未读取到明确余额";
            if(error is HttpRequestException) return "网络或接口不可用，请检查授权";
            if(error is ArgumentException) return "账户配置不完整或格式不正确";
            return "查询未完成，请检查登录与设置";
        }
        private BrowserSession Browser(string id,string region)
        {
            BrowserSession session;if(browsers.TryGetValue(id,out session)) return session;
            session=new BrowserSession(id,Path.Combine(root,"browser",id),BrowserTargets.For(id,region));
            session.ReadingReceived+=delegate(Reading reading) {
                if(closed) return;
                if(!configured.Contains(id)) RebuildVisible();
                if(Visible(id)) rows[id].Accept(reading.DisplayValue,reading.Note,reading.Timestamp.ToLocalTime());
            }; browsers.Add(id,session); return session;
        }
        private void OpenSettings()
        {
            if(running) {MessageBox.Show(this,"请等待当前刷新结束，再修改账户设置。","正在刷新");return;}
            timer.Stop();
            // Login must run AFTER ShowDialog returns and RebuildVisible finishes. Otherwise
            // closing settings rebuilds the board, sees the new Gemini/OpenAI/MiMo session as
            // "not configured yet", Disposes it, and the login window flashes once then vanishes.
            string pendingLogin=null,pendingRegion=null;
            var dialog=new SettingsWindow(kind,settings,delegate(string id) {
                BrowserSession old;if(browsers.TryGetValue(id,out old)) {old.Dispose();browsers.Remove(id);} rows[id].Reset();
            },delegate(string id,string region) {pendingLogin=id;pendingRegion=region;return Task.Delay(0);},preferences,ResetSize,SavePreferences);
            dialog.Owner=this;Theme.PlaceAtOwner(dialog,this);dialog.ShowDialog();
            if(!closed) {
                RebuildVisible();timer.Start();
                // Skip auto-refresh when the user is about to log in; refresh would race the WebView.
                if(dialog.WasSaved && pendingLogin==null) RefreshAfterSettings();
            }
            if(!closed && pendingLogin!=null) ShowLoginAfterSettings(pendingLogin,pendingRegion);
        }
        private async void ShowLoginAfterSettings(string id,string region)
        {
            try {await Browser(id,region).ShowLoginAsync();}
            catch { }
        }
        private async void RefreshAfterSettings() {await RefreshAllAsync(true);}
    }
}
