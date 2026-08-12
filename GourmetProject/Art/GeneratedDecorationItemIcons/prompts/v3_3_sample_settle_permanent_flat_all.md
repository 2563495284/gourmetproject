# V3.3 sample — `settle_permanent_flat_all`

Built-in ImageGen edit prompt. Image 1 is the edit target: the existing V3.2 transparent icon.

```text
Use case: precise-object-edit
Asset type: restaurant game UI item icon, V3.3 simplification sample
Input images: Image 1 is the V3.2 edit target and the exact design anchor.

Primary request: Edit Image 1 by reducing about 20% more small structural detail, while preserving the complete dimensional mechanical scoreboard as the same recognizable object. This is a controlled simplification, not a redesign.

Scene/backdrop: A perfectly flat, uniform solid #00ff00 chroma-key background for local removal. No gradient, texture, lighting variation, floor plane, shadow, reflection, or glow. Do not use #00ff00 anywhere in the object.

Subject and invariants — keep unchanged:
- one complete freestanding vintage mechanical flip scoreboard, not a flat sign or plaque;
- the full mahogany body and brass construction, with a clear three-quarter view and convincing thickness;
- three separate cream flip cards showing exactly: large coral star — large simple plate — large coral star;
- chef-hat crest centered on top;
- right-side circular knob with a blue center;
- blue base;
- thick dark-brown outer contour, bold cartoon painted volume, rounded dimensional forms;
- the same centered composition, scale, silhouette, and generous padding as Image 1.

Change only these detail reductions:
1. Remove the three chunky independent hanging-ring blocks above the cards. Keep one simple horizontal axle and attach each card with only one very short, narrow, minimal connector. The connectors must not read as separate decorative rings or blocks.
2. Make the chef-hat badge use only one clean brass rim around its red center; remove any nested brass rim or duplicate highlight ring.
3. Make the main face frame only one broad brass border plus one inner dark edge; no extra nested gold borders.
4. Make the side knob exactly one plain blue center plus one single brass border; remove every extra highlight ring or inset ring.
5. Make the blue base one uninterrupted blue color block with only one bottom brass line; remove any extra blue or gold edge bands.
6. Remove all wood grain, metal grain, tiny bevel lines, rivets, grooves, decorative seams, and repeated highlights. Each large wood or brass surface may have only one broad simple highlight shape to preserve volume.

Style/medium: polished restaurant-game UI icon; cartoon thick-paint rendering; large clean shapes; warm mahogany, brass gold, cream, coral, and peacock blue; 4–5 coordinated color groups; 2–3 broad cel-shaded value layers; crisp antialiased edges.

Composition/framing: single object centered, roughly 70–80% of the square, fully visible, at least 40 px equivalent clear chroma padding on all sides. No cast shadow or contact shadow.

Constraints: Preserve the complete dimensional machine and all listed invariants aggressively. The result must remain clearly more substantial and three-dimensional than the rejected V2 flat-card style. No text, numbers, letters, logos, watermark, extra props, extra panels, background scene, or personification.
```

Post-process: use the installed `remove_chroma_key.py` helper with border auto-key, soft matte, and despill; then resize the alpha PNG to 512×512 and inspect at 80 px and 64 px.
