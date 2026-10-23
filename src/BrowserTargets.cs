using System;

namespace BalancePanel
{
    // Only providers that read a signed-in web page live here. Key-based providers
    // deliberately have no browser target.
    internal static class BrowserTargets
    {
        internal static Uri For(string provider, string region)
        {
            if(provider == "mimo") return new Uri("https://platform.xiaomimimo.com/console/balance");
            if(provider == "openai") return new Uri("https://platform.openai.com/settings/organization/billing/overview");
            // AI Studio billing page; scraped via GeminiBalanceDom.js (not BalanceDom.js).
            if(provider == "gemini") return new Uri("https://aistudio.google.com/billing");
            throw new ArgumentException("该来源不使用网页登录");
        }
    }
}