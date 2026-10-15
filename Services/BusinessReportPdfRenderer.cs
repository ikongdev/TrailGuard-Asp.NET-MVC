using System.Globalization;
using System.Text;
using SkiaSharp;
using TrailGuard.Models;

namespace TrailGuard.Services;

public interface IBusinessReportPdfRenderer
{
    byte[] Render(BusinessReportData report);
}

/// <summary>Request-scoped, data-only PDF rendering for the admin business report.</summary>
public sealed class BusinessReportPdfRenderer : IBusinessReportPdfRenderer
{
    private const float PageWidth = 612;
    private const float PageHeight = 792;
    private const float Margin = 42;
    private readonly IWebHostEnvironment _environment;

    public BusinessReportPdfRenderer(IWebHostEnvironment environment) => _environment = environment;

    public byte[] Render(BusinessReportData report)
    {
        using var stream = new MemoryStream();
        using var document = SKDocument.CreatePdf(stream);
        using var regular = LoadTypeface("Poppins-Regular.ttf");
        using var medium = LoadTypeface("Poppins-Medium.ttf");
        using var bold = LoadTypeface("Poppins-Bold.ttf");
        var writer = new PdfWriter(document, regular, medium, bold);
        writer.Title("TrailGuard Business & Operational Report");
        writer.Paragraph($"Selected period: {report.Filter.StartDate:MMMM d, yyyy} - {report.Filter.EndDate:MMMM d, yyyy}");
        writer.Paragraph($"Scope: {ScopeText(report)}");
        writer.Paragraph($"Generated: {report.GeneratedAt:MMMM d, yyyy h:mm tt}");
        writer.Paragraph("Event and operational metrics select events by Event Date. Registration submissions select persisted attempts by Registered At; attempts may differ from deduplicated event metrics.", small: true);
        writer.Heading("Summary");
        writer.KeyValues([("Total events", report.TotalEvents.ToString()), ("Completed events", report.CompletedEvents.ToString()), ("Accepted registrations", report.AcceptedRegistrations.ToString()), ("Unique registered participants", report.UniqueRegisteredParticipants.ToString()), ("Closed-event completion rate", report.ClosedEventCompletionRate is null ? "-" : $"{report.ClosedEventCompletionRate}%")]);
        writer.Paragraph("Unique registered participants are distinct users with a latest attempt in Pending, Awaiting Payment, For Payment Verification, or Accepted for an event in this report. Accepted registrations are registrations, not attendance; check-in is not implemented.", small: true);
        writer.Heading("Charts");
        writer.DrawLineChart(report.SubmissionTrend, "Registration submissions");
        writer.DrawBarChart(report.TrailChart, report.TrailChart.IsAggregated ? "Accepted registrations by trail - Top 8 plus Other" : "Accepted registrations by trail - all trails");
        writer.DrawDoughnut(report.EventStatusDistribution.Select(x => (x.Status, x.Count)).ToList(), "Event status distribution");
        writer.Heading("Trail performance");
        writer.Table(["Trail", "Events", "Completed", "Accepted", "Avg. accepted / completed"], report.TrailPerformance.Select(x => new[] { x.Trail, x.TotalEvents.ToString(), x.CompletedEvents.ToString(), x.AcceptedRegistrations.ToString(), x.AverageAcceptedPerCompletedEvent?.ToString("0.00") ?? "-" }));
        writer.Heading("Event performance");
        writer.Table(["Event / trail", "Organizer", "Date", "Status", "Capacity", "Accepted", "Occupied"], report.EventPerformance.Select(x => new[] { $"{x.EventTitle}\n{x.Trail}", x.Organizer, x.EventDate.ToString("MMM d, yyyy"), x.Status, x.Capacity.ToString(), x.AcceptedRegistrations.ToString(), x.OccupancyPercent is null ? $"{x.ActiveOccupiedSlots} (-)" : $"{x.ActiveOccupiedSlots} ({x.OccupancyPercent:0.#}%)" }));
        writer.Heading("Registration status breakdown");
        writer.Table(["Status", "Latest attempts"], report.StatusBreakdown.Select(x => new[] { x.Status, x.Count.ToString() }));
        writer.Heading("Payment overview");
        writer.KeyValues([("Verified paid", report.VerifiedPaid.ToString()), ("Awaiting payment", report.AwaitingPayment.ToString()), ("Awaiting payment verification", report.AwaitingPaymentVerification.ToString()), ("Contradictory legacy payment records", report.PaymentDataExceptions.ToString())]);
        writer.Paragraph("Verified paid means IsPaid is true and the latest status is Accepted. The two awaiting counts require IsPaid false and their named latest status. IsPaid true outside Accepted is excluded from these counts and reported as a contradictory legacy record. Revenue is not reported because there is no verified structured payment-amount field.", small: true);
        writer.Heading("Operational summary");
        writer.KeyValues([("Upcoming", report.UpcomingEvents.ToString()), ("Completed", report.CompletedEvents.ToString()), ("Cancelled", report.CancelledEvents.ToString()), ("Past-dated Upcoming events requiring manual closure", report.ManualClosureEvents.ToString())]);
        writer.Paragraph("Historical reports use current stored statuses for selected records; they do not reconstruct status as of a past date. Active occupied slots use the latest attempt and the existing active-status policy; cancelled events occupy zero slots.", small: true);
        writer.Finish();
        document.Close();
        return stream.ToArray();
    }

