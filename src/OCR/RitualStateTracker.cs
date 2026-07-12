using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RuneshapePriceChecker.Configuration;

namespace RuneshapePriceChecker.OCR;

public enum RitualCellState
{
    Empty,
    Unread,
    Read
}

public sealed class RitualStateTracker
{
    private readonly IOptionsMonitor<OcrOptions> _options;
    private readonly ILogger<RitualStateTracker> _logger;
    private readonly RitualCellState[,] _grid = new RitualCellState[10, 10];
    private long _lastSignature = 0;
    private bool _gridInitialized = false;

    public RitualStateTracker(IOptionsMonitor<OcrOptions> options, ILogger<RitualStateTracker> logger)
    {
        _options = options;
        _logger = logger;
    }

    public RitualCellState[,] GetGridState()
    {
        var copy = new RitualCellState[10, 10];
        Array.Copy(_grid, copy, 100);
        return copy;
    }

    public void MarkCellRead(int col, int row)
    {
        if (col >= 0 && col < 10 && row >= 0 && row < 10)
        {
            _grid[col, row] = RitualCellState.Read;
        }
    }

    public bool TryUpdateGridFromCapture(Bitmap captureBitmap, Rectangle regionBounds)
    {
        var bounds = _options.CurrentValue.RitualRegionBounds;
        if (bounds is not { Length: 4 }) return false;

        var gridRect = new Rectangle(bounds[0], bounds[1], bounds[2], bounds[3]);
        
        // Translate grid rect to capture bitmap coordinates
        var localGridRect = new Rectangle(
            gridRect.X - regionBounds.X,
            gridRect.Y - regionBounds.Y,
            gridRect.Width,
            gridRect.Height);

        // Ensure the grid is fully within the capture bitmap
        if (localGridRect.X < 0 || localGridRect.Y < 0 ||
            localGridRect.Right > captureBitmap.Width || localGridRect.Bottom > captureBitmap.Height)
        {
            return false;
        }

        long signature = 0;
        var cellW = localGridRect.Width / 10f;
        var cellH = localGridRect.Height / 10f;

        var data = captureBitmap.LockBits(localGridRect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var stride = data.Stride;
            var bytes = new byte[Math.Abs(stride) * localGridRect.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);

            for (var row = 0; row < 10; row++)
            {
                for (var col = 0; col < 10; col++)
                {
                    var cellX = (int)(col * cellW);
                    var cellY = (int)(row * cellH);
                    var cW = (int)cellW;
                    var cH = (int)cellH;

                    var hasItem = CheckCellHasItem(bytes, Math.Abs(stride), cellX, cellY, cW, cH);
                    if (hasItem)
                    {
                        signature |= 1L << ((row * 10) + col);
                    }
                }
            }
        }
        finally
        {
            captureBitmap.UnlockBits(data);
        }

        if (signature != _lastSignature)
        {
            _logger.LogInformation("Ritual grid signature changed from {Old} to {New}. Resetting grid state.", _lastSignature, signature);
            _lastSignature = signature;
            ResetGrid(signature);
            _gridInitialized = true;
            return true;
        }

        return _gridInitialized;
    }

    private void ResetGrid(long signature)
    {
        for (var row = 0; row < 10; row++)
        {
            for (var col = 0; col < 10; col++)
            {
                var bit = 1L << ((row * 10) + col);
                var hasItem = (signature & bit) != 0;
                _grid[col, row] = hasItem ? RitualCellState.Unread : RitualCellState.Empty;
            }
        }
    }

    private static bool CheckCellHasItem(byte[] data, int stride, int x, int y, int w, int h)
    {
        // Simple heuristic: Count non-dark pixels in the center of the cell
        var cx = x + (w / 2);
        var cy = y + (h / 2);
        var radius = Math.Min(w, h) / 4;
        
        var brightCount = 0;
        var totalCount = 0;

        for (var ry = cy - radius; ry <= cy + radius; ry++)
        {
            var rowOffset = ry * stride;
            for (var rx = cx - radius; rx <= cx + radius; rx++)
            {
                var b = data[rowOffset + (rx * 3)];
                var g = data[rowOffset + (rx * 3) + 1];
                var r = data[rowOffset + (rx * 3) + 2];

                var brightness = (r * 0.299) + (g * 0.587) + (b * 0.114);
                if (brightness > 40) // Threshold for empty dark cell vs item icon
                {
                    brightCount++;
                }
                totalCount++;
            }
        }

        return ((double)brightCount / totalCount) > 0.1; // If more than 10% of center is bright, it has an item
    }
}
