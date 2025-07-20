using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Grimoire.Util;

/// <summary>
///  A toroidal buffer
/// </summary>
sealed class Toroid<T> : IReadOnlyCollection<T>
{
	private readonly T[] buffer;
	private int offset = 0;

	public int Capacity
		=> buffer.Length;

	public int Count { get; private set; } = 0;

	/// <summary>
	///  Used to detect concurrent modification during enumeration
    /// </summary>
	private ulong deltaHash = 0;

	public Toroid(int capacity)
	{
		this.buffer = new T[capacity];
	}

	public T this[int ix]
	{
		get
		{
			if(ix < 0 || ix >= Count)
				throw new IndexOutOfRangeException();

			return buffer[(ix + offset) % buffer.Length];
		}
	}

	private sealed class Enumerator : IEnumerator<T>
	{
		private readonly Toroid<T> toroid;
		private int cur = -1;
		private readonly ulong deltaHash;

		public Enumerator(Toroid<T> toroid)
		{
			this.toroid = toroid;
			this.deltaHash = toroid.deltaHash;
		}

		T IEnumerator<T>.Current => toroid[cur];
		object IEnumerator.Current => toroid[cur]!;

		void IDisposable.Dispose()
		{}

		bool IEnumerator.MoveNext()
		{
			if(toroid.deltaHash != deltaHash)
				throw new InvalidOperationException("Collection was modified during enumeration");

			++cur;
			return cur < toroid.Count;
		}

		void IEnumerator.Reset()
		{
			this.cur = -1;
		}
	}

	/// <summary>
	///  Adds onto the end, replacing the head element if Capacity is already reached
	/// </summary>
	public void Push(T entry)
	{
		++deltaHash;
		buffer[(offset + Count) % buffer.Length] = entry;

		if(Count < Capacity)
			++Count;
		else
			offset = (offset + 1) % buffer.Length;
	}

	public bool Pop([MaybeNullWhen(false)] out T head)
	{
		if(Count == 0)
		{
			head = default;
			return false;
		}

		head = buffer[offset];
		++deltaHash;
		offset = (offset + 1) % buffer.Length;
		--Count;

		return true;
	}

	public void Clear()
	{
		++deltaHash;
		this.Count = 0;
		this.offset = 0;
	}

	IEnumerator<T> IEnumerable<T>.GetEnumerator()
		=> new Enumerator(this);

	IEnumerator IEnumerable.GetEnumerator()
		=> new Enumerator(this);
}