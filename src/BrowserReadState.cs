using System;
namespace BalancePanel
{
    internal sealed class BrowserReadState
    {
        private bool requiresLogin;
        internal bool CanAutoRefresh { get { return !requiresLogin; } }
        internal void MissedReading(bool reachedBalancePage) { requiresLogin = !reachedBalancePage; }
        internal void Succeeded() { requiresLogin = false; }
        internal static bool IsBalancePage(Uri current, Uri target)
        {
            if(current == null || current.Scheme != "https" || current.Host != target.Host || current.Port != target.Port) return false;
            string expected = target.AbsolutePath.TrimEnd('/');
            if(string.Equals(current.AbsolutePath.TrimEnd('/'),expected,StringComparison.OrdinalIgnoreCase)) return true;
            string route = current.Fragment.TrimStart('#').Split('?')[0].TrimEnd('/');
            return current.AbsolutePath == "/" && string.Equals(route,expected,StringComparison.OrdinalIgnoreCase);
        }
    }
}
