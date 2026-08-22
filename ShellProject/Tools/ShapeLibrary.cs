using System;
using System.Collections.Generic;
using System.Windows;

namespace PaintClone.Tools
{
    /// <summary>One drawable shape: an id (its tool key), a display name, and a function returning
    /// its outline as points in a -1..1 unit square. Everything else - scaling into the dragged
    /// box, rotation, fill, stroke style - is handled uniformly by PolyShapeTool, so adding a shape
    /// means adding one entry here and nothing else. The toolbox icon is generated from these same
    /// points, which is what keeps a shape and its icon from ever disagreeing.</summary>
    public sealed class ShapeDef
    {
        public string Id { get; init; }
        public string Name { get; init; }
        public Func<ToolContext, IReadOnlyList<Point>> Unit { get; init; }
    }

    /// <summary>
    /// The shape catalogue behind the toolbox's shape slot. Most entries are generated from a
    /// handful of families (regular polygons, stars at each point count, block arrows, chevrons,
    /// bursts) rather than written out one by one - which is both far less code and impossible to
    /// get subtly inconsistent between siblings.
    /// </summary>
    public static class ShapeLibrary
    {
        private static readonly Lazy<IReadOnlyList<ShapeDef>> _all = new(Build);
        public static IReadOnlyList<ShapeDef> All => _all.Value;

        private static readonly Lazy<Dictionary<string, ShapeDef>> _byId =
            new(() =>
            {
                var d = new Dictionary<string, ShapeDef>(StringComparer.Ordinal);
                foreach (var s in All) d[s.Id] = s;
                return d;
            });

        public static ShapeDef ById(string id) => _byId.Value.TryGetValue(id, out var s) ? s : null;

        /// <summary>The shape the slot shows before anything else is picked.</summary>
        public const string DefaultId = "Star";

        // ---------------------------------------------------------------
        // Primitive generators. All work in the -1..1 square, first vertex
        // at the top, so every family lines up with every other.
        // ---------------------------------------------------------------

        private static Point P(double a, double r) => new(Math.Cos(a) * r, Math.Sin(a) * r);

        private static List<Point> Regular(int sides, double startAngle = -Math.PI / 2)
        {
            var pts = new List<Point>(sides);
            for (int i = 0; i < sides; i++) pts.Add(P(startAngle + i * Math.PI * 2 / sides, 1));
            return pts;
        }

        private static List<Point> Star(int points, double inner)
        {
            var pts = new List<Point>(points * 2);
            for (int i = 0; i < points * 2; i++)
                pts.Add(P(-Math.PI / 2 + i * Math.PI / points, i % 2 == 0 ? 1 : inner));
            return pts;
        }

        /// <summary>A star whose inner radius tightens as points are added, so a many-pointed one
        /// still reads as a star rather than a gear. Same rule the original Star tool used.</summary>
        private static double AutoInner(int points) => Math.Max(0.25, 0.55 - points * 0.03);

        /// <summary>Block arrow pointing right, in the unit square. tailW is the shaft half-height,
        /// headW the barb half-height, headLen how much of the length the head takes.</summary>
        private static List<Point> BlockArrow(double tailW = 0.34, double headW = 0.9, double headLen = 0.75)
        {
            double hx = 1 - headLen * 2 * 0.5; // where the head starts
            return new List<Point>
            {
                new(-1, -tailW), new(hx, -tailW), new(hx, -headW), new(1, 0),
                new(hx, headW), new(hx, tailW), new(-1, tailW),
            };
        }

        private static List<Point> Rotate(IReadOnlyList<Point> pts, double radians)
        {
            double c = Math.Cos(radians), s = Math.Sin(radians);
            var outPts = new List<Point>(pts.Count);
            foreach (var p in pts) outPts.Add(new Point(p.X * c - p.Y * s, p.X * s + p.Y * c));
            return outPts;
        }

