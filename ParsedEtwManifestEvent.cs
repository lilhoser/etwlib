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

namespace etwlib
{
    public class ParsedEtwManifestEvent : IEquatable<ParsedEtwManifestEvent>, IComparable<ParsedEtwManifestEvent>
    {
        public string Id { get; set; }
        public string Version { get; set; }
        public string? Opcode { get; set; }
        public string? Channel { get; set; }
        public string Level { get; set; }
        public string? Keywords { get; set; }
        public string Task { get; set; }
        public string? Template { get; set; }

        private ParsedEtwManifestEvent() { } // For XML serialization

        public ParsedEtwManifestEvent(
            string id,
            string version,
            string? opcode,
            string? channel,
            string level,
            string? keywords,
            string task,
            string? template)
        {
            Id = id;
            Version = version;
            Opcode = opcode;
            Channel = channel;
            Level = level;
            Keywords = keywords;
            Task = task;
            Template = template;
        }

        public override bool Equals(object? Other)
        {
            if (Other == null)
            {
                return false;
            }
            var field = Other as ParsedEtwManifestEvent;
            return Equals(field);
        }

        public bool Equals(ParsedEtwManifestEvent? Other)
        {
            if (Other == null)
            {
                return false;
            }

            //
            // According to https://learn.microsoft.com/en-us/windows/win32/api/evntprov/ns-evntprov-event_descriptor
            //          "For manifest-based ETW, the combination Provider.DecodeGuid +
            //          Event.Id + Event.Version should uniquely identify an event"
            // I have seen cases where event IDs + version for a given provider is not enough,
            // but adding in opcode is. Not sure if "decoding GUID" comes from provider traits
            // and thus has another level of indirection to determine uniqueness.
            //
            return Id == Other.Id && Version == Other.Version &&
                Opcode == Other.Opcode;
        }

        public static bool operator ==(ParsedEtwManifestEvent? Event1, ParsedEtwManifestEvent? Event2)
        {
            if ((object)Event1 == null || (object)Event2 == null)
                return Equals(Event1, Event2);
            return Event1.Equals(Event2);
        }

        public static bool operator !=(ParsedEtwManifestEvent? Event1, ParsedEtwManifestEvent? Event2)
        {
            if ((object)Event1 == null || (object)Event2 == null)
                return !Equals(Event1, Event2);
            return !(Event1.Equals(Event2));
        }

        public override int GetHashCode()
        {
            return (Id, Version).GetHashCode();
        }

        public int CompareTo(ParsedEtwManifestEvent? Other)
        {
            if (Other == null)
            {
                return 1;
            }
            return Id.CompareTo(Other.Id);
        }

        public override string ToString()
        {
            return $"Id={Id}, Version={Version}, Task={Task}, Opcode={Opcode}, " +
                   $"Channel={Channel}, Level={Level}, Keywords={Keywords}, Template={Template}";
        }

        public static ParsedEtwManifestEvent? FromString(string Value)
        {
            var values = Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (values.Length != 8)
            {
                return null;
            }
            var dict = new Dictionary<string, string>();
            foreach (var v in values)
            {
                var kvp = v.Split('=', StringSplitOptions.RemoveEmptyEntries);
                if (kvp.Length < 1)
                {
                    return null;
                }
                var key = kvp[0];
                var val = string.Empty;
                if (kvp.Length == 2)
                {
                    val = kvp[1];
                }
                if (dict.ContainsKey(key))
                {
                    return null;
                }
                dict.Add(key, val);
            }
            return new ParsedEtwManifestEvent(
                dict["Id"],
                dict["Version"],
                dict["Opcode"],
                dict["Channel"],
                dict["Level"],
                dict["Keywords"],
                dict["Task"],
                dict["Template"]);
        }
    }
}
