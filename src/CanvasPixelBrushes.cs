namespace CardReview;
public sealed partial class ReviewForm
{
    static readonly string[] pixelBrushModes={"方形","圆形","菱形","抖动","喷点"};
    static readonly int[] pixelDitherMatrix={0,8,2,10,12,4,14,6,3,11,1,9,15,7,13,5};
    int canvasPixelBrushMode,canvasPixelStrokeSeed;
    float canvasPixelDensity=50;

    static bool PixelBrushCovers(int mode,int x,int y,int side,int worldX,int worldY,float density,int seed)
    {
        float dx=x-(side-1)/2f,dy=y-(side-1)/2f,r=side/2f;
        if(mode is 1 or 4 && dx*dx+dy*dy>r*r)return false;
        if(mode==2&&Math.Abs(dx)+Math.Abs(dy)>r)return false;
        if(mode==3)return pixelDitherMatrix[(worldY&3)*4+(worldX&3)]<density*16/100f;
        if(mode==4)
        {
            // World-space noise remains stable across stamps and clipped previews.
            uint hash=unchecked((uint)worldX*374761393u+(uint)worldY*668265263u+(uint)seed*2246822519u);
            hash=unchecked((hash^(hash>>13))*1274126177u);hash^=hash>>16;
            return (hash&0xffffff)/16777216f<density/100f;
        }
        return true;
    }
}
