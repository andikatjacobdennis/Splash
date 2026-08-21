# Splash — a classic early-2000s style Paint program (WPF)

An independent, unofficial project recreating the look and feel of classic early-2000s
paint programs, built in C# / WPF / .NET 10. Not affiliated with or endorsed by any
software vendor.

The solution is named **Splash** (after the app's mascot); the main app project inside
it is named **ShellProject**. Note: the C# code itself still uses `namespace PaintClone`
throughout (and many class names like `PaintDocument`, `PaintCanvas` reflect that
original name) - only the solution file, the project file, and the build output's
name changed. Renaming 40+ files' internal namespace declarations without a compiler
available to verify every reference would have been a much riskier change for little
practical benefit, since a project's file name and its internal namespace are
independent in .NET and don't need to match.
## Status

This is a **real, from-scratch implementation** — a `WriteableBitmap`-backed raster
engine, working menus, a real toolbox, undo/redo, file I/O, and 17 functioning tools —
not a mockup or a themed skin over a modern drawing library.

### Fixes since the first build

- **Tools not responding on the canvas**: the transparent overlay `Canvas` used for
  in-place text editing (`TextOverlayLayer`) was hit-test-visible by default, so it
  sat on top of the drawing canvas and silently swallowed every mouse click before
  the canvas ever saw them. It's now only hit-test-visible while a text box is
  actually being edited (toggled in `BeginTextEditing` / `CommitActiveTextBox`).
- **No way to resize the canvas by dragging**: classic Paint's primary resize
  interaction — small square handles on the right edge, bottom edge, and
  bottom-right corner of the canvas that you drag to grow/shrink it — was missing
  entirely; only the Image → Attributes dialog worked. Added real drag handles
  (`AddResizeHandles` / `ResizeDrag` in `MainWindow.xaml.cs`), anchored top-left like
  the original, with a single undo entry for the whole drag.
- **Garbled toolbox icons**: several tool glyphs used emoji/symbol characters (🔍,
  🖌, ❋, ⌗, ⬠) that the classic Tahoma UI font has no glyphs for, so Windows was
  substituting fallback "tofu" boxes. Replaced with plain 3-letter ASCII labels
  (PEN, BRU, FIL, etc.) that render correctly on any system font.

### Second round of fixes

- **Performance**: found two real bugs, both variants of the same mistake -
  `RasterSurface.Blit()` and `SelectionManager.Commit()` were calling `GetPixel()`
  per source pixel, and `GetPixel()` locks/unlocks the entire `WriteableBitmap` on
  every call when the surface isn't already locked. Locking/unlocking a
  `WriteableBitmap` is not cheap, and `Blit` runs on *every mouse-move* while
  dragging a selection or shape preview, so this was the primary cause of lag.
  Both now lock their source surface once before the loop and read raw pixels
  directly. Also fixed `RasterSurface.Clear()`, which did a per-pixel managed loop
  even for the extremely common "clear to transparent" case (called every
  mouse-move to reset the tool-preview layer) - it now does a raw bulk memory clear
  for transparent, and a row-copy (`Buffer.MemoryCopy`) instead of per-pixel writes
  for solid colors.
- **Canvas didn't stay anchored top-left when resized**: `CanvasHost` was
  horizontally/vertically *centered* in its scroll area, so growing the canvas
  re-centered it and visually shifted the top-left corner even though the
  underlying bitmap data was correctly anchored top-left. Changed to
  top/left-alignment so only the right and bottom edges move, matching classic
  Paint and the explicit requirement that only right/bottom/corner drags should
  change the size.
- **Real tool icons**: extracted all 16 tool icons from the supplied `tools.svg`
  sprite sheet (`Resources/Icons/*.png`, bundled as WPF `Resource` items) and wired
  them into the toolbox in place of the placeholder text labels.
- **Magnifier and Selection tools showed no options**: `BuildToolOptions` never had
  a case for them. Magnifier now shows the classic zoom-level buttons (1x/2x/4x/
  6x/8x); Select and Free-Form Select now show Opaque/Transparent buttons wired to
  the same `SelectionManager.DrawOpaque` flag as the Image → Draw Opaque menu item
  (kept in sync both ways).
- **Options box was clipping content**: the panel had a hardcoded `Height="70"`
  fighting against its parent `DockPanel`'s fill behavior. Removed the fixed height
  (it now fills available space naturally) and wrapped it in a `ScrollViewer` as a
  safety net for very small windows.
- **Edit Colors threw a `NullReferenceException`**: `Channel_Changed` (wired to all
  three RGB `TextBox`es' `TextChanged` event) fires *during* `InitializeComponent()`
  as soon as the first text box's literal `Text="0"` is set in XAML - before the
  *other* named fields on the class have been wired up yet. The suppress-flag
  defaulted to `false`, so that first firing read `GBox`/`BBox` while they were
  still `null`. Fixed by defaulting the flag to `true` (plus an explicit null-check
  as a second line of defense).
- **Show Grid did nothing**: implemented for real via an efficient tiled
  `DrawingBrush` (one repeating 1-cell pattern, not thousands of `Line` elements),
  active at 400% zoom and above like classic Paint.
- **Text box couldn't be resized**: added a draggable corner handle (same `Thumb`
  pattern as the canvas resize handles) so you can resize the box after drawing it,
  and added a proper font-size selector to the Text tool's options box (previously
  only Bold/Italic/Underline existed, with no way to change size at all).
- **Print Preview / Page Setup did nothing**: WPF doesn't ship its own Page Setup or
  Print Preview dialogs, so these now go through WinForms interop
  (`<UseWindowsForms>true</UseWindowsForms>`) using the real
  `System.Windows.Forms.PageSetupDialog` and `PrintPreviewDialog`, both backed by a
  shared `System.Drawing.Printing.PrintDocument` so Page Setup choices (paper size,
  orientation, margins) actually carry through to Print Preview and Print.
- **Help Topics wasn't really there**: replaced the one-line message box with an
  actual `HelpTopicsDialog` (two-pane topic list + content, matching the other XP
  dialogs) covering tools, shapes, selection, text, zoom, canvas resizing, and
  undo/clipboard.

### Third round of fixes

- **Toolbox icons were tiny**: buttons and icons enlarged to a more standard,
  comfortable size (26×26 icon in a 36×34 button, up from 18×18 in 26×24), toolbox
  widened to fit.
- **Custom colors didn't survive restarting the app**: they were held in an
  in-memory-only static list. Added `Services/CustomColorStore.cs`, which persists
  them to a small text file under `%AppData%\PaintClone\customcolors.txt` and loads
  them back on the next run.
- **Help Topics rewritten to look like the genuine Windows Help viewer**: a toolbar
  (Help Topics / Back / Print buttons), a book-and-page `TreeView` "Contents" pane
  grouped into categories, working Back navigation, and substantially expanded,
  more authentic topic content (20 topics across 9 categories) instead of the
  flat list from the previous round.
- **More Save As formats, matching classic Paint's actual list**: added the four
  BMP color-depth variants classic Paint offered - Monochrome, 16 Color, 256 Color,
  and 24-bit Bitmap (via `FormatConvertedBitmap` + a computed `BitmapPalette`) -
  alongside the existing JPEG/GIF/TIFF/PNG. The chosen variant is remembered on the
  document so a plain Ctrl+S re-save uses the same one instead of silently
  reverting to 24-bit.
- **About dialog redesigned**: icon, proper type hierarchy (title/subtitle/
  separator/detail rows), and actual version/runtime info pulled at runtime
  instead of a plain three-line text block.
- **Grid still didn't work**: the previous fix added rendering logic but gated it
  behind `zoom >= 400%` with only a status-bar message explaining why - easy to
  miss entirely if you toggled it on at the default 100% zoom. Removed the gate;
  the grid now renders immediately at whatever zoom you're at.
- **Canvas had too much top-left margin**: reduced from 16px to 6px.

### Fourth round of fixes

