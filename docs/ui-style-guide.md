# SummonersVault UI style guide

The following prompt is the canonical UI direction and must remain available to future contributors:

```css
Create a dark, premium application UI with a calm, polished, game-adjacent visual style.

Color palette:
- Main background: #090b0f
- Primary surface/panels: #11151d
- Raised surface: #121720
- Input/subtle surface: #0d1016
- Standard border: #313746
- Subtle border: #242a35
- Primary text: #f5f2ea
- Secondary text: #aab3c2
- Disabled text: #6f7785
- Primary interactive accent: #7aaee6
- Primary hover: #5f9ddd
- Warm highlight/focus accent: #d0a54f
- Success: #62b989
- Warning: #d9a441
- Danger: #df6b6b

Visual direction:
- Near-black blue-gray background with layered charcoal surfaces.
- Warm off-white typography instead of pure white.
- Muted blue for normal interactive elements.
- Restrained antique gold for keyboard focus, selected states, important highlights, and premium accents.
- Avoid neon colors, pure black/white contrast, glassmorphism, and strong gradients.
- Use subtle gradients only for depth or texture.
- Use thin blue-gray borders, soft dark shadows, and 12–16px rounded corners.
- Keep layouts spacious and calm; prefer clear grouping and light separators over many colored panels.
- Labels should be compact and functional. Headings should be clear, semibold, and not oversized.
- Buttons should feel solid and understated: blue primary buttons, dark bordered secondary buttons, transparent ghost buttons, muted red destructive buttons.
- Hover states should be subtle: slight border brightening, small background lift, or a 1–2px vertical movement.
- Animations should be short, around 150ms, and respect reduced-motion preferences.
- Keep accessibility high: visible gold keyboard focus, sufficient contrast, and never use color as the only status indicator.
Bijbehorende neutrale CSS-variabelen:
:root {
  --background: #090b0f;
  --surface: #11151d;
  --surface-raised: #121720;
  --surface-muted: #0d1016;

  --border: #313746;
  --border-subtle: #242a35;
  --border-highlight: #687183;

  --foreground: #f5f2ea;
  --muted-foreground: #aab3c2;
  --disabled-foreground: #6f7785;

  --primary: #7aaee6;
  --primary-hover: #5f9ddd;
  --highlight: #d0a54f;
  --focus-ring: #d0a54f;

  --success: #62b989;
  --warning: #d9a441;
  --danger: #df6b6b;
}
```

## WPF implementation rules

All colors are exposed as brushes in `Themes/Theme.xaml`. Layout uses 8 px spacing increments, 12 px control radii, and 16 px panel radii. Segoe UI Variable is the preferred typeface, with Segoe UI as fallback. Account status always combines text with an icon or shape. Motion is optional decoration and never required to understand state.

Current product refinement: the implemented primary interaction color is antique gold (`#B88A3D`, hover `#C99B4D`), with muted-gold input outlines. The canonical prompt above remains verbatim; this refinement supersedes its original primary-blue values in the application theme.
