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
using static etwlib.NativeTraceConsumer;
using static etwlib.NativeTraceControl;

namespace etwlib
{
    using static TraceLogger;

    public class FileTrace : IDisposable
    {
        private bool m_Disposed;
        private readonly string m_EtlFileName;
        private long m_PerfFreq;

        public FileTrace(string EtlFileName)
        {
            m_EtlFileName = EtlFileName;
            m_Disposed = false;
        }

        ~FileTrace()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (m_Disposed)
            {
                return;
            }

            Trace(TraceLoggerType.FileTrace,
                TraceEventType.Information,
                "Disposing FileTrace");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Consume(
            EventRecordCallback EventCallback,
            BufferCallback BufferCallback
            )
        {
            var logfile = new EVENT_TRACE_LOGFILE()
            {
                LogFileName = m_EtlFileName,
                EventCallback = EventCallback,
                BufferCallback = BufferCallback,
                ProcessTraceMode = ProcessTraceMode.EventRecord
            };
            var logFilePointer = Marshal.AllocHGlobal(Marshal.SizeOf(logfile));
            Trace(TraceLoggerType.FileTrace,
                TraceEventType.Information,
                "Consuming events from ETL file " + m_EtlFileName);
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
                var error = "OpenTrace() returned an invalid handle:  0x" +
                    Marshal.GetLastWin32Error().ToString("X");
                Trace(TraceLoggerType.FileTrace,
                    TraceEventType.Error,
                    error);
                throw new Exception(error);
            }

            Trace(TraceLoggerType.FileTrace,
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
                    var error = "ProcessTrace() failed: 0x" + status.ToString("X") +
                        ", GetLastError: " + Marshal.GetLastWin32Error().ToString("X");
                    Trace(TraceLoggerType.FileTrace,
                        TraceEventType.Error,
                        error);
                    throw new Exception(error);
                }
                Trace(TraceLoggerType.FileTrace,
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
    }
}
