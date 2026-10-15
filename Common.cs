using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

[assembly: System.Reflection.AssemblyTitle("AIVPN Windows")]
[assembly: System.Reflection.AssemblyCompany("AI WorkPort")]
[assembly: System.Reflection.AssemblyProduct("AIVPN")]
[assembly: System.Reflection.AssemblyVersion("0.1.0.14")]
[assembly: System.Reflection.AssemblyFileVersion("0.1.0.14")]
[assembly: System.Reflection.AssemblyInformationalVersion(Aivpn.Product.Version)]
namespace Aivpn {
public static class Product {
    public const string Version = "0.1.0-rc14";
    public const string Service = "AIVPNCandidate1";
    public const string Pipe = "AIVPNCandidate1.Local";
    public const string Api = "https://aivpn.103.6.169.231.sslip.io/api/v1";
    public const string Login = "https://aiworkport.com/vpn";
    public const string ExitIp = "103.6.169.231";
    public static string InstallDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AIVPN Candidate"); } }
    public static string DataDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AIVPN Candidate"); } }
    public static string UserDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIVPN Candidate"); } }
    public static bool Installed { get { return String.Equals(Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\'), InstallDir, StringComparison.OrdinalIgnoreCase); } }
}
public static class TrafficDisplay {
    public static long Delta(long current,long previous) { return current>=previous ? current-previous : 0; }
    public static string Rate(long bytes,double seconds) {
        if(bytes<=0 || seconds<=0)return "0 Kbps";
        double bitsPerSecond=bytes*8d/seconds;
        if(bitsPerSecond<1000000d)return (bitsPerSecond/1000d).ToString("0.#")+" Kbps";
        return (bitsPerSecond/1000000d).ToString("0.##")+" Mbps";
    }
    public static string Bytes(long bytes) {
        if(bytes<0)bytes=0;
        if(bytes<1024)return bytes+" B";
        if(bytes<1024L*1024)return (bytes/1024d).ToString("0.0")+" KB";
        if(bytes<1024L*1024*1024)return (bytes/(1024d*1024d)).ToString("0.0")+" MB";
        return (bytes/(1024d*1024d*1024d)).ToString("0.00")+" GB";
    }
}
public static class Json {
    public static string Write(object o) { return new JavaScriptSerializer { MaxJsonLength=8000000 }.Serialize(o); }
    public static object Read(string s) { return new JavaScriptSerializer { MaxJsonLength=8000000, RecursionLimit=24 }.DeserializeObject(s); }
    public static Dictionary<string,object> Obj(string s) { return (Dictionary<string,object>)Read(s); }
    public static Dictionary<string,object> Map(params object[] pairs) { var d=new Dictionary<string,object>(); for(int i=0;i<pairs.Length;i+=2)d.Add((string)pairs[i],pairs[i+1]); return d; }
    public static string Str(IDictionary<string,object> d,string k) { return d.ContainsKey(k) && d[k]!=null ? Convert.ToString(d[k]) : ""; }
    public static bool Bool(IDictionary<string,object> d,string k) { return d.ContainsKey(k) && d[k] is bool && (bool)d[k]; }
}
public static class Security {
    public static string Hash(string path) { using(var sha=SHA256.Create())using(var f=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(f)).Replace("-","").ToLowerInvariant(); }
    public static void PrivateDirectory(string path,bool system) {
        Directory.CreateDirectory(path);
        if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("E_PATH");
        var acl=new DirectorySecurity(); acl.SetAccessRuleProtection(true,false);
        // An elevated install must not leave the ordinary account as owner: ownership permits
        // rewriting the DACL even when the file permissions themselves are read-only.
        acl.SetOwner(system?new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null):WindowsIdentity.GetCurrent().User);
        var ids=new List<SecurityIdentifier>{new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null)};
        if(!system)ids.Add(WindowsIdentity.GetCurrent().User);
        foreach(var id in ids)acl.AddAccessRule(new FileSystemAccessRule(id,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        Directory.SetAccessControl(path,acl);
    }
    public static bool LoginAllowed(string url) {
        Uri u; return Uri.TryCreate(url,UriKind.Absolute,out u) && u.Scheme=="https" && u.Port==443 && u.UserInfo=="" &&
            new[]{"aiworkport.com","www.aiworkport.com","aivpn.103.6.169.231.sslip.io"}.Contains(u.DnsSafeHost);
    }
    public static string Assertion(string url) {
        if(!LoginAllowed(url))return null; var u=new Uri(url);
        if(u.Host!="aivpn.103.6.169.231.sslip.io" || u.AbsolutePath!="/sso")return null;
        var fields=u.Fragment.TrimStart('#').Split('&').Where(x=>x.StartsWith("assertion=",StringComparison.Ordinal)).ToArray();
        if(fields.Length!=1)return null;
        string value=Uri.UnescapeDataString(fields[0].Substring(10));
        return value.Length>0 && value.Length<=16000 ? value : null;
    }
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool QueryFullProcessImageName(IntPtr p,int flags,StringBuilder name,ref int length);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr p);
    [DllImport("kernel32.dll",SetLastError=true)] public static extern bool GetNamedPipeServerProcessId(IntPtr pipe,out uint pid);
    [DllImport("kernel32.dll",SetLastError=true)] public static extern bool GetNamedPipeClientProcessId(IntPtr pipe,out uint pid);
    public static bool IsProductProcess(uint pid,string file) {
        var p=OpenProcess(0x1000,false,pid); if(p==IntPtr.Zero)return false;
        try {int n=32768; var b=new StringBuilder(n); return QueryFullProcessImageName(p,0,b,ref n) && String.Equals(b.ToString(),Path.Combine(Product.InstallDir,file),StringComparison.OrdinalIgnoreCase);}
        finally {CloseHandle(p);}
    }
    [StructLayout(LayoutKind.Sequential)] struct ServiceStatus {public uint Type,State,Controls,Win32Exit,ServiceExit,Checkpoint,WaitHint,ProcessId,Flags;}
    [StructLayout(LayoutKind.Sequential)] struct ServiceConfig {public uint Type,Start,Error;public IntPtr Binary,Group;public uint Tag;public IntPtr Dependencies,Account,Display;}
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr OpenSCManager(string machine,string database,uint access);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr OpenService(IntPtr manager,string name,uint access);
    [DllImport("advapi32.dll",SetLastError=true)] static extern bool QueryServiceStatusEx(IntPtr service,int level,out ServiceStatus status,int size,out int needed);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool QueryServiceConfig(IntPtr service,IntPtr config,int size,out int needed);
    [DllImport("advapi32.dll")] static extern bool CloseServiceHandle(IntPtr handle);
    // SCM is the authority for this protected service's PID and binary. A normal user may query
    // SCM metadata but cannot OpenProcess even with QUERY_LIMITED_INFORMATION on some systems.
    public static bool IsServiceProcess(uint pid) {
        IntPtr manager=OpenSCManager(null,null,1);if(manager==IntPtr.Zero)return false;
        IntPtr service=IntPtr.Zero,buffer=IntPtr.Zero;
        try {
            service=OpenService(manager,Product.Service,5);if(service==IntPtr.Zero)return false;
            ServiceStatus status;int needed;
            if(!QueryServiceStatusEx(service,0,out status,Marshal.SizeOf(typeof(ServiceStatus)),out needed) || status.State!=4 || status.ProcessId!=pid || status.Type!=0x10)return false;
            QueryServiceConfig(service,IntPtr.Zero,0,out needed);if(needed<Marshal.SizeOf(typeof(ServiceConfig)) || needed>32768)return false;
            buffer=Marshal.AllocHGlobal(needed);if(!QueryServiceConfig(service,buffer,needed,out needed))return false;
            var config=(ServiceConfig)Marshal.PtrToStructure(buffer,typeof(ServiceConfig));
            return String.Equals(Marshal.PtrToStringUni(config.Binary).Trim('"'),Path.Combine(Product.InstallDir,"AIVPN.Service.exe"),StringComparison.OrdinalIgnoreCase) &&
                String.Equals(Marshal.PtrToStringUni(config.Account),"LocalSystem",StringComparison.OrdinalIgnoreCase);
        }finally {if(buffer!=IntPtr.Zero)Marshal.FreeHGlobal(buffer);if(service!=IntPtr.Zero)CloseServiceHandle(service);CloseServiceHandle(manager);}
    }
}
public sealed class Vault {
    readonly string path; Dictionary<string,object> data;
    public Vault(string directory) {Security.PrivateDirectory(directory,false); path=Path.Combine(directory,"session.bin");data=Json.Map();
        if(File.Exists(path)) {try{data=Json.Obj(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser)));}catch{throw new InvalidOperationException("E_STORE");}} }
    public string Get(string k) {return Json.Str(data,k);}
    public void Set(string k,object value) {data[k]=value;Save();}
    public void Remove(params string[] keys) {foreach(var k in keys)data.Remove(k);Save();}
    void Save() {var bytes=ProtectedData.Protect(Encoding.UTF8.GetBytes(Json.Write(data)),null,DataProtectionScope.CurrentUser);var tmp=path+".new";File.WriteAllBytes(tmp,bytes);if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}
}
public class ApiError:Exception {public readonly int Status;public ApiError(int status):base("E_API_"+status){Status=status;} }
public sealed class ApiClient:IDisposable {
    readonly Vault vault; readonly HttpClient http;
    public ApiClient(Vault v) {vault=v;http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseProxy=false}){Timeout=TimeSpan.FromSeconds(25)};http.MaxResponseContentBufferSize=2000000;}
    public async Task<string> Call(string path,object body=null,bool auth=true,bool retry=true) {
        if(!path.StartsWith("/") || path.Contains("..") || path.Contains("://"))throw new InvalidOperationException("E_API_PATH");
        using(var req=new HttpRequestMessage(body==null?HttpMethod.Get:HttpMethod.Post,Product.Api+path)) {
            if(auth)req.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",vault.Get("access"));
            if(body!=null)req.Content=new StringContent(Json.Write(body),Encoding.UTF8,"application/json");
            using(var response=await http.SendAsync(req)) {
                if((int)response.StatusCode==401 && auth && retry && vault.Get("refresh")!="") {
                    var tokens=Json.Obj(await Call("/auth/refresh",Json.Map("refresh_token",vault.Get("refresh")),false,false));SaveTokens(tokens);return await Call(path,body,true,false);
                }
                if(!response.IsSuccessStatusCode)throw new ApiError((int)response.StatusCode);
                return await response.Content.ReadAsStringAsync();
            }
        }
    }
    public void SaveTokens(Dictionary<string,object> tokens) {if(Json.Str(tokens,"access_token")=="" || Json.Str(tokens,"refresh_token")=="")throw new InvalidOperationException("E_LOGIN");vault.Set("access",tokens["access_token"]);vault.Set("refresh",tokens["refresh_token"]);}
    public async Task Login(string assertion) {
        SaveTokens(Json.Obj(await Call("/auth/sso",Json.Map("assertion",assertion),false,false)));
        var me=Json.Obj(await Call("/me"));string account=Json.Str(me,"email").ToLowerInvariant();if(account=="")throw new InvalidOperationException("E_LOGIN");
        if(vault.Get("owner")!=account)vault.Remove("device","pending_provision");vault.Set("owner",account);
    }
    public async Task<Dictionary<string,object>> Prepare() {
        var me=Json.Obj(await Call("/me"));if(Json.Str(me,"email").ToLowerInvariant()!=vault.Get("owner"))throw new InvalidOperationException("E_ACCOUNT");
        if(vault.Get("installation")=="")vault.Set("installation",Guid.NewGuid().ToString("N"));
        string name="AIVPN Windows "+vault.Get("installation").Substring(0,12);
        var list=((object[])Json.Read(await Call("/devices"))).Cast<Dictionary<string,object>>().ToArray();
        var matches=list.Where(x=>Json.Str(x,"name")==name && Json.Str(x,"platform")=="WINDOWS" && Json.Bool(x,"enabled")).ToArray();
        if(matches.Length>1)throw new InvalidOperationException("E_DEVICE_DUPLICATE");
        Dictionary<string,object> device=matches.FirstOrDefault();
        if(device==null) {
            if(vault.Get("device")!="")throw new InvalidOperationException("E_DEVICE_DISABLED");
            if(vault.Get("pending_provision")!="")throw new InvalidOperationException("E_DEVICE_PENDING");
            var nodes=((object[])Json.Read(await Call("/nodes"))).Cast<Dictionary<string,object>>();
            var node=nodes.FirstOrDefault(x=>!Json.Bool(x,"locked") && Json.Str(x,"status")=="AVAILABLE" && x.ContainsKey("amneziawg") && x["amneziawg"]!=null);
            if(node==null)throw new InvalidOperationException("E_NODE");
            vault.Set("pending_provision","1");
            try {device=Json.Obj(await Call("/devices/provision",Json.Map("name",name,"platform","WINDOWS","node_id",node["id"],"device_identifier",vault.Get("installation"),"routing_mode","global")));}
            catch(ApiError ex) {if(ex.Status>=400 && ex.Status<500)vault.Remove("pending_provision");throw;}
        }
        vault.Set("device",device["id"]);vault.Remove("pending_provision");
        string conf=await Call("/devices/"+Convert.ToInt32(device["id"])+"/desktop.conf?routing_mode=global");
        return Json.Map("id",device["id"],"profile",conf);
    }
    public void Dispose(){http.Dispose();}
}
public static class Profile {
    static readonly string[] Fields={"jc","jmin","jmax","s1","s2","s3","s4","h1","h2","h3","h4"};
    static string Key(string s) {byte[] b=Convert.FromBase64String(s);if(b.Length!=32 || Convert.ToBase64String(b)!=s)throw new InvalidOperationException("E_KEY");return s;}
    public static Dictionary<string,object> Parse(string text) {
        if(text==null || text.Length>16384)throw new InvalidOperationException("E_PROFILE");
        var sections=new Dictionary<string,Dictionary<string,string>>(StringComparer.OrdinalIgnoreCase);Dictionary<string,string> current=null;
        foreach(string raw in text.Split('\n')) {
            string line=raw.Trim();if(line=="" || line.StartsWith("#"))continue;
            if(line.StartsWith("[")) {if(line!="[Interface]" && line!="[Peer]")throw new InvalidOperationException("E_SECTION");string section=line.Trim('[',']');if(sections.ContainsKey(section))throw new InvalidOperationException("E_DUPLICATE");current=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);sections.Add(section,current);continue;}
            int at=line.IndexOf('=');if(current==null || at<1)throw new InvalidOperationException("E_PROFILE");string k=line.Substring(0,at).Trim().ToLowerInvariant();if(current.ContainsKey(k))throw new InvalidOperationException("E_DUPLICATE");current.Add(k,line.Substring(at+1).Trim());
        }
        if(sections.Count!=2)throw new InvalidOperationException("E_SECTION");var i=sections["Interface"];var p=sections["Peer"];
        if(i.Keys.Except(new[]{"privatekey","address","dns","mtu"}.Concat(Fields)).Any() || p.Keys.Except(new[]{"publickey","presharedkey","endpoint","allowedips","persistentkeepalive"}).Any())throw new InvalidOperationException("E_EXTENSION");
        if(p["endpoint"]!=Product.ExitIp+":585")throw new InvalidOperationException("E_ENDPOINT");
        if(!System.Text.RegularExpressions.Regex.IsMatch(i["address"],@"^10\.9\.0\.(?:[1-9]|[1-9][0-9]|1[0-9][0-9]|2[0-4][0-9]|25[0-4])/32$"))throw new InvalidOperationException("E_ADDRESS");
        var awg=Json.Map();foreach(var f in Fields){uint n;if(!i.ContainsKey(f)||!UInt32.TryParse(i[f],out n))throw new InvalidOperationException("E_AWG");awg[f]=f.StartsWith("h")?(object)n.ToString():n;}
        if(Convert.ToUInt32(awg["jc"])>128 || Convert.ToUInt32(awg["jmax"])>2048 || Convert.ToUInt32(awg["jmin"])>Convert.ToUInt32(awg["jmax"]) || new[]{"s1","s2","s3","s4"}.Any(x=>Convert.ToUInt32(awg[x])>1024))throw new InvalidOperationException("E_AWG");
        var proxy=Json.Map("name","AIVPN","type","wireguard","server",Product.ExitIp,"port",585,"ip",i["address"].Split('/')[0],"private-key",Key(i["privatekey"]),"public-key",Key(p["publickey"]),"udp",true,"mtu",1280,"allowed-ips",new[]{"0.0.0.0/0"},"persistent-keepalive",25,"amnezia-wg-option",awg);
        if(p.ContainsKey("presharedkey"))proxy["pre-shared-key"]=Key(p["presharedkey"]);return proxy;
    }
    public static Dictionary<string,object> Build(string profile,string[] domains,string[] networks,bool global,bool tun) {
        domains=domains.Select(x=>x.Trim()).Where(x=>x!="" && !x.StartsWith("#")).Distinct().ToArray();
        networks=networks.Select(x=>x.Trim()).Where(x=>x!="" && !x.StartsWith("#")).Distinct().ToArray();
        var proxy=Parse(profile);if(domains.Length==0 || networks.Length==0)throw new InvalidOperationException("E_RULES");
        var dns=Json.Map("enable",true,"ipv6",false,"enhanced-mode","fake-ip","fake-ip-range","198.18.0.1/16","fake-ip-filter",new[]{"*.lan","*.local"},"default-nameserver",new[]{"223.5.5.5"},"nameserver",new[]{"https://1.1.1.1/dns-query#AIVPN","https://1.0.0.1/dns-query#AIVPN"});
        if(!global)dns["nameserver-policy"]=Json.Map("rule-set:cn-domains",new[]{"https://223.5.5.5/dns-query#DIRECT"},"+.oaiusercontent.com",new[]{"https://223.5.5.5/dns-query#DIRECT"});
        // Keep the dedicated ZeRemote relay on the physical route even when AIVPN is enabled.
        // The Windows host route alone is insufficient because strict TUN routing can still
        // capture the process flow; this exact /32 must precede the VPN catch-all in every mode.
        var rules=new List<string>{"IP-CIDR6,::/0,REJECT,no-resolve","IP-CIDR,207.148.117.198/32,DIRECT,no-resolve"};
        if(!global)rules.AddRange(new[]{"DOMAIN-SUFFIX,oaiusercontent.com,DIRECT","IP-CIDR,127.0.0.0/8,DIRECT,no-resolve","IP-CIDR,10.0.0.0/8,DIRECT,no-resolve","IP-CIDR,172.16.0.0/12,DIRECT,no-resolve","IP-CIDR,192.168.0.0/16,DIRECT,no-resolve","DOMAIN-SUFFIX,lan,DIRECT","DOMAIN-SUFFIX,local,DIRECT","RULE-SET,cn-domains,DIRECT","RULE-SET,cn-ip,DIRECT,no-resolve"});rules.Add("MATCH,AIVPN");
        return Json.Map("mode","rule","log-level","silent","allow-lan",false,"mixed-port",0,"ipv6",true,"profile",Json.Map("store-selected",false,"store-fake-ip",false),"dns",dns,"proxies",new[]{proxy},"rule-providers",Json.Map("cn-domains",Json.Map("type","inline","behavior","domain","payload",domains),"cn-ip",Json.Map("type","inline","behavior","ipcidr","payload",networks)),"rules",rules.ToArray(),"tun",Json.Map("enable",tun,"device","AIVPN-Candidate","stack","gvisor","auto-route",true,"strict-route",true,"auto-detect-interface",true,"mtu",1280,"dns-hijack",new[]{"any:53","tcp://any:53"}));
    }
}
public static class Wire {
    const int Maximum=65536;
    static async Task ReadExact(Stream s,byte[] b,int count,CancellationToken token) {int off=0;while(off<count){int n=await s.ReadAsync(b,off,count-off,token);if(n==0)throw new EndOfStreamException();off+=n;}}
    public static async Task<Dictionary<string,object>> Read(Stream s,CancellationToken token) {var b=new byte[4];await ReadExact(s,b,4,token);int n=BitConverter.ToInt32(b,0);if(n<2 || n>Maximum)throw new InvalidOperationException("E_FRAME");b=new byte[n];await ReadExact(s,b,n,token);return Json.Obj(new UTF8Encoding(false,true).GetString(b));}
    public static async Task Write(Stream s,object obj,CancellationToken token) {var b=Encoding.UTF8.GetBytes(Json.Write(obj));if(b.Length>Maximum)throw new InvalidOperationException("E_FRAME");var head=BitConverter.GetBytes(b.Length);await s.WriteAsync(head,0,4,token);await s.WriteAsync(b,0,b.Length,token);await s.FlushAsync(token);}
    public static async Task<Dictionary<string,object>> Request(object obj) {
        using(var cts=new CancellationTokenSource(6000))using(var p=new NamedPipeClientStream(".",Product.Pipe,PipeDirection.InOut,PipeOptions.Asynchronous,TokenImpersonationLevel.Impersonation)) {
            await p.ConnectAsync(2500,cts.Token);uint pid;if(!Security.GetNamedPipeServerProcessId(p.SafePipeHandle.DangerousGetHandle(),out pid)||!Security.IsServiceProcess(pid))throw new InvalidOperationException("E_SERVICE_IDENTITY");
            await Write(p,obj,cts.Token);return await Read(p,cts.Token);
        }
    }
}
}


