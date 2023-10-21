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
    public class ParsedEtwEvent
    {
        public ParsedEtwProvider Provider { get; set; }
        public ushort EventId { get; set; }
        public byte Version { get; set; }
        public uint ProcessId { get; set; }
        public long ProcessStartKey { get; set; }
        public uint ThreadId { get; set; }
        public string? UserSid { get; set; }
        public Guid ActivityId { get; set; }
        public DateTime Timestamp { get; set; }
        public string Level { get; set; }
        public ParsedEtwString? Channel { get; set; }
        public string? Keywords { get; set; }
        public ulong KeywordsUlong { get; set; }
        public ParsedEtwString? Task { get; set; }
        public ParsedEtwString? Opcode { get; set; }
        public List<ulong>? StackwalkAddresses { get; set; }
        public ulong? StackwalkMatchId { get; set; }
        public List<ParsedEtwTemplateItem>? TemplateData { get; set; }

        public ParsedEtwEvent()
        {
            Provider = new ParsedEtwProvider();
            Level = "";
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Event ID {EventId}, Version {Version}");
            sb.AppendLine($"Timestamp: {Timestamp}");
            sb.AppendLine($"Process: {ProcessId} (Key={ProcessStartKey})");
            sb.AppendLine($"TID: {ThreadId}");
            if (UserSid != null)
            {
                sb.AppendLine($"User SID: {UserSid}");
            }
            if (ActivityId != Guid.Empty)
            {
                sb.AppendLine($"Activity ID: {ActivityId}");
            }
            sb.AppendLine($"Level: {Level}");
            sb.AppendLine($"Channel: {Channel}"); ;
            sb.AppendLine($"Keywords: {Keywords}");
            sb.AppendLine($"Task: {Task}");
            sb.AppendLine($"Opcode: {Opcode}");
            sb.AppendLine($"Template data:");
            if (TemplateData != null)
            {
                foreach (var item in TemplateData)
                {
                    sb.AppendLine($"   {item}");
                }
            }
            return sb.ToString();
        }
    }
}
