using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Aivpn {
public sealed class MainWindow:Form {
    readonly Color navy=Color.FromArgb(6,17,31),muted=Color.FromArgb(168,187,203),accent=Color.FromArgb(77,222,195);
    Label status,detail,traffic,trafficHint,monthlyUsage,account;Button action,login,logout;CheckBox compatibility;TableLayoutPanel layout;WebView2 browser;Panel loginPanel;
    Vault vault;ApiClient api;System.Windows.Forms.Timer timer;System.Threading.Timer heartbeatTimer;int heartbeatInFlight;bool busy,polling,active,closing,loginPending,exchanging;string device="";long started;string currentState="DISCONNECTED";bool proof;DateTime lastProof;
    readonly bool demo;int loginGeneration,connectionGeneration;long trafficBaseRx=-1,trafficBaseTx=-1,lastTrafficRx=-1,lastTrafficTx=-1;DateTime lastTrafficAt=DateTime.MinValue,lastMonthlyUsageAt=DateTime.MinValue;bool monthlyUsagePolling;
    public MainWindow(bool demo=false) {
        this.demo=demo;Text="AIVPN · Windows 测试版";ClientSize=new Size(500,760);MinimumSize=new Size(480,780);StartPosition=FormStartPosition.CenterScreen;BackColor=navy;ForeColor=Color.White;Font=new Font("Microsoft YaHei UI",10);AutoScaleMode=AutoScaleMode.Dpi;
        using(var stream=typeof(MainWindow).Assembly.GetManifestResourceStream("aivpn.ico"))if(stream!=null)Icon=new Icon(stream);
        layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,Padding=new Padding(32,22,32,20),AutoScroll=true};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));Controls.Add(layout);
        var header=new Panel{Dock=DockStyle.Fill};header.Controls.Add(new BrandMark{Size=new Size(42,42),Location=new Point(0,8)});
        var name=Label("AIVPN",20,Color.White);name.Dock=DockStyle.None;name.SetBounds(54,4,180,48);header.Controls.Add(name);
        var badge=Label("WINDOWS",8,muted);badge.Dock=DockStyle.Right;badge.Width=90;badge.TextAlign=ContentAlignment.MiddleRight;header.Controls.Add(badge);Add(header,64);
        Add(Label("国内直连 · 海外加密连接",10,muted),30);
        var mark=new BrandMark{Dock=DockStyle.Fill,Margin=new Padding(0,16,0,12)};Add(mark,128);
        status=Label("未连接",25,Color.White);status.TextAlign=ContentAlignment.MiddleCenter;status.TextChanged+=(s,e)=>status.ForeColor=status.Text=="已连接"?accent:Color.White;Add(status,52);
        detail=Label("登录后，点击连接即可使用。",10,muted);detail.TextAlign=ContentAlignment.MiddleCenter;detail.AutoEllipsis=false;Add(detail,64);
        monthlyUsage=Label("本月账号累计 VPN 流量：读取中…",10,accent);monthlyUsage.TextAlign=ContentAlignment.MiddleCenter;monthlyUsage.Visible=false;Add(monthlyUsage,36);
        traffic=Label("实时速率  ↓ 0 Kbps   ↑ 0 Kbps\n本次 VPN 流量  ↓ 0 B   ↑ 0 B",11,accent);traffic.TextAlign=ContentAlignment.MiddleCenter;traffic.Visible=false;Add(traffic,58);
        trafficHint=Label("仅统计经过 VPN 的流量；空闲或国内直连时速率可为 0。",8,muted);trafficHint.TextAlign=ContentAlignment.MiddleCenter;trafficHint.Visible=false;Add(trafficHint,30);
        login=Button("登录并开始使用",true);login.Click+=async(s,e)=>await OpenLogin();
        action=Button("连接",true);action.Click+=async(s,e)=>await ConnectClick();action.Visible=false;
        var actions=new Panel{Dock=DockStyle.Fill};actions.Controls.Add(login);actions.Controls.Add(action);Add(actions,60);
        account=Label("使用现有 AI WorkPort 账号",9,muted);account.TextAlign=ContentAlignment.MiddleCenter;Add(account,36);
        var settings=new Panel{Dock=DockStyle.Fill,BackColor=Color.FromArgb(14,31,46),Padding=new Padding(16,8,16,8),Margin=new Padding(0,12,0,8)};
        compatibility=new ReadableCheckBox{Text="兼容模式",ForeColor=Color.White,Dock=DockStyle.Top,Height=32,AccessibleName="兼容模式：全部流量通过 VPN"};settings.Controls.Add(compatibility);
        var hint=Label("自动模式下，ChatGPT 附件上传直连；兼容模式下全部流量通过 VPN。",9,muted);hint.Dock=DockStyle.Bottom;hint.Height=40;settings.Controls.Add(hint);Add(settings,106);
        Add(Label("默认自动分流 · 连接异常时自动尝试兼容模式",9,muted),32);
        var footer=new Panel{Dock=DockStyle.Fill};logout=Button("退出账号",false);logout.Dock=DockStyle.Right;logout.Width=86;logout.Click+=async(s,e)=>await Logout();footer.Controls.Add(logout);
        var license=new LinkLabel{Text="关于与开源许可",LinkColor=muted,ActiveLinkColor=accent,Dock=DockStyle.Left,Width=155,TextAlign=ContentAlignment.MiddleLeft,Font=new Font("Microsoft YaHei UI",9)};license.LinkClicked+=(s,e)=>MessageBox.Show(this,"内置 Mihomo（GPL-3.0）与 Wintun；完整源码及第三方许可随测试包提供。\n\n独立测试版，长期稳定性仍待验证。\n\n账号令牌使用 Windows 当前用户加密保护。", "AIVPN 测试说明");footer.Controls.Add(license);Add(footer,42);
        Add(Label("Windows x64  /  "+Product.Version+"  /  测试版",8,muted),28);
        if(!demo){vault=new Vault(Product.UserDir);api=new ApiClient(vault);RefreshAccount();timer=new System.Windows.Forms.Timer{Interval=2000};timer.Tick+=async(s,e)=>await Poll();timer.Start();heartbeatTimer=new System.Threading.Timer(_=>Heartbeat(),null,2000,5000);FormClosed+=(s,e)=>{if(heartbeatTimer!=null)heartbeatTimer.Dispose();};}
        FormClosing+=async(s,e)=>{if(busy || exchanging){e.Cancel=true;MessageBox.Show(this,"正在完成当前请求，请稍候再关闭。","AIVPN");return;}if(!closing && active){e.Cancel=true;if(MessageBox.Show(this,"关闭 AIVPN 将断开 VPN，是否继续？","断开并退出",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;closing=true;try{await Wire.Request(Json.Map("command","stop"));}catch{}Close();}};
        FormClosed+=(s,e)=>{loginGeneration++;loginPending=false;if(timer!=null)timer.Dispose();if(browser!=null)browser.Dispose();if(api!=null)api.Dispose();};
    }
    Label Label(string text,float size,Color color){return new Label{Text=text,Font=new Font("Microsoft YaHei UI",size),ForeColor=color,AutoSize=false,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true};}
    Button Button(string text,bool primary){var b=new ReadableButton{Text=text,Dock=DockStyle.Fill,Height=44,Margin=new Padding(0,4,0,4),FlatStyle=FlatStyle.Flat,BackColor=primary?accent:navy,ForeColor=primary?navy:Color.White,Cursor=Cursors.Hand};b.FlatAppearance.BorderSize=primary?0:1;b.FlatAppearance.BorderColor=Color.FromArgb(48,71,88);b.FlatAppearance.MouseOverBackColor=primary?Color.FromArgb(114,235,212):Color.FromArgb(20,41,58);b.FlatAppearance.MouseDownBackColor=primary?Color.FromArgb(51,188,164):Color.FromArgb(31,57,75);b.Font=new Font("Microsoft YaHei UI",primary?12:9,primary?FontStyle.Bold:FontStyle.Regular);return b;}
    void Add(Control c,int height){int r=layout.RowCount++;layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));layout.Controls.Add(c,0,r);}
    void RefreshAccount(){bool signed=vault.Get("refresh")!="";login.Visible=!signed;action.Visible=signed;monthlyUsage.Visible=signed;logout.Enabled=signed && !active && !busy;account.Text=signed?"AI WorkPort 账号已登录":"使用现有 AI WorkPort 账号";compatibility.Enabled=!active && !busy;}
    void StartMonthlyUsageRefresh(){RefreshMonthlyUsage().ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
    async Task RefreshMonthlyUsage(){if(demo || vault.Get("refresh")=="" || monthlyUsagePolling || (DateTime.UtcNow-lastMonthlyUsageAt).TotalSeconds<60)return;monthlyUsagePolling=true;lastMonthlyUsageAt=DateTime.UtcNow;try{var usage=Json.Obj(await api.Call("/usage"));long bytes=usage.ContainsKey("month_bytes")?Convert.ToInt64(usage["month_bytes"]):Convert.ToInt64(usage.ContainsKey("period_bytes")?usage["period_bytes"]:0);monthlyUsage.Text="本月账号累计 VPN 流量（所有设备）："+TrafficDisplay.Bytes(bytes);}catch{monthlyUsage.Text="本月账号累计 VPN 流量：暂时无法读取";}finally{monthlyUsagePolling=false;}}
    public static string Friendly(string code){
        switch(code){case "E_OTHER_VPN":return "检测到其他隧道的全局路由仍在生效，切换尚未完成。";
        case "E_NETWORK_TIMEOUT":return "网络响应超时，自动重试后仍未成功，请稍后连接。";
        case "E_SERVICE_IDENTITY":return "无法验证本机连接服务，请修复安装后重试。";
        case "E_OTHER_SESSION":return "另一 Windows 会话正在使用 AIVPN。";
        case "E_DEVICE_PENDING":return "设备创建结果待确认，请稍后重试；未重复创建设备。";
        case "E_DEVICE_DISABLED":return "本机设备已停用，请在账号设备管理中处理。";
        case "E_DEVICE_DUPLICATE":return "发现重复设备，请联系管理员核对。";
        case "E_API_409":return "设备数量已达套餐上限，请先在网站管理设备。";
        case "E_API_403":return "账号或套餐当前无法使用 VPN，请在网站检查。";
        case "E_API_401":return "登录已过期，请退出账号后重新登录。";
        case "E_CORE_HASH":case "E_RULES_HASH":return "安装文件校验失败，请重新安装官方测试包。";
        case "E_CLIENT_GONE":return "控制窗口响应超时，连接已自动断开。";
        default:return "连接未完成，请检查网络后重试。";}
    }
    async Task OpenLogin(){
        if(active || busy || loginPending)return;
        loginPending=true;exchanging=false;int generation=++loginGeneration;login.Enabled=false;status.Text="正在打开安全登录…";
        loginPanel=new Panel{Dock=DockStyle.Fill,BackColor=navy};var back=Button("返回 AIVPN",false);back.Dock=DockStyle.Top;back.Height=44;back.Click+=(s,e)=>{if(!exchanging)CloseLogin();};
        browser=new WebView2{Dock=DockStyle.Fill,DefaultBackgroundColor=navy};loginPanel.Controls.Add(browser);loginPanel.Controls.Add(back);Controls.Add(loginPanel);loginPanel.BringToFront();
        try{
            string folder=Path.Combine(Product.UserDir,"LoginBrowser");Security.PrivateDirectory(folder,false);
            var env=await CoreWebView2Environment.CreateAsync(null,folder);if(!loginPending || generation!=loginGeneration)return;await browser.EnsureCoreWebView2Async(env);if(!loginPending || generation!=loginGeneration)return;
            var core=browser.CoreWebView2;core.Settings.AreHostObjectsAllowed=false;core.Settings.IsWebMessageEnabled=false;core.Settings.AreDevToolsEnabled=false;core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.IsPasswordAutosaveEnabled=false;core.Settings.IsGeneralAutofillEnabled=false;core.Settings.IsStatusBarEnabled=false;
            if(vault.Get("clear_browser")=="1"){await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);vault.Remove("clear_browser");}
            core.PermissionRequested+=(s,e)=>e.State=CoreWebView2PermissionState.Deny;core.DownloadStarting+=(s,e)=>e.Cancel=true;
            core.NewWindowRequested+=(s,e)=>{e.Handled=true;};
            core.ServerCertificateErrorDetected+=(s,e)=>e.Action=CoreWebView2ServerCertificateErrorAction.Cancel;
            core.FrameNavigationStarting+=(s,e)=>{if(!Security.LoginAllowed(e.Uri))e.Cancel=true;};
            core.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
            core.WebResourceRequested+=(s,e)=>{if(!Security.LoginAllowed(e.Request.Uri))e.Response=env.CreateWebResourceResponse(new MemoryStream(),403,"Blocked","");};
            core.NavigationStarting+=async(s,e)=>{
                string assertion=Security.Assertion(e.Uri);
                if(assertion!=null){e.Cancel=true;if(exchanging || !loginPending || generation!=loginGeneration)return;exchanging=true;back.Enabled=false;
                    try{await api.Login(assertion);CloseLogin();status.Text="登录成功";detail.Text="点击连接即可使用。";RefreshAccount();}
                    catch{vault.Remove("access","refresh");CloseLogin();status.Text="登录未完成";detail.Text="请检查网络后重新登录。";RefreshAccount();}return;
                }
                if(!Security.LoginAllowed(e.Uri)){e.Cancel=true;detail.Text="已阻止非官方登录跳转。";}
            };
            core.Navigate(Product.Login);
        }catch{if(generation!=loginGeneration)return;CloseLogin();status.Text="无法打开登录";detail.Text="请确认已安装 Microsoft Edge WebView2 Runtime，再重试。";}
    }
    void CloseLogin(){loginGeneration++;loginPending=false;exchanging=false;if(browser!=null){browser.Dispose();browser=null;}if(loginPanel!=null){Controls.Remove(loginPanel);loginPanel.Dispose();loginPanel=null;}login.Enabled=true;}
    async Task Logout(){if(active || busy)return;vault.Remove("access","refresh","owner","device","pending_provision");
        // Clear this application's own browser profile before the next login, never the user's browser.
        vault.Set("clear_browser","1");lastMonthlyUsageAt=DateTime.MinValue;monthlyUsage.Text="本月账号累计 VPN 流量：读取中…";RefreshAccount();status.Text="已退出账号";detail.Text="下次连接前请重新登录。";await Task.FromResult(0);
    }
    async Task ConnectClick(){
        if(busy)return;connectionGeneration++;busy=true;action.Enabled=false;RefreshAccount();
        try{
            if(active){await Wire.Request(Json.Map("command","stop"));active=false;proof=false;ResetTraffic();currentState="DISCONNECTED";status.Text="已断开";detail.Text="需要使用时，再点击连接。";action.Text="连接";return;}
            RecordConnectionStage("service-check","");
            if(!Product.Installed)throw new InvalidOperationException("E_INSTALL");
            var preflight=await Wire.Request(Json.Map("command","status"));if(Json.Str(preflight,"state")=="BUSY")throw new InvalidOperationException("E_OTHER_SESSION");
            if(await Task.Run(()=>NetworkGuard.Conflict()))throw new InvalidOperationException("E_OTHER_VPN");
            status.Text="正在获取连接信息…";detail.Text="自动检查账号和本机设备。";
            RecordConnectionStage("account-profile","");
            var prepared=await api.Prepare();Profile.Parse(Json.Str(prepared,"profile"));device=Convert.ToInt32(prepared["id"]).ToString();
            started=(long)(DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds-2;proof=false;lastProof=DateTime.MinValue;ResetTraffic();
            RecordConnectionStage("service-connect","");
            var result=await Wire.Request(Json.Map("command","connect","profile",prepared["profile"],"global",compatibility.Checked));
            string error=Json.Str(result,"error");if(error!="")throw new InvalidOperationException(error);
            active=true;status.Text="正在连接…";detail.Text="正在验证隧道与实际网络。";action.Text="断开";
        }catch(Exception ex){string code=ex.Message.StartsWith("E_")?ex.Message:"E_SERVICE_OR_NETWORK";RecordConnectionStage("connect-failed",code);status.Text="未连接";detail.Text=(code=="E_INSTALL"?"请先运行安装程序，然后从开始菜单打开 AIVPN。":code=="E_SERVICE_OR_NETWORK"?"连接服务未就绪或网络异常，请确认已完成安装。":Friendly(code))+"（"+code+"）";if(code=="E_API_401")vault.Remove("access","refresh");}
        finally{busy=false;action.Enabled=true;RefreshAccount();}
    }
    void RecordConnectionStage(string stage,string code){try{
        // Bounded local metadata only. Never persist exception text, profiles or tokens.
        string safe=System.Text.RegularExpressions.Regex.IsMatch(code,"^E_[A-Z0-9_]{1,60}$")?code:"";
        string record=Json.Write(Json.Map("utc",DateTime.UtcNow.ToString("o"),"stage",stage,"error",safe,"version",Product.Version));
        File.WriteAllText(Path.Combine(Product.UserDir,"connection-stage.json"),record);
        // Keep the last failure after a successful retry, without account or network contents.
        if(safe!="")File.WriteAllText(Path.Combine(Product.UserDir,"last-connection-failure.json"),record);
    }catch{}}
    void ResetTraffic(){trafficBaseRx=trafficBaseTx=lastTrafficRx=lastTrafficTx=-1;lastTrafficAt=DateTime.MinValue;traffic.Text="实时速率  ↓ 0 Kbps   ↑ 0 Kbps\n本次 VPN 流量  ↓ 0 B   ↑ 0 B";traffic.Visible=false;trafficHint.Visible=false;}
    void UpdateTraffic(IDictionary<string,object> connection){
        long rx=Convert.ToInt64(connection["rx_bytes"]),tx=Convert.ToInt64(connection["tx_bytes"]);DateTime now=DateTime.UtcNow;
        if(trafficBaseRx<0 || rx<trafficBaseRx || tx<trafficBaseTx){trafficBaseRx=rx;trafficBaseTx=tx;lastTrafficRx=rx;lastTrafficTx=tx;lastTrafficAt=now;}
        double seconds=(now-lastTrafficAt).TotalSeconds;long down=TrafficDisplay.Delta(tx,lastTrafficTx),up=TrafficDisplay.Delta(rx,lastTrafficRx);
        traffic.Text="实时速率  ↓ "+TrafficDisplay.Rate(down,seconds)+"   ↑ "+TrafficDisplay.Rate(up,seconds)+"\n本次 VPN 流量  ↓ "+TrafficDisplay.Bytes(TrafficDisplay.Delta(tx,trafficBaseTx))+"   ↑ "+TrafficDisplay.Bytes(TrafficDisplay.Delta(rx,trafficBaseRx));
        traffic.Visible=trafficHint.Visible=true;lastTrafficRx=rx;lastTrafficTx=tx;lastTrafficAt=now;
    }
    async Task Poll(){
        if(polling || busy || loginPending || !Product.Installed)return;polling=true;bool failed=false;int generation=connectionGeneration;
        try{
            StartMonthlyUsageRefresh();
            var result=await Wire.Request(Json.Map("command","status"));if(generation!=connectionGeneration || busy)return;string state=Json.Str(result,"state"),error=Json.Str(result,"error");
            if(state!="DISCONNECTED" || error!="")RecordConnectionStage(state,error);
            if(state=="BUSY"){active=false;status.Text="暂时无法连接";detail.Text=Friendly("E_OTHER_SESSION");return;}
            active=state=="CONNECTING" || state=="VERIFIED";action.Text=active?"断开":"连接";
            if(state=="CONNECTING"){proof=false;status.Text="正在连接…";detail.Text=Json.Str(result,"mode")=="global"?"兼容模式 · 正在验证网络":"自动分流 · 正在验证网络";}
            else if(state=="VERIFIED"){
                Dictionary<string,object> connection=null;
                if(device=="") {await Wire.Request(Json.Map("command","stop"));throw new InvalidOperationException("E_DEVICE");}
                if(!proof || (DateTime.UtcNow-lastProof).TotalSeconds>45){
                    try{connection=Json.Obj(await api.Call("/devices/"+device+"/connection?after="+started));}
                    catch(Exception checkError){
                        string code=checkError.Message.StartsWith("E_API_")?checkError.Message:"E_STATUS_QUERY";
                        bool transient=checkError is System.Net.Http.HttpRequestException || checkError is TaskCanceledException || code=="E_API_408" || code=="E_API_429" || code.StartsWith("E_API_5");
                        if(proof && transient){RecordConnectionStage("status-query-retry",code);status.Text="连接状态确认中";detail.Text="状态查询暂时失败，保留当前隧道并重试。";return;}
                        throw;
                    }
                    if(generation!=connectionGeneration || busy)return;
                    if(!Json.Bool(connection,"connected") || Convert.ToInt64(connection["rx_bytes"])<=0 || Convert.ToInt64(connection["tx_bytes"])<=0){await Wire.Request(Json.Map("command","stop"));throw new InvalidOperationException("E_HANDSHAKE");}
                    proof=true;lastProof=DateTime.UtcNow;
                }
                if(connection==null){try{connection=Json.Obj(await api.Call("/devices/"+device+"/connection?after="+started));}catch{}}
                if(connection!=null && Json.Bool(connection,"connected"))UpdateTraffic(connection);
                status.Text="已连接";detail.Text=Json.Str(result,"mode")=="global"?"兼容模式 · 全部通过 VPN":"自动分流 · 附件上传直连，其余按规则";
            }else if(error!=""){status.Text="连接已断开";detail.Text=Friendly(error)+"（"+error+"）";proof=false;ResetTraffic();}
            else if(currentState!="DISCONNECTED"){status.Text="已断开";detail.Text="需要使用时，再点击连接。";proof=false;ResetTraffic();}
            currentState=state;RefreshAccount();
        }catch(Exception pollError){failed=generation==connectionGeneration && !busy;if(failed)RecordConnectionStage("poll-failed",pollError.Message.StartsWith("E_")?pollError.Message:"E_STATUS_QUERY");}
        finally{if(!failed)polling=false;}
        if(failed){if(active){try{await Wire.Request(Json.Map("command","stop"));}catch{}active=false;proof=false;ResetTraffic();action.Text="连接";status.Text="连接校验中断";detail.Text="无法确认隧道状态，已请求断开；服务失联时会自动清理。";RefreshAccount();}polling=false;}
    }
    void Heartbeat(){
        if(Interlocked.Exchange(ref heartbeatInFlight,1)!=0)return;
        Task.Run(async()=>{try{await Wire.Request(Json.Map("command","status"));}catch{}finally{Interlocked.Exchange(ref heartbeatInFlight,0);}});
    }
    public void PreviewState(string state){
        if(!demo)return;
        if(state=="connected"){login.Visible=false;action.Visible=true;action.Text="断开";status.Text="已连接";status.ForeColor=accent;detail.Text="自动分流 · 附件上传直连，其余按规则";traffic.Text="实时速率  ↓ 6.4 Mbps   ↑ 820 Kbps\n本次 VPN 流量  ↓ 128.6 MB   ↑ 18.4 MB";traffic.Visible=trafficHint.Visible=true;account.Text="AI WorkPort 账号已登录";compatibility.Enabled=false;logout.Enabled=false;}
        if(state=="error"){login.Visible=false;action.Visible=true;status.Text="未连接";detail.Text=Friendly("E_OTHER_VPN")+"（E_OTHER_VPN）";account.Text="AI WorkPort 账号已登录";}
    }
    public void Snapshot(string output){Show();Application.DoEvents();using(var b=new Bitmap(Width,Height)){DrawToBitmap(b,new Rectangle(0,0,Width,Height));b.Save(output);}Close();}
    [STAThread] static void Main(string[] args){
        System.Net.ServicePointManager.SecurityProtocol=System.Net.SecurityProtocolType.Tls12;Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length==2 && args[0]=="--ui-snapshot"){using(var f=new MainWindow(true))f.Snapshot(args[1]);return;}
        if(args.Length==3 && args[0]=="--ui-snapshot"){using(var f=new MainWindow(true)){f.PreviewState(args[2]);f.Snapshot(args[1]);}return;}
        bool connectOnOpen=args.Length==1 && args[0]=="--connect";
        if(args.Length!=0 && !connectOnOpen){MessageBox.Show("不支持的启动参数。","AIVPN");return;}
        bool created;using(var mutex=new Mutex(true,"Local\\AIVPNCandidate.UI",out created)){
            if(!created){MessageBox.Show("AIVPN 已在当前会话中运行。","AIVPN");return;}
            if(new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)){MessageBox.Show("请正常打开 AIVPN，不要选择“以管理员身份运行”。只有安装程序需要管理员授权。","AIVPN");return;}
            try{using(var window=new MainWindow()){
                if(connectOnOpen)window.Shown+=async(s,e)=>{
                    // Explicit one-shot request uses the same authenticated UI/service path.
                    // It never bypasses route conflict, service identity or exit verification.
                    if(window.vault.Get("access")!="")await window.ConnectClick();
                };
                Application.Run(window);
            }}catch{MessageBox.Show("无法读取本机安全存储或初始化客户端，请联系支持。未输出账号或密钥。","AIVPN");}
        }
    }
}
}

