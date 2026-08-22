using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PaintClone.Controls;
using PaintClone.Dialogs;
using PaintClone.Models;
using PaintClone.Services;
using PaintClone.Tools;
using PaintClone.Services.Plugins;

namespace PaintClone
{
    public partial class MainWindow : Window
    {
        private readonly ColorManager _colors = new();
        private readonly HistoryManager _history = new();
        private readonly PluginManager _pluginManager = new();
        private string _toolBeforePick;
        private readonly SelectionManager _selection = new();
        private readonly PaintCanvas _canvas = new();
        private PaintDocument _document;
        private ToolContext _ctx;

        private readonly Dictionary<string, ITool> _tools = new();
        private readonly Dictionary<string, ToggleButton> _toolButtons = new();
        private string _currentToolKey = "Pencil";

        private double _zoom = 1.0;
        private bool _showGrid;

        // canvas resize handles (drag the right/bottom/corner edge, like classic Paint)
        private double _dragAccumX, _dragAccumY;
        private int _dragStartW, _dragStartH;

        // text tool state
        private TextBox _activeTextBox;
        private FrameworkElement _textResizeHandle;
        private PendingShape _pendingShape;
        private readonly List<FrameworkElement> _selectionResizeHandles = new();
        private Rect _pendingResizeScreenBounds;
        private FrameworkElement _textMoveHandle;
        private Rect _activeTextDocRect;
        private string _textFontFamily = "Tahoma";
        private double _textFontSize = 16;
        private bool _textBold, _textItalic, _textUnderline;
        private Color _textEditColor;
        /// <summary>True while the box currently being edited is "point text" (see TextTool /
        /// TextLayerData.AutoWidth) - grows in both directions, never wraps.</summary>
        private bool _textAutoWidth;

        /// <summary>True from the moment the tool options bar is clicked until the canvas is
        /// clicked again, so an open text box doesn't treat focus lost to the options bar as
        /// "finished editing".
        ///
        /// Deliberately cleared by the next canvas click rather than on a timer. A ComboBox moves
        /// focus into its dropdown asynchronously, *after* the click that opened it has finished
        /// being processed - so a flag that reset itself at the end of that click was already back
        /// to false by the time the text box lost focus, and the text got committed anyway. Nothing
        /// is lost by holding it: clicking the canvas commits the box explicitly on its own path
        /// (BeginTextEditing calls CommitActiveTextBox), and clicking anywhere else - a menu, the
        /// toolbox - isn't the options bar, so it never sets this in the first place.</summary>
        private bool _suppressTextCommit;

        /// <summary>-1 while creating a brand-new text layer; the index of an existing layer while
        /// re-editing one (see TextTool/BeginTextEditOnActiveLayer) - what CommitActiveTextBox uses
        /// to decide between AddTextLayer and updating that layer's TextLayerData in place.</summary>
        private int _editingLayerIndex = -1;

        public MainWindow()
        {
            InitializeComponent();

            // App.OnStartup already applied the saved theme before this window was constructed -
            // just sync the two Theme menu checkmarks to match so they don't default to "Dark"
            // when the saved preference was actually Light.
            MenuThemeDark.IsChecked = ThemeManager.Current == ThemeManager.Dark;
            MenuThemeLight.IsChecked = ThemeManager.Current == ThemeManager.Light;

            CanvasStack.Children.Insert(0, _canvas);
            _canvas.HorizontalAlignment = HorizontalAlignment.Left;
            _canvas.VerticalAlignment = VerticalAlignment.Top;
            _canvas.CanvasMouseDown += Canvas_MouseDown;
            _canvas.CanvasMouseMove += Canvas_MouseMove;
            _canvas.CanvasMouseUp += Canvas_MouseUp;
            AddResizeHandles();

            _colors.ColorsChanged += (s, e) =>
            {
                UpdateColorSwatches();
                // Live-preview a foreground color change onto whatever text is being typed right
                // now, rather than only taking effect (or not) unpredictably at commit time.
                if (_activeTextBox != null)
                {
                    _textEditColor = _colors.Foreground;
                    var brush = new SolidColorBrush(_textEditColor);
                    _activeTextBox.Foreground = brush;
                    _activeTextBox.CaretBrush = brush;
                }
            };
            _history.StateChanged += (s, e) => UpdateEditMenuState();
            _selection.Changed += (s, e) => { UpdateEditMenuState(); RefreshSelectionHandles(); };

            // Anything pressed in the tool options bar must not be treated as "done editing" by an
            // open text box - see the LostKeyboardFocus handler in StartTextBoxUI. PreviewMouseDown
            // tunnels, so this runs before the focus change the click causes; the flag is cleared
            // once the click has been fully processed.
            ToolOptionsPanel.PreviewMouseDown += (s, e) => _suppressTextCommit = true;

            HistoryPanelCtl.Initialize(_history, JumpToHistoryIndex);

            BuildTools();
            BuildPalette();
            UpdateColorSwatches();
            BuildActionHandlers();

            NewDocument(480, 360, promptSave: false);

            PreviewKeyDown += MainWindow_PreviewKeyDown;

            LoadAndShowPlugins();

            SelectTool("Pencil");
        }

        // ===================================================================
        // Setup
        // ===================================================================

        /// <summary>Every brush shape offered in the toolbox, with the glyph shown on its button.
        /// Kept as one table so adding a shape means adding one line here plus its stamp - the UI
        /// row builds itself from this rather than needing a hand-written entry per shape.</summary>
        private static readonly (BrushShape Shape, string Glyph, string Tip)[] BrushShapeChoices =
        {
            (BrushShape.Round,          "\u25CF", "Round"),
            (BrushShape.Square,         "\u25A0", "Square"),
            (BrushShape.DiagonalRight,  "/",       "Diagonal (right)"),
            (BrushShape.DiagonalLeft,   "\\",     "Diagonal (left)"),
            (BrushShape.Splatter,       "\u2733", "Splatter"),
            (BrushShape.Cross,          "+",       "Cross"),
            (BrushShape.Soft,           "\u2601", "Soft airbrushed edge"),
            (BrushShape.Triangle,       "\u25B2", "Triangle"),
            (BrushShape.Diamond,        "\u25C6", "Diamond"),
            (BrushShape.Star,           "\u2605", "Star"),
            (BrushShape.Ring,           "\u25CB", "Ring (hollow circle)"),
            (BrushShape.HollowSquare,   "\u25A1", "Hollow square"),
            (BrushShape.HorizontalBar,  "\u2550", "Flat horizontal nib"),
            (BrushShape.VerticalBar,    "\u2551", "Flat vertical nib"),
            (BrushShape.Calligraphy,    "\u2571", "Calligraphy nib (angled)"),
            (BrushShape.Chalk,          "\u2592", "Chalk / charcoal (grainy)"),
            (BrushShape.Stipple,        "\u2059", "Stipple (scattered dots)"),
        };

        /// <summary>Blend shapes shown in the Gradient tool's options row.</summary>
        private static readonly (GradientType Type, string Glyph, string Tip)[] GradientTypeChoices =
        {
            (GradientType.Linear,    "\u25A4", "Linear - straight blend along the drag"),
            (GradientType.Reflected, "\u25A5", "Reflected - mirrored either side of the start"),
            (GradientType.Radial,    "\u25C9", "Radial - circular, spreading from the start"),
            (GradientType.Diamond,   "\u25C7", "Diamond - square-cornered rings"),
            (GradientType.Angular,   "\u25D4", "Angular - sweeps around the start point"),
        };

        /// <summary>Arrowhead styles shown in the Arrow tool's options row.</summary>
        private static readonly (ArrowStyle Style, string Glyph, string Tip)[] ArrowStyleChoices =
        {
            (ArrowStyle.End,         "\u2192", "Open head at the end"),
            (ArrowStyle.Both,        "\u2194", "Open heads at both ends"),
            (ArrowStyle.Filled,      "\u27A4", "Solid head at the end"),
            (ArrowStyle.FilledBoth,  "\u2b0c", "Solid heads at both ends"),
            (ArrowStyle.Diamond,     "\u25c6", "Diamond at the end"),
            (ArrowStyle.DiamondBoth, "\u25c6", "Diamonds at both ends"),
            (ArrowStyle.Circle,      "\u25cf", "Dot at the end"),
            (ArrowStyle.CircleBoth,  "\u25cf", "Dots at both ends"),
            (ArrowStyle.Bar,         "\u22a5", "Cross-bar at the end"),
            (ArrowStyle.BarBoth,     "\u22a5", "Cross-bars at both ends"),
            (ArrowStyle.None,        "\u2015", "No head (plain line)"),
        };

        /// <summary>Every font family actually installed on this machine, sorted by name - so the
        /// Text tool offers the real font list rather than a fixed guess at what's probably there.
        /// Falls back to the curated list below if enumeration fails for any reason.
        ///
        /// Note this *finds* fonts, it doesn't install any: putting new font files on the machine
        /// is a system-wide change (and a licensing question) that an image editor shouldn't be
        /// making on its own. Anything installed through Windows shows up here automatically.</summary>
        private static string[] _allFontFamilies;
        private static string[] CommonFontFamilies
        {
            get
            {
                if (_allFontFamilies != null) return _allFontFamilies;
                try
                {
                    var names = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
                    foreach (var f in Fonts.SystemFontFamilies)
                    {
                        // Prefer the name in the user's own language where the family provides one.
                        var src = f.FamilyNames;
                        string name = null;
                        foreach (var kv in src) { name = kv.Value; break; }
                        var culture = System.Globalization.CultureInfo.CurrentUICulture;
                        var tag = System.Windows.Markup.XmlLanguage.GetLanguage(culture.IetfLanguageTag);
                        if (src.TryGetValue(tag, out var localized)) name = localized;
                        if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
                    }
                    if (names.Count > 0)
                    {
                        _allFontFamilies = new string[names.Count];
                        names.CopyTo(_allFontFamilies);
                        return _allFontFamilies;
                    }
                }
                catch
                {
                    // Enumeration can fail on an unusual font configuration - fall back rather
                    // than leaving the Text tool with no fonts at all.
                }
                return _allFontFamilies = FallbackFontFamilies;
            }
        }

        /// <summary>Used only if the system font list can't be read: fifty fonts that ship with, or
        /// are very commonly present on, Windows, covering serif, sans, monospace, script and
        /// display faces. Any that turn out not to be installed simply fall back to the system
        /// default when rendered, so an unusual configuration degrades gracefully.</summary>
        private static readonly string[] FallbackFontFamilies =
        {
            "Arial", "Arial Black", "Arial Narrow", "Bahnschrift",
            "Bookman Old Style", "Calibri", "Cambria", "Candara",
            "Century Gothic", "Comic Sans MS", "Consolas", "Constantia",
            "Copperplate Gothic Bold", "Corbel", "Courier New", "Ebrima",
            "Franklin Gothic Medium", "Gabriola", "Gadugi", "Garamond",
            "Georgia", "Impact", "Ink Free", "Javanese Text",
            "Leelawadee UI", "Lucida Console", "Lucida Sans Unicode", "Malgun Gothic",
            "Microsoft Sans Serif", "Mongolian Baiti", "MS Gothic", "MV Boli",
            "Myanmar Text", "Nirmala UI", "Palatino Linotype", "Papyrus",
            "Rockwell", "Segoe Print", "Segoe Script", "Segoe UI",
            "Segoe UI Emoji", "Segoe UI Historic", "Segoe UI Symbol", "SimSun",
            "Sitka Text", "Sylfaen", "Tahoma", "Times New Roman",
            "Trebuchet MS", "Verdana",
        };

        private static readonly Dictionary<string, string> ToolFallbackText = new()
        {
            ["FreeFormSelect"] = "FF", ["Select"] = "SEL", ["Eraser"] = "ERS", ["Fill"] = "FIL",
            ["Pick"] = "PIK", ["Magnifier"] = "ZUM", ["Pencil"] = "PEN", ["Brush"] = "BRU",
            ["Airbrush"] = "AIR", ["Text"] = "TXT", ["Line"] = "LIN", ["Curve"] = "CUR",
            ["Rectangle"] = "REC", ["Polygon"] = "POL", ["Ellipse"] = "ELL", ["RoundedRectangle"] = "RRC",
            ["MagicWand"] = "MW", ["Arrow"] = "ARR", ["Star"] = "STA", ["Gradient"] = "GRD"
        };

        private UIElement MakeToolIcon(string iconFile, string toolKey)
        {
            try
            {
                // Monochrome vector glyphs in the active theme's colour (Services/ToolIcons),
                // replacing the colour flat-art PNGs this used to load - see that class for why.
                var vector = ToolIcons.Create(toolKey);
                if (vector != null) return vector;

                // No vector defined for this key (a plugin-supplied tool, say) - fall back to the
                // original PNG if one happens to exist under that name.
                var uri = new Uri($"pack://application:,,,/Resources/Icons/{iconFile}.png", UriKind.Absolute);
                var bmp = new BitmapImage(uri);
                var img = new Image { Source = bmp, Width = 26, Height = 26, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                return img;
            }
            catch
            {
                // Icon resource missing for some reason - fall back to a readable label rather
                // than an empty button. Takes the same theme-following glyph colour the vector
                // icons use: this now sits directly on the tool strip (the constant near-white
                // chip that used to be behind it is gone), so a hardcoded dark colour here would
                // be invisible in the dark theme.
                var label = new TextBlock
                {
                    Text = ToolFallbackText.TryGetValue(toolKey, out var t) ? t : toolKey,
                    FontSize = 9,
                    TextAlignment = TextAlignment.Center
                };
                label.SetResourceReference(TextBlock.ForegroundProperty, "PsIconGlyph");
                return label;
            }
        }

        private void BuildTools()
        {
            _tools["FreeFormSelect"] = new FreeFormSelectTool();
            _tools["Select"] = new SelectRectTool();
            _tools["Eraser"] = new EraserTool();
            _tools["Fill"] = new FillTool();
            _tools["Pick"] = new ColorPickerTool { OnSampled = (c, b) => AfterColorPick() };
            _tools["Magnifier"] = new MagnifierTool { OnClick = CycleZoom, OnAreaSelected = ZoomToArea };
            _tools["Pencil"] = new PencilTool();
            _tools["Brush"] = new BrushTool();
            _tools["Airbrush"] = new AirbrushTool();
            _tools["Text"] = new TextTool();
            _tools["Line"] = new LineTool();
            _tools["Curve"] = new CurveTool();
            _tools["Rectangle"] = new RectangleTool();
            _tools["Polygon"] = new PolygonTool();
            _tools["Ellipse"] = new EllipseTool();
            _tools["RoundedRectangle"] = new RoundedRectangleTool();
            _tools["MagicWand"] = new MagicWandSelectTool();
            _tools["Arrow"] = new ArrowTool();
            // Star and its siblings are all the same tool with a different vertex ring; they share
            // one toolbox slot via the shape flyout (see ToolGroups below).
            foreach (var shape in ShapeLibrary.All)
                _tools[shape.Id] = new PolyShapeTool(shape);
            _tools["Gradient"] = new GradientTool();

            // classic 2-column ordering (spec section 11), with Magic Wand added as a 17th tool
            // alongside the other selection tools it belongs with. Icons are extracted from the
            // supplied tools.svg sprite sheet (Resources/Icons/*.png) - falls back to a text
            // abbreviation if a particular icon file is missing (Magic Wand has no source icon,
            // so it always uses its fallback "MW" label) so the toolbox never ends up blank.
            var order = new (string key, string iconFile, string tip)[]
            {
                ("FreeFormSelect", "freeform_select", "Free-Form Select"), ("Select", "select", "Select"),
                ("Eraser", "eraser", "Eraser/Color Eraser"),                ("Fill", "fill", "Fill With Color"),
                ("Pick", "pick_color", "Pick Color"),                       ("Magnifier", "magnifier", "Magnifier"),
                ("Pencil", "pencil", "Pencil"),                             ("Brush", "brush", "Brush"),
                ("Airbrush", "airbrush", "Airbrush"),                      ("Text", "text", "Text"),
                ("Line", "line", "Line"),                                   ("Curve", "curve", "Curve"),
                ("Rectangle", "rectangle", "Rectangle"),                    ("Polygon", "polygon", "Polygon"),
                ("Ellipse", "ellipse", "Ellipse"),                          ("RoundedRectangle", "rounded_rectangle", "Rounded Rectangle"),
                ("MagicWand", "magic_wand", "Magic Wand"),   ("Arrow", "arrow", "Arrow"),
                ("Star", "star", "Star"),                    ("Gradient", "gradient", "Gradient"),
            };

            foreach (var (key, iconFile, tip) in order)
            {
                var btn = new ToggleButton
                {
                    Style = (Style)FindResource("ToolButtonStyle"),
                    ToolTip = ToolGroups.ContainsKey(key) ? GroupSlotTooltip(key, key) : tip,
                    Tag = key
                };
                btn.Content = MakeToolButtonContent(key, iconFile);
                WireToolButton(btn, key);
                _toolButtons[key] = btn;
                ToolGrid.Children.Add(btn);
            }
        }

        /// <summary>Toolbox slots that stand for a family of tools rather than one. The slot shows
        /// whichever member is currently chosen, and a small corner marker opens the rest - the way
        /// Photoshop groups its shape tools behind one button.</summary>
        private static readonly Dictionary<string, string[]> ToolGroups = new()
        {
            ["Star"] = ShapeLibrary.All.Select(s => s.Id).ToArray(),
        };

        /// <summary>Which member of a grouped slot that slot is currently showing.</summary>
        private readonly Dictionary<string, string> _activeGroupMember = new();

        /// <summary>The slot a tool belongs to, or null if it isn't part of a group.</summary>
        private static string GroupSlotFor(string toolKey)
        {
            foreach (var (slot, members) in ToolGroups)
                if (Array.IndexOf(members, toolKey) >= 0) return slot;
            return null;
        }

        /// <summary>A toolbox button's visual: the icon, plus - for a grouped slot - a small
        /// triangle in the bottom-right corner marking that there are more tools behind it.</summary>
        private UIElement MakeToolButtonContent(string slotKey, string iconFile)
        {
            string shown = _activeGroupMember.TryGetValue(slotKey, out var m) ? m : slotKey;
            // A grouped slot shows the chosen shape's own generated glyph, so all hundred-odd
            // shapes get a correct toolbox face without a hand-drawn icon each.
            var icon = ToolGroups.ContainsKey(slotKey)
                ? MakeShapeGlyph(shown, 20) ?? MakeToolIcon(iconFile, shown)
                : MakeToolIcon(iconFile, shown);
            if (!ToolGroups.ContainsKey(slotKey)) return icon;

            var grid = new Grid { Width = 26, Height = 26 };
            if (icon is FrameworkElement fe)
            {
                fe.HorizontalAlignment = HorizontalAlignment.Center;
                fe.VerticalAlignment = VerticalAlignment.Center;
            }
            grid.Children.Add(icon);

            var marker = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M0,5 L5,5 L5,0 Z"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible = false
            };
            marker.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "PsIconGlyph");
            grid.Children.Add(marker);
            return grid;
        }

