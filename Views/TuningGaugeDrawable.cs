namespace StringTuner.Views;

public sealed class TuningGaugeDrawable(Func<double?> centsProvider, Func<Color> colorProvider) : IDrawable
{
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var left = 18f;
        var right = dirtyRect.Width - 18f;
        var width = right - left;
        var centerY = dirtyRect.Height / 2;
        canvas.FillColor = Color.FromArgb("#33413F");
        canvas.FillRectangle(left, centerY - 2, width, 4);

        foreach (var cents in new[] { -50, -25, 0, 25, 50 })
        {
            var x = left + (cents + 50) / 100f * width;
            canvas.StrokeColor = cents == 0 ? Color.FromArgb("#D6E0D8") : Color.FromArgb("#71807B");
            canvas.StrokeSize = cents == 0 ? 2 : 1;
            var tickHeight = cents == 0 ? 25 : cents % 50 == 0 ? 17 : 11;
            canvas.DrawLine(x, centerY - tickHeight / 2f, x, centerY + tickHeight / 2f);
        }

        var measuredCents = centsProvider();
        var markerX = measuredCents is double centsValue
            ? left + (float)(Math.Clamp(centsValue, -50, 50) + 50) / 100f * width
            : left + width / 2;
        canvas.FillColor = colorProvider();
        canvas.FillCircle(markerX, centerY, 8);
        canvas.StrokeColor = Color.FromArgb("#17211F");
        canvas.StrokeSize = 3;
        canvas.DrawCircle(markerX, centerY, 8);
        if (measuredCents is null)
        {
            canvas.FillColor = Color.FromArgb("#17211F");
            canvas.FillCircle(markerX, centerY, 3);
        }
    }
}