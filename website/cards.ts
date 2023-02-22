/** Handles the cards.html UI */
namespace Cards
{
	import bold = Util.bold

	/** Generates a single spell card as a self-contained HTML element */
	function spellCard(spell : Spell, book : string) : HTMLElement
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

	/** Generates spell cards for the given spells */
	async function spellCards(spells : Spell[])
	{
		const books = await Util.getSources();
		const div = document.getElementById("spell-cards");

		for (const spell of spells
			.sort((a,b) => a.name > b.name ? +1 : -1)
			.sort((a,b) => a.level - b.level))
		{
			div.appendChild(spellCard(spell, books[spell.source]));
		}
	}

	/** Initialized the spell card UI.
	 * Must be called from cards.html on document load.
	 */
	export async function initUI()
	{
		if(window.location.hash)
		{
			var list = Util.getSpellList(window.location.hash.substring(1));
			var prepared = new Set(list.prepared);

			document.title = `Spell cards - ${list.name}`

			await spellCards( (await Spells.getFrom(... list.sources)).filter(s => prepared.has(s.name)) );
		}
		else
		{
			const q = new URLSearchParams(window.location.search);
			const f = Spells.toFilter(q.get("q"));

			await spellCards( (await Spells.getFrom(... q.getAll("from"))).filter(s => Spells.match(f, s)) );
		}
	}
}
