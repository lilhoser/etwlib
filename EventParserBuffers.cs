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
using System.Runtime.InteropServices;

namespace etwlib
{
    using static NativeTraceConsumer;

    public class EventParserBuffers : IDisposable
    {
        public EVENT_RECORD m_Event;
        public TRACE_EVENT_INFO m_TraceEventInfo;
        public EVENT_MAP_INFO m_MapInfo;
        public nint m_TraceEventInfoBuffer;
        public nint m_TdhMapBuffer;
        public nint m_TdhOutputBuffer;
        public nint m_EventBuffer;
        public static int ETW_MAX_EVENT_SIZE = 65536; // 65K
        public static int MAP_SIZE = 1024 * 4000; // 4MB
        public static int TDH_STR_SIZE = 1024 * 4000; // 4MB
        public static int TRACE_EVENT_INFO_SIZE = 1024 * 4000; // 4MB
        private bool m_Disposed;

        public EventParserBuffers()
        {
            //
            // Pre-allocate large buffers to re-use across all events that
            // reuse this parser. This helps performance tremendously.
            //
            // The map buffer holds manifest data that can be ulong.max size.
            // We pick a decently large size here (4MB)
            //
            // The TDH output buffer contains arbitrary unicode string data,
            // and can be as large as the ETW provider wants AFAIK.
            //
            // Event buffer can never exceed 65k.
            //
            // The TRACE_EVENT_INFO structure is variable-length and the total
            // size depends on the ETW provider's manifest.
            //
            m_TdhMapBuffer = Marshal.AllocHGlobal(MAP_SIZE);
            m_TdhOutputBuffer = Marshal.AllocHGlobal(TDH_STR_SIZE);
            m_EventBuffer = Marshal.AllocHGlobal(ETW_MAX_EVENT_SIZE);
            m_TraceEventInfoBuffer = Marshal.AllocHGlobal(TRACE_EVENT_INFO_SIZE);
        }

        ~EventParserBuffers()
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

            if (m_TdhMapBuffer != nint.Zero)
            {
                Marshal.FreeHGlobal(m_TdhMapBuffer);
            }

            if (m_TdhOutputBuffer != nint.Zero)
            {
                Marshal.FreeHGlobal(m_TdhOutputBuffer);
            }

            if (m_EventBuffer != nint.Zero)
            {
                Marshal.FreeHGlobal(m_EventBuffer);
            }

            if (m_TraceEventInfoBuffer != nint.Zero)
            {
                Marshal.FreeHGlobal(m_TraceEventInfoBuffer);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void SetEvent(EVENT_RECORD Event)
        {
            m_Event = Event;
            Marshal.StructureToPtr(Event, m_EventBuffer, false);
        }

        public void SetTraceInfo()
        {
            m_TraceEventInfo = (TRACE_EVENT_INFO)Marshal.PtrToStructure(
                m_TraceEventInfoBuffer, typeof(TRACE_EVENT_INFO))!;
        }

        public void SetTraceInfo(nint Buffer)
        {
            m_TraceEventInfoBuffer = Buffer;
            SetTraceInfo();
        }

        public void SetMapInfoBuffer()
        {
            m_MapInfo = (EVENT_MAP_INFO)Marshal.PtrToStructure(
                m_TdhMapBuffer, typeof(EVENT_MAP_INFO))!;
        }
    }
}
