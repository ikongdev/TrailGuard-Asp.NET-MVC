# TrailGuard — Design System

A living record of the visual decisions made for TrailGuard. Not a style guide written up front — it grows as the system is built.

**How to use this:** before styling anything, check whether the pattern is covered here. If it isn't, decide it, build it, then write it down. That last step is what stops the same decision being made differently on different pages.

---

## Approach

**Component-first**, not page-first. The earlier page-by-page attempt meant re-deciding button styling on every page and getting a slightly different answer each time.

Preferred order:

1. Buttons
2. Cards
3. Badges and pills
4. Form inputs
5. Section headers
6. Modals
7. Empty states
8. Status indicators
9. Assessment / explainability components
10. Page-specific work

---

## Color

### Brand gradient

```
from-orange-500 via-pink-500 to-violet-500
```

Used on **primary buttons and the hero heading only**. This is the identity — the gradient reads as special because it appears rarely. Applied to five things it becomes wallpaper.

### Accent

```
violet-500   #8b5cf6
```

The solid counterpart, for anything too small for a gradient to read on: links, active nav, focus rings, icons, section eyebrow labels, progress bars, selected controls, section number badges.

```
Large surface  → brand gradient
Small element  → accent
```

**Gotcha:** Tailwind doesn't generate `accent-color` utilities from theme tokens. `accent-accent` requires an explicit rule in `input.css`:

```css
@utility accent-accent {
    accent-color: var(--color-accent);
}
```

### Colors that carry no meaning

Don't introduce one. Every colour should answer "what does this tell me?" If the answer is "nothing, it looked nice," use a surface or text colour instead.

This is why the three "How it Works" icons are all accent rather than orange/violet/pink, and why the progress bars in Progress & Achievements are all accent — three metrics that aren't different in kind shouldn't be three different colours.

---

## Surface Tokens

```
bg-surface-base     page background
bg-surface-raised   raised section
bg-surface-card     card
```

**Never use the raw hex** (`#000714`, `#030816`, `#0B1325`). The tokens exist so the palette can change in one place, and Tailwind IntelliSense flags every hex occurrence as a warning.

Markdown files must be excluded from Tailwind content scanning:

```css
@source not "../../*.md";
```

Without this, example text in these design docs gets compiled as real classes — `rounded-[...]` from a sentence describing what *not* to use produced invalid CSS that broke the build.

---

## Text

```
white        primary
gray-300     secondary
gray-400     muted
gray-500     disabled
```

The code uses `gray-*` throughout. This document previously recorded `slate-*`, which was never true of the app — the doc was corrected to match the code rather than the code swept to match the doc. Tailwind's `gray` and `slate` differ only in a slight blue cast, and a whole-app find-and-replace to fix a cosmetic mismatch nobody can see is not worth the diff.

`slate-*` still appears in a handful of places (`Register.cshtml` secondary buttons, empty-state icons). Convert them when you're already editing the file, not as a task of its own.

---

## Cards

The pattern actually used across the app:

```
bg-white/5 backdrop-blur-xl border border-white/10 rounded-xl
```

For nested or inset cards inside another card:

```
bg-white/5 border border-white/5 rounded-xl
```

`bg-surface-card` is used where a solid, non-translucent surface is needed — modals, dropdown option backgrounds, and inputs.

**Cards don't scale on hover.** Clickable cards use `hover:border-white/20`.

**Only the real thumbnail image zooms.** Trail Management's `group` + `group-hover:scale-105` (`transition-transform duration-500 ease-out`, `motion-reduce:transform-none motion-reduce:transition-none`) on the `<img>` itself is the approved pattern — Browse Trails (`Participant/Trails.cshtml`) and Browse Events both inherit it on their cards. Fallback artwork (the decorative mountain/image icon shown for a missing or broken thumbnail) is a sibling of the `<img>`, not a descendant of anything with `group-hover:scale-105`, so it never zooms.

**A card's action row reflects what the record actually permits, not a fixed template.** Event Management's card (`Views/Event/Index.cshtml`) renders `[View] [Edit] [Delete]` for a mutable Event, but a `Completed` Event — an immutable historical record, see CLAUDE.md, Event Lifecycle — renders `View` alone, full-width (`w-full` in place of `flex-1`, same secondary styling/height), with Edit and Delete not rendered at all rather than hidden or disabled. This is a server-rendered condition per card (`eventItem.Status == "Completed"`), not a CSS/JS toggle — the server independently enforces the same rule regardless of what the client renders.

`Registration/Register.cshtml`'s right column (Assessment Result, Event & Trail Details, Action) is now three cards of the same canonical `bg-white/5 backdrop-blur-xl border border-white/10 rounded-xl` family above — the Action card previously used an inconsistent `bg-accent/10` fill, and Event & Trail Details previously wrapped its content in a second `bg-surface-card/50` inset box that matched neither the canonical nor the nested-inset (`bg-white/5 border border-white/5`) recipe above. That inset box was removed outright rather than corrected to the nested recipe — its content now renders directly inside the outer card.

---

## Difficulty

Trails no longer carry a raw mountaineering number on their own — the badge shows a PinoyMountaineer-derived band name plus the PM level range it corresponds to, computed by `DifficultyCalculator` from the NPS-based adjusted rating. Four bands, not three:

| Band (`DifficultyCalculator.Bands`) | PM level | Badge class |
|---|---|---|
| Easy | 1–2/9 | `badge-easy` |
| Minor Climb | 3–4/9 | `badge-lime` |
| Major Climb | 5–6/9 | `badge-orange` |
| Major Climb — Difficult | 7–9/9 | `badge-hard` |

Badge colours — dark and near-solid, because an 18%-opacity badge disappears against a bright sky:

| Class | Background | Text | Border |
|---|---|---|---|
| `badge-easy` | `rgb(6 78 59 / 0.85)` | `rgb(110 231 183)` | `rgb(52 211 153 / 0.35)` |
| `badge-lime` | `rgb(26 46 5 / 0.85)` | `rgb(190 242 100)` | `rgb(163 230 53 / 0.35)` |
| `badge-orange` | `rgb(67 20 7 / 0.85)` | `rgb(253 186 116)` | `rgb(251 146 60 / 0.35)` |
| `badge-hard` | `rgb(69 10 10 / 0.85)` | `rgb(252 165 165)` | `rgb(248 113 113 / 0.35)` |

`DifficultyCalculator.BadgeClass` is the single source for this mapping — every page shows a difficulty badge through it. **Don't introduce a fifth band** without changing the calculator, `acsm_gate.py`'s matching Python-side bands, and this document together.

`.badge-moderate` still exists in `input.css` but isn't part of this mapping — it's not orphaned, though: the landing page's static trail showcase (`Home/Index.cshtml`) hand-writes difficulty text like "Moderate 4/9" independent of `DifficultyCalculator`, and still uses it. Leave it until that showcase is rebuilt to pull real trail data.

---

## Suitability

Deliberately echoing the difficulty palette so the two read as related:

| Result | Colour |
|---|---|
| Good-Match | `text-emerald-400` on `bg-emerald-500/15` |
| Borderline | `text-amber-400` on `bg-amber-500/15` |
| Not Recommended | `text-red-400` on `bg-red-500/15` |

Donut charts use the solid values: `#34d399`, `#fbbf24`, `#f87171`.

The result should be recognisable at a glance, but must not visually imply an automatic approval or rejection. The organizer decides.

### Gate reason

When the ACSM gate has overridden the model's own label (`SuitabilityResult.GateApplied`), the reason is shown in an amber note, not folded silently into the result:

```
bg-amber-500/10 border border-amber-500/30 rounded-lg p-3
text-amber-300 text-sm
```

