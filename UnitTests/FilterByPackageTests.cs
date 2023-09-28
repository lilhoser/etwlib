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
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using static etwlib.NativeTraceConsumer;
using static etwlib.NativeTraceControl;
using static UnitTests.Shared;

namespace UnitTests
{
    [TestClass]
    public class FilterByPackageTests
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern int GetPackageFullName(
            [In] nint hProcess,
            [In, Out] ref uint packageFullNameLength,
            [Out] StringBuilder fullName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern int GetApplicationUserModelId(
            [In] nint hProcess,
            [In, Out] ref uint applicationUserModelIdLength,
            [Out] StringBuilder applicationUserModelId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern int ParseApplicationUserModelId(
            [In] string applicationUserModelId,
            [In, Out] ref uint packageFamilyNameLength,
            [In] StringBuilder packageFamilyName,
            [In, Out] ref uint packageRelativeApplicationIdLength,
            [Out] StringBuilder packageRelativeApplicationId);

        [DataTestMethod]
        [DataRow(true, 5)]
        [DataRow(false, 5)]
        [Ignore] // MS Store apps (UWP) are typically suspended, so even if we could
                 // find one like this test does, it won't be emitting events. Need
                 // our own dummy UWP app to do this test.
        public void Basic(bool TestPackageId, int Attempts)
        {
            var packages = new Dictionary<string, string>();

            ConfigureLoggers();

            //
            // Find all running Windows store apps. We'll try to consume ETW events from any
            // of them that are emitting events, up to Attempts.
            //
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.Id == 0 || process.Id == 4 || process.Handle == nint.Zero)
                    {
                        continue;
                    }
                }
                catch (Win32Exception)
                {
                    continue; // probably access is denied
                }
                catch (InvalidOperationException)
                {
                    continue; // probably the process died
                }
                var package = GetPackage(process.Handle);
                if (package == null)
                {
                    continue;
                }
                var (packageId, appId) = package;
                if (packages.ContainsKey(packageId))
                {
                    continue;
                }

                packages.Add(packageId, appId);
            }

            Assert.IsTrue(packages.Count > 0);

            foreach (var package in packages.Take(Attempts))
            {
                if (TryWindowsStoreApp(package.Key, package.Value, TestPackageId))
                {
                    return;
                }
            }

            Assert.Fail();
        }

        private Tuple<string, string>? GetPackage(nint ProcessHandle)
        {
            uint bufferLength = 1024;

            //
            // The package full name is a serialized form of the Package ID, which
            // consists of identifying info like: name, publisher, architecture, etc.
            // This is what the ETW filter type EVENT_FILTER_TYPE_PACKAGE_ID  wants.
            //
            var packageId = new StringBuilder((int)bufferLength);
            var result = GetPackageFullName(ProcessHandle, ref bufferLength, packageId);
            if (result != ERROR_SUCCESS)
            {
                return null;
            }

            var userModelId = new StringBuilder((int)bufferLength);
            result = GetApplicationUserModelId(ProcessHandle, ref bufferLength, userModelId);
            if (result != ERROR_SUCCESS)
            {
                return null;
            }

            //
            // The ETW filter type EVENT_FILTER_TYPE_PACKAGE_APP_ID wants the 
            // "package-relative APP ID (PRAID)" which must be parsed from
            // the "application user model ID".
            //
            var packageFamilyLength = bufferLength;
            var packageFamily = new StringBuilder((int)packageFamilyLength);
            var relativeAppIdLength = bufferLength;
            var relativeAppId = new StringBuilder((int)relativeAppIdLength);
            result = ParseApplicationUserModelId(userModelId.ToString(),
                ref packageFamilyLength,
                packageFamily,
                ref relativeAppIdLength,
                relativeAppId);
            if (result != ERROR_SUCCESS)
            {
                return null;
            }

            return new Tuple<string, string>(
                packageId.ToString(), relativeAppId.ToString());
        }

        private bool TryWindowsStoreApp(string PackageId, string AppId, bool TestPackageId)
        {
            int eventsConsumed = 0;
            var success = false;
            var stopwatch = new Stopwatch();
            var cancel = false;

            //
            // This trace will automatically terminate after a set number
            // of ETW events have been successfully consumed/parsed.
            //
            using (var trace = new RealTimeTrace(
                "Unit Test Real-Time Tracing",
                s_LoggingChannel,
                EventTraceLevel.Verbose,
                0xFFFFFFFFFFFFFFFF,
                0))
            using (var parserBuffers = new EventParserBuffers())
            {
                try
                {
                    if (TestPackageId)
                    {
                        Assert.IsNotNull(PackageId);
                        trace.SetFilteredPackageId(PackageId);
                    }
                    else
                    {
                        Assert.IsNotNull(AppId);
                        trace.SetFilteredPackageAppId(AppId);
                    }
                    trace.Start();
                    stopwatch.Start();

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

                        if (stopwatch.ElapsedMilliseconds > 10000)
                        {
                            cancel = true;
                            return;
                        }

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

                        if (parsedEvent == null)
                        {
                            //
                            // There are many failure cases that are expected, like
                            // unsupported MOF events. Ignore them.
                            //
                            return;
                        }

                        if (parsedEvent.ProcessId == 0)
                        {
                            return;
                        }

                        try
                        {
                            var process = Process.GetProcessById((int)parsedEvent.ProcessId);
                            var package = GetPackage(process.Handle);
                            if (package == null)
                            {
                                return;
                            }

                            if (TestPackageId)
                            {
                                Assert.IsTrue(PackageId == package.Item1);
                            }
                            else
                            {
                                Assert.IsTrue(AppId == package.Item2);
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            return;
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
                        if (eventsConsumed >= s_NumEvents || cancel)
                        {
                            success = true;
                            return 0;
                        }
                        return 1;
                    }));
                }
                catch (AssertFailedException ex)
                {
                    throw ex;
                }
                catch (Exception ex)
                {
                    Assert.Fail($"An exception occurred when consuming events: {ex.Message}");
                }
            }

            return success;
        }
    }
}
