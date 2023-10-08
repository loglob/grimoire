# Grimoire
A dnd-spells.com replacer that supports custom sources such as homebrew.
Contains two components, a **scraper** that compiles a spell database and a **website** to display that database.

# Scraper
Compiles spells from various sources.

The supported sources are:
- The [dnd-wiki](http://http://dnd5e.wikidot.com/)
- A local latex project
- An overleaf instance

## Usage
Running `dotnet run [<config.json>]` processes all sources configured in the given file and outputs a spell database in the `./db/` directory.

## Configuration
An object with the fields `books` and `sources`.

### books
Gives the recognized source books. A map from (unique) book shorthands onto an object with
- `fullName`: The full name of the book to display to users
- `alts`: Optional list of other names for the book. Used when scraping from the dnd wiki
or just a `fullName` value, with empty `alts`.

### sources
Gives the sources to read from.
A list of source items, which are either objects with a `type` field, or just a value for that field.
The possible `type` values are:

#### dnd-wiki
Copies all spells found on the [dnd-wiki](http://http://dnd5e.wikidot.com/).

Optionally an object with the field
- `rateLimit`: A number of milliseconds between HTTP requests to the wiki. 250ms by default. 

#### latex
Processes local LaTeX files.
Expects an object with the fields:
- `MacroFiles`: A list of filenames to extract macros from.
	No spells are extracted from these and macros from other files are not parsed.
- `Files`: A list of filenames to extract spells from, using the procedure described below
- The latex options described below, embedded directly into the object

#### overleaf
Processes files from an overleaf server.

- `projectID`: the overleaf project ID to scrape
- `password`: the overleaf web API password (see [olspy documentation](https://github.com/loglob/olspy) for more details)
- `host`: optional hostname for the overleaf instance.
	If omitted, attempt to connect to a local docker instance.
- `latex`: a separate object containing latex options as described above.

Within the project, every file marked with `%% grimoire include`, followed by a book ID, within the first 10 lines, is included and parsed like a regular latex file.

#### copy
An object with the fields:
- `From`: A list of filenames to copy.

Directly copies spells from the given database files.
Expected to be in the same json format that the scraper outputs.


### latex options
Sources that process latex files provide latex options with the fields:
- `spellAnchor`: a string that always precedes a spell definition. Matched before macro expansion.
- `upcastAnchor`: a string that is always between a spell description and upcast description.
- `environments`: a string -> string dictionary that maps LaTeX environments onto HTML tags, `itemize` or `tabular`.
- `images`: a string -> string dictionary that maps images (either full paths or just filenames) used with `\includegraphics` onto TeX code that should be inserted where they are included instead.

If a file contains a line starting with `%% grimoire begin`, only contents after that line until end of file or a closing `%% grimoire end` is parsed.
The `%% grimoire begin` may also be followed by a book ID which overwrites the current book ID for that segment.
A file may contain multiple such segments.

Otherwise, the file contents between `\begin{document}` and `\end{document}` are searched for spells.
The compiler permits the escape sequence `\< ... \>` for inserting literal HTML code.
Such a sequence may not span over multiple lines, and is copied verbatim, without checking for syntactical correctness.


# Website
To compile the frontend, run `tsc` in the `website` directory.
Then copy the `db` directory into `website/www/`.
Then, start any webserver on the `website/www/` directory.

## Features
- Listing and filtering spells separated by source
- Creating spell lists from a filtered set of spells
- Selecting a prepared subset from such a spell list
	- Spells outside of the filtered set are also possible
- Creating printable spell cards from those prepared spells

## Endpoints
### /index.html?\[from=source ...]&\[<q=query>]
Displays the spell index, using the sources given as `from` parameters, and filtering with the given query.

### /list.html#\[list name]
Displays the local spell list with the given name.

### /details.html?\[from=source]&\[spell=name]
Displays details on the spell from the given source with the given name.

### /cards.html#\[list name]
Displays spell cards for the local spell list with the given name.

### /cards.html#?\[from=source ...]&\[<q=query>]
Displays spell cards for all spells matching the given source and query.

### /search-help.html
Static page that shows documentation on query syntax.