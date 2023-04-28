// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Diagnostics;
using Elastic.Channels.Example;

// usage [totalEvents] [concurrency]

var totalEvents = args.Length > 0 && int.TryParse(args[0].Replace("_", ""), out var t) ? t : 70_000_000;
var concurrency = args.Length > 1 && int.TryParse(args[1], out var c) ? c : 5;
var maxInFlight = Math.Max(10_000_000, (totalEvents / concurrency) / 10);
var bufferSize = Math.Min(10_000, maxInFlight / 10);

Console.WriteLine($"Total Events: {totalEvents:N0} events");
Console.WriteLine($"Max in flight: {maxInFlight:N0}");
Console.WriteLine();

var sw = Stopwatch.StartNew();
/*
Console.WriteLine("--- System.Threading.Channel write/read to completion---");
var (written, read) = await Drain.RegularChannel(totalEvents, maxInFlight);
sw.Stop();
var messagePerSec = totalEvents / sw.Elapsed.TotalSeconds;
Console.WriteLine($"Written: {written:N0} Read: {read:N0}");
Console.WriteLine($"Duration: {sw.Elapsed:g}");
Console.WriteLine($"Messages per second: {messagePerSec:N0}");
Console.WriteLine();
*/

Console.WriteLine("--- Elastic.Channel write/read to completion---");
var expectedSentBuffers = Math.Max(1, totalEvents / bufferSize);
Console.WriteLine($"Max concurrency: {concurrency:N0}");
Console.WriteLine($"Max outbound buffer: {bufferSize:N0}");
Console.WriteLine($"Expected outbound buffers: {expectedSentBuffers:N0}");
sw.Reset();
sw.Restart();
var (writtenElastic, channel) = await Drain.ElasticChannel(totalEvents, maxInFlight, bufferSize, concurrency, expectedSentBuffers);
sw.Stop();
var messagePerSec = totalEvents / sw.Elapsed.TotalSeconds;

Console.WriteLine();
// channel is a DiagnosticBufferedChannel that pretty prints a lot of useful information
Console.WriteLine(channel);
Console.WriteLine();

Console.WriteLine($"Written buffers: {channel.ExportedBuffers:N0}");
Console.WriteLine($"Written events: {writtenElastic:N0} events");
Console.WriteLine($"ObservedConcurrency: {channel.ObservedConcurrency:N0}");
Console.WriteLine($"Duration: {sw.Elapsed:g}");
Console.WriteLine($"Messages per second: {messagePerSec:N0}");
