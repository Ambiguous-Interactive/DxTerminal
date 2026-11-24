namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Allocation
{
    using System;
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine.TestTools;

    /// <summary>
    ///     Contract tests for the allocation instrumentation itself: the
    ///     positive control, the no-op control, unavailable instruments,
    ///     throwing subjects, and the unvalidated-window verdict. A red here
    ///     invalidates every allocation claim built on top.
    /// </summary>
    public sealed class AllocationProbeTests
    {
        private const int ControlStringLength = 40;
        private const int ControlBatchSize = 256;

        private static string _controlString;

        private static void AllocateOneString()
        {
            _controlString = new string('x', ControlStringLength);
        }

        private static void AllocateStringBatch()
        {
            for (int index = 0; index < ControlBatchSize; ++index)
            {
                AllocateOneString();
            }
        }

        [TearDown]
        public void TearDown()
        {
            AllocationProbe.ControlDetectionOverride = null;
            AllocationProbe.ResetValidation();
        }

        [Test]
        public void PositiveControlDetectsForcedAllocations()
        {
            AllocationMeasurement measurement = AllocationAssertions.AssertDetectsAllocation("positive control", AllocationAssertions.ForceControlAllocation);

            /*
                The detection verdict and the byte reader are separate
                capabilities: a working detector does not establish byte
                accuracy, so the byte reading must agree with its capability.
            */
            if (AllocationProbe.ByteCapabilityValid)
            {
                Assert.IsTrue(0 < measurement.AllocatedBytes, "A validated byte reader must see the forced allocation: " + measurement.Describe());
                return;
            }

            Assert.AreEqual(AllocationMeasurement.Unavailable, measurement.AllocatedBytes, "An unvalidated byte reader must report unavailability, never a trusted zero");
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator ByteReaderSeparatesKnownStringsFromOutOfWindowAllocations()
        {
            Action noOp = static () => { };
            Action singleAllocation = AllocateOneString;
            Action batchAllocation = AllocateStringBatch;
            for (int index = 0; index < AllocationAssertions.DefaultWarmupIterations; ++index)
            {
                noOp();
                singleAllocation();
                batchAllocation();
            }

            using ProfilerAllocationTrace trace = new ProfilerAllocationTrace(3);
            yield return null;

            batchAllocation();
            trace.Record(noOp);
            trace.Record(singleAllocation);
            trace.Record(batchAllocation);
            yield return trace.WaitForSamples(3);

            (long quiet, int quietEvents) = trace.Samples[0];
            (long single, int singleEvents) = trace.Samples[1];
            (long batch, int batchEvents) = trace.Samples[2];
            TestContext.Out.WriteLine($"T11 allocation controls: quiet={quiet}, one={single}, {ControlBatchSize} strings={batch}");
            Assert.That(quietEvents, Is.Zero);
            Assert.That(quiet, Is.Zero, "Out-of-window allocations must leave the byte count at zero");
            Assert.That(singleEvents, Is.EqualTo(1));
            Assert.That(single, Is.GreaterThanOrEqualTo(ControlStringLength * sizeof(char)));
            Assert.That(batchEvents, Is.EqualTo(ControlBatchSize));
            Assert.That(batch, Is.EqualTo(single * ControlBatchSize));
        }
#endif

        [Test]
        public void DefaultMeasurementCannotApproveZeroClaims()
        {
            AllocationMeasurement measurement = default;
            Assert.AreEqual(default(AllocationMeasurementStatus), measurement.Status);
            Assert.IsFalse(measurement.IsZeroAllocation);
            Assert.That(measurement.Describe(), Does.Contain("unvalidated"));
        }

        [Test]
        public void NoOpSubjectMeasuresZero()
        {
            AllocationAssertions.AssertZeroAllocations("no-op control", () => { });
        }

        [Test]
        public void UnvalidatedWindowCannotApproveZeroClaims()
        {
            AllocationProbe.ResetValidation();
            try
            {
                AllocationMeasurement measurement = AllocationProbe.Measure(() => { });
                Assert.IsFalse(measurement.IsZeroAllocation, $"An unvalidated window must never map to zero: {measurement.Describe()}");
                Assert.AreEqual(AllocationMeasurementStatus.InstrumentInvalid, measurement.Status, "An unvalidated window reports the invalid verdict");
            }
            finally
            {
                AllocationProbe.ResetValidation();
            }

            AllocationAssertions.AssertZeroAllocations("recovery after unvalidated window", () => { });
        }

        [Test]
        public void UnavailableInstrumentCannotApproveZeroClaims()
        {
            AllocationProbe.ControlDetectionOverride = () => false;
            AllocationProbe.ResetValidation();
            try
            {
                Assert.Catch<AssertionException>(() => AllocationAssertions.EnsureInstrumentValidated(), "A required verdict must fail when the instrument cannot measure");
                Assert.Catch<AssertionException>(
                    () => AllocationAssertions.AssertZeroAllocations("sealed window", () => { }),
                    "An inactive instrument must not create a green zero claim"
                );
            }
            finally
            {
                AllocationProbe.ControlDetectionOverride = null;
                AllocationProbe.ResetValidation();
            }

            AllocationAssertions.AssertZeroAllocations("recovery after unavailable instrument", () => { });
        }

        [Test]
        public void ThrowingSubjectPropagatesAndKeepsInstrumentUsable()
        {
            AllocationAssertions.EnsureInstrumentValidated();
            Assert.Throws<InvalidOperationException>(
                () => AllocationProbe.Measure(() => throw new InvalidOperationException("subject failure")),
                "The subject's exception must reach the caller"
            );

            AllocationAssertions.AssertZeroAllocations("recovery after throw", () => { });
        }
    }
}