        /// <summary>Click selects the slot's current tool; clicking its corner marker - or
        /// right-clicking anywhere on it - opens the group's flyout instead.</summary>
        private void WireToolButton(ToggleButton btn, string slotKey)
        {
            if (!ToolGroups.TryGetValue(slotKey, out var members))
            {
                btn.Click += (s, e) => SelectTool(slotKey);
                return;
            }

            // The corner press is only *recorded* here and acted on when the button is released.
            // Opening on the press doesn't work: a popup with StaysOpen=false treats the mouse-up
            // that follows its own opening press as a click outside itself and closes again
            // immediately, so the flyout appeared never to open at all. (Right-click below already
            // opens on release, which is why that path worked while this one didn't.)
            bool cornerPressed = false;
            btn.PreviewMouseLeftButtonDown += (s, e) =>
            {
                // Bottom-right corner region = "show me the rest of this group".
                var p = e.GetPosition(btn);
                cornerPressed = p.X >= btn.ActualWidth - 12 && p.Y >= btn.ActualHeight - 12;
                if (cornerPressed) e.Handled = true; // don't let it also select the tool
            };
            btn.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (!cornerPressed) return;
                cornerPressed = false;
                e.Handled = true;
                ShowToolGroupFlyout(btn, slotKey, members);
            };
            btn.MouseRightButtonUp += (s, e) => { e.Handled = true; ShowToolGroupFlyout(btn, slotKey, members); };
            btn.Click += (s, e) => SelectTool(_activeGroupMember.TryGetValue(slotKey, out var m) ? m : members[0]);
        }

        /// <summary>The group flyout. Laid out as a scrolling grid of icons rather than a list of
        /// named rows: the shape group runs to over a hundred entries, and a single vertical column
        /// of that many would be taller than the screen and hopeless to scan. Each cell is just the
        /// shape's silhouette with its name on a tooltip, which is how a shape picker is normally
        /// browsed - by eye.</summary>
        private void ShowToolGroupFlyout(ToggleButton btn, string slotKey, string[] members)
        {
            const int columns = 8;
            var grid = new UniformGrid { Columns = columns, Margin = new Thickness(3) };

            var popup = new System.Windows.Controls.Primitives.Popup
            {
                PlacementTarget = btn,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Right,
                StaysOpen = false,
                AllowsTransparency = true,
                IsOpen = false
            };

            var scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 420,
                Content = grid
            };

            var shell = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(1), Child = scroller };
            shell.SetResourceReference(Border.BackgroundProperty, "XpFaceDark");
            shell.SetResourceReference(Border.BorderBrushProperty, "PsAccent");
            popup.Child = shell;

            foreach (var member in members)
            {
                string captured = member;
                var cell = new Button
                {
                    Width = 34,
                    Height = 34,
                    Margin = new Thickness(1),
                    Padding = new Thickness(0),
                    BorderThickness = new Thickness(1),
                    ToolTip = _tools.TryGetValue(member, out var t) ? t.Name : member,
                    Content = MakeShapeGlyph(member, 22)
                };
                cell.Click += (s, e) =>
                {
                    popup.IsOpen = false;
                    _activeGroupMember[slotKey] = captured;
                    // Rebuild the slot's face so it shows whichever shape was just chosen, and
                    // re-label it to match - the tooltip was fixed at the slot's original name, so
                    // it still read "Star" after switching the slot to a heart.
                    btn.Content = MakeToolButtonContent(slotKey, ToolIconFileFor(slotKey));
                    btn.ToolTip = GroupSlotTooltip(slotKey, captured);
                    SelectTool(captured);
                };
                grid.Children.Add(cell);
            }

            popup.IsOpen = true;
        }

        /// <summary>A shape's toolbox glyph, generated from the shape's own outline rather than
        /// hand-drawn. With a catalogue this size, hand-authoring an icon per shape would be both
        /// enormous and a standing invitation for an icon to drift out of step with the shape it
        /// claims to represent - here they cannot disagree, because they're the same points.
        /// Falls back to the hand-drawn icon set for anything that isn't a library shape.</summary>
        private UIElement MakeShapeGlyph(string toolKey, double size)
        {
            var def = ShapeLibrary.ById(toolKey);
            if (def == null) return ToolIcons.Create(toolKey, size);

            // Rendered from a neutral context so a glyph shows the shape's own form, not whatever
            // rotation or star depth the tool options happen to be set to right now.
            var pts = def.Unit(new ToolContext());
            if (pts.Count < 3) return ToolIcons.Create(toolKey, size);

            var figure = new PathFigure { IsClosed = true, IsFilled = false };
            const double r = 11, c = 12; // fit the -1..1 unit square into a 24x24 box
            figure.StartPoint = new Point(c + pts[0].X * r, c + pts[0].Y * r);
            for (int i = 1; i < pts.Count; i++)
                figure.Segments.Add(new LineSegment(new Point(c + pts[i].X * r, c + pts[i].Y * r), true));

            var geo = new PathGeometry();
            geo.Figures.Add(figure);
            geo.Freeze();

            var path = new System.Windows.Shapes.Path
            {
                Data = geo,
                StrokeThickness = 1.3,
                StrokeLineJoin = PenLineJoin.Round
            };
            path.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "PsIconGlyph");

            var canvas = new Canvas { Width = 24, Height = 24 };
            canvas.Children.Add(path);
            return new Viewbox { Width = size, Height = size, Child = canvas, Stretch = Stretch.Uniform };
        }

        /// <summary>Icon-file fallback name for a slot (only used if a tool has no vector icon).</summary>
        private static string ToolIconFileFor(string slotKey) => slotKey.ToLowerInvariant();

        /// <summary>A grouped slot's tooltip - named for whichever member it's currently showing,
        /// not for the slot itself, plus the hint about how to reach the others.</summary>
        private string GroupSlotTooltip(string slotKey, string member)
        {
            string name = _tools.TryGetValue(member, out var t) ? t.Name : member;
            return $"{name} - click the corner (or right-click) for more shapes";
        }

        private void BuildPalette()
        {
            foreach (var c in ColorManager.ClassicPalette)
            {
                var swatch = new Border
                {
                    Background = new SolidColorBrush(c),
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(0.5),
                    Margin = new Thickness(0.5),
                    Cursor = Cursors.Hand,
                    ToolTip = ColorManager.DescribeColor(c)
                };
                swatch.MouseLeftButtonDown += (s, e) => _colors.SetForeground(c);
                swatch.MouseRightButtonDown += (s, e) => _colors.SetBackground(c);
                PaletteGrid.Children.Add(swatch);
            }
        }

        private void UpdateColorSwatches()
        {
            FgSwatch.Background = BrushForSwatch(_colors.Foreground);
            BgSwatch.Background = BrushForSwatch(_colors.Background);
            _selection.LastBackground = _colors.Background;
        }

        private Brush BrushForSwatch(Color c) =>
            c.A == 0 ? (Brush)FindResource("CheckerboardBrush") : new SolidColorBrush(c);

        private void TransparentSwatch_Click(object sender, MouseButtonEventArgs e) => _colors.SetForeground(Colors.Transparent);
        private void TransparentSwatch_RightClick(object sender, MouseButtonEventArgs e) => _colors.SetBackground(Colors.Transparent);

        // ===================================================================
        // Canvas resize handles (spec section 42 - drag-to-resize like classic Paint)
        // ===================================================================

        private FrameworkElement _handleRight, _handleBottom, _handleCorner;
        private FrameworkElement _activeManualDragHandle;
        private Point _manualDragLastPos;

        private void AddResizeHandles()
        {
            _handleRight = MakeResizeHandleVisual();
            _handleBottom = MakeResizeHandleVisual();
            _handleCorner = MakeResizeHandleVisual();

            WireManualDrag(_handleRight, Cursors.SizeWE, StartCanvasResizeDrag, (dx, dy) => ResizePreview(dx, 0), FinishCanvasResize);
            WireManualDrag(_handleBottom, Cursors.SizeNS, StartCanvasResizeDrag, (dx, dy) => ResizePreview(0, dy), FinishCanvasResize);
            WireManualDrag(_handleCorner, Cursors.SizeNWSE, StartCanvasResizeDrag, (dx, dy) => ResizePreview(dx, dy), FinishCanvasResize);

            HandleLayer.Children.Add(_handleRight);
            HandleLayer.Children.Add(_handleBottom);
            HandleLayer.Children.Add(_handleCorner);
        }

        /// <summary>Small white/black square used for both the canvas resize handles and the text-box
        /// resize handle - a plain Border rather than a Thumb, since we drive dragging manually.</summary>
        private Border MakeResizeHandleVisual() => new Border
        {
            Width = 7,
            Height = 7,
            Background = Brushes.White,
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(1)
        };

        /// <summary>
        /// Manual drag tracking that measures the mouse's movement relative to the top-level Window
        /// (a coordinate frame that never moves or rescales during the drag) instead of relative to
        /// the handle itself. This is the key fix for a visible "jump": WPF's built-in Thumb control
        /// computes DragDelta relative to the Thumb's own position, and our handles are repositioned
        /// every single resize tick (they have to - they sit on the edge of the thing being resized).
        /// The moment the handle's own layout position shifts underneath an active mouse capture -
        /// which also happens as a side effect whenever the ScrollViewer's scrollbar visibility
        /// toggles mid-drag - Thumb's math produces a spurious one-frame jump. Measuring against the
        /// Window instead sidesteps the whole problem, since the Window's own coordinate space is
        /// unaffected by anything happening inside its scrollable content.
        /// </summary>
        private void WireManualDrag(FrameworkElement handle, Cursor cursor, Action onStart, Action<double, double> onDelta, Action onEnd = null)
        {
            handle.Cursor = cursor;
            handle.MouseLeftButtonDown += (s, e) =>
            {
                onStart?.Invoke();
                _manualDragLastPos = e.GetPosition(this);
                _activeManualDragHandle = handle;
                handle.CaptureMouse();
                e.Handled = true;
            };
            handle.MouseMove += (s, e) =>
            {
                if (_activeManualDragHandle != handle || e.LeftButton != MouseButtonState.Pressed) return;
                var cur = e.GetPosition(this);
                double dx = cur.X - _manualDragLastPos.X;
                double dy = cur.Y - _manualDragLastPos.Y;
                _manualDragLastPos = cur;
                if (dx != 0 || dy != 0) onDelta(dx, dy);
                e.Handled = true;
            };
            handle.MouseLeftButtonUp += (s, e) =>
            {
                if (_activeManualDragHandle != handle) return;
                handle.ReleaseMouseCapture();
                _activeManualDragHandle = null;
                onEnd?.Invoke();
                e.Handled = true;
            };
        }

        private int _pendingResizeW, _pendingResizeH;

        private void StartCanvasResizeDrag()
        {
            FinalizeFloatingSelection();
            _dragStartW = _document.Width;
            _dragStartH = _document.Height;
            _dragAccumX = 0;
            _dragAccumY = 0;
            _pendingResizeW = _dragStartW;
            _pendingResizeH = _dragStartH;
            CanvasResizePreview.Visibility = Visibility.Visible;
            UpdateResizePreviewRect();
        }

        /// <summary>
        /// Called on every drag tick. Deliberately does NOT touch the real document - only moves a
        /// cheap dashed-outline rectangle and the handle squares. Resizing the actual bitmap is an
        /// O(width*height) copy (see PaintDocument.Resize), and doing that on every single
        /// mouse-move event was the reason dragging felt sluggish rather than buttery smooth. The
        /// real resize now happens exactly once, in FinishCanvasResize below.
        /// </summary>
        private void ResizePreview(double dx, double dy)
        {
            if (_document == null) return;
            _dragAccumX += dx;
            _dragAccumY += dy;
            _pendingResizeW = Math.Max(1, _dragStartW + (int)Math.Round(_dragAccumX / _zoom));
            _pendingResizeH = Math.Max(1, _dragStartH + (int)Math.Round(_dragAccumY / _zoom));
            UpdateResizePreviewRect();
            PositionHandlesForSize(_pendingResizeW * _zoom, _pendingResizeH * _zoom);
            StatusSize.Text = $"{_pendingResizeW} x {_pendingResizeH}px";
        }

        private void UpdateResizePreviewRect()
        {
            CanvasResizePreview.Width = _pendingResizeW * _zoom;
            CanvasResizePreview.Height = _pendingResizeH * _zoom;
        }

        private void FinishCanvasResize()
        {
            CanvasResizePreview.Visibility = Visibility.Collapsed;
            if (_document == null) return;
            if (_pendingResizeW == _document.Width && _pendingResizeH == _document.Height)
            {
                UpdateStatusSize();
                return;
            }
            _history.PushUndoState(_document, "Resize Canvas"); // one undo entry for the whole drag
            _document.Resize(_pendingResizeW, _pendingResizeH, _colors.Background);
            RefreshCanvasBinding();
            UpdateStatusSize();
        }

        /// <summary>Moves the (manually-positioned) resize handles to track the given size in screen
        /// pixels - used both for the real committed size (PositionCanvasResizeHandles) and for the
        /// live rubber-band preview size while dragging (ResizePreview). Safe to update mid-drag now
        /// (unlike the old Thumb-based approach) since dragging is tracked relative to the Window,
        /// not relative to the handle's own position.</summary>
        private void PositionHandlesForSize(double w, double h)
        {
            if (_handleRight == null) return;
            Canvas.SetLeft(_handleRight, w - 3); Canvas.SetTop(_handleRight, h / 2 - 3);
            Canvas.SetLeft(_handleBottom, w / 2 - 3); Canvas.SetTop(_handleBottom, h - 3);
            Canvas.SetLeft(_handleCorner, w - 3); Canvas.SetTop(_handleCorner, h - 3);
        }

        /// <summary>Called from UpdateStatusSize(), which already runs after every size-changing
        /// operation (Attributes, rotate, stretch/skew, new/open, ...).</summary>
        private void PositionCanvasResizeHandles()
        {
            if (_document == null) return;
            PositionHandlesForSize(_document.Width * _zoom, _document.Height * _zoom);
        }

        /// <summary>Rebinds the canvas to the current document AND resyncs everything that depends
        /// on the document's dimensions. Every operation that can change those dimensions goes
        /// through here, because several of them (rotate, flip, stretch/skew) previously called
        /// only RefreshDocumentBinding and skipped UpdateStatusSize - which is what repositions the
        /// canvas resize handles, so after a 90-degree rotate the handles stayed sitting at the
        /// pre-rotate width/height instead of following the canvas's new shape.</summary>
        private void RefreshCanvasBinding()
        {
            _canvas.RefreshDocumentBinding();
            UpdateStatusSize();       // also repositions the canvas resize handles
            RefreshSelectionHandles(); // and keep any selection handles in step too
        }

        // ===================================================================
        // Document lifecycle
        // ===================================================================

        private void WireDocumentLayerEvents()
        {
            _document.LayersChanged += (s, e) =>
            {
                _canvas.RefreshLayers();
                LayersPanelCtl.Refresh();
            };
            LayersPanelCtl.SetDocument(_document, _history); // the Layers panel was pointing at the old document
        }

        private void NewDocument(int width, int height, bool promptSave = true)
        {
            if (promptSave && !ConfirmDiscardChanges()) return;

            // Start the way a fresh document is normally expected to: an opaque white Background
            // to paint against, plus an empty transparent layer above it which is the one selected,
            // so the first stroke lands on its own layer instead of straight onto the background.
            _document = new PaintDocument(width, height, Colors.White);
            _document.Layers[0].Name = "Background";
            _document.AddLayer("Layer 1");   // AddLayer makes the new layer active
            WireDocumentLayerEvents();
            _canvas.SetDocument(_document);
            _canvas.Zoom = _zoom;
            _pendingShape = null; // document is being replaced - don't carry a shape across into it
            _selection.Deselect(_document.Surface);
            _canvas.ShowSelection(null);
            _history.Clear();
            UpdateTitle();
            UpdateStatusSize();
            RebuildContext();
            UpdateGridOverlay();
        }

        private void RebuildContext()
        {
            _ctx = new ToolContext
            {
                Document = _document,
                Canvas = _canvas,
                Colors = _colors,
                History = _history,
                Selection = _selection,
                PenSize = _ctx?.PenSize ?? 1,
                ShapeFillMode = _ctx?.ShapeFillMode ?? ShapeFillMode.OutlineOnly,
                BrushShape = _ctx?.BrushShape ?? BrushShape.Round,
                ArrowStyle = _ctx?.ArrowStyle ?? ArrowStyle.End,
                AntiAlias = _ctx?.AntiAlias ?? false,
                WandTolerance = _ctx?.WandTolerance ?? 0,
                WandContiguous = _ctx?.WandContiguous ?? true,
                GradientType = _ctx?.GradientType ?? GradientType.Linear,
                GradientDither = _ctx?.GradientDither ?? true,
                StarPoints = _ctx?.StarPoints ?? 5,
                SetStatusText = t => StatusText.Text = t,
                BeginTextEditing = BeginTextEditing,
                BeginTextEditOnActiveLayer = BeginTextEditOnActiveLayer,
                RequestToolSwitch = SelectTool,
                BeginPendingShape = BeginPendingShape,
                FinalizePendingShape = FinalizePendingShapeInPlace
            };
        }

        private bool ConfirmDiscardChanges()
        {
            // Don't let the "return to the drawing tool" restore fire here - this runs during
            // window close and on New/Open, where rebuilding tool UI is pointless at best.
            _suppressToolRestore = true;
            try { FinalizeFloatingSelection(); } finally { _suppressToolRestore = false; }
            if (_document == null || !_document.IsDirty) return true;
            var name = _document.FilePath == null ? "untitled" : Path.GetFileName(_document.FilePath);
            var result = MessageBox.Show(this,
                $"Save changes to {name}?", "Splash",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (result == MessageBoxResult.Cancel) return false;
            if (result == MessageBoxResult.Yes) return DoSave();
            return true;
        }

        private bool _suppressToolRestore;

        private void FinalizeFloatingSelection()
        {
            if (_selection.IsFloating)
            {
                // A pending shape hasn't touched the document yet, so its undo state has to be
                // pushed now, at the moment it actually gets rasterized - not back when it was
                // drawn (nothing had changed in the document then).
                string restoreTool = null;
                if (_pendingShape != null)
                {
                    _history.PushUndoState(_document, _pendingShape.Label);
                    restoreTool = _pendingShape.OriginToolKey;
                    _pendingShape = null;
                }
                _selection.Commit(_document.Surface);
                _canvas.ClearPreview();
                _document.MarkDirty();

                // Once the shape has been dropped, go back to the tool that drew it. Otherwise
                // drawing several arrows in a row means re-picking Arrow after every single one,
                // because placing a shape always leaves you on the Select tool. Suppressed when
                // this runs from inside SelectTool, since there the user is explicitly choosing a
                // different tool and that choice must win.
                if (restoreTool != null && !_suppressToolRestore && _tools.ContainsKey(restoreTool))
                    SelectTool(restoreTool);
            }
            else
            {
                // Selection was cleared without ever being committed (e.g. Escape) - drop the
                // pending shape too rather than leaving it dangling for the next commit.
                _pendingShape = null;
            }
        }

        /// <summary>The narrower counterpart to FinalizeFloatingSelection, used when a drag-shape
        /// tool (Rectangle, Ellipse, ...) commits its own still-pending shape in place - e.g.
        /// because the user clicked outside it to start the next one, right where they clicked, in
        /// the very same gesture. Unlike FinalizeFloatingSelection this never switches tools: the
        /// tool that's about to draw the next shape is already current, so there's nothing to
        /// restore. Also handles the (normally unreachable, since SelectTool's own switch-away
        /// cleanup prevents it) edge case of a stray non-floating selection somehow still being
        /// present, so a shape tool's OnMouseDown never has to reason about that itself.</summary>
        private void FinalizePendingShapeInPlace()
        {
            if (!_selection.HasSelection) return;
            if (_selection.IsFloating)
            {
                if (_pendingShape != null)
                {
                    _history.PushUndoState(_document, _pendingShape.Label);
                    _pendingShape = null;
                }
                _selection.Commit(_document.Surface);
                _canvas.ClearPreview();
                _document.MarkDirty();
            }
            // Commit() leaves Bounds set (a plain marquee over the now-rasterized result) rather
            // than clearing the selection outright - drop that too, so it doesn't linger onscreen
            // while the next shape is drawn.
            _selection.Discard();
            _canvas.ShowSelection(null);
            RefreshSelectionHandles();
        }

        /// <summary>Takes a shape that a shape tool drew but deliberately did NOT rasterize, and
        /// sets it up as a floating selection: rendered into a floating bitmap for display, with
        /// the document itself still untouched. While it stays selected, every move/resize
        /// re-renders it from its original defining points (see RenderPendingShapeToFloat) instead
        /// of resampling the previous render - so adjusting it repeatedly costs nothing in quality.
        /// It's only rasterized into the document once the selection is finally committed - which,
        /// per DragShapeToolBase, the same tool that drew it can now do on its own (move it by
        /// dragging its body, resize it via the same handles Select uses, or click away to commit
        /// it and start the next one) without ever switching to the Select tool.</summary>
        private void BeginPendingShape(PendingShape shape)
        {
            _pendingShape = shape;
            var bounds = PendingShapeBounds(shape.Start, shape.End, shape.Pad);
            RenderPendingShapeToFloat(bounds);
            _canvas.ClearPreview();
            SelectRectToolRenderHelper();
            _canvas.ShowSelection(bounds);
            RefreshSelectionHandles();
            StatusText.Text = $"{shape.Label} - drag it to move, resize with the handles, or click elsewhere to start another.";
        }

        /// <summary>Bounds for a pending shape's defining points, padded for stroke thickness.
        ///
        /// Deliberately NOT clamped to the canvas. Clamping is what made a shape dragged out past
        /// an edge snap back inside on release: the shape is re-rendered by mapping its defining
        /// points into whatever box this returns (see RenderPendingShapeToFloat), so cropping the
        /// box to the canvas silently redrew the whole shape smaller, inside it, rather than
        /// leaving the part that overhangs simply not visible. Keeping the true bounds - negative
        /// origins included - means a shape that hangs off the edge stays exactly the shape it was
        /// drawn as, and can still be dragged back into view afterwards.
        ///
        /// Nothing downstream needs the clamp: RasterSurface.Blit already skips destination pixels
        /// outside the surface, so committing an overhanging shape writes only the part that lands
        /// on the canvas. The size is still capped, though - purely to stop a wild drag from
        /// allocating an enormous bitmap.</summary>
        private Int32Rect PendingShapeBounds(Point start, Point end, int pad)
        {
            int x0 = (int)Math.Min(start.X, end.X), y0 = (int)Math.Min(start.Y, end.Y);
            int x1 = (int)Math.Max(start.X, end.X), y1 = (int)Math.Max(start.Y, end.Y);

            int px = x0 - pad;
            int py = y0 - pad;
            int pw = (x1 + pad) - px;
            int ph = (y1 + pad) - py;

            int maxW = Math.Max(64, _document.Width * 4);
            int maxH = Math.Max(64, _document.Height * 4);
            return new Int32Rect(px, py, Math.Clamp(pw, 1, maxW), Math.Clamp(ph, 1, maxH));
        }

        /// <summary>Renders the pending shape fresh into a transparent bitmap sized to the given
        /// bounds, and installs it as the floating selection content. The shape's defining points
        /// are mapped proportionally into the new bounds, so a resize genuinely re-draws the shape
        /// at the new size (crisp at any scale) rather than stretching pixels from a prior render.</summary>
        private void RenderPendingShapeToFloat(Int32Rect bounds)
        {
            var surface = new RasterSurface(bounds.Width, bounds.Height, Colors.Transparent);

            // Map the original points into bounds-local space. The pad inset is what keeps a thick
            // stroke from being clipped at the bitmap's edge.
            int pad = _pendingShape.Pad;
            double localX0 = pad, localY0 = pad;
            double localX1 = Math.Max(localX0, bounds.Width - 1 - pad);
            double localY1 = Math.Max(localY0, bounds.Height - 1 - pad);

            // Preserve which corner was the drag origin, so shapes whose rendering isn't symmetric
            // in start/end still come out oriented the way they were originally drawn.
            bool startIsLeft = _pendingShape.Start.X <= _pendingShape.End.X;
            bool startIsTop = _pendingShape.Start.Y <= _pendingShape.End.Y;
            var s = new Point(startIsLeft ? localX0 : localX1, startIsTop ? localY0 : localY1);
            var en = new Point(startIsLeft ? localX1 : localX0, startIsTop ? localY1 : localY0);

            surface.Lock();
            _pendingShape.Render(s, en, surface);
            surface.Unlock();

            _selection.BeginPaste(surface.Bitmap, bounds.X, bounds.Y); // floating, nothing to vacate
        }

        private void UpdateTitle()
        {
            var name = _document.FilePath == null ? "untitled" : Path.GetFileName(_document.FilePath);
            Title = $"{name} - Splash";
        }

        private void UpdateStatusSize()
        {
            StatusSize.Text = _document == null ? "" : $"{_document.Width} x {_document.Height}px @ {(int)Math.Round(_document.DpiX)} DPI";
            PositionCanvasResizeHandles();
        }

        // ===================================================================
        // Tool switching / routing
        // ===================================================================

        private readonly Dictionary<string, int> _toolSizes = new();
        private readonly Dictionary<string, bool> _toolAntiAlias = new();

        /// <summary>Sensible anti-aliasing default per tool. Pixel-precision tools (pencil, eraser,
        /// fill, magic wand) default off, because a soft edge there is actively unhelpful - a
        /// half-covered pixel isn't the exact colour you asked for, which breaks flood-fill and
        /// pixel editing. Curved and diagonal shapes default on, where smoothing is the whole point.</summary>
        private static bool DefaultAntiAliasForTool(string key) => key switch
        {
            "Pencil" or "Eraser" or "Fill" or "MagicWand" or "Pick" => false,
            "Curve" or "Ellipse" or "RoundedRectangle" or "Star" or "Arrow" or "Line" or "Polygon" => true,
            _ => false,
        };

        private static int DefaultSizeForTool(string key) => key switch
        {
            "Pencil" => 1,
            "Eraser" => 4,
            "Brush" or "Airbrush" => 3,
            "Line" or "Rectangle" or "Ellipse" or "RoundedRectangle" or "Polygon" or "Curve"
                or "Arrow" or "Star" or "Gradient" => 2,
            _ => 2,
        };

        private void SelectTool(string key)
        {
            if (!_tools.ContainsKey(key)) return;

            // switching away from a selection tool finalizes any floating content
            if (_currentToolKey != key)
            {
                _tools[_currentToolKey]?.Cancel(_ctx);
                // The user is picking a tool explicitly here, so their choice must take precedence
                // over the "return to the drawing tool" restore inside FinalizeFloatingSelection.
                _suppressToolRestore = true;
                try { FinalizeFloatingSelection(); } finally { _suppressToolRestore = false; }

                // A shape tool leaves its just-drawn bounds selected so switching to a selection
                // tool lets you immediately move it (see DragShapeToolBase.OnMouseUp) - but if
                // you're switching to anything else, tidy that marquee up rather than leaving it
                // lingering while you draw with a different tool. Also catches a small
                // pre-existing gap: FinalizeFloatingSelection above commits pixels but never
                // cleared the marching-ants display on its own.
                if (key is not ("Select" or "FreeFormSelect" or "MagicWand") && _selection.HasSelection)
                {
                    _selection.Deselect(_document.Surface);
                    _canvas.ShowSelection(null);
                }

                // Remember what was active before switching TO the eyedropper, so AfterColorPick
                // can restore it - matching classic Paint's actual behavior (this was previously a
                // documented simplification: "stays on the eyedropper instead").
                if (key == "Pick" && _currentToolKey != "Pick")
                    _toolBeforePick = _currentToolKey;

                // Each tool remembers its own size independently - save the outgoing tool's
                // current size before switching, so coming back to it later restores it rather
                // than inheriting whatever size a completely different tool was just using.
                _toolSizes[_currentToolKey] = _ctx.PenSize;
                _toolAntiAlias[_currentToolKey] = _ctx.AntiAlias;
            }

            _currentToolKey = key;
            _ctx.PenSize = _toolSizes.TryGetValue(key, out var savedSize) ? savedSize : DefaultSizeForTool(key);
            _ctx.AntiAlias = _toolAntiAlias.TryGetValue(key, out var savedAA) ? savedAA : DefaultAntiAliasForTool(key);
            // A tool inside a group highlights the slot it lives in, since that's the button on
            // screen - "Heart" has no button of its own, it's shown by the shape slot.
            string highlightKey = GroupSlotFor(key) ?? key;
            foreach (var kv in _toolButtons) kv.Value.IsChecked = kv.Key == highlightKey;
            if (key != "Eraser") EraserOutline.Visibility = Visibility.Collapsed;
            _canvas.Cursor = GetCursorForTool(key);

            StatusText.Text = _tools[key].StatusHint;
            BuildToolOptions(key);
            RefreshSelectionHandles();
        }

        private readonly Dictionary<string, Cursor> _cursorCache = new();

        // Deliberately limited to Fill only. The pencil/brush/eraser/airbrush/eyedropper cursors
        // used to use custom tool-shaped .cur files too, but their hotspot coordinates were my
        // best visual estimate, made without any way to pixel-verify them against a real running
        // app - and a wrong hotspot on a *precision* tool is exactly what "there's a gap between
        // where it's drawing and where the cursor is" looks like. For every tool where hitting the
        // exact intended pixel matters, correctness now wins over the cosmetic tool-shaped cursor:
        // they use Cursors.Cross, a WPF built-in with a guaranteed-centered hotspot. Fill is the
        // one exception, since flood fill only needs to land anywhere inside the target region, not
        // on one exact pixel, so a slightly-off hotspot there doesn't cause a functional problem.
        private static readonly Dictionary<string, string> ToolCursorFile = new()
        {
            ["Fill"] = "fill",
        };

        /// <summary>Loads and caches the one remaining custom tool-shaped cursor (Fill). Every
        /// other tool uses a built-in cursor instead - see the comment on ToolCursorFile above for
        /// why.</summary>
        private Cursor GetCursorForTool(string key)
        {
            if (ToolCursorFile.TryGetValue(key, out var file))
            {
                if (_cursorCache.TryGetValue(key, out var cached)) return cached;
                try
                {
                    var uri = new Uri($"pack://application:,,,/Resources/Cursors/{file}.cur", UriKind.Absolute);
                    var stream = Application.GetResourceStream(uri)?.Stream;
                    if (stream != null)
                    {
                        var cur = new Cursor(stream);
                        _cursorCache[key] = cur;
                        return cur;
                    }
                }
                catch
                {
                    // Fall through to the built-in fallback below if the cursor resource can't load.
                }
            }

            // Anything that drags out a shape wants the same precision cross. Tested by tool type
            // rather than by name so the shape family (Triangle, Heart, ...) is covered without
            // having to be re-listed here - they were falling through to a plain arrow, which is
            // the wrong cursor for a tool you aim with.
            if (_tools.TryGetValue(key, out var tool) && tool is DragShapeToolBase) return Cursors.Cross;

            return key switch
            {
                "Text" => Cursors.IBeam,
                "Line" or "Rectangle" or "Ellipse" or "RoundedRectangle" or "Polygon" or "Curve"
                    or "Arrow" or "Gradient" => Cursors.Cross,
                "Select" or "FreeFormSelect" or "MagicWand" => Cursors.Cross,
                "Magnifier" => Cursors.Cross,
                "Pencil" or "Brush" or "Eraser" or "Airbrush" or "Pick" => Cursors.Cross,
                _ => Cursors.Arrow,
            };
        }

        private ScreenPickSession _screenPick;

        /// <summary>Starts an off-window colour pick: the pointer is followed anywhere on screen and
        /// the foreground colour previews live, committed on the next click or abandoned on Escape.
        /// Only reads the single pixel under the pointer, and only for the duration of the gesture.</summary>
        private void BeginScreenColorPick()
        {
            if (_screenPick != null) return; // one at a time
            var original = _colors.Foreground;
            string previousStatus = StatusText.Text;

            _screenPick = new ScreenPickSession(
                _canvas,
                preview => _colors.SetForeground(preview),
                result =>
                {
                    _screenPick = null;
                    // Cancelled (Escape, or capture lost) - put back whatever colour was in use
                    // before the preview started rather than leaving a half-finished pick applied.
                    _colors.SetForeground(result ?? original);
                    StatusText.Text = result.HasValue
                        ? $"Picked {ColorManager.DescribeColor(result.Value).Split('\n')[0]} from screen."
                        : previousStatus;
                });

            if (!_screenPick.Start())
            {
                _screenPick = null;
                StatusText.Text = "Couldn't start the screen colour pick - try again.";
                return;
            }
            StatusText.Text = "Move to any colour on screen, click to pick it, or press Esc to cancel.";
        }

        private void AfterColorPick()
        {
            // Classic Paint returns to whatever tool was active before you picked a color.
            if (_toolBeforePick != null)
            {
                var restore = _toolBeforePick;
                _toolBeforePick = null; // clear first so SelectTool below doesn't re-arm it
                SelectTool(restore);
            }
        }

        private bool _isFullScreen;
        private WindowState _preFullScreenState;
        private WindowStyle _preFullScreenStyle;
        private ResizeMode _preFullScreenResizeMode;

        /// <summary>Toggles borderless full screen (F11). Remembers the exact window state it was
        /// in beforehand, so exiting restores what the user actually had rather than assuming
        /// "normal, maximized".</summary>
        /// <summary>F11: shows the picture on its own, filling the screen with every part of the
        /// editing UI hidden - menus, toolbox, palette and status bar all go away, the way a
        /// full-screen preview works in a modern image editor. The picture is shown scaled to fit
        /// on a neutral dark backdrop; the document itself is untouched, this is purely a view.</summary>
        private void ToggleFullScreen()
        {
            if (!_isFullScreen)
            {
                // Commit anything still floating first, so what's shown full screen is the actual
                // finished picture rather than a picture plus a detached in-progress selection.
                FinalizeFloatingSelection();

                _preFullScreenState = WindowState;
                _preFullScreenStyle = WindowStyle;
                _preFullScreenResizeMode = ResizeMode;

                FullScreenImage.Source = _document.GetFlattenedBitmap();
                FullScreenOverlay.Visibility = Visibility.Visible;
                FullScreenOverlay.Focus();

                // Going to Normal first is required: WindowStyle can't be changed while a window
                // is already Maximized, and without this the window keeps its old chrome and stops
                // short of covering the taskbar.
                WindowState = WindowState.Normal;
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
                _isFullScreen = true;
            }
            else
            {
                FullScreenOverlay.Visibility = Visibility.Collapsed;
                FullScreenImage.Source = null; // don't pin a whole extra copy of the picture in memory

                WindowState = WindowState.Normal;
                WindowStyle = _preFullScreenStyle;
                ResizeMode = _preFullScreenResizeMode;
                WindowState = _preFullScreenState;
                _isFullScreen = false;
                StatusText.Text = _tools[_currentToolKey].StatusHint;
            }
        }

        private void FullScreenOverlay_Click(object sender, MouseButtonEventArgs e)
        {
            // Clicking anywhere leaves full screen, which is what people reach for first.
            if (_isFullScreen) ToggleFullScreen();
        }


        private void CycleZoom(MouseButton btn)
        {
            var levels = MagnifierTool.Levels;
            int idx = Array.IndexOf(levels, _zoom);
            if (idx < 0) idx = 0;
            idx = btn == MouseButton.Right ? Math.Max(0, idx - 1) : Math.Min(levels.Length - 1, idx + 1);
            SetZoom(levels[idx]);
        }

        /// <summary>Drag-to-zoom: picks the largest preset zoom level (from MagnifierTool.Levels)
        /// that still fits the whole dragged region within the visible viewport, then scrolls so
        /// that region is centered - "zoom to area" rather than just cycling through fixed steps.</summary>
        private void ZoomToArea(Int32Rect r)
        {
            double viewportW = CanvasScroller.ViewportWidth > 0 ? CanvasScroller.ViewportWidth : CanvasScroller.ActualWidth;
            double viewportH = CanvasScroller.ViewportHeight > 0 ? CanvasScroller.ViewportHeight : CanvasScroller.ActualHeight;
            if (viewportW <= 0 || viewportH <= 0) { SetZoom(1); return; } // not laid out yet - safe fallback

            var levels = MagnifierTool.Levels;
            double best = levels[0];
            foreach (var lvl in levels)
                if (r.Width * lvl <= viewportW && r.Height * lvl <= viewportH)
                    best = lvl; // levels is ascending, so the last one that still fits wins

            SetZoom(best);

            double centerXDoc = r.X + r.Width / 2.0;
            double centerYDoc = r.Y + r.Height / 2.0;

            // Defer the scroll until after the layout pass triggered by SetZoom (which resizes
            // the canvas) has actually updated the ScrollViewer's scrollable extent - scrolling
            // immediately would measure against a stale extent and could clamp to the wrong spot.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                CanvasScroller.ScrollToHorizontalOffset(Math.Max(0, centerXDoc * best - viewportW / 2.0));
                CanvasScroller.ScrollToVerticalOffset(Math.Max(0, centerYDoc * best - viewportH / 2.0));
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void SetZoom(double z)
        {
            _zoom = z;
            _canvas.Zoom = z;
            UpdateGridOverlay();
            PositionCanvasResizeHandles();
            RepositionActiveTextBoxForZoom();
            RefreshSelectionHandles();
            // Refresh the Magnifier's own Zoom dropdown so it reflects a zoom changed from
            // elsewhere (the View menu, Ctrl +/-). Skipped when the change came *from* that
            // dropdown: rebuilding the options bar clears its children, which would destroy the
            // ComboBox whose SelectionChanged handler is still on the stack.
            if (_currentToolKey == "Magnifier" && !_suppressZoomOptionRebuild) BuildToolOptions("Magnifier");
        }

        private bool _suppressZoomOptionRebuild;

        /// <summary>Keeps the live text box's on-screen size/position/font size in sync with the
        /// current zoom, using _activeTextDocRect (true document-space, unaffected by zoom) as the
        /// source of truth. Without this, zooming while editing text left the box's screen geometry
        /// stale relative to the new zoom - a real gap, since later re-deriving document coordinates
        /// from that stale screen state could produce a wrong commit position/size.</summary>
        private void RepositionActiveTextBoxForZoom()
        {
            if (_activeTextBox == null) return;
            _activeTextBox.Width = _activeTextDocRect.Width * _zoom;
            _activeTextBox.Height = _activeTextDocRect.Height * _zoom;
            _activeTextBox.FontSize = Math.Max(8, _textFontSize * _zoom);
            Canvas.SetLeft(_activeTextBox, _activeTextDocRect.X * _zoom);
            Canvas.SetTop(_activeTextBox, _activeTextDocRect.Y * _zoom);
            PositionTextResizeHandle();
            PositionTextMoveHandle();
        }

        private void UpdateGridOverlay()
        {
            if (!_showGrid || _document == null || _zoom < 4)
            {
                GridOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            double cell = _zoom;
            var geometry = new GeometryGroup();
            geometry.Children.Add(new LineGeometry(new Point(0, 0), new Point(cell, 0)));
            geometry.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, cell)));
            var drawing = new GeometryDrawing
            {
                Pen = new Pen(new SolidColorBrush(Color.FromArgb(120, 128, 128, 128)), 1)
            };
            drawing.Geometry = geometry;

            GridOverlay.Fill = new DrawingBrush
            {
                Drawing = drawing,
                Viewport = new Rect(0, 0, cell, cell),
                ViewportUnits = BrushMappingMode.Absolute,
                TileMode = TileMode.Tile
            };
            GridOverlay.Visibility = Visibility.Visible;
        }

        /// <summary>Tools that never paint into Document.Surface directly - everything else does,
        /// at some point, which is exactly what RasterizeActiveLayerIfText below guards against.</summary>
        private static readonly HashSet<string> ToolsThatDontTouchLayerPixels = new()
        {
            "Select", "FreeFormSelect", "MagicWand", "Text", "Magnifier", "Pick"
        };

        /// <summary>A text layer's Surface is only ever a rendered cache of its TextLayerData,
        /// regenerated from scratch on every re-edit, move, resize, or document resize (see
        /// Services/TextLayerRenderer). Painting into it directly with any other tool would look
        /// right until the next time that regeneration happens - which would silently wipe the
        /// paint back out, since it isn't part of what gets re-rendered. Rasterizing the layer
        /// first, the instant a painting tool is about to touch it, is the same explicit action the
        /// Layers panel's own Rasterize button performs, just triggered automatically instead of
        /// requiring the user to notice and do it themselves first.</summary>
        private void RasterizeActiveLayerIfText()
        {
            if (_document?.ActiveLayer.Text == null) return;
            _history.PushUndoState(_document, "Rasterize Text Layer");
            _document.RasterizeLayer(_document.ActiveLayerIndex);
        }

        private void Canvas_MouseDown(object sender, CanvasMouseEventArgs e)
        {
            try
            {
                // Back on the canvas: whatever was being adjusted in the options bar is done, so an
                // open text box is free to commit on focus loss again (see _suppressTextCommit).
                _suppressTextCommit = false;

                if (_currentToolKey == "Polygon" && e.ClickCount >= 2)
                {
                    ((PolygonTool)_tools["Polygon"]).Finish(_ctx);
                    return;
                }
                if (!ToolsThatDontTouchLayerPixels.Contains(_currentToolKey)) RasterizeActiveLayerIfText();

                // A click inside an existing selection (or a still-pending shape, for a drag-shape
                // tool) is about to start a move, not a new one - switch to a move cursor for the
                // duration of the drag.
                bool willMoveSelection = CurrentToolHandlesSelection()
                    && _selection.HasSelection && _selection.Contains(e.DocPointInt);
                _tools[_currentToolKey].OnMouseDown(_ctx, e);
                if (willMoveSelection) _canvas.Cursor = Cursors.SizeAll;
            }
            catch (Exception ex)
            {
                ReportToolError(ex);
            }
        }

        private void Canvas_MouseMove(object sender, CanvasMouseEventArgs e)
        {
            try
            {
                UpdateStatusCoords(e.DocPointInt);
                UpdateEraserOutline(e.DocPointInt);
                _tools[_currentToolKey].OnMouseMove(_ctx, e);
            }
            catch (Exception ex)
            {
                ReportToolError(ex);
            }
        }

        /// <summary>Shows a live square outline tracking the mouse, matching the eraser's actual
        /// footprint (same size formula EraserTool.Deposit uses) - so you can see the "rubber"
        /// boundary before you erase, not just after.</summary>
        private Point _lastCanvasDocPoint;

        private void UpdateEraserOutline(Point docPoint)
        {
            _lastCanvasDocPoint = docPoint;
            if (_currentToolKey != "Eraser" || _document == null)
            {
                EraserOutline.Visibility = Visibility.Collapsed;
                return;
            }
            int size = Math.Max(4, _ctx.PenSize * 2);
            double screenSize = size * _zoom;
            EraserOutline.Width = screenSize;
            EraserOutline.Height = screenSize;
            // Canvas.Left/Top, not Margin - a Canvas child's position doesn't feed into layout, so
            // the outline can follow the mouse past the canvas edge without stretching it.
            Canvas.SetLeft(EraserOutline, (docPoint.X - size / 2.0) * _zoom);
            Canvas.SetTop(EraserOutline, (docPoint.Y - size / 2.0) * _zoom);
            EraserOutline.Visibility = Visibility.Visible;
        }

        private void Canvas_MouseUp(object sender, CanvasMouseEventArgs e)
        {
            try
            {
                _tools[_currentToolKey].OnMouseUp(_ctx, e);
                _canvas.Cursor = GetCursorForTool(_currentToolKey); // undo any temporary move cursor
            }
            catch (Exception ex)
            {
                ReportToolError(ex);
            }
        }

        /// <summary>Surfaces an unexpected exception from a tool operation as a status message
        /// instead of letting it propagate silently, and resets the current tool's internal state
        /// (e.g. an "in progress" flag that would otherwise stay stuck true/false forever) so the
        /// tool is immediately usable again rather than appearing permanently broken.</summary>
        private void ReportToolError(Exception ex)
        {
            try { _tools[_currentToolKey]?.Cancel(_ctx); } catch { /* best-effort reset only */ }
            _canvas.ClearPreview();
            StatusText.Text = $"That didn't work ({ex.GetType().Name}: {ex.Message}) - try again.";
        }

        private void UpdateStatusCoords(Point p)
        {
            StatusCoords.Text = $"{(int)p.X},{(int)p.Y}";
        }

        private readonly ShortcutManager _shortcuts = new();
        private Dictionary<string, Action> _actionHandlers;

        private void DeselectShortcut()
        {
            FinalizeFloatingSelection();
            _selection.Deselect(_document.Surface);
            _canvas.ShowSelection(null);
            _canvas.ClearPreview();
        }

        private void CycleZoomStep(int direction)
        {
            var levels = MagnifierTool.Levels;
            int idx = Array.IndexOf(levels, _zoom);
            if (idx < 0) idx = 0;
            idx = Math.Max(0, Math.Min(levels.Length - 1, idx + direction));
            SetZoom(levels[idx]);
        }

        private void AdjustPenSize(int direction)
        {
            _ctx.PenSize = Math.Max(1, Math.Min(50, _ctx.PenSize + direction));
            BuildToolOptions(_currentToolKey); // refresh so the highlighted size button matches
            // Redraw the eraser footprint right away at the cursor's last known position.
            // UpdateEraserOutline is otherwise only driven by Canvas_MouseMove, so without this the
            // ring kept its old size until the mouse happened to move - making a Ctrl+/Ctrl- size
            // change look like it hadn't taken effect at all.
            if (_currentToolKey == "Eraser") UpdateEraserOutline(_lastCanvasDocPoint);
            StatusText.Text = $"Size: {_ctx.PenSize}px";
        }

        private void NudgeSelectionIfAny(int dx, int dy)
        {
            if (_selection.HasSelection) NudgeSelection(dx, dy);
        }

        /// <summary>Builds the id -> implementation table the Shortcut Manager's bindings dispatch
        /// through. Every entry here corresponds to one Def in ShortcutManager.Defaults - adding a
        /// new shortcut means adding it in both places.</summary>
        private void BuildActionHandlers()
        {
            _actionHandlers = new Dictionary<string, Action>
            {
                ["New"] = () => New_Click(null, null),
                ["Open"] = () => Open_Click(null, null),
                ["Save"] = () => Save_Click(null, null),
                ["Print"] = () => Print_Click(null, null),
                ["Undo"] = () => Undo_Click(null, null),
                ["Redo"] = () => Redo_Click(null, null),
                ["Cut"] = () => Cut_Click(null, null),
                ["Copy"] = () => Copy_Click(null, null),
                ["Paste"] = () => Paste_Click(null, null),
                ["SelectAll"] = () => SelectAll_Click(null, null),
                ["Deselect"] = DeselectShortcut,
                ["ClearSelection"] = () => ClearSelection_Click(null, null),
                ["Invert"] = () => Invert_Click(null, null),
                ["Attributes"] = () => Attributes_Click(null, null),
                ["Cancel"] = () =>
                {
                    _tools[_currentToolKey].Cancel(_ctx);
                    // A pending shape has never touched the document, so Escape can genuinely
                    // discard it - no undo entry needed, because there's nothing to undo.
                    if (_pendingShape != null)
                    {
                        _pendingShape = null;
                        _selection.Discard(); // Discard, not Deselect - Deselect would commit it
                        _canvas.ClearPreview();
                    }
                    _canvas.ShowSelection(_selection.Bounds);
                },
                ["ZoomIn"] = () => CycleZoomStep(1),
                ["ZoomOut"] = () => CycleZoomStep(-1),
                ["ResetZoom"] = () => SetZoom(1),
                ["ToggleFullScreen"] = ToggleFullScreen,
                ["SizeUp"] = () => AdjustPenSize(1),
                ["SizeDown"] = () => AdjustPenSize(-1),
                ["SizeUpBracket"] = () => AdjustPenSize(1),
                ["SizeDownBracket"] = () => AdjustPenSize(-1),
                ["MoveSelectionLeft"] = () => NudgeSelectionIfAny(-1, 0),
                ["MoveSelectionRight"] = () => NudgeSelectionIfAny(1, 0),
                ["MoveSelectionUp"] = () => NudgeSelectionIfAny(0, -1),
                ["MoveSelectionDown"] = () => NudgeSelectionIfAny(0, 1),
            };
            foreach (var def in ShortcutManager.Defaults)
            {
                if (!def.Id.StartsWith("Tool_")) continue;
                string toolKey = def.Id.Substring("Tool_".Length);
                _actionHandlers[def.Id] = () => SelectTool(toolKey);
            }
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_activeTextBox != null) return; // let typing through untouched

            // Any focused text field must get its keystrokes too, not just the canvas text box.
            // Most tool shortcuts are bare single letters (b, e, g, s, t, ...), so without this a
            // layer being renamed silently lost every one of those characters to a tool switch -
            // which is exactly what "some characters don't work" looked like.
            if (Keyboard.FocusedElement is TextBox or System.Windows.Controls.Primitives.TextBoxBase) return;

            // While the full-screen view is up there's no editing UI to drive, so only the keys
            // that get you out of it should do anything - otherwise a stray tool shortcut would
            // silently change state you can't see.
            if (_isFullScreen)
            {
                if (e.Key is Key.Escape or Key.F11)
                {
                    ToggleFullScreen();
                    e.Handled = true;
                }
                return;
            }

            if (MainMenu.IsKeyboardFocusWithin) return; // let the Menu handle its own keyboard navigation
                                                          // (arrows, Enter, Escape, mnemonics) untouched

            // Shift+Arrow nudges the selection by a bigger step; handled directly rather than
            // through the registry since it's a modifier-variant of the arrow-key bindings below,
            // not a separately rebindable action.
            if (_selection.HasSelection && Keyboard.Modifiers == ModifierKeys.Shift &&
                (e.Key is Key.Left or Key.Right or Key.Up or Key.Down))
            {
                int dx = e.Key == Key.Left ? -10 : e.Key == Key.Right ? 10 : 0;
                int dy = e.Key == Key.Up ? -10 : e.Key == Key.Down ? 10 : 0;
                NudgeSelection(dx, dy);
                e.Handled = true;
                return;
            }

            var actionId = _shortcuts.Match(e.Key, Keyboard.Modifiers);
            if (actionId != null && _actionHandlers.TryGetValue(actionId, out var action))
            {
                action();
                e.Handled = true;
            }
        }

        // ===================================================================
        // Tool options panel (size / fill mode / brush shape / text formatting)
        // ===================================================================

        /// <summary>Appends a small caption to the (horizontal) options bar, vertically centered
        /// against the buttons that follow it and with a fixed gap to whatever comes next - the
        /// single place that gap is decided, so every label in the bar lines up the same way.</summary>
        private void AddOptionLabel(string text) => ToolOptionsPanel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 9,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        });

        /// <summary>Appends one control/group to the options bar, vertically centered with a fixed
        /// gap before the next label - the horizontal-bar counterpart of <see cref="AddOptionLabel"/>.
        /// Any margin already set on the element (e.g. between buttons within a WrapPanel) is left
        /// alone; only the group's own vertical alignment and trailing gap are standardized.</summary>
        private void AddOptionGroup(FrameworkElement element)
        {
            element.VerticalAlignment = VerticalAlignment.Center;
            element.Margin = new Thickness(0, 0, 16, 0);
            ToolOptionsPanel.Children.Add(element);
        }

        /// <summary>Adds a labelled dropdown for a mutually-exclusive option. This is the single
        /// place option pickers are built, so every group (size, brush shape, fill mode, blend,
        /// zoom, ...) looks and behaves the same.
        ///
        /// These were rows of small toggle buttons until now. A dropdown reads its current value at
        /// a glance without decoding which little glyph happens to look pressed in, names each
        /// choice in words rather than a symbol, and - the practical reason - doesn't have to fit
        /// every possible value on screen at once, which is what had kept things like brush size
        /// pinned to a handful of preset numbers.</summary>
        /// <summary>One row of a tool-options dropdown. A plain data object, not a control, because
        /// a ComboBox draws its selected row a second time inside the closed box - and a WPF element
        /// can only have one parent, so reusing controls as items throws the moment such a row is
        /// selected. Bound through a DataTemplate instead, which builds its own visuals per use.</summary>
        private sealed class OptionRow
        {
            public string Text { get; init; }
            public ImageSource Preview { get; init; }
            public object Value { get; init; }
        }

        private DataTemplate _optionRowTemplate;

        /// <summary>Template for an options-dropdown row: preview image (collapsed when the option
        /// has none) followed by the option's name.</summary>
        private DataTemplate OptionRowTemplate => _optionRowTemplate ??= (DataTemplate)System.Windows.Markup.XamlReader.Parse(
            @"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                            xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
                <StackPanel Orientation='Horizontal'>
                  <Image Source='{Binding Preview}' Stretch='None' Margin='0,0,6,0'
                         VerticalAlignment='Center' SnapsToDevicePixels='True'
                         RenderOptions.BitmapScalingMode='NearestNeighbor'>
                    <Image.Style>
                      <Style TargetType='Image'>
                        <Style.Triggers>
                          <DataTrigger Binding='{Binding Preview}' Value='{x:Null}'>
                            <Setter Property='Visibility' Value='Collapsed'/>
                          </DataTrigger>
                        </Style.Triggers>
                      </Style>
                    </Image.Style>
                  </Image>
                  <TextBlock Text='{Binding Text}' VerticalAlignment='Center'/>
                </StackPanel>
              </DataTemplate>");

        private ComboBox AddOptionCombo<T>(string label, IEnumerable<(string Text, T Value)> items,
                                           T current, Action<T> onSelect, double width = 104,
                                           Func<T, ImageSource> preview = null)
        {
            AddOptionLabel(label);
            var combo = new ComboBox
            {
                Width = width,
                Height = 22,
                VerticalContentAlignment = VerticalAlignment.Center,
                ItemTemplate = OptionRowTemplate
            };

            OptionRow selected = null;
            foreach (var (text, value) in items)
            {
                var row = new OptionRow
                {
                    Text = text,
                    Value = value,
                    Preview = preview == null ? null : SafePreview(preview, value)
                };
                combo.Items.Add(row);
                if (EqualityComparer<T>.Default.Equals(value, current)) selected = row;
            }
            combo.SelectedItem = selected;
            if (combo.SelectedItem == null && combo.Items.Count > 0) combo.SelectedIndex = 0;

            combo.SelectionChanged += (o, e) =>
            {
                if (combo.SelectedItem is OptionRow { Value: T value }) onSelect(value);
            };
            AddOptionGroup(combo);
            return combo;
        }

        /// <summary>A preview that fails to render must never take the whole options bar down with
        /// it - the dropdown is still perfectly usable with just its text.</summary>
        private static ImageSource SafePreview<T>(Func<T, ImageSource> preview, T value)
        {
            try { return preview(value); }
            catch { return null; }
        }

        /// <summary>The Text tool's font picker: every installed family, each entry rendered in the
        /// font it names, and type-to-search. With the full system font list this can run to
        /// hundreds of entries, so scrolling alone isn't a reasonable way to find one - the box is
        /// editable and filters the list to whatever has been typed, restoring the full list when
        /// the text is cleared.</summary>
        /// <summary>One font in the picker. A plain data object rather than a ComboBoxItem: an
        /// editable ComboBox derives its edit-box text from the selected *item*, and using
        /// containers as items makes it inherit that item's own font and size - which rendered the
        /// selected font's name as unreadable specks. With a data item plus a template, the list
        /// rows preview each face while the edit box keeps the ordinary UI font.</summary>
        private sealed class FontRow
        {
            public string Name { get; init; }
            public override string ToString() => Name;
        }

        private DataTemplate _fontRowTemplate;
        /// <summary>A font row is drawn in the font it names, which is the point - but a symbol or
        /// non-Latin family renders its own name as glyphs you can't read as text. The tooltip
        /// repeats the name in the ordinary UI font, so there's always a legible way to tell what a
        /// row actually is. The tooltip's font has to be set explicitly: tooltip content otherwise
        /// inherits from what it's attached to, which is exactly the unreadable font in question.</summary>
        private DataTemplate FontRowTemplate => _fontRowTemplate ??= (DataTemplate)System.Windows.Markup.XamlReader.Parse(
            @"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                <TextBlock Text='{Binding Name}' FontFamily='{Binding Name}' FontSize='14'>
                  <TextBlock.ToolTip>
                    <ToolTip FontFamily='Segoe UI' FontSize='11' Content='{Binding Name}'/>
                  </TextBlock.ToolTip>
                </TextBlock>
              </DataTemplate>");

        /// <summary>The Text tool's font picker: a search box feeding a dropdown of every installed
        /// family, each row drawn in the font it names.
        ///
        /// The search is its own field rather than an editable ComboBox. An editable combo derives
        /// its edit-box content from the selected item, and with rows deliberately drawn in the
        /// fonts they name that box rendered the typed text at a few pixels tall - unreadable, and
        /// not fixable by setting the font on the edit box. A separate field sidesteps that
        /// entirely, and reads more clearly as "search" besides.</summary>
        private FrameworkElement BuildFontCombo()
        {
            var all = CommonFontFamilies;

            var combo = new ComboBox
            {
                Width = 172,
                Height = 22,
                VerticalContentAlignment = VerticalAlignment.Center,
                MaxDropDownHeight = 340,
                ItemTemplate = FontRowTemplate,
                ToolTip = "The font used for new text"
            };

            var search = new TextBox
            {
                Width = 86,
                Height = 22,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = $"Search the {all.Length} installed fonts by name"
            };

            bool updating = false;

            void Populate(string filter)
            {
                updating = true;
                combo.Items.Clear();
                foreach (var f in all)
                {
                    if (!string.IsNullOrEmpty(filter) &&
                        f.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                    combo.Items.Add(new FontRow { Name = f });
                }
                // Keep the current font showing whenever it survived the filter.
                foreach (FontRow row in combo.Items)
                    if (row.Name == _textFontFamily) { combo.SelectedItem = row; break; }
                updating = false;
            }

            Populate(null);

            combo.SelectionChanged += (o, e) =>
            {
                if (updating) return;
                if (combo.SelectedItem is not FontRow row) return;
                _textFontFamily = row.Name;
                if (_activeTextBox != null) _activeTextBox.FontFamily = new FontFamily(row.Name);
            };

            search.TextChanged += (o, e) =>
            {
                Populate(search.Text);
                // Show the narrowed list straight away, but don't steal the caret out of the box
                // that's still being typed into.
                combo.IsDropDownOpen = combo.Items.Count > 0 && search.Text.Length > 0;
                search.Focus();
            };

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(search);
            panel.Children.Add(combo);
            return panel;
        }

        /// <summary>A dropdown over an unbroken run of whole numbers - every value in the range, not
        /// a handful of presets. Used for the size pickers, where being able to pick (say) 7 rather
        /// than only 5 or 8 is the whole point.</summary>
        private ComboBox AddNumberCombo(string label, int min, int max, int current, Action<int> onSelect,
                                        double width = 62)
        {
            var values = new List<(string, int)>(max - min + 1);
            for (int i = min; i <= max; i++) values.Add((i.ToString(), i));
            return AddOptionCombo(label, values, Math.Max(min, Math.Min(max, current)), onSelect, width);
        }

        private void BuildToolOptions(string key)
        {
            ToolOptionsPanel.Children.Clear();
            // Every member of the shape family (star, triangle, heart, ...) shares one set of
            // options, so they're tested for as a group rather than listed one by one.
            bool isPolyShape = _tools.TryGetValue(key, out var activeTool) && activeTool is PolyShapeTool;
            bool needsSize = isPolyShape || key is "Pencil" or "Brush" or "Eraser" or "Airbrush" or "Line"
                or "Rectangle" or "Ellipse" or "RoundedRectangle" or "Polygon" or "Curve" or "Arrow";
            bool needsFill = isPolyShape || key is "Rectangle" or "Ellipse" or "RoundedRectangle" or "Polygon";
            bool isBrush = key == "Brush";
            bool isText = key == "Text";

            if (needsSize)
            {
                AddNumberCombo("Size:", 1, 100, (int)_ctx.PenSize, v =>
                {
                    _ctx.PenSize = v;
                    if (_currentToolKey == "Eraser") UpdateEraserOutline(_lastCanvasDocPoint);
                });
            }

            if (key == "Arrow")
            {
                AddOptionCombo("Head:", ArrowStyleChoices.Select(a => (a.Tip, a.Style)),
                    _ctx.ArrowStyle, v => _ctx.ArrowStyle = v, 178,
                    OptionPreviews.ArrowStylePreview);
            }

            // Line style applies to anything that strokes an outline. Filled-only shapes still show
            // it because switching to an outline mode should not also mean re-picking the style.
            if (isPolyShape || key is "Line" or "Curve" or "Rectangle" or "Ellipse"
                or "RoundedRectangle" or "Polygon" or "Arrow")
            {
                AddOptionCombo("Line:", new[]
                {
                    ("Solid", LineStyle.Solid),
                    ("Dashed", LineStyle.Dashed),
                    ("Dotted", LineStyle.Dotted),
                    ("Dash-dot", LineStyle.DashDot),
                    ("Long dash", LineStyle.LongDash),
                }, _ctx.LineStyle, v =>
                {
                    _ctx.LineStyle = v;
                    // The dash length/spacing controls only exist for a broken line, so the bar has
                    // to be rebuilt to show or hide them when the style changes.
                    Dispatcher.BeginInvoke(new Action(() => BuildToolOptions(_currentToolKey)),
                        System.Windows.Threading.DispatcherPriority.Input);
                }, 118);

                // Only meaningful once the line is actually broken up.
                if (_ctx.LineStyle != LineStyle.Solid)
                {
                    var scales = new List<(string, int)>();
                    for (int s = 25; s <= 300; s += 25) scales.Add(($"{s}%", s));

                    AddOptionCombo("Dash:", scales, _ctx.DashLengthPercent,
                        v => _ctx.DashLengthPercent = v, 74);
                    AddOptionCombo("Spacing:", scales, _ctx.DashGapPercent,
                        v => _ctx.DashGapPercent = v, 74);
                }
            }

            if (key == "Pick")
            {
                AddOptionLabel("From:");
                var screenPick = new Button
                {
                    Content = "Pick from screen…",
                    Padding = new Thickness(8, 2, 8, 2),
                    ToolTip = "Sample a colour from anywhere on screen - move to the colour you want, " +
                              "click to take it, or press Esc to cancel"
                };
                screenPick.Click += (o, e) => BeginScreenColorPick();
                AddOptionGroup(screenPick);
            }

            if (key == "Airbrush")
            {
                var flows = new List<(string, int)>();
                for (int f = 10; f <= 200; f += 10) flows.Add(($"{f}%", f));
                AddOptionCombo("Flow:", flows, _ctx.AirbrushFlow, v => _ctx.AirbrushFlow = v, 76);
            }

            if (key == "Fill")
            {
                AddNumberCombo("Tolerance:", 0, 255, _ctx.FillTolerance, v => _ctx.FillTolerance = v);
                AddOptionCombo("Area:", new[] { ("Connected area", true), ("Whole layer", false) },
                    _ctx.FillContiguous, v => _ctx.FillContiguous = v, 148,
                    OptionPreviews.AreaPreview);
            }

            if (key == "RoundedRectangle")
            {
                // 0 keeps the original "work it out from the shape's size" behaviour.
                var radii = new List<(string, int)> { ("Auto", 0) };
                for (int i = 1; i <= 60; i++) radii.Add((i.ToString(), i));
                AddOptionCombo("Corners:", radii, _ctx.CornerRadius, v => _ctx.CornerRadius = v, 72);
            }

            // Anti-aliasing applies to any tool that draws an edge. Offered per tool (and
            // remembered per tool), since the useful default genuinely differs between, say, the
            // Pencil and the Curve.
            if (isPolyShape || key is "Pencil" or "Brush" or "Airbrush" or "Eraser" or "Line" or "Curve"
                or "Rectangle" or "Ellipse" or "RoundedRectangle" or "Polygon" or "Arrow"
                or "Gradient" or "Text")
            {
                AddOptionCombo("Edges:", new[] { ("Hard", false), ("Smooth", true) },
                    _ctx.AntiAlias, v => _ctx.AntiAlias = v, 112,
                    OptionPreviews.EdgesPreview);
            }

            if (key == "MagicWand")
            {
                AddNumberCombo("Tolerance:", 0, 255, _ctx.WandTolerance, v => _ctx.WandTolerance = v);

                AddOptionCombo("Search:", new[] { ("Connected area", true), ("Whole layer", false) },
                    _ctx.WandContiguous, v => _ctx.WandContiguous = v, 148,
                    OptionPreviews.AreaPreview);
            }

            if (key == "Gradient")
            {
                AddOptionCombo("Blend:", GradientTypeChoices.Select(g => (g.Tip, g.Type)),
                    _ctx.GradientType, v => _ctx.GradientType = v, 232,
                    OptionPreviews.GradientPreview);

                AddOptionCombo("Area:", new[] { ("Dragged box", false), ("Whole canvas", true) },
                    _ctx.GradientFillsCanvas, v => _ctx.GradientFillsCanvas = v, 128);

                var reverse = new CheckBox
                {
                    Content = "Reverse",
                    IsChecked = _ctx.GradientReverse,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0),
                    ToolTip = "Swap which end of the blend each colour sits at"
                };
                reverse.Checked += (o, e) => _ctx.GradientReverse = true;
                reverse.Unchecked += (o, e) => _ctx.GradientReverse = false;
                AddOptionGroup(reverse);

                // A plain on/off, so a checkbox rather than a two-entry dropdown.
                var dither = new CheckBox
                {
                    Content = "Dither",
                    IsChecked = _ctx.GradientDither,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "Smooths out banding in gradual blends"
                };
                dither.Checked += (o, e) => _ctx.GradientDither = true;
                dither.Unchecked += (o, e) => _ctx.GradientDither = false;
                AddOptionGroup(dither);
            }

            if (key == "Star")
            {
                var pointCounts = new List<(string, int)>();
                for (int n = 3; n <= 24; n++) pointCounts.Add((n.ToString(), n));
                AddOptionCombo("Points:", pointCounts, _ctx.StarPoints, v => _ctx.StarPoints = v, 74,
                    OptionPreviews.StarPointsPreview);

                // How far the star's points cut in. "Auto" keeps the original behaviour of
                // tightening as points are added, which is what stops a many-pointed star from
                // reading as a gear.
                var depths = new List<(string, int)> { ("Auto", 0) };
                for (int d = 10; d <= 90; d += 5) depths.Add(($"{d}%", d));
                AddOptionCombo("Depth:", depths, _ctx.StarInnerPercent, v => _ctx.StarInnerPercent = v, 74);
            }

            if (isPolyShape)
            {
                var angles = new List<(string, int)>();
                for (int a = 0; a < 360; a += 15) angles.Add(($"{a}°", a));
                AddOptionCombo("Angle:", angles, _ctx.ShapeRotation, v => _ctx.ShapeRotation = v, 68);
            }

            if (isBrush)
            {
                AddOptionCombo("Shape:", BrushShapeChoices.Select(bs => (bs.Tip, bs.Shape)),
                    _ctx.BrushShape, v => _ctx.BrushShape = v, 196,
                    OptionPreviews.BrushShapePreview);
            }

            if (needsFill)
            {
                AddOptionCombo("Fill:", new[]
                {
                    ("Outline only", ShapeFillMode.OutlineOnly),
                    ("Filled", ShapeFillMode.FillOnly),
                    ("Outline + fill", ShapeFillMode.OutlineAndFill),
                }, _ctx.ShapeFillMode, v => _ctx.ShapeFillMode = v, 148,
                   OptionPreviews.FillModePreview);
            }

            if (isText)
            {
                AddOptionLabel("Font:");
                AddOptionGroup(BuildFontCombo());

                AddNumberCombo("Size:", 6, 200, (int)_textFontSize, sz =>
                {
                    _textFontSize = sz;
                    if (_activeTextBox != null)
                    {
                        _activeTextBox.FontSize = Math.Max(8, _textFontSize * _zoom);
                        AutoGrowTextBox(); // a larger face may no longer fit the box it's being typed into
                    }
                });

                AddOptionLabel("Style:");
                var boldBtn = new ToggleButton { Style = (Style)FindResource("OptionToggleStyle"), Content = "B", FontWeight = FontWeights.Bold, IsChecked = _textBold };
                var italicBtn = new ToggleButton { Style = (Style)FindResource("OptionToggleStyle"), Content = "I", FontStyle = FontStyles.Italic, IsChecked = _textItalic };
                var underlineBtn = new ToggleButton { Style = (Style)FindResource("OptionToggleStyle"), Content = "U", IsChecked = _textUnderline };
                boldBtn.Checked += (o, e) => { _textBold = true; if (_activeTextBox != null) _activeTextBox.FontWeight = FontWeights.Bold; };
                boldBtn.Unchecked += (o, e) => { _textBold = false; if (_activeTextBox != null) _activeTextBox.FontWeight = FontWeights.Normal; };
                italicBtn.Checked += (o, e) => { _textItalic = true; if (_activeTextBox != null) _activeTextBox.FontStyle = FontStyles.Italic; };
                italicBtn.Unchecked += (o, e) => { _textItalic = false; if (_activeTextBox != null) _activeTextBox.FontStyle = FontStyles.Normal; };
                underlineBtn.Checked += (o, e) => { _textUnderline = true; if (_activeTextBox != null) _activeTextBox.TextDecorations = TextDecorations.Underline; };
                underlineBtn.Unchecked += (o, e) => { _textUnderline = false; if (_activeTextBox != null) _activeTextBox.TextDecorations = null; };
                var wrap = new WrapPanel();
                wrap.Children.Add(boldBtn); wrap.Children.Add(italicBtn); wrap.Children.Add(underlineBtn);
                AddOptionGroup(wrap);
            }

            if (key == "Magnifier")
            {
                AddOptionCombo("Zoom:", MagnifierTool.Levels.Select(level => ($"{(int)level}x", level)),
                    _zoom, level =>
                    {
                        _suppressZoomOptionRebuild = true;
                        try { SetZoom(level); } finally { _suppressZoomOptionRebuild = false; }
                    }, 70);
            }

            if (key is "Select" or "FreeFormSelect" or "MagicWand")
            {
                AddOptionCombo("Background:", new[] { ("Opaque", true), ("Transparent", false) },
                    _selection.DrawOpaque, v =>
                    {
                        _selection.DrawOpaque = v;
                        MenuDrawOpaque.IsChecked = v; // Image > Draw Opaque is the same setting
                    }, 104);
            }
        }

        // ===================================================================
        // Text tool overlay
        // ===================================================================

        /// <summary>Starts editing a brand-new text layer, from the Text tool - either a plain
        /// click (autoWidth: point text, grows in both directions, never wraps) or a click-drag
        /// (autoWidth false: paragraph text, wraps within docRect's width). Nothing exists yet -
        /// CommitActiveTextBox will create a layer, via AddTextLayer, if anything gets typed.</summary>
        private void BeginTextEditing(Rect docRect, bool autoWidth)
        {
            CommitActiveTextBox();
            _editingLayerIndex = -1;
            StartTextBoxUI(docRect, "", _textFontFamily, _textFontSize, _textBold, _textItalic, _textUnderline, _colors.Foreground, autoWidth);
        }

        /// <summary>Re-opens the active layer's existing text for editing - the Photoshop-style
        /// "click a type layer" gesture (here, a click on the Text tool that lands inside a text
        /// layer that's already active - see TextTool.OnMouseDown). CommitActiveTextBox will update
        /// that same layer's TextLayerData in place rather than creating a new layer.</summary>
        private void BeginTextEditOnActiveLayer()
        {
            int index = _document.ActiveLayerIndex;
            var layer = _document.Layers[index];
            if (layer.Text == null) return;
            CommitActiveTextBox(); // in case some *other* text box was somehow still open
            _editingLayerIndex = index;
            var t = layer.Text;
            // TextLayerData.X/Y is where pixels actually get blitted, which is 1 *screen* pixel
            // inside the live box's own border (see the borderInsetDoc comment in
            // CommitActiveTextBox) - reverse that here so re-opening the box lines its text back
            // up exactly where it already was, rather than nudging it down-right by a pixel.
            double inset = 1.0 / _zoom;
            var outerRect = new Rect(t.X - inset, t.Y - inset, t.Width, t.Height);

            // Hide this layer's already-rendered pixels for the duration of the edit - the live
            // TextBox about to open shows the same text, so leaving both on screen draws it twice.
            // Display-only (see PaintCanvas.SuppressedLayerIndex); the layer itself is untouched.
            _canvas.SuppressedLayerIndex = index;
            _canvas.RefreshLayerVisibility();

            StartTextBoxUI(outerRect, t.Content, t.FontFamily, t.FontSize, t.Bold, t.Italic, t.Underline, t.Color, t.AutoWidth);

            // TextTool may have just switched the active layer to this one (clicking on any
            // visible text layer re-opens IT, not only whichever layer happened to be active
            // already) - refresh the panel so its highlighted row actually matches. Best-effort,
            // deliberately after the box above is already live and focused: editing must work
            // regardless of whether this succeeds.
            try { LayersPanelCtl.Refresh(); } catch { /* cosmetic only */ }
        }

        /// <summary>Shared setup for the live WPF TextBox overlay + its move/resize handles, used
        /// by both BeginTextEditing (empty, brand new) and BeginTextEditOnActiveLayer (pre-filled
        /// from an existing text layer's data). Leaves the caret at the end of whatever content it
        /// was given - position 0 for a fresh, empty box, or ready to keep typing at the end of
        /// existing text for a re-edit. Simpler and more robust than trying to land the caret at
        /// the exact point that was clicked (an earlier version of this tried exactly that, via
        /// GetCharacterIndexFromPoint deferred a dispatcher tick for layout to catch up - correct
        /// in principle, but fragile in practice and not what was actually being asked for here).</summary>
        private void StartTextBoxUI(Rect outerDocRect, string content, string fontFamily, double fontSize,
            bool bold, bool italic, bool underline, Color color, bool autoWidth)
        {
            _activeTextDocRect = outerDocRect;
            _textFontFamily = fontFamily;
            _textFontSize = fontSize;
            _textBold = bold;
            _textItalic = italic;
            _textUnderline = underline;
            _textEditColor = color;
            _textAutoWidth = autoWidth;

            double boxW = outerDocRect.Width * _zoom;
            double boxH = outerDocRect.Height * _zoom;

            _activeTextBox = new TextBox
            {
                AcceptsReturn = true,
                // Point text never wraps - it grows instead (see AutoGrowTextBox) - while
                // paragraph text wraps within its fixed width, same as before.
                TextWrapping = autoWidth ? TextWrapping.NoWrap : TextWrapping.Wrap,
                BorderBrush = Brushes.DimGray,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0), // eliminate WPF's theme-dependent default padding -
                                             // otherwise the live-preview text sits inset from the
                                             // box's edge by an amount the final raster commit
                                             // (which draws starting exactly at the box's edge)
                                             // doesn't know about, causing a visible shift between
                                             // what you typed and what gets committed
                Background = Brushes.Transparent,
                CaretBrush = new SolidColorBrush(color),
                FontFamily = new FontFamily(fontFamily),
                FontSize = Math.Max(8, fontSize * _zoom),
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
                TextDecorations = underline ? TextDecorations.Underline : null,
                Foreground = new SolidColorBrush(color),
                Width = boxW,
                Height = boxH,
                Text = content ?? ""
            };
            // Refreshes the Font/Size/Style controls in the options bar to match whatever's being
            // edited - most visible on a re-edit, where they'd otherwise keep showing whatever the
            // *previous* piece of text (or the tool's own defaults) last left them at.
            if (_currentToolKey == "Text") BuildToolOptions("Text");

            Canvas.SetLeft(_activeTextBox, outerDocRect.X * _zoom);
            Canvas.SetTop(_activeTextBox, outerDocRect.Y * _zoom);
            TextOverlayLayer.IsHitTestVisible = true;
            TextOverlayLayer.Children.Add(_activeTextBox);

            // Commit-on-focus-loss, but only once the box has genuinely held focus at some point.
            // Without this guard, a box that never managed to take focus in the first place - or
            // any stray focus event during setup - tears the editor straight back down before it
            // can be typed into, which looks exactly like "clicking the text does nothing."
            bool everFocused = false;
            _activeTextBox.GotKeyboardFocus += (s, e) => everFocused = true;
            _activeTextBox.LostKeyboardFocus += (s, e) =>
            {
                // Reaching for the options bar isn't finishing the text. Committing here is what
                // made font, size and B/I/U impossible to change on text you were still typing:
                // the click that opened the dropdown took focus, which committed the box, so the
                // setting was applied to nothing. The bar's own controls all mutate _activeTextBox
                // directly, so leaving it open is exactly what makes them work.
                if (_suppressTextCommit) return;
                if (everFocused) CommitActiveTextBox();
            };
            _activeTextBox.TextChanged += (s, e) => AutoGrowTextBox();

            // Move handle at the top-left corner (circular, distinct from the square resize
            // handle) lets the box be dragged to reposition it without disturbing text editing -
            // dragging inside the box itself has to stay reserved for placing the caret /
            // selecting text, so repositioning needs its own dedicated handle outside the box.
            _textMoveHandle = MakeMoveHandleVisual();
            PositionTextMoveHandle();
            WireManualDrag(_textMoveHandle, Cursors.SizeAll, null, (dx, dy) =>
            {
                double newLeft = Canvas.GetLeft(_activeTextBox) + dx;
                double newTop = Canvas.GetTop(_activeTextBox) + dy;
                Canvas.SetLeft(_activeTextBox, newLeft);
                Canvas.SetTop(_activeTextBox, newTop);
                _activeTextDocRect = new Rect(newLeft / _zoom, newTop / _zoom, _activeTextDocRect.Width, _activeTextDocRect.Height);
                PositionTextResizeHandle();
                PositionTextMoveHandle();
            });
            TextOverlayLayer.Children.Add(_textMoveHandle);

            // Drag handle on the bottom-right corner so the box can be resized after the fact,
            // not just fixed at whatever size the initial drag happened to create.
            _textResizeHandle = MakeResizeHandleVisual();
            PositionTextResizeHandle();
            WireManualDrag(_textResizeHandle, Cursors.SizeNWSE, null, (dx, dy) =>
            {
                double newW = Math.Max(20, _activeTextBox.Width + dx);
                double newH = Math.Max(16, _activeTextBox.Height + dy);
                _activeTextBox.Width = newW;
                _activeTextBox.Height = newH;
                _activeTextDocRect = new Rect(_activeTextDocRect.X, _activeTextDocRect.Y, newW / _zoom, newH / _zoom);
                PositionTextResizeHandle();
            });
            TextOverlayLayer.Children.Add(_textResizeHandle);

            Keyboard.Focus(_activeTextBox);
            _activeTextBox.CaretIndex = _activeTextBox.Text.Length;

            // Focus again once layout has actually run. WPF refuses keyboard focus to an element
            // that isn't visible yet, and an element added to the tree inside an event handler
            // hasn't been measured/arranged at that point - so the call above can silently do
            // nothing. Retrying after layout costs nothing when the first attempt already worked
            // (re-focusing an already-focused element is a no-op) and is what guarantees the box
            // is genuinely ready to type into rather than merely visible.
            var box = _activeTextBox;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_activeTextBox != box) return; // already committed/replaced before this ran
                if (!box.IsKeyboardFocusWithin)
                {
                    Keyboard.Focus(box);
                    box.CaretIndex = box.Text.Length;
                }
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private Border MakeMoveHandleVisual() => new Border
        {
            Width = 12,
            Height = 12,
            Background = Brushes.White,
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };

        private void PositionTextMoveHandle()
        {
            if (_textMoveHandle == null || _activeTextBox == null) return;
            Canvas.SetLeft(_textMoveHandle, Canvas.GetLeft(_activeTextBox) - 8);
            Canvas.SetTop(_textMoveHandle, Canvas.GetTop(_activeTextBox) - 8);
        }

        private void PositionTextResizeHandle()
        {
            if (_textResizeHandle == null || _activeTextBox == null) return;
            Canvas.SetLeft(_textResizeHandle, Canvas.GetLeft(_activeTextBox) + _activeTextBox.Width - 3);
            Canvas.SetTop(_textResizeHandle, Canvas.GetTop(_activeTextBox) + _activeTextBox.Height - 3);
        }

        /// <summary>Grows the text box to fit its content as the user types, so text never gets
        /// clipped just because the original drag (or the last manual resize) was too short. Always
        /// grows height; also grows width when editing point text (_textAutoWidth), which never
        /// wraps and so has no fixed width to clip against in the first place. Only ever grows -
        /// never auto-shrinks, so a deliberately-enlarged box via the resize handle isn't fought
        /// back down as soon as you delete a line.</summary>
        private void AutoGrowTextBox()
        {
            if (_activeTextBox == null) return;
            var tb = _activeTextBox;

            var typeface = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, FontStretches.Normal);
            string textForMeasurement = string.IsNullOrEmpty(tb.Text) ? " " : tb.Text;
            var ft = new FormattedText(
                textForMeasurement, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, tb.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);

            bool resized = false;
            if (_textAutoWidth)
            {
                // Point text: leaving MaxTextWidth unconstrained is what makes ft.Width reflect
                // each line's own natural width instead of being wrapped/clipped against a box
                // width that, for point text, doesn't really exist.
                double neededWidth = ft.Width + 10;
                if (neededWidth > tb.Width) { tb.Width = neededWidth; resized = true; }
            }
            else
            {
                ft.MaxTextWidth = Math.Max(1, tb.Width - 8); // rough allowance for the box's own border/padding
            }

            double neededHeight = ft.Height + 10; // small buffer so the caret/last line is never flush against the edge
            if (neededHeight > tb.Height) { tb.Height = neededHeight; resized = true; }

            if (resized)
            {
                _activeTextDocRect = new Rect(_activeTextDocRect.X, _activeTextDocRect.Y, tb.Width / _zoom, tb.Height / _zoom);
                PositionTextResizeHandle();
                PositionTextMoveHandle();
            }
        }

        /// <summary>Closes the live TextBox overlay and turns what it holds into (or updates) a
        /// text layer - never rasterizing text directly into whatever the active layer happened to
        /// be, the way classic Paint's text tool did. The actual pixel rendering is delegated to
        /// Services/TextLayerRenderer, which is also what a later move/resize/edit or a document
        /// resize re-invokes - this is only responsible for turning the live TextBox's current
        /// state into a TextLayerData and deciding whether that's a new layer or an update to the
        /// one being re-edited.</summary>
        private void CommitActiveTextBox()
        {
            if (_activeTextBox == null) return;
            var text = _activeTextBox.Text;
            var box = _activeTextBox;
            TextOverlayLayer.Children.Remove(box);
            if (_textResizeHandle != null) TextOverlayLayer.Children.Remove(_textResizeHandle);
            if (_textMoveHandle != null) TextOverlayLayer.Children.Remove(_textMoveHandle);
            _textResizeHandle = null;
            _textMoveHandle = null;
            TextOverlayLayer.IsHitTestVisible = false;
            _activeTextBox = null;

            // Stop hiding the layer that was being edited, on every exit path from here - whether
            // the text is about to be updated, deleted, or left unchanged. Cleared before any of
            // the branching below so a stale index can't outlive the layer it referred to and end
            // up suppressing an unrelated layer once indices shift (e.g. the delete path).
            _canvas.SuppressedLayerIndex = -1;
            _canvas.RefreshLayerVisibility();

            int editingIndex = _editingLayerIndex;
            _editingLayerIndex = -1;

            if (string.IsNullOrEmpty(text))
            {
                // Typing everything away on an existing text layer deletes it outright - an empty
                // text layer serves no purpose and would just be an invisible, confusing entry
                // sitting in the Layers panel. A brand-new, never-created box just vanishes, as
                // before.
                if (editingIndex >= 0 && editingIndex < _document.Layers.Count)
                {
                    _history.PushUndoState(_document, "Delete Text Layer");
                    _document.DeleteLayer(editingIndex);
                }
                return;
            }

            // The live box's text starts 1 screen px inside its border (BorderThickness=1) -
            // convert that fixed screen-space inset to document space so the layer's stored
            // position matches the same effective spot the live preview showed, not the box's
            // outer edge.
            double borderInsetDoc = 1.0 / _zoom;
            var data = new TextLayerData
            {
                Content = text,
                FontFamily = _textFontFamily,
                FontSize = _textFontSize,
                Bold = _textBold,
                Italic = _textItalic,
                Underline = _textUnderline,
                Color = _textEditColor,
                AntiAlias = _ctx.AntiAlias,
                AutoWidth = _textAutoWidth,
                X = _activeTextDocRect.X + borderInsetDoc,
                Y = _activeTextDocRect.Y + borderInsetDoc,
                Width = Math.Max(1, _activeTextDocRect.Width),
                Height = Math.Max(1, _activeTextDocRect.Height)
            };

            if (editingIndex >= 0 && editingIndex < _document.Layers.Count && _document.Layers[editingIndex].Text != null)
            {
                _history.PushUndoState(_document, "Edit Text");
                _document.Layers[editingIndex].Text = data;
                _document.RefreshTextLayer(_document.Layers[editingIndex]);
                _document.ActiveLayerIndex = editingIndex;
            }
            else
            {
                _history.PushUndoState(_document, "Add Text Layer");
                // Named after its own content (trimmed, single-line) rather than a generic
                // "Layer N" - closer to how Photoshop's type layers name themselves, and far more
                // useful for telling several text layers apart at a glance in the Layers panel.
                string name = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (name.Length > 24) name = name.Substring(0, 24) + "...";
                if (name.Length == 0) name = "Text";
                _document.AddTextLayer(data, name);
            }
        }

        // ===================================================================
        // File menu
        // ===================================================================

        private void New_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new NewDocumentDialog(_document?.Width ?? 480, _document?.Height ?? 360) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            NewDocument(dlg.ResultWidth, dlg.ResultHeight);
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDiscardChanges()) return;
            var dlg = new OpenFileDialog
            {
                Filter = "Splash Project (*.splash)|*.splash|" +
                         "All Picture Files|*.bmp;*.dib;*.png;*.jpg;*.jpeg;*.gif;*.tif;*.tiff;*.wdp;*.jxr;*.ico;*.tga|" +
                         "Bitmap Files|*.bmp;*.dib|PNG Files|*.png|JPEG Files|*.jpg;*.jpeg|GIF Files|*.gif|" +
                         "TIFF Files|*.tif;*.tiff|JPEG XR Files|*.wdp;*.jxr|Icon Files|*.ico|Targa Files|*.tga"
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                // Only the project format keeps layers/text objects intact - everything else here
                // is a flat picture format, opened as a single Background layer just like before.
                _document = IsProjectFile(dlg.FileName)
                    ? SplashProjectFile.Load(dlg.FileName)
                    : PaintDocument.FromBitmap(LoadBitmap(dlg.FileName), dlg.FileName);
                WireDocumentLayerEvents();
                _canvas.SetDocument(_document);
                _canvas.Zoom = _zoom;
                _pendingShape = null; // document is being replaced - don't carry a shape across into it
                _selection.Deselect(_document.Surface);
                _canvas.ShowSelection(null);
                _history.Clear("Opened File");
                UpdateTitle();
                UpdateStatusSize();
                RebuildContext();
                UpdateGridOverlay();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Splash could not open this file.\n({ex.Message})", "Splash",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static WriteableBitmap LoadBitmap(string path)
        {
            // WPF has no TGA decoder, so that one is read by hand (see LoadTga). Everything else
            // - including .wdp/.jxr and .ico - has a built-in decoder.
            if (Path.GetExtension(path).ToLowerInvariant() == ".tga") return LoadTga(path);

            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            // For a multi-size .ico, Frames[0] isn't reliably the largest - pick the biggest frame
            // so opening an icon gives you the best available detail rather than a 16x16 thumbnail.
            var frame = decoder.Frames[0];
            foreach (var f in decoder.Frames)
                if (f.PixelWidth * f.PixelHeight > frame.PixelWidth * frame.PixelHeight) frame = f;
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
            return new WriteableBitmap(converted);
        }

        /// <summary>Reads uncompressed (type 2) and RLE-compressed (type 10) true-colour Targa
        /// files - the two variants that actually turn up in practice. Paletted TGAs are rare
        /// enough that they're rejected with a clear message rather than silently mis-decoded.</summary>
        private static WriteableBitmap LoadTga(string path)
        {
            var data = File.ReadAllBytes(path);
            if (data.Length < 18) throw new InvalidDataException("Not a valid Targa file.");

            int idLength = data[0];
            int imageType = data[2];
            int width = data[12] | (data[13] << 8);
            int height = data[14] | (data[15] << 8);
            int bpp = data[16];
            bool topDown = (data[17] & 0x20) != 0;

            if (imageType != 2 && imageType != 10)
                throw new InvalidDataException("Only true-colour Targa files are supported.");
            if (bpp != 24 && bpp != 32)
                throw new InvalidDataException("Only 24-bit and 32-bit Targa files are supported.");

            int bytesPerPixel = bpp / 8;
            int pos = 18 + idLength;
            var pixels = new byte[width * height * 4];
            int outIndex = 0;

            void Emit(byte b, byte g, byte r, byte a)
            {
                pixels[outIndex++] = b; pixels[outIndex++] = g;
                pixels[outIndex++] = r; pixels[outIndex++] = a;
            }

            if (imageType == 2)
            {
                for (int i = 0; i < width * height && pos + bytesPerPixel <= data.Length; i++, pos += bytesPerPixel)
                    Emit(data[pos], data[pos + 1], data[pos + 2], bytesPerPixel == 4 ? data[pos + 3] : (byte)255);
            }
            else // type 10: run-length encoded
            {
                while (outIndex < pixels.Length && pos < data.Length)
                {
                    int packet = data[pos++];
                    int count = (packet & 0x7F) + 1;
                    if ((packet & 0x80) != 0) // run packet: one pixel repeated
                    {
                        if (pos + bytesPerPixel > data.Length) break;
                        byte b = data[pos], g = data[pos + 1], r = data[pos + 2];
                        byte a = bytesPerPixel == 4 ? data[pos + 3] : (byte)255;
                        pos += bytesPerPixel;
                        for (int i = 0; i < count && outIndex < pixels.Length; i++) Emit(b, g, r, a);
                    }
                    else // raw packet: count distinct pixels
                    {
                        for (int i = 0; i < count && outIndex < pixels.Length && pos + bytesPerPixel <= data.Length; i++, pos += bytesPerPixel)
                            Emit(data[pos], data[pos + 1], data[pos + 2], bytesPerPixel == 4 ? data[pos + 3] : (byte)255);
                    }
                }
            }

            // TGA's default origin is bottom-left, so flip unless the header says top-down.
            if (!topDown)
            {
                int stride = width * 4;
                var flipped = new byte[pixels.Length];
                for (int y = 0; y < height; y++)
                    Array.Copy(pixels, y * stride, flipped, (height - 1 - y) * stride, stride);
                pixels = flipped;
            }

            var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            wb.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
            return new WriteableBitmap(new FormatConvertedBitmap(wb, PixelFormats.Pbgra32, null, 0));
        }

        private void Save_Click(object sender, RoutedEventArgs e) => DoSave();
        private void SaveAs_Click(object sender, RoutedEventArgs e) => DoSaveAs();

        /// <summary>The one format that isn't a flat exported picture - see SplashProjectFile.
        /// Kept as an extension check (not a stored enum/flag on PaintDocument) since it's a
        /// property of the *file*, not something that needs to survive independently of FilePath.</summary>
        private static bool IsProjectFile(string path) =>
            !string.IsNullOrEmpty(path) && Path.GetExtension(path).Equals(".splash", StringComparison.OrdinalIgnoreCase);

        private bool DoSave()
        {
            FinalizeFloatingSelection();
            if (_document.FilePath == null) return DoSaveAs();
            try
            {
                if (IsProjectFile(_document.FilePath))
                    SplashProjectFile.Save(_document, _document.FilePath);
                else
                    SaveBitmap(_document.FilePath, _document.GetFlattenedBitmap(), _document.LastSaveFilterIndex);
                _document.IsDirty = false;
                UpdateTitle();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not save the file.\n({ex.Message})", "Splash", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        // Matches classic Paint's own Save As format list: four BMP color-depth variants first,
        // then the other formats it supported. FilterIndex (1-based, matches this list's order)
        // is what actually decides the pixel depth for BMP - all four share the .bmp extension.
        // Splash Project is appended at the end rather than given pride of place at the front
        // specifically so none of those existing, order-dependent index numbers had to shift.
        private const string SaveFilter =
            "Monochrome Bitmap (*.bmp;*.dib)|*.bmp;*.dib|" +
            "16 Color Bitmap (*.bmp;*.dib)|*.bmp;*.dib|" +
            "256 Color Bitmap (*.bmp;*.dib)|*.bmp;*.dib|" +
            "24-bit Bitmap (*.bmp;*.dib)|*.bmp;*.dib|" +
            "JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|" +
            "GIF (*.gif)|*.gif|" +
            "TIFF (*.tif;*.tiff)|*.tif;*.tiff|" +
            "PNG (*.png)|*.png|" +
            "JPEG XR / HD Photo (*.wdp;*.jxr)|*.wdp;*.jxr|" +
            "Windows Icon (*.ico)|*.ico|" +
            "Targa (*.tga)|*.tga|" +
            "Splash Project - keeps layers and text editable (*.splash)|*.splash";

        private bool DoSaveAs()
        {
            FinalizeFloatingSelection();
            var dlg = new SaveFileDialog
            {
                Filter = SaveFilter,
                FilterIndex = _document.LastSaveFilterIndex,
                FileName = _document.FilePath == null ? "untitled.bmp" : Path.GetFileName(_document.FilePath)
            };
            if (dlg.ShowDialog(this) != true) return false;

            try
            {
                if (IsProjectFile(dlg.FileName))
                {
                    SplashProjectFile.Save(_document, dlg.FileName);
                }
                else
                {
                    // .ico gets an extra step: which sizes to embed, with a preview of how the
                    // artwork holds up at each. Cancelling that dialog cancels the whole save
                    // rather than quietly writing a default set the user never agreed to.
                    var flattened = _document.GetFlattenedBitmap();
                    IReadOnlyList<int> icoSizes = null;
                    if (Path.GetExtension(dlg.FileName).ToLowerInvariant() == ".ico")
                    {
                        var icoDlg = new IcoExportDialog(flattened) { Owner = this };
                        if (icoDlg.ShowDialog() != true) return false;
                        icoSizes = icoDlg.SelectedSizes;
                    }
                    SaveBitmap(dlg.FileName, flattened, dlg.FilterIndex, icoSizes);
                    _document.LastSaveFilterIndex = dlg.FilterIndex;
                }
                _document.FilePath = dlg.FileName;
                _document.IsDirty = false;
                UpdateTitle();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not save the file.\n({ex.Message})", "Splash", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>Sizes used when an .ico is written without going through the export dialog
        /// (e.g. a plain Ctrl+S over an existing .ico).</summary>
        private static readonly int[] DefaultIcoSizes = { 16, 32, 48, 256 };

        private static void SaveBitmap(string path, WriteableBitmap bmp, int filterIndex, IReadOnlyList<int> icoSizes = null)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();

            // Non-BMP formats: filterIndex is unambiguous only for BMP (which shares one
            // extension across four variants), so for everything else just go by extension.
            // Formats with no WPF encoder are written by hand below.
            if (ext == ".ico") { SaveIco(path, bmp, icoSizes ?? DefaultIcoSizes); return; }
            if (ext == ".tga") { SaveTga(path, bmp); return; }

            if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".tif" or ".tiff" or ".wdp" or ".jxr")
            {
                BitmapEncoder enc = ext switch
                {
                    ".png" => new PngBitmapEncoder(),
                    ".jpg" or ".jpeg" => new JpegBitmapEncoder(),
                    ".gif" => new GifBitmapEncoder(),
                    ".wdp" or ".jxr" => new WmpBitmapEncoder(), // JPEG XR / HD Photo
                    _ => new TiffBitmapEncoder(),
                };
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using var fs2 = new FileStream(path, FileMode.Create);
                enc.Save(fs2);
                return;
            }

            // BMP (or .dib): pick the pixel depth from the chosen Save As filter.
            BitmapSource forSave = filterIndex switch
            {
                1 => new FormatConvertedBitmap(bmp, PixelFormats.BlackWhite, null, 0),
                2 => new FormatConvertedBitmap(bmp, PixelFormats.Indexed4, new BitmapPalette(bmp, 16), 0),
                3 => new FormatConvertedBitmap(bmp, PixelFormats.Indexed8, new BitmapPalette(bmp, 256), 0),
                _ => new FormatConvertedBitmap(bmp, PixelFormats.Bgr24, null, 0), // 24-bit Bitmap (default)
            };
            var encoder = new BmpBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(forSave));
            using var fs = new FileStream(path, FileMode.Create);
            encoder.Save(fs);
        }

        /// <summary>Writes a Windows .ico. WPF has no ICO *encoder* (only a decoder), so this
        /// assembles the container by hand: a 6-byte header, one 16-byte directory entry per size,
        /// then each size stored as a complete PNG. PNG-in-ICO is valid for Vista and later and is
        /// far simpler (and smaller) than the legacy BMP-with-AND-mask layout.</summary>
        private static void SaveIco(string path, WriteableBitmap bmp, IReadOnlyList<int> sizes)
        {
            var pngs = new List<byte[]>();
            foreach (int size in sizes)
            {
                var scaled = new TransformedBitmap(bmp,
                    new ScaleTransform((double)size / bmp.PixelWidth, (double)size / bmp.PixelHeight));
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(scaled));
                using var ms = new MemoryStream();
                enc.Save(ms);
                pngs.Add(ms.ToArray());
            }

            using var fs = new FileStream(path, FileMode.Create);
            using var w = new BinaryWriter(fs);
            w.Write((ushort)0);               // reserved
            w.Write((ushort)1);               // type 1 = icon
            w.Write((ushort)sizes.Count);     // image count

            int offset = 6 + 16 * sizes.Count;
            for (int i = 0; i < sizes.Count; i++)
            {
                // 256 is stored as 0 in the single-byte width/height fields.
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0);             // palette size (0 = no palette)
                w.Write((byte)0);             // reserved
                w.Write((ushort)1);           // colour planes
                w.Write((ushort)32);          // bits per pixel
                w.Write(pngs[i].Length);
                w.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (var png in pngs) w.Write(png);
        }

        /// <summary>Writes an uncompressed 32-bit Targa (.tga) - a simple, widely-supported
        /// interchange format that WPF also has no encoder for. Rows are written bottom-up, which
        /// is TGA's default origin.</summary>
        private static void SaveTga(string path, WriteableBitmap bmp)
        {
            int width = bmp.PixelWidth, height = bmp.PixelHeight;
            var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            int stride = width * 4;
            var pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);

            using var fs = new FileStream(path, FileMode.Create);
            using var w = new BinaryWriter(fs);
            w.Write((byte)0);      // no image ID
            w.Write((byte)0);      // no colour map
            w.Write((byte)2);      // uncompressed true-colour
            w.Write(new byte[5]);  // colour map spec (unused)
            w.Write((ushort)0);    // x origin
            w.Write((ushort)0);    // y origin
            w.Write((ushort)width);
            w.Write((ushort)height);
            w.Write((byte)32);     // bits per pixel
            w.Write((byte)8);      // 8 bits of alpha

            for (int y = height - 1; y >= 0; y--)   // bottom-up
                w.Write(pixels, y * stride, stride);
        }

        // Backs Page Setup / Print Preview / Print so settings chosen in Page Setup (paper size,
        // orientation, margins) actually carry through to what gets printed - using WinForms'
        // System.Drawing.Printing classes here since WPF doesn't ship an equivalent Page Setup or
        // Print Preview dialog of its own.
        private readonly System.Drawing.Printing.PrintDocument _printDoc = new();

        private void PageSetup_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.PageSetupDialog { Document = _printDoc };
            dlg.ShowDialog();
        }

        private void PrintPreview_Click(object sender, RoutedEventArgs e)
        {
            FinalizeFloatingSelection();
            _printDoc.PrintPage -= PrintDoc_PrintPage;
            _printDoc.PrintPage += PrintDoc_PrintPage;
            using var preview = new System.Windows.Forms.PrintPreviewDialog
            {
                Document = _printDoc,
                Width = 800,
                Height = 600,
                StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen
            };
            preview.ShowDialog();
        }

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            FinalizeFloatingSelection();
            using var dlg = new System.Windows.Forms.PrintDialog { Document = _printDoc };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                _printDoc.PrintPage -= PrintDoc_PrintPage;
                _printDoc.PrintPage += PrintDoc_PrintPage;
                _printDoc.Print();
            }
        }

        private void PrintDoc_PrintPage(object sender, System.Drawing.Printing.PrintPageEventArgs e)
        {
            using var ms = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(_document.GetFlattenedBitmap()));
            encoder.Save(ms);
            ms.Position = 0;
            using var bmp = new System.Drawing.Bitmap(ms);

            var bounds = e.MarginBounds;
            double scale = Math.Min((double)bounds.Width / bmp.Width, (double)bounds.Height / bmp.Height);
            scale = Math.Min(scale, 1.0); // never upscale small images to fill the page
            int w = Math.Max(1, (int)(bmp.Width * scale));
            int h = Math.Max(1, (int)(bmp.Height * scale));
            int x = bounds.X + (bounds.Width - w) / 2;
            int y = bounds.Y + (bounds.Height - h) / 2;
            e.Graphics.DrawImage(bmp, x, y, w, h);
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!ConfirmDiscardChanges()) e.Cancel = true;
        }

        // ===================================================================
        // Edit menu
        // ===================================================================

        private void UpdateEditMenuState()
        {
            MenuUndo.IsEnabled = _history.CanUndo;
            MenuRedo.IsEnabled = _history.CanRedo;
            bool hasSel = _selection.HasSelection;
            MenuCut.IsEnabled = hasSel;
            MenuCopy.IsEnabled = hasSel;
            MenuClearSel.IsEnabled = hasSel;
            MenuAlignSelection.IsEnabled = hasSel;
            MenuPaste.IsEnabled = TryClipboardHasImage();
        }

        /// <summary>Clipboard.ContainsImage() can throw (COMException/ExternalException) if another
        /// process holds the clipboard locked at that instant - a well-documented Windows
        /// flakiness issue, not something we can prevent. This runs on every selection-changed
        /// event, which several tools now trigger during normal use (e.g. every shape tool's
        /// OnMouseDown clears a leftover selection before starting a new draw) - an unhandled
        /// exception here would abort whatever tool operation was in progress partway through,
        /// leaving it in a broken half-started state. Never let this specific call take the rest
        /// of the app down with it.</summary>
        private static bool TryClipboardHasImage()
        {
            try { return Clipboard.ContainsImage(); }
            catch { return false; }
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            _tools[_currentToolKey].Cancel(_ctx);
            _history.Undo(_document);
            RefreshCanvasBinding();
            RefreshSelectionHandles();
            UpdateStatusSize();
        }

        private void Redo_Click(object sender, RoutedEventArgs e)
        {
            _history.Redo(_document);
            RefreshCanvasBinding();
            RefreshSelectionHandles();
            UpdateStatusSize();
        }

        private void Cut_Click(object sender, RoutedEventArgs e)
        {
            Copy_Click(sender, e);
            ClearSelection_Click(sender, e);
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            if (!_selection.HasSelection) return;
            var bmp = _selection.CopyForClipboard(_document.Surface);
            if (bmp != null) Clipboard.SetImage(bmp);
        }

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            if (!TryClipboardHasImage()) return;
            FinalizeFloatingSelection();
            var src = Clipboard.GetImage();
            var wb = new WriteableBitmap(new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0));
            _history.PushUndoState(_document, "Paste");

            // If the pasted image is bigger than the canvas, offer to grow the canvas so none of
            // it is lost. Pasting into a too-small canvas would otherwise silently clip whatever
            // hangs off the right or bottom edge as soon as the selection is committed.
            if (wb.PixelWidth > _document.Width || wb.PixelHeight > _document.Height)
            {
                int newW = Math.Max(_document.Width, wb.PixelWidth);
                int newH = Math.Max(_document.Height, wb.PixelHeight);
                var answer = MessageBox.Show(this,
                    $"The pasted image ({wb.PixelWidth} x {wb.PixelHeight}) is larger than the canvas " +
                    $"({_document.Width} x {_document.Height}).\n\nEnlarge the canvas to {newW} x {newH} so the whole image fits?",
                    "Splash", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                if (answer == MessageBoxResult.Cancel)
                {
                    // Undo the state we just pushed - nothing actually happened, so it shouldn't
                    // leave a do-nothing step sitting in the history.
                    _history.DiscardLastPush(_document);
                    return;
                }
                if (answer == MessageBoxResult.Yes)
                {
                    // Grows from the top-left, so existing artwork keeps its coordinates and only
                    // new, empty space is added to the right and bottom.
                    _document.Resize(newW, newH, _colors.Background);
                    RefreshCanvasBinding();
                }
            }

            _selection.BeginPaste(wb, 0, 0);
            _canvas.ClearPreview();
            SelectRectToolRenderHelper();
            _canvas.ShowSelection(_selection.Bounds);
            SelectTool("Select");
        }

        /// <summary>True for any tool that can move/resize the current selection on its own - the
        /// three dedicated selection tools, always, plus any drag-shape tool (Rectangle, Ellipse,
        /// ...) while it's the active tool, since DragShapeToolBase now handles its own just-drawn
        /// pending shape directly instead of forcing a detour through the Select tool.</summary>
        private bool CurrentToolHandlesSelection() =>
            _currentToolKey is "Select" or "FreeFormSelect" or "MagicWand"
            || (_tools.TryGetValue(_currentToolKey, out var t) && t is DragShapeToolBase);

        /// <summary>Shows/hides/repositions the 4 corner resize handles on the current selection.
        /// Only offered when on a tool that knows how to act on a selection (see
        /// CurrentToolHandlesSelection) and a selection actually exists. Called whenever the
        /// selection changes, the tool changes, or the zoom changes, so the handles never end up
        /// stale relative to what's on screen.</summary>
        private void RefreshSelectionHandles()
        {
            foreach (var h in _selectionResizeHandles) HandleLayer.Children.Remove(h);
            _selectionResizeHandles.Clear();

            bool eligible = CurrentToolHandlesSelection() && _document != null && _selection.HasSelection;
            if (!eligible) return;

            var b = _selection.Bounds.Value;
            AddSelectionResizeHandle(isLeft: true, isTop: true, b);
            AddSelectionResizeHandle(isLeft: false, isTop: true, b);
            AddSelectionResizeHandle(isLeft: true, isTop: false, b);
            AddSelectionResizeHandle(isLeft: false, isTop: false, b);
        }

        private void AddSelectionResizeHandle(bool isLeft, bool isTop, Int32Rect selBounds)
        {
            var handle = MakeResizeHandleVisual();
            double hx = (isLeft ? selBounds.X : selBounds.X + selBounds.Width) * _zoom;
            double hy = (isTop ? selBounds.Y : selBounds.Y + selBounds.Height) * _zoom;
            Canvas.SetLeft(handle, hx - 3);
            Canvas.SetTop(handle, hy - 3);
            var cursor = isLeft == isTop ? Cursors.SizeNWSE : Cursors.SizeNESW;

            WireManualDrag(handle, cursor,
                onStart: () =>
                {
                    var b = _selection.Bounds.Value;
                    // Tracked in screen space (double precision) for the whole drag, only
                    // converted to document-space integers once at the end - accumulating and
                    // rounding a fractional document-pixel delta on every single tick would lose
                    // sub-pixel movement and make the resize feel sticky, especially at high zoom.
                    _pendingResizeScreenBounds = new Rect(b.X * _zoom, b.Y * _zoom, b.Width * _zoom, b.Height * _zoom);
                },
                onDelta: (dx, dy) =>
                {
                    double x = _pendingResizeScreenBounds.X, y = _pendingResizeScreenBounds.Y;
                    double w = _pendingResizeScreenBounds.Width, h = _pendingResizeScreenBounds.Height;
                    if (isLeft) { x += dx; w -= dx; } else { w += dx; }
                    if (isTop) { y += dy; h -= dy; } else { h += dy; }
                    w = Math.Max(4, w);
                    h = Math.Max(4, h);
                    _pendingResizeScreenBounds = new Rect(x, y, w, h);

                    // Cheap rubber-band preview only during the drag - the actual per-pixel
                    // rescale is deferred to release, matching how shape-tool previews work.
                    _canvas.ShowSelection(ScreenRectToDocRect(_pendingResizeScreenBounds));
                },
                onEnd: () =>
                {
                    var docRect = ScreenRectToDocRect(_pendingResizeScreenBounds);

                    // A still-pending shape is re-rendered from its original defining points at
                    // the new size, rather than stretching the pixels of the previous render.
                    // That's the whole reason a shape stays "virtual" until committed: you can
                    // resize it as many times as you like and it stays exactly as crisp as the
                    // first draw, because every render starts from the shape's definition rather
                    // than from an already-resampled bitmap.
                    if (_pendingShape != null)
                    {
                        RenderPendingShapeToFloat(docRect);
                    }
                    else
                    {
                        if (!_selection.IsFloating)
                        {
                            _history.PushUndoState(_document, "Resize Selection");
                            _selection.Lift(_document.Surface, _colors.Background);
                        }
                        _selection.ResizeTo(docRect.X, docRect.Y, docRect.Width, docRect.Height);
                    }

                    _canvas.ClearPreview();
                    SelectRectToolRenderHelper();
                    _canvas.ShowSelection(_selection.Bounds);
                    RefreshSelectionHandles();
                });

            HandleLayer.Children.Add(handle);
            _selectionResizeHandles.Add(handle);
        }

        private Int32Rect ScreenRectToDocRect(Rect r) => new Int32Rect(
            (int)Math.Round(r.X / _zoom), (int)Math.Round(r.Y / _zoom),
            Math.Max(1, (int)Math.Round(r.Width / _zoom)), Math.Max(1, (int)Math.Round(r.Height / _zoom)));

        private void SelectRectToolRenderHelper()
        {
            if (_selection.IsFloating && _selection.Bounds.HasValue)
            {
                var b = _selection.Bounds.Value;
                // Generated content (a pending shape, a paste) must never have background-coloured
                // pixels keyed out - those are real drawn output, not the see-through backdrop the
                // Opaque/Transparent option refers to. Keeping this in step with SelectionManager.
                // Commit is what makes the on-screen preview match what actually gets placed.
                Color? transparentColor = (_selection.DrawOpaque || _selection.ContentIsGenerated) ? null : _colors.Background;
                _canvas.PreviewSurface.Lock();
                _canvas.PreviewSurface.Blit(_selection.Floating, b.X, b.Y, transparentColor);
                _canvas.PreviewSurface.Unlock();
            }
        }

        /// <summary>Nudges the active selection by (dx, dy) document pixels - used for the arrow-key
        /// move shortcuts. Lifts the selection off the canvas first if it hasn't been already
        /// (matching the same one-undo-entry-per-drag semantics the mouse-driven move uses).</summary>
        private void NudgeSelection(int dx, int dy)
        {
            if (!_selection.HasSelection) return;
            if (!_selection.IsFloating)
            {
                _history.PushUndoState(_document, "Move Selection");
                _selection.Lift(_document.Surface, _colors.Background);
            }
            var b = _selection.Bounds.Value;
            _selection.MoveTo(b.X + dx, b.Y + dy);
            _canvas.ClearPreview();
            SelectRectToolRenderHelper();
            _canvas.ShowSelection(_selection.Bounds);
        }

        /// <summary>Moves the current selection to a position relative to the canvas edges. The
        /// horizontal and vertical parts are independent, so "Left" only changes X and leaves the
        /// selection's own vertical position alone - which is what makes it usable for lining
        /// several things up one axis at a time, rather than every command yanking the selection to
        /// a corner.</summary>
        private void AlignSelection_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem item || item.Tag is not string mode) return;
            if (!_selection.HasSelection || _document == null) return;

            var b = _selection.Bounds.Value;
            int x = b.X, y = b.Y;
            int maxX = Math.Max(0, _document.Width - b.Width);
            int maxY = Math.Max(0, _document.Height - b.Height);

            switch (mode)
            {
                case "Left":        x = 0; break;
                case "Right":       x = maxX; break;
                case "CenterH":     x = maxX / 2; break;
                case "Top":         y = 0; break;
                case "Bottom":      y = maxY; break;
                case "CenterV":     y = maxY / 2; break;
                case "TopLeft":     x = 0;        y = 0; break;
                case "TopRight":    x = maxX;     y = 0; break;
                case "BottomLeft":  x = 0;        y = maxY; break;
                case "BottomRight": x = maxX;     y = maxY; break;
                case "Center":      x = maxX / 2; y = maxY / 2; break;
                default: return;
            }

            if (x == b.X && y == b.Y) return; // already there - don't push a do-nothing undo step

            // Lift first if the selection is still part of the document, exactly as a drag or an
            // arrow-key nudge does, so aligning moves the content rather than stamping a copy of it.
            if (!_selection.IsFloating)
            {
                _history.PushUndoState(_document, "Align Selection");
                _selection.Lift(_document.Surface, _colors.Background);
            }
            _selection.MoveTo(x, y);
            _canvas.ClearPreview();
            SelectRectToolRenderHelper();
            _canvas.ShowSelection(_selection.Bounds);
            RefreshSelectionHandles();
            StatusText.Text = $"Selection aligned: {mode}";
        }

        private void ClearSelection_Click(object sender, RoutedEventArgs e)
        {
            if (!_selection.HasSelection) return;
            _history.PushUndoState(_document, "Clear Selection");
            _selection.DeleteSelection(_document.Surface, _colors.Background);
            _canvas.ClearPreview();
            _canvas.ShowSelection(null);
            _document.MarkDirty();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            FinalizeFloatingSelection();
            var bounds = new Int32Rect(0, 0, _document.Width, _document.Height);
            _selection.BeginSelection(bounds);
            _canvas.ShowSelection(bounds);
            SelectTool("Select");
        }

        // ===================================================================
        // View menu
        // ===================================================================

        private void ToggleToolbox_Click(object sender, RoutedEventArgs e) =>
            ToolboxPanel.Visibility = MenuToolbox.IsChecked ? Visibility.Visible : Visibility.Collapsed;

        private void ToggleColorBox_Click(object sender, RoutedEventArgs e) =>
            ColorBoxPanel.Visibility = MenuColorBox.IsChecked ? Visibility.Visible : Visibility.Collapsed;

        private void ToggleStatusBar_Click(object sender, RoutedEventArgs e) =>
            AppStatusBar.Visibility = MenuStatusBar.IsChecked ? Visibility.Visible : Visibility.Collapsed;

        private void Zoom100_Click(object sender, RoutedEventArgs e) => SetZoom(1);
        private void Zoom200_Click(object sender, RoutedEventArgs e) => SetZoom(2);
        private void Zoom400_Click(object sender, RoutedEventArgs e) => SetZoom(4);
        private void Zoom600_Click(object sender, RoutedEventArgs e) => SetZoom(6);
        private void Zoom800_Click(object sender, RoutedEventArgs e) => SetZoom(8);

        private void ToggleGrid_Click(object sender, RoutedEventArgs e)
        {
            _showGrid = MenuShowGrid.IsChecked;
            UpdateGridOverlay();
            if (_showGrid && _zoom < 4)
                StatusText.Text = "Grid will appear once you zoom to 400% or higher.";
        }

        private void ToggleHistoryWindow_Click(object sender, RoutedEventArgs e) =>
            HistorySection.Visibility = MenuHistoryWindow.IsChecked ? Visibility.Visible : Visibility.Collapsed;

        private void ToggleLayersWindow_Click(object sender, RoutedEventArgs e) =>
            LayersSection.Visibility = MenuLayersWindow.IsChecked ? Visibility.Visible : Visibility.Collapsed;

        private void ThemeDark_Click(object sender, RoutedEventArgs e) => SetTheme(ThemeManager.Dark);
        private void ThemeLight_Click(object sender, RoutedEventArgs e) => SetTheme(ThemeManager.Light);

        /// <summary>The two Theme menu entries behave like a radio group even though MenuItem has
        /// no built-in exclusive-checkable mode - clicking one applies that theme and syncs both
        /// checkmarks to match.</summary>
        private void SetTheme(string themeName)
        {
            ThemeManager.Apply(themeName);
            MenuThemeDark.IsChecked = themeName == ThemeManager.Dark;
            MenuThemeLight.IsChecked = themeName == ThemeManager.Light;
        }

        private void ShortcutManager_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ShortcutManagerWindow(_shortcuts) { Owner = this };
            dlg.ShowDialog();
            // Bindings may have changed (rebind/clear/reset) - the dispatch table itself doesn't
            // need rebuilding since it's keyed by action id, not by key combo, but re-reading here
            // keeps this future-proof if that ever changes.
        }

        // ===================================================================
        // Plugins
        // ===================================================================

        private void LoadAndShowPlugins()
        {
            _pluginManager.LoadAll();
            RebuildPluginsMenu();

            if (_pluginManager.LoadErrors.Count > 0)
            {
                StatusText.Text = $"{_pluginManager.LoadErrors.Count} plugin(s) failed to load - see Plugins menu.";
            }
        }

        private void RebuildPluginsMenu()
        {
            MenuPlugins.Items.Clear();

            if (_pluginManager.Plugins.Count == 0)
            {
                MenuPlugins.Items.Add(new MenuItem { Header = "(no plugins installed)", IsEnabled = false });
            }
            else
            {
                foreach (var plugin in _pluginManager.Plugins)
                {
                    var item = new MenuItem { Header = plugin.Name };
                    if (!string.IsNullOrEmpty(plugin.Description)) item.ToolTip = plugin.Description;
                    item.Click += (s, e) => RunPlugin(plugin);
                    MenuPlugins.Items.Add(item);
                }
            }

            if (_pluginManager.LoadErrors.Count > 0)
            {
                MenuPlugins.Items.Add(new Separator());
                foreach (var err in _pluginManager.LoadErrors)
                    MenuPlugins.Items.Add(new MenuItem { Header = $"\u26A0 {err}", IsEnabled = false });
            }

            MenuPlugins.Items.Add(new Separator());
            var openFolder = new MenuItem { Header = "_Open Plugins Folder..." };
            openFolder.Click += OpenPluginsFolder_Click;
            MenuPlugins.Items.Add(openFolder);
            var reload = new MenuItem { Header = "_Reload Plugins" };
            reload.Click += ReloadPlugins_Click;
            MenuPlugins.Items.Add(reload);
        }

        private void RunPlugin(PluginManager.LoadedPlugin plugin)
        {
            FinalizeFloatingSelection();
            _history.PushUndoState(_document, plugin.Name);
            try
            {
                plugin.Apply(_document.Bitmap);
                _document.MarkDirty();
                RefreshCanvasBinding();
                StatusText.Text = $"Applied plugin: {plugin.Name}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Plugin \"{plugin.Name}\" threw an error and was not applied:\n\n{ex.Message}",
                    "Plugin Error", MessageBoxButton.OK, MessageBoxImage.Error);
                // The plugin may have partially modified the bitmap before throwing - roll back
                // cleanly (not a plain Undo, which would leave that broken state reachable via
                // Redo).
                _history.DiscardLastPush(_document);
                RefreshCanvasBinding();
            }
        }

        private void OpenPluginsFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = PluginManager.PluginsFolder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not open the Plugins folder:\n{ex.Message}\n\nLocation: {PluginManager.PluginsFolder}",
                    "Plugins", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ReloadPlugins_Click(object sender, RoutedEventArgs e) => LoadAndShowPlugins();

        /// <summary>Jumps to any point in the undo/redo timeline, requested by clicking an entry
        /// in the History window - mirrors the same refresh sequence Undo_Click/Redo_Click use.</summary>
        private void JumpToHistoryIndex(int index)
        {
            _tools[_currentToolKey].Cancel(_ctx);
            _history.JumpTo(index, _document);
            RefreshCanvasBinding();
            RefreshSelectionHandles();
            UpdateStatusSize();
        }

        // ===================================================================
        // Image menu
        // ===================================================================

        private void FlipH_Click(object sender, RoutedEventArgs e)
        {
            if (TryTransformSelection(SelectionTransform.FlipHorizontal, "Flip Selection Horizontal")) return;
            ApplyTransform(FlipHorizontal, "Flip Horizontal");
        }

        private void FlipV_Click(object sender, RoutedEventArgs e)
        {
            if (TryTransformSelection(SelectionTransform.FlipVertical, "Flip Selection Vertical")) return;
            ApplyTransform(FlipVertical, "Flip Vertical");
        }

        private void Rotate90_Click(object sender, RoutedEventArgs e) => ApplyRotate(90);
        private void Rotate180_Click(object sender, RoutedEventArgs e) => ApplyRotate(180);
        private void Rotate270_Click(object sender, RoutedEventArgs e) => ApplyRotate(270);

        /// <summary>If a selection is active, applies the transform to just that selection (which
        /// is what classic Paint's Image > Flip/Rotate does when something is selected) and returns
        /// true. Returns false when there's no selection, letting the caller fall through to
        /// transforming the whole picture instead.</summary>
        private bool TryTransformSelection(SelectionTransform transform, string label)
        {
            if (!_selection.HasSelection) return false;

            if (!_selection.IsFloating)
            {
                _history.PushUndoState(_document, label);
                _selection.Lift(_document.Surface, _colors.Background);
            }
            _selection.TransformFloating(transform);
            _canvas.ClearPreview();
            SelectRectToolRenderHelper();
            _canvas.ShowSelection(_selection.Bounds);
            RefreshSelectionHandles();
            return true;
        }
        private void Invert_Click(object sender, RoutedEventArgs e) => ApplyTransform(InvertColors, "Invert Colors");

        private void ApplyTransform(Action<RasterSurface, RasterSurface> op, string label)
        {
            FinalizeFloatingSelection();
            RasterizeActiveLayerIfText();
            _history.PushUndoState(_document, label);
            var src = _document.Surface;
            var dst = new RasterSurface(src.Width, src.Height, _colors.Background);
            dst.Lock(); src.Lock();
            op(src, dst);
            src.Unlock(); dst.Unlock();
            _document.ReplaceSurface(dst);
            RefreshCanvasBinding();
        }

        private static void FlipHorizontal(RasterSurface src, RasterSurface dst)
        {
            for (int y = 0; y < src.Height; y++)
                for (int x = 0; x < src.Width; x++)
                    dst.SetPixel(src.Width - 1 - x, y, src.GetPixel(x, y));
        }

        private static void FlipVertical(RasterSurface src, RasterSurface dst)
        {
            for (int y = 0; y < src.Height; y++)
                for (int x = 0; x < src.Width; x++)
                    dst.SetPixel(x, src.Height - 1 - y, src.GetPixel(x, y));
        }

        private static void InvertColors(RasterSurface src, RasterSurface dst)
        {
            for (int y = 0; y < src.Height; y++)
                for (int x = 0; x < src.Width; x++)
                {
                    var c = src.GetPixel(x, y);
                    dst.SetPixel(x, y, Color.FromArgb(c.A, (byte)(255 - c.R), (byte)(255 - c.G), (byte)(255 - c.B)));
                }
        }

        private void ApplyRotate(int degrees)
        {
            var asSelectionTransform = degrees switch
            {
                90 => SelectionTransform.Rotate90,
                270 => SelectionTransform.Rotate270,
                _ => SelectionTransform.Rotate180,
            };
            if (TryTransformSelection(asSelectionTransform, $"Rotate Selection {degrees}\u00b0")) return;

            FinalizeFloatingSelection();
            RasterizeActiveLayerIfText();
            _history.PushUndoState(_document, $"Rotate {degrees}\u00b0");
            var src = _document.Surface;
            bool swap = degrees != 180;
            int nw = swap ? src.Height : src.Width;
            int nh = swap ? src.Width : src.Height;
            var dst = new RasterSurface(nw, nh, _colors.Background);
            src.Lock(); dst.Lock();
            for (int y = 0; y < src.Height; y++)
            {
                for (int x = 0; x < src.Width; x++)
                {
                    var c = src.GetPixel(x, y);
                    int dx, dy;
                    switch (degrees)
                    {
                        case 90: dx = src.Height - 1 - y; dy = x; break;
                        case 270: dx = y; dy = src.Width - 1 - x; break;
                        default: dx = src.Width - 1 - x; dy = src.Height - 1 - y; break; // 180
                    }
                    dst.SetPixel(dx, dy, c);
                }
            }
            src.Unlock(); dst.Unlock();
            _document.ReplaceSurface(dst);
            RefreshCanvasBinding();
        }

        private void StretchSkew_Click(object sender, RoutedEventArgs e)
        {
            FinalizeFloatingSelection();
            var dlg = new StretchSkewDialog { Owner = this };
            if (dlg.ShowDialog() != true) return;

            var src = _document.Surface;
            int nw = Math.Max(1, (int)(src.Width * dlg.StretchX / 100.0));
            int nh = Math.Max(1, (int)(src.Height * dlg.StretchY / 100.0));
            double skewX = dlg.SkewX * Math.PI / 180.0;
            double skewY = dlg.SkewY * Math.PI / 180.0;

            _history.PushUndoState(_document, "Stretch/Skew");
            var dst = new RasterSurface(nw, nh, _colors.Background);
            src.Lock(); dst.Lock();
            for (int y = 0; y < nh; y++)
            {
                for (int x = 0; x < nw; x++)
                {
                    // Simple nearest-neighbor inverse mapping - deliberately unsophisticated,
                    // matching legacy Paint's simple raster ops rather than a modern resampler.
                    double sx = x * src.Width / (double)nw - y * Math.Tan(skewX);
                    double sy = y * src.Height / (double)nh - x * Math.Tan(skewY);
                    int ix = (int)Math.Round(sx), iy = (int)Math.Round(sy);
                    if (ix >= 0 && iy >= 0 && ix < src.Width && iy < src.Height)
                        dst.SetPixel(x, y, src.GetPixel(ix, iy));
                }
            }
            src.Unlock(); dst.Unlock();
            _document.ReplaceSurface(dst);
            RefreshCanvasBinding();
            UpdateStatusSize();
        }

        private void Attributes_Click(object sender, RoutedEventArgs e)
        {
            FinalizeFloatingSelection();
            var dlg = new AttributesDialog(_document.Width, _document.Height, _document.DpiX) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            _history.PushUndoState(_document, dlg.ClearRequested ? "Clear Image" : "Attributes");
            if (dlg.ClearRequested)
                _document.Resize(1, 1, _colors.Background);
            else
                _document.Resize(dlg.NewWidth, dlg.NewHeight, _colors.Background);
            _document.DpiX = _document.DpiY = dlg.NewDpi;
            RefreshCanvasBinding();
            UpdateStatusSize();
        }

        private void ClearImage_Click(object sender, RoutedEventArgs e)
        {
            FinalizeFloatingSelection();
            RasterizeActiveLayerIfText();
            _history.PushUndoState(_document, "Clear Image");
            _document.Surface.Clear(_colors.Background);
            _document.MarkDirty();
            RefreshCanvasBinding();
        }

        private void DrawOpaque_Click(object sender, RoutedEventArgs e)
        {
            _selection.DrawOpaque = MenuDrawOpaque.IsChecked;
            if (_currentToolKey is "Select" or "FreeFormSelect") BuildToolOptions(_currentToolKey);
        }

        // ===================================================================
        // Colors menu
        // ===================================================================

        private void EditColors_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new EditColorsDialog(_colors.Foreground) { Owner = this };
            if (dlg.ShowDialog() == true) _colors.SetForeground(dlg.SelectedColor);
        }

        private void FgSwatch_Click(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                var dlg = new EditColorsDialog(_colors.Foreground) { Owner = this };
                if (dlg.ShowDialog() == true) _colors.SetForeground(dlg.SelectedColor);
            }
        }
        private void FgSwatch_RightClick(object sender, MouseButtonEventArgs e) { }

        private void BgSwatch_Click(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                var dlg = new EditColorsDialog(_colors.Background) { Owner = this };
                if (dlg.ShowDialog() == true) _colors.SetBackground(dlg.SelectedColor);
            }
        }
        private void BgSwatch_RightClick(object sender, MouseButtonEventArgs e) { }

        // ===================================================================
        // Help menu
        // ===================================================================

        private void HelpTopics_Click(object sender, RoutedEventArgs e) =>
            new HelpTopicsDialog { Owner = this }.ShowDialog();

        private void About_Click(object sender, RoutedEventArgs e) => new AboutDialog { Owner = this }.ShowDialog();
    }
}
