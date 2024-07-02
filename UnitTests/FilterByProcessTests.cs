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
        [DataTestMethod]
        [DataRow(8)] // ETW allows 8 PIDs max in filters.
        public void Basic(int ProcessCount)
        {
            int eventsConsumed = 0;

            ConfigureLoggers();

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
                    // Pick svchost targets, since they're likely to use RPC.
                    //
                    var targets = processes.Where(
                        p => p.ProcessName != null && p.ProcessName.Contains("svchost")).Select(
                        p => p.Id).Take(ProcessCount).ToList();
                    Debug.Assert(targets.Count > 0);
                    var provider = trace.AddProvider(
                        s_RpcEtwGuid, "RPC", EventTraceLevel.LogAlways, 0xFFFFFFFFFFFFFFFF, 0);
                    provider.SetProcessFilter(targets);
                    trace.Start();

                    //
                    // Begin consuming events. This is a blocking call.
                    //
                    trace.Consume(new EventRecordCallback((Event) =>
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
                            Assert.IsTrue(targets.Contains((int)result.ProcessId));
                        }
                        catch (Exception ex)
                        {
                            Assert.Fail($"Unable to parse event: {ex.Message}");
                        }
                        eventsConsumed++;
                    }),
                    new BufferCallback((LogFile) =>
                    {
                        var logfile = new EVENT_TRACE_LOGFILE();
                        try
                        {
                            logfile = (EVENT_TRACE_LOGFILE)
                                Marshal.PtrToStructure(LogFile, typeof(EVENT_TRACE_LOGFILE))!;
                        }
                        catch (Exception ex)
                        {
                            Assert.Fail($"Unable to cast EVENT_TRACE_LOGFILE: {ex.Message}");
                        }
                        if (eventsConsumed >= s_NumEvents)
                        {
                            return 0;
                        }
                        return 1;
                    }));
                }
                catch (Exception ex)
                {
                    Assert.Fail($"An exception occurred when consuming events: {ex.Message}");
                }
            }
        }
    }
}
