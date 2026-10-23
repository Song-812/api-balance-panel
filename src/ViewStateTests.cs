using System;
using BalancePanel;

internal static class ViewStateTests
{
    private static int passed;
    private static int failed;
    private static void Check(string name, Action action)
    {
        try { action(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
    }
    private static void Equal<T>(T want, T got)
    {
        if (!object.Equals(want, got)) throw new Exception("Expected " + want + ", got " + got);
    }
    public static int Main()
    {
        Check("unconfigured rows never invent a zero balance", delegate {
            RowState row = new RowState(); Equal("—", row.Value); Equal("未配置", row.Status);
        });
        Check("a second refresh cannot overlap the first", delegate {
            RowState row = new RowState(); Equal(true, row.BeginRefresh()); Equal(false, row.BeginRefresh());
        });
        Check("successful refresh displays source value and time", delegate {
            RowState row = new RowState(); row.BeginRefresh();
            row.Accept("USD 12.34", "可用余额", new DateTime(2026, 10, 18, 12, 30, 0));
            Equal("USD 12.34", row.Value); Equal(false, row.IsBusy); Equal("可用余额", row.Note);
            Equal("12:30:00 更新", row.Status); Equal(true, row.BeginRefresh());
        });
        Check("failed refresh keeps last success visibly stale", delegate {
            RowState row = new RowState(); row.Accept("CNY 9.80", "可用总额", new DateTime(2026,10,18,12,30,0));
            row.BeginRefresh(); row.Fail("登录已过期");
            Equal("CNY 9.80", row.Value); Equal(true, row.IsStale); Equal(false, row.IsBusy);
            Equal("登录已过期 · 上次 12:30:00", row.Status);
        });
        Check("failure without prior reading stays empty", delegate {
            RowState row = new RowState(); row.BeginRefresh(); row.Fail("网络不可用");
            Equal("—", row.Value); Equal("网络不可用", row.Status); Equal(false, row.IsStale);
        });
        Check("reconfiguration removes the old account reading", delegate {
            RowState row = new RowState(); row.Accept("USD 2.00", "余额", DateTime.Now); row.Reset();
            Equal("—", row.Value); Equal("未配置", row.Status); Equal(false, row.IsBusy);
            Equal(false, row.IsStale);
        });
        Check("balance text changes emit observable notifications", delegate {
            RowState row = new RowState(); bool changed = false;
            row.PropertyChanged += delegate(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
                if (e.PropertyName == "Value") changed = true;
            };
            row.Accept("CNY 0.00", "余额", DateTime.Now); Equal(true, changed); Equal("CNY 0.00", row.Value);
        });
        Check("a slow balance card does not disable future automatic retries", delegate {
            BrowserReadState state = new BrowserReadState(); state.MissedReading(true); Equal(true,state.CanAutoRefresh);
        });
        Check("an authentication redirect pauses automatic reads until user reconnects", delegate {
            BrowserReadState state = new BrowserReadState(); state.MissedReading(false); Equal(false,state.CanAutoRefresh);
            state.Succeeded(); Equal(true,state.CanAutoRefresh);
        });
        Check("only the trusted HTTPS balance route may be read", delegate {
            Uri target = new Uri("https://example.com/console/balance");
            Equal(true,BrowserReadState.IsBalancePage(new Uri("https://example.com/console/balance"),target));
            Equal(true,BrowserReadState.IsBalancePage(new Uri("https://example.com/#/console/balance"),target));
            Equal(false,BrowserReadState.IsBalancePage(new Uri("https://example.com/login"),target));
            Equal(false,BrowserReadState.IsBalancePage(new Uri("https://evil.example/console/balance"),target));
            Equal(false,BrowserReadState.IsBalancePage(new Uri("http://example.com/console/balance"),target));
            Equal(false,BrowserReadState.IsBalancePage(new Uri("https://example.com/console/balance-other"),target));
            Equal(false,BrowserReadState.IsBalancePage(new Uri("https://example.com:444/console/balance"),target));
        });
        Console.WriteLine("ViewState: " + passed + " passed, " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