`fa-shield-halved` icon. Used on the assessment report (`Assessment/Report.cshtml`) wherever `Model.GateApplied` is true.

### Missed-risk highlight

`Organizer/EventComparison.cshtml` marks a row where the final outcome was worse than what was predicted (`item.IsMissedRisk`) with a left-border accent rather than a full badge, since the row's other columns already carry the actual labels:

```
bg-red-500/5 border-l-2 border-l-red-500
```

This is the one place red marks a *comparison* result rather than the Not Recommended label itself — worth remembering before reusing red for something else on that page.

---

## Registration Status

| Status | Colour |
|---|---|
| Pending | `text-amber-400 bg-amber-500/15` |
| Awaiting Payment | `text-amber-400 bg-amber-500/15` |
| For Payment Verification | `text-blue-400 bg-blue-500/15` |
| Accepted | `text-emerald-400 bg-emerald-500/15` |
| Alternative Recommended | `text-blue-400 bg-blue-500/15` |
| Rejected | `text-red-400 bg-red-500/15` |
| Cancelled | `text-slate-400 bg-slate-500/15` |
| Voided | `text-slate-400 bg-slate-500/15` |

Amber means "something is waiting on you." Blue means "waiting on someone else." That consistency matters more than the specific hues.

**Don't rely on colour alone.** States that need action — Awaiting Payment, For Payment Verification, Voided, Alternative Recommended — carry a supporting strip with explanatory text and, where relevant, the action itself.

### Medical clearance badge

On the organizer's registration list (`Organizer/Registrations.cshtml`), a small pill next to a registration that requires clearance:

```
inline-flex items-center gap-1 text-[10px] px-2 py-0.5 rounded-full font-medium
```

Red (`bg-red-500/20 text-red-400`) reading "Clearance missing" when nothing's been uploaded yet; blue (`bg-blue-500/20 text-blue-400`) reading "Clearance required" once it has. `fa-file-medical` icon. Blue here means the same thing it means in the status table above — waiting on someone else, in this case the organizer's review, not the participant.

## Weather Risk

| Level | Colour |
|---|---|
| Low | `text-emerald-400 bg-emerald-500/15` |
| Moderate | `text-amber-400 bg-amber-500/15` |
| Moderate to High | `text-orange-400 bg-orange-500/15` |
| High (Thunderstorm) | `text-red-400 bg-red-500/15` |

Unavailable states are **muted grey and non-clickable**, never red. A forecast that doesn't exist yet isn't an error the participant can act on.

---

## Participant vs Organizer

The participant-facing interface uses the **accent** treatment throughout — the same violet as the rest of the app.

The organizer's assessment explanation panel currently uses **blue accents**, inherited from when it was built. That distinction is worth revisiting during the organizer UI pass: now that the accent is violet, blue and violet sit close together, and the separation is weaker than it was when the participant side was orange.

What does hold is the **wording** difference:

| | Participant | Organizer |
|---|---|---|
| Heading | Why This Result? | Assessment Explanation |
| Positive | Helped | Supported |
| Negative | Reduced | Weakened |
| Extra | — | Decision-support disclaimer |

---

## Navbar

Three zones in one floating capsule: logo (left), nav links (center, desktop only), profile/auth actions (right, desktop only) — plus a hamburger that replaces the whole center-and-right area below `lg`.

```
w-full lg:w-fit lg:max-w-full mx-auto
rounded-full border border-white/15 bg-[#060B1A]/55 backdrop-blur-md
```

`w-full` on mobile, so the capsule spans the viewport with the page's own side margin (`px-4` on the outer `<nav>`) rather than looking like a shrunken pill on a small screen. `lg:w-fit` on desktop, so it hugs its content instead of stretching edge to edge — a full-width bar at desktop size would fight the "floating capsule" identity the rest of the app uses for cards and modals.

**Profile dropdown and mobile menu share one pattern**, deliberately: the panel's own open/closed state is the single source of truth, driving the trigger's `aria-expanded`, the chevron rotation or icon morph, and the panel's own transition — rather than a separate click-counter that can desync once the menu closes via an outside click or `Escape`. Both close on outside click and `Escape`.

Profile chevron: `rotate-180` toggled with `transition-transform duration-200`, driven by whether the dropdown panel is open.

Mobile menu icon: the hamburger and the × cross-fade and rotate into each other (`transition-all duration-200`) rather than swapping instantly.

Mobile panel itself animates height via CSS grid (`grid-template-rows: 0fr` → `1fr`) rather than a fixed max-height — `height` and `max-height` can't transition to `auto`, and a fixed pixel guess breaks the moment the menu's content changes.

The TrailGuard navbar brand always links to the public Landing Page for every authentication state and role. Role-specific Dashboard navigation remains a separate explicit link.

---

## Buttons

### Primary

- Shape: `rounded-full`
- Fill: brand gradient
- Text: white, `font-semibold`
- Hover: `hover:brightness-110 hover:scale-[1.02]`
- **No shadow or glow** — the gradient is enough weight

**Solid-accent exception — no gradient, no hover scale.** `bg-accent hover:bg-accent/90` instead of the brand gradient, used where `hover:scale-[1.02]` would visibly shift a neighboring element: `Participant/Events.cshtml`'s Register/Upload Payment card CTA (`RegistrationButtonHelper.GetState`'s `Primary` style — a card's action row can be a different width across siblings, e.g. Alternative Recommended's single full-width button vs. everyone else's two-button row), the Participant Trail Details modal's "Browse All Events" footer action (`Participant/Trails.cshtml` — a fixed-width footer button next to Close), and `Registration/Register.cshtml`'s Submit Registration action (previously an orange-pink-violet gradient that stood out from the page's neutral card family — corrected to this same recipe). `Participant/Details.cshtml` (Participant Event Details) now uses this recipe for every primary action in its bottom strip — "View Recommended Event", "Register Now"/"Retake Assessment & Register", and "Give Feedback" all used to mix gradient and solid-accent on the same page; all three are solid-accent now, see Participant Event Details, above. Not a new third button variant, just this Primary shape without the scale/gradient.

### Secondary

Two treatments exist in the app today. **Variant A is canonical**; B is being retired.

**Variant A — filled quiet.** Canonical, and now the only treatment in use: `Participant/Details.cshtml`'s "View My Registrations" / registration-status action, `Participant/Events.cshtml` 193 (the card's "View" action — its Alternative Recommended "View Details" variant at line 185 is the same recipe), `Participant/Trails.cshtml` 141 and 215, `Registration/MyRegistrations.cshtml` 160 and 164, and `Registration/Register.cshtml`'s Cancel/Retake Assessment actions (migrated off Variant B below — the outer Action card's surface was also brought onto the same `bg-white/5 backdrop-blur-xl` family as the page's other right-column cards, replacing an inconsistent `bg-accent/10` fill).

```
rounded-full text-gray-300 hover:text-white
bg-white/5 hover:bg-white/10
border border-white/10 hover:border-white/20
transition-colors
```

**Variant B — outline only. Retired, 0 usages.** Formerly `Registration/Register.cshtml`'s Cancel and Retake Assessment actions — migrated to Variant A above, so no consumer remains. Recipe kept here only as a historical reference in case an old build or screenshot is compared against:

```
rounded-full text-sm font-medium text-slate-400
border border-white/20 hover:text-white hover:border-white/40
```

A wins on count, and it holds its own against a `bg-white/5` card where a transparent button disappears. Deliberately quiet next to the primary — the two shouldn't compete.

Disabled state, from `Participant/Details.cshtml`'s "Event Full" / "Registration Closed" / "Feedback Given" bottom-strip buttons: `text-gray-500` with `cursor-not-allowed` and no hover.

