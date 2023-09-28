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
using System.Text;
using static etwlib.NativeTraceControl;
using static etwlib.NativeTraceConsumer;
using System.Security.Principal;

namespace etwlib
{
    using static TraceLogger;

    public class RealTimeTrace : IDisposable
    {
        private readonly string m_SessionName;
        private readonly Guid m_SessionGuid;
        private bool m_Disposed;
        private nint m_PropertiesBuffer;
        private nint m_ParametersBuffer;
        private nint m_FiltersBuffer;
        private Guid m_ProviderGuid;
        private long m_SessionHandle;
        private EventTraceLevel m_TraceLevel;
        private ulong m_MatchAnyKeyword;
        private ulong m_MatchAllKeyword;
        private long m_PerfFreq;
        private List<EVENT_FILTER_DESCRIPTOR> m_FilterDescriptors;
        private nint m_AggregatedPayloadFilters;

        public RealTimeTrace(
            string SessionName,
            Guid Provider,
            EventTraceLevel Level,
            ulong MatchAnyKeyword,
            ulong MatchAllKeyword)
        {
            m_SessionName = SessionName;
            m_SessionGuid = Guid.NewGuid();
            m_Disposed = false;
            m_PropertiesBuffer = nint.Zero;
            m_ProviderGuid = Provider;
            m_SessionHandle = 0;
            m_TraceLevel = Level;
            m_MatchAnyKeyword = MatchAnyKeyword;
            m_MatchAllKeyword = MatchAllKeyword;
            m_FilterDescriptors = new List<EVENT_FILTER_DESCRIPTOR>();
        }

        ~RealTimeTrace()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (m_Disposed)
            {
                return;
            }

            Trace(TraceLoggerType.RealTimeTrace,
                TraceEventType.Information,
                "Disposing RealTimeTrace");

            m_Disposed = true;

            if (m_SessionHandle != 0 && m_SessionHandle != -1)
            {
                var result = EnableTraceEx2(m_SessionHandle,
                    ref m_ProviderGuid,
                    EventControlCode.DisableProvider,
                    m_TraceLevel,
                    0, 0, 0,
                    nint.Zero);
                if (result != ERROR_SUCCESS)
                {
                    Trace(TraceLoggerType.RealTimeTrace,
                          TraceEventType.Error,
                          $"RealTimeTrace dispose could not disable provider: " +
                          $"{result:X}");
                }
                result = ControlTrace(
                    m_SessionHandle,
                    m_SessionName,
                    m_PropertiesBuffer,
                    ControlCode.Stop);
                if (result != ERROR_SUCCESS)
                {
                    Trace(TraceLoggerType.RealTimeTrace,
                          TraceEventType.Error,
                          $"RealTimeTrace dispose could not stop trace: " +
                          $"{result:X}");
                }
            }

            if (m_PropertiesBuffer != nint.Zero)
            {
                Marshal.FreeHGlobal(m_PropertiesBuffer);
            }

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

        public void SetStackwalkLevelKw(EventTraceLevel Level, ulong MatchAnyKeyword, ulong MatchAllKeyword, bool FilterIn)
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

        public void Start()
        {
            Trace(TraceLoggerType.RealTimeTrace,
                TraceEventType.Information,
                "Starting RealTimeTrace " + m_SessionName + "...");

            //
            // Current user must be in "Performance Log Users" group to enable a provider
            //
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                if (!principal.IsInRole(WindowsBuiltInRole.Administrator) &&
                    !principal.IsInRole(new SecurityIdentifier(WellKnownSidType.BuiltinPerformanceLoggingUsersSid, null)))
                {
                    throw new Exception("Current user must be in Administrators or " +
                        "Performance Logging Users Group");
                }
            }

            GenerateTraceProperties();

            uint status;
            for (; ; )
            {
                status = StartTrace(
                    ref m_SessionHandle, m_SessionName, m_PropertiesBuffer);
                if (status == ERROR_ALREADY_EXISTS)
                {
                    Trace(TraceLoggerType.RealTimeTrace,
                        TraceEventType.Warning,
                        $"A trace is already opened with instance " +
                        $"name {m_SessionName}, attempting to stop it.");
                    //
                    // Orphaned session, possibly from a crash. Try to stop it.
                    //
                    status = ControlTrace(
                        0,
                        m_SessionName,
                        m_PropertiesBuffer,
                        ControlCode.Stop);
                    if (status != ERROR_SUCCESS)
                    {
                        var error = $"Unable to stop orphaned trace session: {status:X}";
                        Trace(TraceLoggerType.RealTimeTrace,
                            TraceEventType.Error,
                            error);
                        throw new Exception(error);
                    }
                    Trace(TraceLoggerType.RealTimeTrace,
                        TraceEventType.Information,
                        "Prior trace session stopped.");
                    continue;
                }
                else if (status != ERROR_SUCCESS || m_SessionHandle == 0 || m_SessionHandle == -1)
                {
                    m_SessionHandle = 0;
                    var error = $"StartTrace() failed: 0x{status:X}";
                    Trace(TraceLoggerType.RealTimeTrace,
                        TraceEventType.Error,
                        error);
                    throw new Exception(error);
                }
                break;
            }

