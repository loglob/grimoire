
namespace Games.DnD5e
{
	import bold = Util.bold
	import child = Util.child

	export type Spell =
	{
		name : string, source : string,
		school : string, level : number,
		castingTime : string, reaction : string, ritual : boolean,
		range : string,
		verbal : boolean, somatic : boolean, materials : string|null,
		concentration : boolean, duration : string,
		description : string, upcast : string|null,
		classes : string[],
		statBlock : string|null,
		hint : string|null
	}

	export class Game extends IGame<Spell>
	{
		tableHeaders: (keyof Spell)[] = [
			"level", "school", "castingTime", "ritual", "concentration", "source"
		]
		getSource(s: Spell)
		{
			return s.source;
		}

		spellCard(spell: Spell, book: string): HTMLDivElement
		{
			const div = document.createElement("div");
			div.appendChild( document.createElement("hr") );
	
			{
				var name = document.createElement("h3");
				name.innerText = spell.name;
				var school = document.createElement("p");
				school.innerText = spell.school;
	
				div.append(name, school);
			}
	
			{
				var properties = document.createElement("p");
	
				properties.append(
					"Level: ",
					bold( spell.level ? spell.level.toString() : "Cantrip" ),
					document.createElement("br"),
					"Casting time: ",
					bold( spell.castingTime ),
					document.createElement("br"),
					"Range: ",
					bold( spell.range ),
					document.createElement("br"),
					"Components: ",
					bold( (spell.verbal ? ["V"] : [])
						.concat(spell.somatic ? ["S"] : [])
						.concat(spell.materials ? [`M (${spell.materials})`] : [])
						.join(", ") ),
					document.createElement("br"),
					"Duration: ",
					bold( spell.duration )
				);
	
				div.appendChild( properties );
			}
	
			{
				const desc = document.createElement("p");
				desc.innerHTML = spell.description;
	
				div.appendChild(desc)
			}
	
			if(spell.upcast)
			{
				var ahl = document.createElement("h4");
				ahl.innerText = "At higher levels";
	
				var upc = document.createElement("p");
				upc.innerHTML = spell.upcast;
	
				div.append(ahl, upc);
			}
	
			if(spell.statBlock)
			{
				var sb = document.createElement("p");
				sb.innerHTML = spell.statBlock;
	
				var hr =  document.createElement("hr");
				hr.className = "subtle";
	
				div.append(hr, sb);
			}
	
			{
				const src = document.createElement("p");
				src.id = "from";
				src.className = "subtle";
				src.innerText = spell.hint ? `${book} (${spell.hint})` : book;
	
				div.appendChild(src);
			}
	
			return div;
		}

		spellMatchesTerm(term: string, s: Spell): boolean
		{		
			const term1 = term.substring(1);
			const lim = (term[0] === 'l')
				? term1.split('-').map(x => Number.parseInt(x))
				: [];
			
			return  s.name.toLowerCase().includes(term)
				|| [ s.school, s.castingTime, s.duration, ...s.classes ].some(x => term.toLowerCase() === x)
				|| Util.fieldTermMatch(s, term, "verbal", "somatic", "ritual", "concentration", "upcast")
				|| (s.materials && term === "material")
				|| (this.isPrepared && term === "prepared" && this.isPrepared(s))
				|| (term[0] === '$' && s.materials && s.materials.toLowerCase().includes(term1))
				|| (term[0] === ':' && s.source.toLowerCase() === term1)
				|| (term[0] === '#' && s.hint && s.hint.toLowerCase().includes(term1))
				|| Util.fullTextMatch(term, s.description, s.upcast, s.statBlock)
				|| (term[0] === '\\' && s.name.toLowerCase() === term1)
				|| (term === "$$" && s.materials && /[1-9][0-9,]+\s*gp/i.test(s.materials))
				|| (lim.length == 1 && lim[0] == s.level)
				|| (lim.length == 2 && lim[0] <= s.level && s.level <= lim[1]);
		}

		cardOrder(spells: Spell[]): Spell[]
		{
			return spells
				.sort((a,b) => a.name > b.name ? +1 : -1)
				.sort((a,b) => a.level - b.level)
		}

		details(sp: Spell, book : string, div: HTMLDivElement): void
		{
			// level + class
			child(div, "i").innerText = sp.level
				? `${Util.ordinal(sp.level)}-level ${sp.school}`
				: `${sp.school} Cantrip`;

			// table of properties
			const tr = Util.children(child(div, "table"), "tr", 5);
			child(tr[0], "th").innerText = "Casting Time";
			child(tr[0], "td").innerText = sp.reaction ? `${sp.castingTime}, ${sp.reaction}` : sp.castingTime;
			child(tr[1], "th").innerText = "Range";
			child(tr[1], "td").innerText = sp.range;

			// display components s.t. expensive and consumed materials are highlighted
			{
				child(tr[2], "th").innerText = "Components"
				const comp = child(tr[2], "td");

				comp.append((sp.verbal ? ["V"] : []).concat(sp.somatic ? ["S"] : []).join(", "));

				if(sp.materials)
				{
					const costRegex = /[1-9][0-9,]+\s*gp/ig;
					let nodes : (string|Node)[] = [];
					let off = 0;
					let expensive = false;

					for(let match of Array.from(sp.materials.matchAll(costRegex)))
					{
						nodes.push(sp.materials.substring(off, match.index));
						nodes.push(bold( match[0] ));
						console.log(off, match, match.index + match[0].length);
						off = match.index + match[0].length;
						expensive = true;
					}

					nodes.push(sp.materials.substring(off));

					const consume = sp.materials.toLowerCase().endsWith("consumes");


					if(sp.verbal || sp.somatic)
						comp.append(", ");

					comp.append(expensive ? bold(consume ? "M*" : "M") : "M", " (");
					comp.append(...nodes, ")");
				}
			}

			child(tr[3], "th").innerText = "Duration";
			child(tr[3], "td").innerText = (sp.concentration ? "Concentration, up to " : "") + sp.duration;

			child(tr[4], "th").innerText = "Classes";
			child(tr[4], "td").innerText = sp.classes.join(", ");

			child(div, "hr");
			child(div, "div").innerHTML = sp.description + (sp.statBlock ? sp.statBlock : "");
			child(div, "hr");

			if(sp.upcast)
				child(div, "div").innerHTML = "<strong>At higher levels: </strong>" + sp.upcast;

			child(div, "p", "subtle from").innerText = typeof sp.hint === "string" ? `${book} (${sp.hint})` : book;
		}
	}
}