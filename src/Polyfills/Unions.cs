// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

// C# 15 `union` declarations need these two types. They ship in the net11.0 runtime; on every other
// target framework we define them here, internal, so no public surface depends on them.
// This one file is linked into every library that declares a union, so each assembly has its own internal copy.
#if !NET11_0_OR_GREATER
using System;

namespace System.Runtime.CompilerServices;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
internal sealed class UnionAttribute : Attribute;

internal interface IUnion
{
	object? Value { get; }
}
#endif
