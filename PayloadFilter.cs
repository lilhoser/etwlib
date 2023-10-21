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

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace etwlib
{
    using static TraceLogger;
    using static NativeTraceConsumer;
    using static etwlib.NativeTraceControl;

    public class PayloadFilter : IDisposable
    {
        private Guid m_Provider;
        private readonly EVENT_DESCRIPTOR m_Event;
        private bool m_MatchAny;
        private List<PAYLOAD_FILTER_PREDICATE> m_Predicates;
        private nint m_Filter;
        private bool m_Disposed;

        public PayloadFilter(Guid Provider, EVENT_DESCRIPTOR Event, bool MatchAny)
        {
            //
            // Important: Caller must first load the manifest for Provider or Tdh*
            // calls will likely fail. Use ManifestParser.cs
            //
            m_Provider = Provider;
            m_MatchAny = MatchAny;
            m_Event = Event;
            m_Predicates = new List<PAYLOAD_FILTER_PREDICATE>();
        }

        ~PayloadFilter()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (m_Disposed)
            {
                return;
            }

            Trace(TraceLoggerType.EtwManifestParser,
                TraceEventType.Information,
                "Disposing ManifestParser");

            m_Disposed = true;

            if (m_Filter != nint.Zero)
            {
                var result = TdhDeletePayloadFilter(ref m_Filter);
                Debug.Assert(result == ERROR_SUCCESS);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void AddPredicate(PAYLOAD_FILTER_PREDICATE Predicate)
        {
            AddPredicate(Predicate.PayloadFieldName, Predicate.CompareOp, Predicate.Value);
        }

        public void AddPredicate(string Field, PAYLOAD_OPERATOR Operator, string Value)
        {
            ValidatePredicate(Field, Operator, Value);
            m_Predicates.Add(new PAYLOAD_FILTER_PREDICATE
            {
                PayloadFieldName = Field,
                CompareOp = Operator,
                Value = Value,
            });
        }

        public static void ValidatePredicate(string Field, PAYLOAD_OPERATOR Operator, string Value)
        {
            if (string.IsNullOrEmpty(Field) || string.IsNullOrEmpty(Value))
            {
                throw new Exception("A field name and value is required.");
            }
            if (Operator >= PAYLOAD_OPERATOR.Max)
            {
                throw new Exception("Invalid payload operator.");
            }

            //
            // Notes:
            //      - All types are derived by ETW from the provider's published manifest. If
            //        the manifest doesn't exist, the EnableTraceEx will fail.
            //      - All values must be a simple string, a Guid as a string including
            //        the braces "{<GUID>}", an integer, or a FILETIME as an integer.
            //      - Binary or structured types are not supported by ETW payload filtering.
            //      - For operators that require two integer arguments, they must be
            //        provided as a string with a comma in between - eg, "1,2"
            //      - To compare GUIDs, you must use Is or IsNot operators.
            //
            switch (Operator)
            {
                case PAYLOAD_OPERATOR.Equal:
                case PAYLOAD_OPERATOR.NotEqual:
                case PAYLOAD_OPERATOR.LessOrEqual:
                case PAYLOAD_OPERATOR.GreaterOrEqual:
                case PAYLOAD_OPERATOR.LessThan:
                case PAYLOAD_OPERATOR.GreaterThan:
                case PAYLOAD_OPERATOR.Modulo:
                    {
                        _ = Utilities.StringToInteger(Value);
                        break;
                    }
                case PAYLOAD_OPERATOR.Between:
                case PAYLOAD_OPERATOR.NotBetween:
                    {
                        _ = Utilities.GetBetweenArguments(Value);
                        break;
                    }
                //
                // Is, IsNot, Contains, NotContains operators all require a single string value.
                //
                default:
                    {
                        break;
                    }
            }
        }

        public nint Create()
        {
            var eventPointer = nint.Zero;
            var predicatesPointer = nint.Zero;

            Trace(TraceLoggerType.RealTimeTrace,
                TraceEventType.Information,
                "Creating TDH payload filters...");

            if (m_Predicates.Count == 0)
            {
                throw new Exception("No predicates specified");
            }

            try
            {
                eventPointer = Marshal.AllocHGlobal(
                    Marshal.SizeOf(typeof(EVENT_DESCRIPTOR)));
                if (eventPointer == nint.Zero)
                {
                    throw new Exception("Out of memory");
                }
                Marshal.StructureToPtr(m_Event, eventPointer, false);

                var predicateSize = Marshal.SizeOf(typeof(PAYLOAD_FILTER_PREDICATE));
                predicatesPointer = Marshal.AllocHGlobal(
                    predicateSize * m_Predicates.Count);
                if (predicatesPointer == nint.Zero)
                {
                    throw new Exception("Out of memory");
                }
                var pointer = predicatesPointer;
                foreach (var predicate in m_Predicates)
                {
                    Marshal.StructureToPtr(predicate, pointer, false);
                    pointer = nint.Add(pointer, predicateSize);
                }

                var status = TdhCreatePayloadFilter(
                    ref m_Provider,
                    eventPointer,
                    m_MatchAny,
                    (uint)m_Predicates.Count,
                    predicatesPointer,
                    ref m_Filter);
                switch (status)
                {
                    case ERROR_SUCCESS:
                        {
                            Debug.Assert(m_Filter != nint.Zero);
                            Trace(TraceLoggerType.RealTimeTrace,
                                  TraceEventType.Information,
                                  "Payload filter created successfully.");
                            return m_Filter;
                        }
                    case ERROR_INSUFFICIENT_BUFFER:
                        {
                            throw new Exception($"Payload is too large - " +
                                $"max {MaxEventFilterPayloadSize} bytes");
                        }
                    case ERROR_NOT_FOUND:
                    case ERROR_FILE_NOT_FOUND:
                        {
                            throw new Exception($"ETW was unable to locate " +
                                $"the manifest or schema for provider {m_Provider}. " +
                                $"Usually this is caused by an incorrect field in the " +
                                $"input event descriptor, such as incorrect event ID or " +
                                $"version number.");
                        }
                    case ERROR_INVALID_PARAMETER:
                        {
                            throw new Exception($"TdhCreatePayloadFilter returned " +
                                $"STATUS_INVALID_PARAMETER, which usually means " +
                                $"a field name defined in one or more predicates " +
                                $"is invalid for the provider manifest.");
                        }
                    default:
                        {
                            throw new Exception($"TdhCreatePayloadFilter failed: 0x{status:X}");
                        }
                }
            }
            finally
            {
                if (eventPointer != nint.Zero)
                {
                    Marshal.FreeHGlobal(eventPointer);
                }
                if (predicatesPointer != nint.Zero)
                {
                    Marshal.FreeHGlobal(predicatesPointer);
                }
            }
        }

    }
}
