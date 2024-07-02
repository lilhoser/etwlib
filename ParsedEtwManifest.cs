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
using System.Text;

namespace etwlib
{
    public class ParsedEtwManifest
    {
        public ParsedEtwProvider Provider { get; set; }
        public List<ParsedEtwManifestEvent> Events { get; set; }
        public List<ParsedEtwManifestField> Channels { get; set; }
        public List<ParsedEtwManifestField> Keywords { get; set; }
        public Dictionary<ParsedEtwManifestField, List<ParsedEtwManifestField>> Tasks { get; set; }
        public List<ParsedEtwManifestField> GlobalOpcodes { get; set; }
        public Dictionary<string, List<ParsedEtwTemplateItem>> Templates { get; set; }
        public List<string> StringTable { get; set; }

        public ParsedEtwManifest()
        {
            Provider = new ParsedEtwProvider();
            Events = new List<ParsedEtwManifestEvent>();
            Channels = new List<ParsedEtwManifestField>();
            Keywords = new List<ParsedEtwManifestField>();
            Tasks = new Dictionary<ParsedEtwManifestField, List<ParsedEtwManifestField>>();
            GlobalOpcodes = new List<ParsedEtwManifestField>();
            Templates = new Dictionary<string, List<ParsedEtwTemplateItem>>();
            StringTable = new List<string>();
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Provider: {Provider}");
            foreach (var evt in Events)
            {
                sb.AppendLine($"   {evt}");
            }
            sb.AppendLine($"Channels:");
            foreach (var channel in Channels)
            {
                sb.AppendLine($"   {channel}");
            }
            sb.AppendLine($"Keywords:");
            foreach (var kw in Keywords)
            {
                sb.AppendLine($"   {kw}");
            }
            sb.AppendLine($"Tasks:");
            foreach (var kvp in Tasks)
            {
                var task = kvp.Key;
                sb.AppendLine($"   {task} opcodes:");
                foreach (var opcode in kvp.Value)
                {
                    sb.AppendLine($"     {opcode}");
                }
            }
            if (GlobalOpcodes.Count > 0)
            {
                sb.AppendLine($"   Global opcodes:");
                foreach (var opcode in GlobalOpcodes)
                {
                    sb.AppendLine($"     {opcode}");
                }
            }
            sb.AppendLine($"Templates:");
            foreach (var kvp in Templates)
            {
                sb.AppendLine($"   Template {kvp.Key}:");
                foreach (var item in kvp.Value)
                {
                    sb.AppendLine($"   {item}");
                }
            }
            sb.AppendLine($"String table:");
            foreach (var str in StringTable)
            {
                sb.AppendLine($"   {str}");
            }
            return sb.ToString();
        }