**Not yet built as a partial.** See Known Cleanup.

### Deactivate / Activate (reversible, not destructive)

A third semantic distinct from both Secondary and the red Delete treatment: **restrained amber for Deactivate**, **solid accent for Activate** — never gradient, never the trash icon, never Delete's red. Both are reversible, one-step actions and must not visually imply data loss the way Delete does. From `Trail/Index.cshtml`'s Trail Management cards and Deactivated Trails modal:

```
/* Deactivate (icon-only, sits with View/Edit/Delete on an Active card) */
bg-amber-500/15 hover:bg-amber-500/25
border border-amber-500/30 hover:border-amber-500/50
text-amber-400 hover:text-amber-300

/* Deactivate confirmation modal's confirm button (labelled, not icon-only) */
bg-amber-900/60 hover:bg-amber-800/80
border border-amber-500/40 hover:border-amber-400/60
text-amber-100 hover:text-white

/* Activate (Deactivated Trails modal row) */
bg-accent hover:bg-accent/90 text-white
```

Activate reuses the Solid-accent exception above (no gradient, no hover-scale) since it lives inside a modal list row, not a page-level primary action. The Deactivate confirmation modal itself follows the standard Modals shell but is deliberately not styled as a Delete-style destructive dialog — see Modals, Deactivate Trail / Deactivated Trails, below.

### Progress bars

```
w-full h-2 bg-gray-700 rounded-full overflow-hidden
```

with an inner div sized by inline `style="width: X%"`. All accent — see Color, on why several metrics of the same kind shouldn't be several colours.

### Destructive

Two treatments exist, for two different contexts — neither is a mistake to converge on the other.

**Compact / secondary destructive actions** — a delete icon-button in a list, or a status toggle next to other row actions. Quiet by default, solid red only on hover:

```
bg-red-500/20 hover:bg-red-500
border border-red-500/30 hover:border-red-500
text-red-400 hover:text-white
```

Used by the trail-delete button (`Trail/Index.cshtml`) and the admin account Disable/Enable toggle (`Admin/Accounts.cshtml`, green for Enable using the same recipe).

**Decision-panel destructive actions** — where Reject sits alongside Approve and Recommend Alternative as one of several equally-weighted calls to action, not a lesser one:

```
bg-red-500 hover:bg-red-600 text-white
```

Solid from the start, matching the solid green Approve and solid blue Recommend Alternative buttons beside it (`Organizer/RegistrationDetails.cshtml`). Making Reject the only outlined button in that group would read as less serious than its siblings, not more careful.

Either way: destructive actions must never use the brand gradient.

### Form submit buttons

Can't use `_PrimaryButton` — it renders an `<a>`, not a `<button>` — so they're hand-rolled but match it exactly:

```
w-full flex justify-center py-3.5 px-4 rounded-full
text-sm font-semibold text-white
bg-linear-to-r from-orange-500 via-pink-500 to-violet-500
hover:brightness-110 hover:scale-[1.02]
focus:outline-none focus:ring-2 focus:ring-accent
focus:ring-offset-2 focus:ring-offset-surface-base
transition duration-200 ease-out
```

---

## Form Inputs

```
block w-full px-4 py-3 rounded-xl
bg-surface-card border border-gray-700
text-white placeholder-gray-500
focus:outline-none focus:border-accent focus:ring-1 focus:ring-accent
transition-colors
```

Labels sit above with `mb-2`, styled `text-sm font-medium text-gray-300`.

Inputs with a leading icon use `pl-11` with the icon absolutely positioned at `pl-4`. Password fields add a visibility toggle at `pr-4`.

`focus:outline-none` matters — without it the browser draws its own outline over the ring and you get a doubled border.

### Selected states on option labels

```
border-gray-700 hover:border-accent
has-checked:border-accent has-checked:bg-accent/10
```

### File inputs

A file input with a selection needs a clear (×) button. Established in `MyRegistrations.cshtml` and the assessment registration form.

### Philippine contact numbers

Fixed `+63` prefix, participant enters the local number starting with 9, formatted `9XX XXX XXXX` as they type. The prefix is prepended before submission so the backend receives the full value unchanged.

Existing profile values containing `+63` or a leading `0` must be normalised before populating the field, or the prefix doubles.

### Custom-select listbox scrollbar

Every listbox `wwwroot/js/custom-select.js` generates — portalled (appended to `document.body`) or not — carries `.tg-custom-select-scrollbar`, defined once in `wwwroot/css/input.css`. This is the canonical, permanent TrailGuard convention:

> Scrollable TrailGuard custom-select menus must use the shared component-scoped slim dark scrollbar: transparent track, restrained accent thumb, rounded treatment, and no visible native arrow buttons. They must never fall back visually to a bright operating-system scrollbar inside the dark listbox.

```
scrollbar-width: thin;
scrollbar-color: rgb(139 92 246 / 0.65) transparent;   /* Firefox */

::-webkit-scrollbar { width: 6px; }                     /* Chromium/Edge/Safari */
::-webkit-scrollbar-track { background: transparent; }
::-webkit-scrollbar-thumb { background: rgb(139 92 246 / 0.65); border-radius: 9999px; }
::-webkit-scrollbar-thumb:hover { background: rgb(139 92 246 / 0.85); }
::-webkit-scrollbar-button { display: none; width: 0; height: 0; }
```

This replaced the menu's previous `custom-scrollbar` class — a generic, unscoped name eight pages each redefined locally (different colors/widths) inside their own `<style>` blocks. A portalled menu matched whichever one happened to belong to the currently-loaded page by accident, and a page with no local `.custom-scrollbar` rule at all (Browse Events, among others) fell all the way back to the native OS scrollbar. Six of those eight page-local `.custom-scrollbar` blocks are otherwise unrelated (modal photo grids, etc.) and remain as-is — only the custom-select menu's own class changed for them. The eighth, `Participant/Trails.cshtml`'s Trail Details modal body, was separately replaced during the Browse Trails pass with the id-scoped `#viewTrailModalBody` rule in `input.css` (see Modals, Participant Trail Details, below) — not because it fed the custom-select menu, but because the same "generic unscoped class" defect applied to it independently. The remaining one, `Registration/Register.cshtml`'s, was dead CSS (the class was never applied to any element on that page) and was deleted outright during the registration-page cleanup pass rather than left in place — see Cards, above, and the Register-page notes there.

**Never add a second, page-local scrollbar recipe for this component.** Extend `.tg-custom-select-scrollbar` in `input.css` instead — the class must stay defined in exactly one place, and `output.css` must stay generated (`npm run build`), never hand-edited.

`Registration/Register.cshtml`'s Pickup Schedule field is a `data-custom-select` consumer as of the registration-page visual pass — a native-looking select before that. It uses `data-custom-select-portal` (the Registration Form card is a `backdrop-blur-xl` ancestor, the same containing-block reason Event/Trail Management's filter bars need it) and the default dark form-trigger recipe (no `data-cs-trigger-class` override, since the field already matches that recipe). Server-side validation (`PickupScheduleHelper.FindCanonicalMatch` in `RegistrationController`) is unchanged by the swap — the enhancement only wraps the same bound `<select name="pickupPoint">` in an accessible proxy.

---

## Modals

Always render inside `@section Modals`, never inline in page content. The layout renders that section at the very end of `<body>`, after the footer, so the modal reliably sits above everything without z-index guesswork.

```razor
@section Modals {
    <div id="exampleModal" class="fixed inset-0 z-60 hidden items-center justify-center p-4">
        <div class="fixed inset-0 bg-black/70 backdrop-blur-sm z-60" onclick="closeModal()"></div>
        <div class="relative bg-surface-card border border-white/10 rounded-xl shadow-2xl
                    w-full max-w-lg max-h-[85vh] overflow-hidden flex flex-col z-60">
            <!-- content -->
        </div>
    </div>
}
```

