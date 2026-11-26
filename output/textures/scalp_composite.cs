using System;using System.Drawing;using System.Drawing.Imaging;using System.Runtime.InteropServices;
public class ScalpEdit{
public static string Run(string s,string t,string o){using(var b=new Bitmap(s))using(var tile=new Bitmap(t))using(var dst=b.Clone(new Rectangle(0,0,b.Width,b.Height),PixelFormat.Format32bppArgb)){
int w=b.Width,h=b.Height;var d=dst.LockBits(new Rectangle(0,0,w,h),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);byte[] a=new byte[d.Stride*h];Marshal.Copy(d.Scan0,a,0,a.Length);var orig=(byte[])a.Clone();long changes=0;
for(int y=(int)(h*.15);y<(int)(h*.87);y++)for(int x=(int)(w*.595);x<(int)(w*.895);x++){double u=(double)x/w,v=(double)y/h;if(v>.435&&v<.595&&u>.814)continue;int p=y*d.Stride+x*4;double sum=0;int n=0;for(int dy=-8;dy<=8;dy+=2)for(int dx=-8;dx<=8;dx+=2){sum+=orig[(y+dy)*d.Stride+(x+dx)*4+2];n++;}double alpha=Math.Max(0,Math.Min(1,(191-sum/n)/27));if(alpha==0)continue;var c=tile.GetPixel((int)(x*2.2)%tile.Width,(int)(y*2.2)%tile.Height);double[] tar={c.B*.73,c.G*.73,c.R*.73};for(int k=0;k<3;k++)a[p+k]=(byte)Math.Max(0,Math.Min(255,Math.Round(orig[p+k]*(1-alpha)+tar[k]*alpha)));changes++;}
long protectedDiff=0;for(int y=0;y<h;y++)for(int x=0;x<w;x++){double u=(double)x/w,v=(double)y/h;if(v<.15||u>.90||(u>.814&&v>.435&&v<.595)){int p=y*d.Stride+x*4;for(int k=0;k<4;k++)if(a[p+k]!=orig[p+k]){protectedDiff++;break;}}}
Marshal.Copy(a,0,d.Scan0,a.Length);dst.UnlockBits(d);dst.Save(o,ImageFormat.Png);return w+"x"+h+"; scalp modified="+changes+"; protected differences="+protectedDiff;}}}

