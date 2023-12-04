namespace Util;

public class ChainBuilder<T>
{
	private readonly List<ArraySegment<T>> chunks = new();
	private readonly List<int> lengths = new();

	public int Length { get; protected set; }

	public void Append(ArraySegment<T> chunk)
	{
		if(chunk.Count == 0)
			return;

		chunks.Add(chunk);
		lengths.Add(Length + chunk.Count);
		Length += chunk.Count;
	}

	public void Append(params T[] chunk)
		=> Append(new ArraySegment<T>(chunk));

	public void Append(IEnumerable<T> items)
		=> Append(items.ToArray());

	public void Append(IEnumerable<ArraySegment<T>> chunks)
	{
		ArgumentNullException.ThrowIfNull(chunks);

		foreach (var ch in chunks)
			Append(ch);
	}

	public void Append(Chain<T> chain)
		=> Append(chain.Chunks());

	public Chain<T> Build()
		=> new(chunks.ToArray(), lengths.ToArray());

	public IEnumerable<T> Items()
		=> chunks.SelectMany(x => x);
}