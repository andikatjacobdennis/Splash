using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PaintClone.Models;

namespace PaintClone.Services
{
    /// <summary>
    /// Reads/writes the app's own project format (.splash) - a plain zip archive containing one
    /// PNG per raster layer, plus a document.json describing the document (size, DPI, layer order/
    /// visibility) and, for a text layer, its live TextLayerData directly rather than a PNG. This is
    /// the one file format that round-trips a document exactly as it was being worked on - every
    /// other Save/Save As format (BMP/PNG/JPEG/...) flattens the whole thing, rasterizing text
    /// permanently, which is the right behavior for "export a picture to share" but throws away
    /// exactly the editability this format exists to keep.
    /// </summary>
    public static class SplashProjectFile
    {
        private const int FormatVersion = 1;

        private class DocumentModel
        {
            public int Version { get; set; } = FormatVersion;
            public int Width { get; set; }
            public int Height { get; set; }
            public double DpiX { get; set; } = 96;
            public double DpiY { get; set; } = 96;
            public int ActiveLayerIndex { get; set; }
            public List<LayerModel> Layers { get; set; } = new();
        }

        private class LayerModel
        {
            public string Name { get; set; }
            public bool Visible { get; set; } = true;

            /// <summary>"raster" or "text".</summary>
            public string Kind { get; set; }

            /// <summary>Raster layers only: the zip entry holding this layer's pixels.</summary>
            public string PixelsFile { get; set; }

            // Text layers only - see Models/TextLayerData, which this mirrors field-for-field.
            public string Content { get; set; }
            public string FontFamily { get; set; }
            public double FontSize { get; set; }
            public bool Bold { get; set; }
            public bool Italic { get; set; }
            public bool Underline { get; set; }
            public string Color { get; set; } // "#AARRGGBB"
            public bool AntiAlias { get; set; }
            public bool AutoWidth { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double TextWidth { get; set; }
            public double TextHeight { get; set; }
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        public static void Save(PaintDocument doc, string path)
        {
            // Write to a temp file and swap it in only on success - an interrupted/failed write
            // (disk full, crash, permissions) must never leave a truncated, unopenable zip sitting
            // at the destination the user already had a project saved to.
            string tempPath = path + ".tmp";
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var model = new DocumentModel
                {
                    Width = doc.Width,
                    Height = doc.Height,
                    DpiX = doc.DpiX,
                    DpiY = doc.DpiY,
                    ActiveLayerIndex = doc.ActiveLayerIndex
                };

                for (int i = 0; i < doc.Layers.Count; i++)
                {
                    var layer = doc.Layers[i];
                    if (layer.Text != null)
                    {
                        var t = layer.Text;
                        model.Layers.Add(new LayerModel
                        {
                            Name = layer.Name,
                            Visible = layer.Visible,
                            Kind = "text",
                            Content = t.Content,
                            FontFamily = t.FontFamily,
                            FontSize = t.FontSize,
                            Bold = t.Bold,
                            Italic = t.Italic,
                            Underline = t.Underline,
                            Color = ColorToHex(t.Color),
                            AntiAlias = t.AntiAlias,
                            AutoWidth = t.AutoWidth,
                            X = t.X,
                            Y = t.Y,
                            TextWidth = t.Width,
                            TextHeight = t.Height
                        });
                    }
                    else
                    {
                        string entryName = $"layers/{i}.png";
                        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                        // BitmapEncoder.Save requires a seekable stream internally and throws
                        // NotSupportedException on a ZipArchiveEntry's own stream (deflate-backed,
                        // CanSeek == false) - encode to a MemoryStream first and copy the bytes in.
                        using (var png = new MemoryStream())
                        {
                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(layer.Surface.Bitmap));
                            encoder.Save(png);
                            png.Position = 0;
                            using var entryStream = entry.Open();
                            png.CopyTo(entryStream);
                        }
                        model.Layers.Add(new LayerModel { Name = layer.Name, Visible = layer.Visible, Kind = "raster", PixelsFile = entryName });
                    }
                }

                var docEntry = zip.CreateEntry("document.json", CompressionLevel.Optimal);
                using (var docStream = docEntry.Open())
                    JsonSerializer.Serialize(docStream, model, JsonOptions);
            }

