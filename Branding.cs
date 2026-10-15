using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
namespace Aivpn {
public static class Branding {
    public static void Mark(Graphics g, Rectangle r) {
        var old=g.Save();g.TranslateTransform(r.X,r.Y);g.ScaleTransform(r.Width/256f,r.Height/256f);
        g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var b=new SolidBrush(Color.FromArgb(12,28,43)))g.FillEllipse(b,4,4,248,248);
        using(var p=new Pen(Color.FromArgb(53,78,91),3))g.DrawEllipse(p,5,5,246,246);
        using(var b=new SolidBrush(Color.FromArgb(77,222,195))) {
            g.FillPolygon(b,new[]{new PointF(57,186),new PointF(116, 60),new PointF(140,60),new PointF(99,153),new PointF(86,186)});
            g.FillPolygon(b,new[]{new PointF(150,91),new PointF(201,186),new PointF(170,186),new PointF(136,121)});
        }
        using(var p=new Pen(Color.FromArgb(237,247,249),12)){p.StartCap=LineCap.Round;p.EndCap=LineCap.Round;g.DrawLine(p,111,164,151,164);}
        g.Restore(old);
    }
    public static void Export(string dir) {
        Directory.CreateDirectory(dir);
        using(var b=new Bitmap(256,256)){using(var g=Graphics.FromImage(b))Mark(g,new Rectangle(0,0,256,256));b.Save(Path.Combine(dir,"aivpn-icon.png"),ImageFormat.Png);}
        int[] sizes={16,24,32,48,64,128,256};var data=new byte[sizes.Length][];
        for(int i=0;i<sizes.Length;i++)using(var b=new Bitmap(sizes[i],sizes[i])){using(var g=Graphics.FromImage(b))Mark(g,new Rectangle(0,0,sizes[i],sizes[i]));using(var m=new MemoryStream()){b.Save(m,ImageFormat.Png);data[i]=m.ToArray();}}
        using(var w=new BinaryWriter(File.Create(Path.Combine(dir,"aivpn.ico")))){w.Write((ushort)0);w.Write((ushort)1);w.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;for(int i=0;i<sizes.Length;i++){w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((byte)(sizes[i]==256?0:sizes[i]));w.Write((ushort)0);w.Write((ushort)1);w.Write((ushort)32);w.Write(data[i].Length);w.Write(offset);offset+=data[i].Length;}foreach(var bytes in data)w.Write(bytes);}
    }
}
public sealed class BrandMark:Control {
    public BrandMark(){DoubleBuffered=true;AccessibleName="AIVPN 标志";}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);int n=Math.Min(Width,Height);Branding.Mark(e.Graphics,new Rectangle((Width-n)/2,(Height-n)/2,n,n));}
}
public sealed class ReadableButton:Button {
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(!Enabled){e.Graphics.Clear(BackColor);using(var p=new Pen(Color.FromArgb(48,71,88)))e.Graphics.DrawRectangle(p,0,0,Width-1,Height-1);TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Color.FromArgb(150,172,187),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}}
}
public sealed class ReadableCheckBox:CheckBox {
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(!Enabled){var r=new Rectangle(20,0,Math.Max(0,Width-20),Height);using(var b=new SolidBrush(BackColor))e.Graphics.FillRectangle(b,r);TextRenderer.DrawText(e.Graphics,Text,Font,r,Color.FromArgb(168,187,203),TextFormatFlags.Left|TextFormatFlags.VerticalCenter);}}
}
public static class BrandExport {public static void Main(string[] args){Branding.Export(args[0]);}}
}
