/** Handles the details.html UI */
namespace Details
{
	import bold = Util.bold;

	/** Turns a number into a corresponding ordinal (1st, 2nd, 3rd etc.) */
	function ordinal(i : number) : string
	{
		switch(i <= 20 ? i : i % 10)
		{
			case 1: return `${i}st`;
			case 2: return `${i}nd`;
			case 3: return `${i}rd`;
			default: return `${i}th`;
		}
	}

	/** Initializes the details UI. Must be called from details.html */
	export async function initUI()
	{
		const p = new URLSearchParams(window.location.search);
		const from = p.get("from");
		const spell = p.get("spell");

		if(!from || !spell)
		{
			window.location.href = "index.html";
			return;
		}

		const book = (await Util.getSources())[from];

		if(!book)
		{
			alert(`No source named ${from} exists!`);
			window.location.href = "index.html";
			return;
		}

		const sp = (await Spells.getFrom(from)).find(s => s.name.toLowerCase() === spell.toLowerCase());

		if(!sp)
		{
			alert(`No spell named ${spell} in source ${book}!`);
			window.location.href = "index.html";
			return;
		}

		document.title = sp.name;

		document.getElementById("spell-name").innerText = sp.name;
		document.getElementById("level+class").innerText = sp.level
			? `${ordinal(sp.level)}-level ${sp.school}`
			: `${sp.school} Cantrip`;
		document.getElementById("casting-time").innerText = sp.reaction ? `${sp.castingTime}, ${sp.reaction}` : sp.castingTime;
		document.getElementById("range").innerText = sp.range;

		// display components s.t. expensive and consumed materials are highlighted
		{
			console.log(sp);
			const comp = document.getElementById("components");

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

		document.getElementById("duration").innerText = (sp.concentration ? "Concentration, up to " : "") + sp.duration;
		document.getElementById("classes").innerText = sp.classes.join(", ");

		document.getElementById("description").innerHTML = sp.description + (sp.statBlock ? sp.statBlock : "");
		if(sp.upcast)
			document.getElementById("upcast").innerHTML = "<strong>At higher levels: </strong>" + sp.upcast;

		document.getElementById("from").innerText = typeof sp.hint === "string" ? `${book} (${sp.hint})` : book;
	}
}
