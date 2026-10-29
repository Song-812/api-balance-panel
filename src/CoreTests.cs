using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BalancePanel;

// All fixtures are handwritten, fake data. The HTTP seam never connects to a server.
internal static class CoreTests
{
    private static int passed;
    private static int failed;
    private static string Root;
    private const string Deep = "{\"is_available\":true,\"balance_infos\":[{\"currency\":\"CNY\",\"total_balance\":\"12.50\",\"granted_balance\":\"2.50\",\"topped_up_balance\":\"10.00\"}]}";
    private const string Mini = "{\"available_amount\":\"98.00\",\"cash_balance\":\"0.00\",\"voucher_balance\":\"98.00\",\"credit_balance\":\"0.00\",\"owed_amount\":\"3.25\",\"balance_alert_switch\":false,\"balance_alert_threshold\":\"\",\"base_resp\":{\"status_code\":0,\"status_msg\":\"success\"}}";

    public static int Main(string[] args)
    {
        string baseDirectory = args.Length > 0 ? args[0] : Path.GetTempPath();
        Root = Path.Combine(baseDirectory, "fixtures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Test("DeepSeek total includes grant, not cash alone", delegate { Reading r = BalanceParsers.DeepSeek(Deep); Equal("CNY 12.50", r.DisplayValue); Contains(r.Note, "赠送"); Fresh(r); });
        Test("DeepSeek real zero", delegate { Equal("CNY 0.00", BalanceParsers.DeepSeek(Deep.Replace("12.50", "0")).DisplayValue); });
        Test("DeepSeek preserves multiple currencies", delegate { Equal("CNY 12.50 / USD 2.25", BalanceParsers.DeepSeek(Deep.Replace("}]", "},{\"currency\":\"USD\",\"total_balance\":\"2.25\"}]")).DisplayValue); });
        BadDeep("missing total", "{\"balance_infos\":[{\"currency\":\"CNY\",\"topped_up_balance\":\"9\"}]}");
        BadDeep("empty list", "{\"balance_infos\":[]}");
        BadDeep("wrong currency", Deep.Replace("CNY", "EUR"));
        BadDeep("duplicate currency", Deep.Replace("}]", "},{\"currency\":\"CNY\",\"total_balance\":\"1\"}]"));
        BadDeep("malformed", "<html>Login</html>");
        BadDeep("invalid numeric", Deep.Replace("12.50", "NaN"));
        BadDeep("boolean numeric", Deep.Replace("\"12.50\"", "true"));
        BadDeep("response must not leak", "{private_fixture_payload}");
        Test("xAI inverted cents", delegate { Reading r = BalanceParsers.Xai("{\"changes\":[],\"total\":{\"val\":\"-1234\"}}"); Equal("USD 12.34", r.DisplayValue); Contains(r.Note, "已入账"); Contains(r.Note, "非实时"); });
        Test("xAI zero", delegate { Equal("USD 0.00", BalanceParsers.Xai("{\"total\":{\"val\":\"0\"}}").DisplayValue); });
        Test("xAI debt retained", delegate { Equal("USD -2.50", BalanceParsers.Xai("{\"total\":{\"val\":\"250\"}}").DisplayValue); });
        foreach (string json in new string[] { "{}", "{\"balance\":500}", "{\"total\":{\"val\":\"1.2\"}}", "{\"total\":{\"val\":null}}", "{\"total\":{\"val\":true}}" })
        { string fixture = json; Test("xAI rejects invalid " + json, delegate { Throws<FormatException>(delegate { BalanceParsers.Xai(fixture); }); }); }
        Test("MiniMax available not cash; owed is visible", delegate { Reading r = BalanceParsers.MiniMax(Mini, "CNY"); Equal("CNY 98.00", r.DisplayValue); Contains(r.Note, "3.25"); Contains(r.Note, "欠款"); Contains(r.Note, "币种"); Contains(r.Note, "赠送"); });
        Test("MiniMax zero", delegate { Equal("CNY 0.00", BalanceParsers.MiniMax(Mini.Replace("98.00", "0.00"), "CNY").DisplayValue); });
        Test("MiniMax global site labels USD", delegate { Equal("USD 98.00", BalanceParsers.MiniMax(Mini, "USD").DisplayValue); });
        Test("MiniMax rejects an unknown currency", delegate { Throws<FormatException>(delegate { BalanceParsers.MiniMax(Mini, "EUR"); }); });
        foreach (string json in new string[] { "{}", Mini.Replace("\"available_amount\"", "\"usage\""), Mini.Replace("\"owed_amount\"", "\"unknown\""), Mini.Replace("\"status_code\":0", "\"status_code\":1004"), Mini.Replace("\"98.00\"", "\"oops\""), "<html>sign in</html>" })
        { string fixture = json; Test("MiniMax invalid " + failed + passed, delegate { Throws<FormatException>(delegate { BalanceParsers.MiniMax(fixture, "CNY"); }); }); }
        Test("MiniMax web reads the response amount, not the animated widget", delegate { Reading r = BalanceParsers.MiniMaxWeb("{\"available_amount\":\"12.3456\",\"base_resp\":{\"status_code\":0}}", "CNY"); Equal("CNY 12.3456", r.DisplayValue); Contains(r.Note, "可用额度"); Contains(r.Note, "代金券"); Contains(r.Note, "授信"); });
        Test("MiniMax web keeps a real zero string", delegate { Equal("CNY 0.00", BalanceParsers.MiniMaxWeb("{\"available_amount\":\"0.00\",\"base_resp\":{\"status_code\":0}}", "CNY").DisplayValue); });
        Test("MiniMax web keeps a real zero number", delegate { Equal("CNY 0.00", BalanceParsers.MiniMaxWeb("{\"available_amount\":0,\"base_resp\":{\"status_code\":0}}", "CNY").DisplayValue); });
        Test("MiniMax web keeps debt sign", delegate { Equal("CNY -3.25", BalanceParsers.MiniMaxWeb("{\"available_amount\":\"-3.25\",\"base_resp\":{\"status_code\":0}}", "CNY").DisplayValue); });
        Test("MiniMax web owed is optional", delegate { Reading r = BalanceParsers.MiniMaxWeb("{\"available_amount\":\"5\",\"owed_amount\":\"1.5\",\"cash_balance\":\"0\",\"voucher_balance\":\"6\",\"base_resp\":{\"status_code\":0}}", "CNY"); Equal("CNY 5.00", r.DisplayValue); Contains(r.Note, "1.50"); });
        Test("MiniMax web ignores unrelated account fields", delegate { Equal("CNY 7.00", BalanceParsers.MiniMaxWeb("{\"available_amount\":\"7\",\"email\":\"fixture@example.invalid\",\"token\":\"fixture-secret\",\"base_resp\":{\"status_code\":0}}", "CNY").DisplayValue); });
        foreach (string json in new string[] {
            "{}",
            "{\"available_amount\":\"1\",\"base_resp\":{\"status_code\":2013}}",
            "{\"available_amount\":\"1\",\"base_resp\":{\"status_code\":\"0\"}}",
            "{\"available_amount\":\"1\"}",
            "{\"base_resp\":{\"status_code\":0}}",
            "{\"available_amount\":null,\"base_resp\":{\"status_code\":0}}",
            "{\"available_amount\":\"\",\"base_resp\":{\"status_code\":0}}",
            "{\"available_amount\":true,\"base_resp\":{\"status_code\":0}}",
            "{\"available_amount\":\"NaN\",\"base_resp\":{\"status_code\":0}}",
            "{\"available_amount\":\"--\",\"base_resp\":{\"status_code\":0}}",
            "{\"available_amount\":\"1.2.3\",\"base_resp\":{\"status_code\":0}}",
            "<html>sign in</html>" })
        { string fixture = json; Test("MiniMax web rejects " + failed + passed, delegate { Throws<FormatException>(delegate { BalanceParsers.MiniMaxWeb(fixture, "CNY"); }); }); }
        Test("MiniMax web rejects an unknown site currency", delegate { Throws<FormatException>(delegate { BalanceParsers.MiniMaxWeb("{\"available_amount\":\"1\",\"base_resp\":{\"status_code\":0}}", "EUR"); }); });
        const string KimiOk = "{\"code\":0,\"data\":{\"available_balance\":49.58894,\"voucher_balance\":46.58893,\"cash_balance\":3.00001},\"scode\":\"0x0\",\"status\":true}";
        Test("Kimi available balance CNY", delegate { Reading r = BalanceParsers.Kimi(KimiOk, "CNY"); Equal("CNY 49.58894", r.DisplayValue); Contains(r.Note, "代金券"); });
        Test("Kimi global labels USD", delegate { Equal("USD 49.58894", BalanceParsers.Kimi(KimiOk, "USD").DisplayValue); });
        Test("Kimi accepts status true without code zero", delegate { Equal("CNY 1.50", BalanceParsers.Kimi("{\"status\":true,\"data\":{\"available_balance\":1.5}}", "CNY").DisplayValue); });
        Test("Kimi rejects failed envelope", delegate { Throws<FormatException>(delegate { BalanceParsers.Kimi("{\"code\":1,\"status\":false,\"data\":{\"available_balance\":1}}", "CNY"); }); });
        Test("Kimi rejects unknown currency", delegate { Throws<FormatException>(delegate { BalanceParsers.Kimi(KimiOk, "EUR"); }); });
        const string QwenOk = "{\"Code\":\"200\",\"Message\":\"success\",\"Success\":true,\"Data\":{\"AvailableAmount\":\"10000.00\",\"CreditAmount\":\"0.00\",\"Currency\":\"CNY\",\"AvailableCashAmount\":\"10000.00\"}}";
        Test("Qwen AvailableAmount CNY", delegate { Reading r = BalanceParsers.Qwen(QwenOk); Equal("CNY 10000.00", r.DisplayValue); Contains(r.Note, "阿里云账户可用额度"); Contains(r.Note, "非百炼"); });
        Test("Qwen uses Data.Currency USD", delegate { Equal("USD 12.50", BalanceParsers.Qwen("{\"Success\":true,\"Code\":\"200\",\"Data\":{\"AvailableAmount\":\"12.50\",\"Currency\":\"USD\"}}").DisplayValue); });
        Test("Qwen accepts lowercase data keys", delegate { Equal("CNY 3.00", BalanceParsers.Qwen("{\"success\":true,\"code\":\"200\",\"data\":{\"availableAmount\":3,\"currency\":\"CNY\"}}").DisplayValue); });
        Test("Qwen rejects failed success flag", delegate { Throws<FormatException>(delegate { BalanceParsers.Qwen("{\"Success\":false,\"Code\":\"200\",\"Data\":{\"AvailableAmount\":\"1\",\"Currency\":\"CNY\"}}"); }); });
        Test("Qwen rejects bad code", delegate { Throws<FormatException>(delegate { BalanceParsers.Qwen("{\"Success\":true,\"Code\":\"400\",\"Data\":{\"AvailableAmount\":\"1\",\"Currency\":\"CNY\"}}"); }); });
        Test("Qwen rejects missing AvailableAmount", delegate { Throws<FormatException>(delegate { BalanceParsers.Qwen("{\"Success\":true,\"Code\":\"200\",\"Data\":{\"Currency\":\"CNY\"}}"); }); });
        Test("AliyunRpc percent-encode star and tilde", delegate {
            Equal("a%2Ab", AliyunRpc.PercentEncode("a*b"));
            Equal("~", AliyunRpc.PercentEncode("~"));
            Equal("a%20b", AliyunRpc.PercentEncode("a b"));
        });
        Test("AliyunRpc signature stable for fixed nonce", delegate {
            System.Collections.Generic.SortedDictionary<string, string> parameters = new System.Collections.Generic.SortedDictionary<string, string>(System.StringComparer.Ordinal)
            {
                { "AccessKeyId", "testid" },
                { "Action", "QueryAccountBalance" },
                { "Format", "JSON" },
                { "SignatureMethod", "HMAC-SHA1" },
                { "SignatureNonce", "nonce123" },
                { "SignatureVersion", "1.0" },
                { "Timestamp", "2016-05-23T12:00:00Z" },
                { "Version", "2017-12-14" }
            };
            string signature = AliyunRpc.Sign("GET", parameters, "testsecret");
            Equal(signature, AliyunRpc.Sign("GET", parameters, "testsecret"));
            True(signature.Length > 10, "signature empty");
            System.Uri uri = AliyunRpc.QueryAccountBalance("testid", "testsecret", new System.DateTime(2016, 5, 23, 12, 0, 0, System.DateTimeKind.Utc), "nonce123");
            Equal("https://business.aliyuncs.com/", uri.GetLeftPart(System.UriPartial.Path));
            Contains(uri.Query, "Action=QueryAccountBalance");
            Contains(uri.Query, "AccessKeyId=testid");
            string q = uri.Query.TrimStart('?');
            string found = null;
            foreach (string part in q.Split('&')) if (part.StartsWith("Signature=", System.StringComparison.Ordinal)) found = Uri.UnescapeDataString(part.Substring("Signature=".Length));
            Equal(signature, found);
        });
        const string GlmOk = "{\"code\":200,\"msg\":\"ok\",\"success\":true,\"data\":{\"balance\":42.5,\"availableBalance\":40.0,\"giveAmount\":20.0}}";
        Test("GLM prefers availableBalance", delegate { Reading r = BalanceParsers.Glm(GlmOk); Equal("CNY 40.00", r.DisplayValue); Contains(r.Note, "按量"); Contains(r.Note, "Coding Plan"); });
        Test("GLM falls back to balance", delegate { Equal("CNY 42.50", BalanceParsers.Glm("{\"code\":200,\"data\":{\"balance\":42.5}}").DisplayValue); });
        Test("GLM rejects non-success", delegate { Throws<FormatException>(delegate { BalanceParsers.Glm("{\"code\":1001,\"success\":false,\"data\":{\"availableBalance\":1}}"); }); });
        Page("mimo", "账户余额：¥12.30", "CNY 12.30");
        Page("mimo", "现金余额：￥27.55", "CNY 27.55");
        Page("mimo", "可用余额\n12.30 元", "CNY 12.30");
        Page("openai", "Credit balance\n$0.00", "USD 0.00");
        Page("xai", "Credit balance: USD 1,234.50", "USD 1234.50");
        Page("deepseek", "余额：￥0", "CNY 0.00");
        Page("deepseek", "可用余额 USD 2.10", "USD 2.10");
        Page("minimax", "账户余额 CNY 9.50", "CNY 9.50");
        Page("openai", "Available balance: $4.25", "USD 4.25");
        Page("gemini", "Available credits: $8.00", "USD 8.00");
        Page("gemini", "Credit balance\n$0.00", "USD 0.00");
        Page("gemini", "Credit balance US$0.00", "USD 0.00");
        Page("gemini", "赠金余额 US$0.00", "USD 0.00");
        Page("gemini", "可用赠金: $5.50", "USD 5.50");
        Page("openai", "Account balance: USD 6.75", "USD 6.75");
        Page("mimo", "总余额：CNY 8.20", "CNY 8.20");
        foreach (string text in new string[] { "费用：¥12.30", "Usage $8.90", "Cost $10", "余额费用 ¥1", "余额：$10\n费用：$2", "Credit balance: ¥10", "Credit balance: $1,23", "Credit balance: $10 CNY", "Credit balance: $1 USD CNY", "Credit balance: 10", "Credit balance: $10\nCredit balance: $20", "<span>Credit balance: $1</span>", "Sign in", "Credit balance: $10/day", "Credit balance: $1.2.3" })
        { string fixture = text; Test("page rejects " + text.Replace("\n", " "), delegate { Throws<FormatException>(delegate { BalanceParsers.PageBalance("openai", fixture); }); }); }
        Test("mimo rejects USD", delegate { Throws<FormatException>(delegate { BalanceParsers.PageBalance("mimo", "余额 $2"); }); });
        Vpn("<p>已用 100 GB</p><div>剩余流量</div><strong>20.5 GiB</strong><p>日均 3 GB</p>", "20.5 GiB");
        Vpn("<span>本月剩余：</span><b>0 GB</b>", "0 GB");
        Vpn("<div>剩余: 1 TB</div>", "1 TB");
        Vpn("<script>var x='剩余流量 999 GB';</script><p>剩余流量 256 MiB</p>", "256 MiB");
        Vpn("<p>剩余流量&nbsp;8 GB</p>", "8 GB");
        Vpn("<span>123.45 GB</span><p>剩余免费出站流量（本月额度 1000 GB）</p>", "123.45 GB");
        Vpn("<span>0 GiB</span><p>剩余免费出站流量（本月额度 1000 GiB）</p>", "0 GiB");
        Vpn("<section><p>已用</p><span>77 GB</span></section><section><span>123.45 GB</span><p>剩余免费出站流量（本月额度 1000 GB）</p></section><section><p>日均</p><b>8 GB</b></section><section><p>入站</p><b>99 GB</b></section>", "123.45 GB");
        foreach (string html in new string[] {
            "<div><span>77 GB</span><p>已用</p></div><p>剩余免费出站流量（本月额度 1000 GB）</p>",
            "<span>77 GB</span><p>已用</p><p>剩余免费出站流量（本月额度 1000 GB）</p>",
            "<p>剩余免费出站流量（本月额度 1000 GB）</p>",
            "<span>77 GB</span><p>剩余入站流量</p>",
            "<span>77 GB</span><p>预计剩余免费出站流量</p>",
            "<span>77 GB</span><p>剩余免费出站流量</p><span>88 GB</span><p>剩余免费出站流量</p>",
            "<p>剩余流量 99 GB</p><span>77 GB</span><p>剩余免费出站流量</p>" })
        { string fixture = html; Test("VPN rejects unassociated preceding amount " + failed + passed, delegate { Throws<FormatException>(delegate { BalanceParsers.Vpn(fixture); }); }); }
        foreach (string html in new string[] { "已用 99 GB 日均 2 GB 预计剩余 7 天", "<p>剩余流量</p><p>已用 8 GB</p>", "<form><input type='password'>登录</form>", "<script>剩余 90 GB</script>", "<p>剩余 1 GB</p><p>剩余 2 GB</p>", "<p>剩余 2 GB/天</p>", "<p>剩余 -1 GB</p>" })
        { string fixture = html; Test("VPN rejects ambiguous/used/login " + failed + passed, delegate { Throws<FormatException>(delegate { BalanceParsers.Vpn(fixture); }); }); }
        SettingsTests();
        ClientTests();
        Console.WriteLine("TOTAL " + (passed + failed) + " PASS " + passed + " FAIL " + failed);
        Console.WriteLine("Only synthetic fixtures and in-memory HTTP handlers used; no real balance/VPN requests.");
        return failed == 0 ? 0 : 1;
    }

    private static void SettingsTests()
    {
        Test("missing settings is empty", delegate { Equal(null, new SecureSettings(NewDirectory()).Load("deepseek").ApiKey); });
        Test("empty file is empty settings", delegate { string dir = NewDirectory(); File.WriteAllBytes(Path.Combine(dir, "vpn.dat"), new byte[0]); Equal(null, new SecureSettings(dir).Load("vpn").VpnUrl); });
        Test("DPAPI all fields roundtrip and no plaintext", delegate {
            string dir = NewDirectory(); SecureSettings store = new SecureSettings(dir);
            AccountSettings settings = new AccountSettings { ApiKey = "synthetic-secret-only", AccessKeyId = "fixture-akid", VpnUrl = "https://fixture.invalid/private-fixture", TeamId = "fixture-team", Region = "cn", UseWebLogin = true };
            store.Save("vpn", settings); AccountSettings read = store.Load("vpn"); Equal(settings.ApiKey, read.ApiKey); Equal("fixture-akid", read.AccessKeyId); Equal(settings.VpnUrl, read.VpnUrl); Equal("fixture-team", read.TeamId); Equal("cn", read.Region); Equal(true, read.UseWebLogin);
            byte[] bytes = File.ReadAllBytes(Directory.GetFiles(dir).Single()); string disk = Encoding.UTF8.GetString(bytes); NotContains(disk, settings.ApiKey); NotContains(disk, settings.VpnUrl);
            string plaintext = Encoding.UTF8.GetString(ProtectedData.Unprotect(bytes, Encoding.UTF8.GetBytes("BalancePanel:vpn"), DataProtectionScope.CurrentUser)); Contains(plaintext, "synthetic-secret-only");
        });
        Test("provider storage isolated and replacement atomic", delegate { string dir = NewDirectory(); SecureSettings store = new SecureSettings(dir); store.Save("deepseek", new AccountSettings { ApiKey = "fixture-a" }); store.Save("xai", new AccountSettings { ApiKey = "fixture-b" }); store.Save("deepseek", new AccountSettings { ApiKey = "fixture-c" }); Equal("fixture-c", store.Load("deepseek").ApiKey); Equal("fixture-b", store.Load("xai").ApiKey); Equal(2, Directory.GetFiles(dir).Length); });
        Test("corruption load fails and save preserves original", delegate { string dir = NewDirectory(); string path = Path.Combine(dir, "deepseek.dat"); byte[] corrupt = Encoding.UTF8.GetBytes("corrupt-fixture-not-a-key"); File.WriteAllBytes(path, corrupt); SecureSettings store = new SecureSettings(dir); Throws<InvalidDataException>(delegate { store.Load("deepseek"); }); Throws<InvalidDataException>(delegate { store.Save("deepseek", new AccountSettings()); }); True(corrupt.SequenceEqual(File.ReadAllBytes(path)), "corrupt file changed"); Equal(1, Directory.GetFiles(dir).Length); });
        Test("provider swap ciphertext fails", delegate { string dir = NewDirectory(); SecureSettings store = new SecureSettings(dir); store.Save("deepseek", new AccountSettings { ApiKey = "fixture-only" }); File.Copy(Path.Combine(dir, "deepseek.dat"), Path.Combine(dir, "xai.dat")); Throws<InvalidDataException>(delegate { store.Load("xai"); }); });
        foreach (string provider in new string[] { "../escape", "", "DeepSeek", "unknown", "vpn/path" })
        { string fixture = provider; Test("settings whitelist " + provider, delegate { SecureSettings store = new SecureSettings(NewDirectory()); Throws<ArgumentException>(delegate { store.Load(fixture); }); Throws<ArgumentException>(delegate { store.Save(fixture, new AccountSettings()); }); }); }
    }

    private static void ClientTests()
    {
        Test("DeepSeek GET bearer correct endpoint", delegate { WithClient(Deep, delegate(ProviderClient c, FixtureHandler h) { Equal("CNY 12.50", Get(c, "deepseek", new AccountSettings { ApiKey = "fixture-key" }).DisplayValue); Equal("GET", h.Method); Equal("https://api.deepseek.com/user/balance", h.Url); Equal("Bearer fixture-key", h.Authorization); }); });
        Test("xAI team escaped and management endpoint", delegate { WithClient("{\"total\":{\"val\":\"-500\"}}", delegate(ProviderClient c, FixtureHandler h) { Equal("USD 5.00", Get(c, "xai", new AccountSettings { ApiKey = "fixture-management", TeamId = "a ?#" }).DisplayValue); Equal("https://management-api.x.ai/v1/billing/teams/a%20%3F%23/prepaid/balance", h.Url); Equal("GET", h.Method); }); });
        foreach (string region in new string[] { "cn", "global" })
        { string fixture = region; Test("MiniMax region " + region, delegate { WithClient(Mini, delegate(ProviderClient c, FixtureHandler h) { Equal((fixture == "cn" ? "CNY " : "USD ") + "98.00", Get(c, "minimax", new AccountSettings { ApiKey = "sk-api-fixture", Region = fixture }).DisplayValue); Equal("https://api.minimax." + (fixture == "cn" ? "cn" : "io") + "/account/query_balance", h.Url); Equal("Bearer sk-api-fixture", h.Authorization); Equal("GET", h.Method); Equal("application/json", h.Accept); }); }); }
        Test("Kimi cn endpoint", delegate { WithClient("{\"code\":0,\"status\":true,\"data\":{\"available_balance\":2}}", delegate(ProviderClient c, FixtureHandler h) { Equal("CNY 2.00", Get(c, "kimi", new AccountSettings { ApiKey = "fixture", Region = "cn" }).DisplayValue); Equal("https://api.moonshot.cn/v1/users/me/balance", h.Url); Equal("Bearer fixture", h.Authorization); }); });
        Test("Kimi global endpoint", delegate { WithClient("{\"code\":0,\"status\":true,\"data\":{\"available_balance\":2}}", delegate(ProviderClient c, FixtureHandler h) { Equal("USD 2.00", Get(c, "kimi", new AccountSettings { ApiKey = "fixture", Region = "global" }).DisplayValue); Equal("https://api.moonshot.ai/v1/users/me/balance", h.Url); }); });
        Test("Qwen BSS QueryAccountBalance signed GET", delegate { WithClient("{\"Code\":\"200\",\"Success\":true,\"Data\":{\"AvailableAmount\":\"4.00\",\"Currency\":\"CNY\"}}", delegate(ProviderClient c, FixtureHandler h) {
            Equal("CNY 4.00", Get(c, "qwen", new AccountSettings { AccessKeyId = "fixture-akid", ApiKey = "fixture-secret" }).DisplayValue);
            True(h.Url.StartsWith("https://business.aliyuncs.com/?", System.StringComparison.Ordinal), "wrong host");
            Contains(h.Url, "Action=QueryAccountBalance");
            Contains(h.Url, "AccessKeyId=fixture-akid");
            Contains(h.Url, "Signature=");
            Equal(null, h.Authorization);
            Equal("GET", h.Method);
            Equal("application/json", h.Accept);
        }); });
        Test("Qwen rejects model API Key", delegate { WithClient("", delegate(ProviderClient c, FixtureHandler h) {
            ProviderException e = Throws<ProviderException>(delegate { Get(c, "qwen", new AccountSettings { ApiKey = "sk-ws-fixture-model-key" }); });
            Contains(e.Message, "AccessKey");
            Equal(0, h.Calls);
        }); });
        Test("Qwen rejects missing AccessKeyId", delegate { WithClient("", delegate(ProviderClient c, FixtureHandler h) {
            Throws<ProviderException>(delegate { Get(c, "qwen", new AccountSettings { ApiKey = "fixture-secret-not-model" }); });
            Equal(0, h.Calls);
        }); });
        Test("GLM open.bigmodel.cn endpoint", delegate { WithClient("{\"code\":200,\"success\":true,\"data\":{\"availableBalance\":9}}", delegate(ProviderClient c, FixtureHandler h) { Equal("CNY 9.00", Get(c, "glm", new AccountSettings { ApiKey = "fixture" }).DisplayValue); Equal("https://open.bigmodel.cn/api/biz/account/query-customer-account-report", h.Url); Equal("Bearer fixture", h.Authorization); }); });
        Test("Kimi unknown region does not request", delegate { WithClient("", delegate(ProviderClient c, FixtureHandler h) { Throws<ProviderException>(delegate { Get(c, "kimi", new AccountSettings { ApiKey = "fixture", Region = "bad" }); }); Equal(0, h.Calls); }); });
        Test("VPN user URL only no auth; HTTP allowed", delegate { WithClient("<p>剩余流量 9 GB</p>", delegate(ProviderClient c, FixtureHandler h) { Equal("9 GB", Get(c, "vpn", new AccountSettings { VpnUrl = "http://fixture.invalid/only?token=synthetic" }).DisplayValue); Equal("http://fixture.invalid/only?token=synthetic", h.Url); Equal(null, h.Authorization); Equal("GET", h.Method); }); });
        foreach (string provider in new string[] { "mimo", "openai", "gemini" })
        { string fixture = provider; Test("web login defined " + provider, delegate { WithClient("", delegate(ProviderClient c, FixtureHandler h) { WebLoginRequiredException ex = Throws<WebLoginRequiredException>(delegate { Get(c, fixture, new AccountSettings()); }); Contains(ex.Message, "需网页登录"); Equal(fixture, ex.Provider); Equal(0, h.Calls); }); }); }
        foreach (string team in new string[] { "a/b", "a\\b" })
        { string fixture = team; Test("xAI path separator fails closed", delegate { WithClient("{\"total\":{\"val\":\"-500\"}}", delegate(ProviderClient c, FixtureHandler h) { Throws<ProviderException>(delegate { Get(c, "xai", new AccountSettings { ApiKey = "fixture", TeamId = fixture }); }); Equal(0, h.Calls); }); }); }
        Test("xAI missing team does not request", delegate { WithClient("", delegate(ProviderClient c, FixtureHandler h) { Throws<ProviderException>(delegate { Get(c, "xai", new AccountSettings { ApiKey = "fixture" }); }); Equal(0, h.Calls); }); });
        Test("MiniMax unknown region does not request", delegate { WithClient("", delegate(ProviderClient c, FixtureHandler h) { Throws<ProviderException>(delegate { Get(c, "minimax", new AccountSettings { ApiKey = "sk-api-fixture", Region = "bad" }); }); Equal(0, h.Calls); }); });
        Test("MiniMax coding key not treated as balance", delegate { WithClient("", delegate(ProviderClient c, FixtureHandler h) { Throws<ProviderException>(delegate { Get(c, "minimax", new AccountSettings { ApiKey = "coding-fixture", Region = "cn" }); }); Equal(0, h.Calls); }); });
        Test("invalid VPN URI sanitized", delegate { WithClient("", delegate(ProviderClient c, FixtureHandler h) { ProviderException e = Throws<ProviderException>(delegate { Get(c, "vpn", new AccountSettings { VpnUrl = "file:///private-fixture" }); }); NotContains(e.ToString(), "private-fixture"); Equal(0, h.Calls); }); });
        Test("HTTP error only status no response URL or reason", delegate { FixtureHandler h = new FixtureHandler("private-response-fixture"); h.Status = HttpStatusCode.Forbidden; using (ProviderClient c = new ProviderClient(h)) { ProviderException e = Throws<ProviderException>(delegate { Get(c, "vpn", new AccountSettings { VpnUrl = "https://fixture.invalid/private-url-fixture" }); }); Contains(e.Message, "403"); Equal("vpn", e.Provider); Equal(403, e.StatusCode.Value); NotContains(e.ToString(), "private-response-fixture"); NotContains(e.ToString(), "private-url-fixture"); } });
        Test("transport exception sanitized including inner", delegate { FixtureHandler h = new FixtureHandler(""); h.Failure = new HttpRequestException("https://fixture.invalid/private-error-fixture"); using (ProviderClient c = new ProviderClient(h)) { ProviderException e = Throws<ProviderException>(delegate { Get(c, "deepseek", new AccountSettings { ApiKey = "fixture-key" }); }); NotContains(e.ToString(), "private-error-fixture"); Equal("deepseek", e.Provider); } });
        Test("declared oversized response fails", delegate { WithClient(new string('x', 1048577), delegate(ProviderClient c, FixtureHandler h) { Throws<ProviderException>(delegate { Get(c, "deepseek", new AccountSettings { ApiKey = "fixture-key" }); }); }); });
        Test("chunked oversized response fails", delegate { FixtureHandler h = new FixtureHandler(""); h.StreamBody = true; using (ProviderClient c = new ProviderClient(h)) { Throws<ProviderException>(delegate { Get(c, "vpn", new AccountSettings { VpnUrl = "https://fixture.invalid/" }); }); } });
        Test("response parse error sanitized", delegate { WithClient("<html>private-response-fixture</html>", delegate(ProviderClient c, FixtureHandler h) { ProviderException e = Throws<ProviderException>(delegate { Get(c, "deepseek", new AccountSettings { ApiKey = "fixture-key" }); }); NotContains(e.ToString(), "private-response-fixture"); }); });
        Test("provider failure does not poison next request", delegate { WithClient(Deep, delegate(ProviderClient c, FixtureHandler h) { h.Status = HttpStatusCode.Unauthorized; Throws<ProviderException>(delegate { Get(c, "xai", new AccountSettings { ApiKey = "fixture", TeamId = "fixture" }); }); h.Status = HttpStatusCode.OK; Equal("CNY 12.50", Get(c, "deepseek", new AccountSettings { ApiKey = "fixture" }).DisplayValue); Equal(2, h.Calls); }); });
        Test("disposed client rejects request", delegate { ProviderClient c = new ProviderClient(new FixtureHandler(Deep)); c.Dispose(); Throws<ObjectDisposedException>(delegate { Get(c, "deepseek", new AccountSettings { ApiKey = "fixture" }); }); });
    }

    private static Reading Get(ProviderClient c, string p, AccountSettings s) { return c.FetchAsync(p, s).GetAwaiter().GetResult(); }
    private static void WithClient(string body, Action<ProviderClient, FixtureHandler> action) { FixtureHandler h = new FixtureHandler(body); using (ProviderClient c = new ProviderClient(h)) action(c, h); }
    private static void Page(string p, string text, string expected) { Test("page " + p + " " + text.Replace("\n", " "), delegate { Equal(expected, BalanceParsers.PageBalance(p, text).DisplayValue); }); }
    private static void Vpn(string html, string expected) { Test("VPN remaining " + expected, delegate { Equal(expected, BalanceParsers.Vpn(html).DisplayValue); }); }
    private static void BadDeep(string name, string json) { Test("DeepSeek rejects " + name, delegate { FormatException ex = Throws<FormatException>(delegate { BalanceParsers.DeepSeek(json); }); NotContains(ex.ToString(), "private_fixture_payload"); }); }
    private static string NewDirectory() { string path = Path.Combine(Root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private static void Fresh(Reading r) { True((DateTime.UtcNow - r.Timestamp.ToUniversalTime()).Duration().TotalSeconds < 5, "timestamp stale"); }
    private static void Test(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + " => " + ex.GetType().Name + ": " + ex.Message); } }
    private static void Equal<T>(T expected, T actual) { if (!object.Equals(expected, actual)) throw new Exception("expected [" + expected + "] actual [" + actual + "]"); }
    private static void True(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Contains(string text, string value) { True(text != null && text.Contains(value), "missing expected marker " + value); }
    private static void NotContains(string text, string value) { True(text == null || !text.Contains(value), "sensitive fixture leaked"); }
    private static T Throws<T>(Action action) where T : Exception { try { action(); } catch (T e) { return e; } throw new Exception("expected " + typeof(T).Name); }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        internal string Body, Method, Url, Authorization, Accept;
        internal int Calls;
        internal bool StreamBody;
        internal HttpStatusCode Status = HttpStatusCode.OK;
        internal Exception Failure;
        internal FixtureHandler(string body) { Body = body; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Method = request.Method.Method; Url = request.RequestUri.AbsoluteUri;
            Authorization = request.Headers.Authorization == null ? null : request.Headers.Authorization.ToString(); Accept = request.Headers.Accept.ToString();
            if (Failure != null) throw Failure;
            HttpResponseMessage response = new HttpResponseMessage(Status);
            response.Content = StreamBody ? (HttpContent)new StreamContent(new UnseekableFixtureStream()) : new StringContent(Body, Encoding.UTF8);
            return Task.FromResult(response);
        }
    }
    private sealed class UnseekableFixtureStream : Stream
    {
        private int count;
        public override bool CanRead { get { return true; } } public override bool CanSeek { get { return false; } } public override bool CanWrite { get { return false; } }
        public override long Length { get { throw new NotSupportedException(); } } public override long Position { get { return count; } set { throw new NotSupportedException(); } }
        public override int Read(byte[] buffer, int offset, int size) { int n = Math.Min(size, 1048577 - count); for (int i = 0; i < n; i++) buffer[offset + i] = (byte)'x'; count += n; return n; }
        public override void Flush() { } public override long Seek(long o, SeekOrigin s) { throw new NotSupportedException(); } public override void SetLength(long n) { throw new NotSupportedException(); } public override void Write(byte[] b, int o, int n) { throw new NotSupportedException(); }
    }
}
