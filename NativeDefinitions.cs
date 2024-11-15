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
using System.Runtime.InteropServices;
using static etwlib.NativeTraceConsumer;

namespace etwlib
{
    public static class NativeGeneral
    {
        [StructLayout(LayoutKind.Explicit, Size = 8)]
        public struct LARGE_INTEGER
        {
            [FieldOffset(0)] public long QuadPart;
            [FieldOffset(0)] public uint LowPart;
            [FieldOffset(4)] public uint HighPart;
        }

    }

    public static class NativeTraceControl
    {
        #region Enums
        public enum EventTraceLevel : byte
        {
            LogAlways = 0,
            Critical = 1,
            Error = 2,
            Warning = 3,
            Information = 4,
            Verbose = 5,
        }

        public enum ControlCode : uint
        {
            Query = 0,
            Stop = 1,
            Update = 2,
            Flush = 3
        }

        public enum EventControlCode : uint
        {
            DisableProvider = 0,
            EnableProvider = 1,
            CaptureState = 2,
        }

        [Flags]
        public enum EnableTraceProperties : uint
        {
            Sid = 0x1,
            TerminalServicesId = 0x2,
            StackTrace = 0x4,
            PsmKey = 0x8,
            IgnoreKeyword0 = 0x10,
            ProviderGroup = 0x20,
            EnableKeyword0 = 0x40,
            ProcessStartKey = 0x80,
            EventKey = 0x100,
            ExcludeInPrivate = 0x200,
        }

        [Flags]
        public enum LogFileModeFlags : uint
        {
            None = 0,
            Sequential = 0x00000001,
            Circular = 0x00000002,
            Append = 0x00000004,
            NewFile = 0x00000008,
            Preallocate = 0x00000020,
            NonStoppable = 0x00000040,
            Secure = 0x00000080,
            RealTime = 0x00000100,
            DelayOpen = 0x00000200,
            Buffering = 0x00000400,
            PrivateLogger = 0x00000800,
            AddHeader = 0x00001000,
            UseKBytesForSize = 0x00002000,
            UseGlobalSequence = 0x00004000,
            UseLocalSequence = 0x00008000,
            Relog = 0x00010000,
            PrivateInProc = 0x00020000,
            Reserved = 0x00100000,
            UsePagedMember = 0x01000000,
            NoPerProcessorBuffering = 0x10000000,
            SystemLogger = 0x02000000,
            AddToTriageDump = 0x80000000,
            StopOnHybridShutdown = 0x00400000,
            PersistOnHybridShutdown = 0x00800000,
            IndependentSession = 0x08000000,
            Compressed = 0x04000000,
        }

        [Flags]
        public enum WNodeFlags : uint
        {
            None = 0,
            AllData = 0x00000001,
            SingleInstance = 0x00000002,
            SingleItem = 0x00000004,
            EventItem = 0x00000008,
            FixedInstanceSize = 0x00000010,
            TooSmall = 0x00000020,
            InstancesSame = 0x00000040,
            StaticInstanceNames = 0x00000080,
            Internal = 0x00000100,
            UseTimestamp = 0x00000200,
            PersistEvent = 0x00000400,
            Reference = 0x00002000,
            AnsiInstanceNames = 0x00004000,
            MethodItem = 0x00008000,
            PDOInstanceNames = 0x00010000,
            TracedGuid = 0x00020000,
            LogWNode = 0x00040000,
            UseGuidPtr = 0x00080000,
            UseMofPtr = 0x00100000,
            NoHeader = 0x00200000,
            SendDataBlock = 0x00400000,
            VersionedProperties = 0x00800000,
        }

        [Flags]
        public enum ProcessTraceMode : uint
        {
            RealTime = 0x00000100,
            RawTimestamp = 0x00001000,
            EventRecord = 0x10000000
        }

        public enum WNodeClientContext : uint
        {
            Default = 0,
            QPC = 1,
            SystemTime = 2,
            CpuCycleCounter = 3
        }

