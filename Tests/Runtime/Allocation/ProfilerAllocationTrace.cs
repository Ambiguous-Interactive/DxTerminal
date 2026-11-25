namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Allocation
{
#if UNITY_EDITOR
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using NUnit.Framework;
    using Unity.Profiling;
    using UnityEditor.Profiling;
    using UnityEditorInternal;
    using UnityEngine.Profiling;

    internal sealed class ProfilerAllocationTrace : IDisposable
    {
        private const string AllocationMarkerName = "GC.Alloc";
        private static readonly TimeSpan PublicationTimeout = TimeSpan.FromSeconds(5);

        public IReadOnlyList<(long Bytes, int Events)> Samples
        {
            get { return _samples; }
        }

        private readonly List<(long Bytes, int Events)> _samples;
        private readonly bool _wasEnabled;
        private readonly bool _wasCallstackEnabled;
        private readonly List<ulong> _callstack;
        private readonly string _readyName;
        private readonly string _windowName;
        private readonly ProfilerMarker _ready;
        private readonly ProfilerMarker _window;

        public ProfilerAllocationTrace(int capacity, bool captureLargeAllocationStacks = false)
        {
            string traceName = $"DxTerminal.AllocationTest.{Guid.NewGuid():N}";
            _readyName = traceName + ".Ready";
            _windowName = traceName + ".Window";
            _ready = new ProfilerMarker(ProfilerCategory.Scripts, _readyName);
            _window = new ProfilerMarker(ProfilerCategory.Scripts, _windowName);
            _samples = new List<(long Bytes, int Events)>(capacity);
            _wasEnabled = ProfilerDriver.enabled;
            _wasCallstackEnabled = Profiler.enableAllocationCallstacks;
            if (captureLargeAllocationStacks)
            {
                _callstack = new List<ulong>();
                Profiler.enableAllocationCallstacks = true;
            }

            ProfilerDriver.enabled = true;
        }

        private static string DescribeHistory()
        {
            return $"Profiler enabled: {ProfilerDriver.enabled}. Retained frames: {ProfilerDriver.firstFrameIndex} to {ProfilerDriver.lastFrameIndex}.";
        }

        public void Dispose()
        {
            ProfilerDriver.enabled = _wasEnabled;
            if (_callstack != null)
            {
                Profiler.enableAllocationCallstacks = _wasCallstackEnabled;
            }
        }

        public void Record(Action subject)
        {
            using (_window.Auto())
            {
                subject();
            }
        }

        public IEnumerator WaitUntilReady()
        {
            Stopwatch timeout = Stopwatch.StartNew();
            int frames = 0;
            do
            {
                _ready.Begin();
                _ready.End();
                yield return null;
                ++frames;
                ReadAvailable(_readyName);
                if (0 < _samples.Count)
                {
                    TestContext.Out.WriteLine($"Allocation profiler ready after {frames} frames ({timeout.ElapsedMilliseconds} ms)");
                    _samples.Clear();
                    yield break;
                }
            } while (timeout.Elapsed < PublicationTimeout);

            Assert.Fail($"The profiler did not publish a warm-up marker after {frames} frames. {DescribeHistory()}");
        }

        public IEnumerator WaitForSamples(int expected)
        {
            Stopwatch timeout = Stopwatch.StartNew();
            do
            {
                yield return null;
                ReadAvailable(_windowName);
                if (expected <= _samples.Count)
                {
                    break;
                }
            } while (timeout.Elapsed < PublicationTimeout);

            Assert.That(_samples.Count, Is.EqualTo(expected), $"The profiler must retain every marked window. {DescribeHistory()}");
        }

        private void ReadAvailable(string markerName)
        {
            _samples.Clear();
            int firstFrame = ProfilerDriver.firstFrameIndex;
            int lastFrame = ProfilerDriver.lastFrameIndex;
            // The newest frame may still receive samples.
            for (int index = Math.Max(0, firstFrame); index < lastFrame; ++index)
            {
                using RawFrameDataView frame = ProfilerDriver.GetRawFrameDataView(index, 0);
                if (!frame.valid)
                {
                    continue;
                }

                ReadFrame(frame, markerName);
            }
        }

        private void ReadFrame(RawFrameDataView frame, string markerName)
        {
            int windowId = frame.GetMarkerId(markerName);
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
                        long allocated = frame.GetSampleMetadataAsLong(child, 0);
                        bytes += allocated;
                        ++events;
                        if (_callstack != null && 1024 <= allocated)
                        {
                            _callstack.Clear();
                            frame.GetSampleCallstack(child, _callstack);
                            TestContext.Out.WriteLine($"T11 allocation in sample {_samples.Count}: {allocated} B");
                            foreach (ulong address in _callstack)
                            {
                                TestContext.Out.WriteLine(frame.ResolveMethodInfo(address).methodName);
                            }
                        }
                    }
                }

                _samples.Add((bytes, events));
                index = end - 1;
            }
        }
    }
#endif
}
