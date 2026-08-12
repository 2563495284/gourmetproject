# Candidate items 94–102 — V3.2 storybook light style transfer

Mode: built-in `image_gen`; nine independent edit calls.

Input roles for every call:

- Image 1: corresponding approved V3.2 full item; sole edit target for subject, geometry, composition, perspective, object count and color identity.
- Image 2: supplied cake illustration; style reference only. Never copy its cake subject, green palette, plate or geometry.

Shared prompt:

```text
Use case: style-transfer
Asset type: transparent game UI restaurant-item icon
Primary request: apply a light-touch rendering-style migration to Image 1 only. Preserve Image 1's complete subject, exact composition, camera angle, perspective, silhouette, proportions, geometry, construction, object placement, hard counts, recognizable palette and current V3.2 detail density. Do not redesign, simplify structurally, add, delete, merge or move any element. Use Image 2 only for its rendering language.
Scene/backdrop: isolate the unchanged object on a perfectly flat, uniform solid #ff00ff chroma-key background for local background removal.
Style/medium: gently translate the existing V3.2 art into Image 2's flat storybook illustration and opaque gouache feel: visible dry-brush and painted-stroke texture within surfaces, slightly irregular hand-drawn deep-brown contour, broad matte color areas, simple block shadows, restrained edge highlights, less glassy/3D-plastic specular shine. Keep enough painted dimensionality that the result remains a complete physical restaurant prop, never a flat functional symbol.
Color/material invariants: preserve Image 1's existing color identity and material distinctions; do not inherit Image 2's green cake colors. Keep 4–5 coordinated color groups where already present. Retain the same level of structural detail; only change the brushwork, surface finish and line character.
Composition/framing: exactly match Image 1's framing, centered placement and negative space; object fully visible and uncropped.
Constraints: change the rendering style only. Pixel-for-pixel geometry is not required, but every component, count, tier, slot, tile, medallion, rivet, accessory and relationship must remain semantically identical to Image 1. Background must be one uniform #ff00ff field with no shadow, gradient, texture, reflection, floor plane or lighting variation. Do not use #ff00ff anywhere in the subject. No text, numbers, logo or watermark.
Avoid: new or deleted objects, count changes, perspective change, silhouette change, geometry redesign, composition drift, extra ornament, reduced detail density, V2 flat pictogram, photorealism, glossy plastic/metal reflections, smooth vector finish, background scene, cast shadow, contact shadow and particles.
```

Item-specific invariants appended to the shared prompt:

## 94 — transfer_extra_targets

```text
Preserve Image 1's actual candy inventory and placement exactly, without imposing a new count: the upper tray retains its blue wrapped candy, pink round candy, brown round candy and red heart candy; the lower tray retains its gold/orange wrapped candy, red heart candy, brown round candy, blue wrapped candy and cream round candy with red dot. Preserve exactly two circular serving tiers, one upper and one lower; central gold spindle and fluted pedestal; top red-and-white peppermint finial; exactly two outward-flying gold star candies, one left and one right. Strictly preserve the pink/coral trays, gold spindle, cream base and the red, pink, blue, orange, brown and cream candy colors. Never inherit olive green, black-gray or muted green from Image 2.
```

## 95 — flavored_flat

```text
Preserve the freestanding arched ceramic plaque, thick circular display area, gold pedestal base, exactly three large colored flavor medallions, exactly one coral jar and exactly one potted herb sprig. Preserve their positions and palette.
```

## 96 — flavored_count_as

```text
Preserve exactly two circular tasting tiers/plates, central gold post and foot, two sauce pools on the upper tier and one sauce pool on the lower tier. Preserve their coral, gold-orange and teal colors and exact placement.
```

## 97 — active_count_flat_all

```text
Preserve the freestanding clipped board and base, exactly three large supply pictograms—coral bottle, blue tongs and gold packet—and exactly two teal status dots. Preserve all positions.
```

## 98 — empty_active_slot_mult_all

```text
Preserve the open purple-blue cabinet, two doors, exactly three shelf slots total, exactly one occupied upper slot containing the coral service bell and exactly two empty slots below. Do not add cabinet contents, shelves or hardware.
```

## 99 — edge_flat

```text
Preserve the rolled cream tablecloth, teal roll band, continuous coral piping and exactly one gold corner clasp. Preserve the roll direction, unfurled corner and 3/4 perspective.
```

## 100 — non_edge_mult

```text
Preserve the complete circular central table, cream tabletop, teal center turntable, single coral vase with exactly two teal leaves, dark-wood pedestal and gold foot. Preserve all geometry and placement.
```

## 101 — gold_per5_flat_all

```text
Preserve the thick coral wooden menu frame, blank cream panel, pedestal foot and exactly five large gold coin rivets at top-center, upper-left, upper-right, lower-left and lower-right. No extra or missing rivet; panel remains blank.
```

## 102 — empty_cell_flat_all

```text
Preserve the thick gold frame and exact 3x3 cell geometry: exactly eight raised cream tiles plus exactly one recessed blue-violet center empty slot. No extra or missing tile, no relocated empty slot.
```
