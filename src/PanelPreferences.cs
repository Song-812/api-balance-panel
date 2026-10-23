using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace BalancePanel
{
    internal static class SourceCatalog
    {
        internal static string[] Ids(PanelKind kind) { return kind==PanelKind.Vpn ? new [] {"vpn"} : new [] {"deepseek","openai","gemini","xai","mimo","qwen","glm","kimi","minimax"}; }
        internal static string Name(string id) {
            switch(id) {
                case "deepseek": return "DeepSeek";
                case "openai": return "OpenAI";
                case "gemini": return "Gemini";
                case "xai": return "xAI";
                case "mimo": return "MiMo";
                case "qwen": return "Qwen";
                case "glm": return "GLM";
                case "kimi": return "Kimi";
                case "minimax": return "MiniMax";
                default: return "剩余流量";
            }
        }
    }

    // This file contains presentation choices only; account settings remain in the existing DPAPI store.
    internal sealed class PanelPreferences
    {
        public List<string> Order {get;set;}
        public List<string> Hidden {get;set;}
        public double Width {get;set;}
        public double Height {get;set;}
        public double? Left {get;set;}
        public double? Top {get;set;}
        public PanelPreferences() {Order=new List<string>(); Hidden=new List<string>(); Width=300; Height=250;}
        internal static PanelPreferences Load(string path,PanelKind kind) {
            PanelPreferences result=new PanelPreferences();
            try {
                if(File.Exists(path) && new FileInfo(path).Length<65536)
                    result=new JavaScriptSerializer().Deserialize<PanelPreferences>(File.ReadAllText(path,Encoding.UTF8)) ?? result;
            } catch(IOException) {} catch(ArgumentException) {} catch(InvalidOperationException) {} catch(UnauthorizedAccessException) {}
            string[] known=SourceCatalog.Ids(kind);
            result.Order=(result.Order??new List<string>()).Where(known.Contains).Distinct().Concat(known).Distinct().ToList();
            result.Hidden=(result.Hidden??new List<string>()).Where(known.Contains).Distinct().ToList();
            if(!Finite(result.Width) || result.Width<280 || result.Width>4096) result.Width=300;
            if(!Finite(result.Height) || result.Height<230 || result.Height>4096) result.Height=250;
            if(result.Left.HasValue && !Finite(result.Left.Value)) result.Left=null;
            if(result.Top.HasValue && !Finite(result.Top.Value)) result.Top=null;
            return result;
        }
        private static bool Finite(double number) {return !double.IsNaN(number) && !double.IsInfinity(number);}
        internal bool IsVisible(string id) {return !Hidden.Contains(id);}
        internal void SetVisible(string id,bool visible) {Hidden.Remove(id); if(!visible) Hidden.Add(id);}
        internal void SetVisibleOrder(IEnumerable<string> visible) {
            var queue=new Queue<string>(visible); var included=new HashSet<string>(queue);
            for(int i=0;i<Order.Count;i++) if(included.Contains(Order[i])) Order[i]=queue.Dequeue();
        }
        internal void Save(string path) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(this),new UTF8Encoding(false));
                if(File.Exists(path)) File.Replace(temporary,path,null); else File.Move(temporary,path);
            } finally {if(File.Exists(temporary)) File.Delete(temporary);}
        }
    }
}