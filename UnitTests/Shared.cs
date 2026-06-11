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
using Microsoft.Win32;
using symbolresolver;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static etwlib.NativeTraceConsumer;
using static etwlib.NativeTraceControl;

// Every test in this suite creates a real ETW session named
// "Unit Test Real-Time Tracing". If two classes execute in parallel they
// race on that name, causing sporadic ProcessTrace() failures (observed
// 0x57 / 0x1069). Force serial execution for the whole assembly.
[assembly: DoNotParallelize]

namespace UnitTests
{
    public static class Shared
    {
        [Flags]
        public enum RegistryProviderKeywords : ulong
        {
            EnumerateKey = 0x800,
            CreateKey = 0x1000,
            OpenKey = 0x2000,
            QueryKey = 0x8000
        }

        public static readonly Guid s_RpcEtwGuid =
            new Guid("6ad52b32-d609-4be9-ae07-ce8dae937e39");
        public static readonly Guid s_WinKernelRegistryGuid =
            new Guid("70eb4f03-c1de-4f73-a051-33d13d5413bd");
        public static readonly Guid s_LoggingChannel =
            new Guid("4bd2826e-54a1-4ba9-bf63-92b73ea1ac4a");
        public static readonly int s_NumEvents = 1000;
        public static readonly string s_DbgHelpLocation = @"C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\dbghelp.dll";
        public static readonly string s_SymbolPath = @"srv*c:\symbols*https://msdl.microsoft.com/download/symbols";
        public static SymbolResolver s_Resolver = new SymbolResolver(s_SymbolPath, s_DbgHelpLocation);
        private static bool s_Initialized = false;

        public static void ConfigureLoggers()
        {
            etwlib.TraceLogger.Initialize();
            etwlib.TraceLogger.SetLevel(SourceLevels.Error);
            symbolresolver.TraceLogger.Initialize();
            symbolresolver.TraceLogger.SetLevel(SourceLevels.Error);
        }

        public static async Task ConfigureSymbolResolver()
        {
            if (s_Initialized)
            {
                return;
            }

            try
            {
                Assert.IsTrue(await s_Resolver.Initialize());
                s_Initialized = true;
            }
            catch (Exception ex)
            {
                Assert.Fail($"Unable to initialize SymbolResolver: {ex.Message}");
            }
        }

        public static async Task StackwalkCheck(int ProcessId, List<ulong> StackwalkAddresses)
        {
            //
            // Note: pid re-use could (rarely) cause us to miss importing symbol
            // information for loaded modules in the reused process.
            //
            try
            {
                foreach (var address in StackwalkAddresses)
                {
                    var resolved = await s_Resolver.ResolveUserAddress(
                        ProcessId, address, SymbolFormattingOption.SymbolAndModule);
                    Assert.IsNotNull(resolved);
                    //
                    // Stackwalk captures for user mode modules vs km modules will differ,
                    // and this is really just a best-guess.
                    //
                    var found = resolved.Contains("EtwEventWriteTransfer") ||
                           resolved.Contains("NtTraceEvent") ||
                           resolved.Contains("EtwEventWrite") ||
                           resolved.Contains("ZwTraceEvent") ||
                           resolved.Contains("rpcrt4") ||
                           resolved.Contains("ntoskrnl") ||
                           resolved.Contains("ntkrnlpa") ||
                           resolved.Contains("ntkrnlmp") ||
                           resolved.Contains("ntkrpamp");
                    Assert.IsTrue(found);
                }
            }
            catch (InvalidOperationException ex)
            {
                Assert.Fail($"SymbolResolver exception: {ex.Message}");
            }
        }

        //
        // Filtered tests need far fewer matching events than the unfiltered
        // s_NumEvents to prove the filter plumbing, and the stimulus classes
        // below produce them at hundreds-per-second, so filtered suites finish
        // in seconds instead of gambling on ambient machine activity.
        //
        public static readonly int s_FilteredNumEvents = 100;

        /// <summary>
        /// Consumes events until MatchedCount reaches MinEvents or the deadline
        /// passes, then asserts the threshold was met. The deadline cannot live
        /// only in the BufferCallback: when a kernel-side filter drops every
        /// event, no buffers are delivered and the callback never fires — the
        /// watchdog stops the session, which reliably unblocks ProcessTrace
        /// (RealTimeTrace.Stop() is retryable as of the stop-handle fix).
        /// </summary>
        public static void ConsumeWithDeadline(
            RealTimeTrace Trace,
            EventRecordCallback OnEvent,
            Func<int> MatchedCount,
            int MinEvents,
            TimeSpan Deadline,
            string Description)
        {
            var stopwatch = Stopwatch.StartNew();
            using var watchdog = new Timer(_ =>
            {
                if (stopwatch.Elapsed > Deadline)
                {
                    Trace.Stop();
                }
            }, null, 1000, 1000);

            Trace.Consume(OnEvent, new BufferCallback((LogFile) =>
            {
                try
                {
                    _ = (EVENT_TRACE_LOGFILE)Marshal.PtrToStructure(
                        LogFile, typeof(EVENT_TRACE_LOGFILE))!;
                }
                catch (Exception ex)
                {
                    Assert.Fail($"Unable to cast EVENT_TRACE_LOGFILE: {ex.Message}");
                }

                if (MatchedCount() >= MinEvents || stopwatch.Elapsed > Deadline)
                {
                    return 0;
                }
                return 1;
            }));

            Assert.IsTrue(MatchedCount() >= MinEvents,
                $"{Description}: only {MatchedCount()}/{MinEvents} matching events " +
                $"within {Deadline.TotalSeconds:F0}s — the filter or the test stimulus " +
                $"is broken. These tests generate their own stimulus and must never " +
                $"wait on ambient machine activity.");
        }

