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
using Microsoft.VisualStudio.TestTools.UnitTesting;
using symbolresolver;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

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
    }
}
