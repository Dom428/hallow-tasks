// Hallow Tasks - a live graph in the console style: a dark well, a faint grid, one or more lines (the first one filled), the last
// 60 seconds. Colours are theme brush keys. It only redraws when the window asks (once a second while sampling).
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace HallowTasks
{
    public class Graph : FrameworkElement
    {
        public class Line { public Ring Data; public string Stroke, Fill; }
        public readonly List<Line> Lines = new List<Line>();
        public double Max = 100;          // fixed scale (percent); 0 = scale to the biggest value shown
        public double MinScale = 1;       // with Max = 0: never scale below this
        public double ShownMax { get; private set; }

        public void Add(Ring data, string stroke, string fill = null) { Lines.Add(new Line { Data = data, Stroke = stroke, Fill = fill }); }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight; if (w < 4 || h < 4) return;
            var bg = Ui.R("GraphBg"); var grid = new Pen(Ui.R("GraphGrid"), 1); grid.Freeze();
            dc.DrawRectangle(bg, new Pen(Ui.R("Line"), 1), new Rect(0.5, 0.5, w - 1, h - 1));
            for (int i = 1; i < 4; i++) { double y = Math.Round(h * i / 4) + 0.5; dc.DrawLine(grid, new Point(1, y), new Point(w - 1, y)); }
            for (int i = 1; i < 6; i++) { double x = Math.Round(w * i / 6) + 0.5; dc.DrawLine(grid, new Point(x, 1), new Point(x, h - 1)); }
            double max = Max;
            if (max <= 0) { max = MinScale; foreach (var l in Lines) if (l.Data != null) max = Math.Max(max, l.Data.Max()); max *= 1.1; }
            ShownMax = max;
            foreach (var l in Lines)
            {
                var d = l.Data; if (d == null || d.Count < 2) continue;
                int size = d.Size; double step = (w - 2) / (size - 1);
                var pts = new List<Point>();
                for (int i = 0; i < d.Count; i++)
                {
                    double x = 1 + (size - d.Count + i) * step, v = Math.Max(0, Math.Min(max, d[i]));
                    pts.Add(new Point(x, h - 1 - (h - 3) * v / max));
                }
                var geo = new StreamGeometry();
                using (var g = geo.Open()) { g.BeginFigure(pts[0], false, false); g.PolyLineTo(pts.GetRange(1, pts.Count - 1), true, true); }
                geo.Freeze();
                if (l.Fill != null)
                {
                    var area = new StreamGeometry();
                    using (var g = area.Open())
                    {
                        g.BeginFigure(new Point(pts[0].X, h - 1), true, true); g.PolyLineTo(pts, true, true); g.LineTo(new Point(pts[pts.Count - 1].X, h - 1), true, true);
                    }
                    area.Freeze(); dc.DrawGeometry(Ui.R(l.Fill), null, area);
                }
                var pen = new Pen(Ui.R(l.Stroke), 1.5) { LineJoin = PenLineJoin.Round }; pen.Freeze();
                dc.DrawGeometry(null, pen, geo);
            }
        }
    }
}
