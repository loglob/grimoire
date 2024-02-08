
namespace Grimoire;

public abstract class Log
{
	private class Endpoint(TextWriter dest) : Log
	{
		private ( bool onPin, int width ) pinState = (false, 0);

		public void write(string message)
		{
			lock(this)
			{
				if(pinState.onPin)
				{
					dest.WriteLine();
					pinState.onPin = false;
				}

				dest.WriteLine(message);
			}
		}

		override protected void write(string prefix, string message)
			=> write(prefix + (prefix.Length > 0 ? " " : "") + message);

		override public void Pin(string message)
		{
			lock(this)
			{
				if(pinState.onPin)
					dest.Write('\r');
				
				dest.Write(message);

				if(pinState.onPin && message.Length < pinState.width)
					dest.Write(new string(' ', pinState.width - message.Length));
				
				pinState = (true, message.Length);
			}
		}
	}

	private class Prefixed(Endpoint End, string Prefix) : Log
	{
		public readonly Endpoint End = End;
		public readonly string Prefix = Prefix;

		private string pad(string mode, string message)
			=> mode + Prefix + (mode.Length + Prefix.Length > 0 ? " " : "") + message;

		override protected void write(string mode, string message)
			=> End.write(pad(mode, message));

		override public void Pin(string message)
			=> End.Pin(pad("", message));
	}

	protected abstract void write(string mode, string message);
	public abstract void Pin(string message);

	public static readonly Log DEFAULT = new Endpoint(Console.Out);

	private Log()
	{}

	public virtual void Warn(string message)
		=> write("[WARN]", message);

	public virtual void Info(string message)
		=> write("[INFO]", message);

	/// <summary>
	///  Writes a log entry without any prefix
	/// </summary>
	public void Emit(string message)
		=> write("", message);

	public Log AddTags(params string[] tags)
	{
		var pfx = string.Join("", tags.Select(x => "[" + x + "]"));

		return this switch {
			Endpoint e => new Prefixed(e, pfx) ,
			Prefixed t => new Prefixed(t.End, t.Prefix + pfx) ,
			_ => throw new InvalidDataException()
		};
	}
}