

using HtmlAgilityPack;
using System.Data;
using System.Formats.Asn1;
using System.Net;
using System.Text;
using System.Web;
using static Grimoire.Markdown.Parser;

namespace Grimoire.Markdown;

static class ToHtml
{
	private sealed class Inner(StringBuilder content)
	{
		private enum Tag { EM, STRONG };

		private readonly Stack<Tag> state = new();
		/// <summary>
		///  Only used to parse links
		/// </summary>
		private Inner? nested = null;
		private readonly StringBuilder content = content;

		void push(Tag tag)
		{
			if(state.Any(x => x == tag))
				throw new FormatException($"Nesting tag <{tag.ToString().ToLower()}>");

			content.Append($"<{tag.ToString().ToLower()}>");
			state.Push(tag);
		}

		void toggle(Tag tag)
		{
			if(state.TryPeek(out var cur) && cur == tag)
			{
				content.Append($"</{tag.ToString().ToLower()}>");
				state.Pop();
			}
			else
				push(tag);
		}

		/// <summary>
		///  Checks if all tags were matched properly at end of line
		/// </summary>
		void checkEnd()
		{
			if(state.TryPop(out var t))
				throw new FormatException($"Unmatched <{t.ToString().ToLower()}>" + (state.Count > 0 ? $" (and {state.Count} more tags)" : ""));
		}

		void convert(Parser.Token tk)
		{
			if(nested is not null)
			{
				if(tk is Parser.BeginLink)
					throw new FormatException("Nested <a> tags");
				else if(tk is Parser.EndLink el)
				{
					nested.checkEnd();
					// there seems to be no method that encodes " in URIs?!
					content.Append($"<a href=\"{el.Url}\">");
					content.Append(nested.content);
					content.Append("</a>");
					nested = null;
				}
				else
					nested.convert(tk);

				return;
			}

			if(tk is Parser.ToggleItalic)
				toggle(Tag.EM);
			else if(tk is Parser.ToggleBold)
				toggle(Tag.STRONG);
			else if(tk is Parser.BeginLink) // use a nested buffer because the href is at the end in MD but the start in HTML
				nested = new Inner(new());
			else if(tk is Parser.EndLink el)
				throw new FormatException("Orphaned '](…)'");
			else if(tk is Parser.Text tx)
				// for some reason, AoN uses HTML mixed with markdown, instead of just HTML
				content.Append(tx.Content);
				// content.Append(HttpUtility.HtmlEncode(tx.Content));
		}

		public void convert(IEnumerable<Parser.Token> tokens)
		{
			foreach(var tk in tokens)
				convert(tk);

			checkEnd();
		}

	}

	private class Outer(StringBuilder content)
	{
		private abstract record Tag(string HtmlName)
		{
			public string Closing
				=> $"</{HtmlName}>";

			public string Opening
				=> $"<{HtmlName}>";

			public abstract Tag? Pop();
		}

		private record ListTag(int depth, bool Ordered, ListTag? Outer) : Tag(Ordered ? "ol" : "ul")
		{
			public override Tag? Pop()
				=> Outer;
		}
		private record ParagraphTag() : Tag("p")
		{
			public override Tag? Pop()
				=> null;
		}

		private Tag? state = null;

		private void pop()
		{
			content.Append(state!.Closing);
			state = state.Pop();
		}
		private void push(Tag tag)
		{
			content.Append(tag.Opening);
			state = tag;
		}

		private void pushList(int depth, bool ordered)
		{
			while(state is ParagraphTag || (state is ListTag lx && lx.depth > depth))
				pop();

			if(state is ListTag l)
			{
				if(l.depth < depth)
					push(new ListTag(depth, ordered, l));
			}
			else
				push(new ListTag(depth, ordered, null));
		}

		private void pushParagraph()
		{
			while(state is (not null) and (not ParagraphTag))
				pop();

			if(state is null)
			{
				state = new ParagraphTag();
				content.Append(state.Opening);
			}
		}


		private void close()
		{
			while(state is not null)
				pop();
		}

		private void convert(Parser.Line line)
		{
			content.AppendLine();

			if(line is Parser.EmptyLine)
				close();
			else if(line is Parser.Rule)
			{
				close();
				content.Append("<hr />");
			}
			else if(line is Parser.ListItem li)
			{
				pushList(li.Depth, false);
				content.Append("<li> ");
				new Inner(content).convert(li.Content);
				content.Append(" </li>");
			}
			else if(line is Parser.Headline hl)
			{
				content.Append($"<h{hl.Level}> ");
				new Inner(content).convert(hl.Content);
				content.Append($" </h{hl.Level}>");
			}
			else if(line is Parser.PlainLine pl)
			{
				pushParagraph();
				new Inner(content).convert(pl.Content);

				if(pl.LineBreak)
					content.Append(" <br/>");
			}
			else
				throw new InvalidOperationException();
		}

		public void convert(IEnumerable<Parser.Line> line)
		{
			foreach (var ln in line)
				convert(ln);

			close();
		}
	}

	public static string Convert(IEnumerable<Parser.Line> parsed)
	{
		var sb = new StringBuilder();
		new Outer(sb).convert(parsed);
		return sb.ToString();
	}
}