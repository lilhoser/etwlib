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
using System.Text;

namespace etwlib
{
    using static NativeTraceControl;

    public class SessionEnabledProvider
    {
        public Guid ProviderId { get; set; }
        public uint ProcessId { get; set; }
        public TRACE_PROVIDER_INSTANCE_FLAGS InstanceFlags { get; set; }
        public byte Level { get; set; }
        public EnableTraceProperties EnableProperty { get; set; }
        public ulong MatchAnyKeyword { get; set; }
        public ulong MatchAllKeyword { get; set; }

        private SessionEnabledProvider() { } // For XML serialization
        public SessionEnabledProvider(
            Guid providerId,
            uint processId,
            TRACE_PROVIDER_INSTANCE_FLAGS instanceFlags,
            byte level,
            EnableTraceProperties enableProperty,
            ulong matchAnyKeyword,
            ulong matchAllKeyword)
        {
            ProviderId = providerId;
            ProcessId = processId;
            InstanceFlags = instanceFlags;
            Level = level;
            EnableProperty = enableProperty;
            MatchAnyKeyword = matchAnyKeyword;
            MatchAllKeyword = matchAllKeyword;
        }
    
        public override string ToString()
        {
            var enablePropertyStr = "";
            if (EnableProperty != 0)
            {
                enablePropertyStr = $", EnableProperty={EnableProperty}";
            }
            return $"{ProviderId} registered by PID {ProcessId}, InstanceFlags={InstanceFlags}, "+
                $"Level={Level}{enablePropertyStr}, AnyKeyword={MatchAnyKeyword:X}, "+
                $"AllKeyword={MatchAllKeyword:X}";
        }
    }

    public class ParsedEtwSession : IEquatable<ParsedEtwSession>, IComparable<ParsedEtwSession>
    {
        public ushort LoggerId { get; set; }
        public List<SessionEnabledProvider> EnabledProviders { get; set; }

        private ParsedEtwSession() // For xml serialization
        {
            LoggerId = 0;
            EnabledProviders = new List<SessionEnabledProvider>();
        }

        public ParsedEtwSession(ushort Id)
        {
            LoggerId = Id;
            EnabledProviders = new List<SessionEnabledProvider>();
        }

        public override bool Equals(object? Other)
        {
            if (Other == null)
            {
                return false;
            }
            var field = Other as ParsedEtwSession;
            return Equals(field);
        }

        public bool Equals(ParsedEtwSession? Other)
        {
            if (Other == null)
            {
                return false;
            }
            return LoggerId == Other.LoggerId;
        }

        public static bool operator ==(ParsedEtwSession? Session1, ParsedEtwSession? Session2)
        {
            if ((object)Session1 == null || (object)Session2 == null)
                return Equals(Session1, Session2);
            return Session1.Equals(Session2);
        }

        public static bool operator !=(ParsedEtwSession? Session1, ParsedEtwSession? Session2)
        {
            if ((object)Session1 == null || (object)Session2 == null)
                return !Equals(Session1, Session2);
            return !(Session1.Equals(Session2));
        }

        public override int GetHashCode()
        {
            return LoggerId.GetHashCode();
        }

        public int CompareTo(ParsedEtwSession? Other)
        {
            if (Other == null)
            {
                return 1;
            }
            return LoggerId.CompareTo(Other.LoggerId);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Logger ID {LoggerId}");
            foreach (var p in EnabledProviders)
            {
                sb.AppendLine($"   {p}");
            }
            return sb.ToString();
        }
    }
}