- **Canvas jumped when dragging near the scrollbar threshold**: root cause was WPF's `Thumb`
  control, which computes `DragDelta` relative to the *Thumb's own* screen position - and our
  resize handles were repositioned via `HorizontalAlignment`/`Margin` on every single resize
  tick (they have to be, since they sit on the edge of what's being resized). The moment a
  handle's own layout position shifted under an active mouse capture - which also happens as a
  side effect whenever the ScrollViewer's scrollbar visibility toggles mid-drag - Thumb's math
  produced a one-frame spurious jump. Replaced with manual mouse-capture dragging that measures
  movement relative to the stable top-level `Window` instead, which is immune to any of that.
  Applied the same fix to the text-box resize handle for the same underlying reason.
- **More brush shapes**: added the two diagonal "calligraphy nib" shapes classic Paint's Brush
  tool actually offered (`StampDiagonalRight`/`StampDiagonalLeft` in `RasterSurface`), plus a
  splatter/star shape, alongside the existing round and square.
- **Text tool hid the picture underneath**: the live-editing `TextBox` had an opaque white
  `Background`, fully covering whatever was drawn in that area while typing (the underlying
  bitmap was untouched - it just looked erased). Now transparent, matching classic Paint's
  see-through text box.
- **Curve tool didn't work as expected**: found a real state-machine bug - advancing from the
  first-bend stage happened on `MouseDown`, while committing the second bend happened on
  `MouseUp`, so the down+up of the *same click* that confirmed the first bend also immediately
  fired the second-bend commit, collapsing the tool's two-click bend interaction into one and
  skipping the second adjustment entirely. Every stage transition now happens uniformly on
  `MouseUp`, with `MouseMove` driving the hover-preview in between - matching the real two-click
  interaction.
- **Rubber-banding gap between the shape endpoint and the mouse cursor on longer drags**:
  couldn't fully isolate a single root cause without a live debugger, but made two concrete,
  defensible fixes that remove the most likely sources of drift - `PaintCanvas` now computes the
  screen-to-document scale from the actual rendered image size rather than the separately
  tracked `_zoom` field (so it can never end up out of sync with what's really on screen), and
  the document/preview `Image` controls now use explicit `Stretch="Fill"` instead of relying on
  the default `Uniform` mode's aspect-ratio-preserving logic.
- **Option-box selections didn't visually appear selected**: the Size, Brush Shape, and Fill
  Mode buttons were plain, stateless `Button`s with no selected-appearance concept at all; Zoom
  and Opaque/Transparent used `ToggleButton` but with the default system checked-state chrome,
  which didn't contrast enough against the custom XP color scheme to read as "selected." All of
  these now go through one shared `AddExclusiveToggleRow` helper using a new `OptionToggleStyle`
  with an unambiguous recessed/highlighted checked state.
- **Selecting Opaque/Transparent had no visible effect until the selection was dropped**: the
  *live drag preview* (`SelectRectTool.RenderFloating`) always blitted the floating selection
  fully opaque, regardless of the setting - only the final `Commit()` actually applied the
  transparency-key logic. The preview now respects `DrawOpaque` the same way Commit already did.
- **More Edit Colors features**: added a genuine HSL spectrum picker - a saturation/luminosity
  square at the current hue plus a separate hue strip, both click-and-drag - alongside the
  existing RGB fields, now joined by matching H/S/L numeric fields and a hex code field, all kept
  in sync bidirectionally. Also added a current-vs-new color comparison swatch (previously only
  the new color was shown).

### Fifth round: first real compile error, from an actual Visual Studio build

- **CS1061: `'UIElement' does not contain a definition for 'Cursor'`**: the drag-handle fields
  and `WireManualDrag`'s parameter were typed as `UIElement`, but `Cursor` is defined on
  `FrameworkElement` (one level down the hierarchy), not the more general `UIElement`. Changed
  `_textResizeHandle`, `_handleRight`/`_handleBottom`/`_handleCorner`,
  `_activeManualDragHandle`, and `WireManualDrag`'s `handle` parameter from `UIElement` to
  `FrameworkElement` (which `Border`, what these actually are, satisfies). This is the first
  fix in this project driven by an actual compiler diagnostic rather than manual code review -
  much more reliable, and a good sign the rest of the codebase is close if this was the first
  error surfaced.

### Sixth round of fixes

- **Target framework**: now `net10.0-windows` (was `net8.0-windows`). Requires the .NET 10 SDK.
- **Curve tool still misbehaved with multiple curves**: found the actual bug this time. The
  preview during the *initial line drag* of a new curve was rendering the full cubic Bezier
  using `_c1`/`_c2` - but those still held the **previous curve's final bend positions** (or
  `(0,0)` for the very first curve) until `OnMouseUp` reset them. So every curve after the first
  would preview visibly bent from the moment you started dragging its base line, bending toward
  wherever the last curve happened to end. Fixed by rendering a plain straight line during the
  initial drag (no control points exist yet at that stage) and resetting `_c1`/`_c2` the instant
  a new curve starts.
- **Canvas resize wasn't buttery smooth**: root cause was straightforward once traced - every
  single mouse-move tick during a resize drag was calling `PaintDocument.Resize`, which does a
  full O(width×height) copy-and-reallocate. For a reasonably large canvas, doing that dozens of
  times per second is exactly what "not smooth" feels like. Changed to the standard approach:
  a cheap dashed-outline rectangle (`CanvasResizePreview`) tracks the drag live (plus the handle
  squares, which is now safe to reposition mid-drag since dragging is Window-relative rather
  than handle-relative - see the earlier "jump" fix), and the actual expensive bitmap resize
  happens exactly once, when you release the mouse.
- **More Edit Colors features**: added a "Recently used" color row (separate from the explicit
  Custom colors tray, auto-populated whenever you click OK, persisted to disk), right-click to
  remove a color from either the Custom or Recently-used trays, a row of tint/shade variations
  of the current color for quick picking, and Grayscale/Invert quick-action buttons.

### Seventh round: second real compile error

- **CS0103: `The name 'WireManualDrag' does not exist in the current context'`**: my own fault -
  the previous round's resize-smoothness fix was applied via a scripted line-range replacement
  (to work around a `str_replace` matching issue), and that script's replacement block omitted
  the `WireManualDrag` method itself while reconstructing the surrounding code, silently deleting
  it. Restored it. Also re-verified there were no other casualties from that same edit (checked
  for duplicate method definitions and confirmed every symbol used in the resize-handle section
  resolves to exactly one definition) - clean.

### Eighth round of fixes

- **Rubber-banded shape/selection sometimes didn't match what was previewed**: a real, systemic
  bug found in three places - `DragShapeToolBase` (Line/Rectangle/Ellipse/RoundedRectangle),
  `SelectRectTool`'s marquee, and `CurveTool` - all independently recomputed the shape's endpoint
  from *each event's own coordinates* at both preview time (`OnMouseMove`) and commit time
  (`OnMouseUp`). Those two events don't always land at exactly the same pixel - a physical mouse
  button release rarely happens at precisely the same coordinate as the last move sample - so the
  final committed shape could occasionally differ slightly from whatever was last shown on
  screen. Fixed by caching the last point actually used for the preview and reusing it verbatim
  at commit time in all three tools, so the result is now guaranteed identical to the last frame
  you saw before releasing. (Checked `FreeFormSelectTool` too - it doesn't have this bug, since
  its `OnMouseUp` only finalizes the already-accumulated trace rather than sampling a new point.)

### Ninth round of fixes

- **Removed Help Topics/Back/Print buttons** from the Help dialog's toolbar per request - the
  Contents tree + content pane remain, now with a bit more room since the toolbar row is gone.
  Cleaned up the now-unused back-navigation stack in the code-behind along with it.
- **Material Design colors added** to Edit Colors: a new `Services/MaterialColors.cs` with the
  standard 19 Material Design hue families and their full 50-900 tonal ranges (the well-known,
  publicly published values from the Material color spec). Selecting a family shows its authentic
  tonal row - distinct from the existing HSL-interpolated Shades row, since Material's tone steps
  aren't simple lightness variations of one hue/saturation, each step has its own hand-tuned
  hue/saturation too.
- **Even more Edit Colors features**: the dialog's left side is now a tabbed Basic/Material/Custom
  layout (previously everything was stacked in one column, which was getting cramped). Added
  Copy-to-clipboard for the hex code, a Complementary-color button (opposite hue, same
  saturation/lightness - distinct from Invert, which is a straight RGB negative), and a Random
  color button.

### Tenth round of fixes

- **Real tool-shaped cursors**: pencil, brush, eraser, fill, eyedropper, and airbrush now show an
  actual cursor shaped like the tool (built from the same extracted icon set as the toolbox)
  instead of the plain arrow. Worth noting: ImageMagick's `.cur` writer turned out to have a real
  bug - it writes `type=1` (ICO) instead of `type=2` (CUR) in the file header regardless of the
  output extension, which a strict cursor loader would reject. Built the `.cur` files by hand in
  Python instead (the format is simple and well-documented), with a sensible hotspot per tool
  (the pencil tip, the eyedropper tip, the eraser's bottom face, etc.). Tools without a dedicated
  cursor asset get a sensible built-in fallback (crosshair for precision drawing tools, I-beam for
  text) rather than the plain arrow.
- **Found the actual cause of the resize-centering bug**: `_canvas` had an explicit fixed
  `Width`/`Height` (needed so it renders at the right zoom level) but no explicit
  `HorizontalAlignment`/`VerticalAlignment`, leaving it at the WPF default of `Stretch`. This is a
  well-known WPF trap: an element with a *fixed* size but `Stretch` alignment gets **centered**
  within a larger parent cell, since it can't actually stretch to fill it. During an active resize
  drag, `CanvasStack` grows to fit the (correctly top-left-anchored) rubber-band preview rectangle
  while `_canvas` itself briefly stays at its old size - so it visibly drifted toward center,
  exactly matching "especially while dragging." Fixed with two lines pinning it to `Left`/`Top`.
- **Edit Colors and Paint Help are now tool windows** (`WindowStyle="ToolWindow"`) - the smaller
  title bar without minimize/maximize buttons that's conventional for utility dialogs like these.
- **Eraser boundary ("rubber boundary") now visible**: added a live square outline that tracks the
  cursor and matches the eraser's actual footprint size (the same formula `EraserTool.Deposit`
  uses), shown only while the Eraser tool is active - so you can see what you're about to erase,
  not just the result after.
- **Two more brush shapes**: Cross/plus and a Soft brush with a fuzzy, probabilistic-falloff edge
  (pixels near the center always deposit, pixels near the edge deposit with decreasing
  probability) rather than every shape so far having a hard edge.
- **History window** (View → History Window): a Photoshop-style panel listing every step in the
  undo/redo timeline by name ("Pencil", "Fill", "Resize Canvas", "Rotate 90°", ...), with the
  current step highlighted and click-to-jump support. Implemented as a targeted extension of the
  existing `HistoryManager` rather than a rewrite - each snapshot now carries a label, and
  `JumpTo(index)` reuses the exact same single-step Undo/Redo logic repeated the right number of
  times, so the well-tested restore mechanics didn't need to change at all. Non-modal, so it stays
  open and live-updating while you keep drawing. Every `PushUndoState` call site across the tools
  and the Image/File menu operations now passes a real label instead of the generic default.

### Eleventh round of fixes and features

**Real bugs fixed:**
- **Underline in text didn't apply until you clicked away**: the Bold/Italic toggle handlers
  updated the live `TextBox` immediately; Underline only set a flag and never touched the box -
  it was only ever applied at commit time via `FormattedText`, which is why it "worked after
  leaving." Fixed to match the Bold/Italic pattern, plus applied on initial box creation too.
- **Pencil size did nothing**: `PencilTool.Deposit` always drew exactly one pixel, completely
  ignoring `ctx.PenSize`, even though the toolbox showed a size selector for it. Now respects
  size (still a true single pixel at size 1, matching classic Paint's default).
- **Keyboard menu navigation broken**: the global `PreviewKeyDown` handler unconditionally
  intercepted Escape (to cancel the active tool) during the tunneling phase, before the `Menu`
  control's own bubbling handling ever saw it - so pressing Escape to close an open menu did
  nothing. Fixed by stepping aside entirely (`if (MainMenu.IsKeyboardFocusWithin) return;`)
  whenever focus is within the menu, letting its native arrow/Enter/Escape/mnemonic handling work
  untouched.
- **Grid visibility threshold**: restored the 400%-and-above zoom requirement (removed in an
  earlier round while chasing a *different*, unrelated rendering bug - that removal was the wrong
  fix; the threshold itself was correct). Now gives clear status-bar feedback if toggled on below
  that zoom instead of just doing nothing.

**New features:**
- **Font family selector** for the Text tool (curated common-fonts list), live-updating text
  already typed, alongside the existing size/bold/italic/underline controls.
- **Hover color info**: added `ColorManager.DescribeColor()` (hex/RGB/HSL) as a tooltip on every
  color swatch across the app - main palette, Material, Flat UI, custom, recent, and shades.
- **Transparency as a real color**: added a checkerboard-pattern "Transparent" swatch (the
  standard convention) next to the color indicator and in Edit Colors. The raster engine already
  handled per-pixel alpha correctly throughout - `RasterSurface.SetPixel` never clamped alpha -
  so this was mostly a matter of exposing it in the UI. Eraser, Fill, and Clear Image all
  naturally support "erase/fill to transparent" once you set a color to Transparent; save as PNG
  to keep it (the other formats don't support alpha).
- **A second professional palette**: added `Services/FlatUIColors.cs` (the well-known 20-color
  Flat UI palette) as another tab alongside Material Design.
- **Magic Wand tool**: click-to-select-by-color, built by adapting `RasterSurface.FloodFill`'s
  exact scanline algorithm (`MagicWandSelect`) to build a boolean mask instead of writing pixels,
  then feeding that mask into the same `SelectionManager` infrastructure Free-Form Select already
  uses - so it gets move/cut/copy/paste/Opaque-Transparent support for free. 17th toolbox tool
  (`W` shortcut); no source icon was available for it, so it uses a text fallback ("MW").
- **More keyboard shortcuts**: `[`/`]` to decrease/increase brush-pencil-eraser size, Ctrl+D to
  deselect, Ctrl+Plus/Minus/0 for zoom, and Photoshop-style single-letter tool shortcuts (P/B/A/
  E/G/I/Z/T/L/C/R/O/U/S/F/W/Y) - all inactive while typing or navigating the menu so they never
  interfere with normal use.
- **Layers**: the biggest addition this round. `PaintDocument` now holds a `List<PaintLayer>`
  instead of a single surface; `Document.Surface` became a passthrough property pointing at
  whichever layer is active, which is the key design choice that kept this low-risk - every tool
  and the entire undo/redo history already worked purely through `ctx.Document.Surface` and
  needed **zero changes**. For display, `PaintCanvas` now stacks one real WPF `Image` per layer
  (bottom to top) instead of a single image, so WPF's own layered alpha rendering does the visual
  compositing for free (GPU-accelerated, zero extra per-pixel CPU work) rather than any manual
  recompositing that would have had to run on every brush-stroke tick. A flattened single bitmap
  is only ever computed on demand (`PaintDocument.GetFlattenedBitmap()`), for Save/Print/Copy.
  New non-modal Layers window (View → Layers Window) with add/delete/reorder/merge-down and
  per-layer visibility toggles. (Two known limitations from this round were later closed - see
  "the layer/undo known issue, actually fixed this round" and "fixed this round" further down.)
  - Worth knowing: layer compositing is a hard per-pixel overwrite, not true alpha blending
    (consistent with how drawing works everywhere else in this engine - nothing in the app blends
    colors). Semi-transparent pixels, like from the Soft brush, won't blend with the layer below
    when flattened; they'll just overwrite it. Fully opaque and fully transparent pixels (the vast
    majority of real usage) are unaffected by this.
- **Updated and expanded Help articles**: rewrote the Keyboard Shortcuts topic to cover every
  shortcut above, added dedicated "The Magic Wand," "Working with layers," and "Transparency"
  topics, added a "Layers" category to the Contents tree, and updated the Edit Colors and Add
  Text topics to mention the new palettes/hover-tooltips/font-family features. 23 topics total,
  each Contents-tree category index verified programmatically to cover every topic exactly once.

### Twelfth round of fixes and features

**The layer/undo known issue, actually fixed this round:** `HistoryManager.PushUndoState` now
takes the whole `PaintDocument` instead of a bare `RasterSurface`, so every snapshot can record
which layer it belongs to. `Undo`/`Redo`/`JumpTo` switch the active layer to match before
restoring pixels, so editing layer A, switching to layer B, switching back to A, and pressing
Undo now correctly restores layer A - previously it would have applied layer B's snapshot to
whichever layer happened to be active. Updated all 12+ `PushUndoState` call sites across the
tool classes and menu handlers.

**More real bugs, found and fixed:**
- **Likely cause of the cursor-gap complaint**: the custom tool-shaped cursors added two rounds
  ago used hotspot coordinates I estimated by eye, with no way to pixel-verify them against a
  real running app. A wrong hotspot on a precision tool is exactly what "there's a gap between
  where it's drawing and where the cursor is" looks like. Correctness now wins over cosmetics:
  Pencil/Brush/Eraser/Airbrush/Pick Color use `Cursors.Cross`, a WPF built-in with a
  guaranteed-centered hotspot. Fill keeps its custom tool-shaped cursor, since flood fill only
  needs to land anywhere inside the target region, not on one exact pixel.
- **Tool icons had huge transparent padding**: turned out they were 64×107 with the actual
  artwork occupying a small corner of that canvas. Trimmed and re-padded all 16 into clean,
  uniform 64×64 icons.
- **Pencil default size**: changed the default from 2px to a true 1px hairline (matching classic
  Paint's actual pencil default) - re-verified the size-selection logic itself was already
  correct from two rounds ago, but starting at 2px instead of 1px could easily read as "pencil
  size isn't doing anything" if you expected the classic thin default and never touched the size
  selector.

**New features:**
- **Cursor changes to a move cursor** while dragging a selection to reposition it.
- **Arrow-key nudging**: with an active selection, arrow keys move it by 1px (10px with Shift).
- **Ctrl+/Ctrl- reassigned to brush/pencil/eraser size** (as explicitly requested, since both
  can't share one combo with zoom) - zoom moved to Ctrl+Shift+/Ctrl+Shift-.
- **Canvas size presets**: File > New now opens a dialog with ten built-in professional sizes
  (VGA through Full HD, a square social-post size, A4/US Letter at 96 DPI) plus a custom
  width/height field and a "Save as preset" button that persists your own sizes to disk
  (`Services/CanvasSizePresetStore.cs`) for reuse in later sessions.
- **History window: delete an entry** (right-click any non-current step). Reordering was
  deliberately not added - explained in the window itself: each snapshot depends on the exact
  sequence of edits that produced it, so reordering two entries would silently produce a
  corrupted, incoherent picture rather than just "moving a step."
- **Shortcut Manager** (View > Keyboard Shortcuts...): every keyboard shortcut in the app is now
  a rebindable entry in `Services/ShortcutManager.cs` instead of a hardcoded key check -
  `MainWindow_PreviewKeyDown` looks up whatever combo was pressed against the registry and
  dispatches through a small `Dictionary<actionId, Action>` instead. The window lists every
  action with its current binding; Change enters a "press the new combo" capture mode with
  conflict detection (offers to reassign if the combo's already used elsewhere), Clear removes a
  binding, and Reset All to Defaults restores everything. Bindings persist to disk automatically.

**Not yet attempted - flagging honestly rather than leaving unmentioned:**
- **Shape adjustment handles** (interactive stretch/skew/rotate handles on a just-drawn shape,
  before it's committed to the bitmap) was not started this round. It's the largest remaining
  item on the list - doing it properly means restructuring every shape tool's commit flow to
  support a post-draw "still editable" state (similar to how the text box stays live-editable
  before commit), and I judged that too large a change to start without enough of this round's
  budget left to see it through safely.

### Thirteenth round: closing the last known issue

**The layer-structure-undo known issue, fixed:** `HistoryManager` was redesigned so every
snapshot captures *every* layer (pixels, name, visibility) instead of just the active layer's
pixels, via a new `PaintDocument.RestoreLayers()` that swaps in a whole restored layer list at
once. `PushUndoState`'s signature didn't need to change again (still takes the whole
`PaintDocument`, from last round's fix), so no drawing-tool call sites needed touching - only
`LayersWindow`'s four structural buttons (Add/Delete/Move Up/Move Down/Merge Down) now call
`_history.PushUndoState(_document, "...")` before delegating to `PaintDocument`, the same way
every drawing tool already does before it mutates pixels. Add/Delete/Reorder/Merge are now
undoable via Ctrl+Z alongside pixel edits. Left out of undo on purpose: toggling a layer's
visibility checkbox and simply clicking a different layer to make it active - both are view
state, not a content change, matching how switching *tools* was never undoable either.

For a single-layer document (still the common case for most pictures) this costs exactly what
the old single-surface snapshot did - the extra memory/CPU only shows up once a document actually
has more than one layer, and even then it's bounded by however many layers exist, which is
normally a handful.

### Fourteenth round of fixes

- **Rounded Rectangle icon was clipped on the left**: re-examined the original `tools.svg` at a
  wider crop margin and confirmed the source art is a properly-rounded rectangle on all four
  corners - my crop inset from a few rounds back had clipped into the left edge. Re-cropped with
  a correctly-fitted bounding box.
- **Magic Wand now has a real icon** instead of the "MW" text fallback: drew one from scratch
  (wand handle + sparkle, built as plain SVG shapes) to match the visual language of the extracted
  icon set - black outlines, a couple of accent colors - then ran it through the same trim/pad/
  crop pipeline as the rest. `MainWindow.xaml.cs` already referenced `"magic_wand"` as the
  filename from when the tool was first added; it silently fell back to text because the file
  didn't exist yet, so no code changes were needed once the icon itself existed.
- **New documents start transparent**: `ColorManager.Background` now defaults to `Colors.Transparent`
  instead of white, and `NewDocument` creates the initial layer transparent too - consistent with
  how additional layers already worked (`AddLayer` always used `Colors.Transparent`). The canvas
  backdrop (`CanvasHost`) now shows the same checkerboard pattern used for the Transparent color
  swatch instead of solid white, so the document's actual transparent state is visible rather than
  looking deceptively like an opaque white canvas. For consistency, canvas-growth operations
  (resize handles, Attributes, Stretch/Skew's unmapped edge pixels) now fill newly-exposed area
  with the *current* background color instead of a hardcoded white, so they don't introduce
  surprise opaque patches now that transparent is the default.
- **Select All + Delete "making the layer transparent"**: traced this and confirmed it's not a
  separate bug - `SelectionManager.DeleteSelection` already correctly filled the vacated area with
  whatever background color was passed in. Once the background defaults to transparent (this
  round), deleting a full-canvas selection naturally clears the layer to transparent, which is the
  correct, intended behavior for erasing a layer's content - not something that needed a fix
  beyond the background-color default change above.

### Fifteenth round of fixes and a visual overhaul

- **Found the actual "Magic Wand not working" bug**: when Magic Wand was added as the 17th
  toolbox tool a couple of rounds back, the toolbox grid went from 8 rows to 9 - but the window's
  default height was never increased to fit the extra row. Did the math: the 9-row toolbox plus
  its options panel needs at least ~404px, and the total UI chrome (menu, color box, status bar)
  needs another ~98px on top of that - 502px minimum, against a 480px default window. That's a
  real, verified deficit, and it would compress the toolbox enough to make the last row (Magic
  Wand) hard or impossible to click precisely. Increased the default window height to 620 with a
  MinHeight/MinWidth floor so resizing can't reintroduce the same problem.
- **All 17 tool icons redrawn from scratch**: the previous set was extracted from a supplied
  sprite sheet depicting classic Windows-era Paint, which - fairly - risked looking copied from a
  commercial application. Designed an entirely new, cohesive icon language instead: navy outlines,
  a small consistent accent palette (blue/yellow/coral), rounded flat shapes, built directly at
  clean proportions in code (see `gen_icons.py`-style generation) rather than extracted and
  cropped from any external source. The Fill tool's cursor was rebuilt to match the new bucket
  icon too.
- **New mascot**: a friendly paint-bucket character named "Splash," original artwork in the same
  new icon style, featured in a redesigned About dialog with a rotating "fun fact" line (click
  "Another fact!" for a new one) instead of the previous plain version/build text.
- **Removed Microsoft trademark references from user-visible text**: the About dialog's disclaimer
  and the README's title/intro no longer mention Microsoft by name. Two categories of reference
  couldn't be removed because they're mandatory parts of the frameworks themselves, not branding
  choices: the `xmlns="http://schemas.microsoft.com/winfx/..."` URI required in every XAML file
  for WPF's parser to recognize it, and `using Microsoft.Win32;`, which is simply where
  `OpenFileDialog`/`SaveFileDialog` live in .NET - there's no alternative non-Microsoft namespace
  for them. Both are technical requirements, not attempts to reference or affiliate with Microsoft.
- **Opaque/Transparent are now visual swatches, not text**: matching classic Paint's own
  icon-based version of this control - a solid square for Opaque, the same checkerboard pattern
  used everywhere else in the app for Transparent - instead of `ToggleButton`s with plain text
  content.

### Sixteenth round: text bugs, plugin support, and a documented simplification closed

- **Found the real cause of "text creates transparent areas"**: `dc.DrawText` uses WPF's default
  anti-aliased text rendering, which produces semi-transparent edge pixels around every glyph
  (the ClearType/grayscale blend between the text color and whatever's behind it). `Blit`'s
  transparency-skip logic only skips pixels that are *exactly* fully transparent - it doesn't
  blend, it hard-overwrites - so those semi-transparent edge pixels got written directly onto the
  document, punching a visible partial-transparency halo around every letter. Fixed two ways:
  disabled text anti-aliasing (`TextOptions.TextRenderingMode = Aliased`), which also matches the
  hard-edged look used everywhere else in the app, and added `RasterSurface.ThresholdAlpha()` as
  a defense-in-depth pass that snaps every pixel's alpha to strictly 0 or 255 before the text
  bitmap ever reaches `Blit` - so no semi-transparent pixel can reach the document regardless of
  how the text got rasterized.
- **Text box now grows automatically while typing**: previously the box was a fixed size from
  the initial drag (or the last manual resize) and just clipped overflow text. Added
  `AutoGrowTextBox()`, wired to `TextChanged`, which measures the content's actual needed height
  and grows the box to fit - but only grows, never auto-shrinks, so a box you've deliberately
  enlarged via the resize handle isn't fought back down the moment you delete a line.
- **Plugin support**: a genuine, working extensibility system, not just a stub. `PluginManager`
  scans a `Plugins` folder next to the executable and loads any public class with a public
  parameterless constructor and a `void Apply(WriteableBitmap bitmap)` method - via reflection
  and duck typing, deliberately with **no shared interface assembly**, so a plugin can be a
  completely standalone class library referencing nothing but standard WPF assemblies. A new
  Plugins menu lists everything found, with Reload and Open Plugins Folder always available; a
  plugin that throws gets a clear error dialog and its undo-stack entry is automatically rolled
  back so it can't leave a stray no-op step in your history. Shipped as a genuinely separate,
  independently-buildable example project (`SamplePlugins/`, added to the .sln) with three real
  working plugins - Grayscale, Sepia Tone, Brightness Boost - and a README walking through writing
  your own.
- **Closed a documented simplification**: the eyedropper previously stayed selected after
  sampling a color instead of returning to whichever tool was active before, which the README
  had flagged as a known deviation from classic Paint. Implemented properly - `SelectTool` now
  remembers the tool you were on before switching to Pick, and `AfterColorPick` restores it
  automatically after one sample, matching the original's actual one-shot eyedropper behavior.

### Seventeenth round: a deeper pass over the plugin system, plus a real pre-existing bug found

I'd flagged that the plugin system got less scrutiny than usual when it first landed, so I went
back over it properly rather than moving straight on to the next feature:

- **Plugin error rollback had a real gap**: when a plugin threw partway through modifying the
  bitmap, the catch block called `HistoryManager.Undo`, which - correctly for a *normal* undo -
  pushes the state you're leaving onto the redo stack. For an error rollback, that's wrong: it
  meant the broken, half-modified output from the failed plugin was sitting one Ctrl+Y away from
  being restored. Added `HistoryManager.DiscardLastPush`, a clean rollback that restores the
  pre-operation snapshot *without* creating a redo entry for the discarded broken state, and
  switched the plugin error handler to use it.
- **Documented a genuine .NET limitation** rather than letting it surprise someone: `Assembly.
  LoadFrom` caches by file path, so Reload Plugins picks up newly-added DLLs fine but won't
  notice if you rebuild a DLL at a path it's already loaded - the runtime keeps using the cached
  copy until Paint restarts. A fully hot-reloadable plugin system needs a collectible
  `AssemblyLoadContext`, which adds enough complexity and edge cases that it wasn't worth the risk
  without a way to test it end-to-end. Documented in `SamplePlugins/README.md` instead of leaving
  it as a silent trap.
- **A scoped step toward "shapes should have adjustment points"**: implementing true stretch/
  skew/rotate handles on a still-editable shape would mean restructuring every shape tool's
  commit flow to support a post-draw "still editable" state (similar to how the text box works) -
  a large change I still don't have a safe, complete design for. Instead, implemented something
  smaller but genuinely useful with zero new architecture: `DragShapeToolBase` (Line/Rectangle/
  Ellipse/RoundedRectangle) now leaves the just-drawn shape's bounding box selected after
  committing, reusing the existing, already-proven `SelectionManager` entirely. Switch to a
  selection tool and the shape you just drew is immediately draggable or arrow-key-nudgeable,
  without having to re-select its bounds by hand. Polygon and Curve now clear a leftover
  selection from a previous shape when you start a new one, for the same reason described next.
- **Found and fixed a real pre-existing bug while wiring the above up**: `SelectTool`'s comment
  said "switching away from a selection tool finalizes any floating content," but the actual
  condition - `if (_currentToolKey is not (Select or FreeFormSelect or MagicWand))
  FinalizeFloatingSelection();` - finalizes only when the tool you're *leaving* is **not** a
  selection tool. Floating content can only ever exist while a selection tool is active (only
  Select/FreeFormSelect/MagicWand's `OnMouseDown` ever call `Lift`), so this condition was
  backwards from its own comment and essentially never fired in the one scenario it existed for -
  switching from an active selection-drag straight to a drawing tool without properly committing
  the floating content first. Since `FinalizeFloatingSelection` is already a safe no-op when
  nothing is floating, simplified this to call it unconditionally, which is correct in every case.

### Eighteenth round: solution/project rename, and a real "tools stopped working" bug found

- **Renamed the solution to Splash and the project to ShellProject**, as requested - the solution
  file is now `Splash.sln`, the app project's folder and `.csproj` are `ShellProject`, and the
  build output is `ShellProject.exe`. The C# code itself still uses `namespace PaintClone`
  throughout, and class names like `PaintDocument`/`PaintCanvas` are unchanged - a project's file
  name/AssemblyName and its internal namespace are independent in .NET, so renaming the former
  didn't require touching the namespace declaration in every one of 40+ files. Doing that full
  rename too would have been considerably riskier without a compiler available to verify every
  reference got updated correctly, for a change that's purely cosmetic either way.
- **Found the actual cause of "rectangle/ellipse not working" and "some tools stopped working"**:
  `Clipboard.ContainsImage()` - called every time `UpdateEditMenuState` runs, which fires on every
  `SelectionManager.Changed` event - is a well-documented flaky Win32 API that can throw if another
  process holds the clipboard locked at that instant. Last round's shape-tool changes made
  `Changed` fire far more often (every shape tool's `OnMouseDown` now clears a leftover selection
  before starting a new draw), which meant far more opportunities for that exception to fire mid
  operation. If it threw partway through a tool's `OnMouseDown`, the exception would abort the
  handler *before* the tool's "drag in progress" flag ever got set to true - so `OnMouseMove`/
  `OnMouseUp` would just silently no-op for the rest of that drag, exactly matching "I click and
  drag and nothing happens." Fixed by wrapping the clipboard check in a try/catch. Also added a
  broader safety net: `Canvas_MouseDown/Move/Up` now catch any unexpected exception from a tool
  operation, reset that tool's state via `Cancel()` so it can't get permanently stuck, and show a
  status message instead of silently going nowhere - so if something *else* unforeseen goes wrong
  in the future, it fails visibly and recoverably instead of looking like the app just stopped
  responding to that tool.

### Nineteenth round: text editing overhaul, per-tool size, and a real shape-move bug found

- **Curve tool ("spline")**: its `Idle`-stage `OnMouseDown` also calls `Deselect`, the same call
  path that triggered last round's clipboard bug in Rectangle/Ellipse - hardened by that same fix
  (`TryClipboardHasImage`, now also applied to `Paste_Click` for consistency, not just the one
  call site that happened to get hit first).
- **"Press Enter and the text disappears until you manually resize"**: the real cause was a WPF
  layout-timing gap, not a rendering bug. `AutoGrowTextBox` called `TextBox.Measure()` +
  `DesiredSize` synchronously inside the `TextChanged` handler - but `TextChanged` fires as part
  of the text update, before WPF's layout pass has necessarily caught up to the new content, so
  `DesiredSize` could reflect the *previous* text, not the line you just added. Replaced with a
  `FormattedText`-based calculation, which is a pure synchronous measurement with no dependency on
  any pending layout pass - it always reflects `tb.Text` exactly as it is when it runs.
- **Text box couldn't be dragged to reposition**: added a second handle - a small circle at the
  top-left corner, distinct in shape from the existing square resize handle at bottom-right - that
  drags the box (and keeps `_activeTextDocRect` in sync) without disturbing normal text editing,
  since the box's interior still needs ordinary clicks for placing the caret and selecting text.
- **Found a real, separate gap while investigating "whatever's behind the text goes transparent"**:
  `SetZoom` never touched the active text box at all. Zooming while editing text left the box's
  on-screen size/position stale relative to the new zoom - and since later logic derives document
  coordinates from that screen state, a stale mismatch there could plausibly produce a
  wrong-positioned or wrong-sized commit. Added `RepositionActiveTextBoxForZoom`, which uses
  `_activeTextDocRect` (true document-space, unaffected by zoom) as the source of truth to
  recompute the box's on-screen geometry whenever the zoom changes.
- **Size is now genuinely per-tool**: previously every size-adjustable tool (Pencil, Brush,
  Eraser, Airbrush, and every shape tool) shared one `ctx.PenSize` value, so setting Eraser to a
  large size and switching to Pencil left Pencil unexpectedly thick too. `SelectTool` now saves
  the outgoing tool's size and restores the incoming tool's own remembered size (or a sensible
  per-tool default) on every switch - implemented entirely in `MainWindow`, with zero changes
  needed to any tool file, since tools still just read/write `ctx.PenSize` as before and have no
  idea MainWindow is swapping its value underneath them based on whichever tool is active.
- **Found and fixed the real cause of "moving a shape leaves part of it behind"**: shapes with a
  thick outline (`PenSize > 1`) are stroked via `StampSquare`, which centers a `size x size` square
  *on* each boundary point rather than insetting from it - meaning the actual rendered pixels can
  extend up to `PenSize / 2` beyond the shape's mathematically exact bounding box (most visible on
  Ellipse, Line, Polygon, and Curve, whose stroking logic all stamp directly on the boundary,
  unlike Rectangle's border-drawing which insets each thickness layer inward and stays exactly
  within bounds). Since last round's "leave the shape selected after drawing" feature sized the
  selection to the *exact* bounding box, moving a thick-outlined shape would leave a sliver of the
  outline behind wherever the stroke extended past the selected bounds - the selection lifted and
  cleared less than what was actually drawn. Padded the post-draw selection by `PenSize / 2 + 1`
  pixels on every side (clamped to canvas bounds) to cover the shape's full rendered extent.

### Twentieth round: magnifier zoom-to-area, a real text-shift bug, and a layer merge fix

- **Magnifier: drag to zoom to an area**, not just click-to-cycle. Dragging a box now picks the
  largest preset zoom level (from the same fixed set click-cycling already used) that fits the
  whole dragged region within the visible viewport, then scrolls to center it - a small drag
  (3px or less) still falls back to the classic click-to-cycle behavior, so an imprecise click
  doesn't accidentally trigger an unwanted zoom-to-area. The scroll is deliberately deferred one
  dispatcher tick past the zoom change, since scrolling immediately would measure against the
  `ScrollViewer`'s pre-resize extent and could clamp to the wrong position.
- **Found the actual cause of "slight shift in text placement after typing"**: WPF's `TextBox`
  has a theme-dependent default `Padding` (non-zero in most themes) that insets the live-preview
  text from the box's edge - but the final raster commit draws starting exactly at the box's edge
  with no offset, so the committed text landed slightly up-and-left of where it visually appeared
  while you were typing. Set `Padding` to `0` to eliminate that discrepancy entirely, and also
  compensated for the one remaining (fixed, known - unlike the theme padding) source of offset:
  the box's own 1px `BorderThickness`, converted to the equivalent document-space amount at
  commit time so the text lands at the same effective position the live box showed.
- **Layer merge had a real correctness gap**: `MergeDown` blitted the layer being merged
  regardless of its visibility, meaning merging a *hidden* layer would silently revive its
  previously-invisible content into the layer below - surprising, and inconsistent with "merge
  should preserve what you actually see." Fixed: if the layer being merged is hidden, it's simply
  removed without blitting its pixels down, the same as it was already invisible. (Everything
  else about merge was already correct: it respects undo/redo via the `LayersWindow` push before
  calling it, and the hard-overwrite-not-alpha-blend behavior remains a documented, deliberate
  simplification consistent with how compositing works everywhere else in this engine - not a bug.)

### Twenty-first round: a just-drawn shape is now actually draggable

**"Shows selected but I can't drag it around like Paint"** - a real gap in how the "leave the
shape selected after drawing" feature (from a couple rounds back) worked. It showed the marching
ants, but the *tool* stayed on whichever shape tool you'd been using - and shape tools have no
"drag inside the selection to move it" logic at all (only Select/Free-Form Select/Magic Wand do).
The selection looked interactive but functionally wasn't, without an extra manual switch to the
Select tool first.

Fixed by having the shape tool request that switch itself: added `ToolContext.RequestToolSwitch`
(same pattern as the existing `BeginTextEditing` callback - a tool asks `MainWindow` to do
something it can't do on its own), wired to `SelectTool`. `DragShapeToolBase.OnMouseUp` now calls
it right after making the selection, so by the time your mouse button is up, you're already on
the Select tool and can immediately drag the shape you just drew - matching Illustrator/
PowerPoint-style "draw then immediately manipulate" behavior. Traced the resulting re-entrant call
carefully before shipping it: `SelectTool` calls `Cancel()` on whatever tool is *currently* active,
which at that moment is still the shape tool, mid-`OnMouseUp` - confirmed `Cancel()` is a safe,
idempotent no-op to call on itself (it only resets already-reset state and clears an
already-cleared preview), so the re-entrancy doesn't cause any corruption.

### Twenty-second round: the mascot is now the app's actual icon

Generated a proper multi-resolution `.ico` (16/32/48/64/128/256px, the 256px frame PNG-compressed
per the standard modern ICO format) from the mascot artwork via `convert -define
icon:auto-resize=...`, previewed it at 16x16 and 32x32 first to confirm it stays recognizable at
small sizes before committing to it. Wired it in two places, both needed for full coverage:
`<ApplicationIcon>` in `ShellProject.csproj` (embeds the icon into the .exe's own PE resources -
what File Explorer, the taskbar, and Alt+Tab show even before the app's own window logic runs),
and `Icon="pack://application:,,,/Resources/Mascot/mascot.ico"` on `MainWindow` itself (the
title bar icon, which doesn't automatically inherit from the exe icon in WPF). Also applied the
same `Icon` attribute to all nine dialog windows (About, Attributes, Edit Colors, Help, History,
Layers, New Picture, Shortcut Manager, Stretch/Skew) for a consistent identity across every
window the app opens, not just the main one - a low-risk, one-attribute-per-file addition. Since
this was a scripted multi-file XAML edit, verified every touched file still parses as
well-formed XML afterward rather than just trusting the script ran cleanly.

### Twenty-third round: closing the last documented known issue

The README's "Known simplifications" list had exactly one item left: true shape adjustment
handles. With the last couple of rounds' groundwork - a just-drawn shape auto-selects and
switches to the Select tool, making it immediately draggable - the remaining gap was resize.
Implemented it, scoped deliberately to stay low-risk rather than attempting a full generalized
transform system:

- **`SelectionManager.ResizeTo`**: rescales the floating content via nearest-neighbor sampling
  (matching every other raster operation in this engine, e.g. Stretch/Skew) and updates `Bounds`
  to match. Scoped to rectangular selections (`Mask == null`) only - a Free-Form Select or Magic
  Wand selection's irregular mask would need rescaling too, which isn't implemented, so those
  simply don't get resize handles rather than resizing incorrectly.
- **Four corner handles** on the Select tool (reusing the same handle visual and
  `WireManualDrag` pattern already proven for the text box and canvas resize handles), shown only
  when a rectangular selection exists. Dragging shows a cheap rubber-band outline preview - the
  actual per-pixel rescale only happens once, on release, matching how shape-tool previews
  already work, rather than re-rasterizing the whole floating bitmap on every mouse-move tick.
- **Precision**: the in-progress drag is tracked in screen-space `double` coordinates for the
  whole gesture and only converted to document-space integers once, at the end. Rounding a small
  fractional document-pixel delta on every single tick (there was an earlier draft that did this)
  would lose sub-pixel movement and make the resize feel sticky, especially at high zoom.
- **Kept in sync**: handles refresh on selection change, tool change, and zoom change (the last
  one specifically to avoid repeating the exact stale-position bug fixed for the text box two
  rounds ago), and also after Undo/Redo/History-jump so they don't end up positioned relative to
  a document state that's no longer current.

### Twenty-fourth round: closing two more open items

Went back over the "Known simplifications" list for anything genuinely actionable (as opposed to
the deliberate design choices - XP window chrome, Bézier curve math, nearest-neighbor
Stretch/Skew - which are intentional and stay as they are). Two were real gaps worth closing:

- **Selection resize now works for irregular selections too.** Last round I scoped `ResizeTo` to
  rectangular selections and skipped Free-Form Select / Magic Wand, on the grounds that their
  mask would need rescaling too. Revisiting it, that turned out to be a much smaller job than the
  deferral implied: the mask is a plain `bool[,]`, so it rescales with the *identical*
  nearest-neighbor sampling already being applied to the pixels - sampling both in the same loop
  with the same `sx`/`sy` guarantees the mask and the pixels it describes stay exactly in
  register. Removed the `Mask != null` bail-out and the matching restriction on which tools show
  handles; all three selection tools now offer resize.
- **DPI is now actually handled, not just untested.** There was no `app.manifest` at all, which
  risks the app being treated as DPI-unaware - Windows would then render it at 96 DPI and
  bitmap-stretch the result, which is blurry in general and particularly bad for a pixel editor
  where crisp pixel edges are the entire point. Added a manifest declaring Per-Monitor V2
  awareness, and set `UseLayoutRounding` on the canvas so document pixels land on whole device
  pixels at fractional scale factors (125%, 150%) instead of straddling boundaries at uneven
  widths. Also verified the mouse-to-document coordinate conversion was already DPI-safe - it
  derives its scale from the canvas's actual rendered size rather than an assumed constant, so it
  needed no change. Worth being precise about what this does and doesn't claim: the app is no
  longer *unhandled* at non-100% scaling, but with no Windows available here it remains
  *unverified* on real high-DPI hardware.

### Twenty-fifth round: flip and rotate now apply to the selection

Reviewing what was actually left, the remaining "Known simplifications" entries split cleanly into
two groups. Most are **deliberate design choices, not defects** - the standard WPF window chrome
(modern Windows can't render the XP visual style natively anyway), the Bézier curve
approximation, and nearest-neighbor Stretch/Skew (which intentionally matches legacy Paint's own
unsophisticated raster behavior). Those stay as they are. The one genuine gap left was selection
transforms:

- **Image > Flip/Rotate now applies to the active selection** rather than always transforming the
  whole picture - which is what classic Paint does when something is selected, and was a real
  behavioral gap, not just a missing nicety. Added `SelectionManager.TransformFloating`, which
  flips or rotates the floating content and rescales any free-form mask in exact lockstep with the
  pixels it describes (same approach that made irregular-selection resize work last round). Falls
  through to the existing whole-image transform when there's no selection, so nothing about the
  previous behavior changes when you aren't using a selection.
- **Rotation pivots around the selection's own center**, so a 90/270 rotation that swaps width and
  height stays visually in place instead of jumping to a new corner origin - which is what you'd
  expect when rotating one piece of a drawing in place.
- Checked the obvious edge case before shipping: a rotated selection whose new bounds extend past
  the canvas edge. `RasterSurface.SetPixel` already bounds-checks every write, so that case clips
  safely rather than corrupting memory - no extra guarding needed.

**Still genuinely unimplemented, and worth stating precisely:** *arbitrary-angle* rotation and
skew of a selection. Those produce content that no longer fits an axis-aligned `Int32Rect`, so
unlike 90-degree steps they can't be layered onto the existing model - they'd require changing how
selection bounds are represented throughout. That's a fair amount of churn across selection
rendering, hit-testing, and commit, so it's flagged here rather than half-attempted.

### Twenty-sixth round: shapes stay "virtual" until committed (the quality fix)

Previously a shape was rasterized into the document the instant you released the mouse, and the
selection left behind just wrapped those already-painted pixels. So resizing it resampled a
bitmap - and resizing again resampled *that* resample. Every adjustment compounded quality loss.

Shapes are now **deferred-rasterization**: drawn but not painted until the selection is committed.

- **`PendingShape`** (in `ITool.cs`) carries the shape's *defining parameters* - its start/end
  points, stroke padding, undo label - plus a `Render` delegate. The key realization that made
  this cheap to build: `DragShapeToolBase.DrawPreview(ctx, start, end, surface)` already renders a
  shape from its parameters into *any* surface. It was already exactly the abstraction needed;
  nothing about the individual shape tools had to change.
- **`DragShapeToolBase.OnMouseUp`** now hands that `PendingShape` to MainWindow instead of writing
  to the document. (Degenerate near-zero-area shapes still commit immediately the old way -
  they're not worth a selection.)
- **Every move/resize re-renders from the original parameters**, not from the previous render.
  Resize a rectangle from 50px to 400px and back to 50px twenty times and the result is bit-for-bit
  identical to drawing it at 50px directly, because no step ever samples a prior output.
- **Rasterized exactly once**, at commit - clicking away, switching tools, or Ctrl+D. That's also
  when the undo entry is pushed, since that's the moment the document actually changes; pushing it
  at draw time would have recorded a state where nothing had happened yet.
- **Esc discards it outright.** Since the document was never touched, this is a true cancel with
  nothing to undo. This needed a new `SelectionManager.Discard()` - the existing `Deselect()`
  *commits* floating content before clearing, which would have painted the very shape being
  cancelled.

Two ordering traps caught while building this, both of which would have silently defeated the
whole feature: `BeginPendingShape` has to call `SelectTool("Select")` **before** creating the
floating content, because `SelectTool` internally calls `FinalizeFloatingSelection()` and would
otherwise have committed the shape the instant it was created. And New/Open now explicitly clear
any pending shape, so one drawn just before opening a different file can't get committed into the
newly-loaded document. Also verified the existing move path was already safe: it only lifts when
`!IsFloating`, and a pending shape is always floating, so it never tries to vacate document pixels
that were never written.

### Twenty-seventh round: naming, full screen, icons, and three real fixes

- **App is now called Splash everywhere user-visible** - window title bar and the About dialog
  both said "Paint". (The C# `namespace PaintClone` is unchanged, as before: a namespace is
  independent of the product name, and renaming it across 40+ files buys nothing.)
- **F11 toggles full screen**, added as a proper rebindable entry in the shortcut registry rather
  than a hardcoded key check, so it shows up in the Shortcut Manager like everything else. It
  remembers the exact window state beforehand and restores it on exit, and goes via
  `WindowState.Normal` in between - `WindowStyle` can't be changed on an already-maximized
  window, and skipping that step leaves the chrome on and stops short of the taskbar.
- **Committed text no longer collapses to one ellipsized line.** `FormattedText.Trimming` was left
  at its default, so overflow could be replaced with "..." instead of rendering the wrapped lines
  that were visible while typing. Set explicitly to `TextTrimming.None`. (Worth noting: I first
  also set `MaxTextHeight = double.PositiveInfinity` here - WPF rejects a non-finite value for
  that property and it would have thrown at runtime. It's unbounded by default, so the correct fix
  was to simply not set it.)
- **Canvas resize handles now follow a rotate.** `UpdateStatusSize()` is what repositions those
  handles, and `ApplyRotate` / `ApplyTransform` / Stretch-Skew all rebound the canvas without ever
  calling it - so after a 90-degree rotate the handles stayed at the pre-rotate dimensions. Rather
  than patch just the rotate path, all 11 canvas-rebinding call sites now go through one
  `RefreshCanvasBinding()` helper that rebinds *and* resyncs both the canvas and selection
  handles, which fixes the same latent bug in flip and stretch/skew too.
- **Curve tool bends by dragging**, matching what its own status hint always promised ("drag it
  into a curve") and how the classic tool works. Previously the control points tracked plain hover
  movement - the curve writhed around under an un-pressed cursor and each bend was confirmed by a
  bare click. Bends now only track the mouse while a button is held, so the whole interaction is
  three clean press-drag-release gestures: draw the line, bend, bend again.
- **Fill and Magic Wand icons redrawn.** Fill is now a recognizable tilted paint bucket with a
  visible handle arcing over the pail, pouring a stream into a puddle (and the Fill cursor was
  rebuilt to match, with its hotspot moved to the new spout position). Magic Wand is now much
  closer to the familiar Photoshop silhouette: a slim wand on a steep diagonal with a dark tip
  section and a four-point sparkle burst.

### Twenty-eighth round: curve rewritten, ten more brush shapes, eraser preview fix

- **Curve tool rewritten from scratch**, which also fixed the reported bug where the curve snapped
  back toward its starting point on the second click. The old version confirmed each bend using a
  *remembered* hover position (`_lastHoverPoint`) rather than the coordinates of the press itself -
  so if a press landed somewhere no `MouseMove` had reported yet, the bend used a stale coordinate,
  often still the baseline's own start point. The rewrite uses an explicit six-state machine
  (Idle -> DrawingLine -> AwaitBend1 -> Bending1 -> AwaitBend2 -> Bending2) and takes every point
  straight from the event that caused the transition, so no remembered coordinate can go stale.
  Control points now also start pinned to the baseline, guaranteeing the preview is a true straight
  line until the first bend is actually applied. A zero-length click resets cleanly instead of
  stranding the tool mid-gesture. The interaction is now three clean press-drag-release gestures:
  draw, bend, bend.
- **Ten new brush shapes** (17 total), aimed at more expressive work: Triangle, Diamond, Star, Ring
  (hollow circle), Hollow Square, flat Horizontal and Vertical nibs, an angled Calligraphy nib
  (broad edge at ~30 degrees, so strokes naturally vary thick-to-thin with direction), grainy
  Chalk/charcoal (density falls off toward the rim), and Stipple (scattered dots). The toolbox row
  is now generated from a single `BrushShapeChoices` table rather than a hand-written entry per
  shape, so the buttons, glyphs, and tooltips all stay in sync automatically - and it already
  used a `WrapPanel`, so 17 buttons lay out without any layout change. Verified programmatically
  that all 17 enum values are covered in the stamp dispatch, in the toolbox UI, and by an actual
  `RasterSurface` stamp implementation, with none missing on any of the three.
- **Eraser outline now resizes immediately.** `UpdateEraserOutline` was only ever driven by
  `Canvas_MouseMove`, so changing the size with Ctrl+/Ctrl- (or the toolbox buttons) left the ring
  at its old size until the mouse happened to move - making the change look like it hadn't
  registered. The last cursor position is now remembered and the outline is redrawn right away on
  any size change.

### Twenty-ninth round: document DPI, five more file formats, three more tools

**DPI support.** This is *document* DPI - the picture's own print resolution - which is separate
from the display-scaling work done earlier. `PaintDocument` now carries `DpiX`/`DpiY` (default 96),
read from a file when one is opened and written back into saved files that carry a DPI field
(PNG/JPEG/TIFF all do). Image > Attributes gained a Resolution field with a live readout of the
physical size the picture will print at in both inches and cm - which is the only place DPI
becomes visible, since pixel dimensions alone don't tell you that. The status bar now shows it too.

**Five more file formats**, taking the list from 8 to 13 entries:
- **JPEG XR / HD Photo** (`.wdp`, `.jxr`) via WPF's `WmpBitmapEncoder`.
- **Windows Icon** (`.ico`) - WPF ships an ICO *decoder* but no encoder, so this writes the
  container by hand: header, one directory entry per size, then each size embedded as a complete
  PNG (valid for Vista and later, and far simpler than the legacy BMP-plus-AND-mask layout).
  Exports at six sizes, 16x16 through 256x256.
- **Targa** (`.tga`) - also no WPF codec either way, so both a writer and a reader were needed. The
  writer emits uncompressed 32-bit bottom-up TGA; the reader handles both uncompressed (type 2) and
  RLE-compressed (type 10) true-colour files and honours the header's top-down flag. Paletted TGAs
  are rejected with a clear message rather than silently mis-decoded.
- Opening a multi-size `.ico` now picks the **largest** frame rather than `Frames[0]`, which isn't
  reliably the biggest - otherwise opening an icon could hand you a 16x16 thumbnail.

**Three more tools** (20 total): **Arrow** (line plus a proportionate arrowhead, angled off the
line's own direction so it points correctly whichever way you drag), **Star** (five-pointed,
inscribed in the dragged box, with the same outline/fill options as the other closed shapes), and
**Gradient** (linear blend from foreground to background across the dragged area, with the drag
direction setting the angle rather than being locked to horizontal/vertical). Star reuses the
existing polygon scanline fill - which was private to `PolygonTool` and is now `internal` - rather
than duplicating that algorithm. The toolbox grew to 10 rows and the window default height with it.

Cross-checked programmatically that all 20 tools are registered, present in the toolbox order,
have an icon file on disk, and have a keyboard shortcut - and that no two of the now-47 shortcuts
collide.

### Thirtieth round: tool restore after drop, arrow/star options, Shift fixes

- **The drawing tool comes back after a shape is dropped.** Placing a shape always left you on
  the Select tool, so drawing three arrows meant re-picking Arrow twice. `PendingShape` now records
  which tool created it and `FinalizeFloatingSelection` switches back once the shape commits. This
  needed a re-entrancy guard: `SelectTool` itself calls `FinalizeFloatingSelection`, and without
  suppressing the restore there, explicitly picking a different tool while a shape was still
  floating would have bounced you straight back to the shape tool - overriding the user's own
  choice. Same guard on the close/New/Open path, where rebuilding tool UI mid-teardown is pointless.
- **Shift now works properly on arrows.** The 45-degree direction snap was hardcoded to
  `this is LineTool`, so Arrow (and Gradient) fell through to the *square bounding box* constraint
  instead - which forces dx == dy and is exactly why only diagonals worked. Replaced with a
  `ConstrainsToAngle` virtual that Line, Arrow, and Gradient opt into; the snap has always included
  horizontal and vertical, they just weren't reaching it.
- **Arrowheads no longer clip when the selection is resized.** The pending-shape bounds were padded
  only for stroke thickness, but an arrowhead sticks out perpendicular to the line well beyond
  that - so re-rendering into a tightly-fitted bitmap cut the head off, most visibly when dragging
  the selection shorter vertically. Added an `ExtraPad` virtual, which Arrow overrides with its
  head length.
- **Five arrow styles**: open head at the end, open heads at both ends, solid head, solid heads at
  both ends, and no head. Solid heads reuse the shared polygon scanline fill and are also outlined,
  since at small sizes a bare scanline fill can look ragged.
- **Star point count is selectable** (3, 4, 5, 6, 8, or 12). The inner radius now scales down as
  the point count rises - a fixed inner ratio made a 12-point star read as a gear rather than a star.
- **Unsaved-changes prompt on close** was already implemented and correctly wired through
  `Window_Closing`; verified it works for pending shapes too, since finalizing one marks the
  document dirty before the check. The dialog captions still said "Paint" though, so those (and the
  couple of other stale ones) now say Splash.

### Thirty-first round: two transparency bugs behind three symptoms

All three reported problems turned out to be two underlying bugs in how transparency was handled
when content is written into the document.

**1. `Blit` was stamping transparent-black over the canvas.** It skipped a source pixel only when
all four channels matched the caller's key colour. But an untouched pixel in a freshly-created
bitmap reads back as `(0,0,0,0)`, and `Colors.Transparent` is `#00FFFFFF` - the RGB differs, so
untouched pixels *failed* the test and got written, overwriting whatever was already on the canvas
with transparent-black. That is exactly the reported "background of the text area changes": the
empty space around the glyphs was punching a hole through the picture. Now any fully-transparent
source pixel is skipped whenever the key colour is itself fully transparent, regardless of its RGB.

**2. Background-colour keying was being applied to freshly-drawn content.** The
Opaque/Transparent option exists so that background-coloured pixels of a *lifted* selection are
see-through while you drag it around. But `SelectionManager.Commit` applied that keying to *all*
floating content - including a pending shape that was just rendered. So a gradient running from the
foreground colour into the background colour lost that entire end of its blend the instant it was
placed, and any shape drawn in the background colour vanished on commit. That's both the
"background is visible while rubber-banding but goes when you place it" symptom and the
"gradient changes when I select another tool" one (selecting another tool is just what triggers the
commit). Added `SelectionManager.ContentIsGenerated`, set for rendered/pasted content and cleared
for anything lifted out of the document, and Commit now skips the keying for generated content.
The two on-screen preview paths use the same condition, so what you see while dragging is now what
actually gets placed.

Audited every entry point that establishes or clears selection content (`BeginSelection`, `Lift`,
`BeginPaste`, `Discard`, `Deselect`) to confirm the new flag is set correctly in each - which
caught one edit that had silently not applied because the parameter name differed from what the
patch expected.

### Thirty-second round: 100-colour palette, gradient rewrite, ICO export dialog

**100-colour palette** replacing the 28-colour classic one, laid out as 20 columns x 5 rows: a
greyscale ramp along the top, then the same 20 hues at four brightness levels. The layout is the
point - each hue keeps a fixed column, so finding a lighter or darker version of a colour is a
straight vertical move rather than a hunt. Generated programmatically from HLS rather than
hand-picked, so the steps are even. The colour strip and window grew to fit, and swatch margins
were tightened since the cells are now much smaller.

**Gradient tool rewritten.** The old one was a single linear blend with no options and visible
banding. Now:
- **Five blend shapes**: linear, reflected (mirrored either side of the start), radial, diamond
  (square-cornered rings), and angular (sweeps around the start point like a colour wheel).
- **Ordered dithering**, on by default, using a fixed 4x4 Bayer matrix. An 8-bit channel can only
  step in whole units, so a slow blend across a wide area shows obvious banding; nudging each
  pixel's position by up to one output step before quantising breaks those hard boundaries into a
  fine interleave. Deliberately a *fixed* matrix rather than random noise - pending shapes
  re-render on every resize, and random dithering would make the gradient shimmer differently each
  time.
- The blend now covers the whole surface it's drawn into rather than assuming document
  coordinates, which is what makes it work correctly as a resizable pending shape.

**ICO export dialog.** Saving as `.ico` now opens a dedicated window before writing the file:
- Checkboxes for all six sizes (16, 32, 48, 64, 128, 256), defaulting to the four Windows actually
  reaches for most (16/32/48 for Explorer, taskbar and Alt+Tab, plus 256 for high-DPI and
  large-icon views).
- **Live preview** of the artwork rendered at each size, over a checkerboard so transparency is
  visible, using the same scaling the exporter itself uses so the preview is honest. This is the
  part worth having: a drawing that reads fine at full size often turns to mush at 16x16, and it's
  much better to discover that before exporting.
- Cancelling the dialog cancels the whole save, rather than quietly writing a default set of sizes
  the user never agreed to.
- Every size is embedded as 32-bit RGBA PNG data, preserving full alpha. `SaveIco` now takes the
  chosen size list; a plain Ctrl+S over an existing `.ico` (which doesn't go through the dialog)
  uses a sensible default set.

### Thirty-third round: two-row palette, paste-grows-canvas, real full-screen view

- **Palette is now a two-row strip** (50 columns), the way a horizontal colour bar works in most
  drawing applications, rather than the five-row block from last round. The colours were reordered
  rather than just reflowed: each column now pairs a darker shade on the top row with its lighter
  counterpart directly beneath, so the two rows stay meaningfully related instead of being an
  arbitrary run of 100 swatches. First ten columns are a greyscale ramp (black to mid-grey above,
  mid-grey to white below); the remaining forty sweep the hue wheel. Wrapped in a horizontal
  scroller so the full strip stays reachable however narrow the window gets.
- **Pasting a larger image offers to grow the canvas.** Previously anything hanging off the right
  or bottom edge was silently clipped the moment the selection committed. Paste now compares the
  clipboard image against the canvas and, if it's bigger, asks whether to enlarge - growing from
  the top-left so existing artwork keeps its coordinates and only empty space is added. Choosing
  Cancel discards the undo state that had already been pushed, so a cancelled paste doesn't leave
  a do-nothing step in the history.
- **F11 now shows just the picture.** The previous implementation only made the window borderless
  and maximised - every bit of editing UI was still on screen, which isn't what a full-screen view
  is for. There's now a proper overlay above the entire window: the picture scaled to fit on a
  neutral dark backdrop, with menus, toolbox, palette and status bar all hidden. Anything still
  floating is committed first, so what you see is the finished picture rather than the picture plus
  a detached in-progress selection. Esc, F11, or a click anywhere exits. While it's up, all other
  keyboard shortcuts are deliberately ignored - there's no visible UI to drive, so a stray tool
  shortcut would otherwise silently change state you can't see.

### Thirty-fourth round: gradient preview fix, anti-aliasing, palette libraries

- **Gradient preview now matches the result.** The gradient filled "the whole surface it's drawn
  into" - but during rubber-banding that surface is the *full-canvas* preview layer, while on
  commit it's a bitmap the size of the dragged box. So the preview covered the entire canvas and
  the committed result covered only the dragged area. It now derives its region from the drag
  itself in both paths, making them identical. This also needed a `UsesStrokePadding` opt-out: the
  pending-shape bounds are padded for stroke thickness, which for a gradient would have left an
  unpainted border around the edge.
- **F11 no longer shows transparency as black.** The full-screen view drew the picture straight
  onto its dark backdrop, so transparent areas read as solid black. A checkerboard now sits behind
  the picture, sized to the picture's own rendered bounds via an element binding so it tracks the
  scaled image exactly rather than tiling the whole screen.
- **Per-tool anti-aliasing.** Added `BlendPixel` (coverage-based alpha blending, as distinct from
  the hard-overwrite `SetPixel` everything used before) and `DrawLineAA`. Rather than Wu's classic
  algorithm - which only handles hairlines - the AA line walks the stroke's bounding box and shades
  each pixel by its distance from the line, so it works at any thickness, which is what the tools
  actually need. Each tool has its own hard/smooth setting *and its own default*: pencil, eraser,
  fill, magic wand and eyedropper default to hard edges, because a half-covered pixel isn't the
  exact colour you asked for and that actively breaks flood fill and pixel editing; curves,
  ellipses and diagonals default to smooth, where that's the whole point.
- **Edit Colors reorganised and expanded.** Material and Flat UI tabs are now scrollable, Flat UI
  gained 24 extended tones (softer pastels and deeper shades the original twenty don't cover), and
  there's a new **Standards** tab with RAL Classic and the CSS/X11 named colours.

  On Pantone specifically, worth being straight about: PANTONE is a trademark and the Matching
  System's values are proprietary and licensed, not public data. Shipping guessed sRGB
  approximations under those names would be worse than useless - the entire point of a spot-colour
  system is that the value is exact and reproducible, so an approximation labelled "PANTONE 185 C"
  actively misleads. RAL Classic is offered instead because it's a genuinely published standard,
  and the Standards tab says plainly why Pantone isn't there and what to do if you need it.

### Thirty-fifth round: colours with opacity, seven standards palettes, a livelier About

- **Colours can now carry transparency, not just be transparent.** Previously a colour was either
  fully opaque or the single special "Transparent" swatch. Edit Colors gained an Opacity slider and
  numeric field, hex accepts and emits eight-digit `#AARRGGBB` when there's actual transparency to
  express (staying with the familiar six-digit form when fully opaque), and any partly transparent
  swatch is painted over the checkerboard so its opacity is visible at a glance rather than looking
  like a slightly different solid colour. The custom-colour file format gained an alpha field, with
  the old three-value R,G,B lines still accepted so an existing colour file survives the upgrade.
- **Five more standards palettes, all collapsible.** Added the web-safe 216, safety/hazard colours
  (ANSI Z535 / ISO 3864 style), the documented CGA/EGA 16-colour hardware palette, traditional
  artists' pigments, and a 5%-step neutral ramp - joining RAL Classic and the CSS named colours for
  seven sets in total. That's several hundred swatches, far too many to show at once, so the
  Standards tab is now built as a stack of `Expander` sections with only the first open, each
  labelled with its colour count and a short note on what the set is (and, where relevant, that
  screen values are approximations of physical standards).
- **30 custom slots, five pre-filled.** The tray is now a fixed 10x3 grid with empty slots drawn as
  placeholders, so it stays a predictable shape instead of reflowing every time a colour is added.
  A fresh install starts with five useful colours rather than thirty empty holes - one of them
  deliberately semi-transparent, which is the quickest way to make it discoverable that custom
  colours can carry an alpha value at all.
- **About is more fun.** The mascot is now pokeable: clicking him plays a quick wobble animation
  (over and back, easing out so it settles rather than stopping dead) and pops a speech bubble with
  one of ten one-liners, with a small reward for anyone who pokes him ten times. The tagline under
  the app name is now picked at random from six on each open, alongside the existing rotating fun
  facts.

### Thirty-sixth round: actual machine verification

"Make it world class" is worth being honest about: the biggest thing standing between this project
and that description isn't a missing feature, it's that ~13,000 lines of C# had never been through
a compiler. Every round so far was reviewed by counting braces and grepping - which is why real
bugs kept surviving several rounds before being spotted.

So this round added actual verification rather than more surface area:

- **A real C# parser.** Mono was available in the sandbox, but at version 6.8 it only understands
  C# 7.3 and reported hundreds of errors on entirely valid modern code (`new()` target-typed
  expressions, mostly). Acting on those would have *damaged* working code to satisfy an obsolete
  parser - so they were verified as false positives and discarded. Installed the tree-sitter C#
  grammar instead, which is current, and every one of the 36 C# files parses cleanly under it.
- **`tools/verify.py`** runs that parse plus the cross-file consistency checks that no compiler
  would catch, because each half is individually valid: a XAML handler with no method behind it, a
  tool registered but missing from the toolbox, an enum value with no dispatch branch, two
  shortcuts on the same key. These are exactly the classes of bug that took several rounds to
  surface before, now caught in under a second.
- **One genuine finding.** The verifier flagged `GradientType.Linear` as having no branch - it was
  being handled by `default:`. Functionally fine, but it meant a *new* gradient type added later
  would silently render as linear instead of failing loudly. Made explicit.
- **A known-limitation carve-out, deliberately narrow.** The grammar can't parse `*(uint*)(expr)`,
  a pointer-cast dereference, which is valid unsafe C# and exactly how the raster engine touches
  the bitmap back buffer. Those four sites are reported as a skipped known limitation rather than
  as failures - a checker that cries wolf about valid code trains people to ignore it.
- **CI workflow** (`.github/workflows/build.yml`): the verifier on Linux for fast feedback, and a
  real `dotnet build` on Windows for both the app and the sample plugins, publishing the build as
  an artifact. That Windows job is the one that finally answers whether this compiles.

### Thirty-seventh round: wand settings, gradient alpha fix, edge control, 50 fonts

- **Found the real cause of "gradient becomes opaque once drawn".** `SelectionManager.Commit` wrote
  every pixel with `SetPixel`, which hard-overwrites. But while you're dragging, WPF composites the
  floating preview layer over the canvas with real alpha blending - so a semi-transparent gradient
  looked correctly blended, then on commit *replaced* the artwork underneath instead of laying
  colour over it. Commit now blends any pixel that isn't fully opaque (using the `BlendPixel` added
  a couple of rounds back) and keeps the fast direct write for fully opaque ones, so placed output
  matches the preview exactly.
- **Magic Wand settings**: a Tolerance option (0/8/16/32/64/128 - per-channel, including alpha,
  because squared-distance matching spreads unevenly across hues and doesn't behave the way a
  "tolerance" number leads people to expect) and a Contiguous/Global switch, so you can grab one
  colour everywhere it appears rather than only the region you clicked.
- **Edge control extended.** Anti-aliasing now covers ellipse outlines - the shape where hard pixel
  edges show worst, since a circle's perimeter is diagonal almost everywhere - via a coverage-based
  routine that shades by distance from the true edge. Text is wired up too, and needed care: with
  anti-aliasing on, the alpha-thresholding pass that makes aliased text safe is deliberately
  skipped, because those soft pixels are the entire point and thresholding would have destroyed
  them. Gradient and Text joined the list of tools offering the option.
- **50 fonts** in the Text tool's font picker, spanning serif, sans, monospace, script and display
  faces. Any not present on a given machine fall back to the system default at render time, so an
  unusual Windows configuration degrades gracefully.
- **Taskbar icon fixed.** The mascot artwork occupied only 401x373 of its 512x512 canvas, with a
  large transparent margin on top - which is exactly why it looked small and off-centre in the
  taskbar. Trimmed to the artwork and re-padded to a square with an even 4% margin, so it now fills
  475 of 512 pixels, and rebuilt the multi-resolution `.ico` from that.

**Worth recording:** while adding wand tolerance, a scripted edit matched patterns in *two* methods
and silently rewrote part of `FloodFill` as well as `MagicWandSelect`, leaving the file unbalanced.
The verifier added last round caught it immediately and it was reverted - which is a fair
demonstration of why that tool was worth building rather than continuing to eyeball diffs.

### Thirty-eighth round: the gradient colour drift, measured and fixed

"The gradient's colour shifts when the selection appears, then shifts again when it's placed" turned
out to be two independent causes, one for each shift.

**Shift one - the dither pattern moved.** The ordered dither indexed the Bayer matrix with raw
surface coordinates (`x & 3, y & 3`). A gradient gets rendered twice from the same parameters: once
during rubber-banding, into the *full-canvas* preview layer where those are document coordinates,
and again as a pending shape, into a *box-sized* bitmap where they restart at zero. So the dither
pattern landed differently the second time and every pixel's value moved by up to one output step.
It's now keyed to each pixel's offset from the gradient's own start point, which is identical in
both coordinate spaces, so the two renders line up exactly.

**Shift two - accumulating rounding error.** `PremultiplyStore` and `ReadRawUnpremultiplied` both
*truncated*. A translucent pixel makes several round trips between them on its way from drawn to
committed (render, blit into the preview layer, blend into the document), and truncation biases
the error the same direction every time, so it compounds. Both now round. Measured across all
alpha values and a spread of channel values:

```
1 round-trip:   before  avg error 3.53   after  avg error 1.45
3 round-trips:  before  avg error 10.00  after  avg error 1.45
```

The number that matters is the second row: the error no longer *accumulates*. Before, it roughly
tripled between one round trip and three - which is precisely the reported "changes, then changes
again". After, three round trips cost no more than one.

Worth stating plainly rather than implying this is now exact: the residual ~1.45 average (and a
much larger worst case at very low alpha) is inherent to storing colour premultiplied in 8 bits.
At an alpha of 2, for instance, a whole range of source colours collapses to the same stored byte
and cannot be recovered. That's a property of the format, not something further rounding fixes -
avoiding it would mean a wider backing buffer, which is a much larger change than this bug warrants.

### Thirty-ninth round: gradients commit immediately

Gradients no longer become a selected, still-editable pending shape - they go straight into the
picture on mouse-up and the Gradient tool stays selected.

The pending-shape flow exists so a *drawn shape* can be nudged or resized before being made
permanent, which is genuinely useful for a rectangle or an arrow. A gradient just fills the area you
dragged over, so there was no meaningful "now reposition it" step - the selection was only ever
something to dismiss, and it blocked the natural workflow of laying down several gradients in a row.

Implemented as a `UsesPendingShape` virtual on `DragShapeToolBase` that Gradient overrides, rather
than special-casing the tool at the call site, so any future tool with the same shape of behaviour
can opt out the same way. The immediate-commit branch it now takes already existed as the fallback
for zero-area drags, so this reuses a proven path rather than adding one.

Two things fall out of this for free. Because the direct-commit path draws with the same document
coordinates the rubber-band preview used, the dither pattern lines up exactly - so the residual
source of gradient colour shift is gone for good, not just reduced. And skipping the re-render round
trip entirely means the premultiplied-alpha precision loss discussed last round never happens for
gradients at all.

### Fortieth round: align a selection to the canvas

Image > Align Selection snaps the current selection to a position relative to the canvas: the three
horizontal positions (left / centred across / right), the three vertical ones (top / centred down /
bottom), the four corners, and centre-in-canvas.

Two decisions worth recording:

- **The horizontal and vertical commands are independent.** Choosing Left moves the selection to the
  left edge and leaves its vertical position exactly where it was. That's what makes the feature
  usable for lining several things up one axis at a time - if every command yanked the selection to
  a corner you'd have to re-place it vertically after every horizontal align. The four corner
  entries are there as a convenience for the common case, not because the axes are coupled.
- **Aligning lifts the selection first**, exactly as dragging or an arrow-key nudge does, so it
  *moves* the content rather than stamping a copy of it and leaving the original behind. It also
  returns early when the selection is already at the requested position, so a redundant align
  doesn't push a do-nothing step into the undo history.

The eleven menu entries share a single handler, dispatching on each item's `Tag`, rather than
eleven near-identical methods. The submenu is disabled whenever there's no selection, driven by the
same `Changed` subscription that already keeps the rest of the Edit menu's state current.

**Build status, stated precisely.** This project was written in a sandbox with no .NET SDK and
no Windows, so **it has never been compiled or run**. That remains the single biggest gap between
this and production-quality software, and no amount of features closes it.

What *is* now machine-verified, via `tools/verify.py`:
- Every C# file parses cleanly under a current C# grammar (tree-sitter), which catches real syntax
  errors that the brace-counting used in earlier rounds could not.
- Every XAML file and project file is well-formed XML.
- Every XAML event handler resolves to a real method.
- All 20 tools are registered, present in the toolbox, and have icon files on disk.
- All 47 keyboard shortcuts are unique and every non-tool action has a dispatch handler.
- Every `BrushShape` and `GradientType` value has a matching branch - so adding an enum value
  without wiring it up fails the check instead of silently doing nothing.

What that does **not** prove: type correctness, method resolution, XAML binding validity, or that
the thing runs. Only a real `dotnet build` on Windows does that, which is what the
`.github/workflows/build.yml` workflow is for - run it (or build locally) and the compiler's
output will be far more useful than any further static review.

### Forty-first round: a Photoshop-style UI

Reskinned and restructured the shell from the classic Windows-XP-era Paint look to a dark,
Photoshop-style creative-tool UI, on request. Two kinds of change, at different scope:

- **App-wide dark theme** (`App.xaml`): every color the old "XP Classic" palette defined
  (`XpFace`, `XpBorder`, etc.) was recolored to a dark charcoal palette rather than renamed, so
  every window that already referenced `{StaticResource XpFace}` - every dialog, not just
  `MainWindow` - re-themes automatically. New implicit (no-`x:Key`) styles for `Window`,
  `TextBlock`, `Button`, `CheckBox`, `RadioButton`, `TextBox`, `ComboBox`, `ListBox`/`ListView`,
  `TreeView`, `TabControl`, `GroupBox`, `Menu`/`MenuItem`, and a minimal flat `ScrollBar` template
  mean plain, unstyled controls anywhere in the app pick up the dark look for free. A handful of
  dialogs (`AboutDialog`, `AttributesDialog`, `IcoExportDialog`, `ShortcutManagerWindow`) had
  hardcoded dark-gray/navy text (`#444`, `#0A246A`, ...) tuned for the old light background;
  those specific `Foreground` values were brightened by hand since local values always win over
  an implicit style, so the theme change alone couldn't fix them.
- **MainWindow restructuring**: the tool options box moved from a vertical strip under the
  toolbox into a horizontal, context-sensitive options bar docked under the menu (Photoshop's
  layout) - `ToolOptionsPanel` is now a horizontal `StackPanel` in a horizontally-scrolling bar
  rather than a vertical one in a sunken side panel; every `BuildToolOptions` call site was
  already just appending a label followed by a `WrapPanel` per option group, so this needed no
  C# changes at all, only the container's orientation. The 19-tool toolbox keeps its existing
  icon-only buttons (it already had no text labels) but narrows to a 64px strip with FG/BG
  color swatches moved to its bottom edge, matching where Photoshop puts them. The floating,
  independently-shown **Layers** and **History** windows (`Dialogs/LayersWindow`,
  `Dialogs/HistoryWindow`) were converted into `UserControl`s (`Dialogs/LayersPanel`,
  `Dialogs/HistoryPanel`) permanently docked on the right alongside a new **Swatches** section
  (the old horizontal palette bar, reflowed into a 10x10 grid), separated by `GridSplitter`s so
  they resize like real docked panels instead of opening as separate top-level windows. The
  View menu's Layers/History toggles now show or hide that docked section's `Visibility` instead
  of opening/closing a `Window`.

Verified with a real `dotnet build` (`net10.0-windows`, .NET 10 SDK) - it builds clean, and the
resulting `ShellProject.exe` was launched and left running for several seconds with no unhandled
XAML/binding exceptions before being closed, confirming the retemplated controls (menu, buttons,
scrollbars, the two new docked panels) actually load rather than just parse.

## How to build

Requires Windows + .NET 10 SDK (WPF is Windows-only; this will not build on Linux/macOS).

```
cd Splash
dotnet build
dotnet run --project ShellProject
```

or open `Splash.sln` in Visual Studio and press F5.

### Verifying without a build

`tools/verify.py` runs a C# syntax parse plus a set of cross-file consistency checks, and works on
any platform (no .NET needed) - useful as a fast pre-commit check:

```
pip install tree_sitter tree_sitter_c_sharp
python3 tools/verify.py
```

It exits non-zero on failure, so it drops straight into a git hook or CI step. It is a pre-filter,
not a substitute for a real build - see "Build status" below.

## What's fully implemented

- **Raster engine** (`Models/RasterSurface.cs`): direct unsafe pixel access to a
  `WriteableBitmap`, exact-color flood fill, Bresenham lines, filled/outlined
  rectangles, ellipses (parametric outline + scanline fill), rounded rectangles,
  region copy/blit. Nothing is drawn as a stack of WPF `Shape` objects — the document
  is genuinely a bitmap, as the spec requires.
- **Undo/redo** (`Services/HistoryManager.cs`): whole-frame snapshots, one history
  entry per committed operation (a whole stroke, a whole shape, a whole fill — never
  per mouse-move event).
- **Tools**: Pencil, Brush (round/square), Airbrush, Eraser (uses the *background*
  color, not hardcoded white), Fill, Eyedropper, Magnifier (cycles zoom 1/2/4/6/8x),
  Line, Rectangle, Ellipse, Rounded Rectangle, Polygon (click vertices, double-click
  to close), Curve, Rectangular Select, Free-Form Select (lasso → scanline
  point-in-polygon mask), Text (real `TextBox` overlay while editing, rasterized into
  the bitmap on commit via `FormattedText`).
- **Selection semantics** (`Services/SelectionManager.cs`): a selection is just a
  marquee until you actually drag it, at which point pixels are lifted into a
  floating bitmap and the vacated area is filled with the background color —
  matching classic Paint's behavior rather than a modern alpha-compositing model.
  Cut/Copy/Paste/Delete/Move all go through this, including Free-Form's mask.
- **Menus**: File (New/Open/Save/Save As/Print/Exit), Edit (Undo/Redo/Cut/Copy/
  Paste/Clear/Select All with correct enable-state), View (toolbox/color box/status
  bar visibility, zoom levels), Image (Flip/Rotate, Stretch/Skew, Invert, Attributes,
  Clear Image, Draw Opaque), Colors (Edit Colors), Help (About).
- **File formats**: BMP/PNG/JPEG/GIF/TIFF read and write, using WPF's built-in
  `BitmapDecoder`/`BitmapEncoder` classes (no extra dependencies needed).
- **Keyboard shortcuts**: Ctrl+N/O/S/P/Z/Y/X/C/V/A/I/E, Delete, Escape.
- **Colors**: classic 28-swatch palette, left/right-click → foreground/background,
  overlapping color indicator, Edit Colors dialog with RGB entry and a custom-colors
  tray.

## Known simplifications (documented deviations from the spec)

These were cut or simplified to keep the deliverable buildable in one pass. All are
straightforward to extend later. (A few items that used to be listed here - tool icon
edge bleed, and a "grid rendering left as a stub" note - were fixed in later rounds and
removed from this list rather than left inaccurate.)

- **Window chrome**: uses a standard WPF `Window` rather than owner-drawn Win32
  non-client area, so you get your OS's real title bar, not a pixel-perfect XP theme
  (modern Windows can't render the XP visual style natively regardless). The menu
  bar, toolbox, palette, canvas, and status bar below the title bar *are* styled to
  the XP "Classic" palette (`App.xaml` resources).
- **Curve tool**: implements the two-stage-drag *workflow* faithfully, but renders
  with a cubic Bézier rather than reverse-engineering the exact legacy curve math.
- **Stretch/Skew**: nearest-neighbor inverse mapping (intentionally simple, matching
  legacy Paint's unsophisticated raster ops) rather than a calibrated skew transform.
- **DPI**: the app now ships an `app.manifest` declaring Per-Monitor V2 DPI awareness,
  and the canvas uses `UseLayoutRounding` so document pixels land on whole device
  pixels at fractional scale factors. Mouse-to-document coordinate conversion derives
  its scale from the canvas's actual rendered size rather than an assumed constant, so
  it stays correct under any scaling. Still not *verified* on real non-100% hardware
  (no Windows available in this environment) - but it's no longer simply unhandled.
- **Shape adjustment**: a selection can be moved (drag or arrow-key nudge), resized
  (drag a corner handle), flipped, and rotated in 90-degree steps (Image > Flip/Rotate
  applies to the selection when one exists) - all working for every selection type,
  including Free-Form Select and Magic Wand's irregular masks. What remains
  unimplemented is *arbitrary-angle* rotation and skew of a selection: those produce
  content that no longer fits an axis-aligned `Int32Rect` bounds model, so they'd need
  a genuinely different representation for selection bounds throughout, rather than
  being another operation layered onto the existing one.

## Project layout

```
Splash.sln
ShellProject/                   (C# code still uses "namespace PaintClone" internally)
  ShellProject.csproj
  App.xaml(.cs)              - XP color/style resources
  MainWindow.xaml(.cs)       - shell: menus, toolbox, palette, canvas host, status bar
  Models/
    RasterSurface.cs         - the raster engine (pixel access, primitives, flood fill)
    PaintDocument.cs         - document state (layers, dirty flag, file path)
    PaintLayer.cs            - one layer: its own RasterSurface, name, visibility
  Services/
    ColorManager.cs          - foreground/background + classic palette
    HistoryManager.cs        - undo/redo (whole-layer-stack snapshots, per-entry labels)
    SelectionManager.cs      - selection lift/move/commit (rect + free-form + magic wand)
    ShortcutManager.cs       - rebindable keyboard shortcut registry
    MaterialColors.cs / FlatUIColors.cs - built-in professional color palettes
    Plugins/
      PluginManager.cs       - scans Plugins/ folder, loads plugins via reflection
  Controls/
    PaintCanvas.cs           - one Image per layer + preview layer + marching-ants selection
  Tools/
    ITool.cs                 - tool interface + shared ToolContext
    FreehandTools.cs         - Pencil, Brush, Airbrush, Eraser
    FillAndPickTools.cs      - Fill, Eyedropper, Magnifier
    ShapeTools.cs            - Line, Rectangle, Ellipse, RoundedRectangle, Polygon, Curve
    SelectionTools.cs        - Rectangular + Free-Form select + Magic Wand
    TextTool.cs               - Text (overlay TextBox → rasterized commit)
  Dialogs/
    AttributesDialog, EditColorsDialog, StretchSkewDialog, AboutDialog,
    NewDocumentDialog, LayersWindow, HistoryWindow, ShortcutManagerWindow, HelpTopicsDialog
  Resources/
    Icons/    - the 17 original tool icons
    Cursors/  - the Fill tool's custom cursor
    Mascot/   - "Splash," the About dialog's mascot (and the solution's namesake)

SamplePlugins/
  SamplePlugins.csproj        - standalone class library, no reference to ShellProject.csproj
  GrayscalePlugin.cs / SepiaPlugin.cs / BrightnessBoostPlugin.cs
  README.md                   - how to write and install your own plugin
```
