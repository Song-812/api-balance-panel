using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace BalancePanel
{
    public class Reading
    {
        public string DisplayValue { get; set; }
        public string Note { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class AccountSettings
    {
        public string ApiKey { get; set; }
        public string AccessKeyId { get; set; }
        public string TeamId { get; set; }
        public string VpnUrl { get; set; }
        public string Region { get; set; }
        public bool UseWebLogin { get; set; }
    }

    internal static class Providers
    {
        internal static bool IsKnown(string provider)
        {
            return provider == "deepseek" || provider == "xai" || provider == "minimax" ||
                provider == "mimo" || provider == "openai" || provider == "gemini" ||
                provider == "qwen" || provider == "glm" || provider == "kimi" || provider == "vpn";
        }

        internal static void Validate(string provider)
        {
            if (!IsKnown(provider)) throw new ArgumentException("不支持的服务商。", "provider");
        }
    }

    public static class BalanceParsers
    {
        private const int MaximumTextLength = 1048576;
        private const string DecimalPattern = @"-?(?:[0-9]+|[0-9]{1,3}(?:,[0-9]{3})+)(?:\.[0-9]+)?";
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

        // https://api-docs.deepseek.com/api/get-user-balance : total_balance includes grants.
        public static Reading DeepSeek(string json)
        {
            Dictionary<string, object> root = JsonObject(json);
            object raw;
            if (!root.TryGetValue("balance_infos", out raw)) throw InvalidBalance();
            IEnumerable items = raw as IEnumerable;
            if (items == null || raw is string || raw is IDictionary) throw InvalidBalance();
            List<string> values = new List<string>();
            HashSet<string> currencies = new HashSet<string>(StringComparer.Ordinal);
            foreach (object item in items)
            {
                Dictionary<string, object> balance = ObjectValue(item);
                string currency = Field(balance, "currency") as string;
                if ((currency != "CNY" && currency != "USD") || !currencies.Add(currency)) throw InvalidBalance();
                values.Add(currency + " " + Money(DecimalValue(Field(balance, "total_balance"))));
            }
            if (values.Count == 0) throw InvalidBalance();
            return Result(string.Join(" / ", values.ToArray()), "可用总余额（含未过期赠送余额与充值余额）");
        }

        // Official schema: https://docs.x.ai/developers/rest-api-reference/management/billing
        // total.val is a signed USD-cent ledger; purchases are negative, not positive.
        public static Reading Xai(string json)
        {
            Dictionary<string, object> total = ObjectValue(Field(JsonObject(json), "total"));
            string cents = Field(total, "val") as string;
            if (cents == null || !Match(cents, @"\A-?[0-9]+\z").Success) throw InvalidBalance();
            decimal value = DecimalValue(cents);
            return Result("USD " + Money(-value / 100m), "已入账预付余额（美元分取反换算；非实时可用余额，未入账使用可能尚未扣减）");
        }

        // MiniMax-AI/cli ca1724896e24c370e6981ba210e732450dd5a0b8:
        // src/types/api.ts AccountBalanceResponse; no currency field is documented, so the
        // currency follows the site the account belongs to (CN site shows ¥, global site $).
        public static Reading MiniMax(string json, string currency)
        {
            if (currency != "CNY" && currency != "USD") throw InvalidBalance();
            Dictionary<string, object> root = JsonObject(json);
            Dictionary<string, object> response = ObjectValue(Field(root, "base_resp"));
            if (DecimalValue(Field(response, "status_code")) != 0m) throw InvalidBalance();
            decimal available = DecimalValue(Field(root, "available_amount"));
            decimal owed = DecimalValue(Field(root, "owed_amount"));
            return Result(currency + " " + Money(available), "账户可用总余额（可能含赠送、代金券或信用额度；接口未提供币种，按所选站点标注为 " + currency + "）；欠款 " + Money(owed) + "（单独列示，不重复扣减）");
        }

        // The CN wallet page renders available_amount from GET https://www.minimax.cn/account/query_balance.
        // The widget animates from 0, so the rendered number is never used as the balance.
        public static Reading MiniMaxWeb(string json, string currency)
        {
            if (currency != "CNY" && currency != "USD") throw InvalidBalance();
            Dictionary<string, object> root = JsonObject(json);
            Dictionary<string, object> response = ObjectValue(Field(root, "base_resp"));
            object status = Field(response, "status_code");
            // The page treats only a numeric zero status as success; HTTP 200 is not enough.
            if (!(status is int) || Convert.ToInt32(status, CultureInfo.InvariantCulture) != 0) throw InvalidBalance();
            decimal available = DecimalValue(Field(root, "available_amount"));
            string note = "网页余额接口的可用额度（含现金、代金券、授信并扣除欠费；不是现金余额单独值）";
            object owedRaw;
            if (!root.TryGetValue("owed_amount", out owedRaw)) owedRaw = null;
            if (owedRaw != null && !(owedRaw is string && ((string)owedRaw).Length == 0))
                note += "；欠款 " + Money(DecimalValue(owedRaw)) + "（已从可用额度中扣除）";
            return Result(currency + " " + Money(available), note);
        }

        // https://platform.kimi.com/docs/api/balance (cn) and platform.kimi.ai (global).
        public static Reading Kimi(string json, string currency)
        {
            if (currency != "CNY" && currency != "USD") throw InvalidBalance();
            Dictionary<string, object> root = JsonObject(json);
            object codeRaw;
            bool hasCode = root.TryGetValue("code", out codeRaw) && codeRaw != null && !(codeRaw is string && ((string)codeRaw).Length == 0);
            object statusRaw;
            root.TryGetValue("status", out statusRaw);
            bool ok = hasCode ? DecimalValue(codeRaw) == 0m : (statusRaw is bool && (bool)statusRaw);
            if (!ok) throw InvalidBalance();
            Dictionary<string, object> data = ObjectValue(Field(root, "data"));
            decimal available = DecimalValue(Field(data, "available_balance"));
            return Result(currency + " " + Money(available), "可用余额（含现金与代金券；按所选站点标注为 " + currency + "）");
        }

        // Official BSS OpenAPI QueryAccountBalance:
        // https://help.aliyun.com/zh/user-center/developer-reference/api-bssopenapi-2017-12-14-queryaccountbalance
        public static Reading Qwen(string json)
        {
            Dictionary<string, object> root = JsonObject(json);
            object successRaw;
            if (root.TryGetValue("Success", out successRaw) || root.TryGetValue("success", out successRaw))
            {
                if (successRaw is bool && !(bool)successRaw) throw InvalidBalance();
            }
            object codeRaw = Optional(root, "Code") ?? Optional(root, "code");
            if (codeRaw != null)
            {
                string codeText = Convert.ToString(codeRaw, CultureInfo.InvariantCulture);
                if (!string.Equals(codeText, "200", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(codeText, "OK", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(codeText, "Success", StringComparison.OrdinalIgnoreCase))
                    throw InvalidBalance();
            }
            object dataRaw = Optional(root, "Data") ?? Optional(root, "data");
            if (dataRaw == null) throw InvalidBalance();
            Dictionary<string, object> data = ObjectValue(dataRaw);
            object amount = Optional(data, "AvailableAmount") ?? Optional(data, "availableAmount");
            if (amount == null) throw InvalidBalance();
            object currencyRaw = Optional(data, "Currency") ?? Optional(data, "currency");
            string currency = currencyRaw == null ? "CNY" : Convert.ToString(currencyRaw, CultureInfo.InvariantCulture);
            if (currency != "CNY" && currency != "USD" && currency != "JPY") throw InvalidBalance();
            return Result(currency + " " + Money(DecimalValue(amount)), "阿里云账户可用额度（可能含信控，非百炼模型 Key 余额）");
        }

        // Console account report on open.bigmodel.cn (pay-as-you-go wallet, not Coding Plan quota).
        public static Reading Glm(string json)
        {
            Dictionary<string, object> root = JsonObject(json);
            object successRaw;
            if (root.TryGetValue("success", out successRaw) && successRaw is bool && !(bool)successRaw) throw InvalidBalance();
            object codeRaw;
            if (root.TryGetValue("code", out codeRaw) && codeRaw != null)
            {
                decimal code = DecimalValue(codeRaw);
                if (code != 200m && code != 0m) throw InvalidBalance();
            }
            Dictionary<string, object> data = ObjectValue(Field(root, "data"));
            object amount = Optional(data, "availableBalance") ?? Optional(data, "balance");
            if (amount == null) throw InvalidBalance();
            return Result("CNY " + Money(DecimalValue(amount)), "按量账户可用余额（非 Coding Plan 套餐额度）");
        }

        private static object Optional(Dictionary<string, object> obj, string name)
        {
            object value;
            if (!obj.TryGetValue(name, out value) || value == null) return null;
            return value;
        }
        // Only a local plain-text balance card, not a full page or a usage dashboard.
        public static Reading PageBalance(string provider, string text)
        {
            Providers.Validate(provider);
            if (provider == "vpn") throw InvalidBalance();
            ValidateText(text);
            if (Match(text, @"[<>]|费用|成本|消费|用量|已用|欠费|\b(?:cost|usage|spent|spend|expense)\b").Success) throw InvalidBalance();
            string pattern = @"\A\s*(?:账户余额|可用余额|总余额|余额|赠金余额|可用赠金|可用额度|(?:Credit|Available|Account)\s+balance|Available\s+credits?)\s*[:：]?\s*" +
                @"(?<prefix>USD|CNY|US\$|\$|¥|￥)?\s*(?<amount>" + DecimalPattern + @")\s*(?<suffix>USD|CNY|美元|元)?\s*\z";
            Match match = Match(text, pattern);
            if (!match.Success) throw InvalidBalance();
            string prefix = Currency(match.Groups["prefix"].Value);
            string suffix = Currency(match.Groups["suffix"].Value);
            if ((prefix == null && suffix == null) || (prefix != null && suffix != null && prefix != suffix)) throw InvalidBalance();
            string currency = prefix ?? suffix;
            if ((provider == "openai" || provider == "xai" || provider == "gemini") && currency != "USD") throw InvalidBalance();
            if (provider == "mimo" && currency != "CNY") throw InvalidBalance();
            return Result(currency + " " + Money(DecimalValue(match.Groups["amount"].Value.Replace(",", ""))), "网页余额局部文本（币种已明确；可能包含赠送余额）");
        }

        public static Reading Vpn(string html)
        {
            ValidateText(html);
            if (Match(html, @"<input\b[^>]*\btype\s*=\s*['"" ]*password\b").Success) throw InvalidBalance();
            string markup = Replace(html, @"<!--.*?-->|<(script|style|noscript|template)\b[^>]*>.*?</\1\s*>", " ");
            string text = WebUtility.HtmlDecode(Replace(markup, @"<[^>]*>", " "));
            string pattern = @"(?<![\p{L}\p{N}])(?:剩余流量|本月剩余|剩余)\s*[:：]?\s*" +
                @"(?<amount>[0-9]+(?:\.[0-9]+)?)\s*(?<unit>TiB|GiB|MiB|KiB|TB|GB|MB|KB|B)\b(?!\s*(?:[/／]|每天|每日|每月|每秒))(?![天日])";
            List<Match> matches = new List<Match>();
            foreach (Match found in Regex.Matches(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout)) matches.Add(found);
            // For this reversed card, require immediate sibling elements, not an arbitrary
            // preceding number in flattened page text. The monthly quota is never captured.
            string preceding = @"<(?<valueTag>span|p|div|strong|b|h[1-6])\b[^>]*>\s*" +
                @"(?<amount>[0-9]+(?:\.[0-9]+)?)\s*(?<unit>TiB|GiB|MiB|KiB|TB|GB|MB|KB|B)\s*</\k<valueTag>\s*>\s*" +
                @"<(?<labelTag>span|p|div|small|strong|b|h[1-6])\b[^>]*>\s*剩余免费出站流量" +
                @"(?:\s*[（(]\s*本月额度\s*[:：]?\s*[0-9]+(?:\.[0-9]+)?\s*(?:TiB|GiB|MiB|KiB|TB|GB|MB|KB|B)\s*[）)])?\s*</\k<labelTag>\s*>";
            string spacedMarkup = Replace(markup, @"&(?:nbsp|#160|#x0*a0);", " ");
            foreach (Match found in Regex.Matches(spacedMarkup, preceding, RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout)) matches.Add(found);
            if (matches.Count != 1) throw InvalidBalance();
            decimal amount = DecimalValue(matches[0].Groups["amount"].Value);
            string unit = matches[0].Groups["unit"].Value;
            string canonical = unit.Length == 3 ? char.ToUpperInvariant(unit[0]) + "iB" : unit.ToUpperInvariant();
            return Result(amount.ToString("0.############################", CultureInfo.InvariantCulture) + " " + canonical, "标签关联的剩余流量（非已用或日均流量）");
        }

        private static string Currency(string token)
        {
            if (token.Length == 0) return null;
            if (string.Equals(token, "USD", StringComparison.OrdinalIgnoreCase) || token == "$" || token.Equals("US$", StringComparison.OrdinalIgnoreCase) || token == "美元") return "USD";
            return "CNY";
        }

        internal static Dictionary<string, object> JsonObject(string json)
        {
            ValidateText(json);
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = MaximumTextLength, RecursionLimit = 32 };
                return ObjectValue(serializer.DeserializeObject(json));
            }
            catch (Exception ex)
            {
                if (!(ex is ArgumentException) && !(ex is InvalidOperationException) && !(ex is FormatException)) throw;
                throw InvalidBalance();
            }
        }

        private static Dictionary<string, object> ObjectValue(object value)
        {
            Dictionary<string, object> result = value as Dictionary<string, object>;
            if (result == null) throw InvalidBalance();
            return result;
        }

        private static object Field(Dictionary<string, object> obj, string name)
        {
            object value;
            if (!obj.TryGetValue(name, out value) || value == null) throw InvalidBalance();
            return value;
        }

        private static decimal DecimalValue(object value)
        {
            if (!(value is string) && !(value is int) && !(value is long) && !(value is decimal) && !(value is double)) throw InvalidBalance();
            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            decimal amount;
            if (!Match(text, @"\A-?[0-9]+(?:\.[0-9]+)?\z").Success || !decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount)) throw InvalidBalance();
            return amount;
        }

        private static void ValidateText(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumTextLength) throw InvalidBalance();
        }

        private static Match Match(string text, string pattern)
        {
            return Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);
        }

        private static string Replace(string text, string pattern, string replacement)
        {
            return Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);
        }

        private static string Money(decimal value) { return value.ToString("0.00##########################", CultureInfo.InvariantCulture); }
        private static FormatException InvalidBalance() { return new FormatException("未找到明确、有效且单位正确的余额或剩余流量。"); }
        private static Reading Result(string value, string note) { return new Reading { DisplayValue = value, Note = note, Timestamp = DateTime.UtcNow }; }
    }

    public class SecureSettings
    {
        private readonly string directory;
        private static readonly object WriteGate = new object();
        private const int MaximumSettingsLength = 1048576;

        public SecureSettings(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("设置目录不能为空。", "directory");
            this.directory = Path.GetFullPath(directory);
        }

        public AccountSettings Load(string provider)
        {
            Providers.Validate(provider);
            lock (WriteGate) return LoadFile(provider, SettingsPath(provider));
        }

        public void Save(string provider, AccountSettings settings)
        {
            Providers.Validate(provider);
            if (settings == null) throw new ArgumentNullException("settings");
            lock (WriteGate)
            {
                string path = SettingsPath(provider);
                // Never replace an unreadable existing file, even when Save is called directly.
                LoadFile(provider, path);
                byte[] plain = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(settings));
                byte[] encrypted;
                try
                {
                    if (plain.Length > MaximumSettingsLength) throw new ArgumentException("设置内容过长。");
                    encrypted = ProtectedData.Protect(plain, Entropy(provider), DataProtectionScope.CurrentUser);
                }
                finally { Array.Clear(plain, 0, plain.Length); }
                Directory.CreateDirectory(directory);
                string temporary = Path.Combine(directory, "." + provider + "." + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(encrypted, 0, encrypted.Length);
                        stream.Flush(true);
                    }
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                }
                finally
                {
                    // temporary is generated in this directory and contains ciphertext only.
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
        }

        private AccountSettings LoadFile(string provider, string path)
        {
            if (!File.Exists(path)) return new AccountSettings();
            byte[] plain = null;
            try
            {
                FileInfo info = new FileInfo(path);
                if (info.Length == 0) return new AccountSettings();
                if (info.Length > MaximumSettingsLength + 4096) throw CorruptSettings();
                byte[] encrypted = File.ReadAllBytes(path);
                plain = ProtectedData.Unprotect(encrypted, Entropy(provider), DataProtectionScope.CurrentUser);
                string json = new UTF8Encoding(false, true).GetString(plain);
                Dictionary<string, object> obj = BalanceParsers.JsonObject(json);
                return new AccountSettings { ApiKey = Setting(obj, "ApiKey"), AccessKeyId = Setting(obj, "AccessKeyId"), TeamId = Setting(obj, "TeamId"), VpnUrl = Setting(obj, "VpnUrl"), Region = Setting(obj, "Region"), UseWebLogin = BooleanSetting(obj, "UseWebLogin") };
            }
            catch (Exception ex)
            {
                if (!(ex is CryptographicException) && !(ex is FormatException) && !(ex is ArgumentException)) throw;
                throw CorruptSettings();
            }
            finally { if (plain != null) Array.Clear(plain, 0, plain.Length); }
        }

        private static string Setting(Dictionary<string, object> obj, string name)
        {
            object value;
            if (!obj.TryGetValue(name, out value) || value == null) return null;
            if (!(value is string)) throw CorruptSettings();
            return (string)value;
        }

        private static bool BooleanSetting(Dictionary<string, object> obj, string name)
        {
            object value;
            if (!obj.TryGetValue(name, out value)) return false;
            if (!(value is bool)) throw CorruptSettings();
            return (bool)value;
        }

        private string SettingsPath(string provider) { return Path.Combine(directory, provider + ".dat"); }
        private static byte[] Entropy(string provider) { return Encoding.UTF8.GetBytes("BalancePanel:" + provider); }
        private static InvalidDataException CorruptSettings() { return new InvalidDataException("设置文件已损坏或无法解密；原文件未覆盖。"); }
    }

    public class ProviderException : Exception
    {
        public string Provider { get; private set; }
        public int? StatusCode { get; private set; }
        public ProviderException(string provider, string message, int? statusCode) : base(message)
        {
            Provider = provider;
            StatusCode = statusCode;
        }
    }

    public class WebLoginRequiredException : ProviderException
    {
        public WebLoginRequiredException(string provider) : base(provider, "需网页登录", null) { }
    }

    // BSS OpenAPI RPC signature (HMAC-SHA1 / SignatureVersion 1.0).
    // https://help.aliyun.com/zh/user-center/developer-reference/signature
    internal static class AliyunRpc
    {
        internal const string BusinessEndpoint = "https://business.aliyuncs.com/";

        internal static Uri QueryAccountBalance(string accessKeyId, string accessKeySecret)
        {
            return QueryAccountBalance(accessKeyId, accessKeySecret, DateTime.UtcNow, Guid.NewGuid().ToString("N"));
        }

        internal static Uri QueryAccountBalance(string accessKeyId, string accessKeySecret, DateTime utcTimestamp, string signatureNonce)
        {
            if (string.IsNullOrWhiteSpace(accessKeyId) || string.IsNullOrWhiteSpace(accessKeySecret))
                throw new ArgumentException("AccessKey 不能为空。");
            if (string.IsNullOrWhiteSpace(signatureNonce)) throw new ArgumentException("SignatureNonce 不能为空。");
            SortedDictionary<string, string> parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                { "AccessKeyId", accessKeyId.Trim() },
                { "Action", "QueryAccountBalance" },
                { "Format", "JSON" },
                { "SignatureMethod", "HMAC-SHA1" },
                { "SignatureNonce", signatureNonce },
                { "SignatureVersion", "1.0" },
                { "Timestamp", utcTimestamp.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) },
                { "Version", "2017-12-14" }
            };
            string signature = Sign("GET", parameters, accessKeySecret.Trim());
            parameters.Add("Signature", signature);
            StringBuilder query = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in parameters)
            {
                if (query.Length > 0) query.Append('&');
                query.Append(PercentEncode(pair.Key)).Append('=').Append(PercentEncode(pair.Value));
            }
            return new Uri(BusinessEndpoint + "?" + query.ToString());
        }

        internal static string Sign(string method, SortedDictionary<string, string> parameters, string accessKeySecret)
        {
            StringBuilder canonical = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in parameters)
            {
                if (string.Equals(pair.Key, "Signature", StringComparison.Ordinal)) continue;
                if (canonical.Length > 0) canonical.Append('&');
                canonical.Append(PercentEncode(pair.Key)).Append('=').Append(PercentEncode(pair.Value));
            }
            string stringToSign = PercentEncode(method) + "&" + PercentEncode("/") + "&" + PercentEncode(canonical.ToString());
            using (HMACSHA1 hmac = new HMACSHA1(Encoding.UTF8.GetBytes(accessKeySecret + "&")))
            {
                return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
            }
        }

        internal static string PercentEncode(string value)
        {
            if (value == null) return string.Empty;
            string encoded = Uri.EscapeDataString(value);
            return encoded.Replace("+", "%20").Replace("*", "%2A").Replace("%7E", "~").Replace("%7e", "~");
        }
    }

    public class ProviderClient : IDisposable
    {
        private const int MaximumResponseBytes = 1048576;
        private readonly HttpClient http;
        private bool disposed;

        public ProviderClient() : this(CreateHandler()) { }
        internal ProviderClient(HttpMessageHandler handler)
        {
            http = new HttpClient(handler, true);
            http.Timeout = TimeSpan.FromSeconds(18);
        }

        private static HttpMessageHandler CreateHandler()
        {
            // No redirects: VPN links can carry secrets in path/query; do not forward them.
            return new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false };
        }

        public async Task<Reading> FetchAsync(string provider, AccountSettings settings)
        {
            if (disposed) throw new ObjectDisposedException("ProviderClient");
            Providers.Validate(provider);
            if (provider == "mimo" || provider == "openai" || provider == "gemini") throw new WebLoginRequiredException(provider);
            try
            {
                if (settings == null) throw Failure(provider, "尚未配置账户。");
                using (HttpRequestMessage request = BuildRequest(provider, settings))
                using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(18)))
                using (HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) throw new ProviderException(provider, "HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture), (int)response.StatusCode);
                    string body = await ReadLimitedAsync(response.Content, timeout.Token).ConfigureAwait(false);
                    if (provider == "deepseek") return BalanceParsers.DeepSeek(body);
                    if (provider == "xai") return BalanceParsers.Xai(body);
                    if (provider == "minimax") return BalanceParsers.MiniMax(body, settings.Region == "global" ? "USD" : "CNY");
                    if (provider == "kimi") return BalanceParsers.Kimi(body, settings.Region == "global" ? "USD" : "CNY");
                    if (provider == "qwen") return BalanceParsers.Qwen(body);
                    if (provider == "glm") return BalanceParsers.Glm(body);
                    return BalanceParsers.Vpn(body);
                }
            }
            catch (ProviderException) { throw; }
            catch (OperationCanceledException) { throw Failure(provider, "读取超时或已取消。"); }
            catch (Exception ex)
            {
                // No original exception or inner exception: framework errors can contain secret URLs.
                if (ex is OutOfMemoryException || ex is StackOverflowException || ex is AccessViolationException) throw;
                throw Failure(provider, "读取失败：网络不可用、响应过大或余额格式无效。");
            }
        }

        private static HttpRequestMessage BuildRequest(string provider, AccountSettings settings)
        {
            if (provider == "vpn")
            {
                Uri url;
                if (!Uri.TryCreate(settings.VpnUrl, UriKind.Absolute, out url) ||
                    (url.Scheme != "https" && url.Scheme != "http") || !string.IsNullOrEmpty(url.UserInfo))
                    throw Failure(provider, "请配置有效的 HTTP 或 HTTPS 地址（不支持地址内嵌用户名密码）。");
                return new HttpRequestMessage(HttpMethod.Get, url);
            }
            if (provider == "qwen")
            {
                string accessKeyId = settings.AccessKeyId == null ? null : settings.AccessKeyId.Trim();
                string accessKeySecret = settings.ApiKey == null ? null : settings.ApiKey.Trim();
                bool looksLikeModelKey = !string.IsNullOrEmpty(accessKeySecret) &&
                    (accessKeySecret.StartsWith("sk-", StringComparison.OrdinalIgnoreCase) ||
                     accessKeySecret.StartsWith("sk-ws-", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(accessKeyId) || string.IsNullOrWhiteSpace(accessKeySecret) || looksLikeModelKey)
                    throw Failure(provider, "请改填阿里云 AccessKey ID 和 Secret（不是百炼模型 API Key）。");
                if (accessKeyId.IndexOfAny(new char[] { '\r', '\n' }) >= 0 || accessKeySecret.IndexOfAny(new char[] { '\r', '\n' }) >= 0)
                    throw Failure(provider, "请配置有效的 AccessKey。");
                HttpRequestMessage qwenRequest = new HttpRequestMessage(HttpMethod.Get, AliyunRpc.QueryAccountBalance(accessKeyId, accessKeySecret));
                qwenRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                return qwenRequest;
            }
            if (string.IsNullOrWhiteSpace(settings.ApiKey) || settings.ApiKey.IndexOfAny(new char[] { '\r', '\n' }) >= 0) throw Failure(provider, "请配置有效的 API Key。");
            string endpoint;
            if (provider == "deepseek") endpoint = "https://api.deepseek.com/user/balance";
            else if (provider == "xai")
            {
                if (string.IsNullOrWhiteSpace(settings.TeamId)) throw Failure(provider, "xAI 需要 Management Key 和 Team ID，不接受推理 Key。");
                // Legacy .NET URI compatibility can decode escaped slashes into route separators.
                if (settings.TeamId.IndexOfAny(new char[] { '/', '\\' }) >= 0) throw Failure(provider, "Team ID 不可包含路径分隔符。");
                endpoint = "https://management-api.x.ai/v1/billing/teams/" + Uri.EscapeDataString(settings.TeamId) + "/prepaid/balance";
            }
            else if (provider == "kimi")
            {
                if (settings.Region != "cn" && settings.Region != "global") throw Failure(provider, "Kimi 需要明确选择 cn 或 global 地区。");
                endpoint = "https://api.moonshot." + (settings.Region == "cn" ? "cn" : "ai") + "/v1/users/me/balance";
            }
            else if (provider == "glm")
            {
                endpoint = "https://open.bigmodel.cn/api/biz/account/query-customer-account-report";
            }
            else
            {
                if (settings.Region != "cn" && settings.Region != "global") throw Failure(provider, "MiniMax 需要明确选择 cn 或 global 地区。");
                if (!settings.ApiKey.StartsWith("sk-api-", StringComparison.Ordinal)) throw Failure(provider, "MiniMax 账户余额需要 sk-api- 账户 Key，Coding Plan 配额不是余额。");
                // MiniMax-AI/cli src/client/endpoints.ts and src/config/schema.ts.
                endpoint = "https://api.minimax." + (settings.Region == "cn" ? "cn" : "io") + "/account/query_balance";
            }
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return request;
        }

        private static async Task<string> ReadLimitedAsync(HttpContent content, CancellationToken cancellation)
        {
            if (content == null || (content.Headers.ContentLength.HasValue && content.Headers.ContentLength.Value > MaximumResponseBytes)) throw new InvalidDataException();
            using (Stream stream = await content.ReadAsStreamAsync().ConfigureAwait(false))
            using (MemoryStream bytes = new MemoryStream())
            {
                byte[] buffer = new byte[8192];
                int count;
                while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) != 0)
                {
                    if (bytes.Length + count > MaximumResponseBytes) throw new InvalidDataException();
                    bytes.Write(buffer, 0, count);
                }
                cancellation.ThrowIfCancellationRequested();
                return new UTF8Encoding(false, true).GetString(bytes.ToArray()).TrimStart('\uFEFF');
            }
        }

        private static ProviderException Failure(string provider, string message) { return new ProviderException(provider, message, null); }
        public void Dispose() { if (!disposed) { disposed = true; http.Dispose(); } }
    }
}
