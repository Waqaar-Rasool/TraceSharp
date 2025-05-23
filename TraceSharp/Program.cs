using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Session;
using System.Diagnostics;

namespace TraceSharp
{
	class Program
	{
		static void Main(string[] args)
		{
			if (args.Length < 1 || !int.TryParse(args[0], out int targetPid))
			{
				Console.WriteLine("Usage: TraceSharp <ProcessID>");
				return;
			}
			
			try
			{
				Process.GetProcessById(targetPid);
				Console.WriteLine($"Monitoring process ID: {targetPid}");
			}
			catch (ArgumentException)
			{
				Console.WriteLine($"Error: Process with ID {targetPid} not found.");
				return;
			}

			// Create a cancellation token source for stopping the monitoring
			using (var cts = new CancellationTokenSource())
			{
				Console.CancelKeyPress += (s, e) =>
				{
					e.Cancel = true;
					cts.Cancel();
					Console.WriteLine("Stopping monitoring...");
				};

				// Start monitoring tasks
				var cpuMonitorTask = Task.Run(() => MonitorCpuWithPerformanceCounter(targetPid));
				var memoryMonitorTask = Task.Run(() => MonitorMemoryUsage(targetPid, cts.Token));

				Console.WriteLine("Monitoring started. Press Ctrl+C to stop.");

				// Wait for tasks to complete (when cancellation is requested)
				Task.WaitAll(memoryMonitorTask);
			}

			Console.WriteLine("Monitoring stopped.");
		}

		static void MonitorCpuWithPerformanceCounter(int targetPid)
		{
			string processName = Process.GetProcessById(targetPid).ProcessName;
			var cpuCounter = new PerformanceCounter("Process", "% Processor Time", processName);

			while (true)
			{
				// First call to NextValue() returns 0, so we call it twice
				cpuCounter.NextValue();
				Thread.Sleep(1000);
				float cpuUsage = cpuCounter.NextValue();

				Console.WriteLine($"CPU Usage: {cpuUsage:F2}%");
				Thread.Sleep(2000); 
			}
		}

		static void MonitorMemoryUsage(int targetPid, CancellationToken cancellationToken)
		{
			try
			{
				using (var session = new TraceEventSession($"Memory_Monitor_{Guid.NewGuid()}"))
				{
					// Enable CLR provider for memory information
					session.EnableProvider(
						ClrTraceEventParser.ProviderGuid,
						TraceEventLevel.Verbose,
						(ulong)(ClrTraceEventParser.Keywords.GC | ClrTraceEventParser.Keywords.GCHeapAndTypeNames));

					// Dictionary to store type name to size mappings
					var typeAllocations = new Dictionary<string, long>();

					// Track object allocations
					session.Source.Clr.GCAllocationTick += delegate (GCAllocationTickTraceData data)
					{
						if (data.ProcessID == targetPid)
						{
							var typeName = data.TypeName;
							
							if (!string.IsNullOrEmpty(typeName))
							{
								if (typeAllocations.ContainsKey(typeName))
									typeAllocations[typeName] += data.AllocationAmount;
								else
									typeAllocations[typeName] = data.AllocationAmount;
							}
						}
					};

					// Use a timer for periodic reporting instead of checking in event handlers
					var reportTimer = new Timer(_ =>
					{
						var now = DateTime.Now;

						Console.WriteLine($"[{now:HH:mm:ss}] Memory Usage by Type:");

						// Get the process to report working set
						try
						{
							var process = Process.GetProcessById(targetPid);
							Console.WriteLine($"Total Working Set: {process.WorkingSet64 / 1024 / 1024} MB");
							Console.WriteLine($"Private Memory: {process.PrivateMemorySize64 / 1024 / 1024} MB");
						}
						catch (ArgumentException)
						{
							Console.WriteLine("Process has exited.");
							return;
						}

						// Report top 5 allocation types
						if (typeAllocations.Any())
						{
							var topTypes = typeAllocations
								.OrderByDescending(kvp => kvp.Value)
								.Take(5);

							Console.WriteLine("Top allocated types in last 3 seconds:");
							foreach (var type in topTypes)
							{
								Console.WriteLine($"  {type.Key}: {type.Value / 1024:F2} KB");
							}

							// Clear for next interval
							typeAllocations.Clear();
						}
						else
						{
							Console.WriteLine("No allocations detected in last 3 seconds");
						}

						Console.WriteLine();

					}, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));

					// Track heap statistics
					session.Source.Clr.GCHeapStats += delegate (GCHeapStatsTraceData data)
					{
						if (data.ProcessID == targetPid)
						{
							var now = DateTime.Now;
							Console.WriteLine($"[{now:HH:mm:ss}] GC Heap Statistics:");
							Console.WriteLine($"  Gen0: {data.GenerationSize0 / 1024 / 1024:F2} MB");
							Console.WriteLine($"  Gen1: {data.GenerationSize1 / 1024 / 1024:F2} MB");
							Console.WriteLine($"  Gen2: {data.GenerationSize2 / 1024 / 1024:F2} MB");
							Console.WriteLine($"  LOH: {data.GenerationSize3 / 1024 / 1024:F2} MB");
							Console.WriteLine($"  Total: {data.TotalHeapSize / 1024 / 1024:F2} MB");
							Console.WriteLine();
						}
					};

					// Start processing events
					var processingTask = Task.Run(() =>
					{
						session.Source.Process();
					}, cancellationToken);

					// Keep the session alive until cancellation is requested
					try
					{
						Task.Delay(-1, cancellationToken).Wait();
					}
					catch (OperationCanceledException)
					{
						// Expected when cancellation is requested
					}
					finally
					{
						reportTimer?.Dispose();
						session.Stop();
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error in memory monitoring: {ex.Message}");
			}
		}
	}
}