        public enum TRACE_QUERY_INFO_CLASS : uint
        {
            TraceGuidQueryList = 0,
            TraceGuidQueryInfo = 1,
            TraceGuidQueryProcess = 2,
            TraceStackTracingInfo = 3,
            TraceSystemTraceEnableFlagsInfo = 4,
            TraceSampledProfileIntervalInfo = 5,
            TraceProfileSourceConfigInfo = 6,
            TraceProfileSourceListInfo = 7,
            TracePmcEventListInfo = 8,
            TracePmcCounterListInfo = 9,
            TraceSetDisallowList = 10,
            TraceVersionInfo = 11,
            TraceGroupQueryList = 12,
            TraceGroupQueryInfo = 13,
            TraceDisallowListQuery = 14,
            TraceInfoReserved15,
            TracePeriodicCaptureStateListInfo = 16,
            TracePeriodicCaptureStateInfo = 17,
            TraceProviderBinaryTracking = 18,
            TraceMaxLoggersQuery = 19,
            TraceLbrConfigurationInfo = 20,
            TraceLbrEventListInfo = 21,
            TraceMaxPmcCounterQuery = 22,
            TraceStreamCount = 23,
            TraceStackCachingInfo = 24,
            TracePmcCounterOwners = 25,
            TraceUnifiedStackCachingInfo = 26,
            TracePmcSessionInformation = 27,
            MaxTraceSetInfoClass = 28
        }

        [Flags]
        public enum TRACE_PROVIDER_INSTANCE_FLAGS : uint
        {
            TRACE_PROVIDER_FLAG_LEGACY = 1,
            TRACE_PROVIDER_FLAG_PRE_ENABLE = 2
        }

        //
        // ETW filtering
        //
        public const uint EventFilterTypeNone = 0;
        public const uint EventFilterTypeSchematized = 0x80000000;
        public const uint EventFilterTypeSystemFlags = 0x80000001;
        public const uint EventFilterTypeTraceHandle = 0x80000002;
        public const uint EventFilterTypePid = 0x80000004;
        public const uint EventFilterTypeExecutableName = 0x80000008;
        public const uint EventFilterTypePackageId = 0x80000010;
        public const uint EventFilterTypePackageAppId = 0x80000020;
        public const uint EventFilterTypePayload = 0x80000100;
        public const uint EventFilterTypeEventId = 0x80000200;
        public const uint EventFilterTypeEventName = 0x80000400;
        public const uint EventFilterTypeStackwalk = 0x80001000;
        public const uint EventFilterTypeStackwalkName = 0x80002000;
        public const uint EventFilterTypeStackWalkLevelKw = 0x80004000;

        public const uint MaxEventFiltersCount = 13;
        public const uint MaxEventFilterPidCount = 8;
        public const uint MaxEventFilterEventIdCount = 64;
        public const uint MaxEventFilterDataSize = 1024;
        public const uint MaxEventFilterPayloadSize = 4096;
        public const uint MaxStackwalkFrames = 192; // from ENABLE_TRACE_PARAMETERS docs.

        #endregion

