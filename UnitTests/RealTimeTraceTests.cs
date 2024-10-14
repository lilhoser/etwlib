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
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Runtime.InteropServices;
using etwlib;
using System.Threading.Tasks;

namespace UnitTests
{
    using static NativeTraceConsumer;
    using static NativeTraceControl;
    using static Shared;

    [TestClass]
    public class RealTimeTraceTests
    {
        [DataTestMethod]
        [DataRow(EventTraceLevel.Information)]
        [DataRow(EventTraceLevel.LogAlways)]
        public void Basic(EventTraceLevel Level)
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
                    var provider = trace.AddProvider(s_RpcEtwGuid, "RPC", Level, 0xFFFFFFFFFFFFFFFF, 0);
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

        [DataTestMethod]
        [DataRow(EventTraceLevel.Information)]
        [DataRow(EventTraceLevel.LogAlways)]
        public void BasicStartStop(EventTraceLevel Level)
        {
            int eventsConsumed = 0;

            ConfigureLoggers();

            var trace = new RealTimeTrace("Unit Test Real-Time Tracing");

            //
            // Start a task to initiate a trace with no stop condition.
            //
            Task.Run(() =>
            {
                using (var parserBuffers = new EventParserBuffers())
                {
                    try
                    {
                        var provider = trace.AddProvider(s_RpcEtwGuid, "RPC", Level, 0xFFFFFFFFFFFFFFFF, 0);
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
                            return 1;
                        }));
                    }
                    catch (Exception ex)
                    {
                        Assert.Fail($"An exception occurred when consuming events: {ex.Message}");
                    }
                }
            });

            //
            // Start a task to stop the trace.
            //
            Task.Run(() =>
            {
                using (var trace = new RealTimeTrace("Unit Test Real-Time Tracing"))
                {
                    try
                    {
                        trace.Stop();
                    }
                    catch (Exception ex)
                    {
                        Assert.Fail($"An exception occurred when consuming events: {ex.Message}");
                    }
                }
            });
        }
    }
}
