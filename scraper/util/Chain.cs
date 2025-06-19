namespace Grimoire.Util;

public readonly struct Chain<T>
{
	private static int[] computeLengths(ArraySegment<T>[] chunks)
	{
		int cur = 0;
		var off = new int[chunks.Length];

		for (int i = 0; i < chunks.Length; i++)
		{
			cur += chunks[i].Count;
			off[i] = cur;
		}

		return off;
	}

	public static Chain<T> operator +(Chain<T> l, Chain<T> r)
	{
		int cc = l.chunks.Length + r.chunks.Length;
		var chunks = new ArraySegment<T>[cc];
		var lens = new int[cc];

		l.chunks.CopyTo(chunks, 0);
		r.chunks.CopyTo(chunks, l.chunks.Length);
		l.lengths.CopyTo(lens, 0);
		int ll = l.Length;

		for (int i = 0; i < r.lengths.Length; ++i)
			lens[i + l.lengths.Length] = ll + r.lengths[i];

		return new Chain<T>(chunks, lens);
	}

	public static Chain<T> Join(IEnumerable<Chain<T>> chains)
		=> new(chains.SelectMany(c => c.chunks));

	public static Chain<T> Join(params Chain<T>[] chains)
		=> Join((IEnumerable<Chain<T>>)chains);

	public static readonly Chain<T> Empty
		= new();

	private readonly ArraySegment<T>[] chunks;

	/// <summary>
	///  The cumulative length of all chunk prefixes
	///  Makes indexing O(log(n))
	/// </summary>
	private readonly int[] lengths;

	public int Length
		=> lengths.Length > 0 ? lengths[^1] : 0;

	public bool IsEmpty
		=> Length == 0;

	public bool IsNotEmpty
		=> Length > 0;

	public T this[int index]
	{
		get
		{
			if(index < 0 || index >= Length)
				throw new IndexOutOfRangeException();

			var (chunk, off) = chunkOffset(index);
			return chunks[chunk][off];
		}
	}

	public Chain() : this(Array.Empty<ArraySegment<T>>(), Array.Empty<int>())
	{}

	internal Chain(ArraySegment<T>[] chunks, int[] lengths)
	{
		ArgumentNullException.ThrowIfNull(chunks);
		ArgumentNullException.ThrowIfNull(lengths);

		if(chunks.Length != lengths.Length)
			throw new ArgumentOutOfRangeException(nameof(lengths), $"Array length mismatch {chunks.Length} vs {lengths.Length}");

		this.chunks = chunks;
		this.lengths = lengths;
	}

	public Chain(params ArraySegment<T>[] chunks) : this(chunks, computeLengths(chunks))
	{}

	public Chain(IEnumerable<ArraySegment<T>> chunks) : this(chunks.Where(x => x.Count > 0).ToArray())
	{}

	public Chain(IEnumerable<T> items) : this( new ArraySegment<T>(items.ToArray()) )
	{}


	private (int chunk, int off) chunkOffset(int index, int minChunk = 0)
	{
		int i = Array.BinarySearch(lengths, minChunk, lengths.Length - minChunk, index);

		return (i >= 0)
			? (i + 1, 0)
			: (~i, index - (~i > 0 ? lengths[~i - 1] : 0));
	}

	public IEnumerable<ArraySegment<T>> Chunks()
		=> chunks;

	public IEnumerable<T> Items()
	{
		foreach (var chunk in chunks)
		{
			for (int i = 0; i < chunk.Count; i++)
				yield return chunk[i];
		}
	}

	/// <summary>
	///  Performs a slice by decoded, inclusive, indices
	/// </summary>
	private Chain<T> slice(int fromChunk, int fromOff, int toChunk, int toOff)
	{
		if(fromChunk == toChunk)
			return new( chunks[fromChunk].Slice(fromOff, toOff - fromOff + 1) );

		var ch = new ArraySegment<T>[toChunk - fromChunk + 1];

		ch[0] = chunks[fromChunk].Slice(fromOff);
		ch[^1] = chunks[toChunk].Slice(0, toOff + 1);
		Array.Copy(chunks, fromChunk + 1, ch, 1, ch.Length - 2);

		// don't bother optimizing for the special case when lengths can be copied exactly
		return new(ch);

	}

	public Chain<T> Slice(int index, int length)
	{
		if(index < 0 || length < 0 || index + length > Length)
			throw new IndexOutOfRangeException();
		if(length == 0)
			return Empty;
		if(length == Length)
			return this;

		var i = chunkOffset(index);
		var j = chunkOffset(index + length - 1, i.chunk);

		return slice(i.chunk, i.off, j.chunk, j.off);
	}

	public Chain<T> Slice(int index)
		=> Slice(index, Length - index);

	/// <summary>
	///  Special case of Slice() where the lengths can be directly reused
	/// </summary>
	private Chain<T> sliceUntil(int chunk, int offset)
	{
		var nChunks = new ArraySegment<T>[chunk + (offset > 0 ? 1 : 0)];
		var nLens = new int[ nChunks.Length ];

		Array.Copy(chunks, 0, nChunks, 0, chunk);
		Array.Copy(lengths, 0, nLens, 0, chunk);

		if(offset > 0)
		{
			nChunks[chunk] = chunks[chunk].Slice(0, offset);
			nLens[chunk] = offset + (chunk > 0 ? lengths[chunk - 1] : 0);
		}

		return new(nChunks, nLens);
	}

	private Chain<T> sliceFrom(int chunk, int offset)
	{
		if(chunk >= chunks.Length)
			return Empty;

		var nChunks = new ArraySegment<T>[ chunks.Length - chunk  ];
		nChunks[0] = chunks[chunk].Slice(offset);

		Array.Copy(chunks, chunk + 1, nChunks, 1, nChunks.Length - 1);

		return new(nChunks);
	}


	public Chain<T> DropWhile(Func<T, bool> cond)
	{
		int level = 0;

		for (int c = 0; c < chunks.Length; c++)
		{
			var cc = chunks[c];

			for (int i = 0; i < cc.Count; i++)
			{
				if(level == 0 && !cond(cc[i]))
					return sliceFrom(c, i);
			}
		}

		return Empty;
	}


	public Chain<T> TakeWhile(Func<T, bool> cond)
	{
		for (int c = 0; c < chunks.Length; c++)
		{
			var cc = chunks[c];

			for (int i = 0; i < cc.Count; i++)
			{
				if(! cond(cc[i]))
					return sliceUntil(c, i);
			}
		}

		return this;
	}

	private (Chain<T> left, Chain<T> right) splitAt(int fromChunk, int fromOffset, int toChunk, int toOffset)
		=> ( sliceUntil(fromChunk, fromOffset), toChunk >= chunks.Length
				? Empty
				: toOffset + 1 >= chunks[toChunk].Count
					? sliceFrom(toChunk + 1, 0)
					: sliceFrom(toChunk, toOffset + 1) );

	public (Chain<T> left, T at, Chain<T> right) SplitAt(int index)
	{
		var (chunk, offset) = chunkOffset(index);
		var (l, r) = splitAt(chunk, offset, chunk, offset);

		return (l, chunks[chunk][offset], r);
	}

	public Chain<T> Replace(int index, int length, Chain<T> put)
	{
		if(index < 0 || length < 0 || index + length > Length)
			throw new IndexOutOfRangeException();
		if(length == Length)
			return put;

		var i = chunkOffset(index);
		var j = chunkOffset(index + length - 1, i.chunk);
		var cJ = chunks[j.chunk];

		var ch = new ArraySegment<T>[ i.chunk + (i.off > 0 ? 1 : 0) + put.chunks.Length
			+ (j.off + 1 < cJ.Count ? 1 : 0) + (chunks.Length - j.chunk - 1) ];

		Array.Copy(chunks, 0, ch, 0, i.chunk);
		int l = i.chunk;

		if(i.off > 0)
			ch[l++] = chunks[i.chunk].Slice(0, i.off);

		Array.Copy(put.chunks, 0, ch, l, put.chunks.Length);
		l += put.chunks.Length;

		if(j.off + 1 < cJ.Count)
			ch[l++] = cJ.Slice(j.off + 1);

		Array.Copy(chunks, j.chunk + 1, ch, l, chunks.Length - j.chunk - 1);

		return new(ch);
	}

	public (Chain<T> left, T at, Chain<T> right)? SplitOn(Func<T,bool> cond)
	{
		for (int c = 0; c < chunks.Length; c++)
		{
			var cc = chunks[c];

			for (int i = 0; i < cc.Count; i++)
			{
				if(cond(cc[i]))
				{
					var (l,r) = splitAt(c, i, c, i);

					return (l, cc[i], r);
				}
			}
		}

		return null;
	}

	public (Chain<T> left, Chain<T> right)? SplitOn(T[] str, Func<T,T,bool> eq)
	{
		if(str.Length == 0)
			return null;

		var ind = this.Items().FindIndices(str, eq, false).FirstOrDefault(-1);

		if(ind < 0)
			return null;

		var l = chunkOffset(ind);
		var r = chunkOffset(ind + str.Length - 1, l.chunk);

		return splitAt(l.chunk, l.off, r.chunk, r.off);
	}

	public IEnumerable<Chain<T>> SplitBy(Func<T, bool> sep)
	{
		(int c, int i) last = (0,0);

		for(int c = 0; c < chunks.Length; ++c)
		{
			var cc = chunks[c];

			for(int i = 0; i < cc.Count; ++i)
			{
				if(sep(cc[i]))
				{
					var p = i > 0
						? (c,        i: i - 1)
						: (c: c - 1, i: (c > 0 ? chunks[c - 1].Count : 0) - 1);

					if(p.c < last.c || (p.c == last.c && p.i < last.i))
						yield return Empty;
					else
						yield return slice(last.c, last.i, p.c, p.i);

					last = (i + 1 >= cc.Count) ? (c + 1, i) : (c, i + 1);
				}
			}
		}

		yield return sliceFrom(last.c, last.i);
	}

	public override string ToString()
		=> "[ " + string.Join(", ", Items()) + " ]";

	public T? SingleOrNull()
		=> IsEmpty ? default(T?) : (T?)this[0];
}