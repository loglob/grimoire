import * as Data from "../Data.js"
import * as Util from "../Util.js"
import { withSelectedSpells } from "./Common.js"

/** Handles the cards.html UI for spellcard print view */
/** Initialized the spell card UI.
 * Must be called from cards.html on document load.
 */
export async function initCards()
{
	withSelectedSpells(async function(game, spells, list) {
		if(list !== null)
			document.title = `Spell cards - ${list.name}`

		const div = Util.getElement("spell-cards");
		const q = new URLSearchParams(window.location.search);

		spells = game.cardOrder(spells);
		const s = q.get("sort");

		if(s)
			Data.sortSpells(game, Data.parseSorting(s), spells)

		for (const spell of spells)
			div.appendChild(game.spellCard(spell, game.books[spell.source].title));
	})
}
