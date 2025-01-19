namespace Grimoire.Markdown;

interface PostProcessor
{
	/// <summary>
	///  Handles an opening HTML tag that was specified in the input Markdown.
	///  NOT called for tags the processor produced on its own.
	///  The caller doesn't track whether opening and closing tags are balanced.
	/// </summary>
	/// <returns> The HTML code to insert, or null to preserve the input exactly. </returns>
	string? translateOpeningHtml(Parser.OpenHtml tag);

	/// <summary>
	///  Handles a closing HTML tag that was specified in the input Markdown.
	///  Only called for standalone closing tags.
	/// </summary>
	/// <returns> The HTML code to insert, or null to preserve the input exactly. </returns>
	string? translateClosingHtml(string tag);

	/// <summary>
	///  Handles an URI that was specified in a markdown link.
	/// </summary>
	/// <param name="original"> The exact input from markdown </param>
	/// <returns> An URI to insert instead, or null to not insert a link at all. </returns>
	Uri? translateURI(Uri original);
}