using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace Aivpn {
public static class Tests {
    static int passed;
    static void Test(string name,Action action){try{action();Console.WriteLine("PASS "+name);passed++;}catch(Exception ex){throw new Exception("FAIL "+name+" ("+ex.GetType().Name+")");}}
    static void Check(bool b){if(!b)throw new Exception("Assertion");}
    static void Reject(Action action){bool rejected=false;try{action();}catch{rejected=true;}Check(rejected);}
    public static string Sample(){return "[Interface]\nPrivateKey = "+Convert.ToBase64String(Enumerable.Range(1,32).Select(x=>(byte)x).ToArray())+"\nAddress = 10.9.0.100/32\nDNS = 223.5.5.5\nMTU = 1280\nJc = 4\nJmin = 64\nJmax = 96\nS1 = 15\nS2 = 20\nS3 = 10\nS4 = 8\nH1 = 1738249911\nH2 = 2847190362\nH3 = 3956137823\nH4 = 947261584\n[Peer]\nPublicKey = "+Convert.ToBase64String(Enumerable.Range(33,32).Select(x=>(byte)x).ToArray())+"\nEndpoint = 103.6.169.231:585\nAllowedIPs = 0.0.0.0/0\nPersistentKeepalive = 25\n";}
    static int Main(string[] args){try{
        if(args.Length>0 && args[0]=="--args"){Console.WriteLine(Json.Write(args.Skip(1).ToArray()));return 0;}
        Test("delayed adapter becomes ready without restarting core",()=>{int checks=0;TunStartup.Wait(()=>++checks==12,()=>"",CancellationToken.None,20,0).GetAwaiter().GetResult();Check(checks==12);});
        Test("native TUN error fails before waiting",()=>{int checks=0;string error="";try{TunStartup.Wait(()=>{checks++;return false;},()=>"E_TUN_ACCESS",CancellationToken.None,20,0).GetAwaiter().GetResult();}catch(InvalidOperationException e){error=e.Message;}Check(error=="E_TUN_ACCESS" && checks==0);});
        Test("missing adapter has bounded deadline",()=>{int checks=0;string error="";try{TunStartup.Wait(()=>{checks++;return false;},()=>"",CancellationToken.None,3,0).GetAwaiter().GetResult();}catch(InvalidOperationException e){error=e.Message;}Check(error=="E_TUN_READY_TIMEOUT" && checks==3);});
        Test("native log never exposes raw content",()=>{Check(TunStartup.Classify("Start TUN listening error: access is denied secret=value")=="E_TUN_ACCESS");Check(TunStartup.Classify("unrelated secret=value")=="");});
        Test("startup timeout then success",()=>{int calls=0;ConnectionReadiness.Verify(t=>{if(++calls==1)throw new TaskCanceledException();return Task.FromResult(0);},CancellationToken.None,0).GetAwaiter().GetResult();Check(calls==2);});
        Test("persistent timeout exits with error",()=>{int calls=0;string error="";try{ConnectionReadiness.Verify(t=>{calls++;throw new TaskCanceledException();},CancellationToken.None,0).GetAwaiter().GetResult();}catch(InvalidOperationException ex){error=ex.Message;}Check(calls==3 && error=="E_NETWORK_TIMEOUT");});
        Test("user cancellation stops retries",()=>{int calls=0;using(var c=new CancellationTokenSource()){bool cancelled=false;try{ConnectionReadiness.Verify(t=>{calls++;c.Cancel();throw new TaskCanceledException();},c.Token,0).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}Check(cancelled && calls==1);}});
        Test("wrong exit is never accepted or retried",()=>{int calls=0;string error="";try{ConnectionReadiness.Verify(t=>{calls++;throw new InvalidOperationException("E_EXIT");},CancellationToken.None,0).GetAwaiter().GetResult();}catch(InvalidOperationException ex){error=ex.Message;}Check(calls==1 && error=="E_EXIT");});
        Test("official login allowed",()=>Check(Security.LoginAllowed("https://aiworkport.com/login")));
        foreach(var url in new[]{"http://aiworkport.com/","https://aiworkport.com.evil.test/","https://evil@aiworkport.com/","https://aiworkport.com:444/","file:///c:/test","javascript:alert(1)"}){var u=url;Test("reject navigation "+u.Split(':')[0],()=>Check(!Security.LoginAllowed(u)));}
        Test("callback extracted",()=>Check(Security.Assertion("https://aivpn.103.6.169.231.sslip.io/sso#assertion=a.b.c")=="a.b.c"));
        Test("wrong callback path",()=>Check(Security.Assertion("https://aivpn.103.6.169.231.sslip.io/other#assertion=x")==null));
        Test("duplicate callback",()=>Check(Security.Assertion("https://aivpn.103.6.169.231.sslip.io/sso#assertion=x&assertion=y")==null));
        Test("empty callback",()=>Check(Security.Assertion("https://aivpn.103.6.169.231.sslip.io/sso#assertion=")==null));
        Test("valid profile",()=>Check(Json.Str(Profile.Parse(Sample()),"ip")=="10.9.0.100"));
        Test("duplicate field rejected",()=>Reject(()=>Profile.Parse(Sample()+"PublicKey = fake\n")));
        Test("shell hook rejected",()=>Reject(()=>Profile.Parse(Sample().Replace("MTU = 1280","PostUp = calc.exe"))));
        Test("foreign endpoint rejected",()=>Reject(()=>Profile.Parse(Sample().Replace("103.6.169.231:585","127.0.0.1:585"))));
        Test("foreign address rejected",()=>Reject(()=>Profile.Parse(Sample().Replace("10.9.0.100/32","192.168.1.1/32"))));
        Test("oversized profile rejected",()=>Reject(()=>Profile.Parse(new string('x',16385))));
        Test("incomplete AWG rejected",()=>Reject(()=>Profile.Parse(Sample().Replace("S4 = 8\n",""))));
        Test("junk range rejected",()=>Reject(()=>Profile.Parse(Sample().Replace("Jmin = 64","Jmin = 999"))));
        Test("duplicate peer rejected",()=>Reject(()=>Profile.Parse(Sample()+"[Peer]\n")));
        Test("empty rules rejected",()=>Reject(()=>Profile.Build(Sample(),new string[0],new[]{"1.0.1.0/24"},false,false)));
        var auto=Profile.Build(Sample(),new[]{"+.baidu.com"},new[]{"1.0.1.0/24"},false,false);
        var global=Profile.Build(Sample(),new[]{"+.baidu.com"},new[]{"1.0.1.0/24"},true,false);
        Test("auto domain direct",()=>Check(((string[])auto["rules"]).Contains("RULE-SET,cn-domains,DIRECT")));
        Test("ZeRemote relay bypasses VPN in auto mode before catch-all",()=>{var rules=(string[])auto["rules"];Check(rules[1]=="IP-CIDR,207.148.117.198/32,DIRECT,no-resolve" && Array.IndexOf(rules,rules[1])<Array.IndexOf(rules,"MATCH,AIVPN"));});
        Test("ZeRemote relay bypasses VPN in global mode before catch-all",()=>{var rules=(string[])global["rules"];Check(rules[1]=="IP-CIDR,207.148.117.198/32,DIRECT,no-resolve" && Array.IndexOf(rules,rules[1])<Array.IndexOf(rules,"MATCH,AIVPN") && rules.Count(x=>x.Contains("DIRECT"))==1);});
        Test("auto ChatGPT attachment upload domain bypasses VPN",()=>{var rules=(string[])auto["rules"];Check(Array.IndexOf(rules,"DOMAIN-SUFFIX,oaiusercontent.com,DIRECT")<Array.IndexOf(rules,"RULE-SET,cn-domains,DIRECT"));});
        Test("auto ChatGPT attachment DNS uses direct resolver",()=>{var dns=(Dictionary<string,object>)auto["dns"];var policy=(Dictionary<string,object>)dns["nameserver-policy"];Check(((string[])policy["+.oaiusercontent.com"])[0]=="https://223.5.5.5/dns-query#DIRECT");});
        Test("global no domestic bypass",()=>Check(!((string[])global["rules"]).Any(x=>x.Contains("DIRECT") && !x.StartsWith("IP-CIDR,207.148.117.198/32,DIRECT"))));
        Test("global ChatGPT attachment traffic remains in VPN",()=>Check(!((string[])global["rules"]).Any(x=>x.Contains("oaiusercontent.com")) && !((Dictionary<string,object>)global["dns"]).ContainsKey("nameserver-policy")));
        Test("rule comments removed",()=>{var clean=Profile.Build(Sample(),new[]{"# source","+.baidu.com"},new[]{"# source","1.0.1.0/24"},false,false);Check(!Json.Write(clean).Contains("# source"));});
        Test("wrong process identity rejected",()=>Check(!Security.IsProductProcess((uint)System.Diagnostics.Process.GetCurrentProcess().Id,"AIVPN.Service.exe")));
        Test("SCM rejects non-service PID",()=>Check(!Security.IsServiceProcess((uint)System.Diagnostics.Process.GetCurrentProcess().Id)));
        Test("IPv6 rejected",()=>Check(((string[])auto["rules"])[0]=="IP-CIDR6,::/0,REJECT,no-resolve"));
        Test("no controller or proxy listener",()=>Check((int)auto["mixed-port"]==0 && !auto.ContainsKey("external-controller")));
        Test("unknown routes VPN",()=>Check(((string[])auto["rules"]).Last()=="MATCH,AIVPN"));
        Test("JSON roundtrip",()=>Check(Json.Write(Json.Read(Json.Write(auto)))==Json.Write(auto)));
        Test("split default route conflict",()=>Check(NetworkGuard.IsConflict("0.0.0.0","128.0.0.0","other")));
        Test("IPv6 lower half conflict",()=>Check(NetworkGuard.IsConflictPrefix("::/1","other")));
        Test("IPv6 upper half conflict",()=>Check(NetworkGuard.IsConflictPrefix("8000::/1","other")));
        Test("IPv6 VPN default conflict",()=>Check(NetworkGuard.IsConflictPrefix("::/0","WireGuard Tunnel")));
        Test("IPv6 physical default allowed",()=>Check(!NetworkGuard.IsConflictPrefix("::/0","Intel WLAN")));
        Test("IPv6 private route allowed",()=>Check(!NetworkGuard.IsConflictPrefix("fd00::/8","Tailscale Tunnel")));
        Test("IPv4 prefix route still conflicts",()=>Check(NetworkGuard.IsConflictPrefix("128.0.0.0/1","other")));
        Test("VPN default route conflict",()=>Check(NetworkGuard.IsConflict("0.0.0.0","0.0.0.0","WireGuard Tunnel")));
        Test("ordinary wifi allowed",()=>Check(!NetworkGuard.IsConflict("0.0.0.0","0.0.0.0","Intel Wi-Fi")));
        Test("Tailscale non-default preserved",()=>Check(!NetworkGuard.IsConflict("100.64.0.0","255.192.0.0","Tailscale")));
        Test("bounded pipe rejects huge frame",()=>{using(var s=new MemoryStream(BitConverter.GetBytes(65537)))Reject(()=>Wire.Read(s,CancellationToken.None).GetAwaiter().GetResult());});
        Test("pipe roundtrip",()=>{using(var s=new MemoryStream()){Wire.Write(s,Json.Map("command","status"),CancellationToken.None).GetAwaiter().GetResult();s.Position=0;Check(Json.Str(Wire.Read(s,CancellationToken.None).GetAwaiter().GetResult(),"command")=="status");}});
        Test("idle status safe",()=>{var t=new Tunnel("unused");Check(Json.Str(t.Handle("test",1,Json.Map("command","status")),"state")=="DISCONNECTED");t.Stop();});
        Test("unknown service command rejected",()=>Reject(()=>new Tunnel("unused").Handle("test",1,Json.Map("command","execute","path","calc.exe"))));
        Test("traffic counter reset never becomes negative",()=>Check(TrafficDisplay.Delta(10,20)==0 && TrafficDisplay.Delta(30,20)==10));
        Test("traffic rate formats bits per second",()=>Check(TrafficDisplay.Rate(125000,1)=="1 Mbps" && TrafficDisplay.Rate(0,2)=="0 Kbps"));
        Test("traffic totals use readable units",()=>Check(TrafficDisplay.Bytes(1536)=="1.5 KB" && TrafficDisplay.Bytes(1048576)=="1.0 MB"));
        string tmp=Path.Combine(Path.GetTempPath(),"aivpn-vault-test-"+Guid.NewGuid().ToString("N"));
        Test("DPAPI encrypted persistence",()=>{try{var v=new Vault(tmp);v.Set("test","synthetic-not-a-secret");Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(tmp,"session.bin"))).Contains("synthetic-not-a-secret"));Check(new Vault(tmp).Get("test")=="synthetic-not-a-secret");v.Remove("test");Check(new Vault(tmp).Get("test")=="");}finally{if(File.Exists(Path.Combine(tmp,"session.bin")))File.Delete(Path.Combine(tmp,"session.bin"));if(Directory.Exists(tmp))Directory.Delete(tmp,false);}});
        if(args.Length==1){Directory.CreateDirectory(args[0]);string baseDir=AppDomain.CurrentDomain.BaseDirectory;var domains=File.ReadAllLines(Path.Combine(baseDir,"cn-domains.list"));var networks=File.ReadAllLines(Path.Combine(baseDir,"cn-ip.txt"));
            File.WriteAllText(Path.Combine(args[0],"auto-fixture.json"),Json.Write(Profile.Build(Sample(),domains,networks,false,false)),new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(args[0],"global-fixture.json"),Json.Write(Profile.Build(Sample(),domains,networks,true,false)),new UTF8Encoding(false));
        }
        Console.WriteLine("TOTAL "+passed+" PASSED (synthetic/offline; no tunnel started)");return 0;
    }catch(Exception ex){Console.WriteLine(ex.Message);return 1;}}
}
}