Both wrapper and backdrop use `fixed inset-0`. An `absolute` backdrop only covers its parent, which leaves the navbar and footer unblurred.

`z-60` sits above the navbar's `z-50`. **`z-100` is not a real Tailwind class** — it compiles to nothing and silently breaks layering.

Toggle visibility with Tailwind's `hidden` / `flex`. Don't introduce a second mechanism.

### Participant Trail Details

`Participant/Trails.cshtml`'s Trail Details modal is a read-only Participant adaptation of the approved Trail Management View modal (`Views/Trail/Index.cshtml`): same `max-w-4xl`/`max-h-[calc(100vh-2rem)]` shell, `h-40 sm:h-56` hero with the same reusable-`<img>` + sibling-placeholder pattern, terrain chips, and Additional Photos gallery. It previously used `z-100` (the dead-class bug above) on the outer wrapper plus an inline `style="z-index: 10;"` on the inner panel, and the page-specific `.modal-hidden`/`.modal-visible` pair instead of `hidden`/`flex` — both fixed to match this page's own documented convention. It adds one Participant-only section neither Trail Management nor its C# model needs: "Upcoming Events on This Trail", fed by `ParticipantController.GetTrailEvents` and rendered through safe DOM construction (`document.createElement`/`textContent`), not the interpolated `innerHTML` template it used before. Its Additional Photos gallery calls a separate, narrow, Participant-authorized endpoint (`ParticipantController.GetTrailPhotos`, returning photo URL only) rather than the Admin/Organizer-only `Trail/GetTrailPhotos`. The footer's second action is "Browse All Events" (solid accent, see Buttons above) — never an Edit Trail action, which stays Organizer/Admin-only.

Technical Trail Class (`Class N — Label`, mirroring `DifficultyCalculator.TrailClassLabel`) and Terrain (chips) are kept visually and semantically separate — the modal never collapses them into one metadata string.

### Deactivate Trail / Deactivated Trails

Both live in `Trail/Index.cshtml`, opened through the same shared `TrailModal` open/close/focus-trap/inert instance the page's View/Add/Edit modals already use — no second, competing modal manager.

**Deactivate Trail** is a small (`max-w-md`) confirmation dialog, not a native `confirm()` and not styled like the Delete confirmation it sits next to conceptually — see Buttons, Deactivate / Activate, for why. It states plainly that the Trail will be hidden from Browse Trails and unavailable for new Events, that existing Events and historical records won't change, and shows the Total linked Event count plus (only when greater than zero) the Upcoming count with a note that Upcoming Events won't be cancelled or modified. Actions are `Keep Active` (Secondary Variant A) and `Deactivate Trail` (the amber treatment above).

**Deactivated Trails** is a `max-w-4xl` read-only list modal (same shell proportions as the View Trail modal), each row showing name, location, a `Deactivated` status pill, and per-status Event counts as a compact definition list — Upcoming/Completed/Cancelled always rendered even at zero, `Other` only when greater than zero, `Total Events` always last. No Edit or Delete action exists in this modal; the only control per row is `Activate`. Its scrollable body uses its own id-scoped scrollbar rule (`#deactivatedTrailsModalBody` in `input.css`), following `#viewTrailModalBody`'s existing pattern — never the page-local `.custom-scrollbar` class already defined for this page's other modals, and never `.tg-custom-select-scrollbar`, which is reserved for custom-select.js listbox menus.

---

## Banners and Alerts

The shared formula: `bg-{hue}-500/10 border border-{hue}-500/30`, an icon, and a short message. Four variants in use today — a fifth should pick a hue by what it means (red still means "blocks you," amber still means "worth knowing"), not invent a new visual recipe.

| Variant | Classes | Icon | Where |
|---|---|---|---|
| Red — required medical clearance | `bg-red-500/10 border border-red-500/30 rounded-xl p-4` | `fa-notes-medical` | `Assessment/Report.cshtml`, when clearance is required |
| Red — form error | `bg-red-500/10 border border-red-500/30 rounded-xl p-4` | `fa-triangle-exclamation` | `Assessment/Form.cshtml`, from `TempData["Error"]` |
| Amber — retake notice | `bg-amber-500/10 border border-amber-500/30`, pill not a full-width box | `fa-rotate` | `Assessment/Report.cshtml`, "Retake Assessment" |
| Amber — gate reason | `bg-amber-500/10 border border-amber-500/30 rounded-lg p-3` | `fa-shield-halved` | `Assessment/Report.cshtml`, see Suitability → Gate reason |

Red boxes use `rounded-xl p-4`; the amber gate-reason box uses the smaller `rounded-lg p-3` since it's a secondary note within a page that already has a headline result, not the first thing on the page. The retake notice is a pill-shaped link rather than a full banner — it's an action, not a warning.

---

## Motion

Hover feedback should be felt, not watched.

**Buttons:** `hover:scale-[1.02]` with `hover:brightness-110`. `scale-105` was too much — it moves the surrounding layout.

**Cards:** border colour change only. No scale, no glow.

**Section reveals:** fade up on scroll, one-time. Grouped items stagger at 150ms so a sequence reads as a sequence.

**Hero:** staggered fade-up on load — badge, heading, paragraph, button, arrow — at 120ms intervals.

```
duration-200   hover and focus
duration-600   reveals
ease-out       throughout
```

Every animation respects `prefers-reduced-motion`.

---

## Radius

Four values, each answering a different question — "how prominent is this container" — not interchangeable and not a matter of per-page taste:

```
rounded-full   buttons, badges, pills, avatars, search and filter inputs,
               primary/navigation actions
rounded-2xl    major page sections and primary content cards
               (Event Details' info/weather/description panels,
               the Profile hero and its top-level cards)
rounded-xl     nested list rows, achievement tiles, and secondary inner
               cards - a card-within-a-card, or a repeated row inside a
               larger rounded-2xl container
rounded-lg     compact action buttons (a small icon/text button that
               isn't a page's primary call to action)
```

`rounded-3xl`, `rounded-4xl`, and arbitrary `rounded-[...]` are still out. This replaces an earlier "two values only" version of this rule that required every card to be `rounded-xl` — that never matched `Event/Details.cshtml`'s top-level panels, which have used `rounded-2xl` since before this section was corrected. The hierarchy above is what's actually applied consistently: a page's major sections get `rounded-2xl`, and anything nested one level inside such a section (an achievement tile, a recent-adventure row, a small stat chip) steps down to `rounded-xl`.

---

## Empty States

Every list needs one. Icon, a plain statement, and where useful a link to the action that would fill it:

```
<i class="fa-regular fa-calendar-plus text-3xl text-slate-600 mb-3"></i>
<p class="text-slate-400 text-sm">No upcoming events yet.</p>
<a class="text-accent text-sm font-medium mt-2 hover:brightness-125">
    Browse events <i class="fa-solid fa-arrow-right ml-1"></i>
</a>
```

A blank area where content would be reads as broken.

---

## Assessment / Explainability UI

The assessment report is the centrepiece of the capstone and shouldn't look like a generic CRUD result page.

Information hierarchy:

```
Suitability Result → Confidence → Why This Result? → SHAP Factors → Recommendations
```

**Do not reintroduce** the legacy rule-based display:

```
Score: 31 / 44
Fitness / Experience / Health / Gear score bars
Required score threshold
Risk Flags
```

These were removed because they contradicted the ML result on the same screen — one showed Good-Match while the other said "below requirement." There is no rule-based fallback left to reintroduce them from, either: `GetResult()` was deleted, not just hidden. If the ML service is unreachable, the assessment produces no result at all — see CLAUDE.md, "ML Failure — No Fallback."

