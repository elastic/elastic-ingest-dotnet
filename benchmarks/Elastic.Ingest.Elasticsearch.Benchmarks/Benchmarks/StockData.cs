// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Ingest.Elasticsearch.Benchmarks;

internal sealed class StockData
{
	private static readonly Dictionary<string, string> CompanyLookup = new()
{
	{ "AAL", "American Airlines Group Inc" },
	{ "MSFT", "Microsoft Corporation" },
	{ "AME", "AMETEK, Inc." },
	{ "M", "Macy's inc" }
};

	public DateTime Date { get; init; }
	public double Open { get; init; }
	public double Close { get; init; }
	public double High { get; init; }
	public double Low { get; init; }
	public int Volume { get; init; }
	public string? Symbol { get; init; }
	public string? Name { get; init; }

	public static StockData ParseFromFileLine(string dataLine)
	{
		var columns = dataLine.Split(',', StringSplitOptions.TrimEntries);

		var date = DateTime.Parse(columns[0]);

		_ = float.TryParse(columns[1], out var open);
		_ = float.TryParse(columns[1], out var high);
		_ = float.TryParse(columns[1], out var low);
		_ = float.TryParse(columns[1], out var close);

		var volume = int.Parse(columns[5]);
		var symbol = columns[6];

		CompanyLookup.TryGetValue(symbol, out var name);

		return new StockData
		{
			Name = name,
			Date = date,
			Open = open,
			Close = close,
			High = high,
			Low = low,
			Volume = volume,
			Symbol = symbol
		};
	}
}
