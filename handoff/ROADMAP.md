# Roadmap after 1.0

Designed or scoped during this handoff, not part of the epic.

| # | Item | Why | Depends on |
| --- | --- | --- | --- |
| R1 | Press-to-capture hotkey fields. | Free-text keys fail silently. Input System actions already exist (`TerminalPlayerInputController`). | 09 |
| R2 | Collapsed pill: a hidden terminal shows a small draggable pill with the count of new errors. | Errors on device go unseen while the terminal is closed. | 10, 11 |
| R3 | Docked side panel form (direction 1b). | Long QA sessions next to the game on wide screens. | 10 |
| R4 | Floating window form (direction 1c). | Games that show the console as part of the fiction. | 10 |
| R5 | Aliases and macros: `alias`, run a saved list of commands. | Testers repeat the same setup lines. | 06 |
| R6 | Per-command manual from XML doc comments (`man <command>`). | `help` shows one line; arguments need more. | 06 |
| R7 | Long-running commands with progress and cancel. Completion stays synchronous. | Scene loads and bulk spawns block input. | 06 |
| R8 | Log export: file on desktop, share sheet on mobile. | Testers attach logs to bug reports. | 11 |

Not planned: remote or web console, runtime C# evaluation, cloud services.
