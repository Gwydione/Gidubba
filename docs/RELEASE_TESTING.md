# Gidubba release testing checklist

For testing a packaged release (`Gidubba-<version>-Setup.exe`) on a Windows 10/11 x64 machine, ideally one
that has never had Gidubba (or TableForge) on it. Tick each item; note anything unexpected with the Gidubba version (About, or the
bottom right of the window) and your Windows version.

Your data lives in `%LOCALAPPDATA%\TableForge`. Nothing here should ever delete it unless a step says so.

## Fresh install

- [ ] The download matches `SHA256SUMS.txt` (`Get-FileHash .\Gidubba-<version>-Setup.exe`).
- [ ] The installer runs (SmartScreen may warn: More info → Run anyway) and asks for **no administrator permission**.
- [ ] **Gidubba** is in the Start Menu, with the Gidubba icon, and starts from there.
- [ ] First start shows the welcome text; the window, taskbar and About show the Gidubba icon.
- [ ] Create a Collection.
- [ ] Paste a table (copied from a PDF or web page), Interpret, correct a row, Save Table.
- [ ] Roll it with **Built-in** dice.
- [ ] Copy Table Text, paste into Notepad: name, dice, blank line, one row per line.
- [ ] Roll screen → **Export…** → **Copy Table Text (Spaces)** (directly below Copy Table Text), paste into Notepad: the
      same text, with one space instead of the tab between each range and its result.
- [ ] Close and restart Gidubba: the collection and table are still there.
- [ ] About shows the right version and RPG Frequencies; License, Privacy and Open Data Folder open.

## Extended and open-ended ranges

Paste this `d20` table (Paste Table → Interpret):

```
D20 Injury
-10-0 The character is dead.
1-5 Succumbs to wounds.
6-10 Severely wounded.
11-15 A week of rest.
16-20 In shock, but alive.
21-25 Knocked out.
26+ Recovers immediately.
```

- [ ] Review shows seven rows with the ranges as written (`-10–0`, `26+`), no warnings, and information notes for the
      `-10–0`, `21–25` and `26+` rows ("outside the natural d20 range").
- [ ] Save, then roll with a situational modifier of `-10` until a roll resolves to "The character is dead."; and with
      `+10` until one resolves to "Recovers immediately." The Roll screen and Recent rolls never show a number like
      2147483647 or -2147483648.
- [ ] Type a roll of `-5` and of `40` by hand: both are accepted and find the right rows.
- [ ] **Export…** → **Copy Table Text** and **Copy for Sojour**: the ranges read `-10-0` and `26+`.
- [ ] **Export…** → **Copy Foundry JSON** and **Copy Tables+ JSON**: both refuse, naming the `26+` range. Change `26+`
      to `26-30` on Review / Edit, save, and both export.
- [ ] A `d66` table with a `61+` row: Review says open-ended ranges are not available for d66, and it cannot be saved.

## Bold and italic result text

Paste this `d6` table (Paste Table → Interpret):

```
D6 Boons
1-2 The creature gains +2 Armor until the next dawn.
3-4 You gain +1d4 Armor until next dawn.
5-6 Nothing happens.
```

- [ ] Row 1: select `+2 Armor` and press **Ctrl+B**; select `next dawn` and click **I**. The words stay selected after
      the click, the text box still shows plain text, and a preview under the row shows **+2 Armor** and *next dawn*.
      Row 3 has no preview.
- [ ] Row 2: select `+1d4 Armor` and apply both **B** and **I**. Select the same words and press **Ctrl+Space**: the
      formatting goes; apply bold and italic again.
