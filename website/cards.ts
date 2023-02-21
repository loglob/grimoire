/** Generates a single spell card as a self-contained HTML element */
function spellCard(spell : Spell) : HTMLElement
{
	var div = document.createElement("div");
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

		{
			var desc = document.createElement("p");
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
	}

	return div;
}

/** Initialized the spell card UI.
 * Must be called from cards.html on document load.
 */
async function spellCards()
{
	var list = loadSpellList();
	var div = document.getElementById("spell-cards");
	var prepared = new Set(list.prepared);

	document.title = `Spell cards - ${list.name}`

	for (const spell of (await Promise.all(list.sources.map(Spells.getFrom)))
		.flat()
		.filter(s => prepared.has(s.name))
		.sort((a,b) => a.name > b.name ? +1 : -1)
		.sort((a,b) => a.level - b.level))
	{
		div.appendChild(spellCard(spell));
	}

}