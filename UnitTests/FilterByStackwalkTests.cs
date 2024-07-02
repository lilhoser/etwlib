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

namespace UnitTests
{
    using static NativeTraceConsumer;
    using static NativeTraceControl;
    using static Shared;

    [TestClass]
    public class FilterByStackwalkTests
    {
        [DataTestMethod]
        [DataRow(EventTraceLevel.Warning, RegistryProviderKeywords.CreateKey | RegistryProviderKeywords.QueryKey, true)]
        [DataRow(EventTraceLevel.Warning, RegistryProviderKeywords.CreateKey | RegistryProviderKeywords.QueryKey, false)]
        [DataRow(EventTraceLevel.Error, RegistryProviderKeywords.EnumerateKey | RegistryProviderKeywords.QueryKey, true)]
        [DataRow(EventTraceLevel.Error, RegistryProviderKeywords.EnumerateKey | RegistryProviderKeywords.QueryKey, false)]
        public void LevelKw(
            EventTraceLevel Level,
            RegistryProviderKeywords MatchAnyKeyword,
            bool Enable)
        {
            int eventsConsumed = 0;

            ConfigureLoggers();
            ConfigureSymbolResolver();

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
                        s_WinKernelRegistryGuid, "WinKernelReg", Level, (ulong)MatchAnyKeyword, 0);
                    provider.SetStackwalkLevelKw(Level, (ulong)MatchAnyKeyword, 0, Enable);
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

                        if (!Enum.TryParse<EventTraceLevel>(parsedEvent.Level, true, out var level))
                        {
                            Assert.Fail($"Unexpected level {parsedEvent.Level}");
                        }

                        Assert.IsTrue(level <= Level);

                        //
                        // Keyword checks
                        //
                        Assert.IsNotNull(parsedEvent.Keywords);
                        var keywords = parsedEvent.Keywords.Split(',');
                        int matchedKeywords = 0;
                        foreach (var kw in keywords)
                        {
                            if (!Enum.TryParse<RegistryProviderKeywords>(kw, true, out var parsed))
                            {
                                //
                                // Not in our enum definition, this is fine. Because keywords
                                // are bitmasked, there might be other keywords present.
                                //
                                continue;
                            }

                            if (MatchAnyKeyword.HasFlag(parsed))
                            {
                                matchedKeywords++;
                            }
                        }

                        Assert.IsTrue(matchedKeywords > 0);

                        //
                        // Stackwalk checks
                        //
                        if (!Enable)
                        {
                            Assert.IsNull(parsedEvent.StackwalkAddresses);
                        }
                        else
                        {
                            Assert.IsNotNull(parsedEvent.StackwalkAddresses);
                            Assert.IsTrue(parsedEvent.StackwalkAddresses.Count > 0);

                            var pid = (int)parsedEvent.ProcessId;
                            if (pid == 0)
                            {
                                return;
                            }

                            var pass = StackwalkCheck(pid, parsedEvent.StackwalkAddresses, out bool skip);
                            if (!pass)
                            {
                                if (skip)
                                {
                                    return;
                                }
                            }
                            Assert.IsTrue(pass);
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
