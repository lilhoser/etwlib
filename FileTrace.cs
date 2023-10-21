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
using static etwlib.NativeTraceControl;

namespace etwlib
{
    using static TraceLogger;

    public class FileTrace : TraceSession
    {
        public FileTrace(string EtlFileName)
        {
            m_LogFile.LogFileName = EtlFileName;
            m_LogFile.ProcessTraceMode = ProcessTraceMode.EventRecord;
        }

        ~FileTrace()
        {
            Dispose(false);
        }

        public override void Start()
        {
            Trace(TraceLoggerType.FileTrace,
                  TraceEventType.Information,
                  $"Starting FileTrace for log {m_LogFile.LogFileName}");
            return;
        }
    }
}
