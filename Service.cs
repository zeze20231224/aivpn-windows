using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aivpn {
public static class ConnectionReadiness {
    // A request timeout is not cancellation of the user's connection attempt.
    // Retry only timeouts; certificate, exit identity and HTTP failures keep their errors.
    public static async Task Verify(Func<CancellationToken,Task> probe,CancellationToken token,int delayMs=1500) {
        for(int attempt=0;attempt<3;attempt++) {
            token.ThrowIfCancellationRequested();
            try {await probe(token);return;}
            catch(OperationCanceledException) {
                token.ThrowIfCancellationRequested();
                if(attempt==2)throw new InvalidOperationException("E_NETWORK_TIMEOUT");
            }
            await Task.Delay(delayMs,token);
        }
    }
}
public static class TunStartup {
    public static async Task Wait(Func<bool> ready,Func<string> failure,CancellationToken token,int attempts=60,int delayMs=500){
        for(int i=0;i<attempts;i++){
            token.ThrowIfCancellationRequested();string code=failure();if(code!="")throw new InvalidOperationException(code);
            if(ready())return;
            await Task.Delay(delayMs,token);
        }
        throw new InvalidOperationException("E_TUN_READY_TIMEOUT");
    }
    public static string Classify(string line){
        if(line==null || line.IndexOf("Start TUN listening error",StringComparison.OrdinalIgnoreCase)<0)return "";
        string s=line.ToLowerInvariant();
        if(s.Contains("access is denied") || s.Contains("permission denied"))return "E_TUN_ACCESS";
        if(s.Contains("already exists") || s.Contains("in use"))return "E_TUN_EXISTS";
        if(s.Contains("route"))return "E_TUN_ROUTE";
        if(s.Contains("dns"))return "E_TUN_DNS";
        return "E_TUN_NATIVE";
    }
}
public sealed class KillJob:IDisposable {
    IntPtr job;
    [StructLayout(LayoutKind.Sequential)] struct Basic {public long ProcessTime,JobTime;public uint Flags;public UIntPtr Min,Max;public uint Active;public UIntPtr Affinity;public uint Priority,Scheduling;}
    [StructLayout(LayoutKind.Sequential)] struct IO {public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes;}
    [StructLayout(LayoutKind.Sequential)] struct Limits {public Basic Basic;public IO IO;public UIntPtr ProcessMemory,JobMemory,PeakProcess,PeakJob;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attr,string name);
    [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr h,int info,IntPtr value,uint size);
    [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
    public KillJob(Process p) {
        job=CreateJobObject(IntPtr.Zero,null);var limit=new Limits();limit.Basic.Flags=0x2000;int n=Marshal.SizeOf(limit);IntPtr b=Marshal.AllocHGlobal(n);
        try{Marshal.StructureToPtr(limit,b,false);if(job==IntPtr.Zero || !SetInformationJobObject(job,9,b,(uint)n) || !AssignProcessToJobObject(job,p.Handle)){Dispose();throw new InvalidOperationException("E_JOB");}}
        finally{Marshal.FreeHGlobal(b);}
    }
    public void Dispose(){if(job!=IntPtr.Zero){Security.CloseHandle(job);job=IntPtr.Zero;}}
}
public static class NetworkGuard {
    public static bool Conflict() {
        var adapters=new Dictionary<int,string>();foreach(var n in NetworkInterface.GetAllNetworkInterfaces()){
            try{adapters[n.GetIPProperties().GetIPv4Properties().Index]=n.Name+" "+n.Description;}catch{}
            try{adapters[n.GetIPProperties().GetIPv6Properties().Index]=n.Name+" "+n.Description;}catch{}
        }
        using(var query=new ManagementObjectSearcher(@"root\StandardCimv2","SELECT DestinationPrefix,InterfaceIndex FROM MSFT_NetRoute WHERE DestinationPrefix='0.0.0.0/0' OR DestinationPrefix='0.0.0.0/1' OR DestinationPrefix='128.0.0.0/1' OR DestinationPrefix='::/0' OR DestinationPrefix='::/1' OR DestinationPrefix='8000::/1'")) {
            query.Options.Timeout=TimeSpan.FromSeconds(5);
            using(var rows=query.Get())foreach(ManagementObject row in rows) {int idx=Convert.ToInt32(row["InterfaceIndex"]);string name=adapters.ContainsKey(idx)?adapters[idx]:"";
                if(IsConflictPrefix(Convert.ToString(row["DestinationPrefix"]),name))return true;
            }
        }return false;
    }
    public static bool IsConflictPrefix(string prefix,string adapter){
        if(new[]{"0.0.0.0/1","128.0.0.0/1","::/1","8000::/1"}.Contains(prefix))return true;
        return (prefix=="0.0.0.0/0" || prefix=="::/0") && System.Text.RegularExpressions.Regex.IsMatch(adapter,"wireguard|wintun|amnezia|clash|vpn|tunnel|tailscale",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
    public static bool IsConflict(string destination,string mask,string adapter) {
        if(mask=="128.0.0.0")return IsConflictPrefix(destination+"/1",adapter);
        return mask=="0.0.0.0" && IsConflictPrefix(destination+"/0",adapter);
    }
    public static bool OwnAdapter() {return NetworkInterface.GetAllNetworkInterfaces().Any(x=>x.Name=="AIVPN-Candidate" && x.OperationalStatus==OperationalStatus.Up);}
}
public sealed class Tunnel {
    readonly object gate=new object();Process core;KillJob job;string directory;string owner;uint ownerPid;DateTime lease;CancellationTokenSource run;
    string state="DISCONNECTED",error="",mode="auto";volatile string nativeFailure="";readonly string baseDir;
    public Tunnel(string baseDir){this.baseDir=baseDir;}
    public static void CleanStaleSessions(){
        Security.PrivateDirectory(Product.DataDir,true);
        foreach(string folder in Directory.GetDirectories(Product.DataDir)){
            if(!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(folder),"^session-[a-f0-9]{32}$") || (File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)continue;
            foreach(string name in new[]{"runtime.json","cache.db"}){string f=Path.Combine(folder,name);if(File.Exists(f) && (File.GetAttributes(f)&FileAttributes.ReparsePoint)==0)File.Delete(f);}
            if(Directory.GetFileSystemEntries(folder).Length==0)Directory.Delete(folder,false);
        }
    }
    public Dictionary<string,object> Handle(string sid,uint pid,Dictionary<string,object> req) {
        lock(gate) {
            string command=Json.Str(req,"command");bool owns=owner==sid && ownerPid==pid;
            if(owner!=null && !owns)return Json.Map("state","BUSY","error","E_OTHER_SESSION","mode","");
            if(command=="status"){if(owns)lease=DateTime.UtcNow;return Status();}
            if(command=="stop"){StopLocked();return Status();}
            if(command!="connect")throw new InvalidOperationException("E_COMMAND");
            if(run!=null)return Status();
            string profile=Json.Str(req,"profile");Profile.Parse(profile);
            if(NetworkGuard.Conflict()){error="E_OTHER_VPN";return Status();}
            owner=sid;ownerPid=pid;lease=DateTime.UtcNow;state="CONNECTING";error="";mode=Json.Bool(req,"global")?"global":"auto";
            run=new CancellationTokenSource();var token=run.Token;Task.Run(()=>Journey(profile,mode=="global",token));return Status();
        }
    }
    Dictionary<string,object> Status(){var status=Json.Map("state",state,"error",error,"mode",mode,"version",Product.Version);try{File.WriteAllText(Path.Combine(Product.DataDir,"connection-state.json"),Json.Write(status));}catch{}return status;}
    public void Watchdog(){lock(gate){if(owner!=null && (DateTime.UtcNow-lease).TotalSeconds>25){StopLocked();error="E_CLIENT_GONE";}if(core!=null && core.HasExited){StopLocked();error="E_CORE_EXIT";}}}
    void StopCore() {
        if(core!=null){try{if(!core.HasExited){core.Kill();core.WaitForExit(5000);}}catch{}finally{if(job!=null){job.Dispose();job=null;}core.Dispose();core=null;}}
        if(directory!=null){try{if(Directory.Exists(directory)){foreach(var f in Directory.GetFiles(directory)){if((File.GetAttributes(f)&FileAttributes.ReparsePoint)==0)File.Delete(f);}Directory.Delete(directory,false);}}catch{}directory=null;}
    }
    void StopLocked(){if(run!=null){run.Cancel();run.Dispose();run=null;}StopCore();owner=null;ownerPid=0;state="DISCONNECTED";error="";}
    public void Stop(){lock(gate)StopLocked();}
    void StartCore(string profile,bool global,CancellationToken token) {
        lock(gate) {
            token.ThrowIfCancellationRequested();StopCore();
            string exe=Path.Combine(baseDir,"mihomo.exe");if(Security.Hash(exe)!=BuildPins.Engine || Security.Hash(Path.Combine(baseDir,"wintun.dll"))!=BuildPins.Wintun)throw new InvalidOperationException("E_CORE_HASH");
            foreach(var pair in new[]{new[]{"cn-domains.list",BuildPins.Domains},new[]{"cn-ip.txt",BuildPins.Networks}})if(Security.Hash(Path.Combine(baseDir,pair[0]))!=pair[1])throw new InvalidOperationException("E_RULES_HASH");
            Security.PrivateDirectory(Product.DataDir,true);directory=Path.Combine(Product.DataDir,"session-"+Guid.NewGuid().ToString("N"));Security.PrivateDirectory(directory,true);
            var config=Profile.Build(profile,File.ReadAllLines(Path.Combine(baseDir,"cn-domains.list")),File.ReadAllLines(Path.Combine(baseDir,"cn-ip.txt")),global,true);
            config["log-level"]="warning";nativeFailure="";
            var path=Path.Combine(directory,"runtime.json");File.WriteAllText(path,Json.Write(config),new UTF8Encoding(false));
            var start=new ProcessStartInfo(exe,"-d \""+directory+"\" -f \""+path+"\""){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=baseDir,RedirectStandardOutput=true,RedirectStandardError=true};
            core=Process.Start(start);try{job=new KillJob(core);}catch{StopCore();throw;}
            var launched=core;
            DataReceivedEventHandler classify=(s,e)=>{string code=TunStartup.Classify(e.Data);if(code!="" && Object.ReferenceEquals(core,launched))nativeFailure=code;};
            core.OutputDataReceived+=classify;core.ErrorDataReceived+=classify;core.BeginOutputReadLine();core.BeginErrorReadLine();
            mode=global?"global":"auto";
        }
    }
    static async Task Probe(CancellationToken token) {
        if(!NetworkGuard.OwnAdapter())throw new InvalidOperationException("E_TUN");
        using(var http=new HttpClient(new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(12)}) {
            http.MaxResponseContentBufferSize=1500000;
            using(var result=await http.GetAsync("https://www.cloudflare.com/cdn-cgi/trace",token)){result.EnsureSuccessStatusCode();string content=await result.Content.ReadAsStringAsync();if(!content.Split('\n').Any(x=>x.Trim()=="ip="+Product.ExitIp))throw new InvalidOperationException("E_EXIT");}
            using(var result=await http.GetAsync("https://www.baidu.com/",token)){result.EnsureSuccessStatusCode();}
        }
    }
    async Task Journey(string profile,bool global,CancellationToken token) {
        try {
            bool fallback=global;
            while(true) {
                StartCore(profile,fallback,token);
                await TunStartup.Wait(NetworkGuard.OwnAdapter,()=>{lock(gate){return nativeFailure!=""?nativeFailure:core==null || core.HasExited?"E_CORE_EXIT":"";}},token);
                try{await ConnectionReadiness.Verify(Probe,token);}catch{token.ThrowIfCancellationRequested();if(!fallback){fallback=true;continue;}throw;}
                lock(gate){token.ThrowIfCancellationRequested();state="VERIFIED";error="";}
                while(true){await Task.Delay(20000,token);try{await ConnectionReadiness.Verify(Probe,token);}catch{token.ThrowIfCancellationRequested();if(!fallback){fallback=true;lock(gate){state="CONNECTING";error="";}break;}throw;}}
            }
        }catch(Exception ex){lock(gate){if(!token.IsCancellationRequested){string native=nativeFailure;StopLocked();error=ex is OperationCanceledException?"E_NETWORK_TIMEOUT":ex is InvalidOperationException && ex.Message.StartsWith("E_")?ex.Message:"E_NETWORK";
                    try{File.WriteAllText(Path.Combine(Product.DataDir,"last-startup-failure.json"),Json.Write(Json.Map("utc",DateTime.UtcNow.ToString("o"),"error",error,"native",native,"version",Product.Version)));}catch{}
                }}}
    }
}
public sealed class TunnelService:ServiceBase {
    readonly CancellationTokenSource stop=new CancellationTokenSource();Tunnel tunnel;System.Threading.Timer timer;
    public TunnelService(){ServiceName=Product.Service;CanStop=true;CanShutdown=true;AutoLog=false;}
    protected override void OnStart(string[] args) {
        if(!Product.Installed)throw new InvalidOperationException("Install required");
        Tunnel.CleanStaleSessions();tunnel=new Tunnel(AppDomain.CurrentDomain.BaseDirectory);timer=new System.Threading.Timer(s=>{try{tunnel.Watchdog();}catch{tunnel.Stop();}},null,1000,1000);Task.Run(()=>Listen());
    }
    async Task Listen() {
        while(!stop.IsCancellationRequested) {
            bool failed=false;
            try{
                var acl=new PipeSecurity();acl.SetAccessRuleProtection(true,false);
                acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid,null),PipeAccessRights.FullControl,AccessControlType.Deny));
                acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.InteractiveSid,null),PipeAccessRights.ReadWrite,AccessControlType.Allow));
                acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),PipeAccessRights.FullControl,AccessControlType.Allow));
                var pipe=new NamedPipeServerStream(Product.Pipe,PipeDirection.InOut,4,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,65536,65536,acl);
                try{await pipe.WaitForConnectionAsync(stop.Token);}catch{pipe.Dispose();throw;}
                Dispatch(()=>Client(pipe));
            }catch(OperationCanceledException){break;}catch{failed=true;}
            if(failed)await Task.Delay(1000);
        }
    }
    // Each request has a fixed deadline and handles its own failure. Keep the listener responsive.
    static void Dispatch(Func<Task> action){Task.Run(action);}
    async Task Client(NamedPipeServerStream pipe) {
        using(pipe)using(var timeout=new CancellationTokenSource(4500))using(timeout.Token.Register(()=>{try{pipe.Dispose();}catch{}})) {
            try{
                var req=await Wire.Read(pipe,timeout.Token);uint pid;
                if(!Security.GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out pid)||!Security.IsProductProcess(pid,"AIVPN.exe"))throw new InvalidOperationException("E_CLIENT_IDENTITY");
                string sid=null;pipe.RunAsClient(()=>{using(var identity=WindowsIdentity.GetCurrent(true)){if(identity!=null && !identity.IsAnonymous && !identity.IsSystem)sid=identity.User.Value;}});
                if(sid==null || Process.GetProcessById((int)pid).SessionId==0)throw new InvalidOperationException("E_CLIENT_IDENTITY");
                var answer=tunnel.Handle(sid,pid,req);await Wire.Write(pipe,answer,timeout.Token);
            }catch{}
        }
    }
    protected override void OnStop(){stop.Cancel();if(timer!=null)timer.Dispose();if(tunnel!=null)tunnel.Stop();}
    protected override void OnShutdown(){OnStop();}
    static void Main(){System.Net.ServicePointManager.SecurityProtocol=System.Net.SecurityProtocolType.Tls12;ServiceBase.Run(new TunnelService());}
}
}
