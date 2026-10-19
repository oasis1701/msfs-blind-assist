---
title: "Tone Cheat Sheet"
---

<div class="guide-part" markdown="1">

Chapter 1 of 4 in [Appendices](index.md#appendices) of the MSFS Blind Assist user guide.

</div>

BA uses a small family of tones. They follow a few shared rules, explained in full in [The Audio Language: What the Tones Mean](core-concepts.md#audio-language):

- **Pan** (which ear the tone is in) almost always means "steer this way". By default you steer towards the tone. Each family of tones has its own setting to reverse that, listed below.
- **Pitch** (how high or low the tone is) is used for up and down: attitude in Hand Fly and Visual Guidance, sink rate in the flare.
- **Silence** in a guidance tone means you are on profile. It never means the feature has stopped: when guidance ends or gives up, BA says so in words.
- **Hard pan** options, found next to each tone's other settings, make the tone jump fully to one ear instead of panning part-way. They help on speakers, where a slight pan is hard to tell from the centre.

All guidance tones play on the device chosen on the **Audio** tab of Settings (**Guidance tone output device**). Screen reader speech is not affected by that setting.

<div class="table-wrap" markdown="1">

| Tone | What the pan means | What the pitch means | What silence means | Pulsing or beeps |
|---|---|---|---|---|
| Taxi steering tone | Turn towards the tone. The further it is to one side, the bigger the turn; full pan at about 30 degrees off. | Fixed. Pitch carries no information. | Your heading is right (within a few degrees; tighter on narrow taxiways). | None while taxiing. |
| Runway lineup (taxi guidance, on the runway) | Turn towards the tone to line up with the centreline. Much more precise than taxiing. | Fixed. | Lined up on the centreline, within about half a degree. | A steady pulse, about three times a second, means you are stopped on the runway but not yet lined up. Keep turning, or move forward slowly, until it stops. |
| Docking steering tone (at the gate) | Turn towards the tone to line up with the gate's centre line. As precise as runway lineup. | Fixed. | Lined up with the gate. | None. See the docking beep. |
| Docking beep (at the gate) | Not panned. | Fixed. | Silence before docking engages, and again after a good park. | Beeps get faster as you near the stop mark: about one a second 30 metres out, rapid beeping close in. A solid, continuous tone means you are on the stop mark: stop. After a good park the solid tone holds for about three seconds, then BA speaks the closure, for example *"Aligned with gate. Parking brake."* If you overshoot there is no solid tone; you hear *"Stop. You have passed the stop position."* |
| Takeoff Assist centreline tone | Steer towards the tone to get back to the centreline. It points you onto the line, not just parallel to it. Full pan at about 5 degrees of correction needed. | Fixed. | With **Play steering tone** set to a threshold (for example **At 2 degrees error**), silence means you are tracking the centreline. With **Always**, the tone keeps sounding, centred when you are on the line. | None. |
| Rollout centreline tone (after touchdown) | Same as Takeoff Assist: steer towards the tone to the centreline. Uses the Takeoff Assist tone settings. | Fixed. | On the centreline (follows the **Play steering tone** setting, as Takeoff Assist does). | None. It hands over to taxi guidance's tone when you turn off for an exit. |
| Flare tone, vertical (manual landing assist, 50 feet to touchdown) | Not used for this tone. | High tone: you are sinking too fast, raise the nose. Low tone: you are floating, ease the back pressure. | Your sink rate is on the flare profile. | None. Stops at touchdown. |
| Flare tone, lateral (manual landing assist, 50 feet to touchdown) | Steer towards the tone to the centreline. It already allows for drift, so a correctly crabbed approach is silent. | Fixed, and a different waveform from the vertical tone so you can tell them apart. | On the centreline and holding it. | None. Carries on into the rollout tone. |
| Hand Fly tone | Shows your bank: a tone in the right ear means right wing down, fully right at about 10 degrees of bank. This is your attitude, not a command. | Shows your pitch: higher tone is nose up, lower is nose down. Level pitch sits in the middle. | Hand Fly's tone does not go silent while it is on; it is muted while Visual Guidance runs. | None. |
| Visual Guidance, desired tone | Where the guidance wants your bank to be. | Where the guidance wants your pitch to be. | Both tones always play while Visual Guidance is on. | None. |
| Visual Guidance, current-attitude tone | Your actual bank. | Your actual pitch. | Not applicable. | When the two tones are at different pitches you hear them beat (a wobble). Match the pitch until the beating stops, and match the pans until both tones sit in the same place. |

</div>

Where to change each tone:

- **Taxi Guidance tab:** **Steering tone type**, **Volume**, **Invert steering tone direction (steer AWAY from the tone)** and **Hard-pan steering tone (full left or full right; speaker-friendly)**. These apply to taxiing, runway lineup and the docking steering tone. The docking beep has its own **Docking beep sound** and **Docking beep volume**.
- **Hand Fly tab, Takeoff Assist:** **Takeoff Assist Tone**, **Takeoff Assist Volume**, **Steer toward the tone to stay on the centerline** (untick it to steer away from the tone), **Hard-pan centerline tone (full left or full right; speaker-friendly)** and **Play steering tone**. These also apply to the rollout centreline tone and the flare's lateral tone.
- **Hand Fly tab, Hand Fly:** **Hand flying feedback type** (tones, announcements or both), **Hand fly tone type** and **Volume**.
- **Hand Fly tab, Visual Guidance:** **Visual Guidance Tone**, **Visual Guidance Volume**, **Current-attitude tone**, **Current-attitude Volume** and **Hard-pan visual-guidance tones (speaker-friendly)**.
- **Audio tab:** the output device, with a **Test Tone** button that plays a tone on the chosen device.

<div class="callout tip" role="note" markdown="1">

**Tip:** Keep the two Visual Guidance tones on different waveforms. If both use the same one, they can blend into a single tone exactly when they match, which is when you most need to hear them as two.

</div>

<nav class="guide-nav" aria-label="Guide chapters" markdown="1">

- [Previous chapter: General Flying Concepts](general-flying-concepts.md)
- [Next chapter: Glossary](glossary.md)
- [Guide contents](index.md)

</nav>
