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
using System.IO;

namespace UnitTests
{
    using static NativeTraceConsumer;
    using static NativeTraceControl;

    [TestClass]
    public class FileTraceTests
    {
        private readonly Guid s_RpcEtwGuid =
            new Guid("6ad52b32-d609-4be9-ae07-ce8dae937e39");
        private readonly int s_NumEvents = 1000;


        [TestMethod]
        [DeploymentItem(@"..\..\..\..\data\trace_files\ms-rpc-capture-arrays.etl")]
        public void Basic()
        {
            int eventsConsumed = 0;

            TraceLogger.Initialize();
            TraceLogger.SetLevel(System.Diagnostics.SourceLevels.Error);

            //
            // This trace will automatically terminate after a set number
            // of ETW events have been successfully consumed/parsed.
            //
            var current = Directory.GetCurrentDirectory();
            var target = Path.Combine(current, "ms-rpc-capture-arrays.etl");

            using (var trace = new FileTrace(target))
            using (var parserBuffers = new EventParserBuffers())
            {
                try
                {
                    //
                    // Begin consuming events. This is a blocking call.
                    //
                    trace.Consume(new EventRecordCallback((Event) =>
                    {
                        var evt = (EVENT_RECORD)Marshal.PtrToStructure(
                                    Event, typeof(EVENT_RECORD))!;

                        if (!evt.EventHeader.ProviderId.Equals(s_RpcEtwGuid))
                        {
                            //
                            // Skip events from other providers, because it might not
                            // be a builtin provider, in which case we'd need to go
                            // find the right manifest and that is overly complex for
                            // this unit test.
                            //
                            return;
                        }
                        var parser = new EventParser(
                            evt,
                            parserBuffers,
                            trace.GetPerfFreq());
                        try
                        {
                            var result = parser.Parse();
                            Assert.IsNotNull(result, "Failed to parse the event");
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
