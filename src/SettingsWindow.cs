using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace BalancePanel
{
    internal sealed class SettingsWindow:Window
    {
        private sealed class Draft
        {
            internal string Secret="",Team="",Region="cn",OriginalRegion="cn";
            internal bool Visible;
        }
        private readonly SecureSettings store;
        private readonly PanelPreferences preferences;
        private readonly Action<string> saved;
        private readonly Func<string,string,Task> login;
        private readonly Action savePreferences;
        private readonly Dictionary<string,Draft> drafts=new Dictionary<string,Draft>();
        private readonly string[] ids;
        private readonly ListBox providers=new ListBox();
        private readonly PasswordBox secret=new PasswordBox();
        private readonly TextBox team=new TextBox();
        private readonly ComboBox region=new ComboBox();
        private readonly CheckBox visible=new CheckBox {Content="显示在面板上",Margin=new Thickness(0,0,0,9),FontSize=11.5};
        private readonly StackPanel secretGroup=new StackPanel(),teamGroup=new StackPanel(),regionGroup=new StackPanel();
        private readonly TextBlock error=Theme.Text("",11,"Dim"),secretCaption=Theme.Text("",11,"Dim");
        private readonly Button loginButton=Theme.Button("打开登录窗口");
        private string current;
        internal bool WasSaved {get;private set;}

        internal SettingsWindow(PanelKind kind,SecureSettings settings,Action<string> onSaved,Func<string,string,Task> onLogin,
            PanelPreferences presentation=null,Action resetSize=null,Action persist=null)
        {
            store=settings;saved=onSaved;login=onLogin;ids=SourceCatalog.Ids(kind);
            preferences=presentation??new PanelPreferences {Order=ids.ToList()};savePreferences=persist;
            Theme.Window(this);Title=kind==PanelKind.Api?"设置账户":"设置来源"; Width=300;Height=250;MinWidth=300;MinHeight=250;ResizeMode=ResizeMode.NoResize;
            foreach(string id in ids) {
                var draft=new Draft {Visible=preferences.IsVisible(id)};
                try {if(id=="minimax" || id=="kimi") draft.Region=draft.OriginalRegion=store.Load(id).Region=="global"?"global":"cn";} catch {error.Text="原配置无法读取；原文件会保留。";}
                drafts.Add(id,draft);
            }
            var root=new Grid();root.RowDefinitions.Add(new RowDefinition {Height=new GridLength(38)});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            root.Children.Add(Theme.Header(this,Title,Theme.Icon("关闭","M 2 2 L 10 10 M 10 2 L 2 10",delegate {Close();})));
            var body=new Grid();body.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(80)});body.ColumnDefinitions.Add(new ColumnDefinition());Grid.SetRow(body,1);root.Children.Add(body);
            providers.Name="ProviderList";providers.Margin=new Thickness(5,4,5,0);providers.BorderThickness=new Thickness(0);providers.Background=System.Windows.Media.Brushes.Transparent;
            providers.SetResourceReference(Control.ForegroundProperty,"Ink");AutomationProperties.SetName(providers,"服务商");
            Theme.ProviderList(providers);
            foreach(string id in ids) providers.Items.Add(new ListBoxItem {Content=SourceCatalog.Name(id),Padding=new Thickness(6,6,6,6),FontSize=11.5});
            var side=new Border {BorderThickness=new Thickness(0,0,0.5,0),Child=providers};side.SetResourceReference(Border.BorderBrushProperty,"Line");body.Children.Add(side);
            var form=new StackPanel {Margin=new Thickness(10,7,10,4)};var scroller=new ScrollViewer {Content=form,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Theme.PanelScroll(scroller);Grid.SetColumn(scroller,1);body.Children.Add(scroller);
            visible.Name="ShowProvider";visible.SetResourceReference(Control.ForegroundProperty,"Ink");Theme.SlidingSwitch(visible);form.Children.Add(visible);
            secretGroup.Children.Add(secretCaption);
            secret.MinHeight=28;secret.Padding=new Thickness(6,4,6,4);secret.FontSize=12;secret.Margin=new Thickness(0,3,0,8);PaintInput(secret);secretGroup.Children.Add(secret);form.Children.Add(secretGroup);
            AutomationProperties.SetName(secret,kind==PanelKind.Vpn?"VPN 流量页面地址，留空保留原值":"API 密钥，留空保留原值");
            Label(teamGroup,"Team ID");team.MinHeight=28;team.FontSize=12;team.Padding=new Thickness(6,4,6,4);team.Margin=new Thickness(0,3,0,8);PaintInput(team);teamGroup.Children.Add(team);form.Children.Add(teamGroup);AutomationProperties.SetName(team,"xAI Team ID");
            Label(regionGroup,"账户站点");region.Items.Add("中国站 · CNY");region.Items.Add("国际站 · USD");region.MinHeight=28;region.FontSize=12;region.Margin=new Thickness(0,3,0,8);region.SetResourceReference(Control.ForegroundProperty,"Ink");regionGroup.Children.Add(region);form.Children.Add(regionGroup);AutomationProperties.SetName(region,"账户站点");
            Theme.SiteChoice(region);
            loginButton.HorizontalAlignment=HorizontalAlignment.Left;form.Children.Add(loginButton);
            loginButton.Click+=async delegate {
                if(!SaveAll()) return;string id=current,site=drafts[id].Region;Close();await login(id,site);
            };
            error.TextWrapping=TextWrapping.Wrap;error.Visibility=error.Text.Length==0?Visibility.Collapsed:Visibility.Visible;form.Children.Add(error);
            var footer=new Grid {Margin=new Thickness(10,6,10,10)};footer.ColumnDefinitions.Add(new ColumnDefinition());footer.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            var reset=CompactButton("恢复默认窗口大小");reset.Padding=new Thickness(2);reset.FontSize=11;reset.HorizontalAlignment=HorizontalAlignment.Left;reset.Click+=delegate {if(resetSize!=null) resetSize();};footer.Children.Add(reset);
            var actions=new StackPanel {Orientation=Orientation.Horizontal};var cancel=CompactButton("取消");cancel.Margin=new Thickness(0,0,6,0);cancel.IsCancel=true;cancel.Click+=delegate {Close();};actions.Children.Add(cancel);
            var save=CompactButton("保存");save.IsDefault=true;save.Click+=delegate {if(SaveAll()) Close();};actions.Children.Add(save);Grid.SetColumn(actions,1);footer.Children.Add(actions);Grid.SetRow(footer,2);root.Children.Add(footer);
            Content=Theme.Shell(root);providers.SelectionChanged+=delegate {SelectProvider();};providers.SelectedIndex=0;
            Closed+=delegate {secret.Clear();team.Clear();foreach(var draft in drafts.Values) {draft.Secret="";draft.Team="";}};
        }
        private static void Label(Panel panel,string label) {panel.Children.Add(Theme.Text(label,11,"Dim"));}
        private static Button CompactButton(string label) {var button=Theme.Button(label);button.MinHeight=28;button.Padding=new Thickness(8,3,8,3);return button;}
        private static void PaintInput(Control control) {control.SetResourceReference(Control.BackgroundProperty,"Input");control.SetResourceReference(Control.ForegroundProperty,"Ink");control.SetResourceReference(Control.BorderBrushProperty,"Line");}
        private void CaptureDraft()
        {
            if(current==null) return;Draft draft=drafts[current];draft.Secret=secret.Password;draft.Team=team.Text;draft.Visible=visible.IsChecked==true;draft.Region=region.SelectedIndex==1?"global":"cn";
        }
        private void SelectProvider()
        {
            CaptureDraft();current=ids[providers.SelectedIndex];Draft draft=drafts[current];secret.Password=draft.Secret;team.Text=draft.Team;visible.IsChecked=draft.Visible;region.SelectedIndex=draft.Region=="global"?1:0;
            bool key=current=="deepseek" || current=="xai" || current=="minimax" || current=="qwen" || current=="glm" || current=="kimi" || current=="vpn";
            secretGroup.Visibility=key?Visibility.Visible:Visibility.Collapsed;teamGroup.Visibility=(current=="xai" || current=="qwen")?Visibility.Visible:Visibility.Collapsed;regionGroup.Visibility=(current=="minimax" || current=="kimi")?Visibility.Visible:Visibility.Collapsed;
            loginButton.Visibility=current=="mimo" || current=="openai" || current=="gemini"?Visibility.Visible:Visibility.Collapsed;
            secretCaption.Text=current=="vpn"?"流量页面地址":current=="xai"?"Management Key":current=="qwen"?"AccessKey Secret（阿里云账户钥匙，不是百炼模型 Key）":"API 密钥";
            // Reuse Team textbox for Qwen AccessKey ID (empty = keep existing).
            if(teamGroup.Children.Count>0 && teamGroup.Children[0] is TextBlock) ((TextBlock)teamGroup.Children[0]).Text=current=="qwen"?"AccessKey ID":"Team ID";
            AutomationProperties.SetName(team,current=="qwen"?"阿里云 AccessKey ID，留空保留原值":"xAI Team ID");
            AutomationProperties.SetName(secret,current=="vpn"?"VPN 流量页面地址，留空保留原值":current=="qwen"?"AccessKey Secret，留空保留原值":"API 密钥，留空保留原值");
            error.Text="";error.Visibility=Visibility.Collapsed;
        }
        private bool SaveAll()
        {
            CaptureDraft();
            try {
                foreach(string id in ids) {
                    Draft draft=drafts[id];
                    bool regioned=id=="minimax" || id=="kimi";
                    bool changed=draft.Secret.Trim().Length>0 || draft.Team.Trim().Length>0 || (regioned && draft.Region!=draft.OriginalRegion);
                    if(!changed) continue;
                    AccountSettings account=store.Load(id);
                    if(draft.Secret.Trim().Length>0) {
                        if(id=="vpn") {Uri uri;if(!Uri.TryCreate(draft.Secret.Trim(),UriKind.Absolute,out uri) || (uri.Scheme!="http" && uri.Scheme!="https") || !string.IsNullOrEmpty(uri.UserInfo)) throw new ArgumentException(); account.VpnUrl=draft.Secret.Trim();}
                        else account.ApiKey=draft.Secret.Trim();
                    }
                    if(draft.Team.Trim().Length>0) {
                        if(id=="qwen") account.AccessKeyId=draft.Team.Trim();
                        else account.TeamId=draft.Team.Trim();
                    }
                    if(regioned) {account.Region=draft.Region;account.UseWebLogin=false;}
                    store.Save(id,account);draft.Secret="";draft.Team="";draft.OriginalRegion=draft.Region;saved(id);
                }
                foreach(string id in ids) preferences.SetVisible(id,drafts[id].Visible);
                if(savePreferences!=null) savePreferences();secret.Clear();team.Clear();WasSaved=true;return true;
            } catch(Exception exception) {error.Text=PanelWindow.SafeError(exception);error.Visibility=Visibility.Visible;return false;}
        }
    }
}
