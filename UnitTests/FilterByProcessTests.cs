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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using static etwlib.NativeTraceConsumer;
using static etwlib.NativeTraceControl;

namespace UnitTests
{
    using static Shared;

    [TestClass]
    public class FilterByProcessTests
    {
        [TestMethod]
        [DataRow(8)] // ETW allows 8 PIDs max in filters.
        public void Basic(int ProcessCount)
        {
            int eventsConsumed = 0;

            ConfigureLoggers();

            //
            // Deterministic event source — our own pid is in the filter targets
            // below, and every OpenSCManager call emits RPC client-call events
            // from this process.
            //
            using var stimulus = new RpcStimulus();

            //
            // This trace will automatically terminate after a set number
            // of ETW events have been successfully consumed/parsed.
            //
            using (var trace = new RealTimeTrace("Unit Test Real-Time Tracing"))
            using (var parserBuffers = new EventParserBuffers())
            {
                try
                {
                    var processes = Process.GetProcesses();
                    //
                    // Our own pid first (the RpcStimulus guarantees it emits),
                    // then svchost fillers to exercise the multi-pid filter
                    // plumbing up to the ETW maximum.
                    //
                    var targets = new List<int> { Environment.ProcessId };
                    targets.AddRange(processes.Where(
                        p => p.ProcessName != null && p.ProcessName.Contains("svchost")).Select(
                        p => p.Id).Take(ProcessCount - 1));
                    Debug.Assert(targets.Count > 0);
                    var provider = trace.AddProvider(
                        s_RpcEtwGuid, "RPC", EventTraceLevel.LogAlways, 0xFFFFFFFFFFFFFFFF, 0);
                    provider.SetProcessFilter(targets);
                    trace.Start();

                    //
                    // Begin consuming events. This is a blocking call bounded by
                    // a deadline (see ConsumeWithDeadline).
                    //
                    ConsumeWithDeadline(trace,
                    new EventRecordCallback((Event) =>
                    {
                        var evt = (EVENT_RECORD)Marshal.PtrToStructure(
                                Event, typeof(EVENT_RECORD))!;

                        var parser = new EventParser(
                            evt,
                            parserBuffers,
                            trace.GetPerfFreq());
                        try
                        {
                            var result = parser.Parse();
                            Assert.IsNotNull(result);
                            Assert.Contains((int)result.ProcessId, targets);
                        }
                        catch (Exception ex)
                        {
                            Assert.Fail($"Unable to parse event: {ex.Message}");
                        }
                        eventsConsumed++;
                    }),
                    () => eventsConsumed,
                    s_FilteredNumEvents,
                    TimeSpan.FromSeconds(60),
                    $"FilterByProcess(own pid + {ProcessCount - 1} svchost)");
                }
                catch (Exception ex)
                {
                    Assert.Fail($"An exception occurred when consuming events: {ex.Message}");
                }
            }
        }
    }
}
