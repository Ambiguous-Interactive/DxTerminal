# Inconsistencies found during the audit

Each item names the issue that fixes it. Items 2 and 5 were decided on 2026-10-11.

| # | Inconsistency | Where | Status |
| --- | --- | --- | --- |
| 1 | Two names: "DX Command Terminal" (README title) and "DxCommandTerminal" (package, docs). | README.md, Documentation~ | Fix in 01: "DxTerminal". |
| 2 | Three names for one surface: `CommandPaletteUI` (code), "Quick-launch bar" (docs), "Command Palette" (settings header, hotkey tooltip). | palette.md, TerminalSettings.cs | **Decided:** "Quick launch" in all user-facing text. Code keeps `CommandPaletteUI`. Fix in 01 and 04. |
| 3 | `migration.md` lists variable watches, persistent command buttons, and an inspector redesign as "not planned". This epic ships all three. | Documentation~/migration.md | Fix in 16. |
| 4 | `Terminal.Log(string, params object[])` breaks rule 17 (no `params` on frequently called APIs). | Terminal.cs | Fix in 06: add typed overloads. The `params` form stays for 4+ arguments. Not breaking. |
| 5 | Default contexts differ by path: the attribute and `CommandInfo` (legacy `AddCommand`) use `All`; `CommandDefinition` and the builder use `Gameplay`. | RegisterCommandAttribute.cs, CommandInfo.cs, CommandDefinition.cs, CommandBuilder.cs | **Decided:** `Gameplay` (Play Mode and players) on every path. Fix in 06; listed in the migration guide. |
| 6 | `MaxArgCount = -1` is a sentinel; rule 16 bans sentinels (documented exception). | RegisterCommandAttribute.cs | Fix in 06: typed methods infer counts. `(CommandArg[])` methods keep the field. |
| 7 | Docs use DocFX syntax (`[!code-csharp]`, `xref:`) that GitHub and MkDocs do not render. | Documentation~ | Fix in 04. |
| 8 | README (45 KB) repeats docs pages. | README.md | Fix in 04. |
| 9 | Default colors are the iOS system palette; no Dx values. | TerminalThemeAsset.cs, Packs/Themes | Fix in 07. |
| 10 | Quick launch capture: blue-on-blue selected text, row label on the row's bottom edge, unstyled block under results, caret outside the panel. | palette.png, palette-wide-long-help.png | Fix in 10. |
| 11 | Inspector shows raw hotkey syntax: `` #` `` means Shift plus backtick. | Hotkeys.png, TerminalKeyboardController.cs | Fix in 09: show key chips; parse and validate on edit. |
| 12 | Samples README and lifecycle.md tell users to register from `Start` because of Reset State On Init. | Samples~/TerminalCommands/README.md, lifecycle.md | Fix in 06 and 16 after session bootstrap. |
| 13 | `HintDisplayMode.AutoCompleteOnly` reads as a code name in the UI. | TerminalSettings.cs | Fix in 09: label "While typing"; enum stays. |
| 14 | The docs say "three ways to register a command". The shell has the attribute plus four `AddCommand` overloads. | commands.md, CommandShell.cs | Fix in 04: the docs teach the attribute and the builder, and put the rest in one "Low-level registration" section. |
| 15 | `CommandPaletteUI` has `Opened` and `Closed` events; `TerminalUI` has none. | CommandPaletteUI.cs, TerminalUI.cs | Fix in 10. |
