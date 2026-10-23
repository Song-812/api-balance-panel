using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace BalancePanel
{
    // Each provider has its own browser-managed profile. No cookies, tokens or passwords are exported.
    internal sealed class BrowserSession : IDisposable
    {
        private readonly string provider;
        private readonly string root;
        private readonly Uri target;
        private readonly Window host;
        private readonly WebView2 browser;
        private readonly TextBlock feedback;
        private bool disposed;
        private readonly BrowserReadState readState = new BrowserReadState();
        private Task initialize;
        private BrowserPopups popups;
        private readonly List<CoreWebView2Frame> frames = new List<CoreWebView2Frame>();
        private readonly object framesGate = new object();
        private string readFailure = "余额页暂时无法打开，请稍后重试。";
        private enum PageReadMode { Displayed, Refresh }
        public event Action<Reading> ReadingReceived;
        public bool Enabled { get { return File.Exists(Path.Combine(root, "enabled")); } }
        public bool CanAutoRefresh { get { return Enabled && readState.CanAutoRefresh; } }
        // True while the login window is on-screen for the user (not parked/hidden).
        // RebuildVisible must not Dispose mid-login or the window flashes once and vanishes.
        public bool IsLoginSurfaceOpen { get { return !disposed && host.IsVisible && host.Opacity > 0.5; } }

        public BrowserSession(string id, string dataRoot, Uri balanceTarget)
        {
            if(balanceTarget == null || !balanceTarget.IsAbsoluteUri || balanceTarget.Scheme != "https" || !string.IsNullOrEmpty(balanceTarget.UserInfo))
                throw new ArgumentException("余额页面目标无效");
            provider = id; root = dataRoot; target = balanceTarget;
            host = new Window { Title = "余额面板 · " + id + " 登录", Width = 1050, Height = 760,
                MinWidth = 720, MinHeight = 540, WindowStartupLocation = WindowStartupLocation.CenterScreen };
            DockPanel layout = new DockPanel();
            StackPanel instructions = new StackPanel { Margin = new Thickness(14) };
            instructions.Children.Add(new TextBlock { Text = "请亲自登录。完成后点击“读取余额”；程序只读取明确标注的余额，不导出登录凭据。", TextWrapping = TextWrapping.Wrap });
            Button read = new Button { Content = "读取余额", Padding = new Thickness(14,7,14,7), Margin = new Thickness(0,10,0,0), HorizontalAlignment = HorizontalAlignment.Left };
            feedback = new TextBlock { Text = "首次登录与验证码需要您自己完成。", Margin = new Thickness(0,7,0,0), TextWrapping = TextWrapping.Wrap };
            read.Click += async delegate {
                read.IsEnabled = false;
                try {
                    feedback.Text = "正在查找余额…";
                    Reading result = await ReadDisplayedAsync();
                    Directory.CreateDirectory(root);
                    File.WriteAllText(Path.Combine(root,"enabled"), "browser-managed session");
                    readState.Succeeded();
                    Action<Reading> received = ReadingReceived; if(received != null) received(result);
                    feedback.Text = "余额已读取，登录状态由浏览器在本机保存。";
                    // A hidden WebView2 stops rendering and eventually stalls page work, so park it
                    // off screen instead: invisible to the user, still a live page for later reads.
                    BrowserHost.Park(host);
                } catch { feedback.Text = readFailure; }
                finally { read.IsEnabled = true; }
            };
            instructions.Children.Add(read); instructions.Children.Add(feedback);
            DockPanel.SetDock(instructions, Dock.Top); layout.Children.Add(instructions);
            browser = new WebView2(); layout.Children.Add(browser); host.Content = layout;
            host.Closing += delegate(object sender, CancelEventArgs e) { if(!disposed) { e.Cancel = true; host.Hide(); } };
        }

        public async Task ShowLoginAsync()
        {
            if(disposed) return;
            BrowserHost.Restore(host);
            try { await InitializeAsync(); }
            catch { feedback.Text = "浏览器无法启动。请确认已安装 Microsoft Edge WebView2 Runtime。"; return; }
            feedback.Text = "正在打开余额页…";
            try {
                await BrowserNavigation.NavigateForLoginAsync(browser,Target);
                feedback.Text = "请完成登录；若出现人机验证，请点选通过后再点“读取余额”。";
            } catch(BrowserNavigationException error) { feedback.Text = error.Message + "，可稍后重试或读取已显示的余额。"; }
        }

        private async Task InitializeAsync()
        {
            if(initialize == null) initialize = InitializeCoreAsync();
            try { await initialize; }
            catch { initialize = null; throw; }
        }

        private async Task InitializeCoreAsync()
        {
            Directory.CreateDirectory(root);
            await BrowserHost.InitializeAsync(host,browser,Path.Combine(root,"profile"));
            if(disposed) throw new ObjectDisposedException("BrowserSession");
            browser.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
            browser.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
            browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            browser.CoreWebView2.FrameCreated += OnFrameCreated;
            if(popups == null) popups = new BrowserPopups(browser,host);
        }

        private string Target { get { return target.AbsoluteUri; } }

        public Task<Reading> ReadAsync() { return ReadCoreAsync(PageReadMode.Refresh); }
        private Task<Reading> ReadDisplayedAsync() { return ReadCoreAsync(PageReadMode.Displayed); }

        private async Task<Reading> ReadCoreAsync(PageReadMode mode)
        {
            if(disposed) throw new InvalidOperationException("窗口已关闭");
            readFailure = "余额页暂时无法打开，请稍后重试。";
            await InitializeAsync();
            // Cold start must keep WebView rendering; Hide() leaves OpenAI on an empty/challenge shell.
            if(!host.IsVisible) BrowserHost.Park(host);
            try {
                // Prefer the already-open balance/auth page. Manual reads hard-navigate when a
                // fresh load is required so HTTP failures surface; background refresh stays soft
                // so Cloudflare 403 challenge pages remain usable.
                if(mode == PageReadMode.Displayed) {
                    Uri current;
                    Uri targetUri = new Uri(Target);
                    if(!(Uri.TryCreate(browser.CoreWebView2.Source,UriKind.Absolute,out current) &&
                        (BrowserReadState.IsBalancePage(current,targetUri) || BrowserNavigation.IsChallengeOrAuthHold(current,targetUri))))
                        await BrowserNavigation.GoToBalanceAsync(browser,Target);
                } else await BrowserNavigation.EnsureDisplayedBalanceAsync(browser,Target);
            } catch(BrowserNavigationException error) { readFailure = error.Message; throw; }
            var serializer = new JavaScriptSerializer();
            bool reachedBalancePage = false;
            bool authHold = false;
            for(int attempt=0; attempt<40 && !disposed; attempt++) {
                Uri current;
                Uri target = new Uri(Target);
                if(Uri.TryCreate(browser.CoreWebView2.Source,UriKind.Absolute,out current)) {
                    if(BrowserNavigation.IsChallengeOrAuthHold(current,target)) authHold = true;
                    if(BrowserReadState.IsBalancePage(current,target)) {
                    reachedBalancePage = true;
                    string balance = await ScrapeBalanceAsync(serializer);
                    if(balance != null && balance.StartsWith("__BP_PROBE__",StringComparison.Ordinal)) {
                        SafeDiagnostics.Save(root,balance.Substring("__BP_PROBE__".Length));
                        readFailure = "结构诊断已生成：仅含固定布尔状态，不含账号、金额或凭据。";
                        throw new InvalidOperationException("结构诊断完成");
                    }
                    if(!string.IsNullOrWhiteSpace(balance)) {
                        readState.Succeeded();
                        return BalanceParsers.PageBalance(provider,balance);
                    }
                    }
                }
                await Task.Delay(500);
            }
            // Auth/Cloudflare interstitials are not a lost session; keep auto-refresh enabled.
            if(reachedBalancePage) {
                try {
                    await SaveFrameAwareProbeAsync(serializer);
                } catch { }
            }
            readState.MissedReading(reachedBalancePage || authHold);
            readFailure = reachedBalancePage
                ? "已进入官方余额页，但未识别到明确余额。无预付余额的后付账户不支持。"
                : (authHold
                    ? "页面仍在登录或人机验证中，请在登录窗口完成后再点刷新。"
                    : "尚未进入官方余额页，请完成登录后再读取。");
            throw new InvalidOperationException("请登录或检查余额页");
        }


        private void OnFrameCreated(object sender, CoreWebView2FrameCreatedEventArgs e)
        {
            CoreWebView2Frame frame = e.Frame;
            lock(framesGate) { frames.Add(frame); }
            frame.Destroyed += delegate {
                lock(framesGate) { frames.Remove(frame); }
            };
        }

        private string BalanceScriptFile()
        {
            return provider == "gemini" ? "GeminiBalanceDom.js" : "BalanceDom.js";
        }

        private string BalanceInvoke(string script, JavaScriptSerializer serializer)
        {
            if(provider == "gemini") return "(" + script + ")()";
            return "(" + script + ")(" + serializer.Serialize(provider) + ")";
        }

        private async Task<string> ScrapeBalanceAsync(JavaScriptSerializer serializer)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, BalanceScriptFile());
            string script = File.ReadAllText(path, Encoding.UTF8);
            string invoke = BalanceInvoke(script, serializer);
            string balance = DeserializeScriptString(await browser.ExecuteScriptAsync(invoke), serializer);
            if(!string.IsNullOrWhiteSpace(balance)) return balance;
            // OpenAI and Gemini both nest labeled balances in child frames on a cold load.
            CoreWebView2Frame[] snapshot;
            lock(framesGate) { snapshot = frames.ToArray(); }
            for(int i=0; i<snapshot.Length; i++) {
                try {
                    balance = DeserializeScriptString(await snapshot[i].ExecuteScriptAsync(invoke), serializer);
                    if(!string.IsNullOrWhiteSpace(balance)) return balance;
                } catch { }
            }
            if(provider == "gemini") {
                foreach(string frameBalance in await EvaluateGeminiInCdpFramesAsync(invoke, serializer)) {
                    if(!string.IsNullOrWhiteSpace(frameBalance)) return frameBalance;
                }
            }
            return balance;
        }

        private async Task<List<string>> EvaluateGeminiInCdpFramesAsync(string expression, JavaScriptSerializer serializer)
        {
            var results = new List<string>();
            if(provider != "gemini") return results;
            try {
                string treeJson = await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.getFrameTree", "{}");
                var frameIds = new List<string>();
                CollectFrameIds(serializer.DeserializeObject(treeJson), frameIds);
                for(int i=0; i<frameIds.Count; i++) {
                    try {
                        string worldJson = await browser.CoreWebView2.CallDevToolsProtocolMethodAsync(
                            "Page.createIsolatedWorld",
                            serializer.Serialize(new Dictionary<string,object> { { "frameId", frameIds[i] }, { "worldName", "bp-gemini-read" } }));
                        Dictionary<string,object> world = serializer.Deserialize<Dictionary<string,object>>(worldJson);
                        if(world == null || !world.ContainsKey("executionContextId")) continue;
                        int contextId = Convert.ToInt32(world["executionContextId"]);
                        var evalArgs = new Dictionary<string,object>();
                        evalArgs["expression"] = expression;
                        evalArgs["contextId"] = contextId;
                        evalArgs["returnByValue"] = true;
                        evalArgs["awaitPromise"] = true;
                        string evalJson = await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", serializer.Serialize(evalArgs));
                        Dictionary<string,object> eval = serializer.Deserialize<Dictionary<string,object>>(evalJson);
                        if(eval == null || !eval.ContainsKey("result")) continue;
                        Dictionary<string,object> result = eval["result"] as Dictionary<string,object>;
                        if(result == null || !result.ContainsKey("value")) continue;
                        object value = result["value"];
                        if(value == null) continue;
                        string text = value as string;
                        if(text == null) text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
                        if(!string.IsNullOrWhiteSpace(text)) results.Add(text);
                    } catch { }
                }
            } catch { }
            return results;
        }

        private static void CollectFrameIds(object node, List<string> frameIds)
        {
            Dictionary<string,object> map = node as Dictionary<string,object>;
            if(map == null) return;
            object frameTree;
            if(map.TryGetValue("frameTree", out frameTree)) { CollectFrameIds(frameTree, frameIds); return; }
            object frame;
            if(map.TryGetValue("frame", out frame)) {
                Dictionary<string,object> frameMap = frame as Dictionary<string,object>;
                object id;
                if(frameMap != null && frameMap.TryGetValue("id", out id) && id != null) frameIds.Add(Convert.ToString(id, System.Globalization.CultureInfo.InvariantCulture));
            }
            object children;
            if(map.TryGetValue("childFrames", out children)) {
                object[] list = children as object[];
                System.Collections.ArrayList arrayList = children as System.Collections.ArrayList;
                if(list != null) { for(int i=0;i<list.Length;i++) CollectFrameIds(list[i], frameIds); }
                else if(arrayList != null) { for(int i=0;i<arrayList.Count;i++) CollectFrameIds(arrayList[i], frameIds); }
            }
        }

        private static string DeserializeScriptString(string output, JavaScriptSerializer serializer)
        {
            if(string.IsNullOrWhiteSpace(output) || output == "null") return null;
            return serializer.Deserialize<string>(output);
        }

        private async Task SaveFrameAwareProbeAsync(JavaScriptSerializer serializer)
        {
            string probeInvoke =
                "(function(){function vis(e){try{var s=getComputedStyle(e);return s.display!=='none'&&s.visibility!=='hidden'&&e.getClientRects().length>0;}catch(x){return false;}}" +
                "var text='';try{text=(document.body&&(document.body.innerText||document.body.textContent))||'';}catch(x){}" +
                "var hasCredit=/Credit\\s+balance/i.test(text);" +
                "var hasZh=text.indexOf('\u8d60\u91d1\u4f59\u989d')>=0;var hasAlt=text.indexOf('\u53ef\u7528\u8d60\u91d1')>=0||text.indexOf('\u53ef\u7528\u989d\u5ea6')>=0;" +
                "var exact=false;var nodes=document.querySelectorAll('span,div,p,h1,h2,h3,h4,h5,label,strong,b');" +
                "for(var i=0;i<nodes.length;i++){var t=((nodes[i].innerText||nodes[i].textContent)||'').replace(/\\s+/g,' ').trim();if(vis(nodes[i])&&(t==='\u8d60\u91d1\u4f59\u989d'||/^Credit\\s+balance$/i.test(t))){exact=true;break;}}" +
                "var frames=false;try{frames=document.querySelectorAll('iframe').length>0;}catch(x){}" +
                "var shadow=false;try{var all=document.querySelectorAll('*');for(var j=0;j<all.length;j++){if(all[j].shadowRoot){shadow=true;break;}}}catch(x){}" +
                "var challenge=/cloudflare|challenge|captcha/i.test(text);" +
                "return JSON.stringify({DomEntered:true,ExactCreditLabel:exact,ContainsCreditLabel:hasCredit,LocalizedLabel:hasZh,AlternativeLabel:hasAlt,HasFrames:frames,HasShadowRoots:shadow,ChallengePage:challenge});})()";

            bool exact=false, credit=false, localized=false, alt=false, framesFlag=false, shadow=false, challenge=false, entered=false;
            Action<string> merge = delegate(string json) {
                if(string.IsNullOrWhiteSpace(json) || json == "null") return;
                Dictionary<string,object> map = serializer.Deserialize<Dictionary<string,object>>(json);
                if(map == null) return;
                entered = true;
                exact = exact || IsTrue(map, "ExactCreditLabel");
                credit = credit || IsTrue(map, "ContainsCreditLabel");
                localized = localized || IsTrue(map, "LocalizedLabel");
                alt = alt || IsTrue(map, "AlternativeLabel");
                framesFlag = framesFlag || IsTrue(map, "HasFrames");
                shadow = shadow || IsTrue(map, "HasShadowRoots");
                challenge = challenge || IsTrue(map, "ChallengePage");
            };

            merge(DeserializeScriptString(await browser.ExecuteScriptAsync(probeInvoke), serializer));
            CoreWebView2Frame[] probeFrames;
            lock(framesGate) { probeFrames = frames.ToArray(); }
            framesFlag = framesFlag || probeFrames.Length > 0;
            for(int i=0; i<probeFrames.Length; i++) {
                try { merge(DeserializeScriptString(await probeFrames[i].ExecuteScriptAsync(probeInvoke), serializer)); }
                catch { }
            }
            if(provider == "gemini") {
                foreach(string frameBalance in await EvaluateGeminiInCdpFramesAsync(probeInvoke, serializer)) {
                    merge(frameBalance);
                }
            }
            var output = new Dictionary<string,object>();
            output["DomEntered"] = entered;
            output["ExactCreditLabel"] = exact;
            output["ContainsCreditLabel"] = credit;
            output["LocalizedLabel"] = localized;
            output["AlternativeLabel"] = alt;
            output["HasFrames"] = framesFlag;
            output["HasShadowRoots"] = shadow;
            output["ChallengePage"] = challenge;
            SafeDiagnostics.Save(root, serializer.Serialize(output));
        }
        private static bool IsTrue(Dictionary<string,object> map, string key)
        {
            object value;
            return map.TryGetValue(key, out value) && value is bool && (bool)value;
        }

        public void Dispose()
        {
            if(disposed) return;
            disposed = true; lock(framesGate) { frames.Clear(); } if(popups != null) popups.Dispose(); browser.Dispose(); host.Close();
        }
    }
}
