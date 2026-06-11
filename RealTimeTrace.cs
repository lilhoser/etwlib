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

    public class RealTimeTrace : TraceSession
    {
        private readonly string m_SessionName;
        private readonly Guid m_SessionGuid;
        private nint m_PropertiesBuffer;
        private long m_SessionHandle;
        private readonly object m_Lock = new object();

        //
        // Win32 status of the most recent Stop() attempt. ERROR_SUCCESS when the
        // session was stopped (or was already gone); any other value means the
        // kernel-side session may still be running and Stop() can be retried.
        //
        public uint LastStopStatus { get; private set; }

        public RealTimeTrace(string SessionName) : base()
        {
            m_SessionName = SessionName;
            m_SessionGuid = Guid.NewGuid();
            m_PropertiesBuffer = nint.Zero;
            m_SessionHandle = nint.Zero;
            m_LogFile.LoggerName = m_SessionName;
            m_LogFile.ProcessTraceMode = ProcessTraceMode.EventRecord | ProcessTraceMode.RealTime;
        }

        ~RealTimeTrace()
        {
            Dispose(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (m_Disposed)
            {
                return;
            }

            Trace(TraceLoggerType.RealTimeTrace,
                TraceEventType.Information,
                "Disposing RealTimeTrace");

            Stop();

            if (m_PropertiesBuffer != nint.Zero)
            {
                Marshal.FreeHGlobal(m_PropertiesBuffer);
            }

            base.Dispose(disposing);
        }

        public override void Start()
        {
            Trace(TraceLoggerType.RealTimeTrace,
                  TraceEventType.Information,
                  $"Starting RealTimeTrace {m_SessionName}...");

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
            foreach (var provider in m_EnabledProviders)
            {
                status = provider.Enable(m_SessionHandle);
                if (status != ERROR_SUCCESS)
                {
                    var error = $"EnableTraceEx2() failed for {provider}: 0x{status:X}";
                    Trace(TraceLoggerType.RealTimeTrace,
                          TraceEventType.Error,
                          error);
                    throw new Exception(error);
                }

                Trace(TraceLoggerType.RealTimeTrace,
                      TraceEventType.Information,
                      $"Provider {provider} enabled successfully.");
            }
        }

        public override void Stop()
        {
            lock (m_Lock)
            {
                if (m_SessionHandle != 0 && m_SessionHandle != -1 && m_PropertiesBuffer != nint.Zero)
                {
                    Trace(TraceLoggerType.RealTimeTrace,
                          TraceEventType.Information,
                          $"Stopping RealTimeTrace {m_SessionName}...");
                    uint result;
                    foreach (var provider in m_EnabledProviders)
                    {
                        result = provider.Disable(m_SessionHandle);
                        if (result != ERROR_SUCCESS)
                        {
                            Trace(TraceLoggerType.RealTimeTrace,
                                  TraceEventType.Error,
                                  $"RealTimeTrace dispose could not disable provider: " +
                                  $"{result:X}");
                        }
                    }
                    result = ControlTrace(
                        m_SessionHandle,
                        m_SessionName,
                        m_PropertiesBuffer,
                        ControlCode.Stop);
                    if (result == ERROR_WMI_INSTANCE_NOT_FOUND)
                    {
                        //
                        // The session is already gone — stopped externally or never
                        // fully started. That is the desired end state.
                        //
                        result = ERROR_SUCCESS;
                    }
                    else if (result != ERROR_SUCCESS)
                    {
                        //
                        // The handle-based stop failed. A stale or invalid handle must
                        // not leave the kernel-side session running (it would survive
                        // this process and orphan), so retry by session name, which is
                        // how external tools like logman address sessions.
                        //
                        Trace(TraceLoggerType.RealTimeTrace,
                              TraceEventType.Warning,
                              $"Handle-based stop failed (0x{result:X}); " +
                              $"retrying by session name...");
                        result = ControlTrace(
                            0,
                            m_SessionName,
                            m_PropertiesBuffer,
                            ControlCode.Stop);
                        if (result == ERROR_WMI_INSTANCE_NOT_FOUND)
                        {
                            result = ERROR_SUCCESS;
                        }
                    }

                    LastStopStatus = result;
                    if (result == ERROR_SUCCESS)
                    {
                        m_SessionHandle = 0;
                    }
                    else
                    {
                        //
                        // Do NOT zero the handle here: doing so makes every later
                        // Stop()/Dispose() a silent no-op and permanently orphans the
                        // kernel-side session. Keeping the handle means the caller
                        // (or Dispose) can retry.
                        //
                        Trace(TraceLoggerType.RealTimeTrace,
                              TraceEventType.Error,
                              $"RealTimeTrace could not stop trace: 0x{result:X}; " +
                              $"session handle retained so Stop() can be retried.");
                    }
                }
            }
        }

        public static long Open(string Name)
        {
            EVENT_TRACE_LOGFILE logfile = new EVENT_TRACE_LOGFILE();
            logfile.LoggerName = Name;
            logfile.ProcessTraceMode = ProcessTraceMode.EventRecord | ProcessTraceMode.RealTime;
            Trace(TraceLoggerType.RealTimeTrace,
                  TraceEventType.Information,
                  $"Opening existing RealTimeTrace {Name}...");
            var logFilePointer = Marshal.AllocHGlobal(Marshal.SizeOf<EVENT_TRACE_LOGFILE>());
            Marshal.StructureToPtr(logfile, logFilePointer, false);
            var handle = OpenTrace(logFilePointer);
            Marshal.FreeHGlobal(logFilePointer);
            if (handle == -1 || handle == 0)
            {
                var error = "OpenTrace() returned an invalid handle:  0x" +
                    Marshal.GetLastWin32Error().ToString("X");
                Trace(TraceLoggerType.TraceSession,
                      TraceEventType.Error,
                      error);
                throw new Exception(error);
            }
            return handle;
        }

        private
        void
        GenerateTraceProperties()
        {
            var loggerName = Encoding.Unicode.GetBytes(m_SessionName + "\0");
            var loggerNameLocation = Marshal.SizeOf<EVENT_TRACE_PROPERTIES>();
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
    }
}
