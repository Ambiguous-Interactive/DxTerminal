namespace WallstopStudios.DxCommandTerminal.Editor.CustomEditors
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Themes;
    using UnityEditor;
    using UnityEngine;

    [CustomEditor(typeof(TerminalThemeAsset))]
    [CanEditMultipleObjects]
    internal sealed class TerminalThemeAssetEditor : UnityEditor.Editor
    {
        private const string ConvertMenu = "Assets/DxTerminal/Convert Legacy Theme";

        private static readonly GUIContent RolesLabel = new("Color roles");
        private static readonly string[] LegacyExclusions = { TerminalThemeAsset.SemanticFlagPropertyName, TerminalThemeAsset.RolesPropertyName };

        private List<string> _contrastWarnings;

        [MenuItem("Assets/Create/Wallstop Studios/DxTerminal/Theme Asset", false, 1_111_124)]
        private static void CreateAsset()
        {
            ProjectWindowUtil.CreateAsset(TerminalThemeAsset.CreateSemantic(), "NewTerminalTheme.asset");
        }

        [MenuItem(ConvertMenu, true)]
        private static bool CanConvertLegacyTheme()
        {
            return Selection.objects.Length == 1
                && Selection.activeObject is TerminalThemeAsset asset
                && asset != null
                && !asset.UsesSemanticRoles
                && AssetDatabase.IsMainAsset(asset);
        }

        [MenuItem(ConvertMenu)]
        private static void ConvertLegacyTheme()
        {
            if (!CanConvertLegacyTheme())
            {
                return;
            }

            TerminalThemeAsset source = (TerminalThemeAsset)Selection.activeObject;
            string sourcePath = AssetDatabase.GetAssetPath(source);
            // Installed packages can be read-only. Put copies in the project's Assets folder.
            string folder = sourcePath.StartsWith("Assets/", StringComparison.Ordinal) ? sourcePath[..sourcePath.LastIndexOf('/')] : "Assets";
            string stem = $"{folder}/{Path.GetFileNameWithoutExtension(sourcePath)} Semantic";
            string assetPath = stem + ".asset";
            for (int suffix = 1; !IsAvailable(assetPath) || !IsAvailable(Path.ChangeExtension(assetPath, ".uss")); ++suffix)
            {
                assetPath = FormattableString.Invariant($"{stem} {suffix}.asset");
            }

            TerminalThemeAsset converted = source.CreateSemanticCopy();
            converted.name = Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(converted, assetPath);
            Selection.activeObject = converted;
        }

        private static bool IsAvailable(string path)
        {
            return !File.Exists(path) && string.Equals(path, AssetDatabase.GenerateUniqueAssetPath(path), StringComparison.Ordinal);
        }

        public override void OnInspectorGUI()
        {
            TerminalThemeAsset asset = (TerminalThemeAsset)target;
            bool valuesUpdated = serializedObject.UpdateIfRequiredOrScript();
            SerializedProperty mode = serializedObject.FindProperty(TerminalThemeAsset.SemanticFlagPropertyName);
            if (mode.hasMultipleDifferentValues)
            {
                EditorGUILayout.HelpBox("Select themes that use the same color fields.", MessageType.Info);
                return;
            }

            if (!asset.UsesSemanticRoles)
            {
                _contrastWarnings = null;
                DrawPropertiesExcluding(serializedObject, LegacyExclusions);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            SerializedProperty roles = serializedObject.FindProperty(TerminalThemeAsset.RolesPropertyName);
            EditorGUILayout.PropertyField(roles, RolesLabel, true);
            bool valuesEdited = serializedObject.ApplyModifiedProperties();
            if (serializedObject.isEditingMultipleObjects)
            {
                EditorGUILayout.HelpBox("Select one theme to check its contrast.", MessageType.Info);
                return;
            }

            if (_contrastWarnings == null || valuesUpdated || valuesEdited)
            {
                UpdateContrastWarnings(asset.ReadRoleColors());
            }

            foreach (string warning in _contrastWarnings)
            {
                EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }

            if (_contrastWarnings.Count == 0)
            {
                EditorGUILayout.HelpBox("Contrast passes on black, mid gray, and white backdrops.", MessageType.Info);
            }
        }

        private void UpdateContrastWarnings(IReadOnlyDictionary<string, Color> colors)
        {
            _contrastWarnings ??= new List<string>();
            _contrastWarnings.Clear();
            Color surface = colors["surface"];
            foreach ((string foreground, string background, double minimum) in ThemeContrast.Pairs)
            {
                Color? layer = string.Equals(background, "surface", StringComparison.Ordinal) ? null : colors[background];
                (double ratio, string backdrop) = ThemeContrast.Measure(colors[foreground], surface, layer);
                if (ratio < minimum)
                {
                    _contrastWarnings.Add($"{foreground} on {background} over {backdrop} gives {ratio:F2}:1. Minimum is {minimum:F1}:1.");
                }
            }
        }
    }
}