    private SKTypeface LoadTypeface(string fileName)
    {
        var path = Path.Combine(_environment.ContentRootPath, "wwwroot", "css", "fonts", fileName);
        return SKTypeface.FromFile(path) ?? throw new InvalidOperationException($"The bundled PDF font '{fileName}' is unavailable.");
    }

    private static string ScopeText(BusinessReportData report) => $"Trail: {report.TrailScope}; Organizer: {report.OrganizerScope}";

    private sealed class PdfWriter
    {
        private const float TableLineHeight = 10;
        private readonly SKDocument _document;
        private readonly SKTypeface _regular;
        private readonly SKTypeface _medium;
        private readonly SKTypeface _bold;
        private readonly SKColor _ink = new(30, 41, 59);
        private readonly SKColor _muted = new(75, 85, 99);
        private readonly SKColor _accent = new(124, 58, 237);
        private SKCanvas _canvas = null!;
        private int _page;
        private float _y;

        public PdfWriter(SKDocument document, SKTypeface regular, SKTypeface medium, SKTypeface bold)
        {
            _document = document;
            _regular = regular;
            _medium = medium;
            _bold = bold;
            NewPage();
        }

        public void Title(string value) => DrawWrapped(value, 19, _bold, _accent, PageWidth - 2 * Margin, 23, after: 4);

        public void Heading(string value)
        {
            Ensure(30);
            _y += 8;
            DrawWrapped(value, 13, _bold, _ink, PageWidth - 2 * Margin, 16, after: 2);
        }

        public void Paragraph(string value, bool small = false) => DrawWrapped(value, small ? 8 : 9.5f, _regular, _muted, PageWidth - 2 * Margin, 12, after: 3);

        public void KeyValues(IEnumerable<(string Label, string Value)> rows)
        {
            const float labelWidth = 238;
            const float valueWidth = PageWidth - 2 * Margin - labelWidth;
            foreach (var (label, value) in rows)
            {
                var labels = BusinessReportPdfTextLayout.Wrap(label, _medium, 9, labelWidth);
                var values = BusinessReportPdfTextLayout.Wrap(value, _bold, 11, valueWidth);
                var rowLines = Math.Max(labels.Count, values.Count);
                var index = 0;
                while (index < rowLines)
                {
                    var capacity = AvailableLines(TableLineHeight, 7);
                    if (capacity == 0) { NewPage(); continue; }
                    var take = Math.Min(capacity, rowLines - index);
                    var height = take * TableLineHeight + 7;
                    DrawClippedLines(labels, index, take, Margin, _y, labelWidth, height, 9, _medium, _muted, TableLineHeight);
                    DrawClippedLines(values, index, take, Margin + labelWidth, _y, valueWidth, height, 11, _bold, _ink, TableLineHeight);
                    _y += height;
                    index += take;
                }
            }
            _y += 3;
        }

