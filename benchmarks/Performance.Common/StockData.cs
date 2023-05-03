// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;

namespace Performance.Common;

public sealed class StockData
{
	private static readonly byte[] FilterPathStartResponseBytes = Encoding.UTF8.GetBytes("{\"items\":[");
	private static readonly byte[] FilterPathItemResponseBytes = Encoding.UTF8.GetBytes("{\"create\":{\"status\":201}}");
	private static readonly byte Comma = (byte)',';
	private static readonly byte[] EndResponseBytes = Encoding.UTF8.GetBytes("]}");

	public DateTime Date { get; init; }
	public double Open { get; init; }
	public double Close { get; init; }
	public double High { get; init; }
	public double Low { get; init; }
	public int Volume { get; init; }
	public string? Symbol { get; init; }

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

		return new StockData
		{
			Date = date,
			Open = open,
			Close = close,
			High = high,
			Low = low,
			Volume = volume,
			Symbol = symbol
		};
	}

	public static StockData[] CreateSampleData(long count)
	{
		var data = new StockData[100_000];

		for (var i = 0; i < count; i++)
		{
			data[i] = ParseFromFileLine("2013-02-08,15.07,15.12,14.63,14.75,8407500,AAL");
		}

		return data;
	}

	public static byte[] CreateSampleDataSuccessWithFilterPathResponseBytes(long count)
	{
		var responseBytesSize = ((FilterPathItemResponseBytes.Length + 1) * count) - 1 + FilterPathStartResponseBytes.Length + EndResponseBytes.Length;
		var responseBytes = new byte[responseBytesSize];

		FilterPathStartResponseBytes.CopyTo(responseBytes, 0);

		var offset = FilterPathStartResponseBytes.Length;

		for (var i = 0; i < count; i++)
		{
			FilterPathItemResponseBytes.CopyTo(responseBytes, offset);

			if (i < count - 1)
			{
				responseBytes[offset + FilterPathItemResponseBytes.Length] = Comma;
				offset += (FilterPathItemResponseBytes.Length + 1);
			}
			else
			{
				offset += (FilterPathItemResponseBytes.Length);
			}
		}

		EndResponseBytes.CopyTo(responseBytes, offset);

		return responseBytes;
	}
}
