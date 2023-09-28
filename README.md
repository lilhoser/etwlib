# Introduction

etwlib is a .NET library that provides raw access to Microsoft Windows Event Tracing (ETW) infrastructure including providers, manifests, and event data. etwlib is meant to be the foundation for larger projects that leverage its capabilities and is distributed as a Nuget package.

# Requirements
* Windows 10+ or later operating system with debugging tools installed
* .NET 7+ runtime
* Some features require administrator privileges
* etwlib depends on the [symbolresolver](https://github.com/lilhoser/symbolresolver) [nuget package](https://www.nuget.org/packages/symbolresolver/) for stackwalk filtering

# Getting started

Before diving in, you should understand basic ETW terminology and the mechanics of its related subsystems. A good place to start is Microsoft's own documentation. In addition to the underlying [win32 API documentation](https://learn.microsoft.com/en-us/windows/win32/api/_etw/) available on Microsoft Learn, Microsoft has long been the primary producer of foundational tooling to explore ETW (it is, after all, their primary diagnostic capability aside from WER). In particular, check out Microsoft Message Analyzer's [conceptual tutorial](https://learn.microsoft.com/en-us/message-analyzer/etw-framework-conceptual-tutorial) and Microsoft Perfview's set of [video tutorials](https://learn.microsoft.com/en-us/shows/perfview-tutorial/). Also, review all of the links in the Resources section of this README for futher reading.

# Using etwlib

* Add the etwlib nuget package to your project using the Nuget package manager.
* Reference the namespace: `using etwlib`

## Starting a trace session

To start a real-time, information-level trace for the Microsoft-Windows-RPC provider, with no keywords:

```
using (var trace = new RealTimeTrace(
       "My ETW Trace",
       new Guid("6ad52b32-d609-4be9-ae07-ce8dae937e39"),
       EventTraceLevel.Information,
       0xFFFFFFFFFFFFFFFF,
       0))
using (var parserBuffers = new EventParserBuffers())
{
    try
    {
        trace.Start();

        //
        // Begin consuming events. This is a blocking call.
        //
        trace.Consume(new EventRecordCallback((Event) =>
        {
            var evt = (EVENT_RECORD)Marshal.PtrToStructure(
                    Event, typeof(EVENT_RECORD))!;
            var parser = new EventParser(
                evt,
                parserBuffers,
                trace.GetPerfFreq());
            var parsedEvent = parser.Parse();
        }),
        new BufferCallback((LogFile) =>
        {
            var logfile = Marshal.PtrToStructure(LogFile, typeof(EVENT_TRACE_LOGFILE))!;
            
            //
            // "logfile" contains statistics for the active trace session and is
            // invoked whenever your ETW buffer(s) is full. This is your opportunity
            // to end the trace session blocked above by returning 0.
            //
            if (readyToCancel)
            {
                return 0;
            }
            return 1;
        }));
    }
    catch (Exception ex)
    {
        //
        // Handle...
        //
    }
}
```

To start a file-based trace, simply change the `using` prologue as follows:

```
using (var trace = new FileTrace(target))
using (var parserBuffers = new EventParserBuffers())
```

When an event has been forwarded by the ETW subsystem, the `EventRecordCallback` routine will be called. The argument passed to this callback will be the event, in the format of `ParsedEtwEvent`.

## Retrieving provider information

To get information about a specific registered ETW provider, you can query it by name:

```
var registryProvider = ProviderParser.GetProvider("Microsoft-Windows-Kernel-Registry");
```

or GUID:

```
var rpcProvider = ProviderParser.GetProvider(new Guid("6ad52b32-d609-4be9-ae07-ce8dae937e39"));
```

You can also retrieve all providers:

```
var providers = ProviderParser.GetProviders();
```

The returned `ParsedEtwProvider` in these examples isn't very useful, unless you want to know the name, GUID or source (Xml, WMI, etc). It's more interesting to dump the provider's registered manifest:

```
var manifest = ProviderParser.GetManifest(new Guid("6ad52b32-d609-4be9-ae07-ce8dae937e39"));
var str = manifest.ToString();
var xml = manifest.ToXml();
```

The manifest contains the meta-information for all of its defined events, templates, channels, task/opcodes, and so on. If you're adventurous, dump all manifests for all registered providers (be careful, this is extremely resource intensive):

```
var all = ProviderParser.GetManifests();
foreach (var kvp in all)
{
    var manifest = kvp.Value.ToXml();
}
```

If you would like to download XML manifests, check out my [etwmanifests repo](https://github.com/lilhoser/etwmanifests).

## Using ETW filtering

ETW has a fairly extensive filtering capability built into it, which etwlib exposes to your trace session. Filtering allows you to reduce the amount of event data sent to your session, which helps reduce noise and improve system performance. Some filtering types drastically reduce the impact your session has on system performance, as they filter the event before it is ever created and sent to ETW. These filtering types are scope and attribute filtering. When possible, leverage these types of filters. You can read more about the supported filtering types in the [documentation of EnableTraceEx2 API](https://learn.microsoft.com/en-us/windows/win32/api/evntrace/nf-evntrace-enabletraceex2). Here are the broad categories supported by etwlib, ordered by _decreasing_ efficiency and _increasing_ impact to system performance:

* Scope filtering by process ID, executable file name, MS Store App Id and MS Store Package ID
* Attribute filtering by level, keyword, or event ID
* Stackwalk filtering by event ID
* Payload filtering by any simple field type (string, integer, or guid) specified in published templatized data

ETW allows you to specify up to one of each of these types of filters, and they must be set before tracing begins. For etwlib, this means your filters go here:

```
using (var trace = new RealTimeTrace(.....))
using (var parserBuffers = new EventParserBuffers())
{
    try
    {
        //
        // Set any filters here >>>
        //
        trace.SetXXXXFilter(...)

        //
        // Now start the trace
        //
        trace.Start();

        //
        // Begin consuming events
        //
        trace.Consume(new EventRecordCallback((Event) =>
        {
            .....
        }
    }
    catch(....){}
}
```

Note: etwlib does not currently support filtering in these scenarios:
* private schematized filtering which involves a custom binary format for all event data, and the provider and consumer must know the binary format
* older applications that use legacy ETW (TMF-based WPP or WMI/MOF)
* providers using [https://learn.microsoft.com/en-us/windows/win32/tracelogging/trace-logging-about](TraceLogging)

### Scope filtering

To filter out events not produced in the context of any svchost process (limited to 4 svchost instances):

```
var processes = Process.GetProcesses();
var targets = processes.Where(
    p => p.ProcessName != null && p.ProcessName.Contains("svchost")).Select(
    p => p.Id).Take(4).ToList();
trace.SetProcessFilter(targets);
```

To filter out any event not produced in the context of a given process executable (ETW does not provide Enable parameter for this filter type):

```
trace.SetFilteredExeName(ExeName);
```

To filter out any event not produced in the context of a given App ID (ETW does not provide Enable parameter for this filter type):

```
trace.SetFilteredPackageAppId(AppId);
```

To filter out any event not produced in the context of a given Package ID (ETW does not provide Enable parameter for this filter type):

```
trace.SetFilteredPackageId(PackageId);
```

See the unit tests defined in `FilterByPackageTests.cs` for details on determining processes that are MS Store apps and how to obtain their app and package IDs.

### Attribute filtering

The simplest form of attribute filtering is to specify a level and keyword when you start the trace session:

```
using (var trace = new RealTimeTrace(
       "My ETW Trace",
       new Guid("70eb4f03-c1de-4f73-a051-33d13d5413bd"),
       EventTraceLevel.Information,  << LEVEL
       0xFFFFFFFFFFFFFFFF,           << ANY KEYWORD
       0))                           << ALL KEYWORD
```

This is the most powerful form of filtering, as it is extremely efficient and entirely eliminates unnecessary event production. The worst thing you can do when using ETW filtering is to set the level to `Verbose` and either leverage payload filtering or perform your own filtering. You will surely strain the system and cause very noticable impact.  By understanding the logging level and keywords of interest for the events you want to analyze, you can easily set this attribute filter and go unnoticed. Be sure to read the [documentation](https://learn.microsoft.com/en-us/windows/win32/api/evntrace/nf-evntrace-enabletraceex2) to properly use the `any`/`all` keyword bitmasks.

The other form of attribute filtering, and also extremely lightweight and efficient, is event ID filtering:

```
var eventIds = new List<int> { 5, 7 };
trace.SetEventIdsFilter(eventIds, Enable);
```

### Stackwalk filtering

This type of filtering allows you to capture a stack trace of the thread that caused the ETW event to be produced. ETW will not allow you to capture a stack trace for _all_ events - you must limit either by event ID or level/keyword.  To produce a stack trace for  events of a given ID (set Enable=true to include or false to exclude):

```
var eventIds = new List<int> { 5 };
trace.SetStackwalkEventIdsFilter(eventIds, Enable);
```

To produce a stackwalk trace for _all_ Microsoft-Windows-Kernel-Registry events by keyword and level (note: you must also include the same keyword/level in the trace session constructor `RealTimeTrace`):

```
trace.SetStackwalkLevelKw(
    EventTraceLevel.Information,
    RegistryProviderKeywords.CreateKey | RegistryProviderKeywords.QueryKey,
    0,
    true);
```

The stackwalk data will be appended to the ETW event of interest by the ETW subsystem. It's important to note that stackwalking is quite resource-intensive, and [according to documentation](https://learn.microsoft.com/en-us/windows/win32/api/evntrace/ns-evntrace-enable_trace_parameters_v1), ETW will drop any events whose overall size with stackwalk is greater than or equal to 64kb. It will also truncate stack traces to 192 frames.

### Payload filtering

Payload filtering, the least performant filtering approach, allows you to post-filter ETW events generated by a provider. That is, the provider emits these events to the ETW subsystem which then applies your payload filter. The word "payload" in this context refers to additional fields that the provider appends to the standard ETW fields in an event (also known as "templates" in ETW manifest parlance). These additional fields or templates must be included in the provider's registered manifest on the system. If you try to filter on a field that is not published in the manifest, the API will return an error. Note that providers are just executables with an ETW XML manifest embedded as a resource, which the ETW runtime extracts when the executable registers as an ETW provider. You can dump these manifests using etwlib's `ProviderParser` class discussed later in this README.

A single payload filter applies to one event type and has two parts:
1. An `event descriptor` that describes some attributes of the type of event you want to payload-filter; at a minimum event ID and version
1. One or more `predicates` that describe your desired filtering logic

Predicates apply to template field names and values (e.g., `ValueKeyName="test"`) and are combined with and/or logic. The snippet below shows how to create a single payload filter for Microsoft-Windows-Kernel-Registry `SetValueKey` (ID=5, Version=0) events that have payload template data containing `Status` of 0 (meaning a successful set value key operation):

```
var eventDescriptor = new EVENT_DESCRIPTOR();
eventDescriptor.Id = 5;
eventDescriptor.Version = 0;
var payloadFilter = new PayloadFilter(new Guid("70eb4f03-c1de-4f73-a051-33d13d5413bd"), eventDescriptor, true);
payloadFilter.AddPredicate("status", PAYLOAD_OPERATOR.Equal, "0");
var filters = new List<Tuple<PayloadFilter, bool>>
{
    new Tuple<PayloadFilter, bool>(payloadFilter, false)
};
trace.AddPayloadFilters(filters);
```

To specify that predicate conditions should be OR'd together, pass `true` to the `PayloadFilter` constructor. To require that all predicates match (AND'd together), pass `false`. ETW does not support any more complicated predicate grouping.

You can specify multiple payload filters that handle different events, or even multiple payload filters that handle the same event in different ways. These filters are chained together, similar to firewall rules. If the payload filter list includes multiple filters that reference the same event, you can pass `true` to the `Tuple` constructor in the snippet above to allow the event to be forwarded to your session if _any_ of the individual payload filters evaluate to true. Passing `false` requires that all of these filters evaluate to true. Note that this only applies to payload filters referencing the same event.

# Building etwlib and running the unit tests

To build etwlib, simply clone the repository and build it with Visual Studio Community 2022.

etwlib leverages MSTest for its unit tests, which are found in the `UnitTests` project. To run them, simply right-click on the project and select `Run Tests`. This command will open the Test Explorer window, build the project, and run all of the tests. Note that you will need to run Visual Studio as administrator in order to access some of the kernel ETW providers leveraged in the tests.

To access more extensive diagnostic output from the unit tests, modify the `ConfigureLoggers()` routine to set your desired reporting level - this output will appear in the VS Output window:

```
etwlib.TraceLogger.SetLevel(SourceLevels.Error);
```

The unit tests are an excellent starting point for exploring the capabilities of etwlib.

# Resources
* [Microsoft Message Analyzer](https://learn.microsoft.com/en-us/message-analyzer/getting-started-with-message-analyzer)
* [Microsoft Perfview](https://github.com/microsoft/perfview/)
* [Microsoft Traceview](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/traceview)
* [tracelog.exe command syntax](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/tracelog-command-syntax)
* [tracerpt.exe command syntax](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/tracerpt)
* [Win32 API documentation](https://learn.microsoft.com/en-us/windows/win32/api/_etw/)
* @zodiacon's [EtwExplorer](https://github.com/zodiacon/EtwExplorer)
* @repnz's [etw-provider-docs](https://github.com/repnz/etw-providers-docs)

# Caveats
* Currently, `NotContains`, `Between` and `NotBetween` operators appear to be broken in ETW payload filtering
