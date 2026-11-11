# Gidubba

**Gidubba turns random tables from RPG books and PDFs into fast, searchable, rollable digital tables.**

Version 1.5.1 · Windows 10/11 (64-bit) · by RPG Frequencies

**Formerly TableForge.** Gidubba is the new name for TableForge, from version 1.2.0. Only the name and icon changed:
installing Gidubba over TableForge upgrades it in place and keeps all your tables and settings. Some technical names
stay as they were, so existing installations keep working: the program file is still `TableForge.exe`, your data is
still in `%LOCALAPPDATA%\TableForge`, and Collection files still end in `.tfcollection`.

Paste a table copied from a PDF or book, let Gidubba interpret it, correct anything it got wrong, save it, and roll it
whenever you need it at the table:

**Paste → Review/Correct → Save → Find → Roll → Read**

Gidubba can also:

- clean up difficult PDF-copied table text (wrapped lines, broken hyphenation, stray characters, several columns);
- organize tables into **Collections** and **Folders**, with search and recent rolls;
- mark rows **Unique**, so each comes up only once until you reset the table ("roll without replacement");
- roll with fast **Built-in dice**, or optionally with **dddice** 3D visual dice, including themes from your own
  dddice account;
- follow **linked tables** ("Scavenging → Scavenged Items") and roll **inline dice** inside results ("gain 1d4 Armor");
- optionally **auto-follow linked tables**, rolling down a chain of linked tables by itself, and stopping to let you
  choose on **Manual-selection tables** such as a "current depth" selector;
