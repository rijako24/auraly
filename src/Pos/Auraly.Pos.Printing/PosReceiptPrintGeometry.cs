namespace Auraly.Pos.Printing;

public static class PosReceiptPrintGeometry
{
    public const int QrWidthMillimeters = 42;

    public static int NearestThermalPixelsPerModule(
        int modules,
        double targetCssWidth,
        double captureScale)
    {
        if (modules <= 0) throw new ArgumentOutOfRangeException(nameof(modules));
        if (!double.IsFinite(targetCssWidth) || targetCssWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetCssWidth));
        if (!double.IsFinite(captureScale) || captureScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(captureScale));
        return Math.Max(1, (int)Math.Round(
            targetCssWidth * captureScale / modules,
            MidpointRounding.AwayFromZero));
    }
}
