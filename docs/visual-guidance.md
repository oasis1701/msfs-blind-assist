# Visual Guidance PID Tuning

Reference guide for understanding and tuning the visual guidance PID controller for runway approaches.

## Overview

The visual guidance system uses a **PID controller** to generate pitch and bank commands that guide the aircraft onto the runway centerline and glideslope. It accounts for wind drift using GPS ground track monitoring.

**Key files:**
- `MSFSBlindAssist/Services/VisualGuidanceManager.cs` (lines 92-109)
- `MSFSBlindAssist/Aircraft/BaseAircraftDefinition.cs` (ground track variable)

## How PID Control Works

The controller combines three terms to calculate desired bank (lateral) and pitch (vertical) commands:

### Proportional (P)
- **What it does**: Provides correction proportional to the error
- **Example**: 1 NM off centerline → commands 5.0° bank
- **Effect**: Stronger values = more aggressive intercept, but can overshoot

### Integral (I)
- **What it does**: Accumulates error over time to eliminate steady-state drift
- **Example**: Aircraft drifting 0.1 NM left in crosswind → integral builds correction until drift stops
- **Effect**: Stronger values = faster drift elimination, but can cause oscillation

### Derivative (D)
- **What it does**: Resists rapid changes to prevent overshoot
- **Example**: Approaching centerline quickly → derivative dampens correction to prevent overshoot
- **Effect**: Stronger values = smoother but slower corrections

## Current PID Parameters

### Lateral (Bank) Control

Located in `VisualGuidanceManager.cs:92-100`

| Constant | Value | Description |
|----------|-------|-------------|
| `LATERAL_GAIN_TRACKING` | 5.0 | Proportional: Cross-track error to bank |
| `LATERAL_RATE_DAMPING` | 15.0 | Derivative: Cross-track rate damping |
| `LATERAL_INTEGRAL_GAIN` | 0.5 | Integral: Drift elimination speed |
| `HEADING_ALIGNMENT_GAIN` | 0.3 | Additional term for heading/track alignment |
| `INTEGRAL_LIMIT_LATERAL` | 10.0 | Anti-windup limit (degrees) |
| `MAX_BANK_RATE_DEG_PER_SEC` | 5.0 | Maximum command change rate |

**Current tuning:** Phase 1 moderate settings for A320

### Vertical (Pitch) Control

Located in `VisualGuidanceManager.cs:102-109`

| Constant | Value | Description |
|----------|-------|-------------|
| `VERTICAL_GAIN` | 0.5 | Proportional: Glideslope error to pitch |
| `VERTICAL_RATE_DAMPING` | 0.15 | Derivative: Glideslope rate damping |
| `VERTICAL_INTEGRAL_GAIN` | 0.3 | Integral: Altitude error elimination |
| `INTEGRAL_LIMIT_VERTICAL` | 5.0 | Anti-windup limit (degrees) |
| `MAX_PITCH_RATE_DEG_PER_SEC` | 2.5 | Maximum command change rate |

**Current tuning:** Phase 1 moderate settings for A320

## Tuning Guide

### Identifying Problems

| Symptom | Problem | Solution |
|---------|---------|----------|
| **Slow to intercept centerline** | Proportional too low | Increase `LATERAL_GAIN_TRACKING` |
| **Overshoots centerline, crosses to other side** | Proportional too high | Decrease `LATERAL_GAIN_TRACKING` |
| **Drifts off in crosswind, "tone centered" but off centerline** | Integral too low | Increase `LATERAL_INTEGRAL_GAIN` |
| **Oscillates back and forth across centerline** | Proportional/Integral too high | Decrease gains or increase `LATERAL_RATE_DAMPING` |
| **Corrections feel "lazy" or sluggish** | Derivative too high | Decrease `LATERAL_RATE_DAMPING` |
| **Corrections too aggressive/twitchy** | Derivative too low | Increase `LATERAL_RATE_DAMPING` |

