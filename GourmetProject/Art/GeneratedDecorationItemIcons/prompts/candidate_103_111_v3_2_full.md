# Candidate 103–111 — V3.2 full production prompts

Built-in ImageGen edit workflow. For each generated icon:

- Image 1: its V1 final icon, used as the edit target and semantic/structural anchor.
- Image 2: approved V3.2 `transfer_extra_targets.png`, used only as a style and detail-density anchor.
- Image 3: approved V3.2 `settle_permanent_flat_all.png`, used only as a style and detail-density anchor.
- Keep Image 1's unique object; do not import objects from Images 2 or 3.
- Render on a perfectly uniform chroma-key background, then remove the key locally with the installed ImageGen helper.

## Shared production specification

```text
Use case: precise-object-edit
Asset type: restaurant game UI item icon
Primary request: Simplify Image 1 to the approved V3.2 detail level while preserving it as a complete, tangible restaurant prop rather than a flat function symbol.
Style anchors: Images 2 and 3 define the target amount of structure, dimensionality, warm cartoon thick-paint finish, broad highlights, and coarse dark outlines only. Do not copy their subjects.
Detail target: preserve all primary construction, recognizable materials, strong dark-brown outline, three-dimensional volume, and 4–5 coordinated color groups. Remove about 35–45% of V1's tiny textures, repeated borders, fragmented accessories, seams, rivets, and repeated highlights. Each large surface gets at most one broad highlight. Do not simplify to the rejected V2 flat-symbol level.
Composition: one complete object centered, fully visible, alpha silhouette longest axis normalized to about 400 px at final 512×512, visually centered, with at least 40 px clear padding on every side.
Constraints: no words, letters, numbers, logos, watermark, background scene, floor, cast shadow, contact shadow, reflection, extra props, or personification. Preserve every item-specific structure and exact count below.
```

## 103 — `unused_discard_mult_all`

```text
Keep the complete teal A-frame freestanding rack and exactly three thick, neatly rolled unused charcoal garbage-bag rolls, one per tier. Preserve the handle and four grounded feet as major construction. Simplify bars into clean large tubes. Remove connector caps, bolts, buckles, tiny seams, roll-film wrinkles, and repeated highlights. Use a perfectly flat uniform solid #ff00ff chroma background; do not use #ff00ff in the object.
```

## 104 — `unused_discard_flat_all`

```text
Keep one complete red horseshoe-magnet base, one large silver clamp jaw/mechanism, and exactly one neat stack of folded unused bags held by the clamp. Preserve the sturdy three-dimensional tabletop-device construction. Remove screws, teeth, tiny hinge grooves, redundant base edging, bag wrinkles, and repeated metallic highlights. Use a perfectly flat uniform solid #00ff00 chroma background; do not use #00ff00 in the object.
```

## 105 — `skip_reward_dish_luck`

```text
Keep one complete empty cream-white restaurant plate, one blank folded-corner receipt pressed under/on the plate, and exactly one green four-leaf-clover wax seal attached at the plate rim. The paper must remain entirely blank. Remove multiple rim rings, decorative flourishes, sparkle effects, paper texture, and repeated highlights. Use a perfectly flat uniform solid #ff00ff chroma background; do not use #ff00ff in the object.
```

## 106 — `skip_reward_passive_luck`

```text
Keep one complete empty miniature glass display cabinet with a single empty interior shelf/level, one blank hanging price tag, and exactly one small gold lucky pendant. Preserve clear cabinet thickness, glass front, door frame, feet and tangible furniture volume. Remove ornate cabinet flourishes, extra shelves, tiny hinges, drawer trim, knots, beads, texture, and repeated highlights. The tag must remain blank. Use a perfectly flat uniform solid #00ff00 chroma background; do not use #00ff00 in the object.
```

## 107 — `super_material_spread`

```text
Keep one thick connected honeycomb table mat made of exactly seven hexagonal cells: one amber center cell surrounded by exactly six teal cells. All seven cells must be fully visible and physically connected. Add only one small simple flowing connection accent between adjacent cells, not magic rays or a background effect. Remove all cell-interior pebble/leather texture, stitches, dots, repeated rims, and repeated highlights. Use a perfectly flat uniform solid #ff00ff chroma background; do not use #ff00ff in the object.
```

## 108 — `count_as_cake`

```text
Keep one complete adjustable rose-gold cake-ring mold with its visible clamp/fastener. Inside the ring, show only 2–3 large unmistakably different savory food pieces, unified by exactly one continuous ring of cream piping. Preserve the substantial circular mold and dimensional food volume. Remove tiny garnish, crumbs, sprinkles, diced toppings, repeated piping detail, surface speckles, and repeated metal highlights. No faces. Use a perfectly flat uniform solid #00ff00 chroma background; do not use #00ff00 in the object.
```

## 109 — `super_fragment_reward`

```text
Keep one accordion-fold ticket/coupon with 2–3 clear broad folds, transforming into or supporting exactly one popped-out square restaurant table. Use a mostly straight-on product-display angle so the leg count is unambiguous: exactly four separate teal leg shafts are fully visible beneath the tabletop—far-left, center-left, center-right, far-right—with clear background gaps between them. The shallow top plane must still read as a square tabletop. Preserve the clever single-prop construction and three-dimensional unfolding form. Remove ticket patterns, perforations, tiny hinges, screws, repeated edge bands, and repeated highlights. All ticket and tabletop surfaces are blank. Use a perfectly flat uniform solid #00ff00 chroma background; do not use #00ff00 in the object.
```

## 110 — `settle_permanent_flat_all`

No new generation. Reuse the approved V3.2 sample source byte-for-byte: complete dimensional mahogany-and-brass mechanical scoreboard, three cream flip panels with star—plate—star, chef-hat crest, blue-core side knob, and blue base. The production final receives only the same alpha-bounds scale/centering normalization as the rest of this batch; the artwork itself is not regenerated or redesigned.

## 111 — `same_base_mult_all`

```text
Keep exactly two completely identical peacock-blue service bells, equal in shape, size, color and orientation, mounted on one shared brass-and-cream double base. Preserve both full domes, two identical brass push buttons, and tangible three-dimensional shared tabletop base. Remove extra base tiers, decorative flourish, micro-bevel rings, and repeated highlights. Each bell dome gets one broad highlight only. Use a perfectly flat uniform solid #00ff00 chroma background; do not use #00ff00 in the object.
```

## Post-processing

Use `remove_chroma_key.py --auto-key border --soft-matte --transparent-threshold 12 --opaque-threshold 220 --despill`. Resize and normalize to 512×512 RGBA so the alpha bounding box longest axis is approximately 400 px, centered, with every margin at least 40 px. Inspect each final at 80 px and 64 px.
