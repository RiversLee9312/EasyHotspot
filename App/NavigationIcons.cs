using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI;

namespace EasyHotspot;

public static class NavigationIcons
{
    const int LogicalSize=20;
    record Bounds(uint Width,uint Height,int Left,int Top,int Right,int Bottom);
    static readonly ConcurrentDictionary<string,Task<Bounds>> bounds=new();
    static string PathFor(string name)=>Path.Combine(AppContext.BaseDirectory,"Assets",name+".png");

    public static ImageIcon Create(string name)
    {
        var icon=new ImageIcon{Width=LogicalSize,Height=LogicalSize,UseLayoutRounding=true};
        XamlRoot? subscribed=null;long generation=0;double lastScale=0;Color lastColor=default;
        async Task Refresh()
        {
            if(icon.XamlRoot is null)return;
            var scale=icon.XamlRoot.RasterizationScale;
            var color=(icon.Foreground as SolidColorBrush)?.Color??Microsoft.UI.Colors.White;
            if(icon.Source is not null&&scale==lastScale&&color==lastColor)return;
            var current=++generation;
            try {
                using var bitmap=await RasterizeAsync(name,scale,color);
                var source=new SoftwareBitmapSource();await source.SetBitmapAsync(bitmap);
                if(current!=generation){source.Dispose();return;}
                var old=icon.Source as IDisposable;icon.Source=source;old?.Dispose();lastScale=scale;lastColor=color;
            }catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException) {
                if(current==generation)icon.Source=new BitmapImage(new Uri(PathFor(name)));
            }
        }
        void Changed(XamlRoot sender,XamlRootChangedEventArgs args)=>_ = Refresh();
        icon.Loaded+=(_,_)=>{subscribed=icon.XamlRoot;subscribed.Changed+=Changed;_ = Refresh();};
        icon.Unloaded+=(_,_)=>{if(subscribed is not null)subscribed.Changed-=Changed;subscribed=null;++generation;};
        icon.ActualThemeChanged+=(_,_)=>_ = Refresh();
        icon.RegisterPropertyChangedCallback(IconElement.ForegroundProperty,(_,_)=>_ = Refresh());
        return icon;
    }

    public static async Task<SoftwareBitmap> RasterizeAsync(string name,double scale,Color color)
    {
        if(name is not ("hotspot" or "proxy" or "settings"))throw new ArgumentException(nameof(name));
        if(!double.IsFinite(scale)||scale<=0||scale>8)throw new ArgumentOutOfRangeException(nameof(scale));
        var b=await bounds.GetOrAdd(name,ReadBounds);
        int size=(int)Math.Ceiling(LogicalSize*scale),padding=Math.Max(1,(int)Math.Round(scale));
        double factor=(size-2.0*padding)/Math.Max(b.Right-b.Left,b.Bottom-b.Top);
        uint width=(uint)Math.Max(1,Math.Round(b.Width*factor)),height=(uint)Math.Max(1,Math.Round(b.Height*factor));
        double sx=(double)width/b.Width,sy=(double)height/b.Height;
        int left=Math.Max(0,(int)Math.Floor(b.Left*sx)-1),top=Math.Max(0,(int)Math.Floor(b.Top*sy)-1);
        int right=Math.Min((int)width,(int)Math.Ceiling(b.Right*sx)+1),bottom=Math.Min((int)height,(int)Math.Ceiling(b.Bottom*sy)+1);
        using var stream=await (await StorageFile.GetFileFromPathAsync(PathFor(name))).OpenReadAsync();
        var decoder=await BitmapDecoder.CreateAsync(stream);
        var transform=new BitmapTransform{ScaledWidth=width,ScaledHeight=height,InterpolationMode=BitmapInterpolationMode.Fant,Bounds=new(){X=(uint)left,Y=(uint)top,Width=(uint)(right-left),Height=(uint)(bottom-top)}};
        var data=(await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Straight,transform,ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.DoNotColorManage)).DetachPixelData();
        var output=new byte[size*size*4];int cropWidth=right-left,cropHeight=bottom-top;
        int startX=(size-cropWidth)/2,startY=(size-cropHeight)/2;
        for(int y=0;y<cropHeight;y++)for(int x=0;x<cropWidth;x++) {
            int dx=x+startX,dy=y+startY;if(dx<0||dy<0||dx>=size||dy>=size)continue;
            int from=(y*cropWidth+x)*4,to=(dy*size+dx)*4;
            int alpha=data[from+3]*color.A/255;
            output[to]=(byte)(color.B*alpha/255);output[to+1]=(byte)(color.G*alpha/255);output[to+2]=(byte)(color.R*alpha/255);output[to+3]=(byte)alpha;
        }
        return SoftwareBitmap.CreateCopyFromBuffer(output.AsBuffer(),BitmapPixelFormat.Bgra8,size,size,BitmapAlphaMode.Premultiplied);
    }
    static async Task<Bounds> ReadBounds(string name)
    {
        using var stream=await (await StorageFile.GetFileFromPathAsync(PathFor(name))).OpenReadAsync();
        var decoder=await BitmapDecoder.CreateAsync(stream);
        var pixels=(await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Straight,new BitmapTransform(),ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.DoNotColorManage)).DetachPixelData();
        int width=(int)decoder.PixelWidth,height=(int)decoder.PixelHeight,left=width,top=height,right=0,bottom=0;
        // Measure visible artwork, retaining its original alpha when rendering. Files stay unchanged.
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)if(pixels[(y*width+x)*4+3]>=32){left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x+1);bottom=Math.Max(bottom,y+1);}
        if(right<=left||bottom<=top)throw new IOException("Empty navigation icon: "+name);
        return new(decoder.PixelWidth,decoder.PixelHeight,left,top,right,bottom);
    }
}
