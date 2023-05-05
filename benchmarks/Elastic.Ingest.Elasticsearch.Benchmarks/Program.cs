// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

global using Elastic.Transport;
global using Elastic.Ingest.Elasticsearch.Benchmarks;
global using Elastic.Ingest.Elasticsearch.Indices;
global using BenchmarkDotNet.Attributes;

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using System.Globalization;
using Elastic.Ingest.Elasticsearch.Benchmarks.Benchmarks;

#if DEBUG
// MANUALLY RUN A BENCHMARKED METHOD DURING DEBUGGING

//var bm = new BulkIngestion();
//bm.Setup();
//await bm.BulkAllAsync();
//Console.WriteLine("DONE");

var bm = new BulkRequestCreationWithFixedIndexNameBenchmarks();
bm.Setup();
await bm.WriteToStreamAsync_OLD();

var length = bm.MemoryStream.Length;

bm.MemoryStream.Position = 0;
var sr = new StreamReader(bm.MemoryStream);
var json = sr.ReadToEnd();

Console.ReadKey();
#else
var config = ManualConfig.Create(DefaultConfig.Instance);
config.SummaryStyle = new SummaryStyle(CultureInfo.CurrentCulture, true, BenchmarkDotNet.Columns.SizeUnit.B, null!, ratioStyle: BenchmarkDotNet.Columns.RatioStyle.Percentage);
config.AddDiagnoser(MemoryDiagnoser.Default);

BenchmarkRunner.Run<BulkRequestCreationWithTemplatedIndexNameBenchmarks>(config);
#endif