        #region Structs
        [StructLayout(LayoutKind.Sequential)]
        public class ENABLE_TRACE_PARAMETERS
        {
            public uint Version;
            public EnableTraceProperties EnableProperty;
            public uint ControlFlags;
            public Guid SourceId;
            public nint EnableFilterDesc;
            public uint FilterDescCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WNODE_HEADER
        {
            public uint BufferSize;
            public uint ProviderId;
            public ulong HistoricalContext;
            public NativeGeneral.LARGE_INTEGER TimeStamp;
            public Guid Guid;
            public WNodeClientContext ClientContext;
            public WNodeFlags Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EVENT_TRACE_PROPERTIES
        {
            public WNODE_HEADER Wnode;
            public uint BufferSize;
            public uint MinimumBuffers;
            public uint MaximumBuffers;
            public uint MaximumFileSize;
            public LogFileModeFlags LogFileMode;
            public uint FlushTimer;
            public uint EnableFlags;
            public uint AgeLimit;
            public uint NumberOfBuffers;
            public uint FreeBuffers;
            public uint EventsLost;
            public uint BuffersWritten;
            public uint LogBuffersLost;
            public uint RealTimeBuffersLost;
            public nint LoggerThreadId;
            public uint LogFileNameOffset;
            public uint LoggerNameOffset;
            public uint VersionNumber;
            public uint FilterDescCount;
            public nint FilterDesc;
            public ulong V2Options;
        }

        [StructLayout(LayoutKind.Sequential, Size = 0xac, CharSet = CharSet.Unicode)]
        public struct TIME_ZONE_INFORMATION
        {
            public int bias;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string standardName;
            [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U2, SizeConst = 8)]
            public ushort[] standardDate;
            public int standardBias;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string daylightName;
            [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U2, SizeConst = 8)]
            public ushort[] daylightDate;
            public int daylightBias;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct TRACE_LOGFILE_HEADER
        {
            public uint BufferSize;
            public uint Version;
            public uint ProviderVersion;
            public uint NumberOfProcessors;
            public NativeGeneral.LARGE_INTEGER EndTime;
            public uint TimerResolution;
            public uint MaximumFileSize;
            public uint LogFileMode;
            public uint BuffersWritten;
            public uint StartBuffers;
            public uint PointerSize;
            public uint EventsLost;
            public uint CpuSpeedInMhz;
            public string LoggerName;
            public string LogFileName;
            public TIME_ZONE_INFORMATION TimeZone;
            public NativeGeneral.LARGE_INTEGER BootTime;
            public NativeGeneral.LARGE_INTEGER PerfFreq;
            public NativeGeneral.LARGE_INTEGER StartTime;
            public uint Reserved;
            public uint BuffersLost;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct EVENT_TRACE_LOGFILE
        {
            public string LogFileName;
            public string LoggerName;
            public long CurrentTime;
            public uint BuffersRead;
            public ProcessTraceMode ProcessTraceMode;
            public EVENT_TRACE CurrentEvent;
            public TRACE_LOGFILE_HEADER LogfileHeader;
            public BufferCallback BufferCallback;
            public uint BufferSize;
            public uint Filled;
            public uint EventsLost;
            public EventRecordCallback EventCallback;
            public uint IsKernelTrace;
            public nint Context;
        }

        //
        // ETW Filtering
        //
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct EVENT_FILTER_DESCRIPTOR
        {
            public nint Ptr;
            public uint Size;
            public uint Type;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EVENT_FILTER_EVENT_ID
        {
            [MarshalAs(UnmanagedType.U1)]
            public bool FilterIn;
            public byte Reserved;
            public ushort Count;
            public ushort Events; // ANYSIZE_ARRAY[1]
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct EVENT_FILTER_LEVEL_KW
        {
            public ulong MatchAnyKeyword;
            public ulong MatchAllKeyword;
            public byte Level;
            [MarshalAs(UnmanagedType.U1)]
            public bool FilterIn;
        }

        //
        // advapi structs for enumerating trace sessions
        //
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct TRACE_GUID_INFO
        {
            public uint InstanceCount;
            public uint Reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct TRACE_PROVIDER_INSTANCE_INFO
        {
            public uint NextOffset;
            public uint EnableCount;
            public uint Pid;
            public TRACE_PROVIDER_INSTANCE_FLAGS Flags;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct TRACE_ENABLE_INFO
        {
            public uint IsEnabled;
            public byte Level;
            public byte Reserved1;
            public ushort LoggerId;
            public EnableTraceProperties EnableProperty;
            public uint Reserved2;
            public ulong MatchAnyKeyword;
            public ulong MatchAllKeyword;
        }

        #endregion

        #region APIs
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint StartTrace(
            [In, Out] ref long SessionHandle,
            [In] string SessionName,
            [In, Out] nint Properties // EVENT_TRACE_PROPERTIES
        );

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint ControlTrace(
            [In] long SessionHandle,
            [In] string SessionName,
            [In, Out] nint Properties, // EVENT_TRACE_PROPERTIES
            [In] ControlCode ControlCode
        );

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern long OpenTrace(
            [In, Out] nint LogFile // EVENT_TRACE_LOGFILE*
        );

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint CloseTrace(
            [In] long SessionHandle
            );

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint ProcessTrace(
            [In] long[] handleArray,
            [In] uint handleCount,
            [In] nint StartTime,
            [In] nint EndTime);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint EnableTraceEx2(
          [In] long SessionHandle,
          [In] Guid ProviderId,
          [In] EventControlCode ControlCode,
          [In] byte Level,
          [In] ulong MatchAnyKeyword,
          [In] ulong MatchAllKeyword,
          [In] uint Timeout,
          [In, Optional] nint EnableParameters // ENABLE_TRACE_PARAMETERS
        );
        #endregion
    }

    public static class NativeTraceConsumer
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        public delegate void EventRecordCallback(
            [In] nint EventRecord
        );

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        public delegate uint BufferCallback(
              [In] nint Logfile // EVENT_TRACE_LOGFILE
            );

        #region Enums

        public enum DecodingSource : uint
        {
            XMLFile = 0,
            Wbem,
            WPP,
            Tlg,
            Max
        }

        [Flags]
        public enum EventHeaderFlags : ushort
        {
            ExtendedInfo = 0x01,
            PrivateSession = 0x02,
            StringOnly = 0x04,
            TraceMessage = 0x08,
            NoCpuTime = 0x10,
            Is32BitHeader = 0x20,
            Is64BitHeader = 0x40,
            ClassicHeader = 0x100,
            ProcessorIndex = 0x200
        }

        public enum EventHeaderExtendedDataType : ushort
        {
            RelatedActivityId = 0x0001,
            Sid = 0x0002,
            TerminalServicesId = 0x0003,
            InstanceInfo = 0x0004,
            StackTrace32 = 0x0005,
            StackTrace64 = 0x0006,
            PebsIndex = 0x0007,
            PmcCounters = 0x0008,
            PsmKey = 0x0009,
            EventKey = 0x000A,
            SchemaTl = 0x000B,
            ProvTraits = 0x000C,
            ProcessStartKey = 0x000D,
            Max = 0x000E,
        }

        [Flags]
        public enum EventHeaderPropertyFlags : ushort
        {
            Xml = 1,
            ForwardedXml = 2,
            LegacyEventLog = 3
        }

        [Flags]
        public enum MAP_FLAGS
        {
            ValueMap = 1,
            Bitmap = 2,
            ManifestPatternMap = 4,
            WbemValueMap = 8,
            WbemBitmap = 16,
            WbemFlag = 32,
            WbemNoMap = 64
        };

        [Flags]
        public enum PROPERTY_FLAGS
        {
            None = 0,
            Struct = 0x1,
            ParamLength = 0x2,
            ParamCount = 0x4,
            WbemXmlFragment = 0x8,
            ParamFixedLength = 0x10,
            ParamFixedCount = 0x20
        }

        public enum TdhInputType : ushort
        {
            Null,
            UnicodeString,
            AnsiString,
            Int8,
            UInt8,
            Int16,
            UInt16,
            Int32,
            UInt32,
            Int64,
            UInt64,
            Float,
            Double,
            Boolean,
            Binary,
            GUID,
            Pointer,
            FILETIME,
            SYSTEMTIME,
            SID,
            HexInt32,
            HexInt64,
            CountedUtf16String = 22,
            CountedMbcsString = 23,
            Struct = 24,
            CountedString = 300,
            CountedAnsiString,
            ReversedCountedString,
            ReversedCountedAnsiString,
            NonNullTerminatedString,
            NonNullTerminatedAnsiString,
            UnicodeChar,
            AnsiChar,
            SizeT,
            HexDump,
            WbemSID
        };

        public enum TdhOutputType : ushort
        {
            Null = 0,  // use TdhInputType
            String = 1,
            DateTime = 2,
            Byte = 3,
            UnsignedByte = 4,
            Short = 5,
            UnsignedShort = 6,
            Integer = 7,
            UnsignedInteger = 8,
            Long = 9,
            UnsignedLong = 10,
            Float = 11,
            Double = 12,
            Boolean = 13,
            Guid = 14,
            HexBinary = 15,
            HexInteger8 = 16,
            HexInteger16 = 17,
            HexInteger32 = 18,
            HexInteger64 = 19,
            Pid = 20,
            Tid = 21,
            Port = 22,
            Ipv4 = 23,
            Ipv6 = 24,
            SocketAddress = 25,
            CimDateTime = 26,
            EtwTime = 27,
            Xml = 28,
            ErrorCode = 29,
            Win32Error = 30,
            Ntstatus = 31,
            Hresult = 32,
            CultureInsensitiveDatetime = 33,
            Json = 34,
            ReducedString = 300,
            NoPrin = 301,
        }

        public enum EVENT_FIELD_TYPE
        {
            KeywordInformation,
            LevelInformation,
            ChannelInformation,
            TaskInformation,
            OpcodeInformation,
            Max
        }

        public enum PAYLOAD_OPERATOR : ushort
        {
            Equal,
            NotEqual,
            LessOrEqual,
            GreaterThan,
            LessThan,
            GreaterOrEqual,
            Between,
            NotBetween,
            Modulo,
            Contains = 20,
            NotContains = 21,
            Is = 30,
            IsNot = 31,
            Max = 32
        }

        #endregion

        #region Structs

        [StructLayout(LayoutKind.Sequential)]
        public struct EVENT_DESCRIPTOR
        {
            public ushort Id;
            public byte Version;
            public byte Channel;
            public byte Level;
            public byte Opcode;
            public ushort Task;
            public ulong Keyword;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EVENT_DATA_DESCRIPTOR
        {
            public long Ptr;
            public int Size;
            public byte Type;
            public byte Reserved1;
            public ushort Reserved2;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EVENT_TRACE_HEADER
        {
            public ushort Size;
            public ushort FieldTypeFlags;
            public byte Type;
            public byte Level;
            public ushort Version;
            public uint ThreadId;
            public uint ProcessId;
            public NativeGeneral.LARGE_INTEGER Timestamp;
            public Guid Guid;
            public ulong ProcessorTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EVENT_TRACE
        {
            public EVENT_TRACE_HEADER Header;
            public uint InstanceId;
            public uint ParentInstanceId;
            public Guid ParentGuid;
            public nint MofData;
            public uint MofLength;
            public uint BufferContext;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EVENT_HEADER
        {
            public ushort Size;
            public ushort HeaderType;
            public EventHeaderFlags Flags;
            public EventHeaderPropertyFlags EventProperty;
            public uint ThreadId;
            public uint ProcessId;
            public long TimeStamp;
            public Guid ProviderId;
            public EVENT_DESCRIPTOR Descriptor;
            public uint KernelTime;
            public uint UserTime;
            public Guid ActivityId;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ETW_BUFFER_CONTEXT
        {
            public byte ProcessorNumber;
            public byte Alignment;
            public ushort LoggerId;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EVENT_HEADER_EXTENDED_DATA_ITEM
        {
            public ushort Reserved1;
            public EventHeaderExtendedDataType ExtType;
            public ushort Reserved2;
            public ushort DataSize;
            public ulong DataPtr;
        };

        [StructLayout(LayoutKind.Sequential)]
        public struct EVENT_RECORD
        {
            public EVENT_HEADER EventHeader;
            public ETW_BUFFER_CONTEXT BufferContext;
            public ushort ExtendedDataCount;
            public ushort UserDataLength;
            public nint ExtendedData; // array of EVENT_HEADER_EXTENDED_DATA_ITEM
            public nint UserData;
            public nint UserContext;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EVENT_MAP_ENTRY
        {
            public int NameOffset;
            public int Value;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EVENT_MAP_INFO
        {
            public int NameOffset;
            public MAP_FLAGS Flag;
            public int EntryCount;
            public int ValueType;
            public EVENT_MAP_ENTRY MapEntryArray;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TRACE_EVENT_INFO
        {
            public Guid ProviderGuid;
            public Guid EventGuid;
            public EVENT_DESCRIPTOR EventDescriptor;
            public DecodingSource Source;
            public int ProviderNameOffset;
            public int LevelNameOffset;
            public int ChannelNameOffset;
            public int KeywordsNameOffset;
            public int TaskNameOffset;
            public int OpcodeNameOffset;
            public int EventMessageOffset;
            public int ProviderMessageOffset;
            public int BinaryXmlOffset;
            public int BinaryXmlSize;
            public int EventNameOffset;
            public int RelatedActivityIDNameOffset;
            public int PropertyCount;
            public int TopLevelPropertyCount;
            public int Flags;
            public nint EventPropertyInfoArray; // EVENT_PROPERTY_INFO[1]
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct EVENT_PROPERTY_INFO
        {
            public PROPERTY_FLAGS Flags;
            public int NameOffset;
            public TdhInputType InType;
            public TdhOutputType OutType;
            public int MapNameOffset;

            public ushort StructStartIndex
            {
                get
                {
                    return (ushort)InType;
                }
            }
            public ushort NumOfStructMembers
            {
                get
                {
                    return (ushort)OutType;
                }
            }
            public ushort CountOrCountIndex;
            public ushort LengthOrLengthIndex;
            public int Reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PROVIDER_EVENT_INFO
        {
            public uint NumberOfEvents;
            public uint Reserved;
            public EVENT_DESCRIPTOR EventDescriptorsArray; // EVENT_DESCRIPTOR[ANYSIZE_ARRAY]
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PROVIDER_FIELD_INFO
        {
            public uint NameOffset;
            public uint DescriptionOffset;
            public ulong Value;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PROVIDER_FIELD_INFOARRAY
        {
            public uint NumberOfElements;
            public EVENT_FIELD_TYPE FieldType;
            public PROVIDER_FIELD_INFO FieldInfoArray; // PROVIDER_FIELD_INFO[ANYSIZE_ARRAY]
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct TRACE_PROVIDER_INFO
        {
            public Guid ProviderGuid;
            public uint SchemaSource;
            public uint ProviderNameOffset;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PROVIDER_ENUMERATION_INFO
        {
            public uint NumberOfProviders;
            public uint Reserved;
            public TRACE_PROVIDER_INFO TraceProviderInfoArray; // TRACE_PROVIDER_INFO[ANYSIZE_ARRAY]
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PAYLOAD_FILTER_PREDICATE
        {
            public string PayloadFieldName;
            public PAYLOAD_OPERATOR CompareOp;
            public string Value;
        }

        #endregion

        #region APIs
        [DllImport("tdh.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint TdhGetEventInformation(
            [In] nint Event, // EVENT_RECORD*
            [In] uint TdhContextCount,
            [In] nint TdhContext,
            [Out] nint Buffer, // TRACE_EVENT_INFO*
            [In, Out] ref uint BufferSize
        );

        [DllImport("tdh.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint TdhGetEventMapInformation(
            [In] nint Event, // EVENT_RECORD*
            [In] string MapName,
            [Out] nint Buffer, // EVENT_MAP_INFO*
            [In, Out] ref uint BufferSize
        );

        [DllImport("tdh.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint TdhFormatProperty(
            [In] nint TraceEventInfo, // TRACE_EVENT_INFO*
            [In, Optional] nint MapInfo,  // EVENT_MAP_INFO*
            [In] uint PointerSize,
            [In] TdhInputType PropertyInType,
            [In] TdhOutputType PropertyOutType,
            [In] ushort PropertyLength,
            [In] ushort UserDataLength,
            [In] nint UserData,           // BYTE*
            [In, Out] ref uint BufferSize,
            [Out, Optional] nint Buffer,  // WCHAR*
            [In, Out] ref ushort UserDataConsumed
        );

        [DllImport("tdh.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint TdhEnumerateProviders(
            [In] nint Buffer,
            [In, Out] ref uint BufferSize
        );

        [DllImport("tdh.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint TdhEnumerateProvidersForDecodingSource(
            [In] DecodingSource DecodingSource,
            [In] nint Buffer,
            [In] uint BufferSize,
            [Out] out uint RequiredSize
        );

        [DllImport("tdh.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint TdhEnumerateManifestProviderEvents(
            [In] ref Guid ProviderGuid,
            [Out] nint Buffer, // PROVIDER_EVENT_INFO*
            [In, Out] ref uint BufferSize
        );

        [DllImport("tdh.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint TdhGetManifestEventInformation(
            [In] ref Guid ProviderGuid,
            [In] nint EventDescriptor, // EVENT_DESCRIPTOR*
            [Out] nint Buffer, // TRACE_EVENT_INFO*
            [In, Out] ref uint BufferSize
        );

        [DllImport("tdh.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint TdhCreatePayloadFilter(
            [In] ref Guid ProviderGuid,
            [In] nint EventDescriptor, // EVENT_DESCRIPTOR*
            [In] [MarshalAs(UnmanagedType.U1)] bool MatchAny,
            [In] uint PayloadPredicateCount,
            [In] nint PayloadPredicates, // PAYLOAD_FILTER_PREDICATE*
            [In, Out] ref nint PayloadFilter    // PVOID*
        );

        [DllImport("tdh.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint TdhDeletePayloadFilter(
            [In] ref nint PayloadFilter    // PVOID*
        );

        [DllImport("tdh.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint TdhAggregatePayloadFilters(
            [In] uint PayloadFilterCount,
            [In] nint[] PayloadFilterPointers, // PVOID*
            [In] byte[] EventMatchAllFlags, // PBOOLEAN
            [In, Out] nint EventFilterDescriptor // EVENT_FILTER_DESCRIPTOR*
        );

        [DllImport("tdh.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint TdhCleanupPayloadEventFilterDescriptor(
            [In] nint EventFilterDescriptor    // EVENT_FILTER_DESCRIPTOR*
        );

        [DllImport("tdh.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint TdhLoadManifest(
            [In] string Manifest
        );

        [DllImport("tdh.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint TdhUnloadManifest(
            [In] string Manifest
        );

        [DllImport("tdh.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint TdhGetAllEventsInformation(
            [In] nint Event,    // PEVENT_RECORD
            [In, Optional] nint WbemService,    // PVOID
            [Out] out uint Index,
            [Out] out uint Count,
            [In, Out] ref nint Buffer,  // PTRACE_INFO*
            [In, Out] ref uint BufferSize
        );

        [DllImport("tdh.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint TdhQueryProviderFieldInformation(
            [In] ref Guid ProviderGuid,
            [In] ulong EventFieldValue,
            [In] EVENT_FIELD_TYPE EventFieldType,
            [In, Out] nint Buffer, // PPROVIDER_FIELD_INFOARRAY
            [In, Out] ref uint BufferSize
        );

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint EnumerateTraceGuidsEx(
            [In] NativeTraceControl.TRACE_QUERY_INFO_CLASS InfoClass,
            [In] nint InBuffer,
            [In] uint InBufferSize,
            [In, Out] nint OutBuffer,
            [In] uint OutBufferSize,
            [In, Out] ref uint ReturnLength
        );
        #endregion

        public const int ERROR_SUCCESS = 0;
        public const int ERROR_FILE_NOT_FOUND = 2;
        public const int ERROR_INVALID_DATA = 13;
        public const int ERROR_INVALID_PARAMETER = 87;
        public const int ERROR_INSUFFICIENT_BUFFER = 122;
        public const int ERROR_ALREADY_EXISTS = 183;
        public const int ERROR_NOT_FOUND = 1168;
        public const int ERROR_XML_PARSE_ERROR = 1465;
        public const int ERROR_RESOURCE_TYPE_NOT_FOUND = 1813;
        public const int ERROR_RESOURCE_NOT_PRESENT = 4316;
        public const int ERROR_WMI_GUID_NOT_FOUND = 4200;
        public const int ERROR_EMPTY = 4306;
        public const int ERROR_EVT_INVALID_EVENT_DATA = 15005;
        public const int ERROR_MUI_FILE_NOT_FOUND = 15100;
        public const int ERROR_MUI_FILE_NOT_LOADED = 15105;
    }
}
