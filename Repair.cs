using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;
namespace AivpnRepair {
static class Repair {
    const string OldHash="1d28114ddb86eff1996b7163e6bc5cf94205f512eb87bcdbdb168d7f9d12a599";
    const string NewHash="003c9c1985cff936628276b5f43168eaddd30d72b6a4564ed388230fbaaf6b29";
    static string Hash(string path){using(var sha=SHA256.Create())using(var file=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant();}
    [STAThread] static void Main(){Application.EnableVisualStyles();try{
        string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"AIVPN Candidate"),dest=Path.Combine(root,"AIVPN.exe");
        if(!File.Exists(dest) || (File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("未找到正常安装目录，未做修改。");
        if(Hash(dest)==NewHash){MessageBox.Show("此修复已经安装。","AIVPN");return;}
        if(Hash(dest)!=OldHash)throw new InvalidOperationException("客户端版本不匹配，未覆盖任何文件。");
        if(NetworkInterface.GetAllNetworkInterfaces().Any(x=>x.Name=="AIVPN-Candidate" && x.OperationalStatus==OperationalStatus.Up))throw new InvalidOperationException("请先断开 AIVPN，再运行修复。");
        while(Process.GetProcessesByName("AIVPN").Length!=0){if(MessageBox.Show("请先关闭 AIVPN 主窗口（不用退出账号），然后点“重试”。\n\n这次只修复客户端识别系统服务的问题，不更改账号、服务器、其他 VPN 或路由。","AIVPN 修复",MessageBoxButtons.RetryCancel,MessageBoxIcon.Information)!=DialogResult.Retry)return;}
        string temp=Path.Combine(root,"AIVPN.rc2.new"),backup=Path.Combine(root,"AIVPN.exe.rc1.bak");
        if(File.Exists(temp)||File.Exists(backup))throw new InvalidOperationException("已有修复暂存或备份文件，请先核对；未覆盖。");
        using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("AIVPN.rc2.exe"))using(var output=File.Open(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))input.CopyTo(output);
        if(Hash(temp)!=NewHash){File.Delete(temp);throw new InvalidOperationException("修复载荷校验失败，原客户端未改动。");}
        File.Replace(temp,dest,backup);if(Hash(dest)!=NewHash)throw new InvalidOperationException("替换后校验失败，请保留备份联系支持。");
        MessageBox.Show("修复完成，账号与原配置均保留。\n\n请从开始菜单正常打开 AIVPN。连接前先在原 VPN 客户端中点击“断开”。","AIVPN 修复");
    }catch(Exception ex){MessageBox.Show(ex is InvalidOperationException?ex.Message:"修复未完成，请检查权限或文件占用。原版备份不会自动删除。","AIVPN",MessageBoxButtons.OK,MessageBoxIcon.Error);}}
}
}
