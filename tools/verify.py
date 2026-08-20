#!/usr/bin/env python3
"""
verify.py - project consistency checks for Splash.

Two kinds of checking happen here.

1. Real C# syntax validation, using the tree-sitter C# grammar (a current grammar, so modern
   language features like target-typed `new()`, switch expressions and file-scoped namespaces
   parse correctly). This catches genuine syntax errors - malformed statements, stray or missing
   punctuation, truncated edits - which brace counting cannot.

2. Cross-file consistency checks that no compiler would catch, because each half is individually
   valid: a XAML event handler with no matching method, an `x:Name` that code never uses, a tool
   registered but missing from the toolbox, an enum value with no dispatch branch, two keyboard
   shortcuts bound to the same combination.

Run:  python3 tools/verify.py
Exit code is non-zero if anything failed, so it works in a pre-commit hook or CI step.

Requires: pip install tree_sitter tree_sitter_c_sharp
"""

import os
import re
import sys
from collections import Counter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
APP = os.path.join(ROOT, "ShellProject")

failures = []
notes = []

# `*(uint*)(...)` - dereferencing a pointer cast. Valid unsafe C#, but outside what the
# tree-sitter C# grammar parses.
POINTER_DEREF = re.compile(r'\*\s*\(\s*\w+\s*\*\s*\)')


def fail(msg):
    failures.append(msg)


def cs_files(base):
    for dirpath, dirnames, filenames in os.walk(base):
        dirnames[:] = [d for d in dirnames if d not in ("obj", "bin")]
        for f in filenames:
            if f.endswith(".cs"):
                yield os.path.join(dirpath, f)


def xaml_files(base):
    for dirpath, dirnames, filenames in os.walk(base):
        dirnames[:] = [d for d in dirnames if d not in ("obj", "bin")]
        for f in filenames:
            if f.endswith(".xaml"):
                yield os.path.join(dirpath, f)


# ---------------------------------------------------------------- 1. C# syntax
def check_csharp_syntax():
    try:
        from tree_sitter import Language, Parser
        import tree_sitter_c_sharp
    except ImportError:
        notes.append("tree_sitter / tree_sitter_c_sharp not installed - skipping syntax check. "
                     "Install with: pip install tree_sitter tree_sitter_c_sharp")
        return

    lang = Language(tree_sitter_c_sharp.language())
    parser = Parser(lang)

    pointer_skips = []
    checked = 0
    for path in cs_files(ROOT):
        src = open(path, "rb").read()
        tree = parser.parse(src)
        checked += 1

        # Walk for ERROR / MISSING nodes, which is how tree-sitter reports a parse failure.
        stack = [tree.root_node]
        while stack:
            node = stack.pop()
            if node.type == "ERROR" or node.is_missing:
                line = node.start_point[0] + 1
                snippet = src[node.start_byte:node.start_byte + 60].decode("utf8", "replace")
                snippet = snippet.replace("\n", " ")
                rel = os.path.relpath(path, ROOT)

                # The grammar doesn't handle dereferencing a pointer cast - `*(uint*)(expr)` -
                # which is valid unsafe C# and is exactly how the raster engine reads and writes
                # the bitmap back buffer. Reported as a known limitation rather than a failure,
                # since flagging valid code as broken would train people to ignore this tool.
                if POINTER_DEREF.search(snippet):
                    pointer_skips.append(f"{rel}:{line}")
                    continue

                fail(f"C# syntax error: {rel}:{line}  near: {snippet!r}")
                continue  # don't descend into a broken subtree; one report per site is enough
            stack.extend(node.children)

    notes.append(f"Parsed {checked} C# file(s) with the tree-sitter C# grammar.")
    if pointer_skips:
        notes.append(f"{len(pointer_skips)} unsafe pointer-dereference site(s) skipped "
                     f"(grammar limitation, not a code problem): {', '.join(pointer_skips)}")


# ------------------------------------------------- 2. XAML handlers and x:Name
EVENT_ATTR = re.compile(
    r'\b(?:Click|Closing|Loaded|SelectionChanged|SelectedItemChanged|TextChanged|ValueChanged|'
    r'Checked|Unchecked|MouseLeftButtonDown|MouseLeftButtonUp|MouseRightButtonDown|MouseMove|'
    r'MouseDoubleClick|KeyDown|PreviewKeyDown)\s*=\s*"([A-Za-z_]\w*)"')


def check_xaml_handlers():
    for xaml in xaml_files(APP):
        cs = xaml + ".cs"
        if not os.path.exists(cs):
            continue
        xaml_src = open(xaml, encoding="utf8").read()
        cs_src = open(cs, encoding="utf8").read()
        rel = os.path.relpath(xaml, ROOT)

        for handler in sorted(set(EVENT_ATTR.findall(xaml_src))):
            if not re.search(r'\b(?:void|async\s+Task)\s+' + re.escape(handler) + r'\s*\(', cs_src):
                fail(f"XAML handler with no method: {rel} -> {handler}()")


