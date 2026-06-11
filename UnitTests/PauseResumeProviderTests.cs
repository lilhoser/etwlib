/*
Licensed to the Apache Software Foundation (ASF) under one
or more contributor license agreements.  See the NOTICE file
distributed with this work for additional information
regarding copyright ownership.  The ASF licenses this file
to you under the Apache License, Version 2.0 (the
"License"); you may not use this file except in compliance
with the License.  You may obtain a copy of the License at

  http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing,
software distributed under the License is distributed on an
"AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
KIND, either express or implied.  See the License for the
specific language governing permissions and limitations
under the License.
*/
using etwlib;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static etwlib.NativeTraceConsumer;
using static etwlib.NativeTraceControl;

namespace UnitTests
{
    using static Shared;

    [TestClass]
    public class PauseResumeProviderTests
    {
        //
        // Phase 0: count events until the pre-pause threshold is reached.
        // Phase 1: providers paused; after a settle period that lets in-flight
        //          kernel buffers and the ~1s flush timer drain, any event
        //          observed proves the pause leaked.
        // Phase 2: providers resumed; events must flow again.
        //
        [TestMethod]
        public void PauseStopsEventFlowAndResumeRestoresIt()
        {
            ConfigureLoggers();

            //
            // Deterministic event source — every OpenSCManager call emits RPC
            // client-call events from this process, so the test never waits on
            // ambient machine activity.
            //
            using var stimulus = new RpcStimulus();

            using var trace = new RealTimeTrace("Unit Test Real-Time Tracing");
            using var parserBuffers = new EventParserBuffers();

            trace.AddProvider(
                s_RpcEtwGuid, "RPC", EventTraceLevel.Information, 0xFFFFFFFFFFFFFFFF, 0);
            trace.Start();

            var phase = 0;
            int prePauseCount = 0, pausedCount = 0, resumedCount = 0;
            bool pauseResult = false, resumeResult = false;
            string? coordinatorFailure = null;

            //
            // Overall watchdog: whatever goes wrong, the trace is stopped so
            // ProcessTrace unblocks and the test fails on assertions instead of
            // hanging the run.
            //
            var stopwatch = Stopwatch.StartNew();
            var overallDeadline = TimeSpan.FromSeconds(150);
            using var watchdog = new Timer(_ =>
            {
                if (stopwatch.Elapsed > overallDeadline)
                {
                    trace.Stop();
                }
            }, null, 1000, 1000);

            var coordinator = Task.Run(() =>
            {
                try
                {
                    if (!WaitFor(() => Volatile.Read(ref prePauseCount) >= s_FilteredNumEvents,
                            TimeSpan.FromSeconds(60)))
                    {
                        coordinatorFailure = "pre-pause event threshold was never reached";
                        return;
                    }

                    pauseResult = trace.PauseProviders();

                    //
                    // Settle: events already in kernel-side session buffers (and
                    // anything the ~1s realtime flush timer delivers) drain out
                    // before the leak-check window opens.
                    //
                    Thread.Sleep(2000);
                    Volatile.Write(ref phase, 1);
                    Thread.Sleep(3000);

                    Volatile.Write(ref phase, 2);
                    resumeResult = trace.ResumeProviders();

                    if (!WaitFor(() => Volatile.Read(ref resumedCount) >= s_FilteredNumEvents,
                            TimeSpan.FromSeconds(60)))
                    {
                        coordinatorFailure = "no events resumed after ResumeProviders()";
                    }
                }
                catch (Exception ex)
                {
                    coordinatorFailure = $"coordinator exception: {ex.Message}";
                }
                finally
                {
                    trace.Stop();
                }
            });

            trace.Consume(
                new EventRecordCallback((Event) =>
                {
                    var evt = (EVENT_RECORD)Marshal.PtrToStructure(
                        Event, typeof(EVENT_RECORD))!;
                    var parser = new EventParser(evt, parserBuffers, trace.GetPerfFreq());
                    ParsedEtwEvent? parsedEvent = null;
                    try
                    {
                        parsedEvent = parser.Parse();
                    }
                    catch
                    {
                        return;
                    }

                    if (parsedEvent == null)
                    {
                        return;
                    }

                    switch (Volatile.Read(ref phase))
                    {
                        case 0:
                            Interlocked.Increment(ref prePauseCount);
                            break;
                        case 1:
                            Interlocked.Increment(ref pausedCount);
                            break;
                        default:
                            Interlocked.Increment(ref resumedCount);
                            break;
                    }
                }),
                new BufferCallback((LogFile) => 1));

            //
            // The coordinator must have finished before its results are read —
            // a still-running coordinator would race the assertions below on
            // pauseResult/resumeResult/coordinatorFailure.
            //
            Assert.IsTrue(coordinator.Wait(TimeSpan.FromSeconds(10)),
                "coordinator did not complete after the trace ended");

            Assert.IsNull(coordinatorFailure, coordinatorFailure);
            Assert.IsTrue(pauseResult, "PauseProviders() reported failure");
            Assert.IsTrue(resumeResult, "ResumeProviders() reported failure");
            Assert.IsTrue(prePauseCount >= s_FilteredNumEvents,
                $"only {prePauseCount}/{s_FilteredNumEvents} events before pause");
            Assert.AreEqual(0, pausedCount,
                $"{pausedCount} event(s) observed while providers were paused — " +
                "the kernel-side disable leaked");
            Assert.IsTrue(resumedCount >= s_FilteredNumEvents,
                $"only {resumedCount}/{s_FilteredNumEvents} events after resume");
        }

        [TestMethod]
        public void PauseAndResumeFailCleanlyWhenSessionNotStarted()
        {
            ConfigureLoggers();
            using var trace = new RealTimeTrace("Unit Test Pause Without Start");
            trace.AddProvider(
                s_RpcEtwGuid, "RPC", EventTraceLevel.Information, 0xFFFFFFFFFFFFFFFF, 0);

            //
            // No Start(): there is no session handle, so both calls must report
            // failure instead of throwing or touching a zero handle.
            //
            Assert.IsFalse(trace.PauseProviders());
            Assert.IsFalse(trace.ResumeProviders());
        }

        private static bool WaitFor(Func<bool> Condition, TimeSpan Deadline)
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < Deadline)
            {
                if (Condition())
                {
                    return true;
                }
                Thread.Sleep(50);
            }
            return Condition();
        }
    }
}