        public void Table(string[] headers, IEnumerable<string[]> rows)
        {
            var values = rows.ToList();
            if (values.Count == 0) { Paragraph("No records for the selected scope."); return; }

            var widths = Enumerable.Repeat((PageWidth - 2 * Margin) / headers.Length, headers.Length).ToArray();
            var headerLines = headers.Select((header, column) => BusinessReportPdfTextLayout.Wrap(header, _bold, 7.3f, widths[column] - 7)).ToArray();
            var headerHeight = Math.Max(17, headerLines.Max(lines => lines.Count) * TableLineHeight + 7);
            var wrappedRows = values.Select(row => row.Select((value, column) => BusinessReportPdfTextLayout.Wrap(value, _regular, 7.2f, widths[column] - 7)).ToArray()).ToList();
            var freshRowHeight = ContentBottom - Margin - headerHeight;
            var firstRowHeight = wrappedRows[0].Max(lines => lines.Count) * TableLineHeight + 7;
            if (firstRowHeight <= freshRowHeight && _y + headerHeight + firstRowHeight > ContentBottom) NewPage();
            DrawHeader();
            foreach (var wrapped in wrappedRows)
            {
                var rowLines = wrapped.Max(lines => lines.Count);
                var completeRowHeight = rowLines * TableLineHeight + 7;
                if (completeRowHeight <= freshRowHeight && _y + completeRowHeight > ContentBottom)
                {
                    NewPage();
                    DrawHeader();
                }
                var index = 0;
                while (index < rowLines)
                {
                    var capacity = AvailableLines(TableLineHeight, 7);
                    if (capacity == 0) { NewPage(); DrawHeader(); continue; }
                    var take = Math.Min(capacity, rowLines - index);
                    var height = take * TableLineHeight + 7;
                    var x = Margin;
                    for (var column = 0; column < headers.Length; column++)
                    {
                        using var border = new SKPaint { Color = new SKColor(203, 213, 225), Style = SKPaintStyle.Stroke, StrokeWidth = .5f };
                        _canvas.DrawRect(x, _y, widths[column], height, border);
                        DrawClippedLines(wrapped[column], index, take, x + 3, _y, widths[column] - 7, height, 7.2f, _regular, _ink, TableLineHeight);
                        x += widths[column];
                    }
                    _y += height;
                    index += take;
                    if (index < rowLines) { NewPage(); DrawHeader(continuedRow: true); }
                }
            }
            _y += 7;

            void DrawHeader(bool continuedRow = false)
            {
                Ensure(headerHeight + (continuedRow ? 13 : 3));
                var x = Margin;
                using var paint = new SKPaint { Color = new SKColor(237, 233, 254) };
                _canvas.DrawRect(Margin, _y, PageWidth - 2 * Margin, headerHeight, paint);
                for (var column = 0; column < headers.Length; column++)
                {
                    DrawClippedLines(headerLines[column], 0, headerLines[column].Count, x + 3, _y, widths[column] - 7, headerHeight, 7.3f, _bold, _ink, TableLineHeight);
                    x += widths[column];
                }
                _y += headerHeight;
                if (continuedRow)
                {
                    Text("Continued row", 6.5f, _medium, _muted);
                    _y += 10;
                }
            }
        }

