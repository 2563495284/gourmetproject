# TimelineAxis Fresh UI ImageGen Record

Generation mode: built-in ImageGen (`image_gen.imagegen`).

The generated source images were post-processed only to remove the baked checkerboard and restore a true alpha channel. No painted content was added after generation.

## `timeline_panel_fresh.png`

```text
Use case: stylized-concept
Asset type: one isolated 2D Unity uGUI timeline HUD panel sprite, a very wide horizontal rounded paper plaque, roughly 6:1 aspect ratio
Style/medium: fresh children's picture-book gouache, slightly irregular dark-brown hand-drawn outline, large flat color masses, minimal detail
Composition: cream center panel, soft sage green outer edge, evenly rounded corners and even border thickness, completely clean stretch-safe center and long straight stretch-safe edges for 9-slice; no contents inside
Color palette: cream #FFF4D6, sage #8BBF7A, deep sage #4E765A, apricot #F3A35C, ink brown #5B3926
Constraints: genuinely transparent background outside the plaque, isolated asset, no text, no letters or numbers, no logo, no watermark, no external drop shadow, sparse subtle paper grain only, high legibility at small UI size
```

## `timeline_track_fresh.png`

```text
Use case: stylized-concept
Asset type: one isolated 2D Unity uGUI timeline HUD track sprite, an extremely long thin horizontal rail with rounded end caps, roughly 8:1 aspect ratio
Style/medium: fresh children's picture-book gouache, slightly irregular dark-brown hand-drawn outline, large flat color masses, minimal detail
Composition: deep sage green rail with a restrained ink-brown outline, clean uniform stretch-safe center, symmetrical rounded caps, no marks or decorations
Color palette: cream #FFF4D6, sage #8BBF7A, deep sage #4E765A, apricot #F3A35C, ink brown #5B3926
Constraints: genuinely transparent background, isolated asset, no text, no letters or numbers, no logo, no watermark, no external shadow, no surrounding canvas color, readable when displayed only 12 pixels tall
```

## `timeline_progress_fresh.png`

```text
Use case: stylized-concept
Asset type: one isolated 2D Unity uGUI timeline progress sprite, an extremely long thin horizontal apricot brush stripe with softly rounded end caps, roughly 8:1 aspect ratio
Style/medium: fresh children's picture-book gouache, slightly irregular ink-brown hand-drawn edge, large flat color, minimal detail
Composition: warm apricot orange center, clear uniform stretch-safe middle, very subtle handmade waviness only, symmetrical compact ends, no decorations
Color palette: cream #FFF4D6, sage #8BBF7A, deep sage #4E765A, apricot #F3A35C, ink brown #5B3926
Constraints: genuinely transparent background, isolated asset, no text, no letters or numbers, no logo, no watermark, no shadow, readable when displayed 10 to 12 pixels tall
```

## `timeline_tick_fresh.png`

```text
Use case: stylized-concept
Asset type: one isolated tiny 2D Unity uGUI timeline date tick sprite, a compact rounded dot shaped like a very simple seed or small leaf peg, centered, roughly square canvas
Style/medium: fresh children's picture-book gouache, slightly irregular dark-brown hand-drawn outline, one large flat color mass, minimal detail
Composition: simple sage green rounded tick with a tiny cream highlight, bold clean silhouette, no stem and no extra decoration
Color palette: cream #FFF4D6, sage #8BBF7A, deep sage #4E765A, apricot #F3A35C, ink brown #5B3926
Constraints: genuinely transparent background, isolated single object, no text, no numbers, no logo, no watermark, no shadow, extremely low detail, readable at 18 by 18 pixels
```

## `timeline_cursor_fresh.png`

```text
Use case: stylized-concept
Asset type: one isolated compact 2D Unity uGUI timeline cursor sprite, a small downward-pointing leaf or teardrop caret, centered on a square canvas
Style/medium: fresh children's picture-book gouache, slightly irregular dark-brown hand-drawn outline, large flat color masses, minimal detail
Composition: apricot orange downward leaf-teardrop with one small sage highlight, clearly points downward, bold uncluttered silhouette
Color palette: cream #FFF4D6, sage #8BBF7A, deep sage #4E765A, apricot #F3A35C, ink brown #5B3926
Constraints: genuinely transparent background, isolated single object, no text, no letters or numbers, no logo, no watermark, no shadow, readable at 32 by 32 pixels
```

## `timeline_day_badge_fresh.png`

```text
Use case: stylized-concept
Asset type: one isolated minimal 2D Unity uGUI current-day text badge sprite, a plain blank rounded rectangle, roughly 8:3 aspect ratio
Style/medium: fresh children's picture-book gouache with a slightly irregular dark-brown hand-drawn outline, extremely simple, flat color
Composition: only a cream rounded rectangle with one narrow sage border; the entire center and all four corners are clean and undecorated for 9-slice; perfectly blank inside
Color palette: cream #FFF4D6, sage #8BBF7A, ink brown #5B3926
Hard constraints: transparent background outside; NO flowers, NO leaves, NO ornaments, NO icons, NO corner decorations, NO text, NO letters, NO numbers, NO logo, NO watermark, NO shadow; single plain shape; readable at 112 by 42 pixels
```

## `timeline_node_bubble_fresh.png`

```text
Use case: stylized-concept
Asset type: one isolated 2D Unity uGUI timeline node bubble shell sprite, a circular blank token centered on a square canvas
Style/medium: fresh children's picture-book gouache, slightly irregular dark-brown hand-drawn outline, large flat color masses, very low detail
Composition: round cream paper token with a simple sage green rim and dark ink-brown outer outline, broad completely empty center reserved for a separate icon, symmetric clean silhouette, no tail
Color palette: cream #FFF4D6, sage #8BBF7A, deep sage #4E765A, apricot #F3A35C, ink brown #5B3926
Constraints: genuinely transparent background, isolated single object, no text, no letters or numbers, no icon inside, no ornament, no logo, no watermark, no shadow, readable at 60 by 60 pixels
```

### Centering and edge-cleanup edit

```text
Use case: precise-object-edit
Asset type: Unity uGUI node-bubble sprite with true transparency
Input image: Image 1 is the edit target and must remain visually the same asset
Primary request: clean and rebuild only the outside silhouette edge, remove every stray/background/checkerboard pixel, crop the transparent margins evenly, and place the circular token at the exact canvas center
Composition: one circular token, perfectly centered horizontally and vertically, equal transparent padding on all four sides (about 5% of canvas width), circular content must not touch the canvas edge
Edge quality: smooth high-resolution anti-aliased alpha edge with soft 1–2 pixel fractional-alpha coverage; no jagged staircase edge, no white fringe, no dark halo, no matte contamination
Preserve exactly: cream center, sage green rim, dark brown irregular hand-drawn outline, original proportions, original low-detail children's picture-book gouache style, empty icon center
Background: genuinely transparent RGBA outside the token, not a checkerboard and not white
Constraints: no text, no icon, no tail, no shadow, no extra decoration, no logo, no watermark; do not redesign the token; do not change palette
```