### SHAP presentation

Each factor shows a **percentage of the total displayed impact**, with the bar width matching. Raw SHAP values (`-1.836`) mean nothing to a reader; "32%" answers "how much of the reason is this?"

The percentage is a share of displayed impact, **not** a probability of the outcome.

Recommendations derive from **negative** SHAP factors. Trail-side features (distance, elevation, terrain, duration) are excluded — there's no action a participant can take about a mountain's height. When every displayed factor is positive, say so plainly rather than padding with filler advice.

### Confidence

One decimal place, **raw — no cap**. A cap was tried and removed: it hid a real, measured property of the model (a meaningful share of predictions saturate near 100%) rather than fixing anything. **Do not re-add it.** See CLAUDE.md, "Confidence Display," for why — it documents this exact regression risk by name.

With no `SuitabilityResult` — the ML service was unreachable and the assessment was rejected, not answered by a fallback — show the label without a donut. Don't leave an empty space and don't invent a number.

### Disclaimer

Organizer-facing explanations state that the ML result is decision support only and the organizer makes the final call. The interface must never imply automatic approval or rejection.

---

## Registration Form

The page's job is the registration task. It shouldn't repeat what the participant just saw on the assessment report.

Keep: suitability result, confidence when available, a link to the full report, and essential event and trail details.

Don't reintroduce the category-score bars or the trail-demand percentage bars — the latter showed a fill percentage against no stated maximum.

---

## Participant Event Details

`Views/Participant/Details.cshtml` follows Organizer Event Details' (`Views/Event/Details.cshtml`) visual hierarchy — same DOM order (Back to Events, hero, main two-column grid, Alternative Recommended panel when applicable, bottom action strip), same major-card treatment (`bg-white/5 backdrop-blur-xl border border-white/10 rounded-2xl p-6`, `text-xl` card headings), and the same `grid grid-cols-1 lg:grid-cols-3 gap-8 lg:items-stretch` proportions (main column `lg:col-span-2`). It is a Participant-scoped adaptation, not a copy: no Organizer lifecycle action (Edit/Complete/Cancel/Reschedule/Assess Participants/View Comparison) exists on this page, and the two views are deliberately not merged into a shared partial — see CLAUDE.md, "Participant Event Details".

**Hero:** `rounded-2xl` (not the Organizer hero's `rounded-3xl` exception — see Radius, above; that inconsistency is not spread here and the Organizer hero itself is unchanged). Badges are `flex flex-wrap items-center gap-2 mb-3`, since up to four can render together: Event status (dot-pill, same `statusSelectClasses`/`statusDotClasses` mapping as Organizer), Event difficulty (`DifficultyCalculator.BadgeClass`), Full (when applicable), and the participant's own registration-state badge — Organizer's hero shows none of the last two since Difficulty lives only in its Event Overview card and there is no "participant's own state" concept for an organizer.

**Weather, Pickup Schedules, and terrain chips** reuse the Organizer page's exact rendering (stored-snapshot validation, canonical `PickupScheduleHelper` entries, trimmed/deduplicated terrain chips) — see CLAUDE.md, "Participant Event Details" and "Stored Weather" behavior there. There is no loading spinner and no client-side weather script on this page.

**No Summary card.** The sidebar is exactly two cards: Organizer Details, then Joined Participants — the old Summary card (Trail/Difficulty/Date/Duration/Registered/Status) was redundant with the hero and main-column cards and was removed rather than replaced with a second overview.

**Desktop sidebar sizing:** the right column is `lg:flex lg:flex-col lg:h-full lg:gap-6` (mobile: `space-y-6`, natural stacking). Organizer Details is `shrink-0`; Joined Participants is the flexible remaining-height card (`lg:flex-1 lg:min-h-0 lg:flex lg:flex-col`), and only its list body scrolls (`lg:flex-1 lg:min-h-0 lg:overflow-y-auto`, `.tg-event-participants-scroll` — a component-scoped rule in `wwwroot/css/input.css`, following the `.tg-records-scroll`/`#viewTrailModalBody` id/class-scoped convention rather than a page-local `.custom-scrollbar` recipe). This keeps the sidebar from leaving an exposed empty column below a short Organizer card when the participant list is short, without a sticky position or a fixed pixel height. Joined Participants renders every Accepted row (no `Take(6)`, no `+ N more` summary) and rows are plain avatar+name — no status pill (every row is already known Accepted) and no profile link.

**This same full-height sidebar structure was later corrected onto Organizer/Admin Event Details** (`Views/Event/Details.cshtml`'s Registered Participants card, which previously used `self-start` plus a fixed `max-h-125`/`.custom-scrollbar` cap with `Take(5)`/`+N more participants` truncation) so both pages behave identically at the layout level — see CLAUDE.md, "Event Details Sidebar Parity". Organizer's card reuses the same `.tg-event-participants-scroll` class rather than a second scrollbar recipe, and now renders every row in `participantRows` with no truncation; its Accepted/Pending status line and conditional `CanViewProfile` Profile link are unchanged from before this correction.

**Empty-state alignment (both pages):** even though the participant-list card stretches to fill the sidebar's full height, an empty list's icon and message stay top-aligned in ordinary flow directly under the heading (`text-center py-6`) rather than vertically centered in the tall card — no `flex`/`items-center`/`justify-center`/`h-full`/`flex-1` on the empty-state wrapper itself. The icon is `aria-hidden="true"`; the unused remainder of the card stays blank below the message. Participant's copy (`Be the first to join.`) and Organizer/Admin's (`Waiting for participants to join.`) are unchanged.

**Bottom action strip:** same `rounded-2xl` glass shell as Organizer's, status/capacity text on the left, Participant actions on the right. Every primary action that used the brand gradient (Register Now / Retake Assessment & Register, Give Feedback) now uses the Solid-accent exception recipe (`bg-accent hover:bg-accent/90`, no scale) — see Buttons, above — matching this page's other primary actions (View Recommended Event) rather than mixing gradient and solid-accent CTAs on the same page. Secondary/disabled controls keep the existing neutral `bg-white/5`/`text-gray-500` treatment unchanged.

---

## Profile

Read-only. The participant's own hiking identity (or, for an authorized Organizer/Admin visitor, the Participant's identity as far as that viewer is allowed to see) — not a second Dashboard and not Settings.

### Layout

Four stacked sections, in this order on every breakpoint: a top row (Profile card + Tier Progress card, `lg:grid-cols-2 items-start lg:items-stretch`), Summary cards, then a lower row (Recent Hikes at 1/3, Achievements at 2/3 — `lg:grid-cols-3` with `lg:col-span-1`/`lg:col-span-2`, since an even split left Achievements' cards cramped against Recent Hikes' excess empty width). Below `lg`, every grid collapses to `grid-cols-1` and the four pieces stack in that same order — Profile, Tier Progress, Summary cards, Recent Hikes, Achievements — so mobile reading order and desktop visual order never disagree. All four are separate sibling `rounded-2xl` cards (a major page section — see Radius, above); there is no longer a single combined hero card.

The top row's alignment is responsive: `items-start` below `lg` lets each stacked card keep its own independent natural content height (there's only ever one card per row at that width, so this has no visible effect beyond being the explicit default); `lg:items-stretch` at the two-column breakpoint restores equal-height siblings — both card `<div>`s are direct, unwrapped grid items with no explicit height of their own, so CSS Grid's stretch alignment alone grows the shorter card's box to match the taller one, with no `h-full` needed on either card for this to work. Both cards also dropped their previous `h-full`/forced-stretch classes and took a vertical-padding step down (`p-6 sm:p-8` → `px-6 sm:px-8 py-5 sm:py-6`) for a noticeably more compact top area, without touching avatar/emblem/text/control sizes or dropping any field.

