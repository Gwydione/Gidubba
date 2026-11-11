# Changelog

Each released version of Gidubba (formerly TableForge), newest first. The full notes for each one are in
`docs/release-notes/`. Versions before 1.2.0 were released as TableForge, and their entries keep that name.

## 1.5.1

- **Fix: a connected dddice account reuses its room after Disconnect and Connect**, also across restarts, instead of
  creating a new room every time. Disconnect still deletes the login (token); the account's room id and dddice
  account id, which are not secret, are kept in `dddice-rooms.json` in the data folder.
- A room is only reused for the dddice account it belongs to (checked with dddice); a different account gets its own.
- If dddice will not create a room (402), Gidubba says the account may have reached its room limit and does not try
  again until you press **Try again**. A failed room check never creates a room.
- Guest dice, rolling, collections, the Streaming Overlay and the data format (schema 12) are unchanged: 1.5.0 can
  still open the data.
- Notes: `docs/release-notes/1.5.1.md`

## 1.5.0

- **New: Back to the original table.** After following links (by hand or with Auto-follow), **← Back to …** above
  **Roll** makes the starting table current again, as a new step at the bottom; earlier results stay. It never rolls.
- **New: the table list follows the Roll screen.** Following a link (or going back) selects that table in the list,
  without opening it, so **Edit table** and **Delete table** act on it. When a folder or search hides it, nothing is
  selected. The selected table is clearly marked even while the list does not have focus.
- **New: Help & Tips** opens the Gidubba guide in your web browser.
- **Unique Results:** **Reset Used Results…** and the used count sit right under **Roll**; the **Unique** / **Used**
  tags are small labels right after each result's text; in Edit table, **Mark All Unique** and **Clear Unique** are
  above the rows, after **Clear Formatting**.
- A read-only line under **Roll** shows the current table's options: **Clamp: On / Off / Not available** (by what
  Clamp actually does there) and **Manual selection: On / Off**; they are changed in **Edit table**.
- A note under **Roll** when a situational modifier takes a roll outside a table that could clamp. Confirmation
  questions have titles naming the action.
- No data-format change (still schema 12): 1.4.0 can still open the data. Collection files, the installer identity,
  install folder and data folder are unchanged.
- Notes: `docs/release-notes/1.5.0.md`

## 1.4.0

- **Data format update: 1.3.0 cannot open the data afterwards.** The first start updates the database from schema 11
  to 12, after saving `tableforge.pre-v12-from-v11.backup.db` beside it. That copy is the only way back to 1.3.0;
  anything changed since the update is lost.
- **New: Unique Results** (Edit table: a **Unique** checkbox per row, **Ctrl+U**, **Mark All Unique**, **Clear Unique**).
  A used Unique row is skipped: a roll landing on it is discarded and the same dice rolled again (Built-in up to 10,000
  times, dddice 10 throws), with the modifier and Clamp as usual. Gaps stay "no result". Typed results on a used row
  are refused. Recent rolls notes "Rerolled N×". **Every existing row starts repeatable.**
- **New: used results on the Roll screen** (tagged **Unique** / **Used**, used rows grey), "Unique results: N of M
  used", and **Reset Used Results…** (asks first; one table; keeps which rows are Unique).
- **Simpler sidebar:** the table list fills the left side under a heading saying what it shows; the Recent tables list
  is gone. **Recent rolls** is a collapsible panel at the bottom of the main area, collapsed at start.
- **Aligned result-set columns** on the Roll screen and in Edit table share the window's width and scroll sideways
  when needed, with headings kept in line.
- **`.tfcollection` format version 3** when a collection has Unique rows (1.3.0 refuses it); otherwise version 1 or 2
  exactly as before. Used results are never exported. Other exports are unchanged.
