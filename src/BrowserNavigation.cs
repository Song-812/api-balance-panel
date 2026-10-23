using System;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace BalancePanel
{
    // Only fixed public status codes cross this boundary; never retain URLs or inner exceptions.
    internal sealed class BrowserNavigationException : Exception
    {
        public string ErrorCode { get; private set; }

        internal BrowserNavigationException() : this("Timeout") { }

        internal BrowserNavigationException(int httpStatusCode, CoreWebView2WebErrorStatus status)
            : this(httpStatusCode >= 400 && httpStatusCode <= 599
                ? "HTTP_" + httpStatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : (Enum.IsDefined(typeof(CoreWebView2WebErrorStatus),status) ? status.ToString() : "Unknown")) { }

        private BrowserNavigationException(string code) : base("余额页面暂时无法打开（" + code + "）")
        {
            ErrorCode = code;
        }
    }

    internal static class BrowserNavigation
    {
        // Cloudflare human-check pages commonly complete as HTTP 403 while still showing a usable challenge UI.
        private static bool IsChallengeHttpStatus(int httpStatusCode)
        {
            return httpStatusCode == 403;
        }

        private static bool ShouldTreatAsFailure(bool isSuccess, int httpStatusCode, CoreWebView2WebErrorStatus status, bool allowChallenge)
        {
            if(allowChallenge && IsChallengeHttpStatus(httpStatusCode)) return false;
            if(!isSuccess) return true;
            return httpStatusCode >= 400 && httpStatusCode <= 599;
        }

        // Keep auth / Cloudflare interstitials; do not treat same-host login paths as "already there".
        internal static bool IsChallengeOrAuthHold(Uri current, Uri target)
        {
            if(current == null || current.Scheme != "https") return false;
            string host = current.Host;
            if(string.Equals(host, "challenges.cloudflare.com", StringComparison.OrdinalIgnoreCase)) return true;
            if(string.Equals(host, target.Host, StringComparison.OrdinalIgnoreCase)) return false;
            if(host.EndsWith(".openai.com", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "openai.com", StringComparison.OrdinalIgnoreCase)) return true;
            if(host.EndsWith(".xiaomimimo.com", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "xiaomimimo.com", StringComparison.OrdinalIgnoreCase)) return true;
            if(host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "google.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".googleusercontent.com", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "accounts.google.com", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static Task EnsureDisplayedBalanceAsync(WebView2 browser, string target)
        {
            Uri current;
            Uri targetUri = new Uri(target);
            if(Uri.TryCreate(browser.CoreWebView2.Source,UriKind.Absolute,out current) &&
               (BrowserReadState.IsBalancePage(current,targetUri) || IsChallengeOrAuthHold(current,targetUri)))
                return Task.FromResult(true);
            // Soft navigate: hard-failing on HTTP 403 kills refresh after a good login.
            return NavigateForLoginAsync(browser,target);
        }

        internal static Task GoToBalanceAsync(WebView2 browser, string target)
        {
            return NavigateAsync(browser, target, false);
        }

        // Login window must keep Cloudflare / auth interstitials visible for the user to complete.
        internal static Task NavigateForLoginAsync(WebView2 browser, string target)
        {
            return NavigateAsync(browser, target, true);
        }

        private static async Task NavigateAsync(WebView2 browser, string target, bool allowChallenge)
        {
            var completion = new TaskCompletionSource<BrowserNavigationException>();
            EventHandler<CoreWebView2NavigationCompletedEventArgs> onCompleted = delegate(object sender, CoreWebView2NavigationCompletedEventArgs e) {
                completion.TrySetResult(ShouldTreatAsFailure(e.IsSuccess,e.HttpStatusCode,e.WebErrorStatus,allowChallenge)
                    ? new BrowserNavigationException(e.HttpStatusCode,e.WebErrorStatus) : null);
            };
            browser.NavigationCompleted += onCompleted;
            try {
                try { browser.CoreWebView2.Navigate(target); }
                catch { throw new BrowserNavigationException(0,CoreWebView2WebErrorStatus.Unknown); }
                if(await Task.WhenAny(completion.Task,Task.Delay(18000)) != completion.Task)
                    throw new BrowserNavigationException();
                BrowserNavigationException failure = await completion.Task;
                if(failure != null) throw failure;
            } finally { browser.NavigationCompleted -= onCompleted; }
        }
    }
}
