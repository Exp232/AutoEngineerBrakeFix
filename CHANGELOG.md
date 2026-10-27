# Changelog

## 2.0.1
- Logger: the tender coal and water columns (`fuelQty`, `waterQty`) were blank on 0.6.0 because load identifiers there carry the pack prefix (`rr/coal`, `rr/water`); both forms are matched now.
- Logger: a short lock on the CSV (OneDrive sync, a viewer) no longer drops rows. The file is opened with shared access, and rows that fail to write are held and written ahead of the next row (up to 6000), with one warning in the game log.

## 2.0.0
- Now a mod for the game's built-in loader (Railroader 0.6.0 and later) instead of Unity Mod Manager: asset-pack `Info.json` (`exp232/autoengineerbrakefix`, requires `rr/game >=0.6.0`), entry point `ModEntryPoint` (IModEntryPoint), built for .NET Framework 4.7.2 against `Railroader.dll` and `Railroader.Modding.dll`. Harmony is no longer supplied by UMM, so `0Harmony.dll` ships in the mod folder.
- Settings live in `Settings.xml` in the mod folder (same layout as the UMM file, read at start-up). There is no in-game settings panel for now.
- Start-up self-check: before patching, every method, field and property each feature relies on is looked up with the exact signature used. A feature with a missing target is switched off (in memory only) and the rest still load; each patch class is applied on its own. One summary block goes to the game log: mod and game version, each feature's state (enabled, disabled in settings, or disabled with what was not found), the patched-method count against the expected count, and the effective settings.
- 0.6.0 added a static overload of `AutoEngineer.CalculateLookaheadDistance`; the planning patch and the logger now pick the instance overload explicitly.
- 0.6.0 removed `brakeErrorPower`, `trainBrakeReleaseBelowOutput` and `paddingForSpeedMph` from `AutoEngineerConfig`; they are read by reflection with the existing no-config fallbacks.
- Known: 0.6.0 replaced the auto-engineer's PID speed loop with a rollout controller. The brake-drag fix, throttle smoothing, holds while slowing, residual braking and stall guard hook that PID loop, so the self-check reports them as disabled on 0.6.0 until they are reworked.

## 1.13.1
- Stall guard: switching the engineer's direction or mode (Road, Yard, Waypoint) while a stall is latched now clears it, so you no longer have to switch the engineer off and on. The mode and direction are noted when the stall latches; direction is read from the sign of the speed limit, which the planner signs by direction of travel.

## 1.13.0
- Brake with the bleed-down in mind. A long train's brake cylinders drain slowly after a release (about 2 psi/s above 30 psi, 0.4-0.9 psi/s below), and until they are empty they keep slowing the train: roughly psi squared / 60 mph on a 52-car train. The stock logic ignores that, so a 5 mph reduction became 9-12 mph under the limit in the 1.12.0 log whenever the brake went past 0.33. While slowing for a lower limit (and 45 s after), the train brake is now released as soon as the cylinders will carry the train the rest of the way to the limit (within 0.5 mph, adjustable), and a new application is held off until they no longer do. Not used near a stop, or while the train is gaining speed (a descent). Settings: on/off, the divisor (60) and the margin.
- Logger: two new columns, `resMph` (speed the cylinders will still take off) and `resNeedMph` (reduction still needed), to tune the divisor.

## 1.12.1
- Stall guard, can't-start trigger: no longer fires on a train that is still gaining speed. The 1.12.0 log showed it cutting a 52-car train creeping to a waypoint (0.05 to 0.5 mph in 10 s) and a 72-car train breaking away at 0.03 mph/s. A gain of more than 0.15 mph from the slowest point now restarts the clock, so it only fires on a train that is truly not moving up.

## 1.12.0
- Starting resistance now has a standing-time option (on by default when starting resistance is on): a car that has only just stopped keeps part of its oil film and starts easier (default 40% of the full drag), settling back to the full drag over about 10 game minutes (both adjustable). Uses the game clock, so it follows pause and time acceleration. It is not saved: cars count as fully settled when a save is loaded.
- Default fade for starting resistance is now 2.5 mph (was 1.5).

