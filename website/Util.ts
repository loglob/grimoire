namespace Util
{
	/**
	 * @returns A <b> element displaying the given text
	 */
	export function bold(txt : string) : HTMLElement
	{
		const b = document.createElement("b");
		b.innerText = txt;
		return b;
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
}