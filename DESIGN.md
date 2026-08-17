# TrailGuard — Design System

A record of the visual decisions we've made. Not a style guide written up front — it grows as we decide things.

**How to use this:** before styling anything, check if it's covered here. If it isn't, decide it, build it, then write it down. That last step is the one that matters — the inconsistency we're fixing came from decisions that were made but never recorded.

---

## Approach

We're working **component-first**, not page-first.

The earlier attempt went page by page, which meant re-deciding button styling while looking at each page — and getting a slightly different answer every time. Fixing the shared components first means most pages correct themselves.

Order:
1. Buttons (primary, secondary, destructive, ghost)
2. Cards
3. Badges and pills
4. Form inputs
5. Section headers
6. Modals
7. Empty states
8. Then page-specific work

---

## Color

### Brand gradient

```
orange → pink → violet
from-orange-500 via-pink-500 to-violet-500
```

Used on primary buttons and the hero heading. This is the identity — it stays.

### Accent

```
violet-500  #8b5cf6
```

The solid counterpart to the gradient, for anything too small for a gradient to read on: links, active nav items, focus rings, icons, section eyebrow labels, progress bars.

Gradient for large surfaces, accent for small ones. Both come from the same family, so they read as one brand.

### Surfaces

```
#000714   page background
#030816   raised section
#0B1325   card
white/10  borders
```

Use the theme tokens (`bg-surface-base`, `bg-surface-raised`, `bg-surface-card`),
never the raw hex. The tokens exist so the palette can change in one place, and
Tailwind IntelliSense flags the hex form as a warning on every occurrence.

### Text

```
white        primary
slate-300    secondary
slate-400    muted
slate-500    disabled
```

### Difficulty

Dark, near-solid backgrounds so they stay readable over any photograph — an
18%-opacity badge disappears against a bright sky.

| Level | Background | Text | Border |
|---|---|---|---|
| Easy | `rgb(6 78 59 / 0.85)` | `rgb(110 231 183)` | `rgb(52 211 153 / 0.35)` |
| Moderate | `rgb(69 39 8 / 0.85)` | `rgb(252 211 77)` | `rgb(251 191 36 / 0.35)` |
| Hard | `rgb(69 10 10 / 0.85)` | `rgb(252 165 165)` | `rgb(248 113 113 / 0.35)` |

### Suitability and status

*To be decided when we get to badges.*

---

## Components

### Form inputs
block w-full px-4 py-3 rounded-xl
bg-surface-card border border-gray-700
text-white placeholder-gray-500
focus:outline-none focus:border-accent focus:ring-1 focus:ring-accent
transition-colors
Labels sit above with `mb-2`, styled `text-sm font-medium text-gray-300`.

Inputs with a leading icon use `pl-11` and an absolutely-positioned icon at
`pl-4`. Password fields add a visibility toggle at `pr-4` on the right.

`focus:outline-none` matters — without it the browser draws its own outline on
top of the ring, and you get a doubled border.

### Form buttons

Submit buttons can't use `_PrimaryButton` (it renders an `<a>`, not a
`<button>`), so they're hand-rolled — but they match it exactly:
w-full flex justify-center py-3.5 px-4 rounded-full
text-sm font-semibold text-white
bg-linear-to-r from-orange-500 via-pink-500 to-violet-500
hover:brightness-110 hover:scale-[1.02]
focus:outline-none focus:ring-2 focus:ring-accent focus:ring-offset-2 focus:ring-offset-surface-base
transition duration-200 ease-out

### Primary button

- **Shape:** capsule (`rounded-full`)
- **Fill:** brand gradient (`bg-linear-to-r from-orange-500 via-pink-500 to-violet-500`)
- **Text:** white, `font-semibold`
- **Hover:** `hover:brightness-110 hover:scale-[1.02]`
- No shadow or glow — the gradient is enough weight on its own

### Secondary button

- **Shape:** capsule (`rounded-full`)
- **Fill:** transparent
- **Border:** `border-white/20`
- **Text:** `text-slate-400`
- **Hover:** border and text brighten

Deliberately quiet next to the primary — the two shouldn't compete, and the hierarchy should be obvious at a glance.

### Destructive button

*To be decided.*

### Everything else

*To be decided as we build it.*

### Modals

- **Always render inside `@section Modals`, never inline in the page body.** The layout puts `@RenderSectionAsync("Modals")` at the very end of `<body>`, after the footer — anything rendered there sits above everything else in the DOM. A modal left inline only stacks correctly within whatever ancestor it happens to be nested in.
- **Both the wrapper and the backdrop use `fixed inset-0`.** An `absolute` backdrop only covers its nearest positioned ancestor — if that ancestor is a `<div>` partway down the page, the blur stops there and the navbar and footer stay sharp behind the modal.
- **`z-60` sits above the navbar's `z-50`.** Apply it to the wrapper, the backdrop, and the panel.
- **`z-100` is not a real Tailwind class.** The default scale stops at `z-50`. `z-100` compiles to nothing, so the class silently does nothing and the element falls back to normal stacking order — the modal renders behind whatever the navbar's `z-50` already claims.

Structural reference:

```html
@section Modals {
<div id="exampleModal" class="fixed inset-0 z-60 hidden items-center justify-center p-4">
    <div class="fixed inset-0 bg-black/70 backdrop-blur-sm z-60" onclick="closeModal()"></div>
    <div class="relative bg-surface-card border border-white/10 rounded-xl shadow-2xl w-full max-w-lg max-h-[85vh] overflow-hidden flex flex-col z-60">
        <!-- modal content -->
    </div>
</div>
}
```

---

## Motion

Hover feedback should be felt, not watched.

**Buttons:** `hover:scale-[1.02]` with `hover:brightness-110` — enough to feel
responsive, small enough not to shift the layout. `scale-105` was too much.

**Cards:** border color change only (`hover:border-accent/50`). No scale, no glow.

**Section reveals:** fade up on scroll, one-time. Grouped items stagger at 150ms
intervals so sequence reads as sequence.

**Hero:** staggered fade-up on load — badge, heading, paragraph, button, arrow —
at 120ms intervals.

`duration-200` for hover, `duration-600` for reveals, `ease-out` throughout.

Every animation respects `prefers-reduced-motion`.

---

## Radius

Two values. `rounded-3xl`, `rounded-4xl`, and arbitrary `rounded-[...]` are out.


---

## Known Cleanup

- **`_SecondaryButton.cshtml` is empty.** Every secondary button in the app is hand-rolled, which is a large part of the inconsistency. Filling this in is the single highest-leverage fix available.
- **A fourth difficulty level that doesn't exist.** `"Technical"` appears in `AssessmentController.GetResult`, `GetAlternativeEvents`, and several view conditionals, but `DifficultyCalculator` only ever returns Easy, Moderate, or Difficult. Dead code from an earlier design — harmless today, misleading to read.
- **`bg-[#0A1122]`** on the landing page doesn't match any of the three documented surface tokens. Either it should be one of them, or it's intentional and needs a name.
- **Two different modal show/hide mechanisms.** `Trails.cshtml` uses custom `.modal-hidden`/`.modal-visible` CSS classes; `MyRegistrations.cshtml` uses Tailwind's `hidden`/`flex`. Standardize on the Tailwind approach and drop the custom CSS. Not urgent, but it's the same inconsistency this pass exists to remove.

---

## Pages Done

- **Landing page** — hero, about, popular trails carousel, how it works
- **Login** — split layout, form card on the right
- **Register** — mirrored split, form card on the left