- [ ] Type inside the bold words (`+2 Heavy Armor`): the new word is bold. Type an `s` right after `Armor`: it is not.
- [ ] **Normalize Text** and **Undo Last Cleanup** leave the formatting on the same words.
- [ ] Save, roll `1` (type it): the result shows **+2 Armor** and *next dawn*; the table's rows show it too.
- [ ] Roll `3`, then **Roll d4**: `Resolved:` shows the number bold and italic, with *next dawn* italic.
- [ ] Recent rolls, **Copy Table Text**, **Copy Table Text (Spaces)**, **Copy for Sojour**, **Copy Foundry JSON** and
      **Copy Tables+ JSON** (paste each into Notepad): plain text only, with no `*`, `<b>` or other marks.
- [ ] Edit the table again: the formatting is still there. Close and restart Gidubba: still there.
- [ ] Paste a two-column table such as `D3 Difficulty` / `1 Easy +10` / `2 Normal +0` / `3 Hard -10`, Interpret, and type
      several characters into one of the aligned cells, then into a range (add a space after `2`): the cursor stays
      where you are typing the whole time.

## Table description

Use a table that links to another table (or make one: a row whose text names another table, linked on Review / Edit).

- [ ] **Edit table**: a **Description (optional)** box sits between the name / dice / folder row and the Clamp checkbox,
      about three lines high. Type two lines (**Enter** makes a new line, it does not save) and **Save Table**.
- [ ] The Roll screen shows both lines under the table's dice, in plain grey text with no label.
- [ ] A table without a description shows nothing there: no empty line or gap before "Not rolled yet."
- [ ] Give the linked-to table its own description. Roll (or type) the linking row and open the link: each step in the
      trail shows only its own table's description.
- [ ] The table list and Recent rolls (and its tooltip) never show description text.
- [ ] Paste more than 2,000 characters into the box: only the first 2,000 are kept. Save: the long text wraps under the
      dice and **Roll** stays usable.
- [ ] **Export…** → **Copy Table Text**, **Copy Table Text (Spaces)**, **Copy for Sojour**, **Copy Foundry JSON** and
      **Copy Tables+ JSON** (paste each into Notepad): none contains the description.
- [ ] Close and restart Gidubba: the descriptions are still there.

## Situational modifier entry

On a `d20` table, with the **Modifier** box showing `0`:

- [ ] Open the table (focus is on **Roll**), press **Tab**: the `0` in the Modifier box is selected. Type `+2`: the box
      shows `+2` (never `+20`). Press **Enter**: the roll shows "… +2 situational" and the box is back to a selected `0`.
- [ ] Without deleting anything, type `-1`: the box shows `-1` (never `-10`). **Enter** rolls with "… -1 situational".
- [ ] Click into the "or enter a roll" box, then **Shift+Tab**: the `0` is selected again.
- [ ] Click into the "or enter a roll" box, then click once into the Modifier box: the `0` is selected; typing `3` gives
      `3`. Click again just left of the `3`: a normal cursor appears there, and typing `1` gives `13`.

## Portable Collections

Use a collection with folders, a linked table and some bold or italic text (the Boons table above works), and **no**
table descriptions.

- [ ] Select it and choose **Export Collection…**: the Save dialog offers `<collection name>.tfcollection`. Save it
      somewhere outside the data folder. Open the file in Notepad: readable text starting with
      `"format": "TableForgeCollection"` and `"formatVersion": 1`, with no `"description"`, and `"generator"` shows this
      version.
- [ ] **Import Collection…** and choose that file: before anything changes it asks to import `<name> (2)`, with the
      right folder and table counts and "Existing Collections will not be changed." Choose **Cancel**: nothing is added.
- [ ] Import it again and choose **OK**: `<name> (2)` is added and selected. Its tables roll exactly like the
      originals, the bold and italic text is still there, and following a link opens the table in `<name> (2)`.
