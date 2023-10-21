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
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace etwlib
{
    using static TraceLogger;
    using static NativeTraceConsumer;
    using static NativeTraceControl;

    public static class SessionParser
    {
        public
        static
        List<ParsedEtwSession>
        GetSessions()
        {
            var results = new List<ParsedEtwSession>();
            nint buffer = nint.Zero;

            try
            {
                uint result = 0;
                uint bufferSize = 0;
                uint returnLength = 0;

                for (; ; )
                {
                    result = EnumerateTraceGuidsEx(
                        TRACE_QUERY_INFO_CLASS.TraceGuidQueryList,
                        nint.Zero,
                        0,
                        buffer,
                        bufferSize,
                        ref returnLength);
                    if (result == ERROR_SUCCESS)
                    {
                        break;
                    }
                    else if (result != ERROR_INSUFFICIENT_BUFFER)
                    {
                        var error = $"EnumerateTraceGuidsEx failed: 0x{result:X}";
                        Trace(TraceLoggerType.EtwSessionParser,
                            TraceEventType.Warning,
                            error);
                        throw new Exception(error);
                    }

                    buffer = Marshal.AllocHGlobal((int)returnLength);
                    bufferSize = returnLength;
                    if (buffer == nint.Zero)
                    {
                        throw new Exception("Out of memory");
                    }
                }

                if (buffer == nint.Zero || bufferSize == 0)
                {
                    throw new Exception("EnumerateTraceGuidsEx returned null " +
                        " or empty buffer.");
                }

                int numProviders = (int)bufferSize / Marshal.SizeOf(typeof(Guid));
                var pointer = buffer;

                for (int i = 0; i < numProviders; i++)
                {
                    var guid = (Guid)Marshal.PtrToStructure(pointer, typeof(Guid))!;
                    var session = GetSessions(guid);
                    if (session == null)
                    {
                        continue;
                    }
                    results.AddRange(session);
                    pointer = nint.Add(pointer, Marshal.SizeOf(typeof(Guid)));
                }

                results.Sort();
                return results;
            }
            catch (Exception ex)
            {
                Trace(TraceLoggerType.EtwProviderParser,
                    TraceEventType.Error,
                    $"Exception in GetProviders(): {ex.Message}");
                throw;
            }
            finally
            {
                if (buffer != nint.Zero)
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }

        public static List<ParsedEtwSession>? GetSessions(string ProviderName)
        {
            var provider = ProviderParser.GetProvider(ProviderName);
            if (provider == null)
            {
                throw new Exception($"Provider {ProviderName} not found");
            }
            return GetSessions(provider.Id);
        }

        public static List<ParsedEtwSession>? GetSessions(Guid ProviderId)
        {
            var inBufferSize = (uint)Marshal.SizeOf(typeof(Guid));
            var inBuffer = Marshal.AllocHGlobal((int)inBufferSize);
            if (inBuffer == nint.Zero)
            {
                throw new Exception("Out of memory");
            }

            Marshal.StructureToPtr(ProviderId, inBuffer, false);

            var sessions = new List<ParsedEtwSession>();
            var outBuffer = nint.Zero;
            uint outBufferSize = 0;
            uint returnLength = 0;

            try
            {
                for ( ; ; )
                {
                    var result = EnumerateTraceGuidsEx(
                        TRACE_QUERY_INFO_CLASS.TraceGuidQueryInfo,
                        inBuffer,
                        inBufferSize,
                        outBuffer,
                        outBufferSize,
                        ref returnLength);
                    if (result == ERROR_SUCCESS)
                    {
                        break;
                    }
                    else if (result == ERROR_WMI_GUID_NOT_FOUND)
                    {
                        //
                        // This can occur if the GUID is registered but not loaded
                        //
                        return null;
                    }
                    else if (result != ERROR_INSUFFICIENT_BUFFER)
                    {
                        var error = $"EnumerateTraceGuidsEx failed: 0x{result:X}";
                        Trace(TraceLoggerType.EtwSessionParser,
                            TraceEventType.Error,
                            error);
                        throw new Exception(error);
                    }

                    outBuffer = Marshal.AllocHGlobal((int)returnLength);
                    outBufferSize = returnLength;
                    if (outBuffer == nint.Zero)
                    {
                        throw new Exception("Out of memory");
                    }
                }

                if (outBuffer == nint.Zero || outBufferSize == 0)
                {
                    throw new Exception("EnumerateTraceGuidsEx returned null " +
                        " or empty buffer.");
                }

                var pointer = outBuffer;
                var info = (TRACE_GUID_INFO)Marshal.PtrToStructure(
                    pointer, typeof(TRACE_GUID_INFO))!;
                pointer = nint.Add(pointer, Marshal.SizeOf(typeof(TRACE_GUID_INFO)));

                //
                // NB: there can be multiple instances of a provider with the same
                // GUID if they're hosted in a DLL loaded in multiple processes.
                //
                for (int i = 0; i < info.InstanceCount; i++)
                {
                    var instance = (TRACE_PROVIDER_INSTANCE_INFO)Marshal.PtrToStructure(
                        pointer, typeof(TRACE_PROVIDER_INSTANCE_INFO))!;
                    if (instance.EnableCount > 0)
                    {
                        var sessionPointer = pointer;
                        for (int j = 0; j < instance.EnableCount; j++)
                        {
                            var sessionInfo = (TRACE_ENABLE_INFO)Marshal.PtrToStructure(
                                sessionPointer, typeof(TRACE_ENABLE_INFO))!;
                            var enabledProvider = new SessionEnabledProvider(
                                ProviderId,
                                instance.Pid,
                                instance.Flags,
                                sessionInfo.Level,
                                sessionInfo.EnableProperty,
                                sessionInfo.MatchAnyKeyword,
                                sessionInfo.MatchAllKeyword);
                            var session = new ParsedEtwSession(sessionInfo.LoggerId);
                            if (!sessions.Contains(session))
                            {
                                sessions.Add(session);
                            }
                            else
                            {
                                session = sessions.FirstOrDefault(s => s == session);
                            }
                            session!.EnabledProviders.Add(enabledProvider);
                            sessionPointer = nint.Add(sessionPointer,
                                Marshal.SizeOf(typeof(TRACE_ENABLE_INFO)));
                        }
                    }
                    pointer = nint.Add(pointer, (int)instance.NextOffset);
                }
                return sessions;
            }
            catch (Exception ex)
            {
                Trace(TraceLoggerType.EtwSessionParser,
                    TraceEventType.Error,
                    $"Exception in GetSessionsForProvider(): {ex.Message}");
                throw;
            }
            finally
            {
                if (outBuffer != nint.Zero)
                {
                    Marshal.FreeHGlobal(outBuffer);
                }
                if (inBuffer != nint.Zero)
                {
                    Marshal.FreeHGlobal(inBuffer);
                }
            }
        }
    }
}
