namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using UnityEngine;
    using Object = UnityEngine.Object;

    /*
        T04 inspector fixture host: renders a real UnityEditor.Editor's
        OnInspectorGUI into the capture render target through an
        InspectorDrawSurface component on the fixture object, so the package's
        custom editors (TerminalUI/theme/font pack) paint with the rest of the
        harness. The capture shows the editors' control surface, not the
        inspector window chrome; the drawn controls receive no input events
        and keep their defaults. The fixture requires a visible Game View.
     */
    public static class EditorInspectorCapture
    {
        public static bool IsSupported
        {
            get
            {
#if UNITY_EDITOR
                return !Application.isBatchMode;
#else
                return false;
#endif
            }
        }

        public static string UnsupportedReason
        {
            get { return "Inspector capture requires an interactive Unity Editor with a visible Game View."; }
        }

        /*
            Creates an editor for target and attaches an InspectorDrawSurface
            to the fixture object that paints it into renderTarget. Dispose
            removes the component and destroys the editor.
         */
        public static InspectorScope Attach(GameObject host, Object target, RenderTexture renderTarget, float width, float height)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

#if UNITY_EDITOR
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (renderTarget == null)
            {
                throw new ArgumentNullException(nameof(renderTarget));
            }

            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
            InspectorDrawSurface surface = host.AddComponent<InspectorDrawSurface>();
            surface.Begin(editor, renderTarget, width, height);
            return new InspectorScope(surface, editor);
#else
            return new InspectorScope();
#endif
        }

        public sealed class InspectorScope : IDisposable
        {
#if UNITY_EDITOR
            private readonly InspectorDrawSurface _surface;
            private readonly UnityEditor.Editor _editor;

            internal InspectorScope(InspectorDrawSurface surface, UnityEditor.Editor editor)
            {
                _surface = surface;
                _editor = editor;
            }

            public void Dispose()
            {
                if (_surface != null)
                {
                    _surface.End();
                    Object.Destroy(_surface);
                }

                if (_editor != null)
                {
                    UnityEditor.Editor.DestroyImmediate(_editor);
                }
            }
#else
            internal InspectorScope() { }

            public void Dispose() { }
#endif
        }
    }
}
