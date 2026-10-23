using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
namespace BalancePanel
{
    internal static class SafeDiagnostics
    {
        internal static void Save(string directory,string json)
        {
            if(string.IsNullOrEmpty(json) || json.Length>2048)throw new FormatException("无效诊断");
            JavaScriptSerializer serializer=new JavaScriptSerializer();
            Dictionary<string,object> input;
            try{input=serializer.Deserialize<Dictionary<string,object>>(json);}catch{throw new FormatException("无效诊断");}
            string[] fields={"DomEntered","ExactCreditLabel","ContainsCreditLabel","LocalizedLabel","AlternativeLabel","HasFrames","HasShadowRoots","ChallengePage"};
            if(input==null || input.Count!=fields.Length)throw new FormatException("无效诊断");
            Dictionary<string,bool> output=new Dictionary<string,bool>();
            foreach(string field in fields){
                object value;
                if(!input.TryGetValue(field,out value) || !(value is bool))throw new FormatException("无效诊断");
                output.Add(field,(bool)value);
            }
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,"last-read-probe.json"),serializer.Serialize(output),new UTF8Encoding(false));
        }

    }
}
