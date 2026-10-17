using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace EasyHotspot;

public sealed class ActionPanel:Panel
{
    const double Gap=12;
    protected override Size MeasureOverride(Size availableSize)
    {
        double width=double.IsInfinity(availableSize.Width)?double.MaxValue:availableSize.Width;
        double x=0,y=0,rowHeight=0,usedWidth=0;
        foreach(var child in Children) {
            child.Measure(new(width,double.PositiveInfinity));var size=child.DesiredSize;
            if(x>0&&x+size.Width>width){usedWidth=Math.Max(usedWidth,x-Gap);y+=rowHeight+Gap;x=0;rowHeight=0;}
            x+=size.Width+Gap;rowHeight=Math.Max(rowHeight,size.Height);
        }
        return new(Math.Min(width,Math.Max(usedWidth,Math.Max(0,x-Gap))),y+rowHeight);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        double x=0,y=0,rowHeight=0;
        foreach(var child in Children) {
            var size=child.DesiredSize;double width=Math.Min(size.Width,finalSize.Width);
            if(x>0&&x+width>finalSize.Width){y+=rowHeight+Gap;x=0;rowHeight=0;}
            child.Arrange(new Rect(x,y,width,size.Height));x+=width+Gap;rowHeight=Math.Max(rowHeight,size.Height);
        }
        return finalSize;
    }
}
