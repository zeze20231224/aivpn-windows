using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Aivpn {
public static class Setup {
    static int Run(string exe,string args,int timeout=60000){using(var p=Process.Start(new ProcessStartInfo(exe,args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){p.OutputDataReceived+=(s,e)=>{};p.ErrorDataReceived+=(s,e)=>{};p.BeginOutputReadLine();p.BeginErrorReadLine();if(!p.WaitForExit(timeout))throw new InvalidOperationException("系统操作超时");return p.ExitCode;}}
    static string Sc {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"sc.exe");}}
    static string RegPath="SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\AIVPNCandidate1";
    static bool RuntimeInstalled(){string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Microsoft","EdgeWebView","Application");return Directory.Exists(root) && Directory.GetDirectories(root).Any(x=>File.Exists(Path.Combine(x,"msedgewebview2.exe")));}
    static string ServicePath(){using(var key=Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\"+Product.Service)){return key==null?null:Convert.ToString(key.GetValue("ImagePath")).Trim('"');}}
    static bool DirectoryIsSafe(string path){return Directory.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)==0;}
    static void StopOwnedClient(){foreach(var process in Process.GetProcessesByName("AIVPN")){using(process){if(!Security.IsProductProcess((uint)process.Id,"AIVPN.exe"))continue;process.CloseMainWindow();if(!process.WaitForExit(15000))throw new InvalidOperationException("请先关闭 AIVPN 窗口后重试；旧客户端未退出，文件未替换。");}}}
    static void StopService(){using(var svc=new ServiceController(Product.Service)){try{if(svc.Status!=ServiceControllerStatus.Stopped){svc.Stop();svc.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(30));}}catch(InvalidOperationException){throw new InvalidOperationException("AIVPN 服务未能安全停止，文件未替换。");}}}
    static void StartService(){using(var svc=new ServiceController(Product.Service)){svc.Start();svc.WaitForStatus(ServiceControllerStatus.Running,TimeSpan.FromSeconds(30));}}
    static void InstallPayload(string target,Assembly assembly,Dictionary<string,object> files){
        Security.PrivateDirectory(target,true);
        var acl=Directory.GetAccessControl(target);acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null),FileSystemRights.ReadAndExecute,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));Directory.SetAccessControl(target,acl);
        using(var zip=new ZipArchive(assembly.GetManifestResourceStream("payload.zip"),ZipArchiveMode.Read)){
            if(zip.Entries.Count!=files.Count)throw new InvalidOperationException("安装包清单不匹配");
            foreach(var entry in zip.Entries){string name=entry.FullName;if(Path.GetFileName(name)!=name || name.Contains("..") || !files.ContainsKey(name))throw new InvalidOperationException("安装包路径无效");string dest=Path.Combine(target,name);entry.ExtractToFile(dest,false);if(Security.Hash(dest)!=(string)files[name])throw new InvalidOperationException("安装包文件校验失败");}
        }
        File.Copy(assembly.Location,Path.Combine(target,"Uninstall.exe"),false);
    }
    static void WriteRegistration(){using(var key=Registry.LocalMachine.CreateSubKey(RegPath)){key.SetValue("DisplayName","AIVPN Windows 测试版");key.SetValue("DisplayVersion",Product.Version);key.SetValue("Publisher","AI WorkPort (unsigned candidate)");key.SetValue("InstallLocation",Product.InstallDir);key.SetValue("UninstallString","\""+Path.Combine(Product.InstallDir,"Uninstall.exe")+"\" --uninstall");key.SetValue("NoModify",1,RegistryValueKind.DWord);key.SetValue("NoRepair",1,RegistryValueKind.DWord);}}
    static void WriteShortcut(){string menu=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),"AIVPN 测试版.lnk");dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));dynamic shortcut=shell.CreateShortcut(menu);shortcut.TargetPath=Path.Combine(Product.InstallDir,"AIVPN.exe");shortcut.WorkingDirectory=Product.InstallDir;shortcut.Save();}
    static void Install(){
        // A completed uninstall may leave only its running executable. Reinstalling the exact same
        // verified package can remove that single file once the old process has exited.
        if(Directory.Exists(Product.InstallDir) && (File.GetAttributes(Product.InstallDir)&FileAttributes.ReparsePoint)==0){var remaining=Directory.GetFileSystemEntries(Product.InstallDir);if(remaining.Length==1 && remaining[0]==Path.Combine(Product.InstallDir,"Uninstall.exe") && Security.Hash(remaining[0])==Security.Hash(Assembly.GetExecutingAssembly().Location)){File.Delete(remaining[0]);Directory.Delete(Product.InstallDir,false);}}
        bool existing=Directory.Exists(Product.InstallDir)||File.Exists(Product.InstallDir);
        if(File.Exists(Product.InstallDir)||(existing&&!DirectoryIsSafe(Product.InstallDir)))throw new InvalidOperationException("安装目录异常或为链接，已停止。");
        string serviceImage=ServicePath();
        bool serviceExists=serviceImage!=null;
        if(existing||serviceExists){
            if(!DirectoryIsSafe(Product.InstallDir)||!serviceExists||!String.Equals(serviceImage,Path.Combine(Product.InstallDir,"AIVPN.Service.exe"),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("检测到不完整或路径不匹配的旧安装。为保护现有文件，未执行覆盖。");
            using(var key=Registry.LocalMachine.OpenSubKey(RegPath)){if(key==null||!String.Equals(Convert.ToString(key.GetValue("InstallLocation")),Product.InstallDir,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("旧安装登记与服务不一致，未执行覆盖。");}
        }
        else if(ServiceController.GetServices().Any(x=>x.ServiceName==Product.Service))throw new InvalidOperationException("检测到无法读取路径的同名服务，未执行覆盖。");
        string prompt=existing?"原地升级 AIVPN Windows 测试版至 "+Product.Version+"？":"安装 AIVPN Windows 独立测试版？";
        prompt+="\n\n需要管理员授权。升级会短暂断开当前 VPN，先校验新文件，再替换客户端和服务；失败时恢复旧程序。加密账号数据保存在安装目录外，会保留。缺少登录组件时会联网安装微软官方 WebView2 Runtime。\n\n此包未签名，且真实账号、TUN和长时间连接尚未验收。";
        if(MessageBox.Show(prompt,"AIVPN 安装",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;
        var assembly=Assembly.GetExecutingAssembly();Dictionary<string,object> files;using(var r=new StreamReader(assembly.GetManifestResourceStream("payload-manifest.json")))files=Json.Obj(r.ReadToEnd());
        string parent=Path.GetDirectoryName(Product.InstallDir),stage=Path.Combine(parent,"AIVPN Candidate.upgrade-"+Guid.NewGuid().ToString("N")),backup=Path.Combine(parent,"AIVPN Candidate.backup-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
        bool serviceStopped=false,serviceWasRunning=false,oldMoved=false,newMoved=false;
        try{
            InstallPayload(stage,assembly,files);
            if(!RuntimeInstalled()&&(!File.Exists(Path.Combine(stage,"MicrosoftEdgeWebview2Setup.exe"))))throw new InvalidOperationException("缺少微软登录组件安装文件。");
            if(existing){using(var svc=new ServiceController(Product.Service)){serviceWasRunning=svc.Status==ServiceControllerStatus.Running;}StopOwnedClient();serviceStopped=true;StopService();Directory.Move(Product.InstallDir,backup);oldMoved=true;}
            Directory.Move(stage,Product.InstallDir);newMoved=true;
            if(existing){if(Run(Sc,"config "+Product.Service+" binPath= \"\\\""+Path.Combine(Product.InstallDir,"AIVPN.Service.exe")+"\\\"\" start= auto")!=0)throw new InvalidOperationException("无法更新连接服务路径。");}
            else if(Run(Sc,"create "+Product.Service+" binPath= \"\\\""+Path.Combine(Product.InstallDir,"AIVPN.Service.exe")+"\\\"\" start= auto DisplayName= \"AIVPN Candidate Connection\"")!=0)throw new InvalidOperationException("无法注册连接服务。");
            WriteRegistration();if(!existing||serviceWasRunning)StartService();WriteShortcut();
            if(!RuntimeInstalled() && (Run(Path.Combine(Product.InstallDir,"MicrosoftEdgeWebview2Setup.exe"),"/silent /install",240000)!=0 || !RuntimeInstalled()))throw new InvalidOperationException("微软登录组件安装未完成。");
            if(oldMoved)try{Directory.Delete(backup,true);}catch{}
        }catch(Exception ex){
            try{if(oldMoved||newMoved){if(serviceExists)StopService();else if(ServicePath()!=null)Run(Sc,"delete "+Product.Service);}}catch{}
            try{if(newMoved&&DirectoryIsSafe(Product.InstallDir)){string failed=Product.InstallDir+".failed-"+Guid.NewGuid().ToString("N");Directory.Move(Product.InstallDir,failed);Directory.Delete(failed,true);}}catch{}
            try{if(oldMoved&&Directory.Exists(backup)&&!Directory.Exists(Product.InstallDir)){Directory.Move(backup,Product.InstallDir);Run(Sc,"config "+Product.Service+" binPath= \"\\\""+Path.Combine(Product.InstallDir,"AIVPN.Service.exe")+"\\\"\" start= auto");WriteRegistration();if(serviceWasRunning)StartService();}else if(serviceStopped&&serviceWasRunning)StartService();}catch{}
            try{if(Directory.Exists(stage))Directory.Delete(stage,true);}catch{}
            throw new InvalidOperationException("安装未完成；已尝试恢复旧程序与服务。"+(Directory.Exists(backup)?"旧程序备份保留在 "+backup:""),ex);
        }
        MessageBox.Show("安装完成。\n\n从开始菜单打开「AIVPN 测试版」，登录 AI WorkPort 账号后点击连接。\n\n请先断开其他 VPN；不要以管理员身份运行客户端。", "AIVPN");
    }
    static void Uninstall(){
        if(!String.Equals(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),Product.InstallDir,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("请通过 Windows 应用列表卸载。");
        if((File.GetAttributes(Product.InstallDir)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("安装目录异常，已停止卸载。");
        foreach(var process in Process.GetProcessesByName("AIVPN")){using(process){if(Security.IsProductProcess((uint)process.Id,"AIVPN.exe"))throw new InvalidOperationException("请先关闭 AIVPN 窗口再卸载。");}}
        if(MessageBox.Show("卸载 AIVPN 测试版并停止它的连接服务？\n\n其他 VPN 不受影响；当前用户的加密登录数据保留。", "卸载 AIVPN",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
        using(var key=Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\"+Product.Service)){
            if(key!=null){string actual=Convert.ToString(key.GetValue("ImagePath")).Trim('"');if(!String.Equals(actual,Path.Combine(Product.InstallDir,"AIVPN.Service.exe"),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("服务路径不匹配，已停止。");
                using(var svc=new ServiceController(Product.Service)){if(svc.Status!=ServiceControllerStatus.Stopped){svc.Stop();svc.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(30));}}
                if(Run(Sc,"delete "+Product.Service)!=0)throw new InvalidOperationException("无法删除测试版服务。");
            }
        }
        var asm=Assembly.GetExecutingAssembly();Dictionary<string,object> manifest;using(var r=new StreamReader(asm.GetManifestResourceStream("payload-manifest.json")))manifest=Json.Obj(r.ReadToEnd());
        foreach(var name in manifest.Keys){if(name!=Path.GetFileName(name))throw new InvalidOperationException("文件名不匹配");var path=Path.Combine(Product.InstallDir,name);if(File.Exists(path)){if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("文件路径异常");File.Delete(path);}}
        string menu=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),"AIVPN 测试版.lnk");if(File.Exists(menu))File.Delete(menu);Registry.LocalMachine.DeleteSubKeyTree(RegPath,false);
        MessageBox.Show("客户端和服务已卸载；账号数据已保留。安装目录中仅留下当前正在运行的 Uninstall.exe，退出后可手动删除。", "AIVPN");
    }
    [STAThread] static void Main(string[] args){Application.EnableVisualStyles();try{if(args.Length==1 && args[0]=="--uninstall")Uninstall();else if(args.Length==0)Install();else throw new InvalidOperationException("不支持的安装参数");}catch(Exception ex){MessageBox.Show("安装操作未完成。\n"+(ex is InvalidOperationException?ex.Message:"请检查权限及安装目录后重试。"),"AIVPN",MessageBoxButtons.OK,MessageBoxIcon.Error);}}
}
}
