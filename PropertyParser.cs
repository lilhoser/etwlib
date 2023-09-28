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
using System.Text;

namespace etwlib
{
    using static NativeTraceConsumer;
    using static TraceLogger;

    public class PropertyParser
    {
        private EventParserBuffers m_Buffers;
        private Dictionary<int, ushort> m_PropertyIndexLookup;
        private List<EVENT_PROPERTY_INFO> m_PropertyInfo;
        private nint m_UserDataCurrentPosition;
        private nint m_UserDataEndPosition;
        private ushort m_UserDataRemaining;
        private bool m_SkipUserData;
        public List<ParsedEtwTemplateItem> m_ParsedEtwTemplateItems;

        public PropertyParser(EventParserBuffers Buffers, bool SkipUserData)
        {
            m_SkipUserData = SkipUserData;
            m_Buffers = Buffers;
            m_PropertyIndexLookup = new Dictionary<int, ushort>();
            if (!m_SkipUserData)
            {
                m_UserDataCurrentPosition = m_Buffers.m_Event.UserData;
                m_UserDataEndPosition =
                    nint.Add(m_UserDataCurrentPosition, m_Buffers.m_Event.UserDataLength);
                m_UserDataRemaining = m_Buffers.m_Event.UserDataLength;
            }
            m_PropertyInfo = new List<EVENT_PROPERTY_INFO>();
            m_ParsedEtwTemplateItems = new List<ParsedEtwTemplateItem>();
        }

        public
        bool
        Initialize()
        {
            //
            // Parse the property array from the TRACE_EVENT_INFO for this event.
            //
            try
            {
                int offset = Marshal.OffsetOf(typeof(TRACE_EVENT_INFO),
                    "EventPropertyInfoArray").ToInt32();
                nint arrayStart = nint.Add(m_Buffers.m_TraceEventInfoBuffer, offset);
                m_PropertyInfo = MarshalHelper.MarshalArray<EVENT_PROPERTY_INFO>(arrayStart,
                            (uint)m_Buffers.m_TraceEventInfo.PropertyCount);
            }
            catch (Exception ex)
            {
                Trace(TraceLoggerType.EtwEventParser,
                    TraceEventType.Error,
                    "Failed to retrieve property array: " + ex.Message);
                return false;
            }
            return true;
        }

