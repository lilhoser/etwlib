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
using static etwlib.NativeTraceConsumer;
using static etwlib.NativeTraceControl;
using System.Collections.Generic;
using System.Linq;

namespace UnitTests
{
    using static Shared;

    [TestClass]
    public class FilterByPayloadTests
    {
        [TestMethod]
        [DataRow(7, 0, "status", PAYLOAD_OPERATOR.Equal, "0")]
        [DataRow(7, 0, "status", PAYLOAD_OPERATOR.NotEqual, "0")]
        [DataRow(7, 0, "InfoClass", PAYLOAD_OPERATOR.GreaterOrEqual, "1")]
        [DataRow(7, 0, "InfoClass", PAYLOAD_OPERATOR.GreaterThan, "1")]
        [DataRow(7, 0, "InfoClass", PAYLOAD_OPERATOR.LessThan, "3")]
        [DataRow(7, 0, "InfoClass", PAYLOAD_OPERATOR.LessOrEqual, "3")]
        public void IntegerEquality(
            int EventId,
            int EventVersion,
            string FieldName,
            PAYLOAD_OPERATOR Operator,
            string Value)
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
                    var eventDescriptor = new EVENT_DESCRIPTOR();
                    eventDescriptor.Id = (ushort)EventId;
                    eventDescriptor.Version = (byte)EventVersion;
                    var payloadFilter = new PayloadFilter(s_WinKernelRegistryGuid, eventDescriptor, true);
                    payloadFilter.AddPredicate(FieldName, Operator, Value);
                    var filters = new List<Tuple<PayloadFilter, bool>>
                    {
                        new Tuple<PayloadFilter, bool>(payloadFilter, false)
                    };

                    var provider = trace.AddProvider(
                        s_WinKernelRegistryGuid, "WinKernelReg", EventTraceLevel.Information, 0xFFFFFFFFFFFFFFFF, 0);
                    provider.AddPayloadFilters(filters);
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

                        if (parsedEvent == null || parsedEvent.TemplateData == null)
                        {
                            //
                            // There are many failure cases that are expected, like
                            // unsupported MOF events. Ignore them.
                            //
                            return;
                        }

                        if (parsedEvent.EventId != EventId)
                        {
                            return;
                        }

                        var templateFields = parsedEvent.TemplateData.Select(
                            t => t.Name.ToLower()).ToList();
                        Assert.Contains(FieldName.ToLower(), templateFields);
                        var value = parsedEvent.TemplateData.FirstOrDefault(
                            t => t.Name.ToLower() == FieldName.ToLower());
                        Assert.IsNotNull(value);
                        Assert.IsNotNull(value.Value);
                        var fieldValue = Utilities.StringToInteger(value.Value);
                        var testValue = Utilities.StringToInteger(Value);