        public void DrawLineChart(IReadOnlyList<BusinessReportTrendPoint> points, string title)
        {
            ChartFrame(title, 160, out var frame);
            var plot = new SKRect(frame.Left + 32, frame.Top + 16, frame.Right - 16, frame.Bottom - 38);
            var max = Math.Max(1, points.DefaultIfEmpty().Max(point => point?.Count ?? 0));
            DrawLineChartScale(plot, max);
            DrawDateTicks(points, plot);
            if (points.Count == 0 || points.All(point => point.Count == 0))
            {
                Text("No submissions in selected period.", 9, _regular, _muted, plot.Left + 8, plot.MidY);
                EndChart();
                return;
            }
            using var builder = new SKPathBuilder();
            using var paint = new SKPaint { Color = _accent, Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
            for (var index = 0; index < points.Count; index++)
            {
                var x = plot.Left + (points.Count == 1 ? plot.Width / 2 : index * plot.Width / (points.Count - 1));
                var y = plot.Bottom - points[index].Count / (float)max * plot.Height;
                if (index == 0) builder.MoveTo(x, y); else builder.LineTo(x, y);
                using var dot = new SKPaint { Color = _accent };
                _canvas.DrawCircle(x, y, 2.3f, dot);
            }
            using var path = builder.Detach();
            _canvas.DrawPath(path, paint);
            EndChart();
        }

        public void DrawBarChart(BusinessReportTrailChart chart, string title)
        {
            const float labelWidth = 150;
            var rows = chart.Entries.Select(entry => new { Entry = entry, Lines = BusinessReportPdfTextLayout.Wrap(entry.Label, _regular, 7.3f, labelWidth - 8) }).ToList();
            var contentHeight = Math.Max(48, 19 + rows.Sum(row => Math.Max(14, row.Lines.Count * 8 + 4)));
            ChartFrame(title, contentHeight, out var plot);
            if (rows.Count == 0) { Text("No trails for this period.", 9, _regular, _muted, plot.Left + 5, plot.MidY); EndChart(); return; }

            Text($"Full-scope accepted registrations: {chart.FullScopeAcceptedRegistrations}", 7.5f, _medium, _muted, plot.Left + 4, plot.Top + 10);
            var maximum = Math.Max(1, rows.Max(row => row.Entry.Count));
            var barLeft = plot.Left + labelWidth;
            var barWidth = plot.Right - barLeft - 25;
            var y = plot.Top + 15;
            foreach (var row in rows)
            {
                var rowHeight = Math.Max(14, row.Lines.Count * 8 + 4);
                DrawClippedLines(row.Lines, 0, row.Lines.Count, plot.Left + 3, y, labelWidth - 8, rowHeight, 7.3f, _regular, _ink, 8);
                var width = row.Entry.Count / (float)maximum * barWidth;
                using var bar = new SKPaint { Color = row.Entry.IsOther ? _muted : _accent };
                _canvas.DrawRect(barLeft, y + 3, width, Math.Max(4, rowHeight - 7), bar);
                Text(row.Entry.Count.ToString(), 7.3f, _bold, _ink, plot.Right - 20, y + Math.Min(rowHeight - 3, 10));
                y += rowHeight;
            }
            EndChart();
        }

        public void DrawDoughnut(IReadOnlyList<(string Label, int Count)> values, string title)
        {
            const float legendWidth = 290;
            var legend = values.Select(value => new { Value = value, Lines = BusinessReportPdfTextLayout.Wrap(value.Label, _regular, 7.5f, legendWidth - 28) }).ToList();
            var contentHeight = Math.Max(128, legend.Sum(item => Math.Max(14, item.Lines.Count * 8 + 4)) + 10);
            ChartFrame(title, contentHeight, out var plot);
            var total = values.Sum(value => value.Count);
            if (total == 0) { Text("No events for this period.", 9, _regular, _muted, plot.Left + 5, plot.MidY); EndChart(); return; }
            var colors = new[] { new SKColor(124, 58, 237), new SKColor(16, 185, 129), new SKColor(245, 158, 11), new SKColor(100, 116, 139), new SKColor(239, 68, 68) };
            var rect = new SKRect(plot.Left + 20, plot.Top + 4, plot.Left + 130, plot.Top + 114);
            var start = -90f;
            for (var index = 0; index < values.Count; index++)
            {
                var sweep = values[index].Count / (float)total * 360;
                using var paint = new SKPaint { Color = colors[index % colors.Length], Style = SKPaintStyle.Fill };
                _canvas.DrawArc(rect, start, sweep, true, paint);
                start += sweep;
            }
            using (var hole = new SKPaint { Color = SKColors.White }) _canvas.DrawCircle(rect.MidX, rect.MidY, 32, hole);
            var y = plot.Top + 8;
            for (var index = 0; index < legend.Count; index++)
            {
                var rowHeight = Math.Max(14, legend[index].Lines.Count * 8 + 4);
                using var paint = new SKPaint { Color = colors[index % colors.Length] };
                _canvas.DrawRect(plot.Left + 160, y - 5, 7, 7, paint);
                DrawClippedLines(legend[index].Lines, 0, legend[index].Lines.Count, plot.Left + 172, y - 8, legendWidth - 28, rowHeight, 7.5f, _regular, _ink, 8);
                Text(legend[index].Value.Count.ToString(), 7.5f, _bold, _ink, plot.Right - 18, y + 2);
                y += rowHeight;
            }
            EndChart();
        }

        public void Finish() => Footer();

        private void ChartFrame(string title, float contentHeight, out SKRect plot)
        {
            var titleLines = BusinessReportPdfTextLayout.Wrap(title, _bold, 10, PageWidth - 2 * Margin);
            var titleHeight = titleLines.Count * 13 + 6;
            Ensure(titleHeight + contentHeight + 8);
            foreach (var line in titleLines) { Text(line, 10, _bold, _ink); _y += 13; }
            _y += 6;
            using var background = new SKPaint { Color = new SKColor(248, 250, 252) };
            _canvas.DrawRect(Margin, _y, PageWidth - 2 * Margin, contentHeight, background);
            plot = new SKRect(Margin, _y, PageWidth - Margin, _y + contentHeight);
            _y += contentHeight;
        }

        private void EndChart() => _y += 8;

        private void DrawLineChartScale(SKRect plot, int max)
        {
            using var axis = new SKPaint { Color = new SKColor(203, 213, 225), StrokeWidth = .7f };
            _canvas.DrawLine(plot.Left, plot.Top, plot.Left, plot.Bottom, axis);
            _canvas.DrawLine(plot.Left, plot.Bottom, plot.Right, plot.Bottom, axis);
            Text("Count", 7, _medium, _muted, plot.Left - 29, plot.Top + 7);
            Text(max.ToString(), 7, _regular, _muted, plot.Left - 22, plot.Top + 8);
            Text("0", 7, _regular, _muted, plot.Left - 9, plot.Bottom + 2);
            Text($"Submissions: 0 to {max}", 7.5f, _medium, _muted, plot.Left, plot.Bottom + 13);
        }

        private void DrawDateTicks(IReadOnlyList<BusinessReportTrendPoint> points, SKRect plot)
        {
            foreach (var index in ChartTickIndexes(points.Count))
            {
                var x = points.Count <= 1 ? plot.Left + plot.Width / 2 : plot.Left + index * plot.Width / (points.Count - 1);
                using var tick = new SKPaint { Color = new SKColor(203, 213, 225), StrokeWidth = .7f };
                _canvas.DrawLine(x, plot.Bottom, x, plot.Bottom + 3, tick);
                DrawCentered(points[index].Label, 6.7f, _regular, _muted, x, plot.Bottom + 25);
            }
        }

        private static IReadOnlyList<int> ChartTickIndexes(int count)
        {
            if (count == 0) return [];
            var maximumTicks = count <= 7 ? count : count <= 31 ? 6 : 5;
            if (maximumTicks == 1) return [0];
            return Enumerable.Range(0, maximumTicks)
                .Select(tick => (int)Math.Round(tick * (count - 1d) / (maximumTicks - 1), MidpointRounding.AwayFromZero))
                .Distinct()
                .Append(count - 1)
                .Distinct()
                .OrderBy(index => index)
                .ToList();
        }

        private void DrawWrapped(string value, float size, SKTypeface face, SKColor color, float width, float lineHeight, float after)
        {
            foreach (var line in BusinessReportPdfTextLayout.Wrap(value, face, size, width))
            {
                Ensure(lineHeight);
                Text(line, size, face, color);
                _y += lineHeight;
            }
            _y += after;
        }

        private int AvailableLines(float lineHeight, float padding) => Math.Max(0, (int)Math.Floor((ContentBottom - _y - padding) / lineHeight));
        private float ContentBottom => PageHeight - Margin - 17;

        private void DrawClippedLines(IReadOnlyList<string> lines, int start, int take, float x, float y, float width, float height, float size, SKTypeface face, SKColor color, float lineHeight)
        {
            var saved = _canvas.Save();
            _canvas.ClipRect(new SKRect(x, y + 1, x + width, y + height - 1));
            for (var line = 0; line < take && start + line < lines.Count; line++)
                Text(lines[start + line], size, face, color, x, y + 10 + line * lineHeight);
            _canvas.RestoreToCount(saved);
        }

        private void Ensure(float need)
        {
            if (_y + need > ContentBottom) NewPage();
        }

        private void NewPage()
        {
            if (_page > 0) { Footer(); _document.EndPage(); }
            _canvas = _document.BeginPage(PageWidth, PageHeight);
            _page++;
            _y = Margin;
        }

        private void Footer() => Text($"TrailGuard Business Report - Page {_page}", 7.5f, _regular, _muted, Margin, PageHeight - 20);

        private void Text(string value, float size, SKTypeface face, SKColor color, float? x = null, float? y = null)
        {
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            using var font = new SKFont(face, size);
            _canvas.DrawText(BusinessReportPdfTextLayout.PrepareForTypeface(value, face, size), x ?? Margin, y ?? _y + size, SKTextAlign.Left, font, paint);
        }

        private void DrawCentered(string value, float size, SKTypeface face, SKColor color, float x, float y)
        {
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            using var font = new SKFont(face, size);
            _canvas.DrawText(BusinessReportPdfTextLayout.PrepareForTypeface(value, face, size), x, y, SKTextAlign.Center, font, paint);
        }
    }
}

public static class BusinessReportPdfTextLayout
{
    /// <summary>Wraps at Unicode text-element boundaries with explicit notation for unsupported glyphs.</summary>
    public static IReadOnlyList<string> Wrap(string value, SKTypeface face, float size, float width)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        using var font = new SKFont(face, size);
        var lines = new List<string>();
        foreach (var paragraph in PrepareForTypeface(value, face, size).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var elements = TextElements(paragraph);
            if (elements.Count == 0) { lines.Add(string.Empty); continue; }
            var current = string.Empty;
            var lastBreak = -1;
            for (var index = 0; index < elements.Count;)
            {
                var candidate = current + elements[index];
                if (font.MeasureText(candidate) <= width || current.Length == 0)
                {
                    current = candidate;
                    if (string.IsNullOrWhiteSpace(elements[index])) lastBreak = current.Length;
                    index++;
                    continue;
                }

                if (lastBreak > 0)
                {
                    lines.Add(current[..lastBreak]);
                    current = current[lastBreak..];
                    lastBreak = LastWhitespaceBoundary(current);
                }
                else
                {
                    lines.Add(current);
                    current = string.Empty;
                }
            }
            lines.Add(current);
        }
        return lines;
    }

