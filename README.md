# Auto Engineer Brake Fix (Railroader)

Makes the AI engineer drive like a person: no brake dragging, fewer wasted coal and water, a smooth throttle,
gentle brake sets for signals, and no creeping past a stop.

All options are on by default, except realistic fuel use, and can be switched off individually in the Unity Mod Manager window (Ctrl+F10).

## Brake fix
In road mode the AI shares one speed setpoint between throttle and brake, with no dead zone. A tiny overspeed applies about 3% train brake, and the AI only releases it once its brake output goes clearly negative, which on a climb at full throttle may never happen: throttle and brake both on for minutes.

While the throttle is open and the train is within a small margin of the AI's own setpoint (default 0.5 mph), the mod releases the brake and blocks new light applications. Real overspeed is left to the AI, so signals, slow orders and downhill control still brake normally.

Test on a 26-car train over 12 miles: throttle+brake overlap fell from 10.3% of the time to 0.4%, with no change in speed.

## Shorter cutoff
The stock AI uses the longest cutoff that reaches peak power, but steam and coal use rise with cutoff while pulling power does not. Above 5 mph the mod picks the shortest cutoff that gives up no more than a set percentage of that power (default 3%), and never a longer one than stock.

Same route and train: coal fell from 4,136 lb to 2,334 lb (-44%) and water by 26% for a few seconds more running time. On climbs of 2.0% or more (adjustable 0.5-5%) the mod hands back to the stock cutoff.

## Throttle smoothing
The stock AI can flip the throttle between 0 and 25% every half second. The mod low-pass filters it (default 1.6 s), limits how fast it can open (full in 5 s) or close, and with "Glide" on moves the lever about 20 times a second between the AI's updates, so it moves smoothly in the cab. When the train is over its setpoint and needs the brake, the throttle closes at once.

## Signals
- **Sight distance** (default 500 m, the game uses 200 m): how far ahead the AI reacts to a signal's aspect.
- **Approach signals**: the game applies the Approach speed limit "right now", which makes the AI brake at full effort. The mod applies it at the signal, so the train slows gradually, and keeps the limit until the next signal is registered so the train doesn't speed up in between.

## Slowing down and stopping
- **Throttle hold**: while slowing for a stop or limit, or just after a brake application, the throttle stays closed unless the train drops clearly below its target. This removes the brake/throttle cycle on gentle downhills and on the way down to a stop.
- **Brake hold**: while slowing for a falling target, a light brake application is not released until the train is clearly under the target, so it builds steadily instead of flickering.
- **Stay stopped**: within 2.5 m of a stop target the AI now holds the stop; the stock AI can treat a train stopped 1-3 m short of a signal as "should be moving" and creep past it.

## Speed limits
- **No power into a reduction**: while a lower limit is in the AI's sight and the train is at or above that speed, the throttle stays closed. Power returns once the train is a mph under the lower limit.
- **Earlier, lighter braking**: a lower limit is treated as starting 6 s of travel sooner (adjustable), and the train brake is released when the speed 3 s ahead reaches the target. Speeds land on the limit instead of under it.
- **Brake with the bleed-down in mind**: a long train's brake cylinders drain slowly after a release and keep slowing the train (roughly psi squared / 60 mph on a 52-car train). The train brake is released once the cylinders will carry the train the rest of the way to the limit (within 0.5 mph), and a new application is held off until they no longer do. Not used near a stop or on a descent. The divisor and margin are adjustable.
- **Coasting band**: a new brake application on a steady limit waits until the train is 1 mph over it.
- Stops are not shifted. In Waypoint and Yard modes the game's own stopping accuracy is kept; the stay-stopped fix is Road mode only.

## Stall guard
The game has no stall check: on a grade the train cannot climb, the AI cycles between full throttle and light braking while rolling back. The mod declares a stall after 10 s of full throttle, under 4 mph, on a climb of 1% or more without gaining speed. It then closes the throttle, holds the brakes (60% train brake while still moving, then 100%; locomotive brake 100%), skips the start-up routine and announces "Stalled on grade". A second trigger covers a train that cannot start at all: under 0.5 mph with the throttle at 50% or more and the brakes off for the same time, on any grade, announced as "Can't start the train". Either hold lasts until you switch the engineer's direction or mode, or switch it off and on.

## Realistic fuel use (experimental, off by default)
The game's coal and water use rises with speed even though a locomotive's power falls with speed, so fuel per horsepower is about right at 10-20 mph and 1.5-2x too high above 30 mph. With this option, steam = steam rate (default 21.5 lb per indicated hp-hour) x the engine's own power x a cutoff factor, and coal follows from the steam through an evaporation curve (7.5 lb water per lb coal at light firing, falling to 5 at heavy firing). Water stops come out realistic: a 2,591-ton train on a 2-10-2 used about 151 gal per mile and 80-110 lb of coal per 1,000 gross ton-miles. Logged use matched the tender within 1%.

By design nothing is burned while the throttle is closed, so a locomotive left idling does not run out of coal and water. A realistic idle burn may come later as a separate "hardcore" option.

## Starting resistance (experimental, off by default)
The game uses the Davis equation for rolling resistance, which is only valid for a moving train: a standing car meets about 1.6 lb per ton, where real plain-bearing cars need 5-10 lb per ton to break away. This option adds a standing-start drag (default 5 lb per ton, fading out exponentially with speed: 37% left at 2.5 mph, about 13% at 5 mph). Standing time (on by default) scales it by how long each car has stood: a car that has just stopped still has oil in its journals and starts at about 40% of the full drag, settling back over about 10 game minutes. It uses the game clock and is not saved, so cars count as fully settled when you load a save.

## Install
1. Install [Unity Mod Manager](https://www.nexusmods.com/site/mods/21) for Railroader.
2. Unzip so you have `Railroader/Mods/AutoEngineerBrakeFix/AutoEngineerBrakeFix.dll` and `Info.json`.
3. Start the game; enable it in the UMM window (Ctrl+F10).

## Settings (summary)
Brake fix on/off and overspeed margin; locomotive brake; shorter cutoff on/off, power given up, steep-grade limit; throttle smoothing, glide, close-on-overspeed; throttle hold, brake hold, stay stopped; speed-limit lead and release lead, stall guard; realistic fuel use (steam rate, cutoff effect, coal scale); signal sight distance, approach limit at the signal, hold after the signal; experimental planning sliders (look-ahead multiplier, assumed braking), both stock by default; diagnostic logger (off by default, writes CSVs under `Mods/AutoEngineerBrakeFix/logs`).

## Compatibility
Built for Railroader 2025.1.0. Patches the auto-engineer's PID results, its target list and contextual target speed, and the planner's signal-sight distances. Other mods patching the same code may conflict (it has been run alongside WaypointQueue). Disable it in UMM to restore stock behaviour.

## Known limits
Tested on a handful of steam-hauled freight runs, mostly up to 1% grades. Fuel savings depend on the route and train.
