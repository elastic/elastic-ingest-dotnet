// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
using System.Collections.Generic;
using FluentAssertions;

namespace Elastic.Channels.Tests;

/// <summary>An <see cref="ArrayPool{T}"/> that proves every rented array is returned exactly once.</summary>
public sealed class TrackingArrayPool<T> : ArrayPool<T>
{
	private readonly ArrayPool<T> _inner = Create();
	private readonly HashSet<T[]> _outstanding = new(ReferenceEqualityComparer.Instance);
	private readonly List<string> _violations = new();

	public int Rents { get; private set; }

	public override T[] Rent(int minimumLength)
	{
		var array = _inner.Rent(minimumLength);
		lock (_outstanding)
		{
			Rents++;
			if (!_outstanding.Add(array)) _violations.Add("handed out an array that was still outstanding");
		}
		return array;
	}

	public override void Return(T[] array, bool clearArray = false)
	{
		lock (_outstanding)
			if (!_outstanding.Remove(array))
			{
				_violations.Add("returned an array that was not rented from this pool, or was returned twice");
				return;
			}
		_inner.Return(array, clearArray);
	}

	public void AssertBalanced()
	{
		lock (_outstanding)
		{
			_violations.Should().BeEmpty();
			_outstanding.Should().BeEmpty("every rented array must be returned");
		}
	}
}
