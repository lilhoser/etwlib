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
using static etwlib.NativeTraceConsumer;
using static etwlib.NativeTraceControl;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace etwlib
{
    using static TraceLogger;

    public abstract class TraceSession : IDisposable
    {
        protected EVENT_TRACE_LOGFILE m_LogFile;
        protected bool m_Disposed;
        protected long m_PerfFreq;
        protected List<EnabledProvider> m_EnabledProviders;

        public TraceSession()
        {
            m_LogFile = new EVENT_TRACE_LOGFILE();
            m_Disposed = false;
            m_PerfFreq = 0;
            m_EnabledProviders = new List<EnabledProvider>();
        }

        protected virtual void Dispose(bool disposing)
        {
            Trace(TraceLoggerType.TraceSession,
                  TraceEventType.Information,
                  "Disposing FileTrace");
            if (m_Disposed)
            {
                return;
            }
            m_Disposed = true;
        }

        public virtual void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public EnabledProvider AddProvider(Guid Id, string Name, byte traceLevel, ulong matchAnyKeyword, ulong matchAllKeyword)
        {
            var provider = new EnabledProvider(Id, Name, traceLevel, matchAllKeyword, matchAnyKeyword);
            m_EnabledProviders.Add(provider);
            return provider;
        }

        public EnabledProvider AddProvider(Guid Id, string Name, EventTraceLevel traceLevel, ulong matchAnyKeyword, ulong matchAllKeyword)
        {
            var provider = new EnabledProvider(Id, Name, (byte)traceLevel, matchAllKeyword, matchAnyKeyword);
            m_EnabledProviders.Add(provider);
            return provider;
        }

        public void AddProvider(EnabledProvider Provider)
        {
            m_EnabledProviders.Add(Provider);
        }

        public abstract void Start();
        public abstract void Stop();

        public void Consume(
            EventRecordCallback EventCallback,
            BufferCallback BufferCallback
            )
        {
            m_LogFile.BufferCallback = BufferCallback;
            m_LogFile.EventCallback = EventCallback;

            //
            // Pin both delegates for the duration of the native trace. Without
            // this, the CLR is free to relocate or collect the thunk the kernel
            // is holding a pointer to; when ETW fires an in-flight callback
            // after ProcessTrace returns, the target may already be invalid
            // and the runtime fires "Attempt to execute managed code after the
            // .NET runtime thread state has been destroyed" during testhost
            // shutdown or between tests.
            //
            var eventCallbackHandle = GCHandle.Alloc(EventCallback);
            var bufferCallbackHandle = GCHandle.Alloc(BufferCallback);

            var logFilePointer = Marshal.AllocHGlobal(Marshal.SizeOf<EVENT_TRACE_LOGFILE>());
            Marshal.StructureToPtr(m_LogFile, logFilePointer, false);
            var handle = OpenTrace(logFilePointer);
            //
            // Marshal the structure back so we can get the PerfFreq
            //
            var logfile = Marshal.PtrToStructure<EVENT_TRACE_LOGFILE>(logFilePointer);
            Marshal.FreeHGlobal(logFilePointer);
            if (handle == -1 || handle == 0)
            {
                eventCallbackHandle.Free();
                bufferCallbackHandle.Free();
                var error = "OpenTrace() returned an invalid handle:  0x" +
                    Marshal.GetLastWin32Error().ToString("X");
                Trace(TraceLoggerType.TraceSession,
                      TraceEventType.Error,
                      error);
                throw new Exception(error);
            }

            Trace(TraceLoggerType.TraceSession,
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
                    Trace(TraceLoggerType.TraceSession,
                          TraceEventType.Error,
                          error);
                    throw new Exception(error);
                }
                Trace(TraceLoggerType.TraceSession,
                      TraceEventType.Information,
                      "Trace processing successfully completed.");
            }
            finally
            {
                CloseTrace(handle);

                //
                // ETW can deliver one or more in-flight callbacks after
                // ProcessTrace returns and even briefly after CloseTrace
                // unwinds. Keep both delegate handles alive a little longer
                // so any final callback hits a live target, then release.
                //
                GC.KeepAlive(EventCallback);
                GC.KeepAlive(BufferCallback);
                eventCallbackHandle.Free();
                bufferCallbackHandle.Free();
            }
        }

        public long GetPerfFreq()
        {
            return m_PerfFreq;
        }
    }
}