                        switch (Operator)
                        {
                            case PAYLOAD_OPERATOR.Equal:
                                {
                                    Assert.AreEqual(testValue, fieldValue);
                                    break;
                                }
                            case PAYLOAD_OPERATOR.NotEqual:
                                {
                                    Assert.AreNotEqual(testValue, fieldValue);
                                    break;
                                }
                            case PAYLOAD_OPERATOR.GreaterThan:
                                {
                                    Assert.IsGreaterThan(testValue, fieldValue);
                                    break;
                                }
                            case PAYLOAD_OPERATOR.GreaterOrEqual:
                                {
                                    Assert.IsGreaterThanOrEqualTo(testValue, fieldValue);
                                    break;
                                }
                            case PAYLOAD_OPERATOR.LessThan:
                                {
                                    Assert.IsLessThan(testValue, fieldValue);
                                    break;
                                }
                            case PAYLOAD_OPERATOR.LessOrEqual:
                                {
                                    Assert.IsLessThanOrEqualTo(testValue, fieldValue);
                                    break;
                                }
                            default:
                                {
                                    break;
                                }
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

        // TDH seems to be broken for Between and NotBetween
        [Ignore]
        [TestMethod]
        [DataRow(new int[] { 4, 5, 7 }, 0, "DataSize", PAYLOAD_OPERATOR.Between, "1,512")]
        [DataRow(new int[] { 7 }, 0, "InfoClass", PAYLOAD_OPERATOR.NotBetween, "0,1")]
        public void IntegerRange(
            int[] EventIds,
            int EventVersion,
            string FieldName,
            PAYLOAD_OPERATOR Operator,
            string Value)
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
                    var filters = new List<Tuple<PayloadFilter, bool>>();
                    foreach (var eventId in EventIds)
                    {
                        var eventDescriptor = new EVENT_DESCRIPTOR();
                        eventDescriptor.Id = (ushort)eventId;
                        eventDescriptor.Version = (byte)EventVersion;
                        var payloadFilter = new PayloadFilter(s_WinKernelRegistryGuid, eventDescriptor, true);
                        payloadFilter.AddPredicate(FieldName, Operator, Value);
                        filters.Add(new Tuple<PayloadFilter, bool>(payloadFilter, false));
                    }

                    var provider = trace.AddProvider(
                        s_WinKernelRegistryGuid, "WinKernelReg", EventTraceLevel.Information, 0xFFFFFFFFFFFFFFFF, 0);
                    provider.AddPayloadFilters(filters);
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

                        if (parsedEvent == null || parsedEvent.TemplateData == null)
                        {
                            //
                            // There are many failure cases that are expected, like
                            // unsupported MOF events. Ignore them.
                            //
                            return;
                        }

                        if (!EventIds.ToList().Contains(parsedEvent.EventId))
                        {
                            return;
                        }

                        var templateFields = parsedEvent.TemplateData.Select(
                            t => t.Name.ToLower()).ToList();
                        Assert.Contains(FieldName.ToLower(), templateFields);
                        var value = parsedEvent.TemplateData.FirstOrDefault(
                            t => t.Name.ToLower() == FieldName.ToLower());
                        Assert.IsNotNull(value);
                        Assert.IsNotNull(value.Value);
                        var fieldValue = Utilities.StringToInteger(value.Value);
                        var expectedRange = Utilities.GetBetweenArguments(Value);
                        var lower = expectedRange.Item1;
                        var upper = expectedRange.Item2;

                        switch (Operator)
                        {
                            case PAYLOAD_OPERATOR.Between: // range-inclusive
                                {
                                    Assert.IsTrue(fieldValue >= lower && fieldValue <= upper);
                                    break;
                                }
                            case PAYLOAD_OPERATOR.NotBetween:
                                {
                                    Assert.IsTrue(fieldValue < lower || fieldValue > upper);
                                    break;
                                }
                            default:
                                {
                                    Assert.Fail("Invalid operator");
                                    return;
                                }
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

        // TDH seems to be broken for NotContains
        [TestMethod]
        [DataRow(new int[] { 5, 7 }, 0, "ValueName", PAYLOAD_OPERATOR.Contains, "a")]
        //[DataRow(new int[] { 5, 7 }, 0, "ValueName", PAYLOAD_OPERATOR.NotContains, "a")]
        [DataRow(new int[] { 5, 7 }, 0, "ValueName", PAYLOAD_OPERATOR.Is, "ConfigFlags")]
        [DataRow(new int[] { 5, 7 }, 0, "ValueName", PAYLOAD_OPERATOR.IsNot, "ConfigFlags")]
        public void StringEquality(
            int[] EventIds,
            int EventVersion,
            string FieldName,
            PAYLOAD_OPERATOR Operator,
            string Value)
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
                    var filters = new List<Tuple<PayloadFilter, bool>>();
                    foreach (var eventId in EventIds)
                    {
                        var eventDescriptor = new EVENT_DESCRIPTOR();
                        eventDescriptor.Id = (ushort)eventId;
                        eventDescriptor.Version = (byte)EventVersion;
                        var payloadFilter = new PayloadFilter(s_WinKernelRegistryGuid, eventDescriptor, true);
                        payloadFilter.AddPredicate(FieldName, Operator, Value);
                        filters.Add(new Tuple<PayloadFilter, bool>(payloadFilter, false));
                    }

                    var provider = trace.AddProvider(
                        s_WinKernelRegistryGuid, "WinKernelReg", EventTraceLevel.Information, 0xFFFFFFFFFFFFFFFF, 0);
                    provider.AddPayloadFilters(filters);
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

                        if (parsedEvent == null || parsedEvent.TemplateData == null)
                        {
                            //
                            // There are many failure cases that are expected, like
                            // unsupported MOF events. Ignore them.
                            //
                            return;
                        }

                        if (!EventIds.ToList().Contains(parsedEvent.EventId))
                        {
                            return;
                        }

                        var templateFields = parsedEvent.TemplateData.Select(
                            t => t.Name.ToLower()).ToList();
                        Assert.Contains(FieldName.ToLower(), templateFields);
                        var value = parsedEvent.TemplateData.FirstOrDefault(
                            t => t.Name.ToLower() == FieldName.ToLower());
                        Assert.IsNotNull(value);

                        //
                        // This seems like an ETW bug - it will send events where the payload
                        // value is empty, as if that's a match.
                        //
                        if (string.IsNullOrEmpty(value.Value))
                        {
                            return;
                        }

                        switch (Operator)
                        {
                            case PAYLOAD_OPERATOR.Contains:
                                {
                                    Assert.Contains(Value.ToLower(), value.Value.ToLower());
                                    break;
                                }
                            case PAYLOAD_OPERATOR.NotContains:
                                {
                                    Assert.DoesNotContain(Value.ToLower(), value.Value.ToLower());
                                    break;
                                }
                            case PAYLOAD_OPERATOR.Is:
                                {
                                    Assert.AreEqual(value.Value.ToLower(), Value.ToLower());
                                    break;
                                }
                            case PAYLOAD_OPERATOR.IsNot:
                                {
                                    Assert.AreNotEqual(value.Value.ToLower(), Value.ToLower());
                                    break;
                                }
                            default:
                                {
                                    Assert.Fail("Invalid operator");
                                    return;
                                }
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
                        if (eventsConsumed >= s_NumEvents ||
                            (Operator == PAYLOAD_OPERATOR.Is && eventsConsumed >= 5))
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

        public static IEnumerable<object[]> MultiplePredicatesArgs
        {
            get
            {
                return new[]
                {
                    new object[] {
                        new int[] { 5, 7 }, // Event ID
                        0,                  // Event Version
                        new PAYLOAD_FILTER_PREDICATE[] {
                            new PAYLOAD_FILTER_PREDICATE {
                                PayloadFieldName = "ValueName",
                                CompareOp = PAYLOAD_OPERATOR.Contains,
                                Value = "a"
                            },
                            new PAYLOAD_FILTER_PREDICATE {
                                PayloadFieldName = "status",
                                CompareOp = PAYLOAD_OPERATOR.Equal,
                                Value = "0"
                            },
                        },
                        true // "OR"
                    },
                    new object[] {
                        new int[] { 5, 7 }, // Event ID
                        0,                  // Event Version
                        new PAYLOAD_FILTER_PREDICATE[] {
                            new PAYLOAD_FILTER_PREDICATE {
                                PayloadFieldName = "ValueName",
                                CompareOp = PAYLOAD_OPERATOR.Contains,
                                Value = "a"
                            },
                            new PAYLOAD_FILTER_PREDICATE {
                                PayloadFieldName = "status",
                                CompareOp = PAYLOAD_OPERATOR.Equal,
                                Value = "0"
                            },
                        },
                        false // "AND"
                    },
                };
            }
        }

        [TestMethod]
        [DynamicData(nameof(MultiplePredicatesArgs))]
        public void MultiplePredicates(
            int[] EventIds,
            int EventVersion,
            PAYLOAD_FILTER_PREDICATE[] Predicates,
            bool MatchAny)
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
                    var filters = new List<Tuple<PayloadFilter, bool>>();
                    foreach (var eventId in EventIds)
                    {
                        var eventDescriptor = new EVENT_DESCRIPTOR();
                        eventDescriptor.Id = (ushort)eventId;
                        eventDescriptor.Version = (byte)EventVersion;
                        var payloadFilter = new PayloadFilter(s_WinKernelRegistryGuid, eventDescriptor, MatchAny);
                        foreach (var pred in Predicates)
                        {
                            payloadFilter.AddPredicate(pred);
                        }
                        filters.Add(new Tuple<PayloadFilter, bool>(payloadFilter, false));
                    }

                    var provider = trace.AddProvider(
                        s_WinKernelRegistryGuid, "WinKernelReg", EventTraceLevel.Information, 0xFFFFFFFFFFFFFFFF, 0);
                    provider.AddPayloadFilters(filters);
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

                        if (parsedEvent == null || parsedEvent.TemplateData == null)
                        {
                            //
                            // There are many failure cases that are expected, like
                            // unsupported MOF events. Ignore them.
                            //
                            return;
                        }

                        if (!EventIds.ToList().Contains(parsedEvent.EventId))
                        {
                            return;
                        }

                        var templateFields = parsedEvent.TemplateData.Select(
                            t => t.Name.ToLower()).ToList();
                        var numMatches = 0;

                        foreach (var pred in Predicates)
                        {
                            Assert.Contains(pred.PayloadFieldName.ToLower(), templateFields);
                            var value = parsedEvent.TemplateData.FirstOrDefault(
                                t => t.Name.ToLower() == pred.PayloadFieldName.ToLower());
                            Assert.IsNotNull(value);

                            //
                            // This seems like an ETW bug - it will send events where the payload
                            // value is empty, as if that's a match.
                            //
                            if (string.IsNullOrEmpty(value.Value))
                            {
                                return;
                            }

                            switch (pred.CompareOp)
                            {
                                case PAYLOAD_OPERATOR.Contains:
                                    {
                                        if (value.Value.ToLower().Contains(pred.Value.ToLower()))
                                        {
                                            numMatches++;
                                        }
                                        break;
                                    }
                                case PAYLOAD_OPERATOR.Equal:
                                    {
                                        var fieldValue = Utilities.StringToInteger(value.Value);
                                        var testValue = Utilities.StringToInteger(pred.Value);

                                        if (fieldValue == testValue)
                                        {
                                            numMatches++;
                                        }
                                        break;
                                    }
                                default:
                                    {
                                        Assert.Fail("Invalid operator");
                                        return;
                                    }
                            }
                        }

                        if (MatchAny)
                        {
                            Assert.IsGreaterThan(0, numMatches);
                        }
                        else
                        {
                            Assert.AreEqual(Predicates.Length, numMatches);
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
                        if (eventsConsumed >= s_NumEvents ||
                            (Predicates.Any(p => p.CompareOp == PAYLOAD_OPERATOR.Is) && eventsConsumed >= 5))
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