            Trace(TraceLoggerType.RealTimeTrace,
                TraceEventType.Information,
                "Trace started. Enabling provider...");
            GenerateTraceParameters();
            status = EnableTraceEx2(
                m_SessionHandle,
                ref m_ProviderGuid,
                EventControlCode.EnableProvider,
                m_TraceLevel,
                m_MatchAnyKeyword,
                m_MatchAllKeyword,
                0xffffffff,
                m_ParametersBuffer);
            if (status != ERROR_SUCCESS)
            {
                var error = $"EnableTraceEx2() failed: 0x{status:X}";
                Trace(TraceLoggerType.RealTimeTrace,
                    TraceEventType.Error,
                    error);
                throw new Exception(error);
            }

            Trace(TraceLoggerType.RealTimeTrace,
                TraceEventType.Information,
                "Provider enabled successfully.");
        }

        public void Consume(
            EventRecordCallback EventCallback,
            BufferCallback BufferCallback
            )
        {
            var logfile = new EVENT_TRACE_LOGFILE()
            {
                EventCallback = EventCallback,
                BufferCallback = BufferCallback,
                LoggerName = m_SessionName,
                ProcessTraceMode = ProcessTraceMode.EventRecord |
                    ProcessTraceMode.RealTime
            };

            var logFilePointer = Marshal.AllocHGlobal(Marshal.SizeOf(logfile));
            if (logFilePointer == nint.Zero)
            {
                throw new Exception("Out of memory");
            }
            Trace(TraceLoggerType.RealTimeTrace,
                TraceEventType.Information,
                "Consuming events...");
            Marshal.StructureToPtr(logfile, logFilePointer, false);
            var handle = OpenTrace(logFilePointer);
            //
            // Marshal the structure back so we can get the PerfFreq
            //
            logfile = (EVENT_TRACE_LOGFILE)Marshal.PtrToStructure(
                logFilePointer, typeof(EVENT_TRACE_LOGFILE))!;
            Marshal.FreeHGlobal(logFilePointer);
            logFilePointer = nint.Zero;
            if (handle == -1 || handle == 0)
            {
                var error = $"OpenTrace() returned an invalid handle:  0x" +
                    $"{Marshal.GetLastWin32Error():X}";
                Trace(TraceLoggerType.RealTimeTrace,
                    TraceEventType.Error,
                    error);
                throw new Exception(error);
            }

            Trace(TraceLoggerType.RealTimeTrace,
                TraceEventType.Information,
                "Trace session successfully opened, processing trace..");

            try
            {
                //
                // Update PerfFreq so event's timestamps can be parsed.
                //
                m_PerfFreq = logfile.LogfileHeader.PerfFreq.QuadPart;

                //
                // Blocking call.  The caller's BufferCallback must return false to
                // unblock this routine.
                //
                var status = ProcessTrace(
                    new long[1] { handle },
                    1,
                    nint.Zero,
                    nint.Zero);
                if (status != ERROR_SUCCESS)
                {
                    var error = $"ProcessTrace() failed: 0x{status:X}" +
                        $", GetLastError: {Marshal.GetLastWin32Error():X}";
                    Trace(TraceLoggerType.RealTimeTrace,
                          TraceEventType.Error,
                          error);
                    throw new Exception(error);
                }
                Trace(TraceLoggerType.RealTimeTrace,
                    TraceEventType.Information,
                    "Trace processing successfully completed.");
            }
            finally
            {
                CloseTrace(handle);
            }
        }

        public
        long
        GetPerfFreq()
        {
            return m_PerfFreq;
        }

        private
        void
        GenerateTraceParameters()
        {
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
        }

        private
        void
        GenerateTraceProperties()
        {
            var loggerName = Encoding.Unicode.GetBytes(m_SessionName + "\0");
            var loggerNameLocation = Marshal.SizeOf(typeof(EVENT_TRACE_PROPERTIES));
            int total = loggerNameLocation + loggerName.Length;
            var buffer = Marshal.AllocHGlobal(total);
            var properties = new EVENT_TRACE_PROPERTIES();
            properties.Wnode.BufferSize = (uint)total;
            properties.Wnode.Flags =
                WNodeFlags.TracedGuid | WNodeFlags.VersionedProperties;
            properties.Wnode.ClientContext = WNodeClientContext.QPC;
            properties.Wnode.Guid = m_SessionGuid;
            properties.VersionNumber = 2;
            properties.BufferSize = 64; // high freq should use 64kb - 128kb (this field in KB!)
            properties.LogFileMode =
                LogFileModeFlags.RealTime | LogFileModeFlags.Sequential;
            properties.MinimumBuffers = 4;
            properties.MaximumBuffers = 4;
            properties.FilterDescCount = 0;
            properties.FilterDesc = nint.Zero;
            properties.LogFileNameOffset = 0;
            properties.LoggerNameOffset = (uint)loggerNameLocation;
            Marshal.StructureToPtr(properties, buffer, false);
            nint dest = nint.Add(buffer, loggerNameLocation);
            Marshal.Copy(loggerName, 0, dest, loggerName.Length);
            m_PropertiesBuffer = buffer;
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
            var dest = nint.Add(descriptor.Ptr,(int)Marshal.OffsetOf<EVENT_FILTER_EVENT_ID>("Events"));
            var ids = EventIds.ConvertAll(id => (ushort)id).ToArray();
            var byteSize = ids.Length * Marshal.SizeOf(typeof(ushort));
            var bytes = new byte[byteSize];
            Buffer.BlockCopy(ids, 0, bytes, 0, byteSize);
            Marshal.Copy(bytes, 0, dest, byteSize);
            m_FilterDescriptors.Add(descriptor);
        }
    }
}
