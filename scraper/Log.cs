
using Newtonsoft.Json.Linq;
using System.Text;

public class Log
{
	private readonly string tags;

	public static readonly Log DEFAULT = new("");

	private Log(string tags)
		=> this.tags = tags;

	private void write(string prefix, string message)
		=> Console.Error.WriteLine(prefix + tags + " " + message);


	public void Warn(string message)
		=> write("[WARN]", message);

	public void Info(string message)
		=> write("[INFO]", message);

	public Log AddTags(params string[] tags)
		=> new Log(this.tags + string.Join("", tags.Select(x => "[" + x + "]")));
}