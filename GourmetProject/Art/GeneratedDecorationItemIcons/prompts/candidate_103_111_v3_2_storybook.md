# Candidate 103–111 — V3.2 light storybook style transfer

Built-in ImageGen edit workflow. Each icon is generated independently.

- Image 1: the corresponding V3.2 full final; edit target and absolute subject/geometry anchor.
- Image 2: supplied storybook reference; style reference only.

## Shared edit prompt

```text
Use case: style-transfer
Asset type: restaurant game UI item icon
Primary request: Apply a light style-only transfer to Image 1. Change only how it is painted; preserve the V3.2 item itself exactly.
Style reference: Use Image 2 only for its flat picture-book illustration character: opaque gouache feel, visible but controlled hand-painted brush strokes, slightly irregular deep-brown hand-drawn outlines, large matte color fields, simple block shadows, warm paper-like pigment variation inside the object, and much less glossy/mirror-like highlighting.
Invariants: preserve Image 1's subject, object count, composition, framing, scale, silhouette, perspective, geometry, construction, proportions, palette identity, material identity, V3.2 detail density, and every item-specific hard count. Do not add, remove, merge, replace, rotate, flatten, or redesign any object or part. Keep the same three-dimensional tangible prop; do not turn it into a flat symbol.
Backdrop: perfectly uniform flat solid #ff00ff chroma key. No gradient, texture, lighting variation, vignette, floor, cast shadow, contact shadow, reflection, or background scene. Do not use #ff00ff anywhere in the subject.
Constraints: no text, letters, numbers, logos, watermark, extra props, magic effects, or personification.
```

## 103 — `unused_discard_mult_all`

Preserve the teal A-frame rack, handle, feet, and exactly three charcoal garbage-bag rolls, one per tier.

## 104 — `unused_discard_flat_all`

Preserve one red horseshoe-magnet base, one silver clamp mechanism, and exactly one folded cream bag stack.

## 105 — `skip_reward_dish_luck`

Preserve one complete empty cream plate, one entirely blank folded-corner receipt, and exactly one green four-leaf-clover seal at the rim.

## 106 — `skip_reward_passive_luck`

Preserve one complete empty glass cabinet with exactly one interior shelf, one blank hanging tag, and exactly one gold lucky pendant.

## 107 — `super_material_spread`

Preserve exactly seven thick connected honeycomb cells: one amber center and exactly six teal surrounding cells, including the same tiny single connection accent.

## 108 — `count_as_cake`

Preserve the rose-gold adjustable ring and clamp, exactly three large savory food pieces, and exactly one continuous cream piping ring.

## 109 — `super_fragment_reward`

Preserve the blank accordion ticket, one square tabletop, and exactly four fully visible teal table legs in the same front-facing layout.

## 110 — `settle_permanent_flat_all`

Preserve the full mahogany-and-brass mechanical scoreboard, chef-hat crest, blue-core side knob, blue base, and exactly three flip panels showing star—plate—star.

## 111 — `same_base_mult_all`

Preserve exactly two completely identical peacock-blue service bells on one shared brass-and-cream base.

## Post-processing

Use the installed ImageGen `remove_chroma_key.py` helper with border auto-key, soft matte, and despill; use edge-contract 1 only if inspection shows a visible magenta fringe. Normalize to 512×512 RGBA, alpha longest axis approximately 400 px, centered with every margin at least 40 px. Inspect every final at 80 px and 64 px.
