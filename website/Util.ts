namespace Util
{
	/** Collator for case-insensitive string compare */
	const coll = Intl.Collator("en", { "sensitivity": "accent", "usage": "search" })

	/** case-insensitive string comparison */
	export function same(l : string, r : string) : boolean
	{
		return l.length === r.length && coll.compare(l, r) === 0;
	}

	/** case-insensitive string.includes */
	export function infixOf(needle : string, haystack : string) : boolean
	{
		return RegExp(
			needle.replace(/[/\\^$*+?.()|[\]{}]/g, '\\$&'),
			"i"
			).test(haystack);
	}

	/** checks if needle is a delimited word within haystack, case insensitive */
	export function infixWordOf(needle : string, haystack : string) : boolean
	{
		return RegExp(
			"(^|\\W)" +  needle.replace(/[/\\^$*+?.()|[\]{}]/g, '\\$&') + "($|\\W)",
			"i"
			).test(haystack);
	}

	/**
	 * @returns A <b> element displaying the given HTML code
	 */
	export function bold(html : string) : HTMLElement
	{
		const b = document.createElement("b");
		b.innerHTML = html;
		return b;
	}

	export function wrap(tag : keyof HTMLElementTagNameMap, ...content : (string|Node)[]) : HTMLElement
	{
		const node = document.createElement(tag)
		node.append(...content)
		return node
	}

	/** Turns a number into a corresponding ordinal (1st, 2nd, 3rd etc.) */
	export function ordinal(i : number) : string
	{
		switch(i <= 20 ? i : i % 10)
		{
			case 1: return `${i}st`;
			case 2: return `${i}nd`;
			case 3: return `${i}rd`;
			default: return `${i}th`;
		}
	}

	/** Converts camelCase to Title Case */
	export function camelToTitle(str : string) : string
	{
		const words = str.split(/(?=[A-Z])/)
		words[0] = words[0][0].toUpperCase() + words[0].substring(1)

		return words.join(" ")
	}

	export function child(parent : HTMLElement, ns : keyof HTMLElementTagNameMap, cssClass : string = "") : HTMLElement
	{
		const x = document.createElement(ns)
		parent.appendChild(x);

		if(cssClass)
			x.className = cssClass;

		return x;
	}

	export function children(parent : HTMLElement, ns : keyof HTMLElementTagNameMap, n : number = 1) : HTMLElement[]
	{
		return new Array(n).fill(ns).map(n => child(parent, ns))
	}

	/** Checks a term for full-text match as described in the search help
	 * @param term The exact user-supplied term (after query parsing), with possibly leading '/'
	 * @param test the text fields to check against
	 */
	export function fullTextMatch(term : string, ...test : (string | undefined | null)[]) : boolean
	{
		if(term[0] != '/')
			return false;

		const t1 = term.substring(1);
		const t2 = term.substring(2);

		return test.some(txt =>
			txt && (term[1] === '/' ? infixWordOf(t2, txt) : infixOf(t1, txt))
		);
	}

	/** Checks if `term` is in the list `fields` and also if `obj` has that field set to a truthy value. */
	export function fieldTermMatch<T>(obj : T, term : string, ...fields : (keyof T)[]) : boolean
	{
		return fields.some(f => obj[f] && term === f);
	}

	export function letNull<T,R>(value : T | null, bind : (x : T) => R ) : R | null
	{
		return value !== null ? bind(value) : null;
	}

	export function letIn<T, R>(value : T, bind : (x : T) => R) : R | null
	{
		return bind(value);
	}

	/** null-aware multiplication */
	export function nMul(...xs : (number | null)[]) : number | null
	{
		let acc = 1.0

		for(const x of xs)
		{
			if(x === null)
				return null

			acc *= x
		}

		return acc
	}

	export function nDiv(x : number|null, ...ys : (number | null)[]) : number | null
	{
		if(x === null)
			return null

		for(const y of ys)
		{
			if(y === null)
				return null

			x /= y
		}

		return x
	}

	export function getElement(id : string) : HTMLElement
	{
		const e = document.getElementById(id)

		if(e === null)
			throw Error(`Essential DOM element '${id}' was not found`)

		return e;
	}

	/** Navigates back to the index page and (optionally) issues an alert().
	 * @param error The alert message to show. `undefined` to skip.
	 * @param game The game ID to display on the index.
	 * 				`undefined` to open the current game again,
	 * 				`null` to go to the default game.
	 * @returns
	 */
	export function backToIndex(error ?: string, game ?: string | null) : never
	{
		if(game === undefined)
		{
			// grab game index from current URL
			game = new URLSearchParams(window.location.search).get("game") ?? null;
		}

		return changeLocation(game ? `index.html?game=${game}` : "index.html", error)
	}

	export function changeLocation(newPage : string, errorMessage ?: string) : never
	{
		if(errorMessage !== undefined)
			alert(errorMessage)

		window.location.href = newPage
		throw Error("Redirection didn't fire correctly")
	}
}