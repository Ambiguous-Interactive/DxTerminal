namespace WallstopStudios.DxCommandTerminal.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using Themes;
    using UnityEngine;
#if UNITY_EDITOR
    using System.IO;
    using UnityEditor;
    using UnityEngine.UIElements;
#endif

    public sealed class TerminalThemeConversionTests
    {
#if UNITY_EDITOR
        [Test]
        public void LegacyThemeConversionRepairsAWritableRole()
        {
            TerminalThemeAsset source = ScriptableObject.CreateInstance<TerminalThemeAsset>();
            TerminalThemeAsset defaults = TerminalThemeAsset.CreateSemantic();
            TerminalThemeAsset converted = null;
            try
            {
                IReadOnlyDictionary<string, Color> roles = defaults.ReadRoleColors();
                using (SerializedObject serialized = new(source))
                {
                    serialized.FindProperty("_terminalBg").colorValue = roles["surface"];
                    serialized.FindProperty("_buttonBg").colorValue = roles["surface-raised"];
                    serialized.FindProperty("_buttonHoverBg").colorValue = roles["hover"];
                    serialized.FindProperty("_inputFieldBg").colorValue = roles["input"];
                    serialized.FindProperty("_inputTextColor").colorValue = roles["ink"];
                    serialized.FindProperty("_textMessage").colorValue = roles["ink-body"];
                    serialized.FindProperty("_textShell").colorValue = new Color(0.2f, 0.2f, 0.2f, 1f);
                    serialized.FindProperty("_caretColor").colorValue = roles["accent"];
                    serialized.FindProperty("_textInputEcho").colorValue = roles["accent-soft"];
                    serialized.FindProperty("_buttonSelectedBg").colorValue = roles["selection"];
                    serialized.FindProperty("_textWarning").colorValue = roles["warning"];
                    serialized.FindProperty("_textError").colorValue = roles["error-text"];
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                converted = source.CreateSemanticCopy();
                IReadOnlyDictionary<string, Color> colors = converted.ReadRoleColors();
                (double ratio, string backdrop) = ThemeContrast.Measure(colors["ink-muted"], colors["surface"], colors["input"]);
                Assert.That(ratio, Is.GreaterThanOrEqualTo(4.5), backdrop);
                Assert.That(source.BuildUss(), Does.Contain("--text-shell: rgba(51, 51, 51, 1);"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(converted);
                UnityEngine.Object.DestroyImmediate(defaults);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void AssetsMenuConvertsAndImportsACopyWithoutReplacingExistingFiles()
        {
            string folder = $"Assets/DxTerminalThemeTest-{Guid.NewGuid():N}";
            UnityEngine.Object[] previousSelection = Selection.objects;
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            try
            {
                string originalPath = folder + "/Legacy.asset";
                string originalSheetPath = Path.ChangeExtension(originalPath, ".uss");
                TerminalThemeAsset source = ScriptableObject.CreateInstance<TerminalThemeAsset>();
                source.name = "Legacy";
                using (SerializedObject serialized = new(source))
                {
                    serialized.FindProperty("_textError").colorValue = new Color(31f / 255f, 63f / 255f, 127f / 255f, 0.45f);
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                AssetDatabase.CreateAsset(source, originalPath);
                byte[] originalAsset = File.ReadAllBytes(originalPath);
                string originalSheet = File.ReadAllText(originalSheetPath);
                string occupiedSheetPath = folder + "/Legacy Semantic.uss";
                const string handWritten = ".custom-theme { color: red; }";
                File.WriteAllText(occupiedSheetPath, handWritten);
                AssetDatabase.ImportAsset(occupiedSheetPath, ImportAssetOptions.ForceSynchronousImport);
                string occupiedAssetPath = folder + "/Legacy Semantic 1.asset";
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<TerminalThemePack>(), occupiedAssetPath);
                string occupiedAssetGuid = AssetDatabase.AssetPathToGUID(occupiedAssetPath);

                Selection.activeObject = source;
                Assert.That(EditorApplication.ExecuteMenuItem("Assets/DxTerminal/Convert Legacy Theme"), Is.True);

                string convertedPath = AssetDatabase.GetAssetPath(Selection.activeObject);
                Assert.That(convertedPath, Is.EqualTo(folder + "/Legacy Semantic 2.asset"));
                AssetDatabase.ImportAsset(convertedPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                TerminalThemeAsset converted = AssetDatabase.LoadAssetAtPath<TerminalThemeAsset>(convertedPath);
                Assert.That(converted != null, Is.True);
                Assert.That(converted.UsesSemanticRoles, Is.True);

                string convertedSheetPath = Path.ChangeExtension(convertedPath, ".uss");
                string convertedSheet = File.ReadAllText(convertedSheetPath);
                Assert.That(convertedSheet, Is.EqualTo(converted.BuildUss()));
                Assert.That(convertedSheet, Does.Contain(".legacy-semantic-2-theme"));
                Assert.That(convertedSheet, Does.Contain("--dxt-error-text: rgba(31, 63, 127, 0.45);"));
                Assert.That(AssetDatabase.LoadAssetAtPath<StyleSheet>(convertedSheetPath) != null, Is.True);
                Assert.That(source.UsesSemanticRoles, Is.False);
                Assert.That(File.ReadAllBytes(originalPath), Is.EqualTo(originalAsset));
                Assert.That(File.ReadAllText(originalSheetPath), Is.EqualTo(originalSheet));
                Assert.That(File.ReadAllText(occupiedSheetPath), Is.EqualTo(handWritten));
                Assert.That(AssetDatabase.AssetPathToGUID(occupiedAssetPath), Is.EqualTo(occupiedAssetGuid));
                Assert.That(AssetDatabase.LoadAssetAtPath<TerminalThemePack>(occupiedAssetPath) != null, Is.True);
            }
            finally
            {
                Selection.objects = previousSelection;
                AssetDatabase.DeleteAsset(folder);
            }
        }
#endif

        [Test]
        public void LegacyColorsFeedSemanticRolesAndNewRolesKeepTheirDefaults()
        {
            TerminalThemeAsset source = ScriptableObject.CreateInstance<TerminalThemeAsset>();
            TerminalThemeAsset defaults = TerminalThemeAsset.CreateSemantic();
            TerminalThemeAsset converted = source.CreateSemanticCopy();
            try
            {
                string legacyUss = source.BuildUss();
                IReadOnlyDictionary<string, Color> roles = converted.ReadRoleColors();
                Dictionary<string, string> legacyVariables = new(StringComparer.Ordinal)
                {
                    ["surface"] = "terminal-bg",
                    ["surface-raised"] = "button-bg",
                    ["input"] = "input-field-bg",
                    ["selection"] = "button-selected-bg",
                    ["hover"] = "button-hover-bg",
                    ["ink"] = "input-text-color",
                    ["ink-body"] = "text-message",
                    ["ink-muted"] = "text-shell",
                    ["accent"] = "caret-color",
                    ["accent-soft"] = "text-input-echo",
                    ["warning"] = "text-warning",
                    ["error-text"] = "text-error",
                };
                string convertedUss = converted.BuildUss();
                IReadOnlyDictionary<string, Color> defaultColors = defaults.ReadRoleColors();
                Assert.That(roles.Keys, Is.EquivalentTo(defaultColors.Keys));
                foreach (KeyValuePair<string, Color> role in roles)
                {
                    if (legacyVariables.TryGetValue(role.Key, out string variable))
                    {
                        Match originalColor = Regex.Match(legacyUss, $"--{variable}: (rgba[^;]+);");
                        Assert.That(originalColor.Success, Is.True, variable);
                        Assert.That(convertedUss, Does.Contain($"--dxt-{role.Key}: {originalColor.Groups[1].Value};"), role.Key);
                    }
                    else
                    {
                        Assert.That(role.Value, Is.EqualTo(defaultColors[role.Key]), role.Key);
                    }
                }

                Assert.That(converted.UsesSemanticRoles, Is.True);
                Assert.That(source.UsesSemanticRoles, Is.False);
                Assert.That(source.BuildUss(), Is.EqualTo(legacyUss));
            }
            finally
            {
#if UNITY_EDITOR
                UnityEngine.Object.DestroyImmediate(converted);
                UnityEngine.Object.DestroyImmediate(defaults);
                UnityEngine.Object.DestroyImmediate(source);
#endif
            }
        }
    }
}
