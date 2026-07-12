using System;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RuneshapePriceChecker.OCR;

public static class BitmapDataExtensions
{
    public static byte[] CopyPixels(this BitmapData data)
    {
        var absStride = Math.Abs(data.Stride);
        var length = absStride * data.Height;
        var bytes = new byte[length];

        for (var y = 0; y < data.Height; y++)
        {
            Marshal.Copy(data.Scan0 + (y * data.Stride), bytes, y * absStride, absStride);
        }

        return bytes;
    }

    public static void CopyPixelsTo(this BitmapData data, byte[] destBytes)
    {
        var absStride = Math.Abs(data.Stride);
        for (var y = 0; y < data.Height; y++)
        {
            Marshal.Copy(data.Scan0 + (y * data.Stride), destBytes, y * absStride, absStride);
        }
    }
}