## 1.11.1
- Stall guard: the throttle no longer reopens after the stall is declared. The throttle glider kept easing the lever toward its last (full) target, so the engine pulled against the brakes for about a second. The glider now stands down while the guard holds.
- Stall guard: the speed threshold is now under 4 mph (was 6), still after 10 s at full throttle on a 1% or steeper climb without gaining speed.
- Stall guard, second trigger: a train that cannot get moving at all (under 0.5 mph, throttle 50% or more, brakes off, any grade, for the stall time) is held the same way, with its own announcement ("We can't get this train moving..."). It runs every frame instead of in the throttle PID, which does not run during the engineer's start-up state.
- Stall guard: the brakes go on in two stages. The locomotive brake is full at once and the train brake is 60% while the train is still moving, then full once it has been still for a second (or after 8 s). This replaces the instant 100% application.

## 1.11.0
- New, off by default: starting resistance. The game's rolling resistance is the Davis equation, which only covers a moving train (about 1.6 lb per ton at 0 mph; real plain-bearing cars need 5-10). The option adds a standing-start drag (default 5 lb per ton, fading out with a 1.5 mph time constant) to the game's own retarding-force sum, so heavy trains start slower and marginal grades are harder to start on. Settings under "Train resistance".
## 1.10.0
- README now documents speed limits, the stall guard and realistic fuel use (no burn while the throttle is closed, on purpose).
- Lower speed limits (all speeds were compared signed, so the 1.9.0 short-zone cap never worked while running in reverse). Replaced by: no power while a lower limit is in sight (anywhere in the planner's lookahead) and the train is at or above that speed; power returns once it is a mph under it. Setting: "No power while a lower speed limit is in sight".
- Lower limits are treated as starting 6 s of travel sooner (adjustable): braking begins earlier and is lighter, and the speed the brakes keep bleeding off after release lands on the limit instead of under it. Stops and signals' stop targets are not shifted.
- The train brake is released when the speed 3 s ahead (adjustable) reaches the target, not when it is already there.
- Stall guard: full throttle, under 6 mph, climbing 1% or more and not gaining speed for 10 s is declared a stall. The throttle shuts, the brakes are held, the start-up routine is skipped, and the engineer announces it ("Stalled on grade"). Lasts until the engineer is switched off and on. The game has no stall check; before this, the AI cycled between full throttle and light braking while rolling back.
- Waypoint and Yard modes keep the game's own stopping accuracy: the stay-stopped and stop-line fixes now apply to Road mode only.
- Removed the 1.9.0 timer-based "skip short higher zones" setting.
## 1.9.0
- Short higher zones: when a lower speed limit is due within 60 s of travel (adjustable, 0 = off), the train no longer accelerates for the higher limit in between (a 400 m stretch of 45 mph inside 35 mph track made the AI go to full throttle and then brake). A train already above the lower speed is left to the stock braking curve.
- Coasting band: a new brake application on a steady limit now waits until the train is 1.0 mph over (adjustable, 0 = stock), so 34 = power off, 35 = coast, 36 = brake. Slowing for a lower limit or stop is unaffected.
- After a brake release the throttle now opens as soon as the speed the train is heading for (3 s ahead) is under the setpoint, instead of waiting until it is 2 mph under (that let the train undershoot a 30 mph zone to 27.8).
## 1.8.2
- Realistic fuel now depends on cutoff: steam per horsepower-hour = rate x (1 + 0.5 x (cutoff - 0.30)), so the shorter-cutoff option saves fuel again. New "Cutoff effect on steam" slider (0 turns it off). Default steam rate 21.5 keeps the example run at about 112 lb of coal per 1,000 gross ton-miles with the shortened cutoff.
## 1.8.1
- Realistic fuel: default steam rate raised from 17 to 20 lb per indicated hp-hour. On a 2,591-ton, 12-mile run this gives about 112 lb of coal per 1,000 gross ton-miles (17 gave 88, below the real-world range).
## 1.8.0
- New, experimental and off by default: realistic coal and water use. Steam is now steam rate (17 lb per indicated hp-hour) x the engine's own power, and coal follows from an evaporation curve, so fuel per horsepower stays realistic at every speed (the game's own figures are about right at 10-17 mph and far too high at 35-40 mph). Settings under "Fuel use".
- The logger now writes a separate steam_engine_<loco> file for each steam locomotive in play (the config dump only covered the first one).
## 1.7.5
- Diagnostics for a fuel-use comparison: the config dump now lists the steam engine's numbers (heating surface, boiler pressure, cylinders, estimated grate area, ...), and the CSV log has teLbf, waterGps and coalLbs columns (tractive effort in lbf, water in gal/s, coal in lb/s). No behaviour change.
## 1.7.4
- Default throttle smoothing is now 1.6 s. Author set. README rewritten.
## 1.7.3
- Stopping at a signal/stop target: the AI used to treat a train stopped 1-3 m short of its target as "stopped but should be moving" and ran its start-up routine, creeping past the signal. The contextual target speed is now 0 within 2.5 m of a stop target, so the AI keeps the train stopped. Setting: "Stay stopped at a stop signal or stop target".
## 1.7.2
- Throttle bursts between brake pulses are now also suppressed when cruising at the limit on a gentle downhill (while the brake is on or was applied in the last 10 s), unless the train is clearly under the setpoint.
- Brake holding only applies to light applications (up to 2 notches) and with a smaller margin, so the train no longer overshoots 3 mph under the target and then needs throttle (seen on the way down to a stop).
- No throttle within 3 m of a stop target: the planner's speed curve never reaches 0, so the AI used to creep past a signal it had just stopped at.
- The throttle hold now works down to 1.5 mph (was 3).
## 1.7.1
- New: while slowing for a falling target the train brake is no longer released the moment the train dips a hair under the setpoint; it stays applied until the train is clearly under the target. Cuts the 0/1/2-notch brake flicker on the way down to a stop. Setting: "Keep the brake applied while slowing...".
## 1.7.0
- New: after passing an Approach signal the 25 mph limit is kept ("Approach Hold" target) until the next signal is registered, so the train no longer speeds up in the gap.
- New: while slowing for a falling target (stop, signal, speed drop) the throttle stays closed unless the train drops clearly below the target. Removes the throttle bursts between brake pulses on the way down to a stop. Applies above 3 mph.
## 1.6.0
- Signals: the AI now reacts to a signal's aspect from a configurable distance (default 500 m; the game uses 200 m), and the Approach speed limit is applied at the signal instead of "right now". Together these remove the full-effort brake application at Approach signals. Settings under "Signals".
## 1.5.0
- New experimental planning sliders (stock by default): look-ahead distance multiplier, and assumed braking when planning (start slowing earlier and brake more gently for known targets).
- Logger: new columns lookaheadM and targets (every target the AI knows about, "mph@metres:reason").
## 1.4.5
- New: when the train is over the setpoint and the brake is on (or clearly over), the throttle closes at once instead of stepping down 0.25 per half second, removing the throttle/brake overlap on speed-limit drops. Setting: "Close the throttle at once when overspeed needs the brake".
## 1.4.4
- After the start-up routine hands over, the throttle holds at its 50% until the speed controller catches up, instead of dipping toward 25% first.
## 1.4.3
- Throttle smoothing and glide now reach the target in finite time instead of creeping the last few percent (97% to 100%).
## 1.4.2
- Smooth handover from the AI's start-up routine: the throttle no longer drops from 50% to 25% when the speed controller takes over; it eases from the lever's actual position.
## 1.4.1
- Throttle smoothing now steps aside at the same overspeed margin as the brake fix, so the throttle can no longer stay open while the brake applies (seen as 1% throttle/brake overlap in a 45 mph run).
## 1.4.0
- New: throttle glide (on by default). Between the auto-engineer's half-second updates the lever moves smoothly (about 20 updates a second) toward the smoothed target. Stands down if anything else moves the throttle.
## 1.3.0
- New: throttle smoothing (on by default, 2.0 s). Low-pass filter and rate limit on the auto-engineer's throttle; real overspeed bypasses it.
## 1.2.0
- Shorter-cutoff option is now on by default (3% power tolerance), no longer experimental.
- New: stock cutoff is used on steep climbs (default 2.0% uphill or more, eased in over the last 0.5%), judged from the grade under and just ahead of the train.
## 1.1.0
- Experimental shorter-cutoff option (off by default): picks the shortest cutoff within a power tolerance of the stock choice. Can only shorten the stock cutoff.
## 1.0.0
- Renamed from AEBrakeLogger. Logging and fuel logging off by default. Attaches to engineers already running when enabled.
- Brake fix for train brake and locomotive brake with adjustable overspeed margin.
