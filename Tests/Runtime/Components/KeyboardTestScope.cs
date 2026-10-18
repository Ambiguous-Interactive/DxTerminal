namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Components
{
#if ENABLE_INPUT_SYSTEM
    using System;
    using System.Collections.Generic;
    using Input;
    using NUnit.Framework;
    using UnityEngine.InputSystem;

    internal sealed class KeyboardTestScope : IDisposable
    {
        private readonly List<Snapshot> _originals = new();
        private readonly List<Keyboard> _owned = new();
        private readonly Keyboard _originalCurrent;
        private readonly InputSettings _originalSettings;
        private readonly InputSettings _testSettings;

        internal KeyboardTestScope()
        {
            _originalCurrent = Keyboard.current;
            _originalSettings = InputSystem.settings;
            _testSettings = UnityEngine.Object.Instantiate(_originalSettings);
            foreach (InputDevice device in InputSystem.devices)
            {
                if (device is Keyboard keyboard)
                {
                    _originals.Add(new Snapshot(keyboard));
                }
            }
            foreach (Snapshot snapshot in _originals)
            {
                InputSystem.RemoveDevice(snapshot.Device);
            }
            // Queued test input must reach runtime polls while the editor is unfocused.
            _testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _testSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = _testSettings;
            AddKeyboard();
            InputHelpers.ResetControlMemoForTesting();
        }

        public void Dispose()
        {
            foreach (Keyboard keyboard in _owned)
            {
                InputSystem.RemoveDevice(keyboard);
            }
            _owned.Clear();
            InputSystem.settings = _originalSettings;
            UnityEngine.Object.Destroy(_testSettings);
            foreach (Snapshot snapshot in _originals)
            {
                InputSystem.AddDevice(snapshot.Device);
                if (snapshot.Enabled)
                {
                    InputSystem.EnableDevice(snapshot.Device);
                }
                else
                {
                    InputSystem.DisableDevice(snapshot.Device);
                }
            }
            _originalCurrent?.MakeCurrent();
            InputHelpers.ResetControlMemoForTesting();
            Assert.That(InputSystem.settings, Is.SameAs(_originalSettings));
            Assert.That(Keyboard.current, Is.SameAs(_originalCurrent));
            foreach (Snapshot snapshot in _originals)
            {
                Assert.That(snapshot.Device.deviceId, Is.EqualTo(snapshot.DeviceId));
                Assert.That(snapshot.Device.enabled, Is.EqualTo(snapshot.Enabled));
            }
        }

        internal void AddKeyboard()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            _owned.Add(keyboard);
            InputSystem.EnableDevice(keyboard);
            keyboard.MakeCurrent();
        }

        internal void RemoveKeyboards()
        {
            foreach (Keyboard keyboard in _owned)
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }

        private readonly struct Snapshot
        {
            internal readonly Keyboard Device;
            internal readonly int DeviceId;
            internal readonly bool Enabled;

            internal Snapshot(Keyboard device)
            {
                Device = device;
                DeviceId = device.deviceId;
                Enabled = device.enabled;
            }
        }
    }
#endif
}
