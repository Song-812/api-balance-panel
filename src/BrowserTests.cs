using System;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

internal static class BrowserTests
{
    private static int passed, failed;
    private const System.Reflection.BindingFlags PrivateInstance = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

    private static void Check(string name, bool condition)
    {
        if(condition) { passed++; Console.WriteLine("PASS " + name); }
        else { failed++; Console.WriteLine("FAIL " + name); }
    }

    private static async Task<Exception> FailureAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch(Exception error) { return error; }
    }

    private static bool IsSafeNavigationFailure(Exception error, string expectedCode)
    {
        var navigationError = error as BalancePanel.BrowserNavigationException;
        return navigationError != null && navigationError.InnerException == null && navigationError.Data.Count == 0 &&
            navigationError.ErrorCode == expectedCode && navigationError.Message == "余额页面暂时无法打开（" + expectedCode + "）";
    }

    private static System.Windows.Controls.Button FindReadButton(DependencyObject root)
    {
        var button = root as System.Windows.Controls.Button;
        if(button != null && (string)button.Content == "读取余额") return button;
        foreach(object child in LogicalTreeHelper.GetChildren(root)) {
            DependencyObject element = child as DependencyObject;
            if(element == null) continue;
            var found = FindReadButton(element);
            if(found != null) return found;
        }
        return null;
    }

    private static bool ThrowsArgument(Action action)
    {
        try { action(); return false; }
        catch(ArgumentException) { return true; }
    }

    private static async Task CheckSessionAsync(string root)
    {
        using(var session = new BalancePanel.BrowserSession("mimo",root,new Uri("https://balance.test/billing"))) {
            Type type = session.GetType();
            WebView2 browser = (WebView2)type.GetField("browser",PrivateInstance).GetValue(session);
            var feedback = (System.Windows.Controls.TextBlock)type.GetField("feedback",PrivateInstance).GetValue(session);
            int status = 403, navigations = 0, foreignRequests = 0;
            bool cancel = false;
            string amount = "88.80";
            browser.CoreWebView2InitializationCompleted += delegate {
                browser.CoreWebView2.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
                browser.CoreWebView2.WebResourceRequested += delegate(object sender, CoreWebView2WebResourceRequestedEventArgs e) {
                    if(new Uri(e.Request.Uri).Host != "balance.test") foreignRequests++;
                    string html = "<section><span>可用余额</span><b>￥" + amount + "</b></section>";
                    e.Response = browser.CoreWebView2.Environment.CreateWebResourceResponse(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(html)),status,"Synthetic","Content-Type: text/html; charset=utf-8");
                };
                browser.NavigationStarting += delegate(object sender,CoreWebView2NavigationStartingEventArgs e) {
                    navigations++;
                    if(new Uri(e.Uri).Host != "balance.test") { foreignRequests++; e.Cancel = true; }
                    if(cancel) e.Cancel = true;
                };
            };
            Exception initializeError = await FailureAsync(delegate { return (Task)type.GetMethod("InitializeAsync",PrivateInstance).Invoke(session,null); });
            Check("session initializes only the engine and privacy settings without navigation",initializeError == null && navigations == 0 &&
                !browser.CoreWebView2.Settings.IsPasswordAutosaveEnabled && !browser.CoreWebView2.Settings.IsGeneralAutofillEnabled && !browser.CoreWebView2.Settings.AreDevToolsEnabled);
            await session.ShowLoginAsync();
            Check("challenge HTTP during login stays usable instead of claiming WebView2 is missing",feedback.Text.Contains("人机验证") && !feedback.Text.Contains("Runtime"));
            status = 200;
            await BalancePanel.BrowserNavigation.GoToBalanceAsync(browser,"https://balance.test/billing");
            cancel = true;
            await session.ShowLoginAsync();
            Check("canceled login navigation remains a page error, not an engine error",feedback.Text.Contains("OperationCanceled") && !feedback.Text.Contains("Runtime"));
            int beforeRead = navigations;
            BalancePanel.Reading reading = null;
            Exception readError = await FailureAsync(async delegate { reading = await (Task<BalancePanel.Reading>)type.GetMethod("ReadDisplayedAsync",PrivateInstance).Invoke(session,null); });
            Check("failed login navigation does not force a new network request when reading the displayed balance",readError == null && reading != null && reading.DisplayValue.Contains("88.80") && navigations == beforeRead);
            cancel = false; amount = "91.20";
            Exception refreshError = await FailureAsync(async delegate { reading = await session.ReadAsync(); });
            Check("background session reads reuse the owned page when it is already the balance URL",refreshError == null && reading.DisplayValue.Contains("88.80") && navigations == beforeRead);
            await BalancePanel.BrowserNavigation.GoToBalanceAsync(browser,"https://balance.test/login");
            status = 403;
            Window sessionHost = (Window)type.GetField("host",PrivateInstance).GetValue(session);
            var readButton = FindReadButton(sessionHost);
            var completedRead = new TaskCompletionSource<bool>();
            DependencyPropertyChangedEventHandler readCompleted = delegate(object sender,DependencyPropertyChangedEventArgs e) { if((bool)e.NewValue) completedRead.TrySetResult(true); };
            readButton.IsEnabledChanged += readCompleted;
            try {
                readButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                bool completed = await Task.WhenAny(completedRead.Task,Task.Delay(5000)) == completedRead.Task;
                Check("manual read button preserves the safe HTTP code when the displayed page needs navigation",completed && feedback.Text == "余额页面暂时无法打开（HTTP_403）");
            } finally { readButton.IsEnabledChanged -= readCompleted; }
            Check("session regression tests never request an official or external host",foreignRequests == 0);
        }
    }

    [STAThread]
    public static int Main(string[] args)
    {
        int code = 1;
        Application app = new Application();
        Window host = new Window { Width = 650, Height = 400, Title = "余额面板 · 无账号测试" };
        WebView2 web = new WebView2(); host.Content = web;
        host.Loaded += async delegate {
            try {
                Window hiddenHost = new Window { Width = 320, Height = 220 };
                WebView2 hiddenBrowser = new WebView2(); hiddenHost.Content = hiddenBrowser;
                Task hiddenInit = BalancePanel.BrowserHost.InitializeAsync(hiddenHost,hiddenBrowser,Path.Combine(args[0],"hidden"));
                bool readyInBackground = await Task.WhenAny(hiddenInit,Task.Delay(5000)) == hiddenInit;
                bool parkedQuiet = readyInBackground && hiddenHost.Opacity < 0.05 && !hiddenHost.ShowInTaskbar && hiddenHost.Left <= -1000;
                if(parkedQuiet) { passed++; Console.WriteLine("PASS hidden browser initialization completes and stays hidden"); }
                else { failed++; Console.WriteLine("FAIL hidden browser initialization stalled or surfaced a window"); }
                if(!readyInBackground) { hiddenHost.Show(); await Task.WhenAny(hiddenInit,Task.Delay(5000)); }
                if(hiddenInit.IsCompleted) await hiddenInit;
                hiddenBrowser.Dispose(); hiddenHost.Close();
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, args[0]);
                await web.EnsureCoreWebView2Async(env);
                string script = File.ReadAllText(args[1], Encoding.UTF8);
                string geminiScript = File.ReadAllText(Path.Combine(Path.GetDirectoryName(args[1]), "GeminiBalanceDom.js"), Encoding.UTF8);
                string[,] cases = new string[,] {
                    { "mimo", "<div>账户信息 不应导出</div><section><span>可用余额</span><strong>¥ 123.45</strong></section>", "可用余额 ¥ 123.45" },
                    { "openai", "<section><span>Usage this month</span><b>$99.00</b></section><section><h3>Credit balance</h3><div>$12.34</div></section>", "Credit balance $12.34" },
                    { "openai", "<section><h3>Usage this month</h3><div>$99.00</div></section>", "" },
                    { "mimo", "<section><span>可用余额</span><b>¥ 0.00</b></section>", "可用余额 ¥ 0.00" },
                    { "minimax", "<section><span>可用余额</span><b>￥88.80</b></section>", "可用余额 ￥88.80" },
                    { "openai", "<section style='display:none'><span>Credit balance</span><b>$77.00</b></section><section><span>Credit balance</span><b>$5.00</b></section>", "Credit balance $5.00" },
                    { "openai", "<section><span>Credit balance</span><b>$5.00</b><b>$6.00</b></section>", "" },
                    { "mimo", "<section><span>可用余额</span><b>123.45</b></section>", "" },
                    { "mimo", "<section><span>账户余额</span><b>42.50 元</b></section>", "账户余额 42.50 元" },
                    { "mimo", "<section><span>可用余额</span><b>¥12.00</b></section><section><span>账户余额</span><b>¥12.00</b></section>", "可用余额 ¥12.00" },
                    { "openai", "<section><span>Credit balance</span><b>-$12.00</b></section>", "Credit balance $-12.00" },
                    { "openai", "<section><span>Credit balance</span><i>unavailable</i><p>Monthly cost $45.00</p></section>", "" },
                    { "openai", "<section><span>Credit balance</span><p>Add credits $20.00</p></section>", "" },
                    { "mimo", "<h1>余额</h1><button>充值</button><section><div>余额</div><div>¥12.34</div><div>充值余额</div><div>¥10.00</div><div>赠送余额</div><div>¥2.34</div></section>", "余额 ¥12.34" },
                    { "mimo", "<section><div><span>余额</span><svg aria-hidden='true'></svg></div><div><span>¥</span><span>12.34</span></div><p>含赠送余额</p></section>", "余额 ¥12.34" },
                    { "mimo", "<section><span>余额</span><button>¥100.00</button></section>", "" },
                    { "openai", "<div><span>API credit balance</span><div>$12.34</div></div>", "Credit balance $12.34" },
                    { "openai", "<div><span>API credit balance</span><div>$0.00</div></div>", "Credit balance $0.00" },
                    { "openai", "<div><span>API credit balance</span><div>-$3.25</div></div>", "Credit balance $-3.25" },
                    { "openai", "<section><span>Usage this month</span><b>$90.00</b></section><div><span>API credit balance</span><div>$7.25</div></div>", "Credit balance $7.25" },
                    { "openai", "<div><span>API credit balance</span><p>Monthly cost $10.00</p></div>", "" },
{ "gemini", "<section><span>Available credits</span><div>$15.00</div></section>", "Available credits $15.00" },
{ "gemini", "<section><span>Credit balance</span><div>$0.00</div></section>", "Credit balance $0.00" },
{ "gemini", "<section><span>赠金余额</span><div>US$0.00</div></section>", "Credit balance US$0.00" },
{ "gemini", "<section><span>赠金余额</span><div>$0.00</div></section>", "Credit balance $0.00" },
{ "gemini", "<section><span>可用赠金</span><div>$5.00</div></section>", "Available credits $5.00" },
{ "gemini", "<section><span>Usage</span><b>$9.00</b></section><section><span>赠金余额</span><div>US$0.00</div></section>", "Credit balance US$0.00" },
{ "gemini", "<section><div><span>赠金余额</span></div><div><span>US$0.00</span></div><p>为避免服务中断，请购买点数</p></section>", "Credit balance US$0.00" },
{ "gemini", "<section><div><span>赠金余额</span><button>i</button></div><div><span>US$0.00</span></div><a>US$0 符合条件的 GCP 赠金</a></section>", "Credit balance US$0.00" },
{ "gemini", "<div class='card'><h3>支付</h3><div class='label'>赠金余额</div><div class='amt'>US$0.00</div><button>购买点数</button></div>", "Credit balance US$0.00" },
{ "gemini", "<div><div>赠金余额</div><h1>US$0.00</h1></div>", "Credit balance US$0.00" },
{ "gemini", "<section><span>Usage</span><b>$9.00</b></section><section><span>Available credits</span><div>$3.25</div></section>", "Available credits $3.25" },
                };
                for (int i=0; i<cases.GetLength(0); i++) {
                    var loaded = new TaskCompletionSource<bool>();
                    EventHandler<CoreWebView2NavigationCompletedEventArgs> done = null;
                    done = delegate(object s, CoreWebView2NavigationCompletedEventArgs e) { web.NavigationCompleted -= done; loaded.TrySetResult(true); };
                    web.NavigationCompleted += done;
                    web.NavigateToString("<!doctype html><meta charset='utf-8'><body>" + cases[i,1] + "</body>");
                    await loaded.Task;
                    string gotJson = await web.ExecuteScriptAsync(cases[i,0] == "gemini" ? ("(" + geminiScript + ")()") : ("(" + script + ")('" + cases[i,0] + "')"));
                    string got = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<string>(gotJson);
                    if (got == cases[i,2]) { passed++; Console.WriteLine("PASS DOM " + i); }
                    else { failed++; Console.WriteLine("FAIL DOM " + i + " expected [" + cases[i,2] + "] got [" + got + "]"); }
                }
                CoreWebView2Deferral waiting = null;
                CoreWebView2WebResourceRequestedEventArgs waitingRequest = null;
                web.CoreWebView2.AddWebResourceRequestedFilter("https://balance.test/*",CoreWebView2WebResourceContext.All);
                web.CoreWebView2.WebResourceRequested += delegate(object sender, CoreWebView2WebResourceRequestedEventArgs e) {
                    string path = new Uri(e.Request.Uri).AbsolutePath;
                    if(path == "/wait") { waitingRequest = e; waiting = e.GetDeferral(); return; }
                    if(path == "/http-403" || path == "/http-503") {
                        e.Response = web.CoreWebView2.Environment.CreateWebResourceResponse(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("synthetic-response-account-marker")),path == "/http-403" ? 403 : 503,"Synthetic","Content-Type: text/html");
                        return;
                    }
                    string html = e.Request.Uri.EndsWith("/auth")
                        ? "<script>if(window.opener){window.opener.postMessage('auth-complete','https://balance.test'); window.close();}</script>"
                        : "<title>waiting</title><script>window.addEventListener('message',function(e){if(e.origin==='https://balance.test' && e.data==='auth-complete')document.title='auth-complete';});</script>";
                    e.Response=web.CoreWebView2.Environment.CreateWebResourceResponse(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(html)),200,"OK","Content-Type: text/html; charset=utf-8");
                };
                using(var popups = new BalancePanel.BrowserPopups(web,host)) {
                    var ready = new TaskCompletionSource<bool>();
                    EventHandler<CoreWebView2NavigationCompletedEventArgs> navigation = null;
                    navigation=delegate(object sender,CoreWebView2NavigationCompletedEventArgs e){web.NavigationCompleted-=navigation;ready.TrySetResult(true);};
                    web.NavigationCompleted+=navigation; web.CoreWebView2.Navigate("https://balance.test/start"); await ready.Task;
                    await web.ExecuteScriptAsync("var p=window.open('about:blank','auth'); if(p){p.document.write(\"<script>window.opener.postMessage('auth-complete','https://balance.test');window.close();</script>\");p.document.close();} void 0;");
                    bool complete=false;
                    for(int i=0;i<20;i++) {
                        if(await web.ExecuteScriptAsync("document.title") == "\"auth-complete\""){complete=true;break;}
                        await Task.Delay(150);
                    }
                    bool stayedOnBalance = web.CoreWebView2.Source == "https://balance.test/start";
                    if(complete && stayedOnBalance) {passed++;Console.WriteLine("PASS popup authentication preserves opener and postMessage return");}
                    else {failed++;Console.WriteLine("FAIL popup authentication: returned=" + complete + ", parentPreserved=" + stayedOnBalance);}
                }
                await BalancePanel.BrowserNavigation.GoToBalanceAsync(web,"https://balance.test/billing");
                if(web.CoreWebView2.Source == "https://balance.test/billing") {passed++;Console.WriteLine("PASS reading navigates to the owned balance page instead of a login landing page");}
                else {failed++;Console.WriteLine("FAIL reading remained on the unrelated landing page");}
                await web.ExecuteScriptAsync("window.__bpRenderedBalance='already-rendered';");
                await BalancePanel.BrowserNavigation.EnsureDisplayedBalanceAsync(web,"https://balance.test/billing");
                if(await web.ExecuteScriptAsync("window.__bpRenderedBalance") == "\"already-rendered\""){passed++;Console.WriteLine("PASS manual read keeps an already rendered trusted balance page");}
                else {failed++;Console.WriteLine("FAIL manual read discarded the rendered balance page");}
                await BalancePanel.BrowserNavigation.GoToBalanceAsync(web,"https://balance.test/login");
                await BalancePanel.BrowserNavigation.EnsureDisplayedBalanceAsync(web,"https://balance.test/billing");
                if(web.CoreWebView2.Source == "https://balance.test/billing"){passed++;Console.WriteLine("PASS manual read leaves unrelated or login routes for the fixed balance page");}
                else {failed++;Console.WriteLine("FAIL manual read stayed on an unrelated route");}
                foreach(string status in new [] { "403", "503" }) {
                    Exception error = await FailureAsync(delegate { return BalancePanel.BrowserNavigation.GoToBalanceAsync(web,"https://balance.test/http-" + status + "?synthetic-query-marker=private"); });
                    Check("HTTP " + status + " navigation exposes only a safe fixed HTTP code",IsSafeNavigationFailure(error,"HTTP_" + status));
                    Check("panel feedback retains the safe HTTP code",error != null && BalancePanel.PanelWindow.SafeError(error).Contains("HTTP_" + status));
                }
                EventHandler<CoreWebView2NavigationStartingEventArgs> canceled = delegate(object sender,CoreWebView2NavigationStartingEventArgs e) { if(new Uri(e.Uri).AbsolutePath == "/cancel") e.Cancel = true; };
                web.NavigationStarting += canceled;
                Exception canceledError = await FailureAsync(delegate { return BalancePanel.BrowserNavigation.GoToBalanceAsync(web,"https://balance.test/cancel?synthetic-query-marker=private"); });
                web.NavigationStarting -= canceled;
                Check("canceled navigation reports OperationCanceled without a URL or inner exception",IsSafeNavigationFailure(canceledError,"OperationCanceled"));
                Exception timeoutError = await FailureAsync(delegate { return BalancePanel.BrowserNavigation.GoToBalanceAsync(web,"https://balance.test/wait?synthetic-query-marker=private"); });
                Check("elapsed navigation wait is distinctly labeled Timeout",IsSafeNavigationFailure(timeoutError,"Timeout"));
                if(waiting != null) {
                    waitingRequest.Response = web.CoreWebView2.Environment.CreateWebResourceResponse(new MemoryStream(new byte[0]),200,"OK","Content-Type: text/html");
                    waiting.Complete();
                }
                Check("MiniMax no longer has a browser target",ThrowsArgument(delegate { BalancePanel.BrowserTargets.For("minimax",null); }));
                Check("OpenAI fixed target still trusts its billing overview",BalancePanel.BrowserReadState.IsBalancePage(new Uri("https://platform.openai.com/settings/organization/billing/overview"),BalancePanel.BrowserTargets.For("openai",null)));
                Check("MiMo fixed target still trusts its console balance",BalancePanel.BrowserReadState.IsBalancePage(new Uri("https://platform.xiaomimimo.com/console/balance"),BalancePanel.BrowserTargets.For("mimo",null)));
                Check("Gemini fixed target trusts AI Studio billing",BalancePanel.BrowserReadState.IsBalancePage(new Uri("https://aistudio.google.com/billing"),BalancePanel.BrowserTargets.For("gemini",null)));
                Check("Key providers still have no browser target",ThrowsArgument(delegate { BalancePanel.BrowserTargets.For("kimi",null); }) && ThrowsArgument(delegate { BalancePanel.BrowserTargets.For("qwen",null); }) && ThrowsArgument(delegate { BalancePanel.BrowserTargets.For("glm",null); }));
                await CheckSessionAsync(Path.Combine(args[0],"session"));
                Console.WriteLine("Browser DOM: " + passed + " passed, " + failed + " failed");
                code = failed == 0 ? 0 : 1;
            } catch(Exception error) { Console.WriteLine("Browser setup failed: " + error.GetType().Name + " " + error.Message); }
            host.Close(); app.Shutdown();
        };
        app.Run(host); return code;
    }
}