- make words in a result **bold** or *italic* on the Review screen, shown that way when it is rolled;
- give a table an optional plain-text **description** ("Roll when entering a new region"), shown under its dice while rolling;
- make **several rolls at once**, keeping every result;
- show the latest result in **OBS** with the **Streaming Overlay** (dddice's 3D dice can be a separate OBS source);
- **Copy Table Text** as clean plain text for other tools and VTTs;
- **Copy for Sojour**: just the rows, ready to paste into a Sojour Lookup Table;
- **export to Foundry VTT** as JSON for the Roll Table Importer module;
- **export to Owlbear Rodeo (Tables+)** as Tables+ import JSON (`d20`, `d66` and `2d6` checked in Tables+ so far);
- **export to PUM Companion** as a `.pumb` bundle, one table or a whole Collection, after a review of what will be
  exported, skipped or changed;
- **export a whole Collection** to a `.tfcollection` file and give it to another Gidubba user, who can **import** it as
  a new Collection without changing anything already in their library.

## Install

1. Download `Gidubba-<version>-Setup.exe` from **[GitHub Releases](https://github.com/Gwydione/Gidubba/releases)**.
2. Run the installer. It installs for your Windows user only and needs no administrator rights.
3. Start **Gidubba** from the Start Menu. (If TableForge was installed, the installer replaces it in place and its
   Start Menu shortcut becomes **Gidubba**.)

- **Windows x64 only** for now (64-bit Windows 10 or 11).
- **.NET is included**; nothing else needs installing.
- The **Microsoft Edge WebView2 Runtime** is needed only for the optional dddice visual dice. It comes with Windows 11 and
  current Windows 10. Built-in dice always work, with or without WebView2 and dddice.
- To upgrade, run a newer installer: it replaces the program and keeps your tables. A portable
  `Gidubba-<version>-win-x64.zip` is also published for people who prefer not to install; it keeps its data in the same
  place.

**SmartScreen:** Gidubba is not code-signed, so Windows may show "Windows protected your PC"
when you run the installer. If you downloaded it from the [Gidubba Releases page](https://github.com/Gwydione/Gidubba/releases) and trust it, choose
**More info**, then **Run anyway**. You can compare the download with the `SHA256SUMS.txt` published beside it
(`Get-FileHash Gidubba-<version>-Setup.exe` in PowerShell). Don't turn off SmartScreen or other Windows protection.

## First use

1. Type a name for a **Collection** (a game or campaign) at the top left and press **Enter**.
2. Choose **Paste Table…** and paste a random table copied from a PDF or book.
3. Choose **Interpret**, then review and correct the rows (notes point out anything Gidubba was unsure about).
4. **Save Table**.
5. **Roll**.

## Your data and backups

Everything Gidubba keeps (your tables, settings, dice choice and any dddice connection) is in one folder, which keeps
its TableForge name so that upgrading finds your data:

```
%LOCALAPPDATA%\TableForge
```

- **Open Data Folder**, at the bottom of the Gidubba window, opens it.
- **Uninstalling Gidubba does not delete this folder.** Reinstalling finds your tables again automatically.
- **To back up**, close Gidubba and copy the whole folder somewhere safe. To restore, put it back.
- When a newer version needs to update your data's format, Gidubba first saves an automatic safety copy of your
  database beside it (and changes nothing if it cannot). The older version cannot open the updated data; going back
  means restoring that copy. Gidubba 1.4.0 is such an update (see [Data folder details](#data-folder-details)).
- **Export Collection…** (see [Sharing a Collection](#sharing-a-collection-tfcollection)) saves one collection's tables
  to a file. It is a way to share a collection, not a full backup: it does not include your other collections, recent
  rolls or settings.

## Privacy

Gidubba keeps its data on your computer and has **no telemetry or analytics**. The optional dddice dice make network
requests to dddice; Gidubba never asks for, collects or stores your dddice password, and a connected dddice account's
access token is protected with Windows DPAPI for your Windows user. The optional **Streaming Overlay** (off by default) shares
only the current result with programs on this computer (address `127.0.0.1`); it is never reachable from the network. See
[PRIVACY.txt](PRIVACY.txt) for the details.

## Known limitations

- Text is taken from what you copy: image-only or scanned PDFs need OCR first, which Gidubba does not do.
- Unusual PDF layouts may need some manual cleanup on the Review screen.
- Gidubba does not find tables in a whole PDF by itself; you copy the table you want.
- **Copy Table Text** produces generic tab-separated text, not a format for any particular VTT.
- **Foundry VTT export** is for the Roll Table Importer module; it exports one result set at a time, with no links, and
  Foundry does not reproduce Clamp.
- **Owlbear Rodeo (Tables+) export** has been checked in Tables+ only for `d20`, `d66` and `2d6` tables; other cases
  (negative numbers, gaps and overlaps, `{2d6}`, `#reroll`, a real `1d66`, importing a saved file) are untested.
  `2d6+1`-style dice (several dice with a modifier) are refused for now.
- **PUM Companion export** skips any table it can't reproduce exactly in PUM, and says why. Links become text lines
  unless you tick **Roll linked tables automatically in PUM**, and even then some stay text (the report says why). Inline
  dice are not rolled by PUM, and descriptions are not exported. Rows beyond the dice, which only PUM's own roll overrides
  reach, have not been checked in PUM.
- **Auto-follow** follows one link per result and at most 8 tables in a chain, and never chooses on a Manual-selection
  table. Every table starts as an ordinary table: mark your selector tables yourself.
- **Unique Results:** which results have been used is kept on this computer only, never in exported files, and Reset
  works on a whole table. dddice tries 10 throws per result. Exports to other programs leave out Unique.
- **Copy for Sojour** has been tested only by pasting with **Ctrl+V** into a Sojour Lookup Table; it copies one result
  set's rows, and the table's name and dice are set up in Sojour yourself.
- dddice dice need an internet connection and the WebView2 Runtime.
- Connecting a dddice account takes a few steps in your web browser.
- Windows x64 only for now.
- Only one copy of Gidubba should be open at a time, and closing the window discards unsaved Review edits without asking.
- The parser is deliberately conservative: anything it is unsure about stays together and is flagged on the Review screen
  instead of being guessed. Side-by-side columns that each restart at the same number are not split.
- dddice accounts can use only themes with every standard die; custom dice are not supported.
- The **Streaming Overlay** shows one current result. dddice's 3D dice can be shown in OBS only with a connected dddice
  account, and two copies of Gidubba cannot use the same overlay port.

## Feedback and bug reports

Please report problems and ideas on **[GitHub Issues](https://github.com/Gwydione/Gidubba/issues)**. It helps to include:

- your Gidubba version (bottom right of the window, or **About**);
- your Windows version;
- what you were trying to do, and what happened instead;
- for table-reading problems, the table text you pasted (or a screenshot of it);
- the exact error message, if there was one.

**Never post dddice tokens, `dddice-account.json`, or any account credentials.**

## License

Gidubba is copyright (c) 2026 RPG Frequencies, all rights reserved; see [LICENSE.txt](LICENSE.txt). Third-party
components are listed in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

---

# Using Gidubba in detail

## Everyday use

1. Type a name in the box at the top left (for example a game or campaign) and press **Enter** to create a collection.
2. Choose **Paste Table…**, paste the copied text, and choose **Interpret** (or **Ctrl+Enter**). Check the result on the
   Review screen — notes beside rows say where the parser was unsure — then **Save Table**.
3. Choose a table to open it, then **Roll** (or type a number and press **Enter** to resolve a specific roll).
4. If a result links to another table, a button appears; following it never rolls automatically, unless you turn on
   [Auto-follow linked tables](#auto-follow-linked-tables).
5. After following one or more links, **← Back to …** (just above **Roll**) makes the table you started from current
   again, as a new step at the bottom; every earlier result stays. It never happens by itself. The table list always
   selects the table the Roll screen is on, so **Edit table** and **Delete table** act on that table (when the list's
   folder or search hides it, nothing is selected and they are unavailable until it is shown).

**Help & Tips**, at the bottom of the window, opens the [Gidubba guide](https://rpgfrequencies.com/gidubba-guide.html)
in your web browser.

Keyboard: in the table search box **Enter** opens the first match and **Escape** clears the search. In the table list the
arrow keys move the highlight and **Enter** opens. In “Paste rows into this set…”, **Escape** closes it and **Ctrl+Enter**
adds the rows. On the Review screen, **Ctrl+J** joins the selected row into the row above it (see below), and in a row's
result text **Ctrl+B**, **Ctrl+I** and **Ctrl+Space** make the selected words bold, italic or plain again.

The table list fills the left side, under a heading that says what it shows: "All tables", a folder's tables, or "Search
results" while you search. **Recent rolls (N)**, at the bottom of the main area, shows the last 10 rolls anywhere as
read-only snapshots, with the time, any rerolls and the whole result. It starts collapsed; click its heading to open or
close it. Clicking a roll reopens its table (a snapshot of a deleted table stays readable but cannot be opened). It is
hidden while you paste or review a table.

## Data folder details

One SQLite database file:

```
%LOCALAPPDATA%\TableForge\tableforge.db
```

(typically `C:\Users\<you>\AppData\Local\TableForge\tableforge.db`). It holds your collections, tables, recent tables and
recent rolls. The location is fixed in V1. **Open Data Folder**, at the bottom of the main window, opens this folder. To back
up your data, close Gidubba and copy the whole `TableForge` folder somewhere safe; to start fresh, delete it.

Beside it Gidubba keeps `dice-provider.txt` (one word, `builtin` or `dddice`: your dice choice) and, only if you ever choose
dddice, a `WebView2` folder (the browser profile that draws the dice; it holds no tables). If you connect a dddice account there is
also `dddice-account.json`: the connection's token, encrypted with Windows (DPAPI, readable only by your Windows user), plus your
account's display name, chosen theme, dddice room and dddice account id. Disconnect deletes it. Deleting it by hand just returns
dddice to guest. Once an account has used dddice there is also `dddice-rooms.json`: for each dddice account (by its dddice account
id), the room Gidubba uses for it. It holds no token, password or other credential, and Disconnect keeps it, so connecting the
same account again reuses its room instead of creating another. Deleting it by hand only means a new room may be created.
Once you use the [Streaming Overlay](#streaming-overlay-obs) there are also `streaming-overlay.json` (its settings) and
`streaming-overlay.html` (the page OBS loads). Once you change **Auto-follow linked tables** there is also
`auto-follow.txt` (one word, `on` or `off`).

On first launch Gidubba creates the folder and database itself. When a newer Gidubba needs to update an existing
database to its format, it first saves a copy beside it, `tableforge.pre-v8-from-v7.backup.db` (the format it updates to,
and the one it came from; Gidubba 1.4.0 makes `tableforge.pre-v12-from-v11.backup.db`), and updates nothing if that copy
cannot be made. If an update then fails, Gidubba says so and
where the copy is (to go back, close Gidubba and copy the backup over `tableforge.db`). The newest three copies are kept.
Nothing is copied on an ordinary start. A database from a newer Gidubba is never opened or changed: Gidubba asks you
to install the newer version.

The legal and privacy notes are `LICENSE.txt`, `THIRD-PARTY-NOTICES.txt` and `PRIVACY.txt` (also installed with the program).
Gidubba collects no telemetry.

## Dice expressions

A table's dice are an ordinary `NdM` with **one optional fixed modifier**: `d20`, `2d6`, `2d6+1`, `d20-2`, `3d8+4`
(case does not matter, and the count may be omitted). That is all: no second term, keep/drop, exploding dice, multiplication or
variables. Anything else is refused with a message rather than partly understood.

The legal results include the modifier: `2d6+1` produces 3–13 and `d20-2` produces -1–18, and a table is checked against that
final range. When you type a roll by hand, enter the **final** result (dice already added to the modifier, e.g. 9); Gidubba
never adds the modifier again. Rows for negative results are written like `-1-0`, and open-ended rows like `26+`,
`20 or more` or `1 or less` (not for `d66`). A table may have rows its dice alone never reach (`-10-0` or `26+` on a `d20`,
for modified rolls): when a result set covers every number its dice can roll, the Review screen shows those rows as
information rather than a warning, and a roll typed by hand may be any number the rows cover.

### `2d6` and `d66` are different

- **`2d6`** rolls two six-sided dice and **adds** them: 3 and 5 make **8**. Legal results are every number from 2 to 12.
- **`d66`** rolls two six-sided dice and **reads them in order as tens and ones**: 3 then 5 makes **35**, and 5 then 3 makes 53.
  The legal results are exactly the 36 numbers whose two digits are both 1–6:

  ```
  11 12 13 14 15 16
  21 22 23 24 25 26
  ...
  61 62 63 64 65 66
  ```

  Numbers such as 17, 20, 27, 40 or 60 cannot occur, so a complete d66 table (`11-16`, `21-26`, … `61-66`) has no gaps, and a row
  for an impossible number (or a range such as `15-22` that runs through 17–20) is flagged on the Review screen.

The dice you write decide which one it is; Gidubba never guesses from the rows. A heading such as `D66 RANDOM ENCOUNTER` or
`NAME GENERATOR (D66)` is a d66 table, `2D6 RANDOM ENCOUNTER` is a 2d6 table. `d66` takes **no modifier** (`d66+1` is refused).
Only a bare `d66` is this convention: an explicit count (`1d66`, `2d66`) still means a die with 66 sides, as it did in earlier
release candidates. Typing a roll by hand for a d66 table means the final number (`35`), and impossible numbers are refused.
The roll shows as "Rolled 35 (d66)", and with dddice a d66 is simply two ordinary d6 whose order becomes tens and ones.

## Table description

A table can have an optional **Description**: a few lines of plain text saying when or how to use it, such as "Roll when
entering a new region or when the weather changes." or "Apply +1 if the NPC is already friendly." Type it in the
**Description** box on the Review screen (below the name, dice and folder; up to 2,000 characters, line breaks allowed). A
newly pasted table starts with none: Gidubba never takes one from the pasted text. When saved, line breaks are kept and
spaces or blank lines at the very start and end are removed.

While rolling, the description appears under the table's dice. A table with no description shows nothing there. A
followed table shows its own description in its own step. The description is only a note: it never changes how the table
rolls or resolves, and it does not appear in the table list or Recent rolls.

## Auto-follow linked tables

**Auto-follow linked tables**, under **Roll** on the Roll screen, is **off** by default, and Gidubba remembers it between
sessions. Off, a linked result just shows its **Open …** button, as it always has. On:

- After a roll, or a result you enter yourself, whose result links to one other table, Gidubba opens that table and rolls
  it, and carries on down the chain. Each table it opened says where it came from, such as "Auto-followed from Area Type
  (5)", above its name. The step above keeps its own result.
- Every automatic roll is an ordinary roll: it uses your dice (Built-in or dddice, waiting for dddice's dice to settle),
  goes into Recent rolls and shows on the Streaming Overlay.
- It follows nothing when a result has more than one link, or after several rolls at once. A message says why, and the
  **Open …** buttons are there as usual.
- It stops by itself after 8 linked tables, or when one table has already been rolled automatically twice in the chain
  (a "reroll on this table" row). The link to carry on by hand is still there.
- **Stop** (beside **Roll** while something is under way) ends the chain, cancelling any dddice roll in the air. Leaving
  the table, or turning the setting off, ends it too.

### Manual-selection tables

Some tables are meant to be chosen from, not rolled: a "current depth" or "current region" selector, say. Mark such a
table in Edit table: tick **Manual-selection table** (under **Clamp to Range**), then **Save Table**. Its Roll screen then
says "Manual-selection table: choose the result and enter it below."

- Auto-follow opens a Manual-selection table **without rolling it**. Type the result you want and **Resolve**, and the
  chain carries on from there.
- **Roll** still works on it. A rolled result is not followed automatically; use its **Open …** button.
- **Every table starts as an ordinary table.** Gidubba never guesses from a table's name, dice or description, so mark
  your selector tables yourself.
- Export Collection keeps the setting (`"manualSelection": true`) without changing the file's format version. Gidubba
  1.2.0 imports such a file and ignores the setting.

## Unique Results

A row can be **Unique**: once it has been the result, it is skipped until you reset the table, like drawing a card and
not putting it back. Every other row stays repeatable. Use it for treasure that can be found only once, a deck of
events, or names you don't want to repeat.

**Marking rows.** In Edit table each row has a **Unique** checkbox (or press **Ctrl+U** anywhere in the row). **Mark All
Unique** and **Clear Unique**, just after **Clear Formatting** above the rows, change every row of the result set
(of every result set, when they are shown side by side). Every row starts repeatable, including every row from before
1.4.0.

**Rolling.** Gidubba always rolls the table's own dice. A roll that lands on a used Unique row is discarded and the same
dice are rolled again, so each result still available keeps its usual chance:

- The situational modifier and Clamp to Range apply to every reroll, exactly as to any roll.
- A roll where no row covers it (a gap) is the usual "no result"; it is not rerolled.
- With overlapping rows, or several result sets, a roll is refused if it lands on **any** used Unique row, and an
  accepted roll uses up every Unique row it lands on.
- Only the accepted result is shown and recorded. If rerolls were needed, the result and Recent rolls say "Rerolled 2×".
- If every result the dice can reach has been used, nothing is rolled: "All results used — Reset to roll again."
- Built-in dice try up to 10,000 times for one result. **dddice** tries 10 throws, the first included, since each throw
  takes a few seconds; if none lands on an unused result it stops, uses nothing up and says so. It never switches to
  Built-in dice for you. **Stop** stops a run of rerolls.
- Auto-follow and several rolls at once follow the same rules (each result is used up before the next roll). A result
  you type yourself on a used Unique row is refused: "Result already used — choose another result."

**Seeing and resetting.** On the Roll screen Unique rows are tagged **Unique**; used ones are tagged **Used** and shown
in grey, still readable. A table with Unique rows shows "Unique results: 3 of 8 used" and **Reset Used Results…** right
under **Roll**, which asks first and makes every used result of that table available again. It does not change which
rows are Unique.

Which rows have been used belongs to the row, not its text: editing a row's text or range keeps it used, and deleting
the row deletes its used state too. It is kept in your database only. It is never exported, so an imported collection
starts with every result available. Copy Table Text, Copy for Sojour, Foundry, Tables+ and PUM Companion exports leave
out Unique entirely.

## Sharing a Collection (.tfcollection)

**Export Collection…** (under the Collection list) saves the selected collection as one `.tfcollection` file: its folders,
tables, dice, result sets, rows and their ranges (including `96-00`, negative and open-ended ranges such as `26+`), bold
and italic text, Clamp settings, table descriptions, which rows are Unique and the links between its tables. It is plain,
readable UTF-8 text (JSON). It does not include which results have been used, recent rolls, dice settings or anything
from your other collections.

**Older versions (including TableForge):** a collection whose tables have no descriptions is saved in the same file format as
1.0.0-rc25 (format version 1), so rc25 can import it. A collection with any table description is saved as format version
2, which rc25 refuses ("uses a newer file format… Nothing was changed") instead of importing it without the descriptions.
A collection with any Unique row is saved as format version 3, which Gidubba 1.3.0 and earlier refuse the same way
instead of importing every row as repeatable; after exporting one, the status line says it needs Gidubba 1.4 or later.
This version imports all three.

**Import Collection…** (under the New collection box) reads a `.tfcollection` file and, after showing you its name and
how many folders and tables it holds, adds it as a **new** collection. It never merges into, replaces or changes a
collection you already have. If a collection with the same name exists, the new one is called `Name (2)`, then
`Name (3)`, and so on. Links between its tables point at the imported copies.

The whole file is checked before anything is saved, and the import is all or nothing: a file that is damaged, from a
newer Gidubba, or that contains something Gidubba cannot hold is refused with the reason, and nothing is changed.
A file contains table data only, never anything that runs. A collection whose rows link to a table in a *different*
collection cannot be exported (Gidubba itself never creates such links).

**Delete Collection…** deletes the selected collection with all its folders and tables, after asking you to confirm.
This cannot be undone. Recent rolls from its tables stay readable in Recent rolls. Use it, for example, to remove an
imported collection you no longer want.

## Copy Table Text

**Copy Table Text** puts a clean plain-text copy of a table on the clipboard, ready to paste into a VTT, a document or any other
tool. It is on the Roll screen (under **Export…**, beside the table's name) and on the Review screen (beside **Save Table**), so a freshly
pasted table can be copied out without saving it (it is offered there whenever **Save Table** is). The text is:

```
RANDOM STARTING GEAR
d10

1-2<TAB>Backpack
3<TAB>Knife
```

one result set at a time (with several, a chooser beside the button says which), the name, the dice, a blank line, then one
row per line with a tab between range and result. Ranges keep how they were written (`00`, `08`, `96-00`, d66 as `11`–`66`).
Result text is only trimmed, with line breaks and tabs turned into single spaces; links, inline-roll results, the modifier,
clamp results, the table's description and roll history are never included, and copying changes nothing on screen.

**Export…** → **Copy Table Text (Spaces)** (Roll screen) copies exactly the same text for the same result set, with one ordinary
space instead of the tab between each range and its result (`1-2 Backpack`), for tools where a pasted tab is awkward. It is
plain text, not a format for any particular VTT.

## Copy for Sojour

**Export…** → **Copy for Sojour** (Roll screen) copies only the rows of one result set, one per line with a tab between
range and result, exactly as they appear in Copy Table Text but with no name, dice or blank line above them:

```
1<TAB>Axor, God of the Earth
2<TAB>Dhylesia, Goddess of Dreams
3<TAB>Irus, God of the Sun
```

1. In Gidubba, choose **Export…** → **Copy for Sojour**.
2. In Sojour, create or open a Lookup Table.
3. Select the first cell.
4. Paste with **Ctrl+V**. Each row fills two cells: range and result.

Compatibility was tested by pasting with **Ctrl+V**; other ways of pasting have not been tested. The table's name and dice
are not copied, so set them up in Sojour. Ranges keep how they were written (`96-00`), result text is trimmed with line
breaks and tabs turned into single spaces, inline dice stay as written text, and links, the table's description and roll
state are never copied.

## Foundry VTT export

On the Roll screen, **Export…** → **Copy Foundry JSON** or **Save Foundry JSON…** turns one result set of a saved table into
the JSON that Foundry VTT's [Roll Table Importer](https://foundryvtt.com/packages/roll-table-importer) module imports (in
Foundry: Roll Tables → **Import Tables**, then paste it or choose the file). Copy and Save give exactly the same JSON:

```json
{
  "name": "Goods",
  "formula": "1d12",
  "results": [
    { "range": [1, 4], "text": "Backpacks or sacks" }
  ]
}
```

- **One result set per table.** With several, the chooser beside **Export…** says which, and the Foundry table is named
  `Table — Result Set`.
- **Formula:** `d20` becomes `1d20`, `2d6+1` stays `2d6+1`, and a `d66` becomes `1d6 * 10 + 1d6`, which is exactly tens and
  ones (11–66, never 17 or 20). An explicit `1d66` stays a 66-sided die.
- **Ranges are numbers:** a written `96–00` is `[96, 100]` and `08` is `[8, 8]`. Gaps and overlaps are exported as they are.
  A result set with an open-ended row (`26+`, `1 or less`) is refused, since a Foundry range needs two numbers.
- **Text** is the result as written (trimmed), with line breaks, Unicode and inline dice like `1d6` left as plain text.
  Links, inline-roll results, the modifier, Clamp and roll history are never exported, and the table's description is not
  included.
- **Clamp:** Foundry does not clamp. If this table clamps and its dice can roll past its rows (`d20+2` over rows 1–20), a
  note says so after exporting; the export still happens.
- **Save Foundry JSON…** suggests a file name made from the table's name, with characters Windows does not allow replaced by `_`.

## Owlbear Rodeo (Tables+) export

On the Roll screen, **Export…** → **Copy Tables+ JSON** or **Save Tables+ JSON…** turns one result set of a saved table into
one table in the JSON shape that the Tables+ extension for Owlbear Rodeo documents for importing. Copy and Save give
exactly the same JSON:

```json
{
  "name": "Wilderness Encounters",
  "type": "weighted",
  "dice": "1d20",
  "entries": [
    { "low": 1, "high": 3, "text": "Wolf pack" }
  ]
}
```

- **Checked in Tables+ so far (2026-09-27):** TableForge-exported `d20`, `d66` (`T66`, in the sample tested) and `2d6`
  tables imported and rolled correctly. A separate hand-written `1d4+20` Tables+ table always rolled 21–24, so Tables+
  applies a positive modifier on one die; a Gidubba export with a modifier was not itself tested.
- **Not yet checked in Tables+:** negative modifiers and negative ranges, gaps and overlaps, `{2d6}` and `#reroll` in
  results, a real `1d66`, `d100`, and importing a file saved with **Save Tables+ JSON…**. This is not a claim of full
  Tables+ compatibility; please report what you find (the checklist is in `docs/RELEASE_TESTING.md`).
- **One result set per table,** named like the Foundry export (`Table — Result Set` when there are several).
- **Type and dice:** one die is `weighted` (`1d20`, `1d100`, `1d20+2`, `1d20-2`; a real 66-sided die is `1d66`); a `d66`
  (tens and ones) is `weighted` with `T66`; several dice with no modifier are `bell-curve` (`2d6`, `3d6`). Several dice
  with a modifier, like `2d6+1`, are refused until Tables+ is known to handle them; the modifier is never dropped.
- **Ranges are numbers:** `96–00` is 96 to 100, negative numbers stay negative, and gaps and overlaps are exported as
  they are (the Review screen already points them out). A result set with an open-ended row (`26+`, `1 or less`) is refused.
- **Text** is the result as written (trimmed), with line breaks, Unicode and inline dice kept. Text that means something
  special when typed into Tables+, such as `{2d6}` or `#reroll`, is exported unchanged; whether Tables+ acts on it after
  an import has not been checked. Links, ids, the table's description, roll state and Clamp are never exported.
- **Clamp:** Tables+ JSON has no Clamp setting. If this table clamps and its dice can roll past its rows, a note says so
  after exporting; the export still happens.

## PUM Companion export

**Export to PUM Companion…** (under Export Collection / Delete Collection) saves the selected collection as one `.pumb`
bundle for PUM Companion. On the Roll screen, **Export…** → **Save PUM Companion Bundle…** saves just the table shown,
with every result set (the result-set chooser beside **Export…** is for the other formats). In PUM Companion, import the
file with PUM's own import.

**The review comes first.** Before anything is saved, a window shows:
- how many tables will be exported and how many skipped;
- each skipped table with its reason, and any warnings;
- the full report, which **Copy Report** copies.

**Save Bundle…** then asks where to save. Cancelling at either step saves nothing. The file is written completely or not
at all: if saving fails, an existing file with that name is left as it was. A single table that can't be exported says
why next to **Export…**, with no review.

**What a roll shows is kept.** For every roll of a table's own dice, an exported table shows in PUM what it shows in
Gidubba. Gidubba checks each table before exporting it, and skips any table it can't reproduce exactly. The report
gives the reason and an example roll.

**How tables are written for PUM:**
- **No Match:** where no row covers a roll (a gap, or the low rolls of `d6-2`), the PUM table has a `(No result)` row,
  and its last row catches any higher roll, since PUM otherwise shows its last row. **Clamp to Range** is kept: PUM
  clamps the same way.
- **Rolls below 1:** PUM looks up every roll below 1 as 1. A table whose dice can roll below 1 is exported only if that
  changes nothing. Rows that only the situational modifier could reach below 1 are left out, with a warning.
- **Several result sets** become one PUM table. Each result lists every set (`Weather: Rain` and `Wind: Calm` on two
  lines), so one roll still gives all of them.
- **Dice:** `1d6`, `2d6+1` and `1d20-2` are written as they are. A `d66` (tens and ones) becomes PUM's `1D66`, and a real
  66-sided die stays `1d66`. `96-00` on a `d100` and open-ended rows such as `26+` work.
- **Links** become a line under the result, `→ Table name`, with "(not included in this export)" when that table isn't in
  the bundle, or "(unresolved link)". PUM doesn't open or roll the linked table, unless you tick **Roll linked tables
  automatically in PUM** (see below).
- **Text:** bold and italic become PUM's Markdown (`**bold**`, `*italic*`). Inline dice such as `1d6` stay as text.
- **Text that PUM would run as a command** (`{{Table}}`, `;;2d6;;`, `::result::`) is never written: such a table is skipped.
  Other PUM-like text, such as `%%`, is written as it is and stays plain text in PUM.
- **Blank results:** a row with no text exports blank, with a warning (Remove Empty Rows on the Review screen removes
  such rows). A table whose every result is blank is skipped.
- **Overlapping rows** can't be shown in PUM, so such a table is skipped.
- **Names and categories:**
  - Characters PUM can't store are replaced (`/` becomes ` - `), and repeated names get ` (2)`, ` (3)`, and so on.
  - Each table's PUM category is the collection's name, or `Collection - Folder` for a table in a folder.
  - Exporting the same table again (same collection, folder and name) gives the same PUM table.
- **Table descriptions** are not exported, as with the other exports.

**Roll linked tables automatically in PUM ({{…}}).** A Collection export's review has this checkbox. It is **unticked
every time** the review opens, and unticked the bundle is exactly what earlier versions saved. Ticked, the review and
report update at once:

- A link to a table in the same bundle becomes a PUM nested roll: under the result, `→ Exceptional Treasure Table:`
  followed by the result PUM rolls on that table. The result's own text is kept, so "roll twice on the Mundane Treasure
  Table" still says so, and PUM rolls the linked table once.
- A link stays a `→ Table name` text line when PUM can't roll it safely, and the report says why for each one:
  - it goes to, or comes from, a **Manual-selection table** (PUM can only roll a table, not let you choose its row);
  - it leads back round to its own table (PUM would keep rolling round the loop);
  - **another of your collections has a table with the same name.** PUM finds tables by name alone, in every category
    and ignoring case, so in PUM they would be one table. The review shows this as a warning too;
  - the table isn't in the bundle, or its name contains characters PUM uses for commands (`{{`, `}}`, `%%`, `;;`).
- A line says how many links will roll, for example "55 of 131 links will roll their table in PUM. 76 stay text (see
  the report)."
- Gidubba can't see tables made in PUM itself or imported from elsewhere. If one has the same name as a table in your
  bundle, PUM replaces its rows when you import.

A single table's export (Roll screen → **Export…**) has no such option.

**Importing a bundle again:** PUM Companion offers to overwrite the tables it already has, and overwriting replaces them,
including any changes you made to them in PUM. The review says so whenever there is a bundle to save.

**Rolls beyond the dice:** PUM's own roll overrides, and Gidubba's situational modifier, can reach values outside a
table's dice. There PUM uses its first row for anything below 1 and its last row for anything above it. Where that
differs from Gidubba, the report notes it.

**Checked in PUM Companion (2026-10-23):**
- imports of TableForge 1.1.0-made bundles (the export is unchanged in Gidubba), of a single table and of whole Collections;
- rolls with gaps, Clamp to Range and modifiers;
- `d66`, a real `1d66`, `d100` with `00`, and open-ended rows;
- several result sets, links, bold and italic, and empty rows;
- special and repeated names, categories and folders;
- importing the same bundle again;
- nested rolls (2026-10-26, Windows and iPad): Under the Fourth Kingdom, names that differ only in case, em dashes, ` (2)`
  names and five levels of nesting.

Rows beyond the dice, which only PUM's overrides reach, have not been checked there.

## Several rolls at once

Beside **Roll** is **Rolls:** (1 by default). Choose 2 to 10 and the button reads **Roll 3 Times**: one press makes that many
independent rolls of the current table, one after another (with dddice each set of dice settles and its result appears before
the next throw). Every result is kept and shown separately, headed **Roll 1**, **Roll 2**…, each with its own links and inline
dice buttons; nothing is combined, counted or interpreted, and table text such as "roll 3 times" is never read as a count.

- Each roll is a normal roll: the table's own modifier (`2d6+1`) and Clamp apply to every one, and each is its own entry in
  **Recent rolls**.
- The **Modifier** box is used by the first roll that succeeds, then resets to 0 as usual; it is not added to every roll.
- If a roll fails or is cancelled, the ones already made stay (and stay recorded); the rest are not rolled, and Gidubba says
  where it stopped.
- A roll you type yourself is always one result. The choice goes back to 1 when you follow a link or open another table, and is
  not remembered after Gidubba closes. It is locked while dice are rolling.

## Situational modifier

Beside **Roll** is a small **Modifier** box (`0` by default). Type one whole number from `-1000` to `+1000` (`3`, `+3`,
`-2`; blank means 0) and the next **Roll** adds it to what the dice produced: a d20 that rolls 11 with `+3` is looked up as
**14**, shown as "Rolled 14" with "11 +3 situational" underneath. The box then goes back to 0. It is for one roll at a
time; nothing about it is saved with the table, and the table's own dice (`2d6+1`) are never changed by it.

- The sum is used exactly as calculated, even outside the table: a d6 that rolls 1 with `-1` is **0**, which usually
  shows "No entry covers 0." Gidubba never rerolls or picks the nearest row, and clamps only a table you have
  explicitly set to (see **Clamp to table range** below). That roll still uses the modifier up. When the table could
  clamp, a note under **Roll** says so: "The modifier took the roll below this table's lowest row. To use the nearest
  row instead, turn on Clamp in Edit table." (The result itself, Recent rolls and the overlay are unchanged.)
- Anything that is not one whole number in range (`+`, `2.5`, `1d4`, `+1001`) turns Roll off and says why; it is never
  read as 0. **Enter** in the box rolls. Moving into the box (**Tab**, **Shift+Tab** or a click) selects its value, and so
  does the reset to 0 after a roll, so what you type next replaces the 0.
- If the dice cannot finish (a dddice roll is cancelled or fails), the modifier stays for the retry. dddice still shows
  the real die (11); Gidubba adds the modifier once the dice settle. The box is locked while dice are in the air.
- A roll you type yourself is already final, so it ignores the modifier and leaves it waiting. Inline dice rolls ignore
  it too.
- Opening a different table, or following a link, starts the box at 0 again.
- A d66 table has no Modifier box: arithmetic does not respect its tens-and-ones results (35 + 2 is not a d66 result).
- **Recent rolls** keeps the final number and the modifier used, e.g. `14 (+3)`; hover for "d20 → 14 (11 +3 situational)".

## Clamp to table range

A table can opt in to **Clamp out-of-range rolls to table range** (a checkbox under the name and dice on Review / Edit
table; off for every table unless you turn it on). Then a roll that lands below the table's lowest row uses that lowest
value, and one above its highest row uses the highest value. The range is the one the rows actually cover (a d20 table
whose rows run 5–15 clamps 3 to 5 and 18 to 15), not the dice's theoretical range. An open-ended side (`26+`) never
clamps: every roll past it already lands on that row.

- The calculated number is never hidden: a d6 rolling 6 with `+2` shows "Rolled 8", "6 +2 situational", then
  "Resolved as 6 (clamped)" and row 6's result. Stored modifiers count the same way (`d6+1` rolling 7 on rows 1–6 uses 6).
- Only values outside the outer boundaries move. A gap inside the table (rows 1–3 and 5–6, roll 4) is still "No entry
  covers 4.", and overlapping rows are still reported as ambiguous.
- A roll you type yourself is never clamped, and neither are inline dice. A followed link uses the destination
  table's own setting.
- Every result set must share the same lowest and highest value, so one roll clamps to one number for all of them;
  otherwise the checkbox is unavailable and says why. It is never available for d66. Editing a clamped table into one of
  those shapes turns Clamp off when you save (the checkbox shows that before you do).
- **Recent rolls** keeps both numbers, e.g. `8 (+2) → 6 (clamped)`, and the result of the row actually used.
- The Roll screen shows the current table's options in a small grey line under **Roll**: "Clamp: On", "Clamp: Off" or
  "Clamp: Not available" (d66, result sets with different ranges, or no rows; hover for why), and "Manual selection:
  On" or "Off". The line is read-only; change these options in **Edit table**.

## Inline dice in results

When a result's own text contains a dice expression Gidubba already knows how to roll — `You gain +1d4 Armor`,
`Encounter 2d6 Skeletons`, `Gain 2d6+1 supplies`, `Consult entry d66` — a **Roll** button for that expression appears
beneath it once the result is showing. Pressing it rolls through whichever dice provider is currently selected (Built-in
or dddice, with the same visible dice and the same wait for dddice to settle) and shows the result **resolved in
context** on a smaller line under the original text:

```
You gain +1d4 Armor
Resolved: You gain +3 Armor
[Roll Again]
```

Only the dice expression itself is replaced, by its final number (dice plus the expression's own modifier: `2d6+1` → `9`);
everything around it — a leading `+`, commas, full stops, the words — is kept exactly as written. Gidubba does not
fix grammar (`Encounter 1 Skeletons` is shown as is). **Roll Again** replaces the number; it does not keep a history.
The original text is never rewritten, in the table or anywhere else.

A result with more than one supported expression (`Gain 1d6 food and 1d4 water`) offers one button per distinct
expression (`Roll d6`, `Roll d4`, then `Roll d6 Again` / `Roll d4 Again`); an expression not rolled yet stays as written
in the resolved line (`Resolved: Gain 4 food and 1d4 water`). The same expression repeated in one result (`Gain 1d6 gold
and lose 1d6 reputation`) gets only one button, and its result fills every occurrence (`Gain 4 gold and lose 4
reputation`). Text such as `UD6` or `4d6kh3` that only looks like dice notation is left alone: Gidubba never guesses
at a second, more permissive dice grammar, only the same one a table's own dice already use.

These rolls are auxiliary: they never appear in Recent Rolls, ignore the Modifier box (and leave it as it is), and never
re-resolve the table or follow a linked result — a result that both contains dice text and links to another table offers
both actions independently. A roll that fails or is cancelled leaves the previous resolved line unchanged. Inline results
are transient: rolling the parent table again, following a link, or opening another table clears them; nothing is saved.

## Dice: Built-in or dddice

The **Dice** choice near the top of the left pane (just under **Paste Table…**) picks how tables are rolled:

- **Built-in** (the default): rolls instantly, works offline, needs nothing.
- **dddice** (3D dice, needs internet): press Roll, watch the dice tumble in a panel above the table, and the table result
  appears when they settle. No dddice account is needed: Gidubba uses dddice's anonymous *guest* access (free
  `dddice-bees` dice). Choosing dddice shows "Preparing dddice…" and enables Roll once the dice are ready (a few seconds).
  A guest identity is created once per Gidubba session and is never saved.

dddice rolls only the base dice (`2d6+1` shows a 2d6); Gidubba adds the modifier itself, so the result is the same kind of
number Built-in produces. It shows d4, d6, d8, d10, d12, d20 and d100 (a tens die plus a ones die), in any count. A table with
other dice (a d3, d7…) says so and can be rolled with Built-in. Typing a roll yourself never contacts dddice.

If dddice cannot start or a roll fails (no internet, dddice down or rate-limiting new guests, script or connection failure, a roll
that does not finish in 30 seconds), Gidubba says why and offers **Use Built-in Dice** (and **Try again**). It never rerolls
with Built-in by itself, and Recent Rolls only ever records results the dice really produced. If dddice was your choice last
time and cannot start, Gidubba opens normally and offers Built-in. Keep the Gidubba window open (not minimised) while
the dice roll.

**What dddice sees:** the kind of dice rolled (for example d20, or d10 with a tens die), the anonymous guest identity, a room
named "Gidubba" (a room made by TableForge keeps the name "TableForge"), the rolls made in it, and normal web traffic (its script and dice models from dddice's CDN, and a
WebSocket). Gidubba never sends table names, table text, results or collection names. Nothing dddice-related is contacted or
created unless you choose dddice.

**Requirements for dddice:** internet, and the free *Microsoft Edge WebView2 Runtime* (Evergreen). It is preinstalled on Windows 11
and on current Windows 10 with Microsoft Edge; if it is missing Gidubba says so and Built-in still works. The publish folder adds only
`Microsoft.Web.WebView2.*.dll`, `WebView2Loader.dll` and `Assets\dddice-host.html` (about 2 MB in all); no runtime is bundled.

### Connecting a dddice account (optional)

Guest dice need no account and stay the default. To roll with a theme from your own **Digital Dice Box**, press **Account…**
under the Dice choice (the sidebar then shows `dddice: <your name>` and `Theme: <theme>` instead of `dddice: Guest`):

1. **Connect** shows a short code. Press **Open dddice** (it opens `dddice.com/activate` in your own browser), sign in there if
   asked, and enter the code. **Copy** copies just the code. Gidubba never asks for, sees or stores your dddice password.
2. Gidubba notices the approval by itself (it checks every 5 seconds; **Cancel** stops, and the code expires after about
   5 minutes: "Connection timed out. You can try again."). Nothing is saved until you approve.
3. Your Dice Box is listed. A theme can be chosen only if it has every standard die Gidubba rolls: d4, d6, d8, d10, the
   percentile d10x, d12 and d20, all with ordinary numbered faces. Other themes stay listed, greyed out with the reason
   ("Missing d12, d20", "No d10x (percentile)", "Non-standard d6 faces"). **Refresh** reloads the list.

The connection and theme are remembered across restarts, and one dddice room is created for the account and reused. The account
is checked with dddice only when dddice is actually used. If dddice refuses it, Gidubba says "dddice connection expired.
Reconnect in Account…" and keeps it until you reconnect or disconnect; if dddice is busy or unreachable it says so and keeps
everything. If your chosen theme leaves your Dice Box or can no longer be used, Gidubba says so and asks you to choose
another. It never switches to the guest dice or another theme by itself; **Use Built-in Dice** is always there.
**Disconnect** forgets the connection on this computer only (your dddice account is not changed) and dddice rolls as a guest again.
It deletes the login (the token) and the theme choice, but keeps the account's room id and dddice account id, which are not
secret, in `dddice-rooms.json`: connecting the same account again, even after a restart, reuses its room. A room is only ever
reused for the account it belongs to, and only after dddice confirms it can still be used; a different account gets its own. If
dddice will not create a room (for example "dddice would not create a room for Gidubba (402)", which may mean the account has
reached its room limit), Gidubba says so and does not try again until you press **Try again**.

Not included yet: custom dice, other dice apps. To show dddice's dice in OBS, see [Streaming Overlay](#streaming-overlay-obs).

## Streaming Overlay (OBS)

The Streaming Overlay shows the latest Gidubba result (table name, roll value and result text) in OBS as its own Browser
Source, on a transparent background; OBS decides where it goes and how big it is. It works the same with Built-in dice, dddice
and rolls you type yourself, and it needs no dddice account.

1. Choose **Streaming Overlay…** at the bottom of the Gidubba window and tick **Enable streaming overlay**. The status line
   says "Running on port 41285."
2. Beside **OBS Browser Source, "Local file" (recommended)** choose **Copy**: it copies the path of `streaming-overlay.html`
   in your data folder (normally `%LOCALAPPDATA%\TableForge\streaming-overlay.html`).
3. In OBS add a **Browser** source, tick **Local file**, choose that file, and place it in your scene.
4. **Show Test Result** shows "Gidubba Overlay Test / 12 — Your streaming overlay is working." so you can place it without
   rolling; **Clear Overlay** empties it. Neither is a roll, and neither appears in Recent rolls.

Loaded from the local file, the overlay starts showing results by itself whenever Gidubba is running, even if OBS was
opened first. The dialog also gives a URL (`http://127.0.0.1:41285/`) for a URL Browser Source; it shows the same thing, but if
OBS was started before Gidubba you must refresh that source in OBS (Properties → **Refresh cache of current page**).

What it shows:

- The latest result replaces the one before and stays until another result replaces it or you choose **Clear Overlay**.
  Opening other tables, following a link, editing, importing and exporting never change it; rolling the linked table does.
- **Show table name** and **Show roll value** (both on by default) change the overlay straight away. The result text is
  always shown, with its bold and italic; each result set is its own line.
- The roll value is the final number: a 3 with a situational modifier of `+2` shows **5** (the arithmetic stays on the Roll
  screen). With Clamp it shows the number actually rolled, while the result is the boundary row's.
- A roll no row covers shows Gidubba's "No entry covers N." in place of the previous result.
- Rolling inline dice in the current result ("2d6 Skeletons") updates that result in place ("7 Skeletons"). An inline roll in
  an older result (an earlier roll of several) does not change the overlay.
- When Gidubba closes, the overlay goes blank within a few seconds. A new Gidubba session starts with it empty.

**dddice's 3D dice in OBS.** dddice draws its own dice; Gidubba does not capture or relay them. To show them too, add
dddice's own stream as a **second, separate** Browser Source:

1. Connect your dddice account (**Account…** under Dice) and choose a theme.
2. In **Streaming Overlay…** choose **Open my dddice room**: it opens Gidubba's room on dddice.com in your browser.
3. In dddice open **Streaming tools** and copy the **3D Dice** link.
4. In OBS add that link as its own Browser Source (a URL, not a local file). Keep it separate from the Gidubba source.

How the dice look, including their size, is set in dddice (the room's settings), not in Gidubba. Gidubba never sees,
stores or needs that streaming link. If Gidubba says "dddice room changed. Your OBS dice source may need to be updated.",
dddice has given your account a new room: open it again with **Open my dddice room** and replace the dice source's link in
OBS. Guest dddice dice cannot be shown in OBS this way.

**Port and troubleshooting.** The overlay listens only on `127.0.0.1` (this computer), on port **41285** unless you change
it, and only while it is enabled. If the status says the port is already in use, another program (or another copy of
Gidubba) has it: type another port from 1024 to 65535 and choose **Apply**. Gidubba never picks a different port by
itself. After changing the port, refresh the Browser Source in OBS once. To restyle the overlay, OBS's own **Custom CSS** can
target its `tf-…` classes (for example `tf-table-name`, `tf-roll-value`, `tf-result-text`); Gidubba has no appearance
settings of its own.

## What Paste Table understands (copied-PDF text)

- **Rows on one line:** `1-2 Backpack`, `3 Knife`, `01-30 Cold stale air`.
- **A number or range on its own line, followed by a paragraph:** the paragraph (wrapped lines are joined, blank lines
  before it are ignored) becomes that row's result, until the next number or range starts. A second paragraph after a
  blank line is kept but flagged (a page header or footer may have slipped in).
- **Result set headings:** `AMBIENT`, `NOISE`, … followed by rows start separate result sets.
- **Continuation columns** (one table printed in two or more page columns, `01-02 Ael 51-52 Wulf`): joined into one result
  set in numeric order, even when extraction squeezed the columns down to single spaces, provided the whole block shows each
  column continuing exactly where the one before it ends. Spans may be written `01-02` or `01 - 02`. Single values
  (`1 Ael 11 Wulf`) are only read as columns with extra proof — every value followed by the same separator (`1 – Ael`,
  `1. Ael`), or the heading printed once per column (`D20 RESULT D20 RESULT`) — and the columns must then cover the whole
  dice range exactly once. A d66 table steps through legal d66 values only. A line that looks like side-by-side columns
  without that proof is kept as one entry and flagged.
- **Parallel output columns** (`D8 DIFFICULTY MODIFIER` with rows such as `1 Child's play +30`): each output becomes its own
  result set sharing the row's roll, when every row splits the same way (by column gaps, or a consistent trailing `+30`/`-10`/`2d6`
  field matching two heading words). When the heading has more words than columns (`D100 WIND TYPE STRENGTH HULL DAMAGE CAUSED`),
  rows still split if two or more trailing columns each hold one recognisable shape all the way down (signed numbers, dice, or
  plain numbers beside one of those; a lone `–` may stand for "none"). The result sets are then left unnamed — name each one in
  the box above its column on the Review screen.
- **Repeated headings** (`D100 SYLLABLE D100 SYLLABLE`) are read as one heading.

Anything the parser is not sure about is kept together and flagged on the Review screen: information (blue) for
things it did and you may want to glance at, warnings (amber) for things to check.

## Bold and italic in results

On the Review / Edit screen, select some words in a row's result text and choose **B** (**Ctrl+B**) or **I** (**Ctrl+I**)
to make them bold or italic; both together are fine. The same command again takes it off, and **Clear Formatting**
(**Ctrl+Space**) removes both from the selection. The buttons never take the cursor away, so the words stay selected. The
text box itself stays plain; a row with formatting shows a small preview underneath of how it will look when rolled.

The Roll screen shows the formatting in the result, in the "Resolved:" line of an inline dice roll (a rolled value takes
the formatting of the expression it replaces: **+1d4 Armor** resolves to **+3 Armor**), in the table's rows, and on
linked tables. Formatting follows its words as you edit: typing inside a bold word stays bold, typing just before or
after it does not, and Join With Previous Row, Dehyphenate, Normalize Text, Undo Last Cleanup and saving all keep it on
the same words.

Formatting is presentation only. It never changes how a table rolls, what inline dice are found, links or search, and it
is kept beside the text rather than inside it, so nothing like `**` or `<b>` ever appears in your text. Everything that
copies or exports text — Copy Table Text (and Spaces), Copy for Sojour, Foundry and Tables+ JSON — and Recent Rolls get
exactly the plain text, as before. Only result text can be formatted (not table or result-set names). Pasting keeps
text only: bold or italic in a copied PDF or web page is not brought in.

## Cleaning up imperfect PDF copies (Review screen)

A small toolbar above the rows offers deterministic, reversible fixes for the kind of damage a PDF copy leaves behind,
so correcting it takes a click (or **Ctrl+J**) instead of retyping:

- **Join With Previous Row** (**Ctrl+J**): joins the selected row into the row above it in the same result set — the
  shape a wrapped line leaves when the parser could not safely join it on its own. Refuses, with a message, if the
  selected row has its own range (it is very likely a real row) or is the first row of its set.
- **Dehyphenate Selected Row**: removes a PDF line-wrap hyphen from the selected row's result text
  (`magnifi- cent` → `magnificent`). Ordinary hyphenated words (`well-made`, `half-orc`) are never touched — a
  line-wrap hyphen always has whitespace after it, a real one never does.
- **Remove Empty Rows**: removes rows in the current result set with no range and no result text (whitespace-only
  counts as empty). A row with either one is left alone.
- **Normalize Text**: fixes non-breaking spaces, repeated whitespace and the `fi`/`fl` ligatures across the whole
  table, and tidies a range field's separator (`1 – 2`, `1 − 2` → `1-2`). It never touches punctuation or wording in
  result prose, and never alters dice text embedded in a row (`4D6+5` stays `4D6+5`).
- **Undo Last Cleanup**: reverses the single most recent cleanup action above. Only one level deep — Review has no
  general undo/redo history, so this is the smallest safe net rather than a full command stack.

A Unicode replacement character (`�`) left by a broken PDF font mapping is flagged on its row automatically, as a
warning to review by hand; Gidubba never guesses what the missing character was or silently changes it.

## Cleaning up imperfect PDF copies (Paste Table screen)

Some malformed continuation lines never reach Review at all — the parser reads them as something else before you get
there. A small set of raw-text tools sits above the paste box for exactly that, so obvious clipboard damage can be
repaired before Interpret runs, not only after:

- **Ctrl+J**: joins the line the caret is on into the line above it — trims trailing whitespace off the line above,
  leading whitespace off the current line, and joins them with exactly one space. Makes no judgement about whether the
  current line is really a continuation; it is a raw-text edit you asked for, on the line you put the caret on. Does
  nothing (with a status message) if the caret is on the first line.
- **Ctrl+J with several lines selected** (or **Join Lines**): joins every line the selection touches into one line in a
  single step — each line trimmed, blank lines dropped, the rest joined with exactly one space. It works on whole lines:
  if the selection starts or ends partway through a line, that entire line is joined, so nothing unselected on it is
  lost. A selection ending at the very start of a line (having taken only the line break before it) leaves that line
  alone. The join is literal: `well-` + `made` becomes `well- made` (use Dehyphenate for that), and `�` is never
  changed. One **Ctrl+Z** undoes the whole join. With no selection, or a selection within one line, Ctrl+J and Join
  Lines do the single-line join above.
- **Normalize Text**: the same non-breaking-space, repeated-whitespace and `fi`/`fl` ligature fixes as Review's
  Normalize Text, applied one line at a time so every line break is kept exactly as it was.
- **Dehyphenate**: removes PDF line-wrap hyphens from the raw text (`magnifi-` + a line break + `cent` becomes
  `magnificent`), including across the line's own line break. Ordinary hyphenated words (`well-made`, `half-orc`) are
  never touched.
- **Find Next Damaged Character**: only shown while the pasted text contains `�`; moves the selection to the next one
  (wrapping back to the first once it reaches the end).

All three edits go through the paste box's own text editing, so **Ctrl+Z** undoes them exactly as it would undo typing.
A warning above the box reports how many `�` characters are still in the text; as on Review, Gidubba never guesses
what they should be or changes them for you.


---

# For developers

## Building and testing (developers)

Requires the .NET 10 SDK on Windows.

```
dotnet build
dotnet test
```

The test suite uses only temporary SQLite files and never touches the real application data.

## Producing the release (self-contained Windows x64)

From the repository root:

```
.\publish.ps1
```

This is the one supported way to cut a release build. It runs the full test suite in Release configuration first
(retrying once on failure, since the UI tests drive real WPF windows and are occasionally timing-flaky, but two
failures in a row stop the script), then **deletes and recreates** `publish\win-x64\` from scratch — it never
layers a new publish over old files — and finally checks that the published exe's own file version matches the
`<Version>` in `TableForge.csproj`, so a stale or partial publish is caught immediately instead of being shipped.
Pass `-SkipTests` only for iterating on the script itself; never to produce a build you intend to ship.

Output folder: `publish\win-x64\` (about 140 MB, roughly 400 files). Run `publish\win-x64\TableForge.exe`. The target machine
does **not** need .NET installed. The folder never contains debug symbols (`.pdb`): the publish profile turns them off and the
script refuses to finish if any appear.

The publish settings live in `TableForge\Properties\PublishProfiles\win-x64-folder.pubxml`: self-contained, `win-x64`, no
single-file, no trimming, no ReadyToRun. There is no auto-update in V1.

### The installer and release artifacts

```
.\publish.ps1 -Installer
```

does everything above and then builds the public release artifacts in `publish\release\`: the installer
(`installer\TableForge.iss`, Inno Setup 6; install it once with `winget install JRSoftware.InnoSetup`),
`Gidubba-<version>-Setup.exe`, the same files as a portable `Gidubba-<version>-win-x64.zip`, and `SHA256SUMS.txt`. The script's `AppId` must never
change: it is how a newer installer recognises and replaces an installed Gidubba (or TableForge). The installer is the one way Gidubba
is meant to be installed; the zip is for people who prefer to run it from a folder (it uses the same data folder).

Equivalent manual command, if you need it (`publish.ps1` just wraps this with the test gate and a clean folder):

```
dotnet publish TableForge\TableForge.csproj -p:PublishProfile=win-x64-folder
```

### Confirming the running exe matches current source

The bottom-right of the main window shows the running build's version and the exact time its main assembly was
written to disk, e.g. `1.5.0 · built 2026-11-02 12:45 local` (`TableForge/AppInfo.cs`). The build time comes
from the DLL's own file timestamp, not a manually-maintained field, so it stays accurate without anyone
remembering to bump it. To check a specific exe from the command line instead:

```
(Get-Item publish\win-x64\TableForge.exe).VersionInfo.ProductVersion
(Get-Item publish\win-x64\TableForge.dll).LastWriteTime
```

If the version shown doesn't match `TableForge.csproj`'s `<Version>`, or the build time is older than your last
accepted change, the running exe is stale — run `.\publish.ps1` again.

## Developer tools

**Separate data folder.** Set the environment variable `TABLEFORGE_DATA_DIR` to a folder to make Gidubba keep *all* of its data
(database, dice choice, WebView2 profile) there instead of in `%LOCALAPPDATA%\TableForge`. It is meant for testing a build without
touching your real tables; leave it unset normally.

**Clipboard diagnostic.** To see which formats a program puts on the clipboard when you copy from it, and whether bold or
italic survives, copy some text in that program (for example from a PDF in PDF-XChange Editor), then run:

```
TableForge.exe --clipboard-diagnostic
```

A window lists every clipboard format present (and which program owns it), what Paste Table would receive (always plain
text), whether bold/italic exists in HTML or RTF, and whether the copy carries more line/column structure than plain text.
For a report file instead of a window: `TableForge.exe --clipboard-diagnostic --out report.txt`. It only reads the
clipboard and never opens your tables.
