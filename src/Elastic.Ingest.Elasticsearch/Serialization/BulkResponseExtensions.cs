// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Linq;

namespace Elastic.Ingest.Elasticsearch.Serialization;

/// <summary>
/// Extension methods for <see cref="BulkResponse"/>.
/// </summary>
public static class BulkResponseExtensions
{
	/// <summary>
	/// Returns <c>true</c> if the HTTP request succeeded and every individual bulk item
	/// has a 2xx status code, meaning all documents were persisted successfully.
	/// </summary>
	public static bool AllItemsPersisted(this BulkResponse response)
	{
		if (!response.ApiCallDetails.HasSuccessfulStatusCode)
			return false;

		if (response.Items == null)
			return false;

		foreach (var item in response.Items)
		{
			if (item.Status < 200 || item.Status > 299)
				return false;
		}

		return true;
	}
}
