using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RuneshapePriceChecker.Configuration;
using RuneshapePriceChecker.OCR;

namespace RuneshapePriceChecker.Overlay;

public sealed class RitualOverlayRenderer : IDisposable
{
    private readonly IOptionsMonitor<OcrOptions> _options;
    private readonly IOptionsMonitor<AppOptions> _appOptions;
    private readonly ILogger<RitualOverlayRenderer> _logger;
    private readonly object _sync = new();
    
    private Thread? _overlayThread;
    private RitualOverlayForm? _overlayForm;
    private bool _disposed;

    public RitualOverlayRenderer(
        IOptionsMonitor<OcrOptions> options,
        IOptionsMonitor<AppOptions> appOptions,
        ILogger<RitualOverlayRenderer> logger)
    {
        _options = options;
        _appOptions = appOptions;
        _logger = logger;
    }

    public void Render(RitualCellState[,] grid, Rectangle captureBounds)
    {
        if (_appOptions.CurrentValue.AllOverlaysDisabled)
        {
            Hide();
            return;
        }

        var bounds = _options.CurrentValue.RitualRegionBounds;
        if (bounds is not { Length: 4 })
        {
            Hide();
            return;
        }

        var gridRect = new Rectangle(bounds[0], bounds[1], bounds[2], bounds[3]);

        EnsureOverlayThreadStarted();
        
        lock (_sync)
        {
            if (_overlayForm is not null && !_overlayForm.IsDisposed)
            {
                _overlayForm.UpdateGrid(grid, gridRect);
            }
        }
    }

    public void Hide()
    {
        lock (_sync)
        {
            if (_overlayForm is not null && !_overlayForm.IsDisposed)
            {
                _overlayForm.SafeHide();
            }
        }
    }

    private void EnsureOverlayThreadStarted()
    {
        lock (_sync)
        {
            if (_overlayThread is { IsAlive: true }) return;
            
            _overlayThread = new Thread(() =>
            {
                using var form = new RitualOverlayForm();
                lock (_sync) _overlayForm = form;
                Application.Run(form);
                lock (_sync) _overlayForm = null;
            })
            {
                IsBackground = true,
                Name = "RuneshapePriceChecker-RitualOverlay"
            };
            
            _overlayThread.SetApartmentState(ApartmentState.STA);
            _overlayThread.Start();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        lock (_sync)
        {
            if (_overlayForm is not null && !_overlayForm.IsDisposed)
            {
                _overlayForm.Invoke(() => _overlayForm.Close());
            }
        }
    }

    private sealed class RitualOverlayForm : OverlayFormBase
    {
        protected override bool ClickThrough => true;

        private RitualCellState[,] _grid = new RitualCellState[10, 10];
        private Rectangle _gridRect;

        public void UpdateGrid(RitualCellState[,] grid, Rectangle gridRect)
        {
            if (IsDisposed) return;

            if (InvokeRequired)
            {
                _ = BeginInvoke(new Action<RitualCellState[,], Rectangle>(UpdateGrid), grid, gridRect);
                return;
            }

            _grid = grid;
            _gridRect = gridRect;
            
            // Inflate bounds slightly to draw border
            var newBounds = gridRect;
            newBounds.Inflate(4, 4);
            
            if (Bounds != newBounds)
            {
                Bounds = newBounds;
            }

            if (!Visible)
            {
                Show();
                PinTopMost();
            }

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (_gridRect.Width <= 0 || _gridRect.Height <= 0) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var localGridX = _gridRect.X - Bounds.X;
            var localGridY = _gridRect.Y - Bounds.Y;

            var cellW = _gridRect.Width / 10f;
            var cellH = _gridRect.Height / 10f;

            using var unreadPen = new Pen(Color.Red, 2f);
            using var readPen = new Pen(Color.LimeGreen, 2f);

            for (var row = 0; row < 10; row++)
            {
                for (var col = 0; col < 10; col++)
                {
                    var state = _grid[col, row];
                    if (state == RitualCellState.Empty) continue;

                    var rect = new RectangleF(
                        localGridX + (col * cellW),
                        localGridY + (row * cellH),
                        cellW,
                        cellH);

                    // Shrink the box slightly so it fits inside the cell
                    rect.Inflate(-2, -2);

                    var pen = state == RitualCellState.Read ? readPen : unreadPen;
                    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                }
            }
        }
    }
}
