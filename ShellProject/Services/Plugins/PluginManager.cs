using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace PaintClone.Services.Plugins
{
    /// <summary>
    /// Loads image-effect plugins from a "Plugins" folder next to the executable. Deliberately
    /// uses duck typing via reflection rather than a shared interface assembly: a plugin is just
    /// any public class with a public parameterless constructor and a public
    /// <c>void Apply(WriteableBitmap bitmap)</c> method (optionally a <c>Name</c> and
    /// <c>Description</c> property/field). This means a plugin can be a completely standalone
    /// class library referencing nothing but standard WPF assemblies (PresentationCore,
    /// WindowsBase) - no reference to PaintClone itself, no interface-versioning problems, and
    /// nothing for a plugin author to get wrong beyond "have a method with this exact signature."
    /// See PaintClone.SamplePlugins/ for a working example plugin project.
    /// </summary>
    public class PluginManager
    {
        public class LoadedPlugin
        {
            public string Name;
            public string Description;
            public string SourceFile;
            private readonly object _instance;
            private readonly MethodInfo _applyMethod;

            public LoadedPlugin(object instance, MethodInfo applyMethod, string name, string description, string sourceFile)
            {
                _instance = instance;
                _applyMethod = applyMethod;
                Name = name;
                Description = description;
                SourceFile = sourceFile;
            }

            public void Apply(WriteableBitmap bitmap) => _applyMethod.Invoke(_instance, new object[] { bitmap });
        }

        public List<LoadedPlugin> Plugins { get; } = new();
        public List<string> LoadErrors { get; } = new();

        public static string PluginsFolder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");

        public void LoadAll()
        {
            Plugins.Clear();
            LoadErrors.Clear();

            try
            {
                if (!Directory.Exists(PluginsFolder))
                    Directory.CreateDirectory(PluginsFolder);
            }
            catch
            {
                // If we can't even create the folder, there's nothing to scan - fail quietly and
                // let the Plugins menu show "no plugins installed" rather than crash startup.
                return;
            }

            foreach (var dll in Directory.GetFiles(PluginsFolder, "*.dll"))
            {
                try
                {
                    var asm = Assembly.LoadFrom(dll);
                    foreach (var type in asm.GetExportedTypes())
                    {
                        var applyMethod = type.GetMethod("Apply", new[] { typeof(WriteableBitmap) });
                        if (applyMethod == null || applyMethod.ReturnType != typeof(void)) continue;

                        var ctor = type.GetConstructor(Type.EmptyTypes);
                        if (ctor == null) continue;

                        object instance;
                        try { instance = ctor.Invoke(null); }
                        catch { continue; } // a plugin whose constructor throws just doesn't get listed

                        string name = ReadStringMember(instance, type, "Name") ?? type.Name;
                        string description = ReadStringMember(instance, type, "Description") ?? "";

                        Plugins.Add(new LoadedPlugin(instance, applyMethod, name, description, Path.GetFileName(dll)));
                    }
                }
                catch (Exception ex)
                {
                    LoadErrors.Add($"{Path.GetFileName(dll)}: {ex.Message}");
                }
            }
        }

        private static string ReadStringMember(object instance, Type type, string memberName)
        {
            try
            {
                var prop = type.GetProperty(memberName);
                if (prop != null && prop.PropertyType == typeof(string))
                    return prop.GetValue(instance) as string;
                var field = type.GetField(memberName);
                if (field != null && field.FieldType == typeof(string))
                    return field.GetValue(instance) as string;
            }
            catch
            {
                // Non-fatal - just fall back to the type name.
            }
            return null;
        }
    }
}
