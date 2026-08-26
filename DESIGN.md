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

---

## Buttons

### Primary

- Shape: `rounded-full`
- Fill: brand gradient
- Text: white, `font-semibold`
- Hover: `hover:brightness-110 hover:scale-[1.02]`
- **No shadow or glow** — the gradient is enough weight

### Secondary

Two treatments exist in the app today. **Variant A is canonical**; B is being retired.

**Variant A — filled quiet.** 7 usages across 4 pages: `Participant/Details.cshtml` 426, `Participant/Events.cshtml` 164, `Participant/Trails.cshtml` 141 and 215, `Registration/MyRegistrations.cshtml` 160 and 164.

```
rounded-full text-gray-300 hover:text-white
bg-white/5 hover:bg-white/10
border border-white/10 hover:border-white/20
transition-colors
```

**Variant B — outline only.** 2 usages, one page: `Registration/Register.cshtml` 325 and 329.

```
rounded-full text-sm font-medium text-slate-400
border border-white/20 hover:text-white hover:border-white/40
```

A wins on count, and it holds its own against a `bg-white/5` card where a transparent button disappears. Deliberately quiet next to the primary — the two shouldn't compete.

Disabled state, from `Participant/Details.cshtml` 432, 438, 455: `text-gray-500` with `cursor-not-allowed` and no hover.

**Not yet built as a partial.** See Known Cleanup.

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

```
rounded-full   buttons, badges, pills, avatars, search and filter inputs
rounded-xl     cards, modals, panels, form inputs
```

Two values. `rounded-2xl`, `rounded-3xl`, `rounded-4xl`, and arbitrary `rounded-[...]` are out — the current inconsistency came from using all of them interchangeably.

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

**The participant flow is complete:** dashboard, browse trails, browse events, event details, assessment form, assessment report, registration form, and my registrations — plus landing page, login, and register.

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
