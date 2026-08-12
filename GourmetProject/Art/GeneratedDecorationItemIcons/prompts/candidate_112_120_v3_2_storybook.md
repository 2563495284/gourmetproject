# V3.2 storybook style-transfer prompts — items 112–120

Generation mode: built-in ImageGen, one independent `style-transfer` edit per asset. Image 1 is the approved V3.2 edit target; Image 2 is the shared storybook/gouache style reference. This is a light rendering-style migration, never a redesign.

## Shared invariants and art direction

- Change only the rendering treatment. Preserve Image 1's exact subject identity, composition, framing, viewpoint, perspective, silhouette, geometry, proportions, placement, object inventory, functional structure, hard counts, recognisable palette, and approved V3.2 detail density.
- Apply Image 2's flat storybook illustration treatment: opaque gouache-like paint, visible hand-painted brush texture inside colour areas, slightly irregular hand-drawn deep-brown outlines, large matte colour planes, restrained simple block shadows, and very limited non-glossy highlights.
- Do not make it smoother, glossier, more 3D-rendered, more ornate, more minimal, or more icon-symbol-like. Do not add, remove, merge, split, replace, move, or geometrically redesign any object or part.
- Keep all necessary interior separations legible at 64–80 px. No text, numbers, logo, watermark, cast shadow, contact shadow, floor, room, scenery, particles, halo, or anthropomorphism.
- Background is perfectly uniform pure `#ff00ff` all the way to every canvas edge, without gradient, lighting variation, noise, texture, vignette, reflection, or shadow. Do not use `#ff00ff` in the subject.

## 112 — `remove_arrow_cookie_2`

Lightly restyle the approved V3.2 recycling-box icon only. Preserve the same rounded sage-green box, cream front panel, coral-gold latch, gold side hinge, deep top opening, exactly two orange arrow-shaped cookies half inserted, and exactly one green recycling-loop emblem. Keep the same three-quarter angle, proportions, placement, five-colour identification, and detail density. Change only the paint treatment to matte storybook gouache with visible brushwork, slightly irregular dark-brown outlines, broad flat colour shapes, simple block shadows, and fewer glossy highlights. Hard count: exactly two arrow cookies and one recycling-loop emblem.

## 113 — `remove_arrow_cookie_all`

Lightly restyle the approved V3.2 grinder icon only. Preserve the same complete deep-red grinder body, steel funnel, gold trim, lower open crumb drawer, side crank and red handle, exactly four orange arrow cookies visible in the funnel, and the same small amount of cookie pieces in the drawer. Keep its three-quarter perspective, geometry, placement, palette, and detail density unchanged. Change only the rendering to matte hand-painted storybook gouache with visible brush texture, irregular deep-brown outlines, simplified block shading, and reduced mirror-like metal highlights. Hard count: exactly four funnel cookies.

## 114 — `randomize_recipe_dishes`

Lightly restyle the approved V3.2 menu-board icon only. Preserve the same tilted purple board on the same wooden easel, exactly six radial food tiles, their same dumpling/cake/soup/bread/fruit/fish contents and positions, the same gold dividers, and exactly one central violet prism. Keep identical framing, perspective, geometry, proportions, palette, and detail density. Change only the paint treatment to flat opaque storybook gouache, visible hand-painted strokes, slightly irregular deep-brown outlines, matte colour blocks, and simple block shadows. Hard count: exactly six food tiles and one central prism.

## 115 — `discard_dish_flat`

Lightly restyle the approved V3.2 open improvement-book icon only. Preserve the same open two-page thick brick-red book, exactly four gold outer corner protectors, the same left chipped plate with coral fork, the same right simple orange main dish with gold upward step-arrow, and the same red pencil at the bottom. Preserve the no-vegetable/no-garnish state, composition, perspective, geometry, palette, and detail density. Change only the rendering to matte opaque storybook gouache with paper/leather brushwork, hand-drawn deep-brown contours, broad flat colours, and restrained block shadows. Hard count: exactly four corner protectors; no vegetables or garnish.