### Profile card

Plain glass surface — `bg-white/5 backdrop-blur-xl border border-white/10`, no decorative glow, blur orb, or gradient wash behind the content. `flex flex-col` (see Layout, above, for why this needs no `lg:h-full` of its own). Structured top-to-bottom, not a single centered identity block:

1. **Header** — a heading row visually matched to the Tier Progress card's own heading (`text-sm font-semibold text-white flex items-center gap-2`, `fa-solid fa-user text-accent` icon) on the left, and a restrained uppercase `Participant` context label (`text-gray-400`, `text-[11px] tracking-wide`) on the right — a fixed, safe label naming the profile's *subject*, never derived from the viewer or any role field (every Profile route only ever resolves a Participant target).
2. **Horizontal identity block** — avatar on the left (unchanged size/solid-fallback treatment: `bg-accent text-white`, single first-initial rather than a gradient — the brand gradient stays reserved for the landing page, per Color, above; a real photo renders unchanged, `object-cover`, no filter/overlay), name/Member Since/Bio stacked on the right, `sm:flex-row sm:items-start sm:text-left` — below `sm` it stacks and centers instead, so a long name wraps rather than being squeezed into a narrow column. The participant's name is the page's plain white `<h1>` — no brand gradient on the heading itself. Member Since sits directly beneath the name (calendar icon, muted `text-gray-400`, unchanged date/format), never duplicated in the contact rows below. Bio sits beneath Member Since, unchanged fallback copy for an empty value.
3. **Contact Details** — a `border-t` divider, an uppercase `Contact Details` section label, then exactly three rows (Email, Contact Number, Facebook) inside one `divide-y` group — not three separate nested cards. Each row gets a small low-opacity accent icon tile (`bg-accent/10 text-accent`, `rounded-lg`), a muted `text-gray-400` label, and the value beneath it. An absent optional value reads `Not provided` rather than a blank gap or broken markup; long values wrap (`wrap-break-word`/`break-all`) rather than stretching the card. A Facebook value only renders as a clickable link when the server has already validated it as an absolute `http`/`https` URL (`target="_blank" rel="noopener noreferrer"`); anything else — missing, relative, or an unsafe scheme like `javascript:`/`data:` — renders as `Not provided` instead of an unsafe `href`.
4. **Owner-only `Edit Profile`** — `mt-5 lg:mt-auto`, so it anchors to the bottom of the card once the flex layout above has room to push into (i.e. whenever the desktop grid has stretched this card to match a taller Tier Progress card); left-aligned, `min-h-10` tap target, unchanged pencil icon/hover/focus treatment, uses the established Variant A secondary-button recipe (`rounded-full bg-white/5 hover:bg-white/10 border border-white/10 hover:border-white/20`, no gradient, no glow) linking to Settings — never rendered for an Organizer/Admin visitor, and nothing is left in its place when absent.

Empty Bio: owner sees `Add a short bio from Settings.`; a visitor sees `No bio added.` — never a blank gap.

### Tier Progress card

Replaces the old standalone "Rank Progress" card — same information for the owner, plus a manual Tier preview carousel above it. A fixed "Current Tier" row at the top, shown to every viewer, always names the participant's actual tier, independent of anything browsed below it.

**Contents differ by viewer**, decided server-side (`@if (Model.IsOwner)` in Razor, never CSS/JS concealment):

- **Owner:** the full carousel below, then the "Your Progress" block (progress bar, Trail Points, rank placement, calculation disclosure).
- **Organizer/Admin visitor:** heading + "Current Tier" row (as above) + the participant's actual current emblem + its display name — no arrows, no other slides, no "Your Progress" heading, no points-to-next-tier copy, no progress bar (that markup is never rendered for a visitor, not merely hidden). Trail Points and rank are **not** owner-only, though: the visitor's card still shows the same Trail Points row, rank placement/ranked-explanatory text, and Trail Points calculation disclosure the owner sees, from the identical server-computed values — only the next-tier progress detail is exclusive to the owner.

**Carousel (owner only):** left/right arrow buttons flank one combined preview unit — emblem + name + status caption ("[Tier name] — Current Tier / Unlocked / Locked") animated and swapped together, never independently. All five units are server-rendered up front (only one visible/settled at a time; JavaScript toggles which by index — see Explainability/Accessibility conventions on not re-deriving state client-side). The participant's fixed, original tier artwork (`/images/tiers/tier-{key}.webp`, `object-contain`, intrinsic `512×512`) opens on the actual current tier in full color, unanimated. Browsing left shows previously-earned tiers, still full color, labeled `Unlocked`. Browsing right shows not-yet-reached tiers with a restrained `grayscale` + `opacity-40` treatment, labeled `Locked` — no glow, blur aura, neon border on either state.

Arrows are plain chevrons, not circles: no border or background at rest (only a subtle `hover:bg-white/10` circular highlight on hover/focus, which doesn't count as "permanent"), `w-10 h-10` (40px) so the tappable area stays in the required ~40–44px range, `focus:ring-2 focus:ring-accent` for visible keyboard focus, and accessible names (`View previous tier` / `View next tier`). The left arrow is disabled (real `disabled` attribute, not just a visual style, plus `opacity-30` + `cursor-not-allowed` so the disabled state isn't color-only) on Trail Starter; the right arrow is disabled on Trailblazer; neither wraps.

**Animation:** navigating plays a short (~200ms total: two ~100ms phases), direction-aware slide+fade — next exits left/enters from right, previous exits right/enters from left — using only literal, static Tailwind opacity/translate-x/duration/ease classes toggled via `classList` (never built from string fragments; `wwwroot/css/input.css`'s `@source` list already scans `wwwroot/js/**/*.js`, so classes referenced only from `profile-tier-carousel.js` are still found by the build). A transition lock drops any click/key that arrives mid-animation rather than queueing or overlapping it; the carousel's `overflow-hidden` stage keeps a stable box during the transition (transforms don't affect layout, so this can't create page-level horizontal overflow either). `prefers-reduced-motion: reduce` swaps directly to the target slide with no transition. The initial page load is never animated — the server-rendered markup already matches the resting state. The stage is `aria-live="polite"` so a navigation announces its final selected tier once, not per intermediate frame. No autoplay.

**Progress stays factual (owner only):** a "Your Progress" label plus the linear accent progress bar and points-to-next-tier copy always describe the participant's real current-to-next tier — browsing the carousel to a different (locked or unlocked) preview never changes this section's numbers, labels, or the bar's fill; the carousel and the progress block are structurally independent, so there's no code path where one could affect the other. Below the bar: the same plain text pairs (Trail Points, placement) and `<details>`/`<summary>` Trail Points disclosure the old Rank Progress card had, with the secondary explanatory lines (ranked-placement caption, not-yet-ranked caption) also on `text-gray-400` rather than `-500` for the same contrast reason as the Profile card's labels. Ranked state reads `#N of M ranked hikers` with correct singular/plural on "hiker"; zero-completion state names the tier (`Trail Starter`) and explicitly says the account isn't ranked yet rather than showing `#0` or dividing by an empty denominator; top tier shows the bar at 100% with a "highest tier reached" message instead of a next-tier line, and the right arrow disabled state confirms there's nowhere further to browse either.

### Summary cards

Four equal-height (`h-full min-w-0`) cards, `grid-cols-2` on mobile/tablet and `lg:grid-cols-4` on desktop — same shape as the Dashboard's own summary row, positioned between the top row and the lower row. All four use the same accent icon treatment (no per-metric color) per Color, above: three-or-four metrics of the same kind get one color, not several. Achievements Earned may show as `earned / total`. No Tier emblem repeats here.

### Recent Hikes

Visible heading only — the underlying data, query, and everything else described below are identical to what this section (previously labeled "Recent Adventures") has always done; nothing about qualification, ordering, or the `RecentAdventures` model/service name changed for the rename. Reuses the Records page's bounded-viewport recipe exactly: `.tg-records-scroll` (thin accent scrollbar) on the scrollable list, `.tg-records-cue`/`.tg-records-cue.is-visible` (bottom-only fade, JS-owned, never mixed with native `hidden`) for the cue, and the same measured-height technique (five real rows' rendered height, not a fixed pixel guess) — see `wwwroot/css/input.css` and `Views/Records/Index.cshtml`. Difficulty badge reuses `DifficultyCalculator.BadgeClass` unchanged. No action button, no status pill, no Event Details link in this version.