        /// <summary>Scales a silhouette so it exactly fills the -1..1 square without spilling past
        /// it. Applied to every shape rather than trusting each generator to keep inside the box on
        /// its own: several didn't (the egg's taper and the cloud's outer lobes both overshot), and
        /// a shape that escapes its box draws outside the rectangle that was dragged. Each axis is
        /// scaled separately, which is harmless because the box scaling that follows is already
        /// per-axis, and it means a shape fills the drag rather than sitting inset within it.</summary>
        private static IReadOnlyList<Point> Normalized(IReadOnlyList<Point> pts)
        {
            double mx = 0, my = 0;
            foreach (var p in pts) { mx = Math.Max(mx, Math.Abs(p.X)); my = Math.Max(my, Math.Abs(p.Y)); }
            if (mx <= 1.0 && my <= 1.0) return pts;

            double sx = mx > 1 ? 1 / mx : 1, sy = my > 1 ? 1 / my : 1;
            var scaled = new List<Point>(pts.Count);
            foreach (var p in pts) scaled.Add(new Point(p.X * sx, p.Y * sy));
            return scaled;
        }

        private static ShapeDef Fixed(string id, string name, IReadOnlyList<Point> pts)
        {
            var norm = Normalized(pts);
            return new ShapeDef { Id = id, Name = name, Unit = _ => norm };
        }

        // ---------------------------------------------------------------

