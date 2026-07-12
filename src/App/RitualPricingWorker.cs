using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RuneshapePriceChecker.App.Dashboard;
using RuneshapePriceChecker.Configuration;
using RuneshapePriceChecker.Contracts;
using RuneshapePriceChecker.OCR;
using RuneshapePriceChecker.Overlay;

namespace RuneshapePriceChecker.App;

public sealed class RitualPricingWorker(
    RitualStateTracker tracker,
    IPoe2WindowResolutionProvider windowResolutionProvider,
    IOptionsMonitor<OcrOptions> options,
    RitualOverlayRenderer renderer,
    ILoggerFactory loggerFactory) : BackgroundService
{
    private readonly ILogger<RitualPricingWorker> _logger = loggerFactory.CreateLogger<RitualPricingWorker>();
    private readonly OcrCaptureStrategy _captureStrategy = new(loggerFactory.CreateLogger<OcrCaptureStrategy>());

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var scanInterval = options.CurrentValue.ScanIntervalMs;
                var bounds = options.CurrentValue.RitualRegionBounds;

                if (bounds is { Length: 4 } &&
                    windowResolutionProvider.IsPoe2WindowForeground && 
                    windowResolutionProvider.CurrentWindowCaptureContext is not null)
                {
                    var gridRect = new Rectangle(bounds[0], bounds[1], bounds[2], bounds[3]);
                    
                    // Capture just the grid area to check if it's open
                    var region = new OcrCaptureRegion(gridRect.X, gridRect.Y, gridRect.Width, gridRect.Height);
                    using var captureResult = _captureStrategy.Capture(region, windowResolutionProvider.CurrentWindowCaptureContext, options.CurrentValue);
                    using var bmp = captureResult.Bitmap;

                    var isOpen = tracker.TryUpdateGridFromCapture(bmp, gridRect);
                    
                    if (isOpen)
                    {
                        if (GetCursorPos(out var pt))
                        {
                            if (gridRect.Contains(pt.X, pt.Y))
                            {
                                var col = (pt.X - gridRect.X) / (gridRect.Width / 10);
                                var row = (pt.Y - gridRect.Y) / (gridRect.Height / 10);
                                
                                // TODO: OCR the tooltip and mark as read when successful
                                // tracker.MarkCellRead(col, row);
                            }
                        }

                        renderer.Render(tracker.GetGridState(), gridRect);
                    }
                    else
                    {
                        renderer.Hide();
                    }
                }
                else
                {
                    renderer.Hide();
                }
                
                await Task.Delay(scanInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in RitualPricingWorker");
                await Task.Delay(1000, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}

