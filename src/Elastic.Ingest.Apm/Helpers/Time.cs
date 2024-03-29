// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;

namespace Elastic.Ingest.Apm.Helpers;

/// <summary> </summary>
public static class Epoch
{
	/// <summary>
	/// DateTime.UnixEpoch Field does not exist in .NET Standard 2.0
	/// https://docs.microsoft.com/en-us/dotnet/api/system.datetime.unixepoch
	/// </summary>
	internal static readonly DateTime UnixEpochDateTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

	/// <summary> </summary>
	public static long ToEpoch(this DateTime d) => ToTimestamp(d);
	/// <summary> </summary>
	public static long UtcNow => DateTime.UtcNow.ToEpoch();

	/// <summary>
	/// UTC based and formatted as microseconds since Unix epoch.
	/// </summary>
	/// <param name="dateTimeToConvert">
	/// DateTime instance to convert to timestamp - its <see cref="DateTime.Kind" /> should be
	/// <see cref="DateTimeKind.Utc" />
	/// </param>
	/// <returns>UTC based and formatted as microseconds since Unix epoch</returns>
	internal static long ToTimestamp(DateTime dateTimeToConvert)
	{
		if (dateTimeToConvert.Kind != DateTimeKind.Utc)
			throw new ArgumentException($"{nameof(dateTimeToConvert)}'s Kind should be UTC but instead its Kind is {dateTimeToConvert.Kind}" +
				$". {nameof(dateTimeToConvert)}'s value: {dateTimeToConvert}", nameof(dateTimeToConvert));

		return RoundTimeValue((dateTimeToConvert - UnixEpochDateTime).TotalMilliseconds * 1000);
	}

	internal static long RoundTimeValue(double value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);
}
