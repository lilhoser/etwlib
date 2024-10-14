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
using static etwlib.NativeTraceControl;
using static etwlib.NativeTraceConsumer;

namespace etwlib
{
    public class EnabledProvider : IEquatable<EnabledProvider>, IComparable<EnabledProvider>, IDisposable
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public byte Level { get; set; }
        public ulong AllKeywords { get; set; }
        public ulong AnyKeywords { get; set; }
        private List<EVENT_FILTER_DESCRIPTOR> m_FilterDescriptors { get; set; }
        private nint m_AggregatedPayloadFilters { get; set; }
        private bool m_Disposed { get; set; }
        private nint m_ParametersBuffer { get; set; }
        private nint m_FiltersBuffer { get; set; }

        public EnabledProvider(Guid Id, string Name, byte Level, ulong AllKeywords, ulong AnyKeywords)
        {
            this.Id = Id;
            this.Name = Name;
            this.Level = Level;
            this.AllKeywords = AllKeywords;
            this.AnyKeywords = AnyKeywords;
            m_FilterDescriptors = new List<EVENT_FILTER_DESCRIPTOR>();
        }

        ~EnabledProvider()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (m_Disposed)
            {
                return;
            }

            m_Disposed = true;

            //
            // Release filter descriptor buffers
            //
            foreach (var filter in m_FilterDescriptors)
            {
                if (filter.Type == EventFilterTypePayload)
                {
                    //
                    // This one is released further down by ETW.
                    //
                    continue;
                }
                Marshal.FreeHGlobal(filter.Ptr);
            }

            //
            // Release array wrapper for filter descriptors
            //
            if (m_FiltersBuffer != nint.Zero)
            {
                Marshal.FreeHGlobal(m_FiltersBuffer);
            }

            //
            // Release containing parameters buffer
            //
            if (m_ParametersBuffer != nint.Zero)
            {
                Marshal.FreeHGlobal(m_ParametersBuffer);
            }

