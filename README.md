# TraceSharp

**TraceSharp** is a lightweight, real-time .NET console profiler that uses ETW (Event Tracing for Windows) to monitor CPU usage and managed memory allocations for a given process. Inspired by PerfView, this tool helps developers track resource consumption by method and type, ideal for debugging memory leaks or performance bottlenecks.

## 🔧 Features

- ✅ Monitor CPU usage using Windows Performance Counters
- ✅ Track .NET GC allocations in real-time using ETW
- ✅ Display heap size statistics (Gen0, Gen1, Gen2, LOH)
- ✅ Periodically report top memory-consuming .NET object types
- ✅ Graceful shutdown on `Ctrl+C` with real-time insights

## 🖥️ Example

```bash
TraceSharp.exe <ProcessID>
