// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Elastic.Transport;

namespace Elastic.Ingest.Apm.Model
{
	public class EventIntakeResponse : TransportResponse
	{
		[JsonPropertyName("accepted")]
		public long Accepted { get; set; }

		[JsonPropertyName("errors")]
		//[JsonConverter(typeof(ResponseItemsConverter))]
		public IReadOnlyCollection<IntakeErrorItem> Errors { get; set; } = null!;
	}

	public class IntakeErrorItem
	{
		[JsonPropertyName("message")]
		public string Message { get; set; } = null!;

		[JsonPropertyName("document")]
		public string Document { get; set; } = null!;
	}
}
