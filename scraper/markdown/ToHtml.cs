using System.Text;
using static Grimoire.Markdown.Parser;

namespace Grimoire.Markdown;

static class ToHtml
{
	private sealed class Inner(PostProcessor post, StringBuilder content)
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

		void convert(Token tk)
		{
			if(nested is not null)
			{
				if(tk is BeginLink)
					throw new FormatException("Nested <a> tags");
				else if(tk is EndLink el)
				{
					nested.checkEnd();
					var uri = post.translateURI(el.Url);

					if(uri is not null)
					{
						content.Append("<a href=\"");
						// there is no builtin method to do this for XML?
						content.Append(uri.ToString().Replace("\"", "%22"));
						content.Append("\">");
						content.Append(nested.content);
						content.Append("</a>");
					}
					else
						content.Append(nested.content);

					nested = null;
				}
				else
					nested.convert(tk);

				return;
			}

			if(tk is ToggleItalic)
				toggle(Tag.EM);
			else if(tk is ToggleBold)
				toggle(Tag.STRONG);
			else if(tk is BeginLink) // use a nested buffer because the href is at the end in MD but the start in HTML
				nested = new Inner(post, new());
			else if(tk is EndLink el)
				throw new FormatException("Orphaned '](…)'");
			else if(tk is Text tx)
				// for some reason, AoN uses HTML mixed with markdown, instead of just HTML
				content.Append(tx.Content);
				// content.Append(HttpUtility.HtmlEncode(tx.Content));
			else if(tk is OpenHtml oh)
			{
				var code = post.translateOpeningHtml(oh);

				if(code is not null)
					content.Append(code);
				else
				{
					content.Append('<');
					content.Append(oh.tag);

					foreach(var entry in oh.attributes)
					{
						content.Append(' ');
						content.Append(entry.Key);
						content.Append("=\"");
						content.Append(entry.Value);
						content.Append('"');
					}

					if(oh.selfClosing)
						content.Append('/');

					content.Append('>');
				}
			}
			else if(tk is CloseHtml ch)
			{
				var code = post.translateClosingHtml(ch.tag);

				if(code is not null)
					content.Append(code);
				else
				{
					content.Append('<');
					content.Append('/');
					content.Append(ch.tag);
					content.Append('>');
				}
			}
			else
				throw new InvalidOperationException($"Impossible token: {tk}");
		}

		public void convert(IEnumerable<Token> tokens)
		{
			foreach(var tk in tokens)
				convert(tk);

			checkEnd();
		}

	}

	private class Outer(PostProcessor post, StringBuilder content)
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

		private void convert(Line line)
		{
			content.AppendLine();

			if(line is EmptyLine)
				close();
			else if(line is Parser.Rule)
			{
				close();
				content.Append("<hr />");
			}
			else if(line is ListItem li)
			{
				pushList(li.Depth, false);
				content.Append("<li> ");
				new Inner(post, content).convert(li.Content);
				content.Append(" </li>");
			}
			else if(line is Headline hl)
			{
				content.Append($"<h{hl.Level}> ");
				new Inner(post, content).convert(hl.Content);
				content.Append($" </h{hl.Level}>");
			}
			else if(line is PlainLine pl)
			{
				pushParagraph();
				new Inner(post, content).convert(pl.Content);

				if(pl.LineBreak)
					content.Append(" <br/>");
			}
			else
				throw new InvalidOperationException();
		}

		public void convert(IEnumerable<Line> line)
		{
			foreach (var ln in line)
				convert(ln);

			close();
		}
	}

	public static string Convert(string markdown, PostProcessor post)
	{
		var p = Parser.ParseLines(markdown.AsSpan());
		var sb = new StringBuilder();
		new Outer(post, sb).convert(p);
		return sb.ToString();
	}
}