        private static IReadOnlyList<ShapeDef> Build()
        {
            var list = new List<ShapeDef>();

            // "Star" stays first and stays driven by the tool options (point count, depth), which
            // is what the Points/Depth controls act on. The numbered stars below are fixed variants.
            list.Add(new ShapeDef
            {
                Id = "Star",
                Name = "Star",
                Unit = ctx =>
                {
                    int n = Math.Max(3, Math.Min(24, ctx?.StarPoints ?? 5));
                    double inner = (ctx?.StarInnerPercent ?? 0) > 0
                        ? ctx.StarInnerPercent / 100.0
                        : AutoInner(n);
                    return Normalized(Star(n, inner));
                }
            });

            // --- regular polygons, 3..20 sides -------------------------------
            string[] polyNames =
            {
                null, null, null, "Triangle", "Diamond", "Pentagon", "Hexagon", "Heptagon",
                "Octagon", "Nonagon", "Decagon", "Hendecagon", "Dodecagon",
            };
            for (int n = 3; n <= 20; n++)
            {
                string name = n < polyNames.Length && polyNames[n] != null ? polyNames[n] : $"{n}-sided Polygon";
                int sides = n;
                list.Add(Fixed($"Poly{n}", name, Regular(sides)));
            }

            // --- stars at each point count, 3..20 ----------------------------
            // 5 is skipped: the adjustable "Star" above already defaults to a 5-point star, so a
            // fixed one would be an exact visual duplicate sitting next to it in the picker.
            for (int n = 3; n <= 20; n++)
            {
                if (n == 5) continue;
                int points = n;
                list.Add(Fixed($"Star{n}", $"{n}-point Star", Star(points, AutoInner(points))));
            }

            // --- sharp and blunt star variants -------------------------------
            foreach (int n in new[] { 5, 6, 8, 10, 12 })
            {
                list.Add(Fixed($"StarSharp{n}", $"{n}-point Star (sharp)", Star(n, 0.22)));
                list.Add(Fixed($"StarBlunt{n}", $"{n}-point Star (blunt)", Star(n, 0.68)));
            }

            // --- bursts: many shallow points, the "seal"/"explosion" look ----
            foreach (int n in new[] { 8, 10, 12, 16, 20, 24 })
                list.Add(Fixed($"Burst{n}", $"{n}-point Burst", Star(n, 0.78)));

            // --- block arrows, eight directions ------------------------------
            var arrowDirs = new (string Id, string Name, double Angle)[]
            {
                ("Right", "Arrow Right", 0),
                ("Down", "Arrow Down", Math.PI / 2),
                ("Left", "Arrow Left", Math.PI),
                ("Up", "Arrow Up", -Math.PI / 2),
                ("DownRight", "Arrow Down-Right", Math.PI / 4),
                ("DownLeft", "Arrow Down-Left", Math.PI * 3 / 4),
                ("UpLeft", "Arrow Up-Left", -Math.PI * 3 / 4),
                ("UpRight", "Arrow Up-Right", -Math.PI / 4),
            };
            var baseArrow = BlockArrow();
            foreach (var (id, name, angle) in arrowDirs)
                list.Add(Fixed($"BlockArrow{id}", name, Rotate(baseArrow, angle)));

            // --- double-headed arrows ----------------------------------------
            var doubleArrow = new List<Point>
            {
                new(-1, 0), new(-0.5, -0.9), new(-0.5, -0.34), new(0.5, -0.34),
                new(0.5, -0.9), new(1, 0), new(0.5, 0.9), new(0.5, 0.34),
                new(-0.5, 0.34), new(-0.5, 0.9),
            };
            list.Add(Fixed("DoubleArrowH", "Double Arrow (horizontal)", doubleArrow));
            list.Add(Fixed("DoubleArrowV", "Double Arrow (vertical)", Rotate(doubleArrow, Math.PI / 2)));

            // --- chevrons -----------------------------------------------------
            var chevron = new List<Point>
            {
                new(-0.2, -1), new(1, 0), new(-0.2, 1), new(-1, 1), new(0.2, 0), new(-1, -1),
            };
            list.Add(Fixed("ChevronRight", "Chevron Right", chevron));
            list.Add(Fixed("ChevronDown", "Chevron Down", Rotate(chevron, Math.PI / 2)));
            list.Add(Fixed("ChevronLeft", "Chevron Left", Rotate(chevron, Math.PI)));
            list.Add(Fixed("ChevronUp", "Chevron Up", Rotate(chevron, -Math.PI / 2)));

            // --- crosses ------------------------------------------------------
            List<Point> CrossOf(double t) => new()
            {
                new(-t, -1), new(t, -1), new(t, -t), new(1, -t), new(1, t), new(t, t),
                new(t, 1), new(-t, 1), new(-t, t), new(-1, t), new(-1, -t), new(-t, -t),
            };
            list.Add(Fixed("Cross", "Cross", CrossOf(0.34)));
            list.Add(Fixed("CrossThin", "Cross (thin)", CrossOf(0.18)));
            list.Add(Fixed("CrossThick", "Cross (thick)", CrossOf(0.55)));
            list.Add(Fixed("CrossDiagonal", "Cross (diagonal)", Rotate(CrossOf(0.3), Math.PI / 4)));

            // --- quadrilaterals and triangles ---------------------------------
            list.Add(Fixed("Square", "Square", new List<Point> { new(-1, -1), new(1, -1), new(1, 1), new(-1, 1) }));
            list.Add(Fixed("RightTriangle", "Right Triangle", new List<Point> { new(-1, 1), new(1, 1), new(-1, -1) }));
            list.Add(Fixed("Trapezoid", "Trapezoid", new List<Point> { new(-0.55, -1), new(0.55, -1), new(1, 1), new(-1, 1) }));
            list.Add(Fixed("Parallelogram", "Parallelogram", new List<Point> { new(-0.5, -1), new(1, -1), new(0.5, 1), new(-1, 1) }));
            list.Add(Fixed("Kite", "Kite", new List<Point> { new(0, -1), new(0.7, -0.1), new(0, 1), new(-0.7, -0.1) }));
            list.Add(Fixed("Rhombus", "Rhombus", new List<Point> { new(0, -1), new(0.6, 0), new(0, 1), new(-0.6, 0) }));

            // --- curved and organic shapes ------------------------------------
            list.Add(Fixed("Circle", "Circle", Regular(48)));
            list.Add(Fixed("Semicircle", "Semicircle", Semicircle()));
            list.Add(Fixed("Arch", "Arch", Arch()));
            list.Add(Fixed("PieThreeQuarter", "Pie (three-quarter)", Pie(Math.PI * 1.5)));
            list.Add(Fixed("PieHalf", "Pie (half)", Pie(Math.PI)));
            list.Add(Fixed("PieQuarter", "Pie (quarter)", Pie(Math.PI / 2)));
            list.Add(Fixed("Egg", "Egg", Egg()));
            list.Add(Fixed("Leaf", "Leaf", Leaf()));
            list.Add(Fixed("Drop", "Teardrop", Drop()));
            list.Add(Fixed("Moon", "Crescent Moon", Moon()));
            list.Add(Fixed("Heart", "Heart", Heart()));
            list.Add(Fixed("Cloud", "Cloud", Cloud()));
            list.Add(Fixed("Shield", "Shield", Shield()));
            list.Add(Fixed("SpeechBubble", "Speech Bubble", SpeechBubble()));
            list.Add(Fixed("Banner", "Banner", Banner()));
            list.Add(Fixed("Ribbon", "Ribbon", Ribbon()));
            list.Add(Fixed("Bookmark", "Bookmark", Bookmark()));
            list.Add(Fixed("Tag", "Tag", Tag()));
            list.Add(Fixed("Lightning", "Lightning Bolt", Lightning()));
            list.Add(Fixed("Sun", "Sun", Star(12, 0.62)));
            list.Add(Fixed("Gear", "Gear", Gear(10)));
            list.Add(Fixed("Hexagram", "Six-pointed Star", Star(6, 0.58)));
            list.Add(Fixed("Zigzag", "Zigzag", Zigzag()));
            list.Add(Fixed("Wave", "Wave", Wave()));
            foreach (int petals in new[] { 5, 6, 8 })
                list.Add(Fixed($"Flower{petals}", $"Flower ({petals} petals)", Flower(petals)));

            return list;
        }

        // ---------------------------------------------------------------
        // One-off silhouettes
        // ---------------------------------------------------------------

        private static List<Point> Semicircle()
        {
            var pts = new List<Point>();
            for (int i = 0; i <= 32; i++) pts.Add(P(Math.PI + i * Math.PI / 32, 1));
            pts.Add(new Point(1, 1));
            pts.Add(new Point(-1, 1));
            return pts;
        }

        private static List<Point> Arch()
        {
            var pts = new List<Point> { new(-1, 1) };
            for (int i = 0; i <= 32; i++) pts.Add(P(Math.PI + i * Math.PI / 32, 1));
            pts.Add(new Point(1, 1));
            return pts;
        }

        private static List<Point> Pie(double sweep)
        {
            var pts = new List<Point> { new(0, 0) };
            int steps = Math.Max(8, (int)(sweep * 12));
            for (int i = 0; i <= steps; i++) pts.Add(P(-Math.PI / 2 + sweep * i / steps, 1));
            return pts;
        }

        private static List<Point> Egg()
        {
            var pts = new List<Point>();
            for (int i = 0; i < 48; i++)
            {
                double a = -Math.PI / 2 + i * Math.PI * 2 / 48;
                double taper = 1 - 0.22 * Math.Cos(a); // narrower at the top than the bottom
                pts.Add(new Point(Math.Cos(a) / taper, Math.Sin(a)));
            }
            return pts;
        }

        private static List<Point> Leaf()
        {
            var pts = new List<Point>();
            for (int i = 0; i <= 24; i++) { double t = i / 24.0; pts.Add(new Point(-1 + 2 * t, -Math.Sin(t * Math.PI))); }
            for (int i = 0; i <= 24; i++) { double t = 1 - i / 24.0; pts.Add(new Point(-1 + 2 * t, Math.Sin(t * Math.PI))); }
            return pts;
        }

        private static List<Point> Drop()
        {
            var pts = new List<Point> { new(0, -1) };
            for (int i = 0; i <= 36; i++)
            {
                double a = -Math.PI / 2 + Math.PI / 6 + i * (Math.PI * 2 - Math.PI / 3) / 36;
                pts.Add(new Point(Math.Cos(a) * 0.72, 0.3 + Math.Sin(a) * 0.7));
            }
            return pts;
        }

        private static List<Point> Moon()
        {
            var pts = new List<Point>();
            for (int i = 0; i <= 32; i++) pts.Add(P(-Math.PI / 2 + i * Math.PI / 32, 1));       // outer edge
            for (int i = 32; i >= 0; i--)                                                        // inner bite
            {
                double a = -Math.PI / 2 + i * Math.PI / 32;
                pts.Add(new Point(Math.Cos(a) * 1.05 - 0.55, Math.Sin(a)));
            }
            return pts;
        }

        private static List<Point> Heart()
        {
            var pts = new List<Point>();
            for (int i = 0; i < 60; i++)
            {
                double t = i / 60.0 * Math.PI * 2;
                double hx = 16 * Math.Pow(Math.Sin(t), 3);
                double hy = 13 * Math.Cos(t) - 5 * Math.Cos(2 * t) - 2 * Math.Cos(3 * t) - Math.Cos(4 * t);
                pts.Add(new Point(hx / 17.0, -hy / 17.0));
            }
            return pts;
        }

        private static List<Point> Cloud()
        {
            // Overlapping lobes along the top, flat underneath.
            var lobes = new (double cx, double cy, double r)[]
            {
                (-0.6, 0.1, 0.42), (-0.2, -0.25, 0.55), (0.3, -0.15, 0.48), (0.68, 0.15, 0.36),
            };
            var pts = new List<Point>();
            foreach (var (lcx, lcy, r) in lobes)
                for (int i = 0; i <= 16; i++)
                {
                    double a = Math.PI + i * Math.PI / 16;
                    pts.Add(new Point(lcx + Math.Cos(a) * r, lcy + Math.Sin(a) * r));
                }
            pts.Add(new Point(1, 0.62));
            pts.Add(new Point(-1, 0.62));
            return pts;
        }

