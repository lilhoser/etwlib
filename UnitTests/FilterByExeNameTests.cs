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
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO;
using static etwlib.NativeTraceConsumer;
using static etwlib.NativeTraceControl;
using System.ComponentModel;

namespace UnitTests
{
    using static Shared;

    [TestClass]
    public class FilterByExeNameTests
    {
        [TestMethod]
        [DataRow("svchost.exe")]
        [DataRow("svchost.exe;smss.exe")]
        public void Basic(string ExeName)
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
                    var provider = trace.AddProvider(
                        s_RpcEtwGuid, "RPC", EventTraceLevel.Information, 0xFFFFFFFFFFFFFFFF, 0);
                    provider.SetFilteredExeName(ExeName);
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
                        ParsedEtwEvent? parsedEvent = null;

                        //
                        // Parse the event
                        //
                        try
                        {
                            parsedEvent = parser.Parse();
                        }
                        catch (Exception ex)
                        {
                            Assert.Fail($"Unable to parse event: {ex.Message}");
                        }

                        if (parsedEvent == null)
                        {
                            //
                            // There are many failure cases that are expected, like
                            // unsupported MOF events. Ignore them.
                            //
                            return;
                        }

                        if (parsedEvent.ProcessId == 0)
                        {
                            return;
                        }

                        try
                        {
                            var process = Process.GetProcessById((int)parsedEvent.ProcessId);
                            if (process.MainModule == null)
                            {
                                return;
                            }
                            Assert.Contains(Path.GetFileName(process.MainModule.FileName.ToLower()), ExeName);
                        }
                        catch (InvalidOperationException)
                        {
                            return;
                        }
                        catch (AccessViolationException)
                        {
                            return;
                        }
                        catch (Win32Exception)
                        {
                            return;
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
                catch (AssertFailedException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Assert.Fail($"An exception occurred when consuming events: {ex.Message}");
                }
            }
        }
    }
}
