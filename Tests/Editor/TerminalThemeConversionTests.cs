namespace WallstopStudios.DxCommandTerminal.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using Themes;
    using UnityEngine;

    public sealed class TerminalThemeConversionTests
    {
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
