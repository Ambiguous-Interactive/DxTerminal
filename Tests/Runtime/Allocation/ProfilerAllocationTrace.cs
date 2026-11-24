namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Allocation
{
#if UNITY_EDITOR
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using Unity.Profiling;
    using UnityEditor.Profiling;
    using UnityEditorInternal;

    internal sealed class ProfilerAllocationTrace : IDisposable
    {
        private const string WindowName = "DxTerminal.AllocationTest.Window";
        private const string AllocationMarkerName = "GC.Alloc";
        private const int FlushFrameBudget = 60;

        private static readonly ProfilerMarker Window = new ProfilerMarker(ProfilerCategory.Scripts, WindowName);

        public IReadOnlyList<(long Bytes, int Events)> Samples
        {
            get { return _samples; }
        }

        private readonly List<(long Bytes, int Events)> _samples;
        private readonly bool _wasEnabled;
        private readonly int _firstFrame;

        public ProfilerAllocationTrace(int capacity)
        {
            _samples = new List<(long Bytes, int Events)>(capacity);
            _wasEnabled = ProfilerDriver.enabled;
            ProfilerDriver.enabled = true;
            _firstFrame = ProfilerDriver.lastFrameIndex + 1;
        }

        public void Dispose()
        {
            ProfilerDriver.enabled = _wasEnabled;
        }

        public void Record(Action subject)
        {
            using (Window.Auto())
            {
                subject();
            }
        }

        public IEnumerator WaitForSamples(int expected)
        {
            for (int index = 0; index < FlushFrameBudget; ++index)
            {
                yield return null;
                ReadAvailable();
                if (expected <= _samples.Count)
                {
                    break;
                }
            }

            Assert.That(_samples.Count, Is.EqualTo(expected), "The profiler must retain every marked window");
        }

        private void ReadAvailable()
        {
            _samples.Clear();
            int firstFrame = Math.Max(_firstFrame, ProfilerDriver.firstFrameIndex);
            int lastFrame = ProfilerDriver.lastFrameIndex;
            // The newest frame may still receive samples.
            for (int index = firstFrame; index < lastFrame; ++index)
            {
                using RawFrameDataView frame = ProfilerDriver.GetRawFrameDataView(index, 0);
                if (!frame.valid)
                {
                    continue;
                }

                ReadFrame(frame);
            }
        }

        private void ReadFrame(RawFrameDataView frame)
        {
            int windowId = frame.GetMarkerId(WindowName);
            if (windowId == FrameDataView.invalidMarkerId)
            {
                return;
            }

            int allocationId = frame.GetMarkerId(AllocationMarkerName);
            int sampleCount = frame.sampleCount;
            for (int index = 0; index < sampleCount; ++index)
            {
                if (frame.GetSampleMarkerId(index) != windowId)
                {
                    continue;
                }

                int end = index + 1 + frame.GetSampleChildrenCountRecursive(index);
                long bytes = 0;
                int events = 0;
                for (int child = index + 1; child < end; ++child)
                {
                    if (frame.GetSampleMarkerId(child) == allocationId)
                    {
                        bytes += frame.GetSampleMetadataAsLong(child, 0);
                        ++events;
                    }
                }

                _samples.Add((bytes, events));
                index = end - 1;
            }
        }
    }
#endif
}
