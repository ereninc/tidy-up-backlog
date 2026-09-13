#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace EXW.SaveSystem.Editor
{
    internal sealed class JTokenTreeDrawer
    {
        private readonly Dictionary<string, bool> _foldouts =
            new Dictionary<string, bool>(StringComparer.Ordinal);

        private readonly Dictionary<string, string> _newPropertyNames =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public bool Draw(JToken token)
        {
            bool changed = false;
            DrawToken(token, "root", "Save Data", ref changed, canRemove: false);
            return changed;
        }

        private bool DrawToken(
            JToken token,
            string path,
            string label,
            ref bool changed,
            bool canRemove)
        {
            if (token == null)
            {
                EditorGUILayout.LabelField(label, "<missing>");
                return false;
            }

            return token.Type switch
            {
                JTokenType.Object => DrawObject((JObject)token, path, label, ref changed, canRemove),
                JTokenType.Array => DrawArray((JArray)token, path, label, ref changed, canRemove),
                _ => DrawValue((JValue)token, label, ref changed, canRemove)
            };
        }

        private bool DrawObject(
            JObject value,
            string path,
            string label,
            ref bool changed,
            bool canRemove)
        {
            bool remove = false;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool open = GetFoldout(path, defaultValue: path == "root");
                    open = EditorGUILayout.Foldout(open, $"{label}  Object ({value.Count})", true);
                    _foldouts[path] = open;

                    if (canRemove && GUILayout.Button("Remove", GUILayout.Width(64)))
                    {
                        remove = true;
                    }
                }

                if (!GetFoldout(path, defaultValue: false))
                {
                    return remove;
                }

                List<JProperty> properties = value.Properties().ToList();

                for (int i = 0; i < properties.Count; i++)
                {
                    JProperty property = properties[i];
                    string childPath = $"{path}.{property.Name}";

                    if (DrawToken(
                            property.Value,
                            childPath,
                            property.Name,
                            ref changed,
                            canRemove: true))
                    {
                        property.Remove();
                        changed = true;
                    }
                }

                DrawAddProperty(value, path, ref changed);
            }

            return remove;
        }

        private bool DrawArray(
            JArray value,
            string path,
            string label,
            ref bool changed,
            bool canRemove)
        {
            bool remove = false;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool open = GetFoldout(path, defaultValue: false);
                    open = EditorGUILayout.Foldout(open, $"{label}  Array ({value.Count})", true);
                    _foldouts[path] = open;

                    if (canRemove && GUILayout.Button("Remove", GUILayout.Width(64)))
                    {
                        remove = true;
                    }
                }

                if (!GetFoldout(path, defaultValue: false))
                {
                    return remove;
                }

                int removeIndex = -1;

                for (int i = 0; i < value.Count; i++)
                {
                    if (DrawToken(
                            value[i],
                            $"{path}[{i}]",
                            $"[{i}]",
                            ref changed,
                            canRemove: true))
                    {
                        removeIndex = i;
                    }
                }

                if (removeIndex >= 0)
                {
                    value.RemoveAt(removeIndex);
                    changed = true;
                }

                if (GUILayout.Button("Add Element", GUILayout.Width(120)))
                {
                    JToken next = value.Count > 0
                        ? CreateEmptyLike(value[value.Count - 1])
                        : JValue.CreateString(string.Empty);

                    value.Add(next);
                    changed = true;
                }
            }

            return remove;
        }

        private static bool DrawValue(
            JValue value,
            string label,
            ref bool changed,
            bool canRemove)
        {
            bool remove = false;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUI.BeginChangeCheck();

                object nextValue = value.Type switch
                {
                    JTokenType.Integer => EditorGUILayout.LongField(label, value.Value<long>()),
                    JTokenType.Float => EditorGUILayout.DoubleField(label, value.Value<double>()),
                    JTokenType.Boolean => EditorGUILayout.Toggle(label, value.Value<bool>()),
                    JTokenType.Null => EditorGUILayout.TextField(label, string.Empty),
                    _ => EditorGUILayout.TextField(label, value.Value<string>() ?? string.Empty)
                };

                if (EditorGUI.EndChangeCheck())
                {
                    value.Value = nextValue;
                    changed = true;
                }

                if (canRemove && GUILayout.Button("Remove", GUILayout.Width(64)))
                {
                    remove = true;
                }
            }

            return remove;
        }

        private void DrawAddProperty(JObject target, string path, ref bool changed)
        {
            _newPropertyNames.TryGetValue(path, out string propertyName);

            using (new EditorGUILayout.HorizontalScope())
            {
                propertyName = EditorGUILayout.TextField(propertyName ?? string.Empty);
                _newPropertyNames[path] = propertyName;

                using (new EditorGUI.DisabledScope(
                           string.IsNullOrWhiteSpace(propertyName) || target.Property(propertyName) != null))
                {
                    if (GUILayout.Button("Add Property", GUILayout.Width(100)))
                    {
                        target[propertyName] = string.Empty;
                        _newPropertyNames[path] = string.Empty;
                        changed = true;
                    }
                }
            }
        }

        private bool GetFoldout(string path, bool defaultValue)
        {
            if (_foldouts.TryGetValue(path, out bool open))
            {
                return open;
            }

            _foldouts[path] = defaultValue;
            return defaultValue;
        }

        private static JToken CreateEmptyLike(JToken source)
        {
            return source.Type switch
            {
                JTokenType.Object => new JObject(),
                JTokenType.Array => new JArray(),
                JTokenType.Integer => new JValue(0L),
                JTokenType.Float => new JValue(0d),
                JTokenType.Boolean => new JValue(false),
                JTokenType.Null => JValue.CreateNull(),
                _ => new JValue(string.Empty)
            };
        }
    }
}
#endif
