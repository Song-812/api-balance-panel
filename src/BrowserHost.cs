using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace BalancePanel
{
    internal static class BrowserHost
    {
        // WPF WebView2 waits forever when its host has never loaded. Give it a real,
        // offscreen host during startup, then restore the user's ordinary login window.
        internal static async Task InitializeAsync(Window host, WebView2 browser, string profile)
        {
            bool background = !host.IsVisible;
            WindowStartupLocation startup = host.WindowStartupLocation;
            if(background) {
                host.ShowActivated = false; host.ShowInTaskbar = false; host.Opacity = 0;
                host.WindowStartupLocation = WindowStartupLocation.Manual; host.Left = -16000; host.Top = -16000;
                host.Show();
            }
            try {
                Task initialize = InitializeCoreAsync(browser,profile);
                if(await Task.WhenAny(initialize,Task.Delay(18000)) != initialize)
                    throw new TimeoutException("浏览器初始化超时");
                await initialize;
            } finally {
                if(background) {
                    // Hide() stops WebView2 rendering; cold auto-refresh then scrapes an empty/challenge shell.
                    // Park keeps the surface alive off-screen so billing pages can finish loading.
                    host.WindowStartupLocation = startup;
                    Park(host);
                }
            }
        }

        // Keep the session alive but out of the user's way. A hidden WebView2 stops rendering,
        // which can stall the page work that later balance reads depend on.
        internal static void Park(Window host)
        {
            host.ShowActivated = false; host.ShowInTaskbar = false; host.Opacity = 0;
            host.WindowStartupLocation = WindowStartupLocation.Manual;
            host.Left = -16000; host.Top = -16000;
            if(!host.IsVisible) host.Show();
        }

        internal static void Restore(Window host)
        {
            host.Opacity = 1; host.ShowInTaskbar = true; host.ShowActivated = true;
            if(!host.IsVisible) host.Show();
            Rect area = SystemParameters.WorkArea;
            host.Left = area.Left + Math.Max(0, (area.Width - host.Width) / 2);
            host.Top = area.Top + Math.Max(0, (area.Height - host.Height) / 2);
            host.Activate();
        }

        // The wallet page asks for its balance again when it becomes visible, so a brief
        // hide/show cycle refreshes the data without showing anything to the user.
        internal static async Task RefreshByVisibilityAsync(Window host)
        {
            if(!host.IsVisible) { Park(host); return; }
            host.Hide();
            await Task.Delay(250);
            Park(host);
        }

        private static async Task InitializeCoreAsync(WebView2 browser,string profile)
        {
            // Clash fake-ip + system proxy often leaves WebView2 on a blank white page for OpenAI.
            // Force IPv4 and pin the same local proxy the OS already uses (Clash Verge mixed-port).
            CoreWebView2EnvironmentOptions options = new CoreWebView2EnvironmentOptions();
            options.AdditionalBrowserArguments = BrowserProxyArguments();
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null,profile,options);
            await browser.EnsureCoreWebView2Async(environment);
        }

        private static string BrowserProxyArguments()
        {
            string proxy = null;
            try {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    "Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings")) {
                    if(key != null) {
                        object enable = key.GetValue("ProxyEnable");
                        object server = key.GetValue("ProxyServer");
                        if(enable is int && ((int)enable) == 1 && server != null) {
                            string raw = server.ToString().Trim();
                            if(raw.Length > 0) {
                                // IE may store "http=127.0.0.1:7897;https=127.0.0.1:7897" or "127.0.0.1:7897"
                                foreach(string part in raw.Split(';')) {
                                    string piece = part.Trim();
                                    int eq = piece.IndexOf('=');
                                    if(eq >= 0) piece = piece.Substring(eq + 1).Trim();
                                    if(piece.Length > 0) { proxy = piece; break; }
                                }
                            }
                        }
                    }
                }
            } catch { }
            // 跟随系统代理；系统未设置代理时直连，不写死任何本地代理地址。
            if(string.IsNullOrWhiteSpace(proxy)) return "--disable-ipv6";
            return "--disable-ipv6 --proxy-server=http://" + proxy;
        }
    }
}
