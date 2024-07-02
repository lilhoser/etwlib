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
using System.Runtime.InteropServices;

namespace etwlib
{
    using static TraceLogger;
    using static NativeTraceConsumer;

    public class ManifestNotFoundException : Exception
    {
        public ManifestNotFoundException(string message)
            : base(message)
        {
        }
    }

    public static class ProviderParser
    {
        public
        static
        ParsedEtwProvider?
        GetProvider(string Name)
        {
            var providers = GetProviders();
            return providers.FirstOrDefault(p => p.Name?.ToLower() == Name?.ToLower());
        }

        public
        static
        ParsedEtwProvider?
        GetProvider(Guid Id)
        {
            var providers = GetProviders();
            return providers.FirstOrDefault(p => p.Id == Id);
        }

        public
        static
        List<ParsedEtwProvider>
        GetProviders()
        {
            var results = new List<ParsedEtwProvider>();
            nint buffer = nint.Zero;

            try
            {
                uint result = 0;
                uint bufferSize = 0;

                result = TdhEnumerateProviders(nint.Zero, ref bufferSize);

                if (result != ERROR_INSUFFICIENT_BUFFER)
                {
                    var error = $"TdhEnumerateProviders failed: 0x{result:X}";
                    Trace(TraceLoggerType.EtwProviderParser,
                        TraceEventType.Error,
                        error);
                    throw new Exception(error);
                }

                buffer = Marshal.AllocHGlobal((int)bufferSize);
                if (buffer == nint.Zero)
                {
                    throw new Exception("Out of memory");
                }

                result = TdhEnumerateProviders(buffer, ref bufferSize);
                if (result != ERROR_SUCCESS)
                {
                    var error = $"TdhEnumerateProviders failed(2): 0x{result:X}";
                    Trace(TraceLoggerType.EtwProviderParser,
                        TraceEventType.Error,
                        error);
                    throw new Exception(error);
                }

                var providerInfoList = (PROVIDER_ENUMERATION_INFO)Marshal.PtrToStructure(
                    buffer, typeof(PROVIDER_ENUMERATION_INFO))!;
                if (providerInfoList.NumberOfProviders == 0)
                {
                    throw new Exception("There are no ETW providers.");
                }

                var pointer = nint.Add(buffer,
                    (int)Marshal.OffsetOf<PROVIDER_ENUMERATION_INFO>("TraceProviderInfoArray").ToInt64());
                var end = nint.Add(buffer, (int)bufferSize);

                for (int i = 0; i < providerInfoList.NumberOfProviders; i++)
                {
                    var providerInfo = (TRACE_PROVIDER_INFO)Marshal.PtrToStructure(
                        pointer, typeof(TRACE_PROVIDER_INFO))!;
                    var provider = new ParsedEtwProvider();
                    provider.Id = providerInfo.ProviderGuid;
                    //
                    // The "source" retrieved using this TDH API only returns 0 or 1 for
                    // XML vs MOF. But providers specified in ETW events retrieved at
                    // runtime can be from other non-manifest sources like TraceLog.
                    //
                    provider.Source = providerInfo.SchemaSource == 0 ? "xml" : "MOF";
                    provider.Name = "(unnamed)";
                    provider.HasManifest = IsManifestKnown(provider.Id);

                    if (providerInfo.ProviderNameOffset != 0)
                    {
                        var target = nint.Add(buffer, (int)providerInfo.ProviderNameOffset);
                        if (target > end)
                        {
                            throw new Exception($"Provider name offset 0x{providerInfo.ProviderNameOffset:X} " +
                                $"extends beyond buffer's end address 0x{end}");
                        }
                        var name = Marshal.PtrToStringUni(target)!;
                        if (!string.IsNullOrEmpty(name))
                        {
                            provider.Name = name;
                        }
                    }

                    results.Add(provider);
                    pointer = nint.Add(pointer, Marshal.SizeOf(typeof(TRACE_PROVIDER_INFO)));
                }

                results.Sort();
                return results;
            }
            catch (Exception ex)
            {
                Trace(TraceLoggerType.EtwProviderParser,
                    TraceEventType.Error,
                    $"Exception in GetProviders(): {ex.Message}");
                throw;
            }
            finally
            {
                if (buffer != nint.Zero)
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }

        public
        static
        ParsedEtwManifest
        GetManifest(Guid ProviderGuid, [In, Optional] string ManifestLocation)
        {
            var parsedManifest = new ParsedEtwManifest();
            var fieldResults = new List<ParsedEtwManifestField>();

            //
            // If a manifest location was provided, ask TDH to load it.
            //
            if (!string.IsNullOrEmpty(ManifestLocation))
            {
                try
                {
                    LoadProviderManifest(ManifestLocation);
                }
                catch (Exception ex)
                {
                    throw new Exception($"TDH was unable to load the manifest " +
                    $"{ManifestLocation}: {ex.Message}");
                }
            }

            //
            // Get all supported keywords from TDH, minus reserved keywords.
            //
            // Note: The top 16 bits of the keyword are reserved by Microsoft, and appear
            // to be created from channel information (ie, Debug=0x8000000000000000). We
            // can't include them in the manifest, because MC.exe only considers the first
            // two bytes in the keyword mask string - so 0x8000 in this example might
            // collide with a custom keyword, causing TDH to throw an XML error.
            //
            parsedManifest.Keywords = GetProviderSupportedFields(
                ProviderGuid, 0x0000ffffffffffff, EVENT_FIELD_TYPE.KeywordInformation);

            //
            // Get all supported channels from TDH
            //
            for (int i = 0; i < char.MaxValue; i++)
            {
                fieldResults = GetProviderSupportedFields(
                    ProviderGuid, (ulong)i, EVENT_FIELD_TYPE.ChannelInformation);
                if (fieldResults.Count > 0)
                {
                    parsedManifest.Channels.AddRange(fieldResults);
                }
            }

            //
            // Get all supported tasks from TDH (if any)
            //
            for (int i = 0; i < ushort.MaxValue; i++)
            {
                fieldResults = GetProviderSupportedFields(
                    ProviderGuid, (ulong)i, EVENT_FIELD_TYPE.TaskInformation);
                foreach (var field in fieldResults)
                {
                    if (!parsedManifest.Tasks.ContainsKey(field))
                    {
                        parsedManifest.Tasks.Add(field, new List<ParsedEtwManifestField>());
                    }
                }
            }

            //
            // Get all supported task-specific opcodes from TDH (if any)
            //
            foreach (var task in parsedManifest.Tasks.Keys)
            {
                //
                // Custom opcodes must be in the range from 10 through 239
                //
                for (int i = 10; i <= 239; i++)
                {
                    //
                    // Bits 0 - 15 must contain the task value
                    // Bits 16 - 23 must contain the opcode value
                    //
                    var value = ((ulong)i) << 16 | (ushort)task.Value;
                    fieldResults = GetProviderSupportedFields(
                        ProviderGuid, value, EVENT_FIELD_TYPE.OpcodeInformation);
                    if (fieldResults.Count > 0)
                    {
                        parsedManifest.Tasks[task].AddRange(fieldResults);
                    }
                }
            }

            //
            // Create a "default task" for reserved opcodes (eg, "win:Start") are 0-9.
            //
            var defaultTask = new ParsedEtwManifestField("(no task)", "default task", 0);
            parsedManifest.Tasks.Add(defaultTask, new List<ParsedEtwManifestField>());
            for (int i = 0; i < 10; i++)
            {
                //
                // Bits 0 - 15 must contain the task value (no task here)
                // Bits 16 - 23 must contain the opcode value
                //
                var value = ((ulong)i) << 16;
                fieldResults = GetProviderSupportedFields(
                    ProviderGuid, value, EVENT_FIELD_TYPE.OpcodeInformation);
                if (fieldResults.Count > 0)
                {
                    parsedManifest.Tasks[defaultTask].AddRange(fieldResults);
                }
            }

            //
            // There can be opcodes that are not nested in tasks, almost like a "global"
            // opcode that can be used with any task. If the provider defines such an
            // opcode, the TDH api will rightly return global opcodes as well as task-
            // specific opcodes whenever a task is queried (above). This means global
            // opcodes will be repeated in every task's opcode list. Oddly, MC.exe will
            // complain about a manifest that has global opcodes repeated as task-local.
            // So we need to go cull all those out now into a separate bin, so our manifest
            // generation code knows to put them in their own dedicated "<opcodes>" block.
            //
            for (int i = 0; i < parsedManifest.Tasks.Count; i++)
            {
                var opcodes = parsedManifest.Tasks.ElementAt(i).Value;
                for (int j = i + 1; j < parsedManifest.Tasks.Count - 1; j++)
                {
                    var opcodes2 = parsedManifest.Tasks.ElementAt(j).Value;
                    var duplicates = opcodes.Intersect(opcodes2).ToList();
                    duplicates.ForEach(d =>
                    {
                        if (!parsedManifest.GlobalOpcodes.Contains(d))
                        {
                            parsedManifest.GlobalOpcodes.Add(d);
                        }
                    });
                }
            }
            for (int i = 0; i < parsedManifest.Tasks.Count; i++)
            {
                _ = parsedManifest.Tasks.ElementAt(i).Value.RemoveAll(
                    r => parsedManifest.GlobalOpcodes.Any(a => a == r));
            }

            //
            // This is a bit hacky. Use our EventParser class to parse meta information
            // about a provider's events from their manifest. This is overloading the
            // intended purpose of the parser (which is to parse actual live events, not
            // structural manifest events)
            //
            var events = GetProviderSupportedEvents(ProviderGuid);
            foreach (var parsedEvent in events)
            {
                //
                // The provider should be the same across every event, but just
                // assign it always.
                //
                if (string.IsNullOrEmpty(parsedManifest.Provider.Name) &&
                    !string.IsNullOrEmpty(parsedEvent.Provider.Name))
                {
                    parsedManifest.Provider = parsedEvent.Provider;
                    parsedManifest.StringTable.Add(parsedManifest.Provider.Name);
                }

                //
                // Extract template data
                //
                var template = "";
                if (parsedEvent.TemplateData != null && parsedEvent.TemplateData.Count > 0)
                {
                    var found = false;
                    foreach (var kvp in parsedManifest.Templates)
                    {
                        var existingTemplate = kvp.Value;
                        if (parsedEvent.TemplateData.Intersect(
                            existingTemplate).Count() == parsedEvent.TemplateData.Count())
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        var name = $"Args{parsedEvent.EventId}_{parsedEvent.Version}";
                        if (parsedManifest.Templates.ContainsKey(name))
                        {
                            Debug.Assert(false);
                            continue;
                        }
                        parsedManifest.Templates.Add(name, parsedEvent.TemplateData);
                        template = name;
                    }
                }

                //
                // Lookup values from parsed manifest.
                //
                var channel = "";
                var opcode = "";
                var task = "";
                var keywords = "";

                if (parsedEvent.KeywordsUlong != 0)
                {
                    //
                    // We have to mask off reserved keywords here also, because we excluded
                    // them above when we queries all known keywords.
                    //
                    var value = parsedEvent.KeywordsUlong & 0x0000ffffffffffff;
                    var keywordFields = GetProviderSupportedFields(
                        ProviderGuid,
                        value,
                        EVENT_FIELD_TYPE.KeywordInformation);
                    foreach (var keyword in keywordFields)
                    {
                        Debug.Assert(parsedManifest.Keywords.Contains(keyword));
                    }

                    keywords = string.Join(' ', keywordFields.Select(
                        k => k.Name).ToList());
                }

                if (parsedEvent.Channel != null)
                {
                    var match = parsedManifest.Channels.FirstOrDefault(
                        c => c.Value == parsedEvent.Channel.Value);
                    Debug.Assert(match != null);
                    channel = match.Name;
                }

                if (parsedEvent.Opcode != null)
                {
                    ParsedEtwManifestField? matchingOpcode = null;

                    if (parsedEvent.Opcode.Value < 10)
                    {
                        //
                        // Reserved opcodes (0-10) are always kept in the "default task" entry
                        // of the task list, as they would never show up in a task-local or
                        // global opcode list.
                        //
                        matchingOpcode = parsedManifest.Tasks[defaultTask].FirstOrDefault(
                            o => o.Value == parsedEvent.Opcode.Value)!;
                    }
                    else if (parsedEvent.Task == null || parsedEvent.Task.Value == 0)
                    {
                        //
                        // In the case where the task was not provided (0) and opcode is
                        // custom (> 10), we keep those opcodes in the global opcode list.
                        //
                        matchingOpcode = parsedManifest.GlobalOpcodes.FirstOrDefault(
                            opcode => opcode.Value == parsedEvent.Opcode.Value)!;
                        //
                        // Something is buggy with TDH api for opcode/task resolution.
                        // If TDH didn't give us the opcode when we queried for it
                        // earlier, just add it now.
                        //
                        if (matchingOpcode == null)
                        {
                            matchingOpcode = new ParsedEtwManifestField(
                                parsedEvent.Opcode.Name, "", parsedEvent.Opcode.Value);
                            parsedManifest.GlobalOpcodes.Add(matchingOpcode);
                        }
                    }
                    else
                    {
                        //
                        // A task was provided and the opcode is custom, it might be in the
                        // task-local list for the corresponding task.
                        //
                        var matchingTask = parsedManifest.Tasks.FirstOrDefault(
                            t => t.Key.Value == parsedEvent.Task.Value);
                        matchingOpcode = parsedManifest.Tasks[matchingTask.Key].FirstOrDefault(
                            opcode => opcode.Value == parsedEvent.Opcode.Value)!;
                        //
                        // That is, if the provider followed the rules.  *eyeroll*
                        //
                        if (matchingOpcode == null)
                        {
                            matchingOpcode = new ParsedEtwManifestField(
                                parsedEvent.Opcode.Name, "", parsedEvent.Opcode.Value);
                            matchingTask.Value.Add(matchingOpcode);
                        }
                    }
                    Debug.Assert(matchingOpcode != null);
                    opcode = matchingOpcode.Name;
                }

                if (parsedEvent.Task != null && parsedEvent.Task.Value > 0)
                {
                    var matchingTask = parsedManifest.Tasks.FirstOrDefault(
                        t => t.Key.Value == parsedEvent.Task.Value);
                    task = matchingTask.Key.Name;
                }

                //
                // Build an event from the parsed values
                //
                var manifestEvent = new ParsedEtwManifestEvent(parsedEvent.EventId.ToString(),
                    parsedEvent.Version.ToString(),
                    opcode,
                    channel,
                    parsedEvent.Level,
                    keywords,
                    task,
                    template);

                //
                // TODO: Research this condition. Rarely, a provider will have
                // seemingly duplicate events.
                //
                // Debug.Assert(!parsedManifest.Events.Contains(manifestEvent));
                parsedManifest.Events.Add(manifestEvent);
            }

            //
            // Build a list of all unique strings from parsed structures
            //
            parsedManifest.Keywords.ForEach(k => parsedManifest.StringTable.Add(k.Name));
            parsedManifest.Channels.ForEach(k => parsedManifest.StringTable.Add(k.Name));
            parsedManifest.Tasks.Keys.Where(k => k.Name != "(no task)").ToList().ForEach(
                t => parsedManifest.StringTable.Add(t.Name));
            parsedManifest.Tasks.Values.ToList().ForEach(
                opcodeList => opcodeList.ForEach(opcode =>
                {
                    if (!parsedManifest.StringTable.Contains(opcode.Name))
                    {
                        parsedManifest.StringTable.Add(opcode.Name);
                    }
                }));
            parsedManifest.GlobalOpcodes.ForEach(g => parsedManifest.StringTable.Add(g.Name));

            //
            // Make the string list unique and sorted
            //
            parsedManifest.StringTable = parsedManifest.StringTable.Distinct().ToList();
            parsedManifest.StringTable.Sort();

            return parsedManifest;
        }

        public
        static
        Dictionary<ParsedEtwProvider, ParsedEtwManifest>
        GetManifests()
        {
            var results = new Dictionary<ParsedEtwProvider, ParsedEtwManifest>();
            var providers = GetProviders();
            foreach (var provider in providers)
            {
                try
                {
                    if (results.ContainsKey(provider))
                    {
                        //
                        // This is rare; i'm guessing it is a synchronization problem
                        // with the TDH api.
                        //
                        Trace(TraceLoggerType.EtwProviderParser,
                              TraceEventType.Error,
                              $"The provider {provider} appears twice!");
                        continue;
                    }
                    var manifest = GetManifest(provider.Id);
                    results.Add(provider, manifest);
                }
                catch (ManifestNotFoundException)
                {
                    //
                    // Swallow this exception. It's possible the provider is using
                    // TraceLogging, MOF or some other classic ETW mechanism that
                    // precludes a published XML manifest.
                    //
                    Trace(TraceLoggerType.EtwProviderParser,
                          TraceEventType.Warning,
                          $"Unable to locate published manifest for {provider}");
                }
            }
            return results;            
        }

        public
        static
        bool
        IsManifestKnown(Guid ProviderGuid)
        {
            var buffer = nint.Zero;
            try
            {
                uint bufferSize = 0;
                for (; ; )
                {
                    var status = TdhEnumerateManifestProviderEvents(
                        ref ProviderGuid,
                        buffer,
                        ref bufferSize);
                    switch (status)
                    {
                        case ERROR_SUCCESS:
                        case ERROR_INSUFFICIENT_BUFFER:
                            {
                                return true;
                            }
                        case ERROR_NOT_FOUND:
                        case ERROR_FILE_NOT_FOUND:
                        case ERROR_RESOURCE_TYPE_NOT_FOUND:
                        case ERROR_MUI_FILE_NOT_FOUND:
                            {
                                return false;
                            }
                        default:
                            {
                                return false;
                            }
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private
        static
        List<ParsedEtwEvent>
        GetProviderSupportedEvents(Guid ProviderGuid)
        {
            var buffer = nint.Zero;

            try
            {
                uint bufferSize = 0;
                for (; ; )
                {
                    var status = TdhEnumerateManifestProviderEvents(
                        ref ProviderGuid,
                        buffer,
                        ref bufferSize);
                    if (status == ERROR_INSUFFICIENT_BUFFER)
                    {
                        buffer = Marshal.AllocHGlobal((int)bufferSize);
                        if (buffer == nint.Zero)
                        {
                            throw new Exception("Out of memory");
                        }
                        continue;
                    }
                    else if (status == ERROR_SUCCESS)
                    {
                        break;
                    }

                    switch (status)
                    {
                        case ERROR_EMPTY:
                            {
                                //
                                // It's unclear if this is an error but it seems to happen
                                // somewhat rarely.
                                //
                                return new List<ParsedEtwEvent>();
                            }
                        case ERROR_INVALID_DATA:
                            {
                                //
                                // It's unclear if this is an error but it seems to happen
                                // somewhat rarely.
                                //
                                return new List<ParsedEtwEvent>();
                            }
                        case ERROR_NOT_FOUND:
                        case ERROR_FILE_NOT_FOUND:
                        case ERROR_RESOURCE_TYPE_NOT_FOUND:
                        case ERROR_MUI_FILE_NOT_FOUND:
                        {
                            //
                            // We throw this exception here because it's our first chance
                            // to know if this return value truly means "missing manifest"
                            //
                            throw new ManifestNotFoundException($"ETW was unable to locate " +
                                $"the manifest or schema for provider {ProviderGuid}. " +
                                $"Usually this is caused by an incorrect field in the " +
                                $"input event descriptor, such as incorrect event ID or " +
                                $"version number.");
                            }
                        default:
                            {
                                throw new Exception($"TdhEnumerateManifestProviderEvents " +
                                    $"failed: 0x{status:X}");
                            }
                    }
                }

                return ParseProviderEventArray(ProviderGuid, buffer);
            }
            catch (Exception ex)
            {
                Trace(TraceLoggerType.EtwProviderParser,
                    TraceEventType.Error,
                    "Exception in ParseProviderEventArray(): " +
                    ex.Message);
                throw;
            }
            finally
            {
                if (buffer != nint.Zero)
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

        }

        private
        static
        List<ParsedEtwEvent>
        ParseProviderEventArray(Guid ProviderGuid, nint EventArrayBuffer)
        {
            var array = (PROVIDER_EVENT_INFO)Marshal.PtrToStructure(
                EventArrayBuffer, typeof(PROVIDER_EVENT_INFO))!;
            int offset = Marshal.OffsetOf(typeof(PROVIDER_EVENT_INFO),
                    "EventDescriptorsArray").ToInt32();
            nint arrayStart = nint.Add(EventArrayBuffer, offset);
            Debug.Assert(array.NumberOfEvents > 0);
            var events = MarshalHelper.MarshalArray<EVENT_DESCRIPTOR>(arrayStart,
                array.NumberOfEvents);
            var eventDescriptorBuffer = nint.Zero;
            var results = new List<ParsedEtwEvent>();

            try
            {
                eventDescriptorBuffer = Marshal.AllocHGlobal(
                    Marshal.SizeOf(typeof(EVENT_DESCRIPTOR)));
                if (eventDescriptorBuffer == nint.Zero)
                {
                    throw new Exception("Out of memory");
                }

                foreach (var evt in events)
                {
                    Marshal.StructureToPtr(evt, eventDescriptorBuffer, false);
                    var traceEventInfoBuffer = nint.Zero;
                    uint traceEventInfoBufferSize = 0;

                    for (; ; )
                    {
                        var status = TdhGetManifestEventInformation(
                            ref ProviderGuid,
                            eventDescriptorBuffer,
                            traceEventInfoBuffer,
                            ref traceEventInfoBufferSize);
                        if (status == ERROR_INSUFFICIENT_BUFFER)
                        {
                            Debug.Assert(traceEventInfoBuffer == nint.Zero);
                            traceEventInfoBuffer = Marshal.AllocHGlobal(
                                (int)traceEventInfoBufferSize);
                            if (traceEventInfoBuffer == nint.Zero)
                            {
                                throw new Exception("Out of memory");
                            }
                        }
                        else if (status == ERROR_SUCCESS)
                        {
                            break;
                        }
                        else
                        {
                            throw new Exception($"TdhGetManifestEventInformation " +
                                        $"failed: 0x{status:X}");
                        }
                    }

                    Debug.Assert(traceEventInfoBuffer != nint.Zero);

                    using (var parserBuffers = new EventParserBuffers())
                    {
                        //
                        // NB: ownership of allocated traceEventInfoBuffer passed to
                        // EventParser dtor!
                        //
                        var parser = new EventParser(ProviderGuid, evt, traceEventInfoBuffer, parserBuffers);
                        try
                        {
                            var parsedEvent = parser.Parse();
                            Debug.Assert(parsedEvent != null);
                            results.Add(parsedEvent);
                        }
                        catch (Exception ex)
                        {
                            throw new Exception($"Exception parsing event: {ex.Message}");
                        }
                    }
                }

                return results;
            }
            finally
            {
                if (eventDescriptorBuffer != nint.Zero)
                {
                    Marshal.FreeHGlobal(eventDescriptorBuffer);
                }
            }
        }

        private
        static
        List<ParsedEtwManifestField>
        GetProviderSupportedFields(Guid ProviderGuid, ulong FieldValue, EVENT_FIELD_TYPE FieldType)
        {
            var results = new List<ParsedEtwManifestField>();
            var buffer = nint.Zero;

            try
            {

                buffer = GetProviderFieldBuffer(ProviderGuid, FieldValue, FieldType);
                if (buffer == nint.Zero)
                {
                    return results;
                }
                var array = (PROVIDER_FIELD_INFOARRAY)Marshal.PtrToStructure(
                    buffer, typeof(PROVIDER_FIELD_INFOARRAY))!;
                int offset = Marshal.OffsetOf(typeof(PROVIDER_FIELD_INFOARRAY),
                        "FieldInfoArray").ToInt32();
                nint arrayStart = nint.Add(buffer, offset);
                Debug.Assert(array.NumberOfElements > 0);
                var fields = MarshalHelper.MarshalArray<PROVIDER_FIELD_INFO>(arrayStart,
                    array.NumberOfElements);
                foreach (var field in fields)
                {
                    var value = field.Value;
                    if (FieldType == EVENT_FIELD_TYPE.OpcodeInformation)
                    {
                        value &= 0xff0000; // clear task value
                        value >>= 16;     // shift back to char
                    }
                    var parsedField = new ParsedEtwManifestField("", "", value);
                    if (field.NameOffset > 0)
                    {
                        parsedField.Name = Marshal.PtrToStringUni(
                            nint.Add(buffer, (int)field.NameOffset))!;
                    }
                    if (field.DescriptionOffset > 0)
                    {
                        parsedField.Description = Marshal.PtrToStringUni(
                            nint.Add(buffer, (int)field.DescriptionOffset))!;
                    }
                    results.Add(parsedField);
                }
                Marshal.FreeHGlobal(buffer);
                buffer = nint.Zero;
            }
            finally
            {
                if (buffer != nint.Zero)
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            return results;
        }

        private static nint GetProviderFieldBuffer(Guid ProviderGuid, ulong FieldValue, EVENT_FIELD_TYPE FieldType)
        {
            var buffer = nint.Zero;
            uint bufferSize = 0;
            for (; ; )
            {
                var status = TdhQueryProviderFieldInformation(
                    ref ProviderGuid,
                    FieldValue,
                    FieldType,
                    buffer,
                    ref bufferSize);
                if (status == ERROR_INSUFFICIENT_BUFFER)
                {
                    Debug.Assert(buffer == nint.Zero);
                    buffer = Marshal.AllocHGlobal((int)bufferSize);
                    if (buffer == nint.Zero)
                    {
                        throw new Exception("Out of memory");
                    }
                }
                else if (status == ERROR_SUCCESS)
                {
                    break;
                }
                else if (status == ERROR_NOT_FOUND || status == ERROR_FILE_NOT_FOUND)
                {
                    //
                    // Note: in this case, TDH might have loaded the schema but returned
                    // this value because the requested field type doesn't exist in the
                    // schema. Very confusing to return this error code for this situation
                    // as well as the more catastrophic case where the schema doesn't exist
                    // _at all_.
                    // We do NOT throw ManifestNotFoundException here.
                    //
                    return nint.Zero;
                }
                else
                {
                    return nint.Zero;
                }
            }

            Debug.Assert(buffer != nint.Zero);
            return buffer;
        }

        private static void LoadProviderManifest(string Location)
        {
            if (string.IsNullOrEmpty(Location) ||
                !File.Exists(Location))
            {
                throw new ManifestNotFoundException($"Invalid manifest location {Location}");
            }

            var result = TdhLoadManifest(Location);

            switch (result)
            {
                case ERROR_SUCCESS:
                    {
                        break;
                    }
                case ERROR_FILE_NOT_FOUND:
                    {
                        throw new ManifestNotFoundException($"TdhLoadManifest failed: " +
                            $"File not found {Location}");
                    }
                case ERROR_INVALID_PARAMETER:
                    {
                        throw new Exception($"TdhLoadManifest failed: " +
                            $"Invalid manifest {Location}");
                    }
                case ERROR_XML_PARSE_ERROR:
                    {
                        throw new Exception($"TdhLoadManifest failed: " +
                            $"File has invalid XML {Location}");
                    }
                default:
                    {
                        throw new Exception($"TdhLoadManifest failed: 0x{result:X}");
                    }
            }
        }

        public static string TdhInputTypeToSchemaType(TdhInputType Type)
        {
            //
            // see https://learn.microsoft.com/en-us/windows/win32/wes/eventmanifestschema-inputtype-complextype
            //
            switch (Type)
            {
                case TdhInputType.UnicodeString:
                    {
                        return "win:UnicodeString";
                    }
                case TdhInputType.AnsiString:
                    {
                        return "win:AnsiString";
                    }
                case TdhInputType.Int8:
                    {
                        return "win:Int8";
                    }
                case TdhInputType.UInt8:
                    {
                        return "win:UInt8";
                    }
                case TdhInputType.Int16:
                    {
                        return "win:Int16";
                    }
                case TdhInputType.UInt16:
                    {
                        return "win:UInt16";
                    }
                case TdhInputType.Int32:
                    {
                        return "win:Int32";
                    }
                case TdhInputType.UInt32:
                    {
                        return "win:UInt32";
                    }
                case TdhInputType.Int64:
                    {
                        return "win:Int64";
                    }
                case TdhInputType.UInt64:
                    {
                        return "win:UInt64";
                    }
                case TdhInputType.Float:
                    {
                        return "win:Float";
                    }
                case TdhInputType.Double:
                    {
                        return "win:Double";
                    }
                case TdhInputType.Boolean:
                    {
                        return "win:Boolean";
                    }
                case TdhInputType.Binary:
                    {
                        return "win:Binary";
                    }
                case TdhInputType.GUID:
                    {
                        return "win:GUID";
                    }
                case TdhInputType.Pointer:
                    {
                        return "win:Pointer";
                    }
                case TdhInputType.FILETIME:
                    {
                        return "win:FILETIME";
                    }
                case TdhInputType.SYSTEMTIME:
                    {
                        return "win:SYSTEMTIME";
                    }
                case TdhInputType.SID:
                    {
                        return "win:SID";
                    }
                case TdhInputType.HexInt32:
                    {
                        return "win:HexInt32";
                    }
                case TdhInputType.HexInt64:
                    {
                        return "win:HexInt64";
                    }
                case TdhInputType.CountedUtf16String:
                    {
                        return "win:UnicodeString";
                    }
                case TdhInputType.CountedMbcsString:
                    {
                        return "win:AnsiString";
                    }
                case TdhInputType.Struct:
                    {
                        return "win:Binary";
                    }
                case TdhInputType.CountedString:
                    {
                        return "win:UnicodeString";
                    }
                case TdhInputType.CountedAnsiString:
                    {
                        return "win:AnsiString";
                    }
                case TdhInputType.ReversedCountedString:
                    {
                        return "win:UnicodeString";
                    }
                case TdhInputType.ReversedCountedAnsiString:
                    {
                        return "win:AnsiString";
                    }
                case TdhInputType.NonNullTerminatedString:
                    {
                        return "win:Binary"; // unsure
                    }
                case TdhInputType.NonNullTerminatedAnsiString:
                    {
                        return "win:Binary"; // unsure
                    }
                case TdhInputType.UnicodeChar:
                    {
                        return "win:UInt16";
                    }
                case TdhInputType.AnsiChar:
                    {
                        return "win:UInt8";
                    }
                case TdhInputType.SizeT:
                    {
                        return "win:Pointer"; // unsure
                    }
                case TdhInputType.HexDump:
                    {
                        return "win:Binary"; // unsure
                    }
                case TdhInputType.WbemSID:
                    {
                        return "win:Binary"; // unsure
                    }
                default:
                    {
                        //
                        // This occurs rarely, not sure how to handle this.
                        //
                        Trace(TraceLoggerType.EtwProviderParser,
                              TraceEventType.Warning,
                              $"Unrecognized InputType: {Type}");
                        return "win:Binary";
                    }
            }
        }

        public static string TdhOutputTypeToSchemaType(TdhOutputType Type)
        {
            //
            // see https://learn.microsoft.com/en-us/windows/win32/wes/eventmanifestschema-outputtype-complextype
            //
            switch (Type)
            {
                case TdhOutputType.String:
                    {
                        return "xs:string";
                    }
                case TdhOutputType.DateTime:
                    {
                        return "xs:datetime";
                    }
                case TdhOutputType.Byte:
                    {
                        return "xs:byte";
                    }
                case TdhOutputType.UnsignedByte:
                    {
                        return "xs:unsignedByte";
                    }
                case TdhOutputType.Short:
                    {
                        return "xs:short";
                    }
                case TdhOutputType.UnsignedShort:
                    {
                        return "xs:unsignedShort";
                    }
                case TdhOutputType.Integer:
                    {
                        return "xs:int";
                    }
                case TdhOutputType.UnsignedInteger:
                    {
                        return "xs:unsignedInt";
                    }
                case TdhOutputType.Long:
                    {
                        return "xs:long";
                    }
                case TdhOutputType.UnsignedLong:
                    {
                        return "xs:unsignedLong";
                    }
                case TdhOutputType.Float:
                    {
                        return "xs:float";
                    }
                case TdhOutputType.Double:
                    {
                        return "xs:double";
                    }
                case TdhOutputType.Boolean:
                    {
                        return "xs:boolean";
                    }
                case TdhOutputType.Guid:
                    {
                        return "xs:GUID";
                    }
                case TdhOutputType.HexBinary:
                    {
                        return "xs:hexBinary";
                    }
                case TdhOutputType.HexInteger8:
                    {
                        return "win:HexInt8";
                    }
                case TdhOutputType.HexInteger16:
                    {
                        return "win:HexInt16";
                    }
                case TdhOutputType.HexInteger32:
                    {
                        return "win:HexInt32";
                    }
                case TdhOutputType.HexInteger64:
                    {
                        return "win:HexInt64";
                    }
                case TdhOutputType.Pid:
                    {
                        return "win:PID";
                    }
                case TdhOutputType.Tid:
                    {
                        return "win:TID";
                    }
                case TdhOutputType.Port:
                    {
                        return "win:Port";
                    }
                case TdhOutputType.Ipv4:
                    {
                        return "win:IPv4";
                    }
                case TdhOutputType.Ipv6:
                    {
                        return "win:IPv6";
                    }
                case TdhOutputType.SocketAddress:
                    {
                        return "win:SocketAddress";
                    }
                case TdhOutputType.CimDateTime:
                    {
                        return "win:CIMDateTime";
                    }
                case TdhOutputType.EtwTime:
                    {
                        return "win:ETWTIME";
                    }
                case TdhOutputType.Xml:
                    {
                        return "win:Xml";
                    }
                case TdhOutputType.ErrorCode:
                    {
                        return "win:ErrorCode";
                    }
                case TdhOutputType.Win32Error:
                    {
                        return "win:Win32Error";
                    }
                case TdhOutputType.Ntstatus:
                    {
                        return "win:NTSTATUS";
                    }
                case TdhOutputType.Hresult:
                    {
                        return "win:HResult";
                    }
                case TdhOutputType.CultureInsensitiveDatetime:
                    {
                        return "win:DateTimeCultureInsensitive";
                    }
                case TdhOutputType.Json:
                    {
                        return "win:Json";
                    }
                case TdhOutputType.ReducedString:
                    {
                        return "xs:string"; // unsure
                    }
                case TdhOutputType.NoPrin:
                    {
                        return "xs:hexbinary";      // unsure
                    }
                default:
                    {
                        //
                        // This occurs rarely, not sure how to handle this.
                        //
                        Trace(TraceLoggerType.EtwProviderParser,
                              TraceEventType.Warning,
                              $"Unrecognized OutputType: {Type}");
                        return "win:Binary";
                    }
            }
        }

        public static string TraceLevelToSchemaType(string Level)
        {
            NativeTraceControl.EventTraceLevel level;

            if (!Enum.TryParse(Level, out level))
            {
                return Level; // it is a custom defined level
            }

            //
            // see https://learn.microsoft.com/en-us/windows/win32/wes/eventmanifestschema-outputtype-complextype
            //
            switch (level)
            {
                case NativeTraceControl.EventTraceLevel.LogAlways:
                case NativeTraceControl.EventTraceLevel.Verbose:
                    {
                        return "win:Verbose";
                    }
                case NativeTraceControl.EventTraceLevel.Information:
                    {
                        return "win:Informational";
                    }
                case NativeTraceControl.EventTraceLevel.Warning:
                    {
                        return "win:Warning";
                    }
                case NativeTraceControl.EventTraceLevel.Error:
                    {
                        return "win:Error";
                    }
                case NativeTraceControl.EventTraceLevel.Critical:
                    {
                        return "win:Critical";
                    }
                default:
                    {
                        throw new Exception($"Unrecognized trace level {Level}");
                    }

            }

        }
    }
}