        public
        bool
        Parse(
            int PropertyIndexStart,
            int PropertyIndexEnd,
            StringBuilder ParentStruct)
        {
            for (int i = PropertyIndexStart; i < PropertyIndexEnd; i++)
            {
                var propertyInfo = m_PropertyInfo[i];
                string propertyName = "(Unnamed)";
                if (propertyInfo.NameOffset != 0)
                {
                    propertyName = Marshal.PtrToStringUni(
                        nint.Add(m_Buffers.m_TraceEventInfoBuffer,
                            propertyInfo.NameOffset))!;
                }

                bool isArray = false;
                var arrayCount = GetArrayLength(i);

                if (arrayCount > 1 ||
                    (propertyInfo.Flags.HasFlag(PROPERTY_FLAGS.ParamCount) ||
                     propertyInfo.Flags.HasFlag(PROPERTY_FLAGS.ParamFixedCount)))
                {
                    isArray = true;
                }

                //
                // If this property is a scalar integer, remember the value
                // in case it is needed for a subsequent property's length
                // or count.
                //
                if (!propertyInfo.Flags.HasFlag(PROPERTY_FLAGS.Struct) &&
                    !propertyInfo.Flags.HasFlag(PROPERTY_FLAGS.ParamCount) &&
                    propertyInfo.CountOrCountIndex == 1 && !isArray)
                {
                    StorePropertyLookup(i);
                }

                Trace(TraceLoggerType.EtwEventParser,
                      TraceEventType.Verbose,
                      $"Parsing property #{i} [{propertyName}]" +
                      (isArray ? $" - Array with {arrayCount} elements" : ""));

                //
                // For simplicity, non-array properties are treated like 1-length
                // arrays.
                //
                for (int j = 0; j < arrayCount; j++)
                {
                    var usePropertyName = propertyName;

                    if (isArray)
                    {
                        usePropertyName += "-" + j;
                        Trace(TraceLoggerType.EtwEventParser,
                            TraceEventType.Verbose,
                            usePropertyName);
                    }

                    StringBuilder structValue = new StringBuilder();

                    if (propertyInfo.Flags.HasFlag(PROPERTY_FLAGS.Struct))
                    {
                        Trace(TraceLoggerType.EtwEventParser,
                            TraceEventType.Verbose,
                            $"  Property is a struct (start index = " +
                            $"{propertyInfo.StructStartIndex}, count = " +
                            $"{propertyInfo.NumOfStructMembers}), recursing...");

                        //
                        // Recurse structs.
                        //
                        structValue.AppendLine(usePropertyName);
                        int startIndex = propertyInfo.StructStartIndex;
                        int endIndex = startIndex + propertyInfo.NumOfStructMembers;
                        if (!Parse(startIndex, endIndex, structValue))
                        {
                            return false;
                        }
                    }

                    if (structValue.Length > 0)
                    {
                        //
                        // Returning from struct recursion? Use that accumulated
                        // value for this property.
                        //
                        AddProperty(
                            usePropertyName,
                            structValue.ToString(),
                            new StringBuilder(),
                            propertyInfo.LengthOrLengthIndex,
                            i);
                    }
                    else if (!GetPropertyValue(i, j, ParentStruct))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private
        bool
        GetPropertyValue(int PropertyIndex, int ArrayIndex, StringBuilder ParentStruct)
        {
            var propertyInfo = m_PropertyInfo[PropertyIndex];
            var useMap = false;
            var propertyName = "(Unnamed)";
            var propertyLength = GetPropertyLength(PropertyIndex);
            var traceEventInfoBuffer = m_Buffers.m_TraceEventInfoBuffer;
            var eventBuffer = m_Buffers.m_EventBuffer;

            if (propertyInfo.NameOffset != 0)
            {
                propertyName = Marshal.PtrToStringUni(
                    nint.Add(traceEventInfoBuffer, propertyInfo.NameOffset));
            }

            Trace(TraceLoggerType.EtwEventParser,
                TraceEventType.Verbose,
                $"     Property named {propertyName} (length={propertyLength})");

            //
            // If the property has an associated map (i.e. an enumerated type),
            // try to look up the map data. (If this is an array, we only need
            // to do the lookup on the first iteration.)
            //
            if (propertyInfo.MapNameOffset != 0 && ArrayIndex == 0)
            {
                switch (propertyInfo.InType)
                {
                    case TdhInputType.UInt8:
                    case TdhInputType.UInt16:
                    case TdhInputType.UInt32:
                    case TdhInputType.HexInt32:
                        {
                            var mapName = Marshal.PtrToStringUni(
                                    nint.Add(traceEventInfoBuffer, propertyInfo.MapNameOffset));
                            uint sizeNeeded = (uint)EventParserBuffers.MAP_SIZE;
                            var status = TdhGetEventMapInformation(
                                eventBuffer,
                                mapName!,
                                m_Buffers.m_TdhMapBuffer,
                                ref sizeNeeded);
                            if (status != ERROR_SUCCESS)
                            {
                                //
                                // We could retry, but I want to avoid frequent memory
                                // allocations. If this happens, investigate increasing
                                // the static buffer size.
                                //
                                Trace(TraceLoggerType.EtwEventParser,
                                    TraceEventType.Error,
                                    $"TdhGetEventMapInformation() failed: 0x{status:X}");
                                break;
                            }

                            useMap = true;
                            m_Buffers.SetMapInfoBuffer();
                            break;
                        }
                    default:
                        {
                            break;
                        }
                }
            }

            //
            // Loop because we may need to retry the call to TdhFormatProperty.
            //
            for (; ; )
            {
                if (IsEmptyProperty(propertyLength, PropertyIndex) || m_SkipUserData)
                {
                    Trace(TraceLoggerType.EtwEventParser,
                        TraceEventType.Verbose,
                        "     Property has an empty value or userData is being skipped.");
                    AddProperty(propertyName!, "<empty>", ParentStruct, 0, PropertyIndex);
                    return true;
                }

                uint outputBufferSize = 65535; // max is 64kb.
                ushort dataConsumed = 0;
                var result = TdhFormatProperty(
                    traceEventInfoBuffer,
                    useMap ? m_Buffers.m_TdhMapBuffer : nint.Zero,
                    (uint)(m_Buffers.m_Event.EventHeader.Flags.HasFlag(
                        EventHeaderFlags.Is32BitHeader) ? 4 : 8),
                    propertyInfo.InType,
                    propertyInfo.OutType == TdhOutputType.NoPrin ?
                        TdhOutputType.Null : propertyInfo.OutType,
                    propertyLength,
                    m_UserDataRemaining,
                    m_UserDataCurrentPosition,
                    ref outputBufferSize,
                    m_Buffers.m_TdhOutputBuffer,
                    ref dataConsumed);
                if (result == ERROR_INSUFFICIENT_BUFFER)
                {
                    Trace(TraceLoggerType.EtwEventParser,
                        TraceEventType.Error,
                        $"TdhFormatProperty() failed, buffer too small: 0x{result:X}");
                    return false;
                }
                else if (result == ERROR_EVT_INVALID_EVENT_DATA && useMap)
                {
                    //
                    // If the value isn't in the map, TdhFormatProperty treats it
                    // as an error instead of just putting the number in. We'll
                    // try again with no map.
                    //
                    useMap = false;
                    continue;
                }
                else if (result != ERROR_SUCCESS)
                {
                    var error = $"TdhFormatProperty() failed: 0x{result:X}";
                    Trace(TraceLoggerType.EtwEventParser,
                        TraceEventType.Error,
                        error);
                    return false;
                }
                else
                {
                    var propertyValue = Marshal.PtrToStringUni(
                        m_Buffers.m_TdhOutputBuffer);
                    AddProperty(
                        propertyName!, propertyValue!, ParentStruct, propertyLength, PropertyIndex);
                    AdvanceBufferPosition(dataConsumed);
                    Trace(TraceLoggerType.EtwEventParser,
                        TraceEventType.Verbose,
                        $"     Property value is {propertyValue}");
                    return true;
                }
            }
        }

        private
        bool
        IsEmptyProperty(int PropertyLength, int PropertyIndex)
        {
            var propertyInfo = m_PropertyInfo[PropertyIndex];

            //
            // Null data or null-terminated strings are not supported by TdhFormatProperty
            //
            var hasParamLength = propertyInfo.Flags.HasFlag(PROPERTY_FLAGS.ParamLength) ||
                    propertyInfo.Flags.HasFlag(PROPERTY_FLAGS.ParamFixedLength);
            return PropertyLength == 0 && hasParamLength &&
                (propertyInfo.InType == TdhInputType.UnicodeString ||
                 propertyInfo.InType == TdhInputType.AnsiString ||
                 propertyInfo.InType == TdhInputType.Binary);
        }

        private
        void
        StorePropertyLookup(int PropertyIndex)
        {
            if (m_SkipUserData)
            {
                //
                // If the event contains no user data but we still want to parse the
                // event format for manifest information, this call is N/A.
                //
                return;
            }

            var propertyInfo = m_PropertyInfo[PropertyIndex];

            if (m_PropertyIndexLookup.ContainsKey(PropertyIndex))
            {
                //
                // Properties that are repeated in arrays will be revisited for each
                // array element. The index lookup is updated each time.
                //
                m_PropertyIndexLookup.Remove(PropertyIndex);
            }
            //
            // Note: The integer values read here from the Marshaler are
            // read from the current position in the buffer and the buffer
            // should NOT be advanced.
            //
            switch (propertyInfo.InType)
            {
                case TdhInputType.Int8:
                case TdhInputType.UInt8:
                    {
                        if (CanSeek(1))
                        {
                            var value = Marshal.ReadByte(m_UserDataCurrentPosition);
                            m_PropertyIndexLookup.Add(PropertyIndex, value);
                        }
                        else
                        {
                            Trace(TraceLoggerType.EtwEventParser,
                                 TraceEventType.Error,
                                 $"  Unable to read 1 byte for PropertyIndex {PropertyIndex}");
                        }
                        break;
                    }
                case TdhInputType.Int16:
                case TdhInputType.UInt16:
                    {
                        if (CanSeek(2))
                        {
                            var value = (ushort)Marshal.ReadInt16(
                                m_UserDataCurrentPosition);
                            m_PropertyIndexLookup.Add(PropertyIndex, value);
                        }
                        else
                        {
                            Trace(TraceLoggerType.EtwEventParser,
                                 TraceEventType.Error,
                                 $"  Unable to read 2 bytes for PropertyIndex {PropertyIndex}");
                        }
                        break;
                    }
                case TdhInputType.Int32:
                case TdhInputType.UInt32:
                case TdhInputType.HexInt32:
                    {
                        if (CanSeek(4))
                        {
                            var value = Marshal.ReadInt32(m_UserDataCurrentPosition);
                            var asUshort = (ushort)(value > 0xffff ? 0xff : value);
                            m_PropertyIndexLookup.Add(PropertyIndex, asUshort);
                        }
                        else
                        {
                            Trace(TraceLoggerType.EtwEventParser,
                                 TraceEventType.Error,
                                 $"  Unable to read 4 bytes for PropertyIndex {PropertyIndex}");
                        }
                        break;
                    }
                default:
                    {
                        return;
                    }
            }
        }

        private
        ushort
        GetPropertyLength(int PropertyIndex)
        {
            var propertyInfo = m_PropertyInfo[PropertyIndex];

            if (propertyInfo.OutType == TdhOutputType.Ipv6 &&
                propertyInfo.InType == TdhInputType.Binary &&
                propertyInfo.LengthOrLengthIndex == 0 &&
                (!propertyInfo.Flags.HasFlag(PROPERTY_FLAGS.ParamLength) &&
                 !propertyInfo.Flags.HasFlag(PROPERTY_FLAGS.ParamFixedLength)))
            {
                //
                // special case for incorrectly-defined IPV6 addresses
                //
                return 16;
            }

            if (propertyInfo.Flags.HasFlag(PROPERTY_FLAGS.ParamLength))
            {
                if (m_SkipUserData)
                {
                    //
                    // If we're just parsing a manifest, there won't be any user data
                    // and returning 0 here explicitly tells the user this is
                    // variable-length data in the resulting manifest.
                    //
                    return 0;
                }

                //
                // The length of the property was previously parsed, so look that up.
                //
                return m_PropertyIndexLookup[propertyInfo.LengthOrLengthIndex];
            }

            //
            // The length of the property is directly in the field.
            //
            return propertyInfo.LengthOrLengthIndex;
        }

        private
        ushort
        GetArrayLength(int PropertyIndex)
        {
            var propertyInfo = m_PropertyInfo[PropertyIndex];

            if (propertyInfo.Flags.HasFlag(PROPERTY_FLAGS.ParamCount))
            {
                if (m_SkipUserData)
                {
                    //
                    // If we're just parsing a manifest, there won't be any user data
                    // and returning 0 here explicitly tells the user this is
                    // variable-length data in the resulting manifest.
                    //
                    return 0;
                }

                //
                // The length of the array was previously parsed, so look that up.
                //
                return m_PropertyIndexLookup[propertyInfo.CountOrCountIndex];
            }

            //
            // The length of the array is directly in the field.
            //
            return propertyInfo.CountOrCountIndex;
        }

        private
        bool
        CanSeek(int NumBytes)
        {
            return m_UserDataEndPosition.ToInt64() -
                m_UserDataCurrentPosition.ToInt64() >= NumBytes;
        }

        private
        void
        AdvanceBufferPosition(ushort NumBytes)
        {
            Debug.Assert(!m_SkipUserData);
            m_UserDataCurrentPosition = nint.Add(
                m_UserDataCurrentPosition, NumBytes);
            m_UserDataRemaining -= NumBytes;
        }

        private
        void
        AddProperty(
            string PropertyName,
            string PropertyValue,
            StringBuilder ParentStruct,
            ushort PropertyLength,
            int PropertyIndex)
        {
            var propertyInfo = m_PropertyInfo[PropertyIndex];

            if (ParentStruct.Length > 0)
            {
                //
                // We're not directly inserting this property value into the UserData
                // properties because it's actually a field of a struct property we're
                // recursing.  Append it to that value.
                //
                ParentStruct.AppendLine("." + PropertyName + " = " + PropertyValue);
            }
            else
            {
                var parsedProperty = new ParsedEtwTemplateItem(
                    PropertyName,
                    propertyInfo.InType,
                    propertyInfo.OutType,
                    PropertyLength,
                    PropertyValue,
                    PropertyIndex);
                if (m_ParsedEtwTemplateItems.Contains(parsedProperty))
                {
                    Trace(TraceLoggerType.EtwEventParser,
                        TraceEventType.Warning,
                        $"The property {PropertyName} already exists in the list " +
                        $"of template data items parsed from this event. Ignoring this " +
                        $"discovered property value.");
                    return;
                }

                //
                // Remember back-references for parsing manifest. These are resolved
                // when parsing is finished, by ResolveBackreferences().
                //
                if (propertyInfo.Flags.HasFlag(PROPERTY_FLAGS.ParamLength))
                {
                    parsedProperty.FieldBackreference =
                        new ParsedEtwTemplateItem.Backreference(
                            propertyInfo.LengthOrLengthIndex,
                            false); // length-type back reference
                }
                else if (propertyInfo.Flags.HasFlag(PROPERTY_FLAGS.ParamCount))
                {
                    parsedProperty.FieldBackreference =
                        new ParsedEtwTemplateItem.Backreference(
                            propertyInfo.CountOrCountIndex,
                            false); // count-type back reference
                }
                m_ParsedEtwTemplateItems.Add(parsedProperty);
            }
        }

        public
        void
        ResolveBackreferences()
        {
            foreach (var field in m_ParsedEtwTemplateItems)
            {
                if (field.FieldBackreference == null)
                {
                    continue;
                }

                var referencedIndex = field.FieldBackreference.FieldIndex;
                var referencedField = m_ParsedEtwTemplateItems.FirstOrDefault(
                    t => t.Index == referencedIndex);
                if (referencedField == null)
                {
                    throw new Exception($"Unable to resolve back-reference: field " +
                        $"{field.Name} back-references field at index {referencedIndex}");
                }
                field.FieldBackreference.FieldName = referencedField.Name;
            }
        }
    }
}
