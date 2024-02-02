# Grimoire
A dnd-spells.com replacer that supports custom sources such as homebrew.
Contains two components, a **scraper** that compiles a spell database and a **website** to display that database.

# Scraper
Compiles spells from various sources.

The supported sources are:
- The [dnd-wiki](http://dnd5e.wikidot.com/)
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
Copies all spells found on the [dnd-wiki](http:/dnd5e.wikidot.com/).

Optionally an object with the fields
- `rateLimit`: A number of milliseconds between HTTP requests to the wiki. 250ms by default. 
- `cacheLifetime`: The maximum age for a cache webpage in seconds.

#### latex
Processes local LaTeX files.
For dnd5e, this expects [the rpgtex template](https://github.com/rpgtex/DND-5e-LaTeX-Template) format for spells.
Expects an object with the fields:
- `macroFiles`: A list of filenames to extract macros from.
	No spells are extracted from these and macros from other files are not parsed.
- `files`: A map from book IDs to one or more filenames. The same key may be supplied multiple times to concatenate the given filenames.
- `localManifest`: A path to a JSON file that contains additional values for the `macroFiles` and `files` fields 
- The latex options described below, embedded directly into the object

#### overleaf
Processes files from an overleaf server.

- `latex`: A latex object as described above. Paths are relative to the project root.
- `localMacros`: A list of local files to import macros from
- `cacheLifetime`: The maximum age for a cached project in seconds.
- `auth` Either a string which is a latex share link for the project, or an object containing:
	- `host`: The overleaf instance's base URL
	- `ID`: The project ID to access
	- `email`: A user registered on the overleaf with access to the project
	- `password`: The password for the user


#### copy
An object with the fields:
- `from`: A list of filenames to copy.

Directly copies spells from the given database files.
Expected to be in the same json format that the scraper outputs.


### latex options
Sources that process latex files provide latex options with the fields:
- `spellAnchor`: A string that always precedes a spell definition. Matched before macro expansion.
- `upcastAnchor`: A string that is always between a spell description and upcast description. Optional, only required for dnd5e.
- `environments`: A string -> string dictionary that maps LaTeX environments onto HTML tags, `itemize` or `tabular`. The original latex environment name is preserved as a CSS class.
- `images`: A string -> string dictionary that maps images (either full paths or just filenames) used with `\includegraphics` onto TeX code that should be inserted where they are included instead.

If a file contains a line starting with `%% grimoire begin`, only contents after that line until end of file or a closing `%% grimoire end` is parsed.
The `%% grimoire begin` may also be followed by a book ID which overwrites the current book ID for that segment.
A file may contain multiple such segments.

Otherwise, the file contents between `\begin{document}` and `\end{document}` are searched for spells.
The compiler permits the escape sequence `\< ... \>` for inserting literal HTML code.
Such a sequence may not span over multiple lines, and is copied verbatim, without checking for syntactical correctness.

The latex dialect understood by Grimoire has a few quirks:
- Incomplete invocations in macros are expanded eagerly
	- i.e. `\newcommand{\I}{\textit}` would not work as a shorthand
- The `\forcenewcommand` variant of `\newcommand`, which ignores any following `\renewcommand` for the same macro name
- Math mode, most advanced formatting, `\let`, `\def` and most advanced parsing features (i.e. catcodes) aren't supported

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
### /index.html?\[from=source ...]&\[<game=gd>]&\[<q=query>]
Displays the spell index, using the sources given as `from` parameters, and filtering with the given query.

### /list.html#\[list name]
Displays the local spell list with the given name.

### /details.html?\[from=source]&\[<game=gd>]&\[spell=name]
Displays details on the spell from the given source with the given name.

### /cards.html#\[list name]
Displays spell cards for the local spell list with the given name.

### /cards.html#?\[from=source ...]&\[<game=gd>]&\[<q=query>]
Displays spell cards for all spells matching the given source and query.

### /search-help.html
Static page that shows documentation on query syntax.
