using System;
using System.IO;
using System.Linq;
using UnityEngine;
using System.Reflection;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEditorInternal;

public static class LayoutSwitcherTool
{
    [Shortcut("LayoutSwitcher/Layout1", KeyCode.Alpha1, ShortcutModifiers.Alt)]
    public static void Layout1MenuItem()
    {
        OpenLayout("Default");
    }
    
    [Shortcut("LayoutSwitcher/Layout2", KeyCode.Alpha2, ShortcutModifiers.Alt)]
    public static void Layout2MenuItem()
    {
        OpenLayout("HyperCasual");
    }

    static bool OpenLayout(string name)
    {
        string path = GetWindowLayoutPath(name);
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        Type windowLayoutType = typeof(Editor).Assembly.GetType("UnityEditor.WindowLayout");
        if (windowLayoutType != null)
        {
            MethodInfo tryLoadWindowLayoutMethod = windowLayoutType.GetMethod("LoadWindowLayout",
                BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(string), typeof(bool) }, null);

            if (tryLoadWindowLayoutMethod != null)
            {
                object[] arguments = new object[] { path, false };
                bool result = (bool)tryLoadWindowLayoutMethod.Invoke(null, arguments);
                return result;
            }
        }

        return false;
    }

    static string GetWindowLayoutPath(string name)
    {
        string layoutsPreferencesPath = Path.Combine(InternalEditorUtility.unityPreferencesFolder, "Layouts");
        string layoutsModePreferencesPath = Path.Combine(layoutsPreferencesPath, ModeService.currentId);

        if (Directory.Exists(layoutsModePreferencesPath))
        {
            string[] layoutPaths = Directory.GetFiles(layoutsModePreferencesPath).Where(path => path.EndsWith(".wlt"))
                .ToArray();

            if (layoutPaths != null)
            {
                foreach (var layoutPath in layoutPaths)
                {
                    if (string.Compare(name, Path.GetFileNameWithoutExtension(layoutPath)) == 0)
                    {
                        return layoutPath;
                    }
                }
            }
        }

        return null;
    }
}