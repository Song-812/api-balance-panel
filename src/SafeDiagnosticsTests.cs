using System;
using System.IO;
using BalancePanel;
internal static class SafeDiagnosticsTests
{
    public static int Main(string[] args)
    {
        int passed=0,failed=0;
        string root=Path.Combine(args[0],"safe-probe-"+Guid.NewGuid().ToString("N"));
        Action<string,Action> test=delegate(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.GetType().Name);}};
        const string clean="{\"DomEntered\":true,\"ExactCreditLabel\":false,\"ContainsCreditLabel\":true,\"LocalizedLabel\":false,\"AlternativeLabel\":false,\"HasFrames\":true,\"HasShadowRoots\":false,\"ChallengePage\":false}";
        test("only fixed boolean structure diagnostics can be written",delegate{
            SafeDiagnostics.Save(root,clean);
            string saved=File.ReadAllText(Path.Combine(root,"last-read-probe.json"));
            var values=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,object>>(saved);
            if(values.Count!=8 || !(bool)values["DomEntered"] || !(bool)values["HasFrames"])throw new Exception();
        });
        test("unexpected token field is rejected without persisting raw text",delegate{
            string badRoot=root+"-bad";bool rejected=false;
            try{SafeDiagnostics.Save(badRoot,clean.Replace("}",",\"Token\":\"synthetic-do-not-write\"}"));}catch(FormatException){rejected=true;}
            if(!rejected || File.Exists(Path.Combine(badRoot,"last-read-probe.json")))throw new Exception();
        });
        test("diagnostic strings cannot smuggle a URL or account value",delegate{
            bool rejected=false;try{SafeDiagnostics.Save(root,clean.Replace("\"DomEntered\":true","\"DomEntered\":\"https://fixture.invalid/private\""));}catch(FormatException){rejected=true;}
            if(!rejected || File.ReadAllText(Path.Combine(root,"last-read-probe.json")).Contains("fixture.invalid"))throw new Exception();
        });
        Console.WriteLine("Safe diagnostics: "+passed+" passed, "+failed+" failed");return failed==0?0:1;
    }
}