Organizer/Admin Records tables (Event History, Participant Registrations) reuse the Organizer Registrations table's established structure and interaction behavior — a header `<div>`/row `<ul>`/`<li>` grid, not a `<table>`, with top+bottom `.registrations-scroll-cue` scroll cues — while retaining Records-specific columns and historical data. `.tg-records-cue` (bottom-only) remains in use only by this page's Feedback list.

### Achievements

A responsive grid of equal-height (`h-full min-w-0`) cards — `grid-cols-1` on narrow mobile, `sm:grid-cols-2`, `xl:grid-cols-3` where the lower-right column has room — now the lower-right column alongside Recent Hikes rather than beside the old Rank Progress card. Nine cards form a complete 3×3 grid at the `xl` breakpoint, with no empty final slot. Each of the nine fixed, original 512×512 transparent WebP badges (`wwwroot/images/achievements/`) resolves from `ParticipantAchievementResult.AssetKey` — a stable per-achievement key assigned in `ParticipantAchievementCatalog`, never derived from the title in Razor.

**Resting card (compact collectible-badge presentation, not a text list):** badge image only (`w-20 h-20 sm:w-24 sm:h-24`, `object-contain`, decorative `alt=""`/`aria-hidden="true"` — the adjacent title carries the accessible identity), the achievement title, and its progress bar pinned to the card bottom (`mt-auto`) so bars align across a row regardless of title wrap. No requirement text, no "Locked" label, no progress fraction, no earned date, and no Font Awesome icon chip are shown at rest — all of that moved into the requirement reveal, below. Unlocked: full-color badge, `text-white` title, accent-tinted card background, progress bar filled solid accent at 100%. Locked: `grayscale opacity-40` badge (same restrained treatment as a locked Tier emblem), muted `text-gray-400` title, and a `bg-accent/50` fill on the existing neutral `bg-gray-700` track — never grayscale alone; the fill amount and the reveal's exact `current / target` text also carry the locked/unlocked distinction. No page-specific color per achievement, matching the same "don't invent per-item color" rule as the summary cards. No filtering/sorting/tabs/carousel/pagination for nine cards.