- [ ] Import it once more: `<name> (3)`. Close and restart Gidubba: both copies are still there.
- [ ] Copy the file, open the copy in Notepad and change `"formatVersion": 1` to `3`. Importing it is refused ("newer
      file format … Nothing was changed.") and the collection list is unchanged.
- [ ] Give one table in the collection a description and export again: the file says `"formatVersion": 2`, and only that
      table has a `"description"`. Import it: the copy shows the description under that table's dice.
- [ ] With rc25 (see Upgrade): the version 1 file imports; the version 2 file is refused ("uses a newer file format …
      Nothing was changed.") and rc25's collection list is unchanged.
- [ ] Mark one row Unique, use it, and export again: the file says `"formatVersion": 3`, only that row has
      `"unique": true`, and the status line says importing it needs Gidubba 1.4 or later. Import it: the row is Unique and
      not used. Gidubba 1.3.0 refuses the file and changes nothing. Clear Unique and export: back to version 1 or 2.
- [ ] Select `<name> (3)` and choose **Delete Collection…**: it names the collection and its table count, says its
      folders are deleted too and that this cannot be undone. **No** keeps it; **Yes** removes it and selects the next
      collection. The original and `<name> (2)` are unchanged.

## Auto-follow linked tables

Use a copy of Under the Fourth Kingdom: Pallid Delve (or any collection where one table links to another, which links on).
Mark the five Current Depth / Current Scourge selectors as **Manual-selection table** first (Edit table → the checkbox
under Clamp → **Save Table**).

- [ ] Edit table shows **Manual-selection table** under the Clamp checkbox, off for every existing table. Turn it on for
      a selector, save, reopen: still on. Its Roll screen shows "Manual-selection table: choose the result and enter it
      below." under the dice; other tables don't.
- [ ] The Roll screen shows **Auto-follow linked tables**, **off**. With it off, rolling a linked row only offers
      **Open …**, exactly as before.
- [ ] Turn it on. Roll **Area Type** until it shows an Encounter / POI / NPC row: the selector opens with "Auto-followed
      from Area Type (…)" above its name, and it is **not rolled** (no depth is chosen at random). Type your depth (for
      example `4`) and **Resolve**: the depth's table opens and is rolled by itself.
- [ ] Open a selector directly and type a result: its linked table opens and is rolled. Press **Roll** on a selector
      instead: the result shows with its **Open …** button, and nothing is followed.
- [ ] Pale Arroyo POI row 5 ("Reroll …"): auto-follow rolls the table again. If it keeps landing on 5, it stops after two
      automatic rerolls with a message, and **Open Pale Arroyo POI** is still there.
- [ ] Undead Horde — Pool 1 row 8: the Mundane Treasure Table is followed and rolled once; the Undead Merchant row (with
      "twice … and the Exceptional Treasure Table once") is still shown on the step above. **Roll** on Mundane Treasure
      again for the second roll.
- [ ] A table with several result sets whose roll lands on more than one link, or **Rolls: 2** on a linked table: nothing
      is followed, a message says why, and every **Open …** button is there.
- [ ] Recent rolls lists every automatic roll as an ordinary roll. The Streaming Overlay (if on) shows each step's result
      and ends on the last one.
- [ ] Restart Gidubba: **Auto-follow linked tables** is still on. Turn it off and restart: off.
- [ ] With **dddice**: each step throws its own dice and waits for them to settle; the status reads
      "Auto-following… step N". Press **Stop** while dice are in the air: the chain stops ("Auto-follow stopped. The roll
      was cancelled."), the table it had opened stays unrolled, and **Roll** works again. Leaving the table mid-chain also
      stops it. **Stop** also cancels an ordinary dddice roll.
- [ ] Export the collection: only the selectors carry `"manualSelection": true`, and the file version is unchanged (`1`
      without descriptions, `2` with). Import it: the selectors are still Manual-selection tables. Gidubba 1.2.0 imports
      the same file (it ignores the property).

## Unique Results

Use a `d6` table with rows `1`–`6`, and a two-result-set table (for example `D3 Difficulty` / `1 Easy +10` / `2 Normal +0`
/ `3 Hard -10`).

- [ ] Edit table shows a **Unique** checkbox on every row, off for every existing row. **Ctrl+U** in a row toggles it;
      **Mark All Unique** and **Clear Unique** change every row (every result set, in the side-by-side view).
- [ ] Mark rows 1–3 of the `d6` Unique and **Save Table**. The Roll screen tags them **Unique**, shows "Unique results: 0
      of 3 used" and **Reset Used Results…**. A table without Unique rows shows neither.
- [ ] Roll with **Built-in** until row 1 comes up: it is tagged **Used**, grey and still readable; the summary counts it.
      Keep rolling: row 1 never comes up again, and a result that needed rerolls says "Rerolled N×" (also in Recent rolls).
- [ ] Type `1` and **Resolve**: "Result already used — choose another result." Nothing changes.
- [ ] Use up rows 1–3, then mark rows 4–6 Unique and use them up: "All results used — Reset to roll again.", and nothing
      is rolled or recorded.
- [ ] **Reset Used Results…** → **No** keeps them; **Yes** makes them all available, and they are still Unique.
- [ ] Edit a used row's text and save: it stays used. Delete a used row and save: the count drops.
- [ ] With a modifier (`+2`) and with Clamp to Range on: rerolls use the same modifier and Clamp.
- [ ] **Rolls: 3** on a table with three unused Unique rows: three different results, all used afterwards.
- [ ] Auto-follow into a linked table with used Unique rows: the chain rerolls past them, and stops with the message when
      the table is used up.
- [ ] With **dddice**: a throw on a used row is followed by another visible throw, up to 10. If none lands on an unused
      row, it stops with "No unused result came up in 10 rolls, so nothing was used." and does not switch to Built-in.
      **Stop** during rerolls stops them, and the result that was in the air is not used.
- [ ] Restart Gidubba: the used rows are still used.

## Sidebar, Recent rolls and aligned columns

- [ ] The left side shows the table list under "All tables" (a folder's tables, or "Search results" while searching),
      filling the space down to Edit / Delete. There is no Recent tables list.
- [ ] **Recent rolls (N)** sits at the bottom of the main area, collapsed at start. Opening it shows the time, any rerolls
      and the whole result of each roll; clicking one opens its table. It is hidden on Paste Table and Review.
- [ ] A table with three or more result sets: on the Roll screen and in Edit table the columns share the window's width,
      scroll sideways when the window is narrow, and the headings stay over their columns while scrolling.

## Back, the table list and Help (1.5.0)

Use a table whose results link to a second table, which links to a third.

- [ ] Roll the first table and follow a link: **← Back to <first table>** appears just above **Roll**, and the table list
      selects the second table (gold bar, tinted background, bold name) while the Roll screen has focus. Nothing reopens,
      nothing is rolled, and the earlier result is still shown.
- [ ] Follow on to the third table, then **Back**: a new step "Back to the original table" for the first table appears at
      the bottom, "Not rolled yet"; every earlier result is still above it, and the list selects the first table.
- [ ] With **Auto-follow linked tables** on, Roll the first table: the chain runs as before, and **Back** and the list's
      selection follow the last table reached.
- [ ] On a followed table, **Edit table** opens that table, and **Cancel** comes back to it. **Delete table** names that table.
- [ ] Search for (or open a folder without) the followed table: nothing is selected, and **Edit table** / **Delete table**
      are unavailable until it is shown again.
- [ ] **Help & Tips** (bottom of the window) opens https://rpgfrequencies.com/gidubba-guide.html in the default browser.
      **F1** does nothing.
- [ ] A table with Unique rows: **Reset Used Results…** and the count are right under **Roll**; the **Unique** / **Used**
      tags are small rounded labels right after each result's text (under a long result), also in side-by-side columns.
- [ ] Edit table: **Mark All Unique** and **Clear Unique** follow **Clear Formatting** above the rows (on the next line at
      1000×640, nothing cut off), in both the one-set and side-by-side views.
- [ ] Under **Roll**, a small grey line shows the current table's options: a `d6` table with Clamp on reads "Clamp: On" and
      "Manual selection: Off"; with Clamp off, "Clamp: Off"; a `d66` table, "Clamp: Not available", and
      hovering adds "Clamp is not available for d66 tables." to "These are table settings. To change them, use Edit
      table in the table list." A Manual-selection table reads "Manual selection: On". Follow a link, go **Back** and open
      another table: the line always describes the current table. Change Clamp in **Edit table** and save: the line
      changes. At 1000×640 nothing is cut off. Nothing on the line can be clicked or changed.
- [ ] A `d6` table with Clamp off: modifier `+3`, roll a 6: "No entry covers 9." and a note under **Roll** suggesting Clamp.
      Recent rolls and the overlay show only the result.
- [ ] Delete Table, Delete Folder, Delete Collection and Reset Used Results ask with a title naming the action; **No**
      changes nothing.

## dddice

- [ ] Choose **dddice** (guest): the dice panel prepares and a roll shows 3D dice and the right result.
- [ ] Optional, with a dddice account: Account… → Connect, approve on dddice.com, choose a theme, roll with it.
- [ ] Restart: the account and theme are remembered.
- [ ] (1.5.1) Account… → **Disconnect**, then **Connect** the same account and choose the theme: the dice are ready with
      no "dddice room changed" note, **Open my dddice room** opens the same room as before, and dddice.com shows no new
      room. `dddice-account.json` is gone after Disconnect; `dddice-rooms.json` stays and holds no token.
- [ ] With the network off (or dddice failing), **Use Built-in Dice** still works and rolls normally.

## Streaming Overlay (OBS)

Needs OBS Studio. The dddice items also need a connected dddice account.

- [ ] **Streaming Overlay…** (bottom of the window) opens; **Enable streaming overlay** is off. Tick it: "Running on port
      41285."
- [ ] **Copy** beside the Local file path, then in OBS add a **Browser** source with **Local file** ticked and that file:
      the source is transparent and empty.
- [ ] **Show Test Result**: "Gidubba Overlay Test" and "12 — Your streaming overlay is working." appear. **Clear Overlay**
      empties it. Recent rolls is unchanged.
- [ ] Roll a table: its name, roll value and result appear. Untick **Show roll value**, then **Show table name**: each
      disappears straight away and the result stays.
- [ ] Open other tables, edit one, import or export: the overlay does not change. Roll a linked table after following a
      link: it becomes the overlay.
- [ ] With a situational modifier of `+2`: the overlay shows the final number only. A Clamp table rolled past its range
      shows the number rolled. A roll no row covers shows "No entry covers N." in place of the previous result.
- [ ] A result with inline dice (`1d4`): roll the inline dice and the overlay updates the same result in place.
- [ ] Close Gidubba: the overlay goes blank within a few seconds. Start Gidubba again: it works without touching OBS.
- [ ] Close OBS and Gidubba, start OBS first, then Gidubba: the Local file source starts working by itself.
- [ ] With another program on port 41285 (or a second Gidubba with the overlay on), the dialog says the port is in use.
      Type another port, **Apply**: it runs there; refresh the OBS source once and it shows results again.
- [ ] dddice: **Open my dddice room** opens the room on dddice.com; its **Streaming tools** → **3D Dice** link, added as a
      second Browser Source, shows the dice of Gidubba's rolls, then the Gidubba result appears when they settle.

## Foundry VTT export

In Foundry VTT v13 or v14 with the **Roll Table Importer** module enabled (Roll Tables → Import Tables). For each table,
export from Gidubba (Roll screen → **Export…**) and import into Foundry, then roll it there a few times.

- [ ] A `d20` table, pasted with **Copy Foundry JSON**.
- [ ] The same table imported from a file saved with **Save Foundry JSON…**.
- [ ] A `2d6+1` table: the formula is `2d6+1` and rolls land on the right rows.
- [ ] A `d100` table with a `96–00` row: the row is 96–100 and a roll of 100 finds it.
- [ ] A `d66` table: the formula is `1d6 * 10 + 1d6` and rolls only ever give 11–16, 21–26 … 61–66 (never 7, 10, 17 or 37).
- [ ] A `1d66` table (a real 66-sided die): the formula is `1d66`.
- [ ] Result text with curly quotes, em dashes and other Unicode.
- [ ] Result text with `&` (`Sword & Shield`).
- [ ] Result text with `<` and `>` (`Value < 10`).
- [ ] Result text on two lines.
- [ ] A table with a gap (rows 1–4 and 7–10): rolls of 5–6 find nothing, as expected.
- [ ] A table with overlapping rows, if Foundry's behavior is sensible.
- [ ] Note anything Foundry shows differently from Gidubba (especially `&`, `<`, `>` and line breaks).

## Sojour

In Sojour, create or open a Lookup Table, select its first cell, then paste with **Ctrl+V** after each Gidubba
**Export…** → **Copy for Sojour** (Roll screen).

- [ ] A `d6` table: each row fills two cells (range, result) and nothing else is pasted above the first row.
- [ ] A `d100` table with a `96–00` row: the range shows as `96-00`.
- [ ] A table with several result sets: only the chosen one is pasted.
- [ ] Result text with commas, curly quotes, em dashes and other Unicode.
- [ ] Result text on two lines in Gidubba: it pastes as one row, with a space where the line break was.
- [ ] Result text with inline dice (`2d6 Skeletons`): pasted as written.

## Owlbear Rodeo (Tables+)

**Status (2026-09-27):** TableForge-exported `d20`, `d66` (`T66`, in the sample tested) and unmodified `2d6` JSON imported
and rolled correctly in Tables+. A separate, hand-written `1d4+20` Tables+ table consistently rolled 21–24, so Tables+
applies a positive single-die modifier (not an end-to-end Gidubba export test). Everything else below is still untested;
keep re-checking the tested items in each release. Needs Owlbear Rodeo and the Tables+ extension. Import each
file from `docs/external-tests/tables-plus/` (or export the same kind of table with **Export…** → **Copy Tables+ JSON** or
**Save Tables+ JSON…**), then roll each imported table several times. For every item, note whether the import worked,
what Tables+ shows, and any roll that lands on the wrong row or on nothing.

- [ ] `d20.json`: imports as a `1d20` table and rolls land on the right rows.
- [ ] `d100.json`: the last row is 96–100 and a roll of 100 finds it.
- [ ] `genuine-1d66.json`: a single 66-sided die (`1d66`), results 1–66.
- [ ] `d66-T66.json`: `T66` rolls only 11–16, 21–26 … 61–66 (never 17, 20 or 37) and each lands on its row.
- [ ] `2d6.json`: imports as `bell-curve` `2d6`, results 2–12.
- [ ] `d20-plus-2.json`: `1d20+2` is accepted and rolls 3–22 land on the right rows (Tables+ applied `1d4+20` in a
      hand-written test; this Gidubba export itself is untested).
- [ ] `d4-minus-2-signed.json`: whether `1d4-2` and the negative range (-1 to 0) are accepted, and how they roll.
- [ ] `gaps-overlaps.json`: whether rows with a gap (5–6) and an overlap (3–4) import, and what rolls in them do.
- [ ] `text-inline-2d6.json`: whether `{2d6} goblins` shows as written or rolls the dice.
- [ ] `text-reroll.json`: whether `#reroll` shows as written or makes Tables+ roll again.
- [ ] Copy vs Save: pasting and importing a file give the same table.
- [ ] Future compatibility only (Gidubba refuses it today): does a hand-made Tables+ table with `2d6+1` dice import and
      roll 3–13 correctly?

## PUM Companion export

**Status (2026-10-23):**
- **Checked in PUM Companion:** TableForge-made bundles (the Slice 1 acceptance bundle and the Slice 2 smoke collection)
  imported and rolled as expected, and importing the same bundle again offered to overwrite.
- **Not checked:** rows beyond the dice, reached only through PUM's own roll overrides.

Needs PUM Companion. Use synthetic tables (the smoke collection `ZZTF_S2_Smoke.tfcollection` or similar). In PUM, delete
the test tables with its own delete option.

- [ ] **Main window:** **Export to PUM Companion…** sits under Export Collection / Delete Collection. It is greyed out with
      no collection and enabled with one.
- [ ] **Roll screen:** **Export…** still lists every earlier option, then a separator, then **Save PUM Companion Bundle…**.
- [ ] **Review window:** the summary (*N tables will be exported. M will be skipped.*), the ⚠ lines (skipped tables
      with their reasons, then warnings), the boxed overwrite warning, then the full report.
- [ ] **Copy Report** pastes into Notepad as the whole report.
- [ ] **Cancel** in the review saves nothing and opens no Save dialog. **Cancel** in the Save dialog saves nothing.
- [ ] **Saving:** the Save dialog is titled *Export to PUM Companion* and suggests `<collection or table>.pumb`. Saving
      over an existing file asks first, and leaves no `.tmp` file beside it.
- [ ] **Status messages:** after a collection export the status bar says *Exported N of T tables from "…" to … (M
      skipped).* After a single-table export the line next to Export… says *PUM bundle saved to ….*
- [ ] **Unexportable single table:** a table whose every result is blank says why next to Export…, and opens no review.
- [ ] **Followed link:** the export covers the table on screen.
- [ ] **In PUM Companion:**
  - the import reports the exported count;
  - the categories are `Collection` and `Collection - Folder`;
  - gaps roll `(No result)`;
  - a table with two result sets shows both lines;
  - a link shows as a `→ …` text line.
- [ ] **Importing the same bundle again:** PUM offers to overwrite, and the tables are replaced, not duplicated.
- [ ] **Other exports:** Foundry, Tables+, Sojour and Copy Table Text behave exactly as before.

### Roll linked tables automatically in PUM (native nested tables)

Checked in PUM on Windows and iPad with the F11 fixtures (2026-10-26): `{{Name}}` resolves ignoring case, with em dashes and
` (2)`, through five levels; PUM keeps one table per name in every category, and a same-named import replaces its rows.
Use a copy of real data with the five Fourth Kingdom selectors marked Manual-selection, or synthetic tables.

- [ ] **Off by default:** the Collection review shows **Roll linked tables automatically in PUM ({{…}})**, unticked, every
      time. Saved without ticking it, the bundle is byte-identical to the previous version's export.
- [ ] **Single table:** Save PUM Companion Bundle… on a Roll screen shows no such checkbox.
- [ ] **Ticking it:** the report changes at once; a line says how many links will roll (*55 of 131 links will roll their
      table in PUM. 76 stay text…* for Under the Fourth Kingdom), and the boxed name warning appears. Unticking restores both.
- [ ] **Text links explained in the report:** manual-selection tables (both ends), loops (Pale Arroyo POI row 5), and
      a name another Collection also uses (Of Blood and Clay → Event Check → Hazards, a ⚠ warning in the review too).
- [ ] **In PUM (Windows and iPad):**
  - Pale Arroyo POI row 3 shows *→ Exceptional Treasure Table:* followed by a rolled treasure;
  - Undead Horde — Pool 1 row 8 rolls Mundane Treasure once and keeps its whole instruction;
  - the selectors and Area Type rows 3–6 still show `→ …` text lines; Pale Arroyo POI row 5 stays text.

## Uninstall and reinstall

- [ ] Uninstall Gidubba (Settings → Apps).
- [ ] The program and its Start Menu entry are gone.
- [ ] `%LOCALAPPDATA%\TableForge` is **still there**, with `tableforge.db` in it.
- [ ] Reinstall: the earlier collection and table come back by themselves.

## Upgrade

- [ ] Install an **older** packaged release.
- [ ] Create a collection and a table, roll it (and connect dddice if you use it).
- [ ] Close Gidubba, then run the **newer** installer over it.
- [ ] The newer version starts (About shows it), and everything from before is still there.
- [ ] If the newer version changed the data format, a `tableforge.pre-v*-from-v*.backup.db` copy appears in the data folder.
- [ ] From rc23 to rc24: tables look and roll exactly as before, `tableforge.pre-v9-from-v8.backup.db` appears, and rc23
      then refuses the data (it was made by a newer version).
- [ ] From rc24 to rc25: no data-format change, so no new backup copy appears; tables, formatting and rolls are exactly
      as before, and the Export / Import / Delete Collection buttons are there. rc24 can still open the data afterwards.
- [ ] From rc25 to rc26: `tableforge.pre-v10-from-v9.backup.db` appears; every collection, table, formatting, link and
      Clamp setting is exactly as before, and no table has a description yet. rc25 then refuses the data (it was made
      by a newer version) and changes nothing.
- [ ] From rc26 to 1.0.0: no data-format change, so no new backup copy appears; tables, descriptions and rolls are exactly
      as before, and **Streaming Overlay…** is there (off). After turning the overlay on, rc26 can still open the data and
      ignores `streaming-overlay.json` and `streaming-overlay.html`.
- [ ] From 1.0.0 to 1.1.0: no data-format change, so no new backup copy appears. Tables, descriptions, settings and
      rolls are exactly as before. **Export to PUM Companion…** is there, and 1.0.0 can still open the data afterwards.
- [ ] From TableForge 1.1.0 to Gidubba 1.2.0 (the rename): no data-format change, so no new backup copy appears, and every
      file in the data folder is unchanged. Settings → Apps lists **one** entry, now **Gidubba** 1.2.0 by RPG Frequencies;
      the Start Menu has **one** shortcut, **Gidubba**, with the Gidubba icon (the old **TableForge** shortcut is gone).
      The program is still in `%LOCALAPPDATA%\Programs\TableForge` as `TableForge.exe`. A connected dddice account is
      still connected, the dice choice and overlay settings are kept, and an OBS Local file source keeps working.
      1.1.0 can still open the data afterwards, and `.tfcollection` files go both ways between 1.1.0 and 1.2.0.
- [ ] From Gidubba 1.2.0 to 1.3.0: `tableforge.pre-v11-from-v10.backup.db` appears; every collection,
      table, description, link and setting is exactly as before, and no table is a Manual-selection table yet.
      **Auto-follow linked tables** is off. 1.2.0 then refuses the data (it was made by a newer version) and changes
      nothing; to go back, close Gidubba and restore the backup copy in place of `tableforge.db` (changes made since the
      upgrade are lost; export collections first to keep them, since 1.2.0 imports those files).
- [ ] From Gidubba 1.3.0 to 1.4.0: `tableforge.pre-v12-from-v11.backup.db` appears; every collection, table, description,
      link, Manual-selection mark and setting is exactly as before, no row is Unique and Recent rolls are kept. 1.3.0 then
      refuses the data (it was made by a newer version) and changes nothing; to go back, close Gidubba and restore the
      backup copy in place of `tableforge.db` (changes made since the upgrade are lost).
- [ ] From Gidubba 1.5.0 to 1.5.1: no data-format change, so no new backup copy appears. A connected dddice account
      stays connected with its theme, and keeps using the same room (no "dddice room changed" note). 1.5.0 can still
      open the data and still uses the dddice account afterwards.
- [ ] From Gidubba 1.4.0 to 1.5.0: no data-format change, so no new backup copy appears. Every collection, table, Unique
      mark, used result and setting is exactly as before. Settings → Apps still lists one **Gidubba** entry, now 1.5.0.
      1.4.0 can still open the data afterwards, and `.tfcollection` files go both ways between 1.4.0 and 1.5.0.