            if (File.Exists(path)) File.Delete(path);
            File.Move(tempPath, path);
        }

        public static PaintDocument Load(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);

            var docEntry = zip.GetEntry("document.json")
                ?? throw new InvalidDataException("Not a valid Splash project file (missing document.json).");
            DocumentModel model;
            using (var s = docEntry.Open())
                model = JsonSerializer.Deserialize<DocumentModel>(s)
                    ?? throw new InvalidDataException("Not a valid Splash project file.");

            int width = Math.Max(1, model.Width), height = Math.Max(1, model.Height);
            var doc = new PaintDocument(width, height, Colors.Transparent);
            doc.Layers.Clear(); // drop the single "Background" layer the constructor seeds - the real layers come from the file
            doc.DpiX = model.DpiX > 0 ? model.DpiX : 96;
            doc.DpiY = model.DpiY > 0 ? model.DpiY : 96;

            foreach (var lm in model.Layers ?? new List<LayerModel>())
            {
                if (lm.Kind == "text")
                {
                    var t = new TextLayerData
                    {
                        Content = lm.Content ?? "",
                        FontFamily = string.IsNullOrEmpty(lm.FontFamily) ? "Segoe UI" : lm.FontFamily,
                        FontSize = lm.FontSize > 0 ? lm.FontSize : 16,
                        Bold = lm.Bold,
                        Italic = lm.Italic,
                        Underline = lm.Underline,
                        Color = HexToColor(lm.Color) ?? Colors.Black,
                        AntiAlias = lm.AntiAlias,
                        AutoWidth = lm.AutoWidth,
                        X = lm.X,
                        Y = lm.Y,
                        Width = Math.Max(1, lm.TextWidth),
                        Height = Math.Max(1, lm.TextHeight)
                    };
                    var surface = new RasterSurface(width, height, Colors.Transparent);
                    TextLayerRenderer.Render(surface, t);
                    doc.Layers.Add(new PaintLayer { Surface = surface, Name = lm.Name ?? "Text", Visible = lm.Visible, Text = t });
                }
                else
                {
                    var pixelEntry = !string.IsNullOrEmpty(lm.PixelsFile) ? zip.GetEntry(lm.PixelsFile) : null;
                    RasterSurface surface;
                    if (pixelEntry != null)
                    {
                        using var entryStream = pixelEntry.Open();
                        using var ms = new MemoryStream();
                        entryStream.CopyTo(ms);
                        ms.Position = 0;
                        var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                        var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Pbgra32, null, 0);
                        surface = new RasterSurface(new WriteableBitmap(converted));
                    }
                    else
                    {
                        // A raster layer whose pixel entry is missing/unreadable degrades to a
                        // blank layer rather than aborting the whole load - the rest of the
                        // project (every other layer) is still worth recovering.
                        surface = new RasterSurface(width, height, Colors.Transparent);
                    }
                    doc.Layers.Add(new PaintLayer { Surface = surface, Name = lm.Name ?? "Layer", Visible = lm.Visible });
                }
            }

            if (doc.Layers.Count == 0)
                doc.Layers.Add(new PaintLayer { Surface = new RasterSurface(width, height, Colors.White), Name = "Background" });

            doc.ActiveLayerIndex = Math.Max(0, Math.Min(model.ActiveLayerIndex, doc.Layers.Count - 1));
            doc.FilePath = path;
            doc.IsDirty = false;
            doc.LastSaveFilterIndex = 0; // meaningless for a project file - see MainWindow.IsProjectFile
            return doc;
        }

        private static string ColorToHex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

        private static Color? HexToColor(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return null; }
        }
    }
}
