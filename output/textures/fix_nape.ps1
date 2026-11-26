Add-Type -AssemblyName System.Drawing
$code=@"
using System;using System.Drawing;using System.Drawing.Imaging;
public class SeamFix{public static void Run(string path){using(var b=new Bitmap(path)){int w=b.Width,h=b.Height;for(int y=(int)(h*.15);y<(int)(h*.24);y++){int yy=(int)Math.Round(h*1.033665)-y;if(yy>=h)continue;for(int x=(int)(w*.84);x<(int)(w*.91);x++){var a=b.GetPixel(x,y);var c=b.GetPixel(x,yy);if(Math.Min(a.R,c.R)>184)continue;Color d;if(Math.Abs(a.R-c.R)>15)d=a.R<c.R?a:c;else d=Color.FromArgb((a.R+c.R)/2,(a.G+c.G)/2,(a.B+c.B)/2);b.SetPixel(x,y,d);b.SetPixel(x,yy,d);}}b.Save(path+".fixed.png",ImageFormat.Png);}}}
"@
Add-Type -TypeDefinition $code -ReferencedAssemblies System.Drawing
[SeamFix]::Run('C:\Users\VISYON\Documents\MusicGame\output\textures\skinTest_variation_v7_rapat.png')