## 116 — `heart_capacity`

Lightly restyle the approved V3.2 heart candlestick icon only. Preserve the same complete tall gold pedestal, blue base band and diamond, exactly one coral-red heart candle, and exactly one flame. Keep the identical silhouette, viewpoint, proportions, placement, palette, and detail density. Change only the paint treatment to opaque matte storybook gouache with visible brush marks, slightly irregular deep-brown outline, large colour blocks, simple block shadows, and much less polished metallic/wax shine. Hard count: one heart and one flame.

## 117 — `heart_capacity_deluxe`

Lightly restyle the approved V3.2 enamel pedestal dish only. Preserve the same complete cream-and-gold high-foot dish, exactly one raised coral-red heart, and exactly one cobalt-blue scalloped border on the platter; keep the foot plain cream-and-gold. Preserve identical geometry, proportions, viewpoint, palette, placement, and detail density. Change only the rendering to matte hand-painted storybook gouache with visible strokes, irregular deep-brown contours, broad colour fields, and simple block shading; reduce glossy enamel/metal reflections. Hard count: one central heart and one blue scalloped border.

## 118 — `restore_heart`

Lightly restyle the approved V3.2 small wall first-aid box only. Preserve the same rounded ivory box, two wall tabs, one half-open door, gold hinges, pale teal interior, exactly one coral heart latch, and exactly one cream rolled bandage inside. Keep the same geometry, perspective, proportions, framing, palette, and detail density. Change only the paint treatment to matte opaque storybook gouache with visible brushwork, slightly irregular deep-brown outlines, broad flat shapes, simple block shadows, and fewer polished highlights. Hard count: exactly one heart latch and one bandage.

## 119 — `restore_hearts`

Lightly restyle the approved V3.2 large teal first-aid cabinet only. Preserve the same full cabinet and crown, two open doors, exactly one interior shelf/divider, exactly two coral heart medicine bottles, and exactly one cream rolled bandage. Keep the identical geometry, perspective, layout, palette, and V3.2 detail density. Change only the rendering to matte hand-painted storybook gouache with visible strokes, irregular deep-brown outlines, large colour planes, simple block shadows, and reduced enamel/glass shine. Hard counts: two heart bottles, one rolled bandage, one shelf.

## 120 — `slot_cost_discount`

Lightly restyle the approved V3.2 brass coin device only. Preserve the same complete thick circular brass body, navy lower housing and slot, exactly four circular sockets in the same positions, exactly three plain gold coins in three sockets, and exactly one empty dark socket. Keep identical perspective, proportions, geometry, placement, palette, and detail density. Change only the rendering to matte storybook gouache with visible brushwork, slightly irregular deep-brown outlines, broad flat colour areas, simple block shadows, and reduced metallic shine. Hard count: four sockets total, three filled and one empty.

## Targeted regeneration used

### 112 — solid-cookie correction

Regenerated once after the initial keyed result exposed transparency inside both cookies. The corrected prompt kept every shared invariant and added: both arrow cookies must be fully solid opaque baked-biscuit shapes, filled edge-to-edge with warm golden ochre/biscuit yellow and simple darker orange baking brush strokes; continuous deep-brown outlines; absolutely no transparent, hollow, cut-out, magenta, pink, purple, neon, or background-coloured area anywhere inside either cookie; no holes, cracks, internal gaps, or open shapes. The box and recycling emblem remained unchanged.

## Local background-removal note

The chroma source uses a magenta key. To preserve warm orange/red subject fills, the helper was used with border auto-sampling, a conservative hard-key tolerance, and one-pixel edge contraction. This avoids the magenta soft-matte helper's single-channel dominance rule incorrectly lowering alpha inside warm orange gouache areas while still removing the flat key and eliminating visible magenta fringe.
