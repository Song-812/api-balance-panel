using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace BalancePanel
{
    // OAuth/QR flows need a real child window and its opener, not a navigation of the parent.
    internal sealed class BrowserPopups : IDisposable
    {
        private readonly WebView2 browser;
        private readonly Window owner;
        private readonly Dictionary<Window,WebView2> children = new Dictionary<Window,WebView2>();
        private bool disposed;

        internal BrowserPopups(WebView2 parent, Window parentWindow)
        {
            browser = parent; owner = parentWindow;
            browser.CoreWebView2.NewWindowRequested += Open;
        }

        private async void Open(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            Uri uri;
            // Never set Handled before we know we can host the window: doing so makes
            // Cloudflare / OAuth popups flash once and vanish.
            if(disposed || children.Count >= 3 ||
               !Uri.TryCreate(e.Uri,UriKind.Absolute,out uri) ||
               (uri.Scheme != "https" && e.Uri != "about:blank")) return;
            // Login host may be briefly non-visible during restore; keep OAuth in-place
            // rather than dropping the NewWindow (which cancels Google's login dialog).
            if(!owner.IsVisible) {
                e.Handled = true;
                try { browser.CoreWebView2.Navigate(e.Uri); } catch { }
                return;
            }
            e.Handled = true;
            CoreWebView2Deferral deferral = e.GetDeferral();
            Window popup = null;
            try {
                WebView2 view = new WebView2();
                popup = new Window { Title = "余额面板 · 登录认证（" + uri.Host + "）", Width = 700, Height = 720,
                    MinWidth = 420, MinHeight = 400, Owner = owner, Content = view,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner };
                children.Add(popup,view);
                Window child = popup;
                popup.Closed += delegate {
                    children.Remove(child);
                    if(view.CoreWebView2 != null) view.CoreWebView2.NewWindowRequested -= Open;
                    view.Dispose();
                };
                popup.Show();
                await view.EnsureCoreWebView2Async(browser.CoreWebView2.Environment);
                if(disposed || !children.ContainsKey(popup)) { popup.Close(); return; }
                view.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
                view.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
                view.CoreWebView2.Settings.AreDevToolsEnabled = false;
                view.CoreWebView2.NewWindowRequested += Open;
                view.CoreWebView2.WindowCloseRequested += delegate { child.Close(); };
                e.NewWindow = view.CoreWebView2;
            } catch {
                if(popup != null) popup.Close();
                // Fallback: open the auth URL in the parent WebView so Google login is not lost.
                try { if(!disposed && browser.CoreWebView2 != null) browser.CoreWebView2.Navigate(e.Uri); }
                catch {
                    if(!disposed && owner.IsVisible) MessageBox.Show(owner,"认证弹窗未能打开，请关闭登录窗口后重试。","余额面板");
                }
            } finally { deferral.Complete(); }
        }

        public void Dispose()
        {
            if(disposed) return;
            disposed = true;
            if(browser.CoreWebView2 != null) browser.CoreWebView2.NewWindowRequested -= Open;
            foreach(Window child in children.Keys.ToArray()) child.Close();
        }
    }
}
