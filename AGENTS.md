# SummonersVault repository guidance

Before changing any user interface, read and follow `docs/ui-style-guide.md` in full.

- Treat the design tokens in `src/SummonersVault.App/Themes/Theme.xaml` as the only source of UI colors. Do not place raw color literals in views or control templates.
- Preserve the calm, premium, game-adjacent direction without Riot or League logos.
- Every interactive element must have a visible antique-gold keyboard focus state, an automation name, and a non-color status cue.
- Keep animation near 150 ms and disable nonessential motion when Windows client-area animation is disabled.
- Never log, serialize to diagnostics, or transmit account passwords, master-password material, database keys, or League Client lockfile tokens.
- The League Client integration is read-only. Do not add POST, PUT, PATCH, or DELETE calls.
- Do not use em dashes in source code, UI copy, documentation, comments, or commit messages. Use a standard hyphen (`-`) when punctuation is needed.
