# Sample Plugins

Three tiny example plugins (Grayscale, Sepia Tone, Brightness Boost) showing the shape every
Paint plugin follows. This project deliberately does **not** reference `ShellProject.csproj` -
that's the point of the plugin API: any public class with a public parameterless constructor and
a public `void Apply(WriteableBitmap bitmap)` method gets picked up automatically, no shared
interface assembly required. Paint's `PluginManager` finds it via reflection at runtime.

## Writing your own plugin

```csharp
using System.Windows;
using System.Windows.Media.Imaging;

public class MyPlugin
{
    public string Name => "My Plugin";              // shown in the Plugins menu
    public string Description => "What it does.";    // shown as a tooltip (optional)

    public void Apply(WriteableBitmap bitmap)
    {
        int w = bitmap.PixelWidth, h = bitmap.PixelHeight;
        int stride = bitmap.BackBufferStride;
        var pixels = new byte[stride * h];
        bitmap.CopyPixels(pixels, stride, 0);

        // Pbgra32 byte order per pixel: Blue, Green, Red, Alpha.
        for (int i = 0; i < pixels.Length; i += 4)
        {
            // ... modify pixels[i] (B), pixels[i+1] (G), pixels[i+2] (R), pixels[i+3] (A) ...
        }

        bitmap.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
    }
}
```

Your project just needs `<UseWPF>true</UseWPF>` (for `WriteableBitmap`) - see
`SamplePlugins.csproj` for the minimal setup. `Apply` receives the whole document flattened to
one bitmap (whichever layer was active), and Paint pushes an undo step before calling it, so a
plugin never needs to worry about undo/redo itself.

## Building and installing this sample

```
cd SamplePlugins
dotnet build
```

Then copy `bin\Debug\net10.0-windows\SamplePlugins.dll` into Paint's `Plugins` folder (Paint
creates this folder next to its own executable on first run; Plugins > Open Plugins Folder... in
the app takes you straight there). Use Plugins > Reload Plugins in Paint, or just restart it, and
the three example plugins will appear in the Plugins menu.

## A plugin that throws

If `Apply` throws an exception, Paint shows an error dialog naming the plugin and cleanly rolls
back whatever it had partially done, so a broken plugin can't leave a stray or corrupted entry in
your undo history, and the broken in-between state isn't reachable via Redo either.

## One thing to know about Reload Plugins

Paint uses `Assembly.LoadFrom` to load plugin DLLs, which is simple and reliable but has one
real .NET limitation: once a DLL at a given path has been loaded, the runtime keeps using that
same loaded copy - it won't notice if you rebuild the DLL at the same path and click Reload
Plugins again. Reload Plugins *will* pick up DLLs you've newly added since the last load; it just
won't pick up changes to a DLL it already loaded. If you're actively iterating on a plugin,
restart Paint to pick up your rebuild. (A fully hot-reloadable plugin system is possible with a
collectible `AssemblyLoadContext`, but adds enough complexity and edge cases that it wasn't worth
the risk for this project without a way to test it end-to-end.)