- The GitHub project is now [Gwydione/Gidubba](https://github.com/Gwydione/Gidubba). The installer identity, install
  folder and data folder are unchanged.
- Notes: `docs/release-notes/1.4.0.md`

## 1.3.0

- **Data format update: 1.2.0 cannot open the data afterwards.** The first start updates the database from schema 10
  to 11, after saving `tableforge.pre-v11-from-v10.backup.db` beside it. To go back to 1.2.0, restore that copy; anything
  changed since the update is lost.
- **New: Auto-follow linked tables** (Roll screen, off by default, remembered). A result linking to one other table
  opens and rolls it, and so on; each step says where it came from. **Stop** ends a chain or a dddice roll. A chain
  stops after 8 tables, or after rolling one table automatically twice.
- **New: Manual-selection tables** (Edit table, under Clamp to Range). Auto-follow opens such a table without rolling it
  and continues from the result you enter. **Every existing table starts with it off**: mark your own selector tables.
  `.tfcollection` keeps the setting without a new format version, and 1.2.0 imports those files.
- **New, optional: Roll linked tables automatically in PUM ({{…}})** in the Collection PUM export review, **unticked
  every time**. Unticked, the export is byte-identical to 1.2.0. Ticked, links to tables in the bundle become PUM nested
  rolls. Links to or from Manual-selection tables, loops, and names another collection also uses stay text, and the
  report says why.
- The installer identity, install folder and data folder are unchanged.
- Notes: `docs/release-notes/1.3.0.md`

## 1.2.0

- **TableForge is now Gidubba**, by RPG Frequencies, with a new icon (a clay tablet and reed stylus). Only the name and
  icon change: windows, About, messages, the installer, the Start Menu shortcut and the documents now say Gidubba.
- **Upgrading keeps everything.** The installer replaces TableForge 1.1.0 in place (one Apps entry, renamed). The old
  TableForge Start Menu shortcut is replaced by a Gidubba one. The program stays `TableForge.exe` in the same folder, and
  the data stays in `%LOCALAPPDATA%\TableForge`.
- New dddice rooms are named "Gidubba"; an existing connected room keeps its name.
- No change to rolling, exports or the data format (database schema 10, `.tfcollection` unchanged, so 1.1.0 and 1.2.0
  read each other's files).
- Notes: `docs/release-notes/1.2.0.md`

## 1.1.0

- **New: PUM Companion export.**
  - **Collection:** **Export to PUM Companion…** saves the selected collection as a `.pumb` bundle.
  - **Table:** on the Roll screen, **Export…** → **Save PUM Companion Bundle…** saves the table shown.
  - **Review before saving:** a review lists what will be exported, what will be skipped and why, the warnings and the
    full report (**Copy Report**). The file is written completely or not at all.
  - **Same results:** every exported table shows the same result in PUM as in TableForge for every roll of its own dice.
    A table that can't be reproduced exactly is skipped, with the reason. This covers overlapping rows, rolls below 1
    that would change the result, text PUM would run as a command, and tables whose every result is blank.
  - **Conversions:** No Match becomes a `(No result)` row, and Clamp to Range is kept. Several result sets become one
    table, links become a `→ Table` text line, and bold and italic become Markdown. A `d66` becomes `1D66`. Descriptions
    aren't exported.
  - **Re-importing:** the review warns that importing the same bundle again lets PUM overwrite tables it already has.
- No change to rolling, to the other exports or to the data format (database schema 10, `.tfcollection` unchanged).
- Notes: `docs/release-notes/1.1.0.md`

## 1.0.0

- The first stable release, adding the Streaming Overlay (OBS) on top of the 1.0.0 release candidates:
  - Paste and Review, Collections and folders, Built-in and dddice dice;
  - linked tables, inline dice, several rolls at once;
  - table descriptions, and bold and italic results;
  - Portable Collections;
  - Copy Table Text, Copy for Sojour, and Foundry VTT and Owlbear Rodeo (Tables+) export.
- Notes: `docs/release-notes/1.0.0.md`. Notes for the release candidates are `docs/release-notes/1.0.0-rc18.md` to `1.0.0-rc26.md`.
