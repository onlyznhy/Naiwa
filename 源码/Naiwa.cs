using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NaiwaDesktop
{
    internal static class Native
    {
        internal const string Title="NaiwaDesktop.Source.v1";
        internal const int Wake=0x8001, Status=0x8002;
        [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X,Y; internal Point(int x,int y){X=x;Y=y;} }
        [StructLayout(LayoutKind.Sequential)] internal struct Size { internal int W,H; internal Size(int w,int h){W=w;H=h;} }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left,Top,Right,Bottom; }
        [StructLayout(LayoutKind.Sequential,Pack=1)] internal struct Blend { internal byte Op,Flags,Alpha,Format; }
        [StructLayout(LayoutKind.Sequential)] internal struct BitmapInfo
        {
            internal uint Size; internal int Width,Height; internal ushort Planes,BitCount;
            internal uint Compression,SizeImage; internal int XPels,YPels; internal uint Used,Important;
        }
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern IntPtr FindWindow(string cls,string text);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr h,int m,IntPtr w,IntPtr l);
        [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern int GetClassName(IntPtr h,StringBuilder name,int count);
        [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")] internal static extern int DwmRect(IntPtr h,int attr,out Rect value,int size);
        [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")] internal static extern int DwmInt(IntPtr h,int attr,out int value,int size);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr h,out Rect r);
        [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr h,int index);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr h,out uint id);
        [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr h,IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleDC(IntPtr h);
        [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr h);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr h);
        [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
        [DllImport("gdi32.dll")] internal static extern IntPtr CreateDIBSection(IntPtr h,ref BitmapInfo info,uint usage,out IntPtr bits,IntPtr section,uint offset);
        [DllImport("user32.dll",SetLastError=true)] internal static extern bool UpdateLayeredWindow(IntPtr h,IntPtr dest,ref Point position,ref Size size,IntPtr src,ref Point origin,uint key,ref Blend blend,uint flags);
        [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll")] internal static extern int GetGuiResources(IntPtr process,int flags);
    }

    internal static class Program
    {
        [STAThread] private static void Main(string[] args)
        {
            Native.SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if(args.Length>=2 && args[0]=="--self-test") { Tests.Run(args[1]); return; }
            if(args.Length>=2 && args[0]=="--demo") { Tests.Demo(args[1]); return; }
            if(args.Length>=2 && args[0]=="--probe")
            {
                IntPtr h=Native.FindWindow(null,Native.Title); Native.Rect r; Native.GetWindowRect(h,out r);
                File.WriteAllText(args[1],"Found="+(h!=IntPtr.Zero)+"\r\nVisible="+Native.IsWindowVisible(h)+"\r\nBounds="+r.Left+","+r.Top+","+r.Right+","+r.Bottom+"\r\nState="+Native.SendMessage(h,Native.Status,IntPtr.Zero,IntPtr.Zero),Encoding.UTF8); return;
            }
            if(args.Length==1 && args[0]=="--quit") { Native.PostMessage(Native.FindWindow(null,Native.Title),0x10,IntPtr.Zero,IntPtr.Zero); return; }
            bool fresh;
            using(Mutex mutex=new Mutex(true,"Local\\NaiwaDesktop.Source.v1",out fresh))
            {
                if(!fresh){Native.PostMessage(Native.FindWindow(null,Native.Title),Native.Wake,IntPtr.Zero,IntPtr.Zero);return;}
                Application.ThreadException+=delegate(object s,ThreadExceptionEventArgs e){Report(e.Exception); Application.Exit();};
                try { using(Assets assets=new Assets(AppDomain.CurrentDomain.BaseDirectory)) using(PetWindow pet=new PetWindow(assets,false)){Application.Run(pet);} }
                catch(Exception e){Report(e);}
            }
        }
        private static void Report(Exception e)
        {
            try{Directory.CreateDirectory(Settings.Folder);File.AppendAllText(Path.Combine(Settings.Folder,"error.log"),DateTime.Now.ToString("s")+" "+e+Environment.NewLine);}catch{}
            MessageBox.Show("奶蛙暂时没能启动。请将程序、素材.nwp 和原版笑声.wav 放在同一个文件夹。\n\n"+e.Message,"奶蛙桌宠",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
    }

    internal sealed class Assets:IDisposable
    {
        internal readonly int Width,Height,Count,EyeCount,BlinkCount;
        internal readonly double Fps,Duration,BlinkFps;
        private readonly byte[][] encoded;
        private readonly Bitmap[] eyes;
        private readonly Bitmap[][] blinks;
        private readonly Dictionary<int,Bitmap> cache=new Dictionary<int,Bitmap>();
        private readonly Queue<int> order=new Queue<int>();
        internal readonly string AudioPath;
        internal int CacheCount{get{return cache.Count;}}
        internal Assets(string folder)
        {
            AudioPath=Path.Combine(folder,"原版笑声.wav");
            using(ZipArchive zip=ZipFile.OpenRead(Path.Combine(folder,"素材.nwp")))
            {
                Dictionary<string,object> meta;
                using(StreamReader reader=new StreamReader(zip.GetEntry("metadata.json").Open())) meta=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(reader.ReadToEnd());
                Width=Convert.ToInt32(meta["width"]);Height=Convert.ToInt32(meta["height"]);Count=Convert.ToInt32(meta["frames"]);Fps=Convert.ToDouble(meta["fps"]);EyeCount=Convert.ToInt32(meta["eyeFrames"]);Duration=Count/Fps;
                if(Width<1||Height<1||Width>1024||Height>1024||Count<1||Count>2000||Fps<1||Fps>120||EyeCount<2||EyeCount>60)throw new InvalidDataException("素材信息无效");
                eyes=new Bitmap[EyeCount];
                for(int i=0;i<EyeCount;i++)eyes[i]=Decode(Read(zip,"look/"+i.ToString("00")+".png"));
                BlinkCount=Convert.ToInt32(meta["blinkFrames"]);BlinkFps=Convert.ToDouble(meta["blinkFps"]);
                if(BlinkCount<2||BlinkCount>60||BlinkFps<1||BlinkFps>120)throw new InvalidDataException("眨眼素材信息无效");
                blinks=new Bitmap[2][];
                for(int mode=0;mode<2;mode++){blinks[mode]=new Bitmap[BlinkCount];for(int i=0;i<BlinkCount;i++)blinks[mode][i]=Decode(Read(zip,"blink/"+mode+"/"+i.ToString("00")+".png"));}
                encoded=new byte[Count][];
                for(int i=0;i<Count;i++)encoded[i]=Read(zip,"laugh/"+i.ToString("0000")+".png");
            }
            // Warm up the first frames before the first double click.
            Frame(0);Frame(1);Frame(2);
        }
        private static byte[] Read(ZipArchive zip,string name)
        { ZipArchiveEntry entry=zip.GetEntry(name);if(entry==null)throw new InvalidDataException("缺少动作素材："+name);using(Stream s=entry.Open())using(MemoryStream m=new MemoryStream()){s.CopyTo(m);return m.ToArray();} }
        private Bitmap Decode(byte[] data)
        {
            using(MemoryStream stream=new MemoryStream(data)) using(Image image=Image.FromStream(stream))
            {
                if(image.Width!=Width||image.Height!=Height)throw new InvalidDataException("动作尺寸不一致");
                Bitmap b=new Bitmap(Width,Height,PixelFormat.Format32bppPArgb);
                using(Graphics g=Graphics.FromImage(b)){g.CompositingMode=CompositingMode.SourceCopy;g.DrawImageUnscaled(image,0,0);}return b;
            }
        }
        internal Bitmap Eye(double value){return eyes[Math.Max(0,Math.Min(EyeCount-1,(int)Math.Round(value*(EyeCount-1))))];}
        internal Bitmap Blink(double look,int frame){return blinks[look>.5?1:0][Math.Max(0,Math.Min(BlinkCount-1,frame))];}
        internal Bitmap Frame(int index)
        {
            index=Math.Max(0,Math.Min(Count-1,index));Bitmap b;
            if(cache.TryGetValue(index,out b))return b;
            b=Decode(encoded[index]);cache.Add(index,b);order.Enqueue(index);
            while(order.Count>12){int old=order.Dequeue();cache[old].Dispose();cache.Remove(old);}return b;
        }
        public void Dispose(){if(eyes!=null)foreach(Bitmap b in eyes)if(b!=null)b.Dispose();if(blinks!=null)foreach(Bitmap[] bank in blinks)foreach(Bitmap b in bank)if(b!=null)b.Dispose();foreach(Bitmap b in cache.Values)b.Dispose();cache.Clear();}
    }

    internal sealed class Emote:IDisposable
    {
        internal readonly string Name;
        internal readonly int Width,Height,FrameCount;
        internal readonly double Duration;
        private readonly Bitmap[] frames;
        internal Emote(string name,string path)
        {
            Name=name;
            using(Image image=Image.FromFile(path))
            {
                Guid[] dimensions=image.FrameDimensionsList;
                if(dimensions==null||dimensions.Length==0)throw new InvalidDataException("表情动画缺少帧信息："+name);
                FrameDimension dimension=new FrameDimension(dimensions[0]);
                FrameCount=image.GetFrameCount(dimension);
                if(FrameCount<1||FrameCount>120)throw new InvalidDataException("表情动画帧数无效："+name);
                Width=image.Width;Height=image.Height;frames=new Bitmap[FrameCount];
                for(int i=0;i<FrameCount;i++)
                {
                    image.SelectActiveFrame(dimension,i);Bitmap frame=new Bitmap(Width,Height,PixelFormat.Format32bppPArgb);
                    using(Graphics g=Graphics.FromImage(frame)){g.CompositingMode=CompositingMode.SourceCopy;g.DrawImageUnscaled(image,0,0);}frames[i]=frame;
                }
            }
            Duration=12;
        }
        internal Emote(string name,string folder,bool sequence)
        {
            Name=name;
            string[] paths=Directory.GetFiles(folder,"*.png");Array.Sort(paths,StringComparer.Ordinal);
            if(paths.Length<2||paths.Length>120)throw new InvalidDataException("表情帧序列无效："+name);
            List<Bitmap> source=new List<Bitmap>();
            try
            {
                foreach(string path in paths)using(Image image=Image.FromFile(path))
                {
                    Bitmap frame=new Bitmap(image.Width,image.Height,PixelFormat.Format32bppPArgb);
                    using(Graphics g=Graphics.FromImage(frame)){g.CompositingMode=CompositingMode.SourceCopy;g.DrawImageUnscaled(image,0,0);}source.Add(frame);
                }
                FrameCount=source.Count;Width=source.Max(delegate(Bitmap b){return b.Width;});Height=source.Max(delegate(Bitmap b){return b.Height;});frames=new Bitmap[FrameCount];
                for(int i=0;i<FrameCount;i++)
                {
                    Bitmap canvas=new Bitmap(Width,Height,PixelFormat.Format32bppPArgb);
                    using(Graphics g=Graphics.FromImage(canvas)){g.CompositingMode=CompositingMode.SourceCopy;g.Clear(Color.Transparent);g.DrawImageUnscaled(source[i],(Width-source[i].Width)/2,Height-source[i].Height);}frames[i]=canvas;
                }
            }
            finally{foreach(Bitmap frame in source)frame.Dispose();}
            Duration=5;
        }
        internal Bitmap Frame(int index){return frames[Math.Max(0,Math.Min(FrameCount-1,index))];}
        internal int FrameAt(double elapsed)
        {
            if(FrameCount<2)return 0;
            double moving=Math.Max(.1,Duration-2),time=elapsed%Duration;
            return time>=moving?FrameCount-1:Math.Min(FrameCount-1,(int)(time/moving*FrameCount));
        }
        public void Dispose(){if(frames!=null)foreach(Bitmap frame in frames)if(frame!=null)frame.Dispose();}
    }

    internal sealed class Motion
    {
        private const double PokeDuration=.95;
        internal double Look,Start=-1,Elapsed,Clock,BlinkStart=-1,NextBlink=4,WakeStart=-1;
        internal double PokeStart=-1,PokeX=144,PokeY=205,LandingStart=-1,DragAngle;
        internal bool Resting,Holding;
        internal bool Playing{get{return Start>=0;}}
        internal bool Blinking{get{return BlinkStart>=0;}}
        internal bool Poking{get{return PokeStart>=0;}}
        internal bool Landing{get{return LandingStart>=0;}}
        internal bool Waking{get{return WakeStart>=0;}}
        internal bool Moving{get{return Holding||Landing||Math.Abs(DragAngle)>.02;}}
        internal double PokeAmount{get{double t=Clock-PokeStart;if(!Poking||t<0||t>=PokeDuration)return 0;return t<.12?.60*Math.Sin(t/.12*Math.PI/2):.60*Math.Exp(-4.8*(t-.12))*Math.Cos(18*(t-.12));}}
        internal double Squash{get{double t=Clock-LandingStart;return Landing&&t>=0?.055*Math.Exp(-6*t)*Math.Sin(19*t):0;}}
        internal int PlayCount;
        private double previous=-1,dragTarget,dragVelocity,lastDragTime;
        private readonly Random random=new Random();
        internal void Update(double now,bool over,double duration,double blinkDuration)
        {
            Clock=now;double dt=previous<0?0:Math.Min(.05,Math.Max(0,now-previous));previous=now;
            double goal=over?1:0,step=dt/.18;Look+=Math.Max(-step,Math.Min(step,goal-Look));
            if(Playing){Elapsed=Math.Max(0,now-Start);if(Elapsed>=duration){Start=-1;Elapsed=duration;Resting=true;}}
            if(Waking&&now-WakeStart>=.24)WakeStart=-1;
            if(Poking&&now-PokeStart>=PokeDuration)PokeStart=-1;
            if(Landing&&now-LandingStart>=.9)LandingStart=-1;
            if(Blinking&&now-BlinkStart>=blinkDuration){BlinkStart=-1;ScheduleBlink(now);}
            if(!Playing&&!Resting&&!Holding&&!Landing&&!Poking&&!Waking&&!Blinking&&now>=NextBlink)BlinkStart=now;
            if(!Holding||now-lastDragTime>.10)dragTarget=0;
            // Critically damped tilt; bounded steps keep long timer gaps stable.
            int steps=Math.Max(1,(int)Math.Ceiling(dt/.008));double stepTime=dt/steps;
            for(int i=0;i<steps;i++){dragVelocity+=(160*(dragTarget-DragAngle)-25*dragVelocity)*stepTime;DragAngle+=dragVelocity*stepTime;}
            DragAngle=Math.Max(-7,Math.Min(7,DragAngle));
            if(!Holding&&Math.Abs(DragAngle)<.02&&Math.Abs(dragVelocity)<.05){DragAngle=0;dragVelocity=0;}
        }
        private void ScheduleBlink(double now){NextBlink=now+4+random.NextDouble()*4;}
        private void ClearEffects(double now){Clock=now;BlinkStart=PokeStart=WakeStart=LandingStart=-1;Holding=false;DragAngle=dragTarget=dragVelocity=0;ScheduleBlink(now);}
        internal bool Laugh(double now){if(Playing)return false;ClearEffects(now);Resting=false;Start=now;Elapsed=0;PlayCount++;return true;}
        internal void Stop(){Start=-1;Elapsed=0;Resting=false;ClearEffects(Clock);}
        internal void Stand(double now){if(!Resting)return;Resting=false;ClearEffects(now);WakeStart=now;}
        internal static bool InBelly(double x,double y){double dx=(x-144)/69,dy=(y-211)/68;return dx*dx+dy*dy<=1;}
        internal bool Poke(double now,double x,double y){if(Playing||Resting||Holding||!InBelly(x,y))return false;ClearEffects(now);PokeStart=now;PokeX=x;PokeY=y;return true;}
        internal void BeginDrag(double now){Start=-1;Elapsed=0;ClearEffects(now);Holding=true;lastDragTime=now;}
        internal void Drag(double now,double dx){if(!Holding)BeginDrag(now);double dt=Math.Max(1.0/120,now-lastDragTime);lastDragTime=now;dragTarget=Math.Max(-7,Math.Min(7,dx/dt*.013));Clock=now;}
        internal void EndDrag(double now){if(!Holding)return;Holding=false;dragTarget=0;Clock=now;LandingStart=now;ScheduleBlink(now);}
        internal void Suspend(double now){Start=-1;Elapsed=0;Look=0;ClearEffects(now);}
    }

    internal static class Fullscreen
    {
        internal static bool Covers(Rectangle window,Rectangle screen,int style,string name)
        {
            if((style&0x00c00000)!=0||name=="Progman"||name=="WorkerW"||name=="Shell_TrayWnd"||name=="Shell_SecondaryTrayWnd")return false;
            const int tolerance=2;
            return window.Left<=screen.Left+tolerance&&window.Top<=screen.Top+tolerance&&window.Right>=screen.Right-tolerance&&window.Bottom>=screen.Bottom-tolerance;
        }
        internal static bool IsActive(IntPtr window,IntPtr pet,Rectangle screen)
        {
            if(window==IntPtr.Zero||window==pet||!Native.IsWindowVisible(window)||Native.IsIconic(window))return false;
            Native.Rect r;if(!Native.GetWindowRect(window,out r))return false;
            try{int cloaked;if(Native.DwmInt(window,14,out cloaked,4)==0&&cloaked!=0)return false;Native.Rect frame;if(Native.DwmRect(window,9,out frame,16)==0)r=frame;}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}
            StringBuilder cls=new StringBuilder(256);Native.GetClassName(window,cls,cls.Capacity);
            return Covers(Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom),screen,Native.GetWindowLong(window,-16),cls.ToString());
        }
    }

    internal sealed class FullscreenGate
    {
        internal bool Hidden;
        private bool candidate;
        private double since;
        internal bool Update(bool detected,double now)
        {
            if(candidate!=detected){candidate=detected;since=now;}
            if(candidate!=Hidden&&now-since>=(candidate?.15:.25)){Hidden=candidate;return true;}return false;
        }
        internal void Reset(){Hidden=candidate=false;since=0;}
        internal void Set(bool value,double now){Hidden=candidate=value;since=now;}
    }

    internal sealed class Gesture
    {
        internal bool Pressed,Dragged;
        internal Point Origin;
        private Point lastClickPoint;
        private double lastClick=-100;
        internal void Down(Point p){Pressed=true;Dragged=false;Origin=p;}
        internal void Move(Point p){if(Pressed&&(Math.Abs(p.X-Origin.X)>5||Math.Abs(p.Y-Origin.Y)>5))Dragged=true;}
        internal bool Up(Point p,double now)
        {
            if(!Pressed)return false;Pressed=false;
            if(Dragged){lastClick=-100;return false;}
            bool twice=now-lastClick<=SystemInformation.DoubleClickTime/1000.0&&Math.Abs(p.X-lastClickPoint.X)<=SystemInformation.DoubleClickSize.Width&&Math.Abs(p.Y-lastClickPoint.Y)<=SystemInformation.DoubleClickSize.Height;
            lastClick=twice?-100:now;lastClickPoint=p;return twice;
        }
        internal void Cancel(){Pressed=false;Dragged=false;lastClick=-100;}
    }

    internal sealed class Settings
    {
        internal int Scale=85,X,Y;internal bool Position,Sound,OnTop=true,AutoHide=true;
        internal static string Folder{get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NaiwaDesktopPet");}}
        internal static Settings Load()
        {
            Settings s=new Settings();try{
                string file=Path.Combine(Folder,"settings.ini");if(!File.Exists(file))return s;
                foreach(string line in File.ReadAllLines(file)){string[] p=line.Split('=');int n;if(p.Length!=2)continue;
                    if(p[0]=="scale"&&int.TryParse(p[1],out n))s.Scale=Math.Max(50,Math.Min(700,n));
                    if(p[0]=="x"&&int.TryParse(p[1],out n)){s.X=n;s.Position=true;}
                    if(p[0]=="y"&&int.TryParse(p[1],out n))s.Y=n;
                    if(p[0]=="sound")s.Sound=p[1]=="True";
                    if(p[0]=="top")s.OnTop=p[1]!="False";
                    if(p[0]=="fullscreen")s.AutoHide=p[1]!="False";
                }
            }catch{}return s;
        }
        internal void Save(){try{Directory.CreateDirectory(Folder);File.WriteAllLines(Path.Combine(Folder,"settings.ini"),new string[]{"scale="+Scale,"x="+X,"y="+Y,"sound="+Sound,"top="+OnTop,"fullscreen="+AutoHide});}catch{}}
    }

    internal sealed class LayeredSurface:IDisposable
    {
        private IntPtr dc,bmp,old,bits;
        private byte[] buffer;
        private int width,height;
        internal void Update(IntPtr window,Bitmap image,Point location)
        {
            if(width!=image.Width||height!=image.Height)Create(image.Width,image.Height);
            BitmapData locked=image.LockBits(new Rectangle(0,0,width,height),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
            try{Marshal.Copy(locked.Scan0,buffer,0,buffer.Length);Marshal.Copy(buffer,0,bits,buffer.Length);}finally{image.UnlockBits(locked);}
            Native.Point destination=new Native.Point(location.X,location.Y),origin=new Native.Point(0,0);Native.Size size=new Native.Size(width,height);
            Native.Blend blend=new Native.Blend{Op=0,Flags=0,Alpha=255,Format=1};
            if(!Native.UpdateLayeredWindow(window,IntPtr.Zero,ref destination,ref size,dc,ref origin,0,ref blend,2))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        private void Create(int w,int h)
        {
            Dispose();width=w;height=h;buffer=new byte[w*h*4];dc=Native.CreateCompatibleDC(IntPtr.Zero);
            Native.BitmapInfo info=new Native.BitmapInfo{Size=40,Width=w,Height=-h,Planes=1,BitCount=32,Compression=0,SizeImage=(uint)(w*h*4)};
            bmp=Native.CreateDIBSection(dc,ref info,0,out bits,IntPtr.Zero,0);
            if(bmp==IntPtr.Zero)throw new InvalidOperationException("无法创建透明窗口");old=Native.SelectObject(dc,bmp);
        }
        internal bool Hit(int x,int y){return buffer!=null&&x>=0&&y>=0&&x<width&&y<height&&buffer[(y*width+x)*4+3]>24;}
        public void Dispose(){if(dc!=IntPtr.Zero){if(old!=IntPtr.Zero)Native.SelectObject(dc,old);if(bmp!=IntPtr.Zero)Native.DeleteObject(bmp);Native.DeleteDC(dc);}dc=bmp=old=bits=IntPtr.Zero;width=height=0;buffer=null;}
    }

    internal static class Renderer
    {
        internal static Icon CreateIcon(Bitmap image)
        {
            using(Bitmap small=new Bitmap(64,64,PixelFormat.Format32bppArgb))
            {
                Rectangle source=new Rectangle(60*image.Width/288,40*image.Height/368,175*image.Width/288,225*image.Height/368);
                using(Graphics g=Graphics.FromImage(small)){g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(image,new Rectangle(6,0,52,64),source,GraphicsUnit.Pixel);}
                IntPtr h=small.GetHicon();try{using(Icon borrowed=Icon.FromHandle(h))return (Icon)borrowed.Clone();}finally{Native.DestroyIcon(h);}
            }
        }
        internal static Bitmap Render(Assets assets,Motion m,int scale)
        {
            int w=288*scale/100,h=368*scale/100;
            Bitmap canvas=new Bitmap(w,h,PixelFormat.Format32bppPArgb);
            using(Graphics g=Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                // Transforms move the supplied artwork; no replacement body or expression is drawn.
                if(m.Moving)
                {
                    float px=w*.5f,py=h*(m.Holding?.56f:.9565f),s=(float)m.Squash;
                    g.TranslateTransform(px,py-(m.Holding?2f*scale/100:0));g.RotateTransform((float)m.DragAngle);
                    g.ScaleTransform(m.Holding?1.008f:1+s,m.Holding?1.018f:1-s);g.TranslateTransform(-px,-py);
                }
                Rectangle rect=new Rectangle(0,0,w,h);
                if(m.Resting)Draw(g,assets.Frame(assets.Count-1),rect,1);
                else if(m.Waking){float t=(float)Math.Min(1,(m.Clock-m.WakeStart)/.24);Draw(g,assets.Frame(assets.Count-1),rect,1-t);Draw(g,assets.Eye(m.Look),rect,t);}
                else if(!m.Playing)
                {
                    Bitmap idle=m.Blinking?assets.Blink(m.Look,(int)((m.Clock-m.BlinkStart)*assets.BlinkFps)):assets.Eye(m.Look);
                    if(m.Poking)using(Bitmap poke=WarpBelly(idle,m.PokeX,m.PokeY,m.PokeAmount))Draw(g,poke,rect,1);else Draw(g,idle,rect,1);
                }
                else if(m.Elapsed<.10){Draw(g,assets.Eye(m.Look),rect,1);Draw(g,assets.Frame((int)(m.Elapsed*assets.Fps)),rect,(float)(m.Elapsed/.10));}
                else Draw(g,assets.Frame((int)(m.Elapsed*assets.Fps)),rect,1);
            }return canvas;
        }
        internal static Bitmap WarpBelly(Bitmap original,double cx,double cy,double amount)
        {
            int w=original.Width,h=original.Height;byte[] source=new byte[w*h*4];
            BitmapData locked=original.LockBits(new Rectangle(0,0,w,h),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
            try{Marshal.Copy(locked.Scan0,source,0,source.Length);}finally{original.UnlockBits(locked);}
            byte[] result=(byte[])source.Clone();
            double scaleX=w/288.0,scaleY=h/368.0;cx*=scaleX;cy*=scaleY;
            double radiusX=96*scaleX,radiusY=72*scaleY,bellyTop=105*scaleY,bellyBottom=300*scaleY;
            int left=Math.Max(0,(int)(cx-radiusX)),right=Math.Min(w-2,(int)(cx+radiusX)),top=Math.Max((int)bellyTop,(int)(cy-radiusY)),bottom=Math.Min(Math.Min((int)bellyBottom,h-2),(int)(cy+radiusY));
            for(int y=top;y<=bottom;y++)for(int x=left;x<=right;x++)
            {
                double dx=x-cx,dy=y-cy,r=dx*dx/(radiusX*radiusX)+dy*dy/(radiusY*radiusY);if(r>=1)continue;
                // Include the belly outline, fading out before the face and feet.
                double neck=Math.Min(1,(y-bellyTop)/(30*scaleY)),legs=Math.Min(1,(bellyBottom-y)/(20*scaleY));
                double fade=neck*neck*(3-2*neck)*legs*legs*(3-2*legs);
                double fall=(1-r)*(1-r)*amount*fade;
                double sx=Math.Max(0,Math.Min(w-1.001,x+dx*fall)),sy=Math.Max(0,Math.Min(h-1.001,y+dy*fall*.6));
                int ix=Math.Max(0,Math.Min(w-2,(int)sx)),iy=Math.Max(0,Math.Min(h-2,(int)sy));double fx=sx-ix,fy=sy-iy;
                int dest=(y*w+x)*4,a=(iy*w+ix)*4,b=a+4,c=a+w*4,d=c+4;
                for(int channel=0;channel<4;channel++)result[dest+channel]=(byte)Math.Max(0,Math.Min(255,(source[a+channel]*(1-fx)+source[b+channel]*fx)*(1-fy)+(source[c+channel]*(1-fx)+source[d+channel]*fx)*fy));
            }
            Bitmap warped=new Bitmap(w,h,PixelFormat.Format32bppPArgb);locked=warped.LockBits(new Rectangle(0,0,w,h),ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb);
            try{Marshal.Copy(result,0,locked.Scan0,result.Length);}finally{warped.UnlockBits(locked);}return warped;
        }
        private static void Draw(Graphics g,Bitmap b,Rectangle rect,float alpha)
        {
            if(alpha<=0)return;
            using(ImageAttributes a=new ImageAttributes())
            {
                a.SetWrapMode(WrapMode.TileFlipXY);if(alpha<1){ColorMatrix c=new ColorMatrix();c.Matrix33=alpha;a.SetColorMatrix(c);}
                g.DrawImage(b,rect,0,0,b.Width,b.Height,GraphicsUnit.Pixel,a);
            }
        }
    }

    // A click-through transparent window used only while a full-screen pet effect is active.
    internal sealed class EffectWindow:Form
    {
        private readonly LayeredSurface surface=new LayeredSurface();
        private readonly Point target;
        private readonly int initialY;
        private readonly double startAt,duration;
        internal readonly bool SmallForeground;
        private Bitmap image;
        internal EffectWindow(Bitmap picture,Point location,int startY,double startTime,int fallDuration,bool smallForeground)
        {
            image=picture;SmallForeground=smallForeground;Text=Native.Title+".effect";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;
            target=location;initialY=startY;startAt=startTime;duration=fallDuration/1000.0;StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;ClientSize=image.Size;Location=new Point(location.X,startY);TopMost=true;
            Shown+=delegate{surface.Update(Handle,image,Location);image.Dispose();image=null;};
        }
        internal bool Advance(double now)
        {
            if(now<startAt)return false;
            int y=FallY(initialY,target.Y,now-startAt,duration);
            // The layered bitmap was uploaded once when shown. Moving its HWND avoids copying
            // hundreds of thousands of pixels for every pet on every animation frame.
            if(Top!=y)Location=new Point(target.X,y);
            return now-startAt>=duration;
        }
        internal int TargetY{get{return target.Y;}}
        internal int CenterY{get{return target.Y+Height/2;}}
        internal bool ReachedTarget{get{return Top==target.Y;}}
        internal static int FallY(int initialY,int targetY,double elapsed,double duration)
        {
            float progress=(float)Math.Max(0,Math.Min(1,elapsed/duration));
            if(progress<.84f)
            {
                float falling=progress/.84f;return (int)Math.Round(initialY+(targetY-initialY)*falling*falling);
            }
            float rebound=(progress-.84f)/.16f;
            return (int)Math.Round(targetY-Math.Sin(rebound*Math.PI)*(1-rebound)*Math.Min(52,Math.Abs(targetY-initialY)*.07));
        }
        protected override bool ShowWithoutActivation{get{return true;}}
        protected override CreateParams CreateParams{get{CreateParams p=base.CreateParams;p.ExStyle|=0x80000|0x80|0x08000000|0x20;p.ClassStyle|=8;return p;}}
        protected override void Dispose(bool disposing){if(disposing){surface.Dispose();if(image!=null){image.Dispose();image=null;}}base.Dispose(disposing);}
    }

    internal sealed class EffectSpec
    {
        internal string Name;internal float Angle;internal int Height,CenterX,CenterY,StartOffset,Delay,Duration;
    }
    internal sealed class PreparedEffect
    {
        internal EffectSpec Spec;internal Bitmap Picture;
    }

    internal sealed class PetWindow:Form
    {
        private readonly Assets assets;
        private readonly bool testing;
        private readonly Settings settings;
        internal readonly Motion Motion=new Motion();
        private readonly Gesture gesture=new Gesture();
        private readonly Stopwatch watch=Stopwatch.StartNew();
        private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
        private readonly LayeredSurface surface=new LayeredSurface();
        private readonly SoundPlayer sound;
        private readonly Icon petIcon;
        private readonly ContextMenuStrip menu;
        private readonly NotifyIcon tray;
        private ToolStripMenuItem soundItem,topItem,stopItem,fullscreenItem,holdEmoteItem,stopEmoteItem,stopEffectItem;
        private readonly Dictionary<string,Emote> emotes=new Dictionary<string,Emote>();
        private readonly Dictionary<string,Bitmap> effectSources=new Dictionary<string,Bitmap>();
        private Emote activeEmote;
        private double emoteStart;
        private bool holdEmote;
        private readonly List<EffectWindow> effectWindows=new List<EffectWindow>();
        private readonly List<EffectWindow> fallingEffects=new List<EffectWindow>();
        private readonly Queue<EffectSpec> pendingEffects=new Queue<EffectSpec>();
        private readonly Queue<PreparedEffect> preparedEffects=new Queue<PreparedEffect>();
        private readonly object effectQueueLock=new object();
        private Task effectRenderTask;
        private int effectGeneration,effectOutstanding;
        private bool screenEffectActive;
        private Rectangle effectArea;
        private int scaleBeforeEffect;
        private Point locationBeforeEffect;
        private bool topBeforeEffect;
        private Point dragWindow,lastDragPointer;
        private readonly FullscreenGate fullscreenGate=new FullscreenGate();
        private double nextFullscreenCheck,nextEffectFrame;
        private string lastVisual="";
        private bool disposing,menuOpen,ready,effectSettled;
        internal bool HiddenForFullscreen{get;private set;}
        internal int ScalePercent{get{return settings.Scale;}}
        internal bool SoundEnabled{get{return settings.Sound;}}
        internal int EffectCount{get{return effectWindows.Count;}}
        internal int PendingEffectCount{get{return Interlocked.CompareExchange(ref effectOutstanding,0,0);}}
        internal bool EffectComplete{get{return screenEffectActive&&PendingEffectCount==0&&fallingEffects.Count==0&&effectWindows.Count>=90&&effectWindows.All(e=>e.ReachedTarget);}}
        internal bool EffectSettled{get{return effectSettled;}}
        internal bool EffectWindowsTopMost{get{return effectWindows.Any(e=>e.TopMost);}}
        internal bool EffectBottomFirst{get{return effectWindows.Count>=90&&effectWindows[0].CenterY>effectWindows[effectWindows.Count-1].CenterY&&effectWindows.Zip(effectWindows.Skip(1),(a,b)=>a.CenterY>=b.CenterY).All(v=>v);}}
        internal double EffectPictureMs,EffectShowMs,EffectMoveMs;
        internal double EffectMaxShowMs,EffectMaxMoveMs;
        private double Now{get{return watch.Elapsed.TotalSeconds;}}

        internal PetWindow(Assets a,bool test)
        {
            assets=a;testing=test;settings=testing?new Settings():Settings.Load();
            Text=Native.Title;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.StandardClick|ControlStyles.StandardDoubleClick,true);
            ClientSize=SizeFor(settings.Scale);TopMost=settings.OnTop;Cursor=Cursors.Hand;
            if(settings.Position)Location=new Point(settings.X,settings.Y);else Home();
            KeepVisible();
            petIcon=Renderer.CreateIcon(assets.Eye(0));
            Icon=petIcon;
            if(File.Exists(assets.AudioPath)){sound=new SoundPlayer(assets.AudioPath);sound.Load();}
            LoadEmotes(AppDomain.CurrentDomain.BaseDirectory);
            menu=BuildMenu();
            if(!testing){tray=new NotifyIcon{Icon=petIcon,Text="奶蛙 · 戳肚子 / 双击笑 / 拖动",ContextMenuStrip=menu,Visible=true};tray.DoubleClick+=delegate{ReturnHome();};}
            timer.Interval=16;timer.Tick+=delegate{Tick();};
            Shown+=delegate{ready=true;RenderNow(true);if(!testing){CheckFullscreen(true);timer.Start();}};
        }
        protected override bool ShowWithoutActivation{get{return true;}}
        protected override CreateParams CreateParams{get{CreateParams p=base.CreateParams;p.ExStyle|=0x80000|0x80|0x08000000;p.ClassStyle|=8;return p;}}
        protected override void WndProc(ref Message m)
        {
            if(m.Msg==Native.Wake){ReturnHome();return;}
            if(m.Msg==Native.Status){m.Result=(IntPtr)(HiddenForFullscreen?4:Motion.Resting?3:Motion.Playing?2:Motion.Look>.5?1:0);return;}
            if(m.Msg==0x21){m.Result=(IntPtr)3;return;}
            if(m.Msg==0x84 && ready && !gesture.Pressed)
            {
                long value=m.LParam.ToInt64();Point p=PointToClient(new Point((short)(value&0xffff),(short)((value>>16)&0xffff)));
                m.Result=surface.Hit(p.X,p.Y)?(IntPtr)1:new IntPtr(-1);return;
            }
            base.WndProc(ref m);
            if(m.Msg==0x7e && ready){KeepVisible();RenderNow(true);Save();}
        }
        private ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip result=new ContextMenuStrip{Font=new Font("Microsoft YaHei UI",10f),ShowImageMargin=false};
            result.Items.Add(new ToolStripMenuItem("奶蛙 · 原版陪伴") {Enabled=false});result.Items.Add(new ToolStripSeparator());
            result.Items.Add("捧腹大笑（双击）",null,delegate{Laugh();});
            stopItem=new ToolStripMenuItem("停止大笑 / 站起来",null,delegate{if(Motion.Resting){Motion.Stand(Now);RenderNow(true);}else StopLaugh();});result.Items.Add(stopItem);
            soundItem=new ToolStripMenuItem("原版笑声",null,delegate{SetSound(!settings.Sound);});result.Items.Add(soundItem);
            result.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem sizes=new ToolStripMenuItem("大小");
            int[] values={65,85,110,150,200,300,600,700};string[] labels={"小号","标准","大号","大号 Pro","大号 Max","大号 Pro Max","大杯（600%）","超大杯（700%）"};
            for(int i=0;i<values.Length;i++){int v=values[i];ToolStripMenuItem entry=new ToolStripMenuItem(labels[i],null,delegate{SetSize(v);});entry.Tag=v;sizes.DropDownItems.Add(entry);}result.Items.Add(sizes);
            if(emotes.ContainsKey("heart"))result.Items.Add("比心",null,delegate{PlayEmote("heart");});
            if(emotes.ContainsKey("emo_idle"))result.Items.Add("EMO（发呆）",null,delegate{PlayEmote("emo_idle");});
            if(emotes.ContainsKey("emo_wipe"))result.Items.Add("EMO（擦眼）",null,delegate{PlayEmote("emo_wipe");});
            if(emotes.ContainsKey("smoke"))result.Items.Add("靠墙抽烟",null,delegate{PlayEmote("smoke");});
            if(emotes.ContainsKey("umbrella"))result.Items.Add("雨天撑伞",null,delegate{PlayEmote("umbrella");});
            if(emotes.ContainsKey("coffee"))result.Items.Add("喝咖啡发呆",null,delegate{PlayEmote("coffee");});
            if(emotes.ContainsKey("sad"))result.Items.Add("抱膝低落",null,delegate{PlayEmote("sad");});
            holdEmoteItem=new ToolStripMenuItem("停留当前动作",null,delegate{holdEmote=!holdEmote;UpdateMenu();});result.Items.Add(holdEmoteItem);
            stopEmoteItem=new ToolStripMenuItem("结束表情动作",null,delegate{holdEmote=false;StopEmote(true);UpdateMenu();});result.Items.Add(stopEmoteItem);
            result.Items.Add(new ToolStripSeparator());
            result.Items.Add("满屏：原版动作旋转铺满",null,delegate{StartScreenEffectAction("original");});
            result.Items.Add("满屏：原版大笑旋转铺满",null,delegate{StartScreenEffectAction("laugh");});
            result.Items.Add("满屏：同一动作旋转铺满",null,delegate{StartScreenEffect(false);});
            result.Items.Add("满屏：不同动作旋转铺满",null,delegate{StartScreenEffect(true);});
            stopEffectItem=new ToolStripMenuItem("关闭满屏效果",null,delegate{StopScreenEffect();});result.Items.Add(stopEffectItem);
            topItem=new ToolStripMenuItem("总在最上面",null,delegate{TopMost=!TopMost;settings.OnTop=TopMost;Save();UpdateMenu();});result.Items.Add(topItem);
            fullscreenItem=new ToolStripMenuItem("全屏时自动隐藏",null,delegate{SetAutoHide(!settings.AutoHide);});result.Items.Add(fullscreenItem);
            result.Items.Add("回到屏幕右下角",null,delegate{ReturnHome();});
            result.Items.Add("打开使用说明",null,delegate{Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"使用说明.txt")){UseShellExecute=true});});
            result.Items.Add(new ToolStripSeparator());result.Items.Add("退出奶蛙",null,delegate{Close();});
            result.Opening+=delegate{menuOpen=true;UpdateMenu();};result.Closed+=delegate{menuOpen=false;};return result;
        }
        private void UpdateMenu()
        {
            if(menu==null)return;SetToggleMark(soundItem,settings.Sound);soundItem.Enabled=sound!=null;SetToggleMark(topItem,TopMost);stopItem.Enabled=Motion.Playing||Motion.Resting;SetToggleMark(fullscreenItem,settings.AutoHide);holdEmoteItem.Enabled=activeEmote!=null;stopEmoteItem.Enabled=activeEmote!=null;stopEffectItem.Enabled=effectWindows.Count>0;SetToggleMark(holdEmoteItem,holdEmote);
            foreach(ToolStripItem entry in menu.Items){ToolStripMenuItem p=entry as ToolStripMenuItem;if(p==null)continue;foreach(ToolStripItem child in p.DropDownItems){ToolStripMenuItem c=child as ToolStripMenuItem;if(c!=null&&c.Tag is int)c.Checked=(int)c.Tag==settings.Scale;}}
        }
        private static void SetToggleMark(ToolStripMenuItem item,bool selected)
        {
            item.Checked=selected;
            // Use the native right-aligned column for status; no shortcut key is assigned.
            item.ShowShortcutKeys=true;item.ShortcutKeyDisplayString=selected?"✓":" ";
        }
        internal void SetSound(bool enabled)
        {
            settings.Sound=enabled&&sound!=null;
            if(!settings.Sound&&sound!=null)sound.Stop();
            // Enabling sound takes effect at the next laugh, so audio never starts out of sync.
            UpdateMenu();Save();
        }
        internal bool Laugh()
        {
            if(HiddenForFullscreen)return false;
            StopEmote(false);
            if(!Motion.Laugh(Now))return false;
            if(sound!=null&&settings.Sound&&!testing)sound.Play();RenderNow(true);return true;
        }
        internal void StopLaugh(){Motion.Stop();if(sound!=null)sound.Stop();RenderNow(true);}
        internal void Tick()
        { TickWithForeground(Native.GetForegroundWindow()); }
        internal void TickWithForeground(IntPtr foreground)
        {
            if(Now>=nextFullscreenCheck){nextFullscreenCheck=Now+.12;CheckFullscreenWindow(foreground,false);}
            if(HiddenForFullscreen)return;
            if(screenEffectActive)
            {
                // Demote on the next tick after the final landing. Other apps can then cover
                // the collage when clicked, while the control pet stays above its effect windows.
                if(!effectSettled&&PendingEffectCount==0&&fallingEffects.Count==0&&effectWindows.Count>0)SettleScreenEffect();
                if(Now>=nextEffectFrame){AdvanceEffects();nextEffectFrame=Now+.025;}
                if(PendingEffectCount>0)BuildEffectBatch();
            }
            Point p=PointToClient(Cursor.Position);bool over=!menuOpen&&surface.Hit(p.X,p.Y);
            bool wasPlaying=Motion.Playing;Motion.Update(Now,over,assets.Duration,assets.BlinkCount/assets.BlinkFps);
            if(wasPlaying&&!Motion.Playing&&sound!=null)sound.Stop();
            RenderNow(false);
        }
        internal void RenderNow(bool force)
        {
            if(!IsHandleCreated||disposing||HiddenForFullscreen)return;
            if(activeEmote!=null)
            {
                int frame=holdEmote?activeEmote.FrameCount-1:activeEmote.FrameAt(Now-emoteStart);string emoteKey="X"+activeEmote.Name+"/"+frame+"/"+(int)((Now-emoteStart)*30)+"/"+settings.Scale;
                if(!force&&emoteKey==lastVisual)return;
                using(Bitmap image=RenderEmote(activeEmote,frame))surface.Update(Handle,image,Location);
                lastVisual=emoteKey;return;
            }
            string key=Motion.Resting?"R":Motion.Playing?"L"+(int)(Motion.Elapsed*assets.Fps):Motion.Blinking?"B"+(Motion.Look>.5?1:0)+"/"+(int)((Motion.Clock-Motion.BlinkStart)*assets.BlinkFps):"E"+(int)Math.Round(Motion.Look*(assets.EyeCount-1));
            if(Motion.Poking||Motion.Moving||Motion.Waking)key+="/M"+(int)(Motion.Clock*60);
            key+="/"+settings.Scale;
            if(!force&&key==lastVisual)return;
            using(Bitmap frame=Renderer.Render(assets,Motion,settings.Scale))surface.Update(Handle,frame,Location);
            lastVisual=key;
        }
        internal void SetAutoHide(bool value)
        {
            settings.AutoHide=value;fullscreenGate.Reset();
            if(!value)ApplyFullscreen(false);else if(!testing)CheckFullscreen(true);
            Save();UpdateMenu();
        }
        private void CheckFullscreen(bool immediate)
        {CheckFullscreenWindow(Native.GetForegroundWindow(),immediate);}
        private void CheckFullscreenWindow(IntPtr foreground,bool immediate)
        {
            if(!ready||menuOpen||gesture.Pressed)return;
            bool detected=settings.AutoHide&&Fullscreen.IsActive(foreground,Handle,Screen.FromRectangle(Bounds).Bounds);
            if(immediate){fullscreenGate.Set(detected,Now);ApplyFullscreen(detected);}
            else if(fullscreenGate.Update(detected,Now))ApplyFullscreen(fullscreenGate.Hidden);
        }
        internal void ApplyFullscreen(bool hidden)
        {
            if(hidden==HiddenForFullscreen)return;
            HiddenForFullscreen=hidden;
            if(hidden)
            {
                gesture.Cancel();Capture=false;Motion.Suspend(Now);if(sound!=null)sound.Stop();
                Hide();timer.Interval=150;
            }
            else
            {
                Motion.Suspend(Now);timer.Interval=16;Show();RenderNow(true);
            }
        }
        private void ReturnHome(){Home();if(!testing)CheckFullscreen(true);if(!HiddenForFullscreen){Show();TopMost=true;BringToFront();TopMost=settings.OnTop;RenderNow(true);}Save();}
        private Size SizeFor(int scale){return new Size(288*scale/100,368*scale/100);}
        private Size SizeFor(Emote emote)
        {
            int height=368*settings.Scale/100;
            return new Size(Math.Max(1,emote.Width*height/emote.Height),height);
        }
        internal void SetSize(int scale)
        {
            Screen screen=Screen.FromRectangle(Bounds);Rectangle area=screen.WorkingArea;
            int center=Left+Width/2,bottom=Top+Height;settings.Scale=Math.Max(50,Math.Min(700,scale));ClientSize=activeEmote==null?SizeFor(settings.Scale):SizeFor(activeEmote);
            Location=new Point(center-Width/2,bottom-Height);KeepVisibleAfterResize(area);RenderNow(true);Save();UpdateMenu();
        }
        internal void Home()
        {
            Rectangle area=Screen.FromPoint(Cursor.Position).WorkingArea;
            Location=new Point(area.Right-Width-24,area.Bottom-Height+(int)(12*settings.Scale/100.0));KeepVisible();
        }
        internal static Point Clamp(Point p,Size size,Rectangle area,int allowance)
        {return new Point(Math.Max(area.Left,Math.Min(p.X,area.Right-size.Width)),Math.Max(area.Top,Math.Min(p.Y,area.Bottom-size.Height+allowance)));}
        private void KeepVisibleAfterResize(Rectangle area)
        {
            int visibleWidth=Math.Min(Width,Math.Min(area.Width,Math.Max(120,(int)Math.Ceiling(Width*.8))));
            int visibleHeight=Math.Min(Height,Math.Min(area.Height,Math.Max(120,(int)Math.Ceiling(Height*.8))));
            Left=Math.Max(area.Left+visibleWidth-Width,Math.Min(Left,area.Right-visibleWidth));
            Top=Math.Max(area.Top+visibleHeight-Height,Math.Min(Top,area.Bottom-visibleHeight));
        }
        private void KeepVisible()
        {
            // Every size may be dragged beyond any edge.  Leave a small visible grip so the pet can always be pulled back.
            Rectangle area=Screen.FromRectangle(Bounds).Bounds;int grip=Math.Max(18,Math.Min(48,Math.Min(Width,Height)/4));
            Location=new Point(Math.Max(area.Left-Width+grip,Math.Min(Left,area.Right-grip)),Math.Max(area.Top-Height+grip,Math.Min(Top,area.Bottom-grip)));
        }
        private void Save(){if(testing)return;settings.X=Left;settings.Y=Top;settings.Save();}
        private void LoadEmotes(string folder)
        {
            string emoteFolder=Path.Combine(folder,"emotes");
            string[] names={"heart","emo_idle","emo_wipe","smoke","umbrella","coffee","sad"};
            foreach(string name in names)try{string path=Path.Combine(emoteFolder,name+".gif");if(!File.Exists(path))path=Path.Combine(emoteFolder,name+".png");if(File.Exists(path)){Emote emote=new Emote(name,path);emotes.Add(name,emote);effectSources.Add(name,MakeEffectSource(emote.Frame(0)));}}catch{}
            effectSources.Add("original",MakeEffectSource(assets.Eye(0)));
            effectSources.Add("laugh",MakeEffectSource(assets.Frame(Math.Min(assets.Count-1,(int)(assets.Fps*1.2)))));
        }
        private static Bitmap MakeEffectSource(Bitmap source)
        {
            int height=Math.Min(source.Height,600),width=Math.Max(1,(int)Math.Round(source.Width*height/(double)source.Height));
            Bitmap result=new Bitmap(width,height,PixelFormat.Format32bppPArgb);
            using(Graphics g=Graphics.FromImage(result)){g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(source,new Rectangle(0,0,width,height));}
            return result;
        }
        private void PlayEmote(string name)
        {
            Emote emote;if(HiddenForFullscreen||!emotes.TryGetValue(name,out emote))return;
            if(sound!=null)sound.Stop();Motion.Suspend(Now);holdEmote=false;activeEmote=emote;emoteStart=Now;
            int center=Left+Width/2,bottom=Top+Height;ClientSize=SizeFor(emote);Location=new Point(center-Width/2,bottom-Height);KeepVisible();RenderNow(true);
        }
        private void StopEmote(bool render)
        {
            if(activeEmote==null)return;activeEmote=null;
            int center=Left+Width/2,bottom=Top+Height;ClientSize=SizeFor(settings.Scale);Location=new Point(center-Width/2,bottom-Height);KeepVisible();
            if(render)RenderNow(true);
        }
        internal void StartScreenEffect(bool mixed)
        {
            string currentAction=activeEmote!=null?activeEmote.Name:Motion.Playing?"laugh":"original";
            StartScreenEffectCore(mixed?null:currentAction,mixed);
        }
        private void StartScreenEffectAction(string action){StartScreenEffectCore(action,false);}
        private void StartScreenEffectCore(string action,bool mixed)
        {
            if(HiddenForFullscreen)return;StopScreenEffect();StopEmote(false);
            screenEffectActive=true;effectSettled=false;scaleBeforeEffect=settings.Scale;locationBeforeEffect=Location;topBeforeEffect=TopMost;
            settings.Scale=85;ClientSize=SizeFor(settings.Scale);Home();TopMost=true;RenderNow(true);
            Rectangle area=Screen.FromPoint(Cursor.Position).Bounds;effectArea=area;
            string[] choices=mixed?new string[]{"original","laugh","heart","emo_idle","emo_wipe","smoke","umbrella","coffee","sad"}:new string[]{action};
            // Build only lightweight placement specifications here; image rotation is batched after the first pets begin falling.
            Random random=new Random(unchecked(Environment.TickCount*397^Guid.NewGuid().GetHashCode()));
            int columns=8,rows=7,baseCount=columns*rows,extraCount=40;
            int index=0,count=baseCount+extraCount;
            for(index=0;index<count;index++)
            {
                int centerX,centerY;
                if(index<baseCount)
                {
                    int column=index%columns,row=rows-1-index/columns;
                    centerX=area.Left+(int)((column+.5)*area.Width/columns+(random.NextDouble()-.5)*area.Width/columns*.72);
                    centerY=area.Top+(int)((row+.5)*area.Height/rows+(random.NextDouble()-.5)*area.Height/rows*.72);
                }
                else
                {
                    centerX=area.Left+(int)(random.NextDouble()*area.Width);
                    centerY=area.Top+(int)(random.NextDouble()*area.Height);
                }
                int layer=Math.Max(0,Math.Min(rows,(area.Bottom-centerY)*rows/area.Height));
                // The grid supplies broad coverage; additional pets provide visibly smaller sizes.
                // Keep every size band in each run instead of relying on one random range.
                int minimumHeight,maximumHeight;
                if(index<baseCount)
                {
                    if(index%4==0){minimumHeight=(int)(area.Height*.43);maximumHeight=Math.Min(760,(int)(area.Height*.52));}
                    else{minimumHeight=(int)(area.Height*.30);maximumHeight=Math.Min(620,(int)(area.Height*.43));}
                }
                else if(index<baseCount+16){minimumHeight=(int)(area.Height*.09);maximumHeight=(int)(area.Height*.16);}
                else if(index<baseCount+32){minimumHeight=(int)(area.Height*.18);maximumHeight=(int)(area.Height*.30);}
                else{minimumHeight=(int)(area.Height*.31);maximumHeight=Math.Min(620,(int)(area.Height*.43));}
                minimumHeight=Math.Max(64,minimumHeight);maximumHeight=Math.Max(minimumHeight+1,maximumHeight);
                pendingEffects.Enqueue(new EffectSpec{Name=choices[random.Next(choices.Length)],Angle=(float)(random.NextDouble()*360),Height=random.Next(minimumHeight,maximumHeight),CenterX=centerX,CenterY=centerY,StartOffset=random.Next(0,Math.Max(1,area.Height/3)),Delay=random.Next(0,90),Duration=1000+layer*100+random.Next(0,220)});
            }
            Show();TopMost=false;TopMost=true;BringToFront();RenderNow(true);
            EffectSpec[] specifications=pendingEffects.ToArray();pendingEffects.Clear();
            Array.Sort(specifications,delegate(EffectSpec a,EffectSpec b){return b.CenterY.CompareTo(a.CenterY);});
            int generation;
            lock(effectQueueLock){generation=++effectGeneration;effectOutstanding=specifications.Length;}
            effectRenderTask=Task.Run(delegate{PrepareEffects(specifications,generation);});
            BuildEffectBatch();
            UpdateMenu();
        }
        private void PrepareEffects(EffectSpec[] specifications,int generation)
        {
            try
            {
                foreach(EffectSpec spec in specifications)
                {
                    while(true)
                    {
                        lock(effectQueueLock){if(generation!=effectGeneration)return;if(preparedEffects.Count<5)break;}
                        Thread.Sleep(3);
                    }
                    Bitmap source;if(!effectSources.TryGetValue(spec.Name,out source)){Interlocked.Decrement(ref effectOutstanding);continue;}
                    Stopwatch measure=Stopwatch.StartNew();Bitmap picture=MakeEffectPicture(source,spec.Height,spec.Angle);
                    EffectPictureMs+=measure.Elapsed.TotalMilliseconds;
                    lock(effectQueueLock)
                    {
                        if(generation==effectGeneration)preparedEffects.Enqueue(new PreparedEffect{Spec=spec,Picture=picture});
                        else{picture.Dispose();return;}
                    }
                }
            }
            catch(Exception error)
            {
                try{Directory.CreateDirectory(Settings.Folder);File.AppendAllText(Path.Combine(Settings.Folder,"error.log"),DateTime.Now.ToString("s")+" full-screen image preparation: "+error+Environment.NewLine);}catch{}
            }
        }
        private void BuildEffectBatch()
        {
            if(!screenEffectActive)return;
            PreparedEffect prepared=null;
            lock(effectQueueLock){if(preparedEffects.Count>0){prepared=preparedEffects.Dequeue();effectOutstanding--;}}
            if(prepared==null)return;
            EffectSpec spec=prepared.Spec;Bitmap picture=prepared.Picture;Stopwatch measure=Stopwatch.StartNew();
            Point target=new Point(spec.CenterX-picture.Width/2,spec.CenterY-picture.Height/2);
            EffectWindow effect=new EffectWindow(picture,target,effectArea.Top-picture.Height-spec.StartOffset,Now+spec.Delay/1000.0,spec.Duration,spec.Height<effectArea.Height*.18);
            effectWindows.Add(effect);fallingEffects.Add(effect);effect.Show();
            EffectShowMs+=measure.Elapsed.TotalMilliseconds;EffectMaxShowMs=Math.Max(EffectMaxShowMs,measure.Elapsed.TotalMilliseconds);
            if(effectWindows.Count%4==0||PendingEffectCount==0)
                foreach(EffectWindow window in effectWindows)if(window.SmallForeground)window.BringToFront();
            BringToFront();
        }
        private void AdvanceEffects()
        {
            Stopwatch measure=Stopwatch.StartNew();
            double now=Now;
            for(int i=fallingEffects.Count-1;i>=0;i--)
                if(fallingEffects[i].Advance(now))fallingEffects.RemoveAt(i);
            EffectMoveMs+=measure.Elapsed.TotalMilliseconds;EffectMaxMoveMs=Math.Max(EffectMaxMoveMs,measure.Elapsed.TotalMilliseconds);
        }
        private static Bitmap MakeEffectPicture(Bitmap source,int height,float angle)
        {
            float scale=height/(float)source.Height;float width=source.Width*scale;float radians=(float)(Math.PI*angle/180.0);
            int side=(int)Math.Ceiling(Math.Abs(width*(float)Math.Cos(radians))+Math.Abs(height*(float)Math.Sin(radians)))+4;
            int side2=(int)Math.Ceiling(Math.Abs(width*(float)Math.Sin(radians))+Math.Abs(height*(float)Math.Cos(radians)))+4;
            Bitmap result=new Bitmap(side,side2,PixelFormat.Format32bppPArgb);
            using(Graphics g=Graphics.FromImage(result)){g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBilinear;g.TranslateTransform(side/2f,side2/2f);g.RotateTransform(angle);g.DrawImage(source,new RectangleF(-width/2,-height/2,width,height));}
            return result;
        }
        private void SettleScreenEffect()
        {
            foreach(EffectWindow window in effectWindows)window.TopMost=false;
            TopMost=false;
            BringToFront();
            effectSettled=true;
            UpdateMenu();
        }
        internal void StopScreenEffect()
        {
            pendingEffects.Clear();fallingEffects.Clear();
            lock(effectQueueLock)
            {
                effectGeneration++;effectOutstanding=0;
                while(preparedEffects.Count>0)preparedEffects.Dequeue().Picture.Dispose();
            }
            if(effectRenderTask!=null){try{effectRenderTask.Wait(1000);}catch{}effectRenderTask=null;}
            foreach(EffectWindow window in effectWindows)window.Dispose();effectWindows.Clear();effectSettled=false;
            if(screenEffectActive)
            {
                settings.Scale=scaleBeforeEffect;ClientSize=SizeFor(settings.Scale);Location=locationBeforeEffect;TopMost=topBeforeEffect;screenEffectActive=false;KeepVisible();
            }
            if(!HiddenForFullscreen&&!IsDisposed){Show();RenderNow(true);}UpdateMenu();
        }
        private Bitmap RenderEmote(Emote emote,int index)
        {
            Size size=SizeFor(emote);Bitmap result=new Bitmap(size.Width,size.Height,PixelFormat.Format32bppPArgb);
            double seconds=Now-emoteStart,phase=seconds*2.2;float bob=emote.FrameCount>1?0:(float)(Math.Sin(phase)*4*settings.Scale/100.0),breath=emote.FrameCount>1?1f:1f+(float)(Math.Sin(phase)*.014);
            using(Graphics g=Graphics.FromImage(result))
            {
                g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                g.TranslateTransform(size.Width*.5f,size.Height*.5f+bob);g.ScaleTransform(breath,breath);
                g.DrawImage(emote.Frame(index),new Rectangle(-size.Width/2,-size.Height/2,size.Width,size.Height));
            }return result;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if(e.Button==MouseButtons.Right){Motion.EndDrag(Now);gesture.Cancel();Capture=false;menu.Show(this,e.Location);return;}
            if(e.Button!=MouseButtons.Left)return;
            gesture.Down(PointToScreen(e.Location));lastDragPointer=gesture.Origin;dragWindow=Location;Capture=true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);if(!gesture.Pressed)return;Point p=PointToScreen(e.Location);gesture.Move(p);
            if(gesture.Dragged)
            {
                if(!Motion.Holding){Motion.BeginDrag(Now);if(sound!=null)sound.Stop();}
                Motion.Drag(Now,p.X-lastDragPointer.X);lastDragPointer=p;
                Location=new Point(dragWindow.X+p.X-gesture.Origin.X,dragWindow.Y+p.Y-gesture.Origin.Y);RenderNow(true);
            }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);if(e.Button!=MouseButtons.Left||!gesture.Pressed)return;
            bool moved=gesture.Dragged,twice=gesture.Up(PointToScreen(e.Location),Now);Capture=false;
            if(moved){Motion.EndDrag(Now);KeepVisible();RenderNow(true);Save();}
            else if(twice)Laugh();
            else if(Motion.Resting){Motion.Stand(Now);RenderNow(true);}
            else if(Motion.Poke(Now,e.X*100.0/settings.Scale*288.0/assets.Width,e.Y*100.0/settings.Scale*368.0/assets.Height))RenderNow(true);
        }
        protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture&&gesture.Pressed){Motion.EndDrag(Now);gesture.Cancel();KeepVisible();RenderNow(true);Save();}}
        protected override void OnFormClosing(FormClosingEventArgs e){Save();timer.Stop();StopScreenEffect();if(sound!=null)sound.Stop();base.OnFormClosing(e);}
        protected override void Dispose(bool disposingNow)
        {
            if(disposingNow&&!disposing){disposing=true;foreach(EffectWindow window in effectWindows)window.Dispose();effectWindows.Clear();timer.Dispose();if(tray!=null){tray.Visible=false;tray.Dispose();}if(menu!=null){Font f=menu.Font;menu.Dispose();f.Dispose();}if(sound!=null){sound.Stop();sound.Dispose();}foreach(Bitmap source in effectSources.Values)source.Dispose();foreach(Emote emote in emotes.Values)emote.Dispose();surface.Dispose();if(petIcon!=null)petIcon.Dispose();}
            base.Dispose(disposingNow);
        }
    }

    internal static class Tests
    {
        private static void Check(bool value,string name,List<string> log){if(!value)throw new Exception("FAIL: "+name);log.Add("PASS: "+name);}
        private static void Advance(Motion m,double begin,double end,bool over,Assets a)
        {for(double t=begin;t<=end+.00001;t+=1.0/60)m.Update(t,over,a.Duration,a.BlinkCount/a.BlinkFps);}
        private static bool Equal(Bitmap a,Bitmap b,int fromY,int toY)
        {for(int y=fromY;y<toY;y++)for(int x=0;x<a.Width;x++)if(a.GetPixel(x,y)!=b.GetPixel(x,y))return false;return true;}
        internal static void Run(string folder)
        {
            Directory.CreateDirectory(folder);List<string> log=new List<string>();
            try
            {
                using(Assets a=new Assets(AppDomain.CurrentDomain.BaseDirectory))
                {
                    using(Emote heart=new Emote("heart",Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"emotes","heart.png")))
                    {Check(heart.FrameCount==1&&heart.Width>0&&heart.Height>0&&heart.Duration==12,"heart emote loads",log);}
                    string[] staticEmotes={"emo_idle","emo_wipe","smoke","umbrella","coffee","sad"};
                    foreach(string name in staticEmotes)using(Emote emote=new Emote(name,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"emotes",name+".png")))
                    {Check(emote.FrameCount==1&&emote.Width>0&&emote.Height>0&&emote.Duration==12,name+" emote loads",log);}
                    Check(EffectWindow.FallY(-300,700,0,1.2)==-300&&EffectWindow.FallY(-300,700,1.2,1.2)==700&&EffectWindow.FallY(-300,700,.6,1.2)<700,"gravity fall reaches its pile target",log);
                    Check(a.Count==422&&Math.Abs(a.Fps-30)<.01,"original 422-frame clip at 30 fps",log);
                    Check(a.Eye(0).GetPixel(0,0).A==0&&a.Eye(1).GetPixel(0,0).A==0,"transparent idle and hover backgrounds",log);
                    int differences=0,outsideEyes=0;
                    for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++)if(a.Eye(0).GetPixel(x,y)!=a.Eye(1).GetPixel(x,y)){differences++;if(y>92)outsideEyes++;}
                    Check(differences>10&&outsideEyes==0,"hover changes only eye area, body is identical",log);
                    byte[] wav=File.ReadAllBytes(a.AudioPath);Check(wav.Length>1000&&Encoding.ASCII.GetString(wav,0,4)=="RIFF","original soundtrack decodes as WAV",log);
                    double blinkDuration=a.BlinkCount/a.BlinkFps;
                    Check(a.BlinkCount==7&&a.BlinkFps==30,"blink uses seven original video frames",log);
                    for(int bank=0;bank<2;bank++)for(int f=0;f<a.BlinkCount;f++)Check(Equal(a.Eye(bank),a.Blink(bank,f),92,a.Height),"blink body preserved, bank "+bank+" frame "+f,log);
                    Check(!Equal(a.Eye(0),a.Blink(0,3),0,92),"closed eye differs from open eye",log);
                    Motion m=new Motion();Advance(m,0,.3,true,a);Check(m.Look>.999,"hover turns eyes toward pointer",log);
                    Advance(m,.32,.62,false,a);Check(m.Look<.001,"mouse leave restores original eyes",log);
                    Check(m.Laugh(1)&&!m.Laugh(1.01)&&m.PlayCount==1,"double-click playback does not overlap or restart",log);
                    m.Update(1+a.Duration-.05,false,a.Duration,blinkDuration);Check(m.Playing,"complete source clip plays through ending",log);
                    m.Update(1+a.Duration+.01,false,a.Duration,blinkDuration);Check(!m.Playing&&m.Resting,"laugh ends in resting pose",log);
                    m.Update(100,true,a.Duration,blinkDuration);Check(m.Resting&&!m.Blinking,"resting pose stays until clicked",log);
                    m.Stand(100);Check(!m.Resting&&m.Waking,"single click starts standing up",log);Advance(m,100,100.3,false,a);Check(!m.Waking&&!m.Playing,"standing settles to original idle",log);
                    m.Laugh(101);m.Update(101+a.Duration+.001,false,a.Duration,blinkDuration);Check(m.Laugh(120)&&!m.Resting,"can laugh again from resting pose",log);m.Stop();Check(!m.Playing&&!m.Resting,"stop action cancels playback",log);
                    m=new Motion();m.Update(4,false,a.Duration,blinkDuration);Check(m.Blinking,"idle schedules a blink",log);
                    m.Update(4+blinkDuration+.01,false,a.Duration,blinkDuration);Check(!m.Blinking&&m.NextBlink>=m.Clock+4&&m.NextBlink<=m.Clock+8,"blink ends and schedules random 4-8 second interval",log);
                    m.Poke(5,143,211);Check(m.Poking&&!m.Blinking,"poke interrupts blink",log);m.Update(5.1,false,a.Duration,blinkDuration);
                    using(Bitmap warped=Renderer.WarpBelly(a.Eye(0),m.PokeX,m.PokeY,m.PokeAmount))
                    {Check(Equal(warped,a.Eye(0),0,105),"belly feedback preserves face",log);Check(!Equal(warped,a.Eye(0),160,260),"belly feedback visibly deforms source pixels",log);}
                    Advance(m,5.11,6,false,a);Check(!m.Poking&&Math.Abs(m.PokeAmount)<.0001,"belly rebound settles",log);
                    Check(!m.Poke(6,125,67),"clicking face does not poke belly",log);
                    m.Poke(6,144,210);m.Laugh(6.1);Check(!m.Poking&&m.Playing,"double click cancels poke before original laugh",log);
                    m.BeginDrag(7);Check(m.Holding&&!m.Playing,"drag interrupts playback",log);
                    for(int i=1;i<=30;i++){m.Drag(7+i/60.0,12);m.Update(7+i/60.0,true,a.Duration,blinkDuration);}
                    Check(m.DragAngle>2&&m.DragAngle<=7,"drag tilts toward movement and stays bounded",log);
                    m.EndDrag(7.5);m.Update(7.57,true,a.Duration,blinkDuration);Check(!m.Holding&&m.Landing&&m.Squash>.01,"release starts landing bounce",log);
                    Advance(m,7.58,9,false,a);Check(!m.Landing&&Math.Abs(m.DragAngle)<.05,"drag and landing settle cleanly",log);
                    Gesture gesture=new Gesture();Point p=new Point(100,100);gesture.Down(p);Check(!gesture.Up(p,1),"single click does not laugh",log);gesture.Down(p);Check(gesture.Up(p,1.12),"double click triggers laugh",log);
                    gesture.Down(p);gesture.Move(new Point(140,160));Check(!gesture.Up(new Point(140,160),2),"drag does not trigger laugh",log);
                    gesture.Down(p);Check(!gesture.Up(p,2.1),"drag resets double-click sequence",log);gesture.Cancel();Check(!gesture.Pressed,"cancelled pointer capture releases drag",log);
                    Point clamped=PetWindow.Clamp(new Point(-3000,-50),new Size(245,312),new Rectangle(-1920,0,1920,1040),10);Check(clamped.X==-1920&&clamped.Y==0,"negative-coordinate monitor bounds",log);
                    Rectangle monitor=new Rectangle(0,0,1920,1080);
                    Check(Fullscreen.Covers(monitor,monitor,0,"FullscreenPlayer"),"borderless fullscreen is detected",log);
                    Check(!Fullscreen.Covers(monitor,monitor,0x00c00000,"NormalWindow"),"ordinary maximized window is ignored",log);
                    Check(!Fullscreen.Covers(new Rectangle(0,0,1920,1040),monitor,0,"Video"),"window that leaves taskbar visible is not fullscreen",log);
                    Check(!Fullscreen.Covers(new Rectangle(-1920,0,1920,1080),monitor,0,"Video"),"fullscreen on another monitor is ignored",log);
                    Check(Fullscreen.Covers(new Rectangle(-1920,0,1920,1080),new Rectangle(-1920,0,1920,1080),0,"Video"),"fullscreen works on negative-coordinate monitor",log);
                    Check(!Fullscreen.Covers(monitor,monitor,0,"WorkerW"),"desktop background is not fullscreen app",log);
                    FullscreenGate gate=new FullscreenGate();Check(!gate.Update(true,0)&&!gate.Update(false,.08)&&!gate.Hidden,"brief fullscreen switch does not flash pet",log);
                    gate.Update(true,1);Check(gate.Update(true,1.2)&&gate.Hidden,"stable fullscreen hides pet",log);gate.Update(false,2);Check(gate.Update(false,2.3)&&!gate.Hidden,"leaving fullscreen restores pet",log);
                    using(PetWindow form=new PetWindow(a,true))
                    {
                        form.Show();Application.DoEvents();form.RenderNow(true);
                        Check(Native.IsWindowVisible(form.Handle),"native pet window is visible",log);
                        int style=Native.GetWindowLong(form.Handle,-20);Check((style&0x80000)!=0&&(style&0x80)!=0&&(style&0x08000000)!=0,"alpha transparency, no taskbar entry, no focus stealing",log);
                        form.SetSize(65);Check(form.Width==288*65/100&&form.Height==368*65/100,"small size matches original scale",log);form.SetSize(110);Check(form.Width==288*110/100,"large size matches original scale",log);form.SetSize(150);Check(form.Width==288*150/100,"pro size matches original scale",log);form.SetSize(200);Check(form.Width==288*200/100,"max size matches original scale",log);form.SetSize(300);Check(form.Width==288*300/100,"pro max size matches original scale",log);form.SetSize(600);Check(form.Width==288*600/100,"600 percent size matches original scale",log);form.SetSize(700);Check(form.Width==288*700/100,"700 percent size matches original scale",log);form.SetSize(85);
                        form.Location=new Point(-50000,-50000);form.SetSize(85);Check(Screen.FromRectangle(form.Bounds).WorkingArea.IntersectsWith(form.Bounds),"lost window position recovers",log);
                        form.SetSound(true);Check(form.SoundEnabled,"original audio switch can be enabled",log);form.SetSound(false);Check(!form.SoundEnabled,"sound switch can be muted",log);
                        IntPtr mousePoint=(IntPtr)((160<<16)|120);
                        Native.SendMessage(form.Handle,0x201,(IntPtr)1,mousePoint);Native.SendMessage(form.Handle,0x202,IntPtr.Zero,mousePoint);
                        Check(!form.Motion.Playing&&form.Motion.Poking,"native single belly click triggers poke",log);
                        Native.SendMessage(form.Handle,0x203,(IntPtr)1,mousePoint);Native.SendMessage(form.Handle,0x202,IntPtr.Zero,mousePoint);
                        Check(form.Motion.Playing&&!form.Motion.Poking,"native double-click events replace poke with playback",log);form.StopLaugh();
                        form.Motion.Laugh(0);form.Motion.Update(a.Duration,false,a.Duration,blinkDuration);Check(form.Motion.Resting,"native window reaches resting state",log);
                        IntPtr footPoint=(IntPtr)((240<<16)|170);
                        Native.SendMessage(form.Handle,0x201,(IntPtr)1,footPoint);Native.SendMessage(form.Handle,0x202,IntPtr.Zero,footPoint);
                        Check(!form.Motion.Resting&&form.Motion.Waking,"native click wakes resting pet",log);form.StopLaugh();
                        Native.SendMessage(form.Handle,0x201,(IntPtr)1,mousePoint);
                        Native.SendMessage(form.Handle,0x200,(IntPtr)1,(IntPtr)((180<<16)|155));Check(form.Motion.Holding,"native drag starts feedback",log);
                        Native.SendMessage(form.Handle,0x202,IntPtr.Zero,mousePoint);Check(!form.Motion.Holding&&form.Motion.Landing&&!form.Motion.Playing,"native drag release bounces without laughing",log);form.StopLaugh();
                        using(Form fullscreen=new Form())
                        {
                            fullscreen.FormBorderStyle=FormBorderStyle.None;fullscreen.StartPosition=FormStartPosition.Manual;fullscreen.ShowInTaskbar=false;fullscreen.Opacity=.01;
                            fullscreen.Bounds=Screen.FromRectangle(form.Bounds).Bounds;fullscreen.Show();Application.DoEvents();
                            Check(Fullscreen.IsActive(fullscreen.Handle,form.Handle,fullscreen.Bounds),"native borderless fullscreen detection",log);
                            Check(!Fullscreen.IsActive(form.Handle,form.Handle,fullscreen.Bounds),"pet window is excluded from fullscreen detection",log);
                            // Supply the actual test HWND; sandbox desktops cannot take foreground focus.
                            form.TickWithForeground(fullscreen.Handle);Thread.Sleep(220);form.TickWithForeground(fullscreen.Handle);Check(form.HiddenForFullscreen&&!Native.IsWindowVisible(form.Handle),"native fullscreen detection automatically hides pet through timer",log);
                            fullscreen.Hide();Check(!Fullscreen.IsActive(fullscreen.Handle,form.Handle,fullscreen.Bounds),"hidden window is excluded from fullscreen detection",log);
                            Thread.Sleep(180);form.TickWithForeground(fullscreen.Handle);Thread.Sleep(300);form.TickWithForeground(fullscreen.Handle);
                            Check(!form.HiddenForFullscreen&&Native.IsWindowVisible(form.Handle),"leaving fullscreen automatically restores pet through timer",log);fullscreen.Close();
                        }
                        Point savedLocation=form.Location;form.Laugh();form.ApplyFullscreen(true);
                        Check(form.HiddenForFullscreen&&!Native.IsWindowVisible(form.Handle)&&!form.Motion.Playing,"fullscreen hides native pet and interrupts playback",log);
                        Check(!form.Laugh(),"hidden pet cannot play audio or laugh",log);
                        form.ApplyFullscreen(false);Check(!form.HiddenForFullscreen&&Native.IsWindowVisible(form.Handle)&&form.Location==savedLocation,"fullscreen exit restores same position",log);
                        form.Motion.Laugh(0);form.Motion.Update(a.Duration,false,a.Duration,blinkDuration);form.ApplyFullscreen(true);form.ApplyFullscreen(false);
                        Check(form.Motion.Resting,"resting pose survives fullscreen hide and restore",log);
                        form.ApplyFullscreen(true);form.SetAutoHide(false);Check(Native.IsWindowVisible(form.Handle)&&!form.HiddenForFullscreen,"disabling automatic hide reveals pet",log);form.StopLaugh();
                        // Fill the bounded cache before measuring steady-state resource use.
                        for(int i=0;i<12;i++)a.Frame(i);
                        int before=Native.GetGuiResources(Process.GetCurrentProcess().Handle,0);Stopwatch sw=Stopwatch.StartNew();
                        form.Motion.Laugh(0);
                        for(int i=0;i<a.Count;i++){form.Motion.Elapsed=i/a.Fps;form.RenderNow(true);}
                        sw.Stop();int after=Native.GetGuiResources(Process.GetCurrentProcess().Handle,0);
                        log.Add("METRIC: all-frame decode/render average "+(sw.Elapsed.TotalMilliseconds/a.Count).ToString("F2")+" ms; GDI delta "+(after-before));
                        // GDI+ lazily initializes renderer resources on its first complete playback.
                        for(int i=0;i<a.Count;i++){form.Motion.Elapsed=i/a.Fps;form.RenderNow(true);}
                        int stable=Native.GetGuiResources(Process.GetCurrentProcess().Handle,0);
                        log.Add("METRIC: repeat-playback GDI delta "+(stable-after));
                        Check(stable-after<10,"repeat playback does not leak GDI handles",log);Check(a.CacheCount<=12,"decoded frame cache stays bounded",log);
                        form.Motion.Stop();form.RenderNow(true);Stopwatch pileClock=Stopwatch.StartNew();form.StartScreenEffect(false);
                        while(!form.EffectComplete&&pileClock.Elapsed.TotalSeconds<12){Application.DoEvents();form.Tick();Thread.Sleep(12);}
                        log.Add("METRIC: full-screen pile completion "+pileClock.Elapsed.TotalSeconds.ToString("F2")+" s; windows "+form.EffectCount+"; pending "+form.PendingEffectCount);
                        log.Add("METRIC: picture "+form.EffectPictureMs.ToString("F0")+" ms; window show "+form.EffectShowMs.ToString("F0")+" ms; movement "+form.EffectMoveMs.ToString("F0")+" ms");
                        log.Add("METRIC: max show "+form.EffectMaxShowMs.ToString("F1")+" ms; max movement frame "+form.EffectMaxMoveMs.ToString("F1")+" ms");
                        Check(form.EffectBottomFirst,"bottom row is generated before upper rows",log);
                        Check(form.EffectComplete,"all falling pets reach the pile",log);
                        Rectangle pileArea=Screen.FromRectangle(form.Bounds).Bounds;
                        using(Bitmap screenshot=new Bitmap(pileArea.Width,pileArea.Height))
                        {
                            using(Graphics capture=Graphics.FromImage(screenshot))capture.CopyFromScreen(pileArea.Location,Point.Empty,pileArea.Size);
                            screenshot.Save(Path.Combine(folder,"full-screen-pile.png"),ImageFormat.Png);
                        }
                        form.Tick();
                        Check(form.EffectSettled&&!form.TopMost&&!form.EffectWindowsTopMost,"finished pile lets other windows come forward",log);
                        Check(form.Left>=pileArea.Left&&form.Right<=pileArea.Right&&form.Top>=pileArea.Top&&form.Bottom<=pileArea.Bottom,"control pet remains on the selected screen",log);
                        form.StopScreenEffect();
                        form.Motion.Stop();form.RenderNow(true);form.Close();Check(form.IsDisposed,"window closes cleanly",log);
                    }
                    using(Icon icon=Renderer.CreateIcon(a.Eye(0)))using(FileStream f=File.Create(Path.Combine(folder,"naiwa.ico")))icon.Save(f);
                    m=new Motion();
                    using(Bitmap b=Renderer.Render(a,m,100))b.Save(Path.Combine(folder,"native-idle.png"),ImageFormat.Png);
                    m.Look=1;using(Bitmap b=Renderer.Render(a,m,100))b.Save(Path.Combine(folder,"native-hover.png"),ImageFormat.Png);
                    m.Laugh(0);m.Elapsed=1;using(Bitmap b=Renderer.Render(a,m,100))b.Save(Path.Combine(folder,"native-laugh.png"),ImageFormat.Png);
                    m=new Motion();m.Poke(0,144,211);m.Update(.10,false,a.Duration,blinkDuration);using(Bitmap b=Renderer.Render(a,m,100))b.Save(Path.Combine(folder,"native-poke.png"),ImageFormat.Png);
                    m.BeginDrag(1);m.DragAngle=6;using(Bitmap b=Renderer.Render(a,m,100))b.Save(Path.Combine(folder,"native-drag.png"),ImageFormat.Png);
                    m=new Motion();m.BlinkStart=0;m.Clock=.1;using(Bitmap b=Renderer.Render(a,m,100))b.Save(Path.Combine(folder,"native-blink.png"),ImageFormat.Png);
                    m.Stop();m.Resting=true;using(Bitmap b=Renderer.Render(a,m,100))b.Save(Path.Combine(folder,"native-rest.png"),ImageFormat.Png);
                }
                log.Add("RESULT: PASS");
            }
            catch(Exception e){log.Add(e.ToString());log.Add("RESULT: FAIL");Environment.ExitCode=1;}
            File.WriteAllLines(Path.Combine(folder,"self-test.txt"),log.ToArray(),Encoding.UTF8);
        }
        internal static void Demo(string folder)
        {
            Directory.CreateDirectory(folder);using(Assets a=new Assets(AppDomain.CurrentDomain.BaseDirectory))
            {
                Motion m=new Motion();m.NextBlink=.6;
                for(int i=0;i<720;i++)
                {
                    double t=i/30.0;
                    if(i==60)m.Poke(t,143,205);
                    if(i==90)m.BeginDrag(t);
                    if(i>=90&&i<126)m.Drag(t,i<108?5:-5);
                    if(i==126)m.EndDrag(t);
                    if(i==174)m.Laugh(t);
                    if(i==666)m.Stand(t);
                    m.Update(t,t>=1.1&&t<5.8,a.Duration,a.BlinkCount/a.BlinkFps);
                    using(Bitmap b=Renderer.Render(a,m,100))b.Save(Path.Combine(folder,i.ToString("0000")+".png"),ImageFormat.Png);
                }
            }
        }
    }
}
