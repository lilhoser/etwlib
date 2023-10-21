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

namespace etwlib
{
    public static class TraceLogger
    {
        private static MemoryStream m_BaseStream = new MemoryStream();
        private static TextWriterTraceListener m_TraceListener =
            new TextWriterTraceListener(m_BaseStream, "etwlibListener");
        private static SourceSwitch m_Switch =
            new SourceSwitch("etwlibListener", "Verbose");
        private static TraceSource[] Sources = {
            new TraceSource("TraceSession", SourceLevels.Verbose),
            new TraceSource("RealTimeTrace", SourceLevels.Verbose),
            new TraceSource("FileTrace", SourceLevels.Verbose),
            new TraceSource("EventParser", SourceLevels.Verbose),
            new TraceSource("ProviderParser", SourceLevels.Verbose),
            new TraceSource("ManifestParser", SourceLevels.Verbose),
            new TraceSource("SessionParser", SourceLevels.Verbose),
        };

        public enum TraceLoggerType
        {
            TraceSession,
            RealTimeTrace,
            FileTrace,
            EtwEventParser,
            EtwProviderParser,
            EtwManifestParser,
            EtwSessionParser,
            Max
        }

        public static void Initialize()
        {
            System.Diagnostics.Trace.AutoFlush = true;
            foreach (var source in Sources)
            {
                source.Listeners.Add(m_TraceListener);
                source.Switch = m_Switch;
            }
        }

        public static void SetLevel(SourceLevels Level)
        {
            m_Switch.Level = Level;
        }

        public static void Trace(TraceLoggerType Type, TraceEventType EventType, string Message)
        {
            if (Type >= TraceLoggerType.Max)
            {
                throw new Exception("Invalid logger type");
            }
            Sources[(int)Type].TraceEvent(EventType, 1, Message);
        }

        public static MemoryStream GetStream()
        {
            return m_BaseStream;
        }
    }
}
