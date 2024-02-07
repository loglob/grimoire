public class Log
{
	private readonly string tags;

	public static readonly Log DEFAULT = new("");

	private Log(string tags)
		=> this.tags = tags;

	private void write(string prefix, string message)
		=> Console.Error.WriteLine(prefix + tags + (prefix.Length + tags.Length > 0 ? " " : "") + message);


	public void Warn(string message)
		=> write("[WARN]", message);

	public void Info(string message)
		=> write("[INFO]", message);

	/// <summary>
	///  Writes a log entry without any prefix
	/// </summary>
	public void Emit(string message)
		=> write("", message);

	public Log AddTags(params string[] tags)
		=> new(this.tags + string.Join("", tags.Select(x => "[" + x + "]")));
}