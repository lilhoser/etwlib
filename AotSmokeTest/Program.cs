// AOT smoke test for etwlib.
//
// Runs as a published Native AOT executable in CI. Exercises the surface area
// most sensitive to AOT regressions:
//   - LibraryImport-based P/Invoke (the v2.0.0 modernization)
//   - EventRecordCallback / BufferCallback delegate marshalling (the path
//     fixed in 8a2fe371 — pinning of managed delegates passed to native code)
//
// Mirrors the realtime-trace pattern from UnitTests/RealTimeTraceTests.cs so
// privilege requirements are identical (user-mode RPC provider, satisfied by
// the windows-latest runner's Administrators-group membership).
//
// Exits 0 on success; non-zero on any failure path. No assertions framework —
// kept dependency-free so the AOT publish closure stays minimal.

using System.Runtime.InteropServices;
using etwlib;
using static etwlib.NativeTraceConsumer;
using static etwlib.NativeTraceControl;

const int TargetEventCount = 10;
var rpcProviderGuid = new Guid("6ad52b32-d609-4be9-ae07-ce8dae937e39");

TraceLogger.Initialize();
TraceLogger.SetLevel(System.Diagnostics.SourceLevels.Error);

int eventsConsumed = 0;
int parseFailures = 0;

using (var trace = new RealTimeTrace("etwlib AOT Smoke Test"))
using (var parserBuffers = new EventParserBuffers())
{
    trace.AddProvider(rpcProviderGuid, "RPC", EventTraceLevel.Information, 0xFFFFFFFFFFFFFFFF, 0);
    trace.Start();

    trace.Consume(
        new EventRecordCallback(eventPtr =>
        {
            // Use the generic Marshal.PtrToStructure<T> overload — the legacy
            // (IntPtr, Type) overload triggers IL3050 (RequiresDynamicCode)
            // and breaks under AOT. This is the pattern consumers must use.
            var evt = Marshal.PtrToStructure<EVENT_RECORD>(eventPtr);
            try
            {
                var parser = new EventParser(evt, parserBuffers, trace.GetPerfFreq());
                _ = parser.Parse();
            }
            catch
            {
                parseFailures++;
            }
            eventsConsumed++;
        }),
        new BufferCallback(_ =>
        {
            return eventsConsumed >= TargetEventCount ? 0u : 1u;
        }));
}

Console.WriteLine($"events consumed: {eventsConsumed}, parse failures: {parseFailures}");

if (eventsConsumed < TargetEventCount)
{
    Console.Error.WriteLine($"FAIL: only {eventsConsumed} events consumed (target {TargetEventCount})");
    return 1;
}

if (parseFailures > 0)
{
    Console.Error.WriteLine($"FAIL: {parseFailures} parse failures");
    return 2;
}

Console.WriteLine("AOT smoke test passed");
return 0;