**Requirement reveal:** a dark (`bg-black/85`), fully card-contained (`absolute inset-0` inside a `relative overflow-hidden` card — never clips outside it, never shifts the card's size) overlay showing a small "Requirement" label, the achievement's exact description, and either its current-vs-target progress (locked) or earned date (earned, using the same stored/derived date the old always-visible line used — never invented). A transparent, full-card-covering `<button type="button">` (accessible name `View requirements for {title}`, `aria-controls` pointing at the panel, `aria-describedby` pointing at a matching `sr-only` description so screen readers get the same content once, since the visible panel itself is `aria-hidden`) is the trigger. Mouse hover reveals it declaratively via CSS `group-hover` alone, with no script involved. Keyboard focus and tap/click are both owned by `wwwroot/js/profile-achievements.js` (one delegated listener set per page) through three coordinated `data-*` attributes on each card — `data-expanded` (persistent, tap/click-pinned; the only one that drives `aria-expanded`), `data-focus-visible` (a transient Tab-driven preview that never touches `aria-expanded`), and `data-focus-suppressed` (set when Escape or a second activation closes a card whose trigger still holds focus, so the preview can't immediately reassert itself before focus actually moves; cleared on blur). There is deliberately no `group-focus-within` here — a plain `:focus-within` rule has no way to learn "the script just closed this," which is exactly the stuck-open bug this state model replaces. Escape and a second activation both close without calling `.blur()`, so focus never moves; opening one card closes every other; outside-click closes whichever is open; and `motion-reduce:transition-none` skips the fade for reduced-motion users. Every card's progress bar keeps `role="progressbar"` with an achievement-specific `aria-label` and an `aria-valuetext` carrying the same current-vs-target/earned wording.

An Organizer/Admin visitor sees only earned cards (or `No achievements earned yet.` — never an empty grid); locked cards, their progress, and their reveal content never render outside the owner's own view — presentation changes apply only to whichever set the controller already authorized for that viewer, never a second visibility rule.

---

## Settings

Editable, unlike Profile — the account owner's own Profile Information and Security (password), plus a read-only Account Information summary. `container: max-w-6xl mx-auto`, matching every other authenticated page.

### Layout

Three full-width sections stacked with `space-y-6`, in exactly this DOM order on every breakpoint — Profile Information, Account Information, Security — no side-by-side columns, no sticky positioning, no CSS-driven visual reorder. This replaces an earlier version of this page that put Profile Information at `lg:col-span-2` beside a sticky `lg:col-span-1` Account Information card: that arrangement left a permanent empty desktop column below the short Account Information card once Security moved to full-width, so the whole page went full-width instead.

### Cards

All three are the standard major-card treatment (`bg-white/5 backdrop-blur-xl border border-white/10 rounded-2xl`, `p-6 sm:p-8`) with an icon-tile header — `w-10 h-10 rounded-full bg-accent/15 text-accent` circle plus a heading/subtitle pair, the same icon-tile recipe `Admin/Index.cshtml`'s summary cards already use. Profile Information and Security both carry a subtitle line under the heading; Account Information does not.

Profile Information's own content stacks vertically, not side-by-side: a compact photo identity row, a `border-b border-white/10` divider, the full-width field grid, then the action area — no permanent photo column beside the fields. That side-by-side arrangement (`grid-cols-1 lg:grid-cols-4`, photo one column beside a three-column field area) was tried and reverted: at `lg`+ it left a large empty area below the short avatar column for the entire height of the (much taller) field column, made the fields feel unnecessarily narrow, and stretched card height with dead footer space. The photo row is `flex items-center gap-4` — avatar and camera button on the left, "Profile Photo"/format-and-size helper text on the right — and stays one horizontal row at every breakpoint rather than stacking on mobile, since an 80px avatar plus two short lines of text comfortably fits even a narrow phone width. The field grid that follows uses the card's full width at every breakpoint, so First/Last and Middle/Phone form two balanced columns at `sm`+ with real room, not a column squeezed beside a sidebar.

The avatar's camera button is the **only** upload trigger — there is no separate "Change Photo" text button; a redundant second control performing the identical action was removed rather than kept as a decorative-but-functional duplicate. The camera button is a real `<button type="button" aria-label="Change profile photo">`, `w-10 h-10` (a ~40×40px touch target), positioned at the avatar's lower-right corner without covering most of the photo, with a visible focus ring and a decorative (`aria-hidden="true"`) camera icon. The underlying `<input type="file" name="ProfileImage">` stays `sr-only tabindex="-1" aria-hidden="true"`, triggered only by that one button via `addEventListener` (no inline `onclick`/`onchange`) — it's never a separate, confusing stop in the tab order. `Save Changes` sits in a compact action area immediately after the field grid, right-aligned at `sm`+ and full-width on narrow mobile — not spanning under a photo column that no longer exists.

Account Information is a semantic `<dl>` with `divide-y sm:divide-y-0 sm:divide-x divide-white/10` — three stacked rows on mobile, three columns separated by vertical rules on `sm`+ — never three nested mini-cards, never sticky, no editable control anywhere on it. Account Status pairs an icon with the word "Active"/"Disabled" rather than colour alone.

Form inputs use the standard documented recipe (`rounded-xl bg-surface-card border border-gray-700 ... focus:border-accent focus:ring-1 focus:ring-accent`), not the page's previous `rounded-full` orange-bordered inputs. `Save Changes` and `Update Password` both use the established solid primary-action recipe (`rounded-full bg-accent hover:brightness-110 text-white ... focus:ring-2 focus:ring-accent focus:ring-offset-2 focus:ring-offset-surface-card`, the same class list `Views/Event/Index.cshtml` and `Views/Account/AccessDenied.cshtml` already use) — no gradient, no glow. Security's three password fields sit in a `grid-cols-1 lg:grid-cols-3` row (Current / New / Confirm); the card is full-width so this doesn't cramp the way it would inside a 2/3-width column.

The password-confirmation modal (gating Profile Information's save) now renders inside `@section Modals` like every other modal in the app, instead of inline in page content — see Modals, above.

### Progress

Restyled — no longer part of the app-wide remaining-pages list below.

---

## Landing Page — Popular Trails Carousel

Desktop keeps its established look untouched: an autoplay accordion (six-second interval, hover/focus pause, `flex-grow` expansion, no arrows/dots/pagination). Mobile is a **separate interaction model on the same markup** — a no-autoplay native horizontal scroll-snap carousel, not a shrunk copy of the accordion.

**Mobile card sizing:** the card is 86% of the mobile scroll viewport (within the target 85–88% range) — wide enough for the full title/location/stats/CTA stack, while leaving a visible sliver of both neighbors as a swipe cue. That 86% is named once, `--mobile-trail-card-width` on `.carousel-track`; the track owns symmetric `padding-inline: calc((100% - var(--mobile-trail-card-width)) / 2)` (7% each side) so the first and last cards can reach true center under `scroll-snap-align: center`, not just the middle four. The card itself then takes `flex: 0 0 100%` **of that padded content box**, not 86% again — percentage `flex-basis` resolves against the box left over after the track's own padding, so a card also asking for 86% would compound to ~74% of the real viewport (86% × 86%), silently undersizing every card and throwing off exact centering. Both active and inactive mobile cards share this same `flex: 0 0 100%`, so they're always identical widths. `scroll-snap-stop: always` on the card ensures one swipe settles on the immediately adjacent card — `scroll-snap-type: x mandatory` alone still permits a fast fling to skip several snap points.

**Active-card state is scroll-driven, not tap-driven, on mobile.** Whichever card sits nearest the track's horizontal center is "active" — synced continuously off the track's native `scroll` event, never off a separate touch-drag implementation. Tapping a card or pressing Left/Right centers it via `track.scrollTo()`; tapping the already-active card does nothing; tapping `.card-link` always bypasses activation. The desktop-only accordion effect (bigger title, `flex-grow`) does not run on mobile — every mobile card shows its full detail block regardless of active state, so no card visually shrinks or grows as the active one changes.

**Position indicator:** a restrained `N of 6` text counter below the track, visible only under `md`, secondary in weight (`text-xs text-slate-400`) — no dot pagination, no gradient. The visible counter updates immediately; a paired `aria-live="polite"` region announces the settled selection ~250ms after the last change, deliberately without the trail name (each slide's own `aria-label` already carries that), so a fast swipe across several cards doesn't narrate every one it passes.

**Mobile scrollbar:** styled directly on `.carousel-track` inside its own mobile media query — thin, transparent track, the same violet `rgb(139 92 246 / 0.65)` accent as the rest of the app. This does **not** reuse `.tg-custom-select-scrollbar` (reserved for `custom-select.js` listboxes) and is not a generic `.custom-scrollbar` utility — it's scoped to this one component, the same convention as every other scrollbar treatment in this document.

**Reduced motion:** swipe navigation stays fully usable; tap/keyboard selection centers instantly (`behavior: "auto"`) instead of smoothly; no autoplay at either breakpoint; the position counter still updates.

**Image loading:** the section sits below the Hero's `min-h-screen`, so the first card's image is never above the fold — it loads eager (not `fetchpriority="high"`, which is for actual LCP candidates), the rest load lazy, and all six carry `decoding="async"` plus real intrinsic `width`/`height` (inert for layout purposes since `object-fit: cover` already controls the rendered size, but they still prevent layout shift while the file downloads).

**Outstanding, unrelated to this pass:** all six `View trail` links still point to `/Trail`, which only Organizer/Admin accounts can open. Left unchanged — see `CLAUDE.md`, Landing Page — Popular Trails Carousel, for the same note.

---

## Accessibility

- Visible focus states on every interactive element
- Never colour alone — pair it with an icon, label, or supporting text
- Readable contrast against dark surfaces
- Motion respects `prefers-reduced-motion`
- File inputs offer a clear action once a file is selected

---

## Known Cleanup

Lower priority than unfinished functionality, but worth doing when nearby:

**`_SecondaryButton.cshtml` is empty** (0 bytes). Every secondary button in the app is hand-rolled, which is most of the remaining inconsistency.

Filling it in is not on its own enough. `_PrimaryButton` has only three call sites — `_NavbarPublic.cshtml` 102 and 166, `Home/Index.cshtml` 33 — so the partial system was never really adopted. A new partial nothing calls changes nothing. **The partial and the migration of existing call sites are one piece of work, not two.**

`_PrimaryButton` also has **no focus state at all**, only `hover:`. That contradicts the Accessibility section of this document. It should take the same ring as the hand-rolled form submit.

**`_InfoCard`, `_SectionHeader`, `_PageContainer` are dead and light-theme** — `bg-white`, `text-slate-900`, `bg-green-100 text-green-700`, `bg-slate-50`. Zero call sites, and a green that exists nowhere in the palette. Delete them rather than restyling them.

`_SecondaryButton` needs to render either an `<a>` or a `<button>`. `_PrimaryButton` renders only an `<a>`, which is why every form submit and every wizard nav button in the app is hand-rolled.

**Two modal mechanisms.** `Trail/Index.cshtml` and `Participant/Trails.cshtml` use custom `.modal-hidden`/`.modal-visible` CSS; everything else uses Tailwind `hidden`/`flex`. Standardise on Tailwind and drop the custom CSS.

---

## Progress

**The participant flow is complete:** dashboard, browse trails, browse events, event details, assessment form, assessment report, registration form, my registrations, the read-only Profile page, and Settings — plus landing page, login, and register.

**Remaining:**
- Feedback page — rebuilt as a three-step wizard functionally, but not restyled
- **All organizer pages** — dashboard, events, registrations, registration details, post-event assessment, event comparison
- **All admin pages** — dashboard, accounts, records
- **Reports** (aggregate model validation) — new page, not yet styled
- Shared: navbar partials, footer, error pages

`Organizer/RegistrationDetails.cshtml` has a SHAP panel from earlier feature work but hasn't been through the UI pass.

---

## Design Principle

TrailGuard should feel like one application, not a collection of separately designed pages.

```
Check existing pattern → reuse if possible
    → if genuinely new: decide, implement, document
```

The visual hierarchy should support the core workflow:

```
Discover trail → Assess suitability → Understand the result
    → Register → Organizer review → Final decision
```
