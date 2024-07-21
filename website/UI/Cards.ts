/** Handles the cards.html UI */
namespace UI
{
	import IGame = Games.IGame

	/** Generates spell cards for the given spells */
	function spellCards<TSpell extends Data.ISpell>(game : IGame<TSpell>, spells : TSpell[])
	{
		const div = document.getElementById("spell-cards");

		for (const spell of game.cardOrder(spells))
			div.appendChild(game.spellCard(spell, game.books[spell.source]));
	}

	/** Initialized the spell card UI.
	 * Must be called from cards.html on document load.
	 */
	export async function initCards()
	{
		if(window.location.hash)
		{
			var list = getSpellList(window.location.hash.substring(1));
			var prepared = new Set(list.prepared);

			document.title = `Spell cards - ${list.name}`

			await withGameNamed(list.game, async function(g) {
				return spellCards(g, (await g.fetchSources(... list.sources)).filter(s => prepared.has(s.name)) );
			})
		}
		else await withGame(async function(g) {
			const q = new URLSearchParams(window.location.search);
			const f = Data.parseQuery(q.get("q"));

			return spellCards(g, (await g.fetchSources(... q.getAll("from"))).filter(s => g.spellMatchesQuery(f, s)) );
		})
	}
}
