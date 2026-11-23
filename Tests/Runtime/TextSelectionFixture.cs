namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using UnityEngine.UIElements;
#if UNITY_2022_3
    using System;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEngine;
#endif


    internal static class TextSelectionFixture
    {
#if UNITY_2022_3
        private static readonly PropertyInfo TextHandle = typeof(TextElement).GetProperty("uitkTextHandle", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo UpdateText = TextHandle?.PropertyType.GetMethod("Update", Type.EmptyTypes);
#endif

        internal static void PrepareCaret(TextField field)
        {
#if UNITY_2022_3
            if (!Application.isBatchMode)
            {
                return;
            }

            /*
                Unity 2022.3 clamps selection reads to TextCore's generated
                character count. Unpainted batch-mode fields can retain
                zero characters even with a nonempty value and loaded font.
             */
            TextElement text = field.Q("unity-text-input")?.Q<TextElement>();
            Assert.That(text, Is.Not.Null, "The field exposes an editable TextElement");
            Assert.That(UpdateText, Is.Not.Null, "Unity 2022.3 exposes TextCore's update method");
            UpdateText.Invoke(TextHandle.GetValue(text), Array.Empty<object>());
#endif
        }
    }
}
