using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace PaintClone.Dialogs
{
    public partial class HelpTopicsDialog : Window
    {
        private class Topic
        {
            public string Title;
            public string Body;
        }

        private readonly List<Topic> _topics = new();
        private readonly Dictionary<TreeViewItem, int> _nodeToTopic = new();
        private bool _suppressSelection;

        public HelpTopicsDialog()
        {
            InitializeComponent();
            BuildTopics();
            BuildTree();
            ShowTopic(0);
        }

        private void BuildTopics()
        {
            void Add(string title, string body) => _topics.Add(new Topic { Title = title, Body = body });

            Add("Welcome to Paint",
                "Paint is a drawing program you can use to create simple pictures, or edit " +
                "photographs and other bitmap images. Pictures you create in Paint are bitmaps - " +
                "grids of individual colored pixels - so Paint is best suited to simple drawings, " +
                "touch-ups, and screenshots rather than print-quality artwork.\n\n" +
                "Select a topic on the left to learn more about a specific part of Paint.");

            Add("The Paint window",
                "The Paint window has the following parts:\n\n" +
                "\u2022 The menu bar, with File, Edit, View, Image, Colors, and Help menus.\n" +
                "\u2022 The tool box, on the left, with the drawing and selection tools.\n" +
                "\u2022 The tool options area, below the tool box, which changes depending on the " +
                "tool you have selected.\n" +
                "\u2022 The color box, above the picture, with the available colors and the current " +
                "foreground/background color indicator.\n" +
                "\u2022 The drawing area (canvas) itself.\n" +
                "\u2022 The status bar, along the bottom, which shows helpful information such as the " +
                "pointer's position and the size of the picture.");

            Add("Draw with the Pencil, Brush, or Airbrush",
                "To draw a free-form line:\n\n" +
                "1. Click the Pencil, Brush, or Airbrush tool in the tool box.\n" +
                "2. If you like, choose a shape or size in the tool options area.\n" +
                "3. Point to where you want the line to begin.\n" +
                "4. Drag the pointer to draw. Use the left mouse button to draw in the foreground " +
                "color, or the right mouse button to draw in the background color.\n\n" +
                "The Pencil always draws a thin, hard-edged line. The Brush can draw a wider, round " +
                "or square stroke. The Airbrush sprays a scattered pattern of dots for as long as " +
                "you hold the button down.");

            Add("Erase part of a picture",
                "To erase part of a picture, click the Eraser tool, then drag the pointer over the " +
                "part you want to erase.\n\n" +
                "The Eraser does not simply paint white - it paints with the current background " +
                "color. If you want to erase to a color other than white, set the background color " +
                "first by right-clicking a color in the color box.");

            Add("Fill an area with color",
                "To fill an enclosed area with color:\n\n" +
                "1. Click the Fill With Color tool.\n" +
                "2. Click inside the area you want to fill, using the left mouse button to fill with " +
                "the foreground color, or the right mouse button to fill with the background color.\n\n" +
                "If the area is not completely enclosed by a solid line, the fill color will leak out " +
                "into the surrounding picture - if that happens, undo (Ctrl+Z) and close the gap in " +
                "the outline first.");

            Add("Draw a line or shape",
                "Paint has separate tools for lines, arrows, curves, rectangles, polygons, ellipses, " +
                "rounded rectangles, stars, and gradients.\n\n" +
                "The Gradient tool blends the foreground colour into the background colour across " +
                "the area you drag; the direction you drag sets the angle of the blend. A gradient " +
                "is added to the picture as soon as you release the mouse and the Gradient tool " +
                "stays selected, so you can lay down several in a row. The Star tool lets you " +
                "choose between 3 and 12 points in the tool options area.\n\n" +
                "The Arrow tool picks a head for each end of the line separately, from seventeen " +
                "shapes covering ordinary arrows and the notations used in technical diagrams - " +
                "hollow and solid triangles and diamonds, circles, sockets, bars and crow's feet. " +
                "Because a relationship is usually more than a head - a UML realization is a hollow " +
                "triangle on a dashed line, where the same head on a solid line means something " +
                "else - the Preset dropdown names the standard combinations (generalization, " +
                "realization, dependency, aggregation, composition, interfaces, sequence-diagram " +
                "messages, and the ER cardinalities) and sets both ends and the line style at once. " +
                "Everything stays adjustable afterwards; the preset then reads \"Custom\".\n\n" +
                "Hold Shift while dragging a line, arrow, or gradient to snap its direction to " +
                "45-degree steps, which includes exact horizontal and vertical.\n\n" +
                "The Curve tool works in three drags: drag once to draw the straight baseline, then " +
                "drag a second time to bend it, and a third time to add the opposite bend. The " +
                "curve is added to the picture after that third drag.\n\n" +
                "To draw a shape, click its tool, then drag on the picture to define it. The shape " +
                "previews as you drag and is only added to the picture when you release the mouse " +
                "button. Hold down Shift while dragging to draw a perfectly straight 45-degree line, " +
                "or a perfect square or circle.\n\n" +
                "In the tool options area, choose whether the shape is drawn as an outline only, " +
                "filled only, or both, pick a line thickness, and choose hard or smoothed " +
                "(anti-aliased) edges. Each tool remembers its own edge setting, since a pencil " +
                "usually wants hard pixel edges while a curve usually looks better smoothed.\n\n" +
                "After you release the mouse, the shape stays selected and is not yet part of the " +
                "picture - Paint keeps it as a live, still-editable shape. You can drag it to a new " +
                "spot, or drag a corner handle to resize it, as many times as you like, and it is " +
                "redrawn fresh at its new size every time rather than having its pixels stretched. " +
                "That means it stays perfectly crisp no matter how much you adjust it. The shape is " +
                "finally drawn into the picture when you click somewhere else or press Ctrl+D - and " +
                "once it is, Paint switches back to whichever shape tool you drew it with, so you " +
                "can draw another straight away. Press Esc while it's still selected to discard it completely - since " +
                "it was never added to the picture, there's nothing to undo.");

            Add("Choose colors",
                "The color box above the picture shows the available colors, and the current " +
                "foreground and background colors in the overlapping squares at its left edge.\n\n" +
                "Left-click a color to make it the foreground color, used when you draw with the " +
                "left mouse button. Right-click a color to make it the background color, used when " +
                "you draw with the right mouse button.");

            Add("Edit colors",
                "Choose Edit Colors from the Colors menu for a full color picker: drag in the large " +
                "square and the strip beside it to pick visually, or type exact Red/Green/Blue, " +
                "Hue/Saturation/Lightness, or a hex code. Hover over any color swatch anywhere in " +
                "Paint to see its exact hex, RGB, and HSL values.\n\n" +
                "Besides the built-in 100-color palette (a two-row strip where each column pairs a " +
                "darker shade above with its lighter counterpart below), the Material and Flat UI tabs offer two " +
                "well-known professional color sets. Once you've picked a color, click Add to Custom " +
                "Colors to save it - custom colors, and your recently-used colors, persist across " +
                "Paint sessions. Quick actions along the bottom give you Grayscale, Invert, " +
                "Complementary, Random, and Transparent with one click.");

            Add("Select part of a picture",
                "Use the Select tool for a rectangular selection, Free-Form Select to trace an " +
                "irregular area, or the Magic Wand to select every pixel of the same color touching " +
                "the one you click. Drag on the picture to make a Select or Free-Form Select " +
                "selection; just click for the Magic Wand.\n\n" +
                "Once you have a selection, drag inside it to move it - the area it leaves behind is " +
                "filled with the background color. The Opaque/Transparent option in the tool options " +
                "area controls whether background-colored pixels in the selection move with it, or " +
                "are treated as see-through. Press Ctrl+D or click outside the selection to deselect.\n\n" +
                "Image > Align Selection snaps the selection to a position relative to the " +
                "canvas - the edges, the centre, or any corner. The horizontal and vertical " +
                "commands work independently, so choosing Left moves it to the left edge without " +
                "changing how far down the picture it sits; combine two commands to line " +
                "something up on both axes.\n\n" +
                "A selection also shows four small square handles at the corners of its bounding " +
                "box - drag one to stretch the selected content to a new size. This works for " +
                "every selection type, including the irregular shapes made by Free-Form Select " +
                "and the Magic Wand.");

            Add("The Magic Wand",
                "The Magic Wand selects a region by color rather than by shape: click any pixel and " +
                "every pixel connected to it with the exact same color becomes selected - useful for " +
                "grabbing a solid-colored area, like a shape's fill or a flat background, without " +
                "tracing its outline by hand.\n\n" +
                "It uses the same matching rule as Fill With Color: colors must match exactly, and " +
                "the match only spreads through directly touching pixels, so a thin gap in an outline " +
                "will stop it from leaking into the rest of the picture. Once selected, drag inside " +
                "the selection to move it, just like any other selection tool.");

            Add("Cut, copy, and paste a selection",
                "With a selection active, use Edit > Cut (Ctrl+X) or Edit > Copy (Ctrl+C) to place it " +
                "on the Windows Clipboard, and Edit > Paste (Ctrl+V) to bring it back - or to bring in " +
                "an image copied from another program. If what you paste is larger than the canvas, " +
                "Paint offers to enlarge the canvas so none of it is lost. " +
                "a picture copied from another program. Press Delete to erase a selection without " +
                "copying it.");

            Add("Add text to a picture",
                "To add text:\n\n" +
                "1. Click the Text tool.\n" +
                "2. Drag out a text box on the picture.\n" +
                "3. Type your text. Drag the small handle at the bottom-right corner of the box to " +
                "resize it.\n" +
                "4. Choose a font, size, and style (bold, italic, underline) from the tool options " +
                "area - changes apply immediately, even to text you've already typed.\n\n" +
                "The text becomes a permanent part of the picture as soon as you click somewhere else.");

            Add("Zoom in or out",
                "Use the Magnifier tool, or View > Zoom, to change how large the picture appears - " +
                "from 100% up to 800%. Click with the Magnifier to step through zoom levels (right-" +
                "click to step back down), or drag out a box around a specific area to zoom straight " +
                "to whichever preset level best fits that area in the window, centered on it. " +
                "Zooming only changes how the picture is displayed; it does not change the actual " +
                "size of the picture.");

            Add("Show the grid",
                "Choose View > Zoom > Show Grid to display a pixel grid over the picture, which makes " +
                "it easier to edit individual pixels precisely. The grid is most useful once you have " +
                "zoomed in.");

            Add("Resize the canvas",
                "To change the size of the picture:\n\n" +
                "\u2022 Drag the small square handles on the right edge, bottom edge, or bottom-right " +
                "corner of the picture. The top-left corner always stays fixed in place.\n" +
                "\u2022 Or, for exact dimensions, choose Image > Attributes and type the width and " +
                "height you want.");

            Add("Stretch, skew, flip, or rotate a picture",
                "Choose Image > Stretch/Skew to resize the picture by a percentage, or slant it at an " +
                "angle. Choose Image > Flip/Rotate to flip the picture horizontally or vertically, or " +
                "rotate it in 90-degree steps.\n\n" +
                "If you have an active selection, Flip/Rotate applies to just that selection rather " +
                "than the whole picture - so you can flip or rotate one piece of your drawing in " +
                "place, then drag it wherever you want it. Rotating by 90 or 270 degrees swaps the " +
                "selection's width and height, and it stays centered where it was rather than " +
                "jumping to a different position.");

            Add("Save a picture",
                "Choose File > Save (Ctrl+S) to save your changes to the current file, or File > Save " +
                "As to save to a new file or a different format. Paint can save as a Monochrome, " +
                "16-color, 256-color, or 24-bit Bitmap, as well as JPEG, GIF, TIFF, PNG, JPEG XR, " +
                "Windows Icon (.ico), and Targa (.tga).\n\n" +
                "Saving as .ico opens an extra window first, where you choose which sizes to store " +
                "in the icon (16x16 up to 256x256) and preview how your artwork looks at each one - " +
                "worth checking, since a drawing that reads clearly at full size often turns to mush " +
                "at 16x16. Every size is stored as 32-bit PNG data inside the file, which keeps full " +
                "transparency.");

            Add("Open a picture",
                "Choose File > Open (Ctrl+O) and browse to the picture you want to edit. Paint can " +
                "open Bitmap, PNG, JPEG, GIF, and TIFF files.");

            Add("Print a picture",
                "Choose File > Page Setup to choose paper size, orientation, and margins; File > Print " +
                "Preview to see how the picture will look on the page; and File > Print (Ctrl+P) to " +
                "send it to a printer.");

            Add("Undo and redo",
                "Choose Edit > Undo (Ctrl+Z) to reverse your last action, and Edit > Repeat (Ctrl+Y) to " +
                "restore an action you just undid. Undo works on whole actions - a whole brush stroke, " +
                "a whole shape, a whole fill - not on every individual pixel. The History window " +
                "(View > History Window) shows every step by name and lets you jump straight to any " +
                "point instead of pressing Ctrl+Z repeatedly.");

            Add("Working with layers",
                "Layers let you keep different parts of a picture on separate, independent sheets - " +
                "sketch on one layer and ink on another, for example, without one affecting the other. " +
                "Open the Layers window from View > Layers Window.\n\n" +
                "Every drawing tool always works on the current active layer, shown highlighted in the " +
                "Layers window - click a layer's row to make it active. Use the checkbox next to a " +
                "layer to show or hide it, Add to create a new blank layer, Del to remove the active " +
                "one, the up/down arrows to reorder layers, and Merge to flatten a layer down into the " +
                "one below it.\n\n" +
                "New layers start fully transparent, so anything on layers below shows through until " +
                "you draw on them. Saving, printing, and copying the whole picture always combines " +
                "every visible layer into one flat image automatically - you don't need to merge them " +
                "first just to save.\n\n" +
                "Drawing tools act on the active layer alone, but Image > Invert Colors acts on the " +
                "whole picture, every layer at once - it's a change to the image, not to one sheet " +
                "of it. That matters most when you've painted on the transparent layer a new picture " +
                "starts you on: inverting just that layer would turn black paint white against a " +
                "white background that hadn't changed, and the picture would look like it had gone " +
                "blank instead of inverted.");

            Add("Transparency",
                "Paint supports a true transparent color, shown as a checkered pattern wherever it " +
                "appears - look for the checkered swatch next to the color palette, or the " +
                "Transparent button in Edit Colors.\n\n" +
                "Set the foreground or background color to Transparent the same way as any other " +
                "color (left-click for foreground, right-click for background), then use it like " +
                "normal: erase to transparent instead of a solid color, fill an area with transparency " +
                "to punch a hole in it, or draw shapes with a transparent outline or fill. Save as PNG " +
                "to keep the transparency in the saved file - the other formats don't support it.");

            Add("Keyboard shortcuts",
                "File and editing:\n" +
                "Ctrl+N   New\nCtrl+O   Open\nCtrl+S   Save\nCtrl+P   Print\n" +
                "Ctrl+Z   Undo\nCtrl+Y   Repeat (Redo)\n" +
                "Ctrl+X   Cut\nCtrl+C   Copy\nCtrl+V   Paste\n" +
                "Ctrl+A   Select All\nCtrl+D   Deselect\nDelete   Clear Selection\n" +
                "Ctrl+E   Attributes\nCtrl+I   Invert Colors\n" +
                "X        Swap foreground and background colors\n" +
                "Esc      Cancel the current action\n\n" +
                "View:\n" +
                "Ctrl++ / Ctrl+-   Zoom in / out\nCtrl+0   Reset zoom to 100%\nF11   Full screen\n\n" +
                "Brush, pencil, and eraser size:\n" +
                "[   Decrease size\n]   Increase size\n\n" +
                "Tools (press the letter with no other key held):\n" +
                "P Pencil   B Brush   A Airbrush   E Eraser   G Fill\n" +
                "I Pick Color   Z Magnifier   T Text\n" +
                "L Line   C Curve   R Rectangle   O Ellipse   U Rounded Rectangle\n" +
                "S Select   F Free-Form Select   W Magic Wand   Y Polygon\n\n" +
                "These tool letters and the size brackets are inactive while typing text or " +
                "navigating the menu, so they never interfere with normal typing.");
        }

        private void BuildTree()
        {
            void Category(string name, params int[] topicIndexes)
            {
                var node = new TreeViewItem { Header = name, FontWeight = FontWeights.Bold };
                foreach (var i in topicIndexes)
                {
                    var leaf = new TreeViewItem { Header = _topics[i].Title, FontWeight = FontWeights.Normal };
                    _nodeToTopic[leaf] = i;
                    node.Items.Add(leaf);
                }
                node.IsExpanded = true;
                ContentsTree.Items.Add(node);
            }

            Category("Introducing Paint", 0, 1);
            Category("Basic drawing", 2, 3, 4, 5);
            Category("Working with color", 6, 7, 21);
            Category("Selections", 8, 9, 10);
            Category("Text", 11);
            Category("Viewing your picture", 12, 13);
            Category("Changing picture size", 14, 15);
            Category("Layers", 20);
            Category("Saving, opening, and printing", 16, 17, 18);
            Category("Reference", 19, 22);
        }

        private void ShowTopic(int index)
        {
            if (index < 0 || index >= _topics.Count) return;

            TopicTitleText.Text = _topics[index].Title;
            TopicContent.Text = _topics[index].Body;

            _suppressSelection = true;
            foreach (var kv in _nodeToTopic)
            {
                if (kv.Value == index) kv.Key.IsSelected = true;
            }
            _suppressSelection = false;
        }

        private void ContentsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_suppressSelection) return;
            if (e.NewValue is TreeViewItem item && _nodeToTopic.TryGetValue(item, out var index))
                ShowTopic(index);
        }
    }
}
