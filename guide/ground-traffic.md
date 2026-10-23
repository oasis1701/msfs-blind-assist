---
title: "Ground Traffic"
---

<div class="guide-part" markdown="1">

Chapter 3 of 4 in [Part 5: On the Ground](index.md#part-4-on-the-ground) of the MSFS Blind Assist user guide.

</div>

<nav class="chapter-toc" aria-labelledby="in-this-chapter" markdown="1">

<p id="in-this-chapter"><strong>In this chapter:</strong></p>

- [When Ground Traffic Speaks](#traffic-when)
- [How Aircraft Are Named](#traffic-names)
- [The Proximity Callouts](#traffic-proximity)
- [Traffic on Your Route and Converging Traffic](#traffic-route)
- [The Departure Queue](#traffic-queue)
- [The Runway Watch](#traffic-runway-watch)
- [The Traffic Summary: Output Alt+G](#traffic-summary)
- [What Interrupts and What Waits](#traffic-priority)
- [Callout Quick Reference](#traffic-quick-reference)
- [Settings](#traffic-settings)
- [If Something Goes Wrong](#traffic-troubleshooting)

</nav>

At a busy airport you share the taxiways with dozens of other aircraft. A sighted pilot watches them: the airliner stopped ahead in the queue, the one crossing at the next junction, the aircraft on short final to the runway they are about to cross. BA's ground traffic monitor watches them for you: aircraft close to you, traffic on your route, your place in the departure queue, and who is on or approaching the runway you are about to use. At any time on the ground, Output Alt+G gives a summary of the nearest traffic.

The traffic BA can see is the traffic the simulator shows you: AI traffic, and other pilots on an online network when your network software shows them in the simulator. BA never calls out your own aircraft.

<div class="callout warning" role="note" markdown="1">

**Warning:** BA never says a runway is "clear". It only ever says it has seen no traffic: *"no traffic seen on the runway or on final."* BA cannot see everything a controller can. An aircraft the simulator is not showing, a vehicle, or traffic further out on final may still be there. Always follow your ATC clearance. The runway watch is an extra pair of ears, not permission to enter a runway.

</div>

## When Ground Traffic Speaks {#traffic-when}

The automatic callouts run while **taxi guidance is running**: from the moment you start guidance until you stop it, including after you arrive and while holding short. When you have no taxi guidance running at all, the automatic callouts stay quiet. The summary on Output Alt+G always works on the ground, whatever else is happening.

There are a few deliberate quiet times:

- **The takeoff roll.** While takeoff assist is active, proximity, route and queue callouts are silent. Your hands are on the rudder and the thrust levers, and you could not act on them anyway. The runway watch keeps running while you line up and wait, and only goes quiet once your takeoff roll reaches 30 knots.
- **The landing rollout.** While you are still rolling down the runway after landing, traffic callouts wait, so they never talk over the exit and runway-end callouts. This ends the moment you come to a stop, so if ATC holds you on the runway you still hear about the traffic around you.
- **A fast landing exit.** While you turn off the runway on a landing exit at 30 knots or more, only *"Stop"* and the runway watch are spoken. *"Slow down"* and the other callouts wait until you are below 30 knots. If they are still true then, you hear them.
- **In the air.** The monitor is a ground feature. In the air, use TCAS instead; see [TCAS Traffic](takeoff-climb-and-cruise.md#tcas).

To start taxi guidance, see [Taxi Guidance](taxi-guidance.md). The landing exit planner starts it for you automatically after landing; see [The Landing Exit Planner](approach-and-landing.md#landing-exit).

## How Aircraft Are Named {#traffic-names}

BA names other aircraft the way a controller would. If it knows the airline, you hear the airline and the type:

- *"Delta A320"*
- *"Lufthansa 747"*

If it knows only the callsign, you hear the callsign and the type, for example *"DAL 123, A320"*. If it knows only the type, you hear just the type, for example *"737"*. If it knows nothing, you hear *"traffic"*.

Types are spoken the way pilots say them, not as codes: a B77W is a *"777"*, an A20N an *"A320"*, and a B38M a *"737 MAX"*.

In the Alt+G summary, BA also adds the callsign when the airline name hides it, for example *"Delta A320, DAL 1234"*. At a hub where several Delta A320s are taxiing, that is how you and ATC tell them apart.

## The Proximity Callouts {#traffic-proximity}

BA measures the distance to each aircraft on the ground near you and uses three zones. The further out zones grow with your speed: BA adds roughly seven seconds of travel at your current ground speed, so that you have time to react and brake. At 15 knots that adds about 180 feet to each zone.

<div class="table-wrap" markdown="1">

| Zone | Starts at (stopped) | What you hear |
|---|---|---|
| Awareness | About 600 feet | The aircraft's name, direction, distance and what it is doing |
| Caution | About 400 feet | *"Slow down"*, with the aircraft's name, direction and distance |
| Warning | About 250 feet | *"Stop"*, with the aircraft's name, *"very close"*, direction and distance |

</div>

Distances are measured from the centre of your aircraft to the centre of the other one. With two large jets, the real gap between nose and tail is much smaller than the number you hear.

### Awareness {#traffic-awareness}

*"Delta A320, ahead and to the left, 500 feet, stopped."*

An aircraft is nearby. There is nothing to do except keep it in mind. The last part tells you what it is doing:

- *"stopped"*
- *"same direction"*: moving the way you are pointing
- *"opposite direction"*: moving the other way, off to one side
- *"head-on"*: coming toward you
- *"crossing left to right"* or *"crossing right to left"*

The direction words are the same ones [Look Around](knowing-where-you-are.md#where-look-around) uses. Awareness callouts are given for traffic in any direction, including behind you.

### Slow down {#traffic-slow-down}

*"Slow down, Delta A320 ahead, 380 feet."*

You are closing on an aircraft. Reduce your speed and be ready to stop. You only hear this while you are moving at 2 knots or more; if you are stopped, the same aircraft gives an awareness callout instead.

### Stop {#traffic-stop}

*"Stop, Delta A320 very close, ahead, 200 feet."*

An aircraft is very close. **Stop now**, then find out what is happening: press Output Alt+G for the traffic around you, or ask ATC.

### How the zones behave {#traffic-zones-rules}

- **Only traffic in front of you can make you slow down or stop.** *"Slow down"* and *"Stop"* are given only for aircraft within about 120 degrees either side of your nose. An aircraft behind you gets at most an awareness callout. If you then turn to face a very close aircraft, you do hear *"Stop"*.
- **Traffic moving away is not a threat.** If an aircraft is pulling away from you, for example the one ahead moving off in the queue, BA does not tell you to stop for it. If it stops again while still close, or you start closing on it, you hear *"Stop"* then. Whatever the speeds say, BA always warns you once the gap is down to about 200 feet.
- **No nagging.** Each zone is announced when you enter it, not again and again. If you back off and then close in again, BA speaks again only for a more serious zone, or once 15 seconds have passed. There is one exception: after a *"Stop"*, if you creep another 50 feet or so closer, you hear *"Stop"* again.
- **Parked aircraft beside your route.** When taxi guidance has a route, BA knows where you are actually going. An aircraft parked beside your route, which you will pass rather than hit, does not draw *"Slow down"*. It can still draw *"Stop"* if you get within about 250 feet of it. *"Slow down"* and *"Stop"* are kept for traffic on or near your route, traffic genuinely close, and moving traffic on a collision course.
- **One aircraft at a time in a line.** When several aircraft are lined up ahead of you on your route, you hear about the first one, the one you would reach first, rather than every one behind it. The queue callout tells you how many there are. A *"Stop"* is never held back, though, whichever aircraft it is for.

## Traffic on Your Route and Converging Traffic {#traffic-route}

These two callouts need taxi guidance with a route, because they depend on knowing where you are going.

### On your route {#traffic-on-route}

*"Delta A320 on your route, taxiway B, 800 feet ahead, stopped."*

There is an aircraft on the path you are about to taxi, up to about 600 metres ahead. The taxiway is named when BA knows it. The distance is measured along your route, not in a straight line, so round a bend it tells you how far you will actually taxi before you reach it. The last word tells you what it is doing:

- *"stopped"*: it is sitting on your route. Expect to stop behind it, or to be given another way round.
- *"same direction"*: it is going the same way as you. Keep your distance.
- *"coming toward you"*: it is heading toward you along your route. Slow down, be ready to stop, and check with ATC.
- *"crossing"*: it is crossing your route ahead.

You hear this once for each aircraft, until it leaves your route and comes back. You only hear it while you are moving, unless the aircraft is coming toward you. An aircraft on your route that is pulling away faster than you are moving is not announced.

### Converging {#traffic-converging}

*"Delta A320 converging from the left, about 20 seconds."*

A moving aircraft that is not on your route is on a course that will bring it close to you, in about the number of seconds given. The side is *"from the left"*, *"from the right"*, *"from ahead"* or *"from behind"*. Look for it with Output Alt+G, be ready to stop and give way, and follow ATC's instructions about who goes first. You hear this before the aircraft gets close, typically 5 to 40 seconds ahead. You hear it when the traffic is ahead of you, or at any angle while you are moving.

## The Departure Queue {#traffic-queue}

On the way to the runway, the queue can be long. BA tells you where you are in it and helps you keep it moving.

### Your place in the line {#traffic-queue-position}

*"Number 3 in the departure queue."*

When you are stopped or creeping along your route, BA counts the aircraft lined up ahead of you on the route: those stopped or slowly moving along it, pointing the same way. Aircraft parked beside the route, crossing it or coming the other way are not counted. BA only counts the line you are actually in. A big gap ends the line, so a second queue further on is not added to yours.

- *"Number 3 in the departure queue."* There are two aircraft ahead of you in the line.
- *"First in the departure queue."* You have moved up to the front. You only hear this after you have been further back.
- *"... More traffic holding further ahead."* Added when there are more aircraft beyond the end of your line.

You hear *"departure queue"* only when your route ends at a runway and the front of your line is close to the runway hold. Elsewhere you hear just *"queue"*, for example *"Number 2 in the queue."* BA waits until the count is steady before saying it, and says it again only when it changes.

### The aircraft ahead is moving {#traffic-queue-moving}

*"Delta A320 ahead is moving."*

The aircraft directly in front of you, which was stopped, has started to move. Get ready to follow it, keeping a safe distance.

### Move up {#traffic-move-up}

*"Move up. 400 feet to the traffic ahead."* or *"Move up. The traffic ahead has taxied on."*

A gap has opened in front of you and you have not moved. This is only a prompt from BA, not an ATC instruction. Move up only if you are cleared to taxi and it is safe. BA is careful with it:

- it only comes after *"ahead is moving"*, and only when that aircraft moved off along your route;
- you must be stopped, on your route, with taxi guidance running, and not on a runway;
- it is never given while anything else is within about 250 feet ahead of you, and it waits while the departing aircraft is still that close;
- it is given at most three times, 20 seconds apart, and stops as soon as you start moving.

## The Runway Watch {#traffic-runway-watch}

### What it is {#traffic-watch-what}

Runways are where the most serious accidents happen. Whenever you are about to use a runway, or are on one, BA watches that runway and its final approach and tells you what it sees.

### When it runs {#traffic-watch-when}

The runway watch starts on its own when you:

- hold short of a runway with taxi guidance, including a Progressive Taxi hold at a runway;
- cross a runway;
- backtrack along a runway;
- line up on a runway with taxi guidance;
- wait on the runway with takeoff assist active, until your takeoff roll reaches 30 knots;
- are on a runway's pavement for any other reason, including stopped on it after landing.

Holding short, lining up and waiting for takeoff on the same runway count as one watch, so you are not told the same thing again at each step. Crossing a runway is also one watch, from the hold line until you are clear on the far side.

### What you will hear {#traffic-watch-hear}

**The first status.** When a watch starts, BA always speaks the runway's status once, when its first fresh look at the traffic is in. For example:

- *"Runway 27L: no traffic seen on the runway or on final."*
- *"Runway 27L: no traffic seen on the runway. Delta A320 on final runway 27L, 4.5 miles."*
- *"Runway 27L: Delta A320 on the runway, rolling, same direction as you, ahead, 3000 feet. No traffic seen on final."*

If you are near two runways at once, for example at an intersection, each one is described in turn.

**Changes while you watch.** After the first status, BA tells you about each change:

- *"Delta A320 on runway 27L, taxiing, coming toward you, ahead and to the left, 1200 feet."* An aircraft has appeared on the runway. Its movement is *"stopped"*, *"taxiing"* or *"rolling"* (30 knots or more), with its direction where BA can tell: *"coming toward you"*, *"opposite direction"*, *"same direction as you"*, *"moving left to right"* or *"moving right to left"*.
- *"Delta A320 on final runway 27L, 6.2 miles."* An aircraft is on final approach to the runway.
- *"Delta A320 short final runway 27L, 1.8 miles."* An aircraft on final is now within 2 miles. Expect it to land very soon.
- *"Delta A320 landing runway 27L."* An aircraft is over the runway, landing. This includes an aircraft already in the flare.
- *"Runway 27L: no traffic seen on the runway now."* The aircraft that was on the runway has gone. If there is still traffic on final, you hear *"Runway 27L: no traffic seen on the runway now. Traffic still on final."*

Each aircraft is announced once. One that disappears for a moment and comes back is not announced again.

### What to do {#traffic-watch-do}

- **Holding short.** Listen to the status and keep it in mind when ATC clears you. If BA reports an aircraft on short final or landing, and you have been cleared to cross or line up, stop and query the clearance with ATC before you move.
- **On the runway.** If you hear of an aircraft landing, or on short final, while you are on the runway, tell ATC and follow their instructions. If you are crossing, the quickest way off the runway is usually straight ahead.
- **Waiting for takeoff.** An aircraft reported on the runway ahead of you means the runway is occupied. Do not start your takeoff roll until you are cleared.

### Interrupting or waiting {#traffic-watch-priority}

How runway news is delivered depends on where you are:

- **On the runway** (crossing, backtracking, lining up, waiting for takeoff, or stopped on it), a new aircraft on the runway, an aircraft on short final or landing, and a first status that reports traffic on the runway or on short final all **interrupt** whatever is being said. This is urgent.
- **At a hold**, and **while turning off the runway after landing**, runway news **waits its turn**. It never cuts off a taxi or exit instruction.

If you stop on the runway after landing, or press Continue at a hold and roll onto the runway, the situation has changed. If there is traffic on the runway or on short final, BA may repeat the status once, interrupting, because the status you heard at the hold could have been cut off by the instruction that moved you.

## The Traffic Summary: Output Alt+G {#traffic-summary}

### What it is {#traffic-summary-what}

Press Output Alt+G at any time on the ground to hear the nearest traffic. It works whether or not taxi guidance is running. Use it after a *"Stop"* or *"Slow down"* to find out what is around you, or before you start taxiing.

<div class="callout note" role="note" markdown="1">

**Note:** In Input mode, Alt+G opens the Access GSX window instead. See [Gates, GSX and Docking](gates-gsx-and-docking.md).

</div>

### What you will hear {#traffic-summary-hear}

BA reads up to three aircraft on the ground within about 2,000 feet, nearest first, each with its name and callsign, direction, distance and what it is doing:

*"2 aircraft nearby. Delta A320, DAL 1234, ahead and to the left, 800 feet, stopped. Lufthansa 747, DLH 400, behind and to the right, 1500 feet, crossing left to right."*

Then, when they apply:

- your place in the queue, for example *"Number 3 in the departure queue."*, if you are stopped or slow in one;
- the status of the runway you are watching, in the same words as the runway watch's first status.

Other answers:

- *"No ground traffic nearby."*
- *"Ground traffic monitor not active in flight."*
- *"Ground traffic monitor not connected to the simulator."*

## What Interrupts and What Waits {#traffic-priority}

Taxiing is busy, and taxi guidance has a lot to say. To keep the important things audible:

- Only three kinds of callout **interrupt** what is being said: *"Stop"*, *"Slow down"*, and runway news while you are on the runway.
- A *"Stop"* is never cut off by a *"Slow down"* or by a second *"Stop"* a second later. A newer warning only cuts in straight away if it is more urgent, and otherwise is spoken a moment later.
- Everything else (awareness, on your route, converging, the queue and runway news at a hold) **waits its turn** behind other speech. At most one of these alerts is spoken every three seconds.

## Callout Quick Reference {#traffic-quick-reference}

<div class="table-wrap" markdown="1">

| You hear | It means | What to do |
|---|---|---|
| *"Delta A320, ahead and to the left, 500 feet, stopped."* | An aircraft is nearby | Be aware of it |
| *"Slow down, Delta A320 ahead, 380 feet."* | You are closing on it | Slow down, be ready to stop |
| *"Stop, Delta A320 very close, ahead, 200 feet."* | It is very close | Stop now |
| *"Delta A320 on your route, taxiway B, 800 feet ahead, stopped."* | It is on the path you are about to taxi | Expect to stop behind it or be rerouted |
| *"Delta A320 converging from the left, about 20 seconds."* | A moving aircraft will come close | Be ready to stop and give way |
| *"Number 3 in the departure queue."* | Two aircraft are ahead of you in the line | Wait your turn |
| *"Delta A320 ahead is moving."* | The aircraft in front has moved off | Get ready to follow |
| *"Move up. 400 feet to the traffic ahead."* | A gap has opened | Move up if cleared and safe |
| *"Runway 27L: no traffic seen on the runway or on final."* | BA saw no traffic there | Still wait for your clearance |
| *"Delta A320 short final runway 27L, 1.8 miles."* | An aircraft is about to land | Do not enter the runway; query any clearance |
| *"Delta A320 landing runway 27L."* | An aircraft is landing now | Stay off the runway; tell ATC if you are on it |

</div>

## Settings {#traffic-settings}

The ground traffic monitor has one setting of its own, on the **Taxi Guidance** tab of the Settings window:

- **Show ground traffic distances in metres (default: feet)**: off by default, so traffic distances are in feet, the usual aviation convention. Switch it on to hear them in metres, for example *"Slow down, Delta A320 ahead, 120 metres."*

This setting is separate from **Use feet for distances (default: metres)**, which sets the units for taxi guidance and Look Around. The TCAS window (Output Ctrl+R) also has an **On Ground Traffic** list; see [TCAS Traffic](takeoff-climb-and-cruise.md#tcas).

## If Something Goes Wrong {#traffic-troubleshooting}

- **I hear no traffic callouts at all.** The automatic callouts only run while taxi guidance is running. Start a route in the taxi planner. Check with Output Alt+G that BA can see traffic at all.
- **An aircraft I know is there is never mentioned.** BA can only see what the simulator shows. Some AI traffic and some online pilots are not shown, or are only shown close by. This is one of the reasons BA never calls a runway clear.
- **No callouts during the takeoff roll or landing rollout, no "Slow down" for an aircraft parked beside my route, or only the first aircraft in a line is mentioned.** All deliberate; see [When Ground Traffic Speaks](#traffic-when) and [How the zones behave](#traffic-zones-rules).
- **The runway watch says "no traffic seen" but ATC holds me.** ATC knows about traffic BA cannot see, or has other reasons. Always follow ATC.

<nav class="guide-nav" aria-label="Guide chapters" markdown="1">

- [Previous chapter: Taxi Guidance](taxi-guidance.md)
- [Next chapter: Gates, GSX and Docking](gates-gsx-and-docking.md)
- [Guide contents](index.md)

</nav>
