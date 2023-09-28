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
using System.Text;

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
        public static List<int> s_InitializedPids = new List<int>();
        private static bool s_Initialized = false;

        public static void ConfigureLoggers()
        {
            etwlib.TraceLogger.Initialize();
            etwlib.TraceLogger.SetLevel(SourceLevels.Error);
            symbolresolver.TraceLogger.Initialize();
            symbolresolver.TraceLogger.SetLevel(SourceLevels.Error);
        }

        public static void ConfigureSymbolResolver()
        {
            if (s_Initialized)
            {
                return;
            }

            try
            {
                s_Resolver.Initialize();
                s_Initialized = true;
            }
            catch (Exception ex)
            {
                Assert.Fail($"Unable to initialize SymbolResolver: {ex.Message}");
            }
        }

        public static bool StackwalkCheck(int ProcessId, List<ulong> StackwalkAddresses, out bool Skip)
        {
            Skip = false;

            //
            // Note: pid re-use could (rarely) cause us to miss importing symbol
            // information for loaded modules in the reused process.
            //
            if (!s_InitializedPids.Contains(ProcessId))
            {
                try
                {
                    s_Resolver.InitializeForProcess(ProcessId);
                    s_InitializedPids.Add(ProcessId);
                }
                catch (Exception)
                {
                    //
                    // The process could have died, become frozen (UWP), loaded or
                    // unload a module, etc etc etc.
                    //
                    Skip = true;
                    return false;
                }
            }

            var sb = new StringBuilder();
            foreach (var address in StackwalkAddresses)
            {
                var result = s_Resolver.GetFormattedSymbol(address);
                sb.AppendLine(result);
            }

            var final = sb.ToString();

            //
            // Stackwalk captures for user mode modules vs km modules will differ,
            // and this is really just a best-guess.
            //
            var found = final.Contains("EtwEventWriteTransfer") ||
                   final.Contains("ZwTraceEvent") ||
                   final.Contains("rpcrt4") ||
                   final.Contains("ntoskrnl") ||
                   final.Contains("ntkrnlpa") ||
                   final.Contains("ntkrnlmp") ||
                   final.Contains("ntkrpamp");
            Debug.Assert(found);
            return found;
        }
    }
}