            //
            // Release any aggregated TDH payload filters
            //
            if (m_AggregatedPayloadFilters != nint.Zero)
            {
                var result = TdhCleanupPayloadEventFilterDescriptor(
                    m_AggregatedPayloadFilters);
                Debug.Assert(result == ERROR_SUCCESS);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public override bool Equals(object? Other)
        {
            if (Other == null)
            {
                return false;
            }
            var field = Other as EnabledProvider;
            return Equals(field);
        }

        public bool Equals(EnabledProvider? Other)
        {
            if (Other == null)
            {
                return false;
            }
            return Id == Other.Id;
        }

        public static bool operator ==(EnabledProvider? Provider1, EnabledProvider? Provider2)
        {
            if ((object)Provider1 == null || (object)Provider2 == null)
                return Equals(Provider1, Provider2);
            return Provider1.Equals(Provider2);
        }

        public static bool operator !=(EnabledProvider? Provider1, EnabledProvider? Provider2)
        {
            if ((object)Provider1 == null || (object)Provider2 == null)
                return !Equals(Provider1, Provider2);
            return !(Provider1.Equals(Provider2));
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        public int CompareTo(EnabledProvider? Other)
        {
            if (Other == null)
            {
                return 1;
            }
            return Id.CompareTo(Other.Id);
        }

        public override string ToString()
        {
            return $"{Id} : Level={Level}, All={AllKeywords:X}, Any={AnyKeywords:X}";
        }

        public uint Disable(long SessionHandle)
        {
            return EnableTraceEx2(SessionHandle,
                        Id,
                        EventControlCode.DisableProvider,
                        Level,
                        0, 0, 0,
                        nint.Zero);
        }

        public uint Enable(long SessionHandle)
        {
            GenerateTraceParameters();
            return EnableTraceEx2(
                    SessionHandle,
                    Id,
                    EventControlCode.EnableProvider,
                    Level,
                    AnyKeywords,
                    AllKeywords,
                    0xffffffff,
                    m_ParametersBuffer);
        }

        public void SetProcessFilter(List<int> ProcessIds)
        {
            if (ProcessIds.Count == 0)
            {
                throw new Exception("Event ID list required.");
            }

            if (ProcessIds.Count > MaxEventFilterPidCount)
            {
                throw new Exception($"Maximum {MaxEventFilterPidCount} filtered processes");
            }

            foreach (var desc in m_FilterDescriptors)
            {
                if (desc.Type == EventFilterTypePid)
                {
                    throw new Exception("PID filter can only be used once per session.");
                }
            }

            var descriptor = new EVENT_FILTER_DESCRIPTOR();
            var size = ProcessIds.Count * sizeof(int);
            descriptor.Type = EventFilterTypePid;
            descriptor.Size = (uint)size;
            descriptor.Ptr = Marshal.AllocHGlobal(size);
            if (descriptor.Ptr == nint.Zero)
            {
                throw new Exception("Out of memory");
            }
            Marshal.Copy(ProcessIds.ToArray(), 0, descriptor.Ptr, ProcessIds.Count);
            m_FilterDescriptors.Add(descriptor);
        }

        public void SetEventIdsFilter(List<int> EventIds, bool Enable)
        {
            if (EventIds.Count == 0)
            {
                throw new Exception("Event ID list required.");
            }

            if (EventIds.Count > MaxEventFilterEventIdCount)
            {
                throw new Exception($"Maximum {MaxEventFilterEventIdCount} event IDs");
            }

            foreach (var desc in m_FilterDescriptors)
            {
                if (desc.Type == EventFilterTypeEventId)
                {
                    throw new Exception("Event ID filter can only be used once per session.");
                }
            }

            AddEventIdFilter(EventFilterTypeEventId, EventIds, Enable);
        }

        public void SetStackwalkEventIdsFilter(List<int> EventIds, bool Enable)
        {
            if (EventIds.Count == 0)
            {
                throw new Exception("Event ID list required.");
            }

            if (EventIds.Count > MaxEventFilterEventIdCount)
            {
                throw new Exception($"Maximum {MaxEventFilterEventIdCount} stackwalk event IDs");
            }

            foreach (var desc in m_FilterDescriptors)
            {
                if (desc.Type == EventFilterTypeStackwalk)
                {
                    throw new Exception("Stackwalk filter can only be used once per session.");
                }
            }

            AddEventIdFilter(EventFilterTypeStackwalk, EventIds, Enable);
        }

        public void AddPayloadFilters(List<Tuple<PayloadFilter, bool>> Filters)
        {
            if (Filters.Count == 0)
            {
                throw new Exception("At least one payload filter is required.");
            }

            foreach (var desc in m_FilterDescriptors)
            {
                if (desc.Type == EventFilterTypePayload)
                {
                    throw new Exception("Payload filter can only be used once per session.");
                }
            }

            var filters = new nint[Filters.Count];
            var index = 0;

            foreach (var entry in Filters)
            {
                var filter = entry.Item1.Create();
                Debug.Assert(filter != nint.Zero);
                filters[index++] = filter;
            }

            var matchAllFlags = Filters.ConvertAll(f => Convert.ToByte(f.Item2)).ToArray();
            var eventFilterDescriptor = Marshal.AllocHGlobal(
                Marshal.SizeOf(typeof(EVENT_FILTER_DESCRIPTOR)));
            if (eventFilterDescriptor == nint.Zero)
            {
                throw new Exception("Out of memory");
            }
            var result = TdhAggregatePayloadFilters(
                (uint)Filters.Count,
                filters,
                matchAllFlags,
                eventFilterDescriptor);
            if (result != ERROR_SUCCESS)
            {
                throw new Exception($"TdhAggregatePayloadFilters failed: 0x{result:X}");
            }
            m_AggregatedPayloadFilters = eventFilterDescriptor;
        }

        public void SetFilteredExeName(string ExeName)
        {
            //
            // Note: the ExeName string can contain multiple executable names separated
            // by semi-colons.
            //
            var length = (ExeName.Length + 1) * 2;
            if (length > MaxEventFilterDataSize)
            {
                throw new Exception("Exe name too long");
            }

            foreach (var desc in m_FilterDescriptors)
            {
                if (desc.Type == EventFilterTypeExecutableName)
                {
                    throw new Exception("Exe filter can only be used once per session.");
                }
            }

            var descriptor = new EVENT_FILTER_DESCRIPTOR();
            descriptor.Type = EventFilterTypeExecutableName;
            descriptor.Size = (uint)length;
            descriptor.Ptr = Marshal.StringToHGlobalUni(ExeName);
            if (descriptor.Ptr == nint.Zero)
            {
                throw new Exception("Out of memory");
            }
            m_FilterDescriptors.Add(descriptor);
        }

        public void SetFilteredPackageAppId(string AppId)
        {
            //
            // Note: the AppId string can contain multiple MS Store App IDs separated
            // by semi-colons.
            //
            if (AppId.Length * 2 > MaxEventFilterDataSize)
            {
                throw new Exception("AppId too long");
            }

            foreach (var desc in m_FilterDescriptors)
            {
                if (desc.Type == EventFilterTypePackageAppId)
                {
                    throw new Exception("App ID filter can only be used once per session.");
                }
            }

            var descriptor = new EVENT_FILTER_DESCRIPTOR();
            descriptor.Type = EventFilterTypePackageAppId;
            descriptor.Size = (uint)AppId.Length * 2;
            descriptor.Ptr = Marshal.StringToHGlobalUni(AppId);
            if (descriptor.Ptr == nint.Zero)
            {
                throw new Exception("Out of memory");
            }
            m_FilterDescriptors.Add(descriptor);
        }

        public void SetFilteredPackageId(string PackageId)
        {
            //
            // Note: the PackageId string can contain multiple MS Store package IDs separated
            // by semi-colons.
            //
            if (PackageId.Length * 2 > MaxEventFilterDataSize)
            {
                throw new Exception("PackageId too long");
            }

            foreach (var desc in m_FilterDescriptors)
            {
                if (desc.Type == EventFilterTypePackageId)
                {
                    throw new Exception("Package ID filter can only be used once per session.");
                }
            }

            var descriptor = new EVENT_FILTER_DESCRIPTOR();
            descriptor.Type = EventFilterTypePackageId;
            descriptor.Size = (uint)PackageId.Length * 2;
            descriptor.Ptr = Marshal.StringToHGlobalUni(PackageId);
            if (descriptor.Ptr == nint.Zero)
            {
                throw new Exception("Out of memory");
            }
            m_FilterDescriptors.Add(descriptor);
        }

        public void SetStackwalkLevelKw(byte Level, ulong MatchAnyKeyword, ulong MatchAllKeyword, bool FilterIn)
        {
            foreach (var desc in m_FilterDescriptors)
            {
                if (desc.Type == EventFilterTypeStackWalkLevelKw)
                {
                    throw new Exception("Stackwalk Level/KW filter can only be used once per session.");
                }
            }

            var descriptor = new EVENT_FILTER_DESCRIPTOR();
            descriptor.Type = EventFilterTypeStackWalkLevelKw;
            descriptor.Size = (uint)Marshal.SizeOf(typeof(EVENT_FILTER_LEVEL_KW));
            descriptor.Ptr = Marshal.AllocHGlobal((int)descriptor.Size);
            if (descriptor.Ptr == nint.Zero)
            {
                throw new Exception("Out of memory");
            }

            var filter = new EVENT_FILTER_LEVEL_KW();
            filter.Level = Level;
            filter.MatchAllKeyword = MatchAllKeyword;
            filter.MatchAnyKeyword = MatchAnyKeyword;
            filter.FilterIn = FilterIn;
            Marshal.StructureToPtr(filter, descriptor.Ptr, false);
            m_FilterDescriptors.Add(descriptor);
        }

        private
        nint
        GenerateTraceParameters()
        {
            if (m_ParametersBuffer != nint.Zero)
            {
                Debug.Assert(false);
                throw new Exception("Parameters have already been generated");
            }

            var parameters = new ENABLE_TRACE_PARAMETERS
            {
                Version = 2,
                EnableProperty = EnableTraceProperties.ProcessStartKey |
                    EnableTraceProperties.Sid
            };

            var numFilters = m_FilterDescriptors.Count;
            if (m_AggregatedPayloadFilters != nint.Zero)
            {
                numFilters++;
            }

            if (numFilters > 0)
            {
                Debug.Assert(numFilters < MaxEventFiltersCount);

                //
                // EnableTraceEx2 expects an array of EVENT_FILTER_DESCRIPTOR
                //
                var size = Marshal.SizeOf(typeof(EVENT_FILTER_DESCRIPTOR));
                m_FiltersBuffer = Marshal.AllocHGlobal(numFilters * size);
                if (m_FiltersBuffer == nint.Zero)
                {
                    throw new Exception("Out of memory");
                }
                nint pointer = m_FiltersBuffer;
                foreach (var desc in m_FilterDescriptors)
                {
                    Marshal.StructureToPtr(desc, pointer, false);
                    pointer = nint.Add(pointer, size);
                }
                if (m_AggregatedPayloadFilters != nint.Zero)
                {
                    var payloadFilter = (EVENT_FILTER_DESCRIPTOR)Marshal.PtrToStructure(
                        m_AggregatedPayloadFilters, typeof(EVENT_FILTER_DESCRIPTOR))!;
                    Debug.Assert(payloadFilter.Type == EventFilterTypePayload);
                    Debug.Assert(payloadFilter.Ptr != nint.Zero);
                    Marshal.StructureToPtr(payloadFilter, pointer, false);
                    pointer = nint.Add(pointer, size);
                }
                parameters.FilterDescCount = (uint)numFilters;
                parameters.EnableFilterDesc = m_FiltersBuffer;

                if (m_FilterDescriptors.Any(
                        f => f.Type == EventFilterTypeStackwalk ||
                        f.Type == EventFilterTypeStackWalkLevelKw))
                {
                    //
                    // Note: events over 64kb will be dropped by etw with this set.
                    //
                    parameters.EnableProperty |= EnableTraceProperties.StackTrace;
                }
            }

            m_ParametersBuffer = Marshal.AllocHGlobal(Marshal.SizeOf(parameters));
            Marshal.StructureToPtr(parameters, m_ParametersBuffer, false);
            return m_ParametersBuffer;
        }

        private
        void
        AddEventIdFilter(uint FilterType, List<int> EventIds, bool Enable)
        {
            var descriptor = new EVENT_FILTER_DESCRIPTOR();
            descriptor.Type = FilterType;
            var size = Marshal.SizeOf(typeof(EVENT_FILTER_EVENT_ID)) +
                ((EventIds.Count - 1) * Marshal.SizeOf(typeof(ushort)));
            descriptor.Size = (uint)size;
            descriptor.Ptr = Marshal.AllocHGlobal(size);
            if (descriptor.Ptr == nint.Zero)
            {
                throw new Exception("Out of memory");
            }
            var filter = new EVENT_FILTER_EVENT_ID();
            filter.FilterIn = Enable;
            filter.Reserved = 0;
            filter.Count = (ushort)EventIds.Count;
            Marshal.StructureToPtr(filter, descriptor.Ptr, false);
            var dest = nint.Add(descriptor.Ptr, (int)Marshal.OffsetOf<EVENT_FILTER_EVENT_ID>("Events"));
            var ids = EventIds.ConvertAll(id => (ushort)id).ToArray();
            var byteSize = ids.Length * Marshal.SizeOf(typeof(ushort));
            var bytes = new byte[byteSize];
            Buffer.BlockCopy(ids, 0, bytes, 0, byteSize);
            Marshal.Copy(bytes, 0, dest, byteSize);
            m_FilterDescriptors.Add(descriptor);
        }
    }
}