        public string ToXml()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(@"<?xml version=""1.0"" encoding=""utf-8""?>
<instrumentationManifest xmlns=""http://schemas.microsoft.com/win/2004/08/events""
    xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
    xmlns:xs=""http://www.w3.org/2001/XMLSchema""
    xmlns:win=""http://manifests.microsoft.com/win/2004/08/windows/events""
    xsi:schemaLocation=""http://schemas.microsoft.com/win/2004/08/events eventman.xsd"">");
            sb.AppendLine("<instrumentation>");
            sb.AppendLine("  <events>");
            sb.AppendLine($"  <provider symbol=\"{Provider.Name}\" name=\"{Provider.Name}\" " +
                $"guid=\"{{{Provider.Id}}}\" source=\"Xml\" messageFileName=\"noMessageFile\" " +
                $"resourceFileName=\"noResourceFile\">");
            sb.AppendLine("      <events>");
            foreach (var evt in Events)
            {
                var template = !string.IsNullOrEmpty(evt.Template) ?
                    $" template=\"{evt.Template}\"" : "";
                var task = !string.IsNullOrEmpty(evt.Task) ?
                    $" task=\"{evt.Task}\"" : "";
                var opcode = !string.IsNullOrEmpty(evt.Opcode) ?
                    $" opcode=\"{evt.Opcode}\"" : "";
                var channel = !string.IsNullOrEmpty(evt.Channel) ?
                    $" channel=\"{evt.Channel}\"" : "";
                var keywords = !string.IsNullOrEmpty(evt.Keywords) ?
                    $" keywords=\"{evt.Keywords}\"" : "";
                var level = ProviderParser.TraceLevelToSchemaType(evt.Level);
                sb.AppendLine($"        <event value=\"{evt.Id}\" " +
                    $"version=\"{evt.Version}\" level=\"{level}\" " +
                    $"{channel}{task}{opcode}{keywords}{template}/>");
            }
            sb.AppendLine("      </events>");
            sb.AppendLine("      <channels>");
            var knownChannelTypes = new List<string> {
                "Admin", "Operational", "Analytic", "Debug" };
            foreach (var channel in Channels)
            {
                var chType = "unknown";
                //
                // Windows supports 4 channel types according to
                //  https://learn.microsoft.com/en-us/windows/win32/wes/defining-channels
                // and the convention is to include the type in the channel name:
                //      <myChannelName>/<Type>
                // This is either outdated or plain wrong, as Windows-Kernel-Registry uses
                // "Performance". MC won't accept it and TDH won't load it.
                //
                var pos = channel.Name.IndexOf("/");
                if (pos >= 0)
                {
                    var start = pos + 1;
                    var length = channel.Name.Length - start;
                    chType = channel.Name.Substring(start, length);
                    if (!knownChannelTypes.Contains(chType))
                    {
                        chType = "Debug"; // lol whatever
                    }
                }

                sb.AppendLine($"        <channel value=\"{channel.Value}\" " +
                    $"name=\"{channel.Name}\" type=\"{chType}\"/>");
            }
            sb.AppendLine("      </channels>");
            sb.AppendLine("      <keywords>");
            foreach (var kw in Keywords)
            {
                var name = kw.Name;
                int index = StringTable.FindIndex(s => s == name);
                Debug.Assert(index >= 0);
                sb.AppendLine($"        <keyword name=\"{name}\" mask=\"" +
                    $"0x{kw.Value:X}\" message=\"$(string.string{index})\" />");
            }
            sb.AppendLine("      </keywords>");
            sb.AppendLine("      <tasks>");
            foreach (var kvp in Tasks)
            {
                var task = kvp.Key;
                if (task.Value == 0 || string.IsNullOrEmpty(task.Name))
                {
                    //
                    // Tasks with value of 0 are default tasks.
                    //
                    continue;
                }
                sb.AppendLine($"        <task value=\"{task.Value}\" name=\"" +
                    $"{task.Name}\" message=\"$(string.string{task.Value})\">");
                if (kvp.Value.Count > 0)
                {
                    sb.AppendLine("          <opcodes>");
                    foreach (var opcode in kvp.Value)
                    {
                        var message = "";
                        if (!string.IsNullOrEmpty(opcode.Name))
                        {
                            int index2 = StringTable.FindIndex(s => s == opcode.Name);
                            Debug.Assert(index2 >= 0);
                            message = $" message=\"$(string.string{index2})\"";
                        }

                        int index = StringTable.FindIndex(s => s == opcode.Name);
                        Debug.Assert(index >= 0);
                        sb.AppendLine($"           <opcode value=\"{opcode.Value}\" name=\"" +
                            $"{opcode.Name}\"{message}/>");
                    }
                    sb.AppendLine("          </opcodes>");
                }
                sb.AppendLine($"        </task>");
            }
            sb.AppendLine("      </tasks>");
            if (GlobalOpcodes.Count > 0)
            {
                sb.AppendLine("      <opcodes>");
                foreach (var opcode in GlobalOpcodes)
                {
                    var message = "";
                    if (!string.IsNullOrEmpty(opcode.Name))
                    {
                        int index = StringTable.FindIndex(s => s == opcode.Name);
                        Debug.Assert(index >= 0);
                        message = $" message=\"$(string.string{index})\"";
                    }
                    sb.AppendLine($"        <opcode value=\"{opcode.Value}\" name=\"" +
                        $"{opcode.Name}\"{message}/>");
                }
                sb.AppendLine("      </opcodes>");
            }
            sb.AppendLine("      <templates>");
            foreach (var kvp in Templates)
            {
                sb.AppendLine($"        <template tid=\"{kvp.Key}\">");
                foreach (var item in kvp.Value)
                {
                    //
                    // Length or count is the name of the field that contains that value
                    // in an actual runtime event
                    //
                    var lengthOrCountAttribute = "";
                    if (item.FieldBackreference != null)
                    {
                        if (item.FieldBackreference.IsCountedField)
                        {
                            lengthOrCountAttribute =
                                $"count=\"{item.FieldBackreference.FieldName}\"";
                        }
                        else
                        {
                            lengthOrCountAttribute =
                                $"length=\"{item.FieldBackreference.FieldName}\"";
                        }
                    }

                    var inType = ProviderParser.TdhInputTypeToSchemaType(item.InType);
                    var outType = ProviderParser.TdhOutputTypeToSchemaType(item.OutType);
                    sb.AppendLine($"          <data name=\"{item.Name}\" inType=\"" +
                        $"{inType}\" outType=\"{outType}\" {lengthOrCountAttribute} />");
                }
                sb.AppendLine($"        </template>");
            }
            sb.AppendLine("      </templates>");
            sb.AppendLine("  </provider>");
            sb.AppendLine("  </events>");
            sb.AppendLine("</instrumentation>");
            sb.AppendLine("<localization>");
            sb.AppendLine(@"  <resources culture=""en-US"">");
            sb.AppendLine("     <stringTable>");
            int count = 0;
            foreach (var str in StringTable)
            {
                sb.AppendLine($"       <string id=\"string{count++}\" value=\"{str}\" />");
            }
            sb.AppendLine("     </stringTable>");
            sb.AppendLine("  </resources>");
            sb.AppendLine("</localization>");
            sb.AppendLine("</instrumentationManifest>");
            return sb.ToString();
        }
    }
}
