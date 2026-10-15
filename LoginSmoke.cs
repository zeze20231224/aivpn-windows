using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
namespace Aivpn {
public static class LoginSmoke {
    [STAThread] static int Main(string[] args){
        if(args.Length!=1)return 2;Application.EnableVisualStyles();string folder=Path.GetFullPath(args[0]);Directory.CreateDirectory(folder);bool passed=false;
        using(var f=new Form{Width=650,Height=760,StartPosition=FormStartPosition.Manual,Location=new Point(-16000,-16000)})using(var browser=new WebView2{Dock=DockStyle.Fill}){
            f.Controls.Add(browser);var timer=new System.Windows.Forms.Timer{Interval=45000};timer.Tick+=(s,e)=>f.Close();timer.Start();
            f.Shown+=async(s,e)=>{try{
                var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(folder,"isolated-login-profile"));await browser.EnsureCoreWebView2Async(env);var core=browser.CoreWebView2;
                core.Settings.AreHostObjectsAllowed=false;core.Settings.IsWebMessageEnabled=false;core.Settings.AreDevToolsEnabled=false;core.Settings.IsPasswordAutosaveEnabled=false;core.Settings.IsGeneralAutofillEnabled=false;
                core.PermissionRequested+=(o,a)=>a.State=CoreWebView2PermissionState.Deny;core.ServerCertificateErrorDetected+=(o,a)=>a.Action=CoreWebView2ServerCertificateErrorAction.Cancel;
                core.NewWindowRequested+=(o,a)=>a.Handled=true;core.DownloadStarting+=(o,a)=>a.Cancel=true;
                core.NavigationStarting+=(o,a)=>{if(!Security.LoginAllowed(a.Uri))a.Cancel=true;else {var u=new Uri(a.Uri);Console.WriteLine("NAV "+u.Host+u.AbsolutePath);}};
                core.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);core.WebResourceRequested+=(o,a)=>{if(!Security.LoginAllowed(a.Request.Uri))a.Response=env.CreateWebResourceResponse(new MemoryStream(),403,"Blocked","");};
                core.NavigationCompleted+=(o,a)=>Console.WriteLine("NAV_RESULT "+a.IsSuccess+" "+a.WebErrorStatus.ToString());
                core.Navigate(Product.Login);
                // Next.js may use history replacement without another NavigationCompleted event.
                for(int step=0;step<30 && !f.IsDisposed;step++){
                    await Task.Delay(1000);if(f.IsDisposed)break;Uri location;if(!Uri.TryCreate(core.Source,UriKind.Absolute,out location))continue;
                    if(location.Host=="aiworkport.com" && location.AbsolutePath=="/login" && await core.ExecuteScriptAsync("document.querySelectorAll('input[type=password]').length")=="1"){
                        using(var img=File.Create(Path.Combine(folder,"official-login.png")))await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,img);
                        passed=true;File.WriteAllText(Path.Combine(folder,"result.json"),Json.Write(Json.Map("status","PASS","tested_utc",DateTime.UtcNow.ToString("o"),"webview_runtime",env.BrowserVersionString,"public_login_form",true,"credentials_entered",false,"vpn_started",false)));Console.WriteLine("PASS WebView2 official /vpn -> /login, password form rendered; no account entered");break;
                    }
                }
                if(!passed && !f.IsDisposed){Console.WriteLine("PAGE_TEXT "+await core.ExecuteScriptAsync("document.body.innerText.slice(0,800)"));using(var diagnostic=File.Create(Path.Combine(folder,"login-diagnostic.png")))await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,diagnostic);}f.Close();
            }catch(Exception ex){Console.WriteLine("LOGIN_SMOKE_FAILED "+ex.GetType().Name);f.Close();}};
            Application.Run(f);timer.Dispose();
        }return passed?0:1;
    }
}
}