        /// <summary>
        /// Background registry churn against a private scratch key so tests of
        /// the kernel registry provider never depend on ambient activity. Each
        /// iteration produces the full mix the filter suites await: CreateKey,
        /// SetValue/QueryValue of a value literally named "ConfigFlags" (the
        /// payload StringEquality target), a failing QueryValue (status != 0),
        /// QueryKey (success and, via a deleted-key probe, error level),
        /// value/key enumeration (success and error level), OpenKey misses,
        /// and DeleteValue/DeleteKey.
        /// </summary>
        public sealed class RegistryStimulus : IDisposable
        {
            private const string RootPath = @"Software\etwlib-tests";
            private readonly CancellationTokenSource m_Cts = new CancellationTokenSource();
            private readonly Task m_Loop;

            public RegistryStimulus()
            {
                m_Loop = Task.Run(() =>
                {
                    var iteration = 0;
                    using (var root = Registry.CurrentUser.CreateSubKey(RootPath)!)
                    {
                        while (!m_Cts.IsCancellationRequested)
                        {
                            iteration++;
                            var name = $"stimulus-{iteration % 8}";
                            try
                            {
                                using (var key = root.CreateSubKey(name)!)
                                {
                                    key.SetValue("ConfigFlags", iteration);
                                    _ = key.GetValue("ConfigFlags");
                                    _ = key.GetValue("zzz-missing");      // QueryValue, status != 0
                                    _ = key.SubKeyCount;                  // QueryKey, status == 0
                                    _ = key.GetValueNames();              // EnumerateValueKey
                                    _ = root.GetSubKeyNames();            // EnumerateKey
                                    _ = root.OpenSubKey("zzz-missing");   // OpenKey, status != 0
                                    key.DeleteValue("ConfigFlags", false);
                                    root.DeleteSubKey(name, false);
                                    try { _ = key.SubKeyCount; } catch { }      // QueryKey on deleted key (error level)
                                    try { _ = key.GetSubKeyNames(); } catch { } // EnumerateKey on deleted key (error level)
                                }
                            }
                            catch
                            {
                                //
                                // The stimulus must never fail or stall the test.
                                //
                            }

                            if (iteration % 100 == 0)
                            {
                                Thread.Sleep(1);
                            }
                        }
                    }

                    try { Registry.CurrentUser.DeleteSubKeyTree(RootPath, false); } catch { }
                });
            }

            public void Dispose()
            {
                m_Cts.Cancel();
                try { m_Loop.Wait(TimeSpan.FromSeconds(5)); } catch { }
                m_Cts.Dispose();
            }
        }

        /// <summary>
        /// Background LRPC churn (OpenSCManager/CloseServiceHandle against the
        /// local SCM) so tests of the RPC provider never depend on ambient
        /// activity. Every call emits RpcClientCallStart (5) / RpcClientCallStop
        /// (7) from THIS process, which also makes own-pid / own-exe filter
        /// targets deterministic.
        /// </summary>
        public sealed class RpcStimulus : IDisposable
        {
            [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern nint OpenSCManagerW(string? MachineName, string? DatabaseName, uint DesiredAccess);

            [DllImport("advapi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool CloseServiceHandle(nint Handle);

            private const uint SC_MANAGER_CONNECT = 0x0001;

            private readonly CancellationTokenSource m_Cts = new CancellationTokenSource();
            private readonly Task m_Loop;

            public RpcStimulus()
            {
                m_Loop = Task.Run(() =>
                {
                    var iteration = 0;
                    while (!m_Cts.IsCancellationRequested)
                    {
                        iteration++;
                        var handle = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
                        if (handle != nint.Zero)
                        {
                            CloseServiceHandle(handle);
                        }

                        if (iteration % 25 == 0)
                        {
                            Thread.Sleep(1);
                        }
                    }
                });
            }

            public void Dispose()
            {
                m_Cts.Cancel();
                try { m_Loop.Wait(TimeSpan.FromSeconds(5)); } catch { }
                m_Cts.Dispose();
            }
        }
    }
}