    /// <summary>
    /// Poppins covers the Latin text used by TrailGuard, including normalized Filipino accents and punctuation.
    /// Any text element absent from that bundled face is retained as an explicit U+ code-point representation.
    /// </summary>
    public static string PrepareForTypeface(string value, SKTypeface face, float size)
    {
        using var font = new SKFont(face, size);
        var prepared = new System.Text.StringBuilder();
        var normalized = value.Normalize(NormalizationForm.FormC);
        var enumerator = StringInfo.GetTextElementEnumerator(normalized);
        while (enumerator.MoveNext())
        {
            var element = (string)enumerator.Current!;
            if (element is "\r" or "\n" || font.ContainsGlyphs(element)) prepared.Append(element);
            else prepared.Append('[').Append(string.Join(" ", element.EnumerateRunes().Select(rune => $"U+{rune.Value:X4}"))).Append(']');
        }
        return prepared.ToString();
    }

    private static List<string> TextElements(string value)
    {
        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        while (enumerator.MoveNext()) elements.Add((string)enumerator.Current!);
        return elements;
    }

    private static int LastWhitespaceBoundary(string value)
    {
        var boundary = -1;
        var enumerator = StringInfo.GetTextElementEnumerator(value);
        while (enumerator.MoveNext())
        {
            var element = (string)enumerator.Current!;
            if (string.IsNullOrWhiteSpace(element)) boundary = enumerator.ElementIndex + element.Length;
        }
        return boundary;
    }
}