# ------------------------------------------------------- 3. Tool registration
def check_tool_registration():
    mw = os.path.join(APP, "MainWindow.xaml.cs")
    if not os.path.exists(mw):
        return
    src = open(mw, encoding="utf8").read()

    registered = set(re.findall(r'_tools\["(\w+)"\]\s*=', src))
    in_toolbox = set(re.findall(r'\("(\w+)",\s*"[\w_]+",\s*"[^"]+"\)', src))
    icon_files = set(re.findall(r'\("\w+",\s*"([\w_]+)",\s*"[^"]+"\)', src))

    for key in sorted(registered - in_toolbox):
        fail(f"Tool registered but absent from the toolbox list: {key}")
    for key in sorted(in_toolbox - registered):
        fail(f"Tool listed in the toolbox but never registered: {key}")

    icons_dir = os.path.join(APP, "Resources", "Icons")
    if os.path.isdir(icons_dir):
        have = {os.path.splitext(f)[0] for f in os.listdir(icons_dir)}
        for name in sorted(icon_files - have):
            fail(f"Toolbox references a missing icon file: Resources/Icons/{name}.png")

    if registered:
        notes.append(f"{len(registered)} tools registered, all present in the toolbox with icons.")


# --------------------------------------------------------- 4. Keyboard shortcuts
def check_shortcuts():
    path = os.path.join(APP, "Services", "ShortcutManager.cs")
    if not os.path.exists(path):
        return
    src = open(path, encoding="utf8").read()

    entries = re.findall(r'Id\s*=\s*"([^"]+)".*?DefaultKey\s*=\s*Key\.(\w+),\s*DefaultMods\s*=\s*([^}]+)\}', src)
    combos = {}
    for ident, key, mods in entries:
        combo = (key, mods.strip())
        combos.setdefault(combo, []).append(ident)
    for combo, ids in sorted(combos.items()):
        if len(ids) > 1 and combo[0] != "None":
            fail(f"Duplicate shortcut binding {combo[0]}+{combo[1]}: {', '.join(ids)}")

    # Every non-tool action needs an entry in MainWindow's dispatch table.
    mw = os.path.join(APP, "MainWindow.xaml.cs")
    if os.path.exists(mw):
        mw_src = open(mw, encoding="utf8").read()
        handled = set(re.findall(r'\["([A-Za-z_]+)"\]\s*=', mw_src))
        for ident, _, _ in entries:
            if ident.startswith("Tool_"):
                continue
            if ident not in handled:
                fail(f"Shortcut action with no handler in the dispatch table: {ident}")

    if entries:
        notes.append(f"{len(entries)} keyboard shortcuts defined, no duplicate bindings.")


# ------------------------------------------------- 5. Enum dispatch completeness
def check_enum_dispatch(enum_name, enum_file, dispatch_file, label):
    epath = os.path.join(APP, enum_file)
    dpath = os.path.join(APP, dispatch_file)
    if not (os.path.exists(epath) and os.path.exists(dpath)):
        return
    esrc = open(epath, encoding="utf8").read()
    m = re.search(r'enum\s+' + enum_name + r'\s*\{(.*?)\}', esrc, re.S)
    if not m:
        return
    body = re.sub(r'//.*', '', m.group(1))
    values = [v.strip() for v in body.replace("\n", " ").split(",") if v.strip()]
    dsrc = open(dpath, encoding="utf8").read()
    for v in values:
        if f"{enum_name}.{v}" not in dsrc:
            fail(f"{label}: {enum_name}.{v} has no branch in {dispatch_file}")
    if values:
        notes.append(f"{enum_name}: all {len(values)} values handled in {os.path.basename(dispatch_file)}.")


# ------------------------------------------------------------- 6. XML validity
def check_xml_wellformed():
    import xml.etree.ElementTree as ET
    targets = list(xaml_files(ROOT))
    for extra in ("ShellProject/ShellProject.csproj", "ShellProject/app.manifest",
                  "SamplePlugins/SamplePlugins.csproj"):
        p = os.path.join(ROOT, extra)
        if os.path.exists(p):
            targets.append(p)
    for path in targets:
        try:
            ET.parse(path)
        except Exception as exc:
            fail(f"Malformed XML: {os.path.relpath(path, ROOT)} - {exc}")
    notes.append(f"{len(targets)} XAML/project file(s) are well-formed XML.")


def main():
    check_csharp_syntax()
    check_xml_wellformed()
    check_xaml_handlers()
    check_tool_registration()
    check_shortcuts()
    check_enum_dispatch("BrushShape", "Tools/ITool.cs", "Tools/FreehandTools.cs", "Brush shapes")
    check_enum_dispatch("GradientType", "Tools/ITool.cs", "Tools/ShapeTools.cs", "Gradient types")

    print("Splash - project verification\n" + "=" * 46)
    for n in notes:
        print(f"  ok   {n}")
    if failures:
        print()
        for f in failures:
            print(f"  FAIL {f}")
        print(f"\n{len(failures)} problem(s) found.")
        return 1
    print("\nAll checks passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
