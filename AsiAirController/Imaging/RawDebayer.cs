using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AsiAirController.Imaging;

// ZWO ASI2600MC Duo — Sony IMX571, RGGB Bayer, X=0 Y=0 origin
public static class RawDebayer
{
    private const int SensorWidth  = 6248;
    private const int SensorHeight = 4176;
    private const int Scale        = 4;
    private const int OutWidth     = SensorWidth  / Scale;  // 1562
    private const int OutHeight    = SensorHeight / Scale;  // 1044

    public static Bitmap Debayer(byte[] rawData)
    {
        if (rawData.Length < SensorWidth * SensorHeight * 2)
            throw new ArgumentException(
                $"Expected ≥{SensorWidth * SensorHeight * 2:N0} bytes, got {rawData.Length:N0}");

        // Sample each channel independently — per-channel stretch avoids color casts
        // from differing sensor sensitivity per color.
        int sampleCount = (OutWidth / 4) * (OutHeight / 4);
        var rS = new List<int>(sampleCount);
        var gS = new List<int>(sampleCount);
        var bS = new List<int>(sampleCount);
        for (int oy = 0; oy < OutHeight; oy += 4)
        for (int ox = 0; ox < OutWidth;  ox += 4)
        {
            int ix = ox * Scale, iy = oy * Scale;
            rS.Add(Read16(rawData, ix,     iy));
            gS.Add((Read16(rawData, ix + 1, iy) + Read16(rawData, ix, iy + 1)) >> 1);
            bS.Add(Read16(rawData, ix + 1, iy + 1));
        }
        rS.Sort(); gS.Sort(); bS.Sort();

        int rBlack = rS[Math.Max(0, (int)(rS.Count * 0.005))];
        int rWhite = rS[Math.Min(rS.Count - 1, (int)(rS.Count * 0.995))];
        int gBlack = gS[Math.Max(0, (int)(gS.Count * 0.005))];
        int gWhite = gS[Math.Min(gS.Count - 1, (int)(gS.Count * 0.995))];
        int bBlack = bS[Math.Max(0, (int)(bS.Count * 0.005))];
        int bWhite = bS[Math.Min(bS.Count - 1, (int)(bS.Count * 0.995))];
        if (rWhite <= rBlack) rWhite = rBlack + 1;
        if (gWhite <= gBlack) gWhite = gBlack + 1;
        if (bWhite <= bBlack) bWhite = bBlack + 1;

        // Debayer at 1/4 scale; pack BGRA8888
        var pixels = new byte[OutWidth * OutHeight * 4];
        for (int oy = 0; oy < OutHeight; oy++)
        {
            int iy       = oy * Scale;
            int row0Base = iy * SensorWidth * 2;
            int row1Base = (iy + 1) * SensorWidth * 2;
            int dstBase  = oy * OutWidth * 4;

            for (int ox = 0; ox < OutWidth; ox++)
            {
                int ix  = ox * Scale;
                int ix0 = ix * 2;
                int ix1 = (ix + 1) * 2;

                // RGGB quad: (ix,iy)=R  (ix+1,iy)=G1  (ix,iy+1)=G2  (ix+1,iy+1)=B
                int r  = rawData[row0Base + ix0]     | (rawData[row0Base + ix0 + 1] << 8);
                int g1 = rawData[row0Base + ix1]     | (rawData[row0Base + ix1 + 1] << 8);
                int g2 = rawData[row1Base + ix0]     | (rawData[row1Base + ix0 + 1] << 8);
                int b  = rawData[row1Base + ix1]     | (rawData[row1Base + ix1 + 1] << 8);
                int g  = (g1 + g2) >> 1;

                int d = dstBase + ox * 4;
                pixels[d]     = Stretch(b, bBlack, bWhite);
                pixels[d + 1] = Stretch(g, gBlack, gWhite);
                pixels[d + 2] = Stretch(r, rBlack, rWhite);
                pixels[d + 3] = 255;
            }
        }

        var bmp = new WriteableBitmap(
            new PixelSize(OutWidth, OutHeight),
            new Vector(96, 96),
            PixelFormats.Bgra8888,
            AlphaFormat.Opaque);

        using var fb = bmp.Lock();
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, fb.Address, pixels.Length);
        return bmp;
    }

    private static int Read16(byte[] data, int x, int y)
    {
        int o = (y * SensorWidth + x) * 2;
        return data[o] | (data[o + 1] << 8);
    }

    private static byte Stretch(int v, int black, int white)
    {
        int s = (v - black) * 255 / (white - black);
        return (byte)(s < 0 ? 0 : s > 255 ? 255 : s);
    }
}
