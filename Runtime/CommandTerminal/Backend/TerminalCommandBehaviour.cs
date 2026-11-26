namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    ///     Base component for scene-scoped builder commands: registers commands
    ///     in <see cref="OnEnable"/> and disposes every registration handle in
    ///     <see cref="OnDisable"/> so disabling or destroying the component
    ///     removes only its own commands.
    /// </summary>
    public abstract class TerminalCommandBehaviour : MonoBehaviour
    {
        private readonly List<CommandRegistrationHandle> _handles = new();

        protected virtual void OnEnable()
        {
            RegisterCommands();
        }

        protected virtual void OnDisable()
        {
            foreach (CommandRegistrationHandle handle in _handles)
            {
                handle.Dispose();
            }

            _handles.Clear();
        }

        protected void Register(CommandBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            CommandShell shell = Terminal.Shell;
            if (shell == null)
            {
                return;
            }

            if (shell.AddCommand(builder, out CommandRegistrationHandle handle))
            {
                _handles.Add(handle);
            }
        }

        protected abstract void RegisterCommands();
    }
}