        private static List<Point> Shield()
        {
            var pts = new List<Point> { new(-0.85, -1), new(0.85, -1), new(0.85, 0.1) };
            for (int i = 0; i <= 20; i++)
            {
                double t = i / 20.0;
                pts.Add(new Point(0.85 * (1 - t) - 0 * t, 0.1 + Math.Sin(t * Math.PI / 2) * 0.9));
            }
            for (int i = 0; i <= 20; i++)
            {
                double t = i / 20.0;
                pts.Add(new Point(-0.85 * t, 1 - Math.Sin((1 - t) * Math.PI / 2) * 0.9));
            }
            return pts;
        }

        private static List<Point> SpeechBubble()
        {
            var pts = new List<Point>();
            for (int i = 0; i <= 40; i++)                      // rounded body
            {
                double a = -Math.PI / 2 + i * Math.PI * 2 / 40;
                pts.Add(new Point(Math.Cos(a), Math.Sin(a) * 0.72 - 0.18));
                if (i == 27) { pts.Add(new Point(-0.35, 0.55)); pts.Add(new Point(-0.62, 1)); pts.Add(new Point(-0.2, 0.54)); }
            }
            return pts;
        }

        private static List<Point> Banner() => new()
        {
            new(-1, -0.55), new(1, -0.55), new(1, 0.55), new(0.62, 0.2),
            new(0.25, 0.55), new(-0.25, 0.2), new(-0.62, 0.55), new(-1, 0.2),
        };

        private static List<Point> Ribbon() => new()
        {
            new(-1, -0.45), new(-0.6, 0), new(-1, 0.45), new(-0.35, 0.45), new(-0.35, -0.45),
            new(0.35, -0.45), new(0.35, 0.45), new(1, 0.45), new(0.6, 0), new(1, -0.45),
        };

        private static List<Point> Bookmark() => new()
        {
            new(-0.55, -1), new(0.55, -1), new(0.55, 1), new(0, 0.45), new(-0.55, 1),
        };

        private static List<Point> Tag() => new()
        {
            new(-1, -0.6), new(0.45, -0.6), new(1, 0), new(0.45, 0.6), new(-1, 0.6),
        };

        private static List<Point> Lightning() => new()
        {
            new(0.15, -1), new(-0.65, 0.12), new(-0.1, 0.12),
            new(-0.35, 1), new(0.62, -0.16), new(0.02, -0.16), new(0.55, -1),
        };

        private static List<Point> Gear(int teeth)
        {
            var pts = new List<Point>();
            int steps = teeth * 4;
            for (int i = 0; i < steps; i++)
            {
                double a = -Math.PI / 2 + i * Math.PI * 2 / steps;
                // Square-ish teeth: two samples out, two in, repeating.
                double r = (i % 4 is 0 or 1) ? 1.0 : 0.72;
                pts.Add(P(a, r));
            }
            return pts;
        }

        private static List<Point> Flower(int petals)
        {
            var pts = new List<Point>();
            int steps = petals * 24;
            for (int i = 0; i < steps; i++)
            {
                double a = i * Math.PI * 2 / steps;
                double r = 0.45 + 0.55 * Math.Abs(Math.Cos(petals * a / 2));
                pts.Add(P(a - Math.PI / 2, r));
            }
            return pts;
        }

        private static List<Point> Zigzag()
        {
            var pts = new List<Point>();
            const int n = 6;
            for (int i = 0; i <= n; i++) pts.Add(new Point(-1 + 2.0 * i / n, i % 2 == 0 ? -0.5 : 0.15));
            for (int i = n; i >= 0; i--) pts.Add(new Point(-1 + 2.0 * i / n, i % 2 == 0 ? 0.15 : 0.8));
            return pts;
        }

        private static List<Point> Wave()
        {
            var pts = new List<Point>();
            for (int i = 0; i <= 40; i++) { double t = i / 40.0; pts.Add(new Point(-1 + 2 * t, -0.35 + Math.Sin(t * Math.PI * 2) * 0.45)); }
            for (int i = 40; i >= 0; i--) { double t = i / 40.0; pts.Add(new Point(-1 + 2 * t, 0.35 + Math.Sin(t * Math.PI * 2) * 0.45)); }
            return pts;
        }
    }
}