### Safe Tuning Ranges

**Lateral (Bank):**
- Proportional: 4.0 - 10.0 (current: 5.0)
- Derivative: 8.0 - 18.0 (current: 15.0)
- Integral: 0.3 - 1.5 (current: 0.5)

**Vertical (Pitch):**
- Proportional: 0.3 - 1.2 (current: 0.5)
- Derivative: 0.1 - 0.3 (current: 0.15)
- Integral: 0.2 - 0.8 (current: 0.3)

### Tuning Workflow

1. **Adjust one parameter at a time**
2. **Make small changes** (±20% increments)
3. **Test in crosswind conditions** to verify drift elimination
4. **If oscillation occurs**, reduce proportional/integral or increase derivative by 30%
5. **If still too lazy**, continue increasing proportional/integral in small steps

### Derivative to Proportional Ratio

**Current ratio:** 15.0 / 5.0 = 3.0:1

- **Ratio < 1.5:1** → More responsive, risk of oscillation
- **Ratio 1.5-2.5:1** → Balanced
- **Ratio > 2.5:1** → Too damped, "lazy" response (current: 3.0:1)

## Ground Track Monitoring

### What is Ground Track?

**Heading (what we used before):**
- Direction the aircraft **nose is pointing**
- Example: Aircraft heading 360° (pointing north)

**Ground Track (what we use now):**
- Direction the aircraft is **actually moving** over the ground
- Example: Heading 360° with right crosswind → Ground track 355° (moving northwest)
- **SimConnect Variable**: `GPS GROUND MAGNETIC TRACK`

### Why Ground Track Matters

**Without ground track (heading-only):**
- Aircraft can be pointed at runway but drifting off due to wind
- PID sees "heading aligned" and doesn't correct drift
- Can settle at equilibrium with steady-state error (off centerline)

**With ground track:**
- PID detects actual movement direction vs. runway heading
- Immediately sees drift even if nose is pointed correctly
- Integral term eliminates drift automatically

**Example:**
```
Runway heading: 360°
Aircraft heading: 365° (crabbing into wind)
Ground track: 360° (actually tracking straight)

Old system: "Heading 5° off - turn left!" (Wrong!)
New system: "Ground track perfect - maintain!" (Correct!)
```

### Ground Track in the Code

Located in `VisualGuidanceManager.cs:464-470`:

```csharp
// Use ground track if available for better drift detection, otherwise fall back to heading
double actualTrackAngle = cachedGroundTrack ?? heading;
double trackAngleError = NormalizeHeading(runway.HeadingMag - actualTrackAngle);
```

**Behavior:**
- Primary: Uses GPS ground track (drift-aware)
- Fallback: Uses heading if ground track unavailable (still functional)

## Anti-Windup Strategy

The integral term is protected from "windup" (accumulating excessive correction) by:

1. **Clamping**: Integral limited to ±10° bank or ±5° pitch equivalent
2. **Reset on crossing**: Integral resets when aircraft crosses centerline or glideslope
3. **Reset when far**: Integral resets when >1.0 NM from centerline (prevents windup during large intercepts)

This is automatic - no tuning required.

## Phase 2 Tuning (More Aggressive)

If Phase 1 settings (current) still feel too lazy, try these more aggressive values:

**Lateral:**
- `LATERAL_GAIN_TRACKING`: 8.0 (was 5.0)
- `LATERAL_RATE_DAMPING`: 10.0 (was 15.0)
- `LATERAL_INTEGRAL_GAIN`: 1.2 (was 0.5)

**Vertical:**
- `VERTICAL_GAIN`: 1.0 (was 0.5)
- `VERTICAL_INTEGRAL_GAIN`: 0.7 (was 0.3)

**Warning:** Test carefully - more aggressive settings risk oscillation.

## Related Documentation

- [Architecture](architecture.md) - Overall system design
- [Adding Features](adding-features.md) - Development workflows
