# Changelog

## 2.5.11
- Water stop: the learnt stop gaps are now kept per locomotive **and train weight** (`stopgaps.txt` has the car id, gross tons, approach gap and correction gap; up to 8 weights per locomotive). How far a train stops before its waypoint depends on its mass, and the same engine hauls different trains. A stop uses the entry nearest in weight (within 25%) and otherwise the defaults; each stop updates the entry for its weight, or adds one. `Player.log` says which it used. An old `stopgaps.txt` from 2.5.10 is ignored (the format changed) and replaced when a stop learns something.

## 2.5.10
- Water stop, 2.5.9 test: both trains filled. EMR 5000 stopped 0.21 m past (stop gap 1.24 m); EMR 1 stopped 0.78 m past on the approach (gap 0.67 m), then needed two corrections (0.60 m short, then 0.14 m short) because its first correction still used the default gap.
  - The learnt stop gaps are now kept per locomotive in `stopgaps.txt` in the mod folder and loaded next time, so the first approach of a later game already aims with them (they were forgotten at every restart).
  - The default gap for a correction's waypoint is 0.7 m (was 1.2 m): corrections measured 0.57-0.75 m on EMR 1.

## 2.5.9
- Water stop, small consists: the 2.5.8 test shows the train stops a different distance before its waypoint depending on the consist (EMR 5000 shoving 274 m: 1.4-1.8 m; EMR 1 alone: about 0.6-0.7 m). EMR 5000 filled on the first spot (0.33 m short, inside the 0.55 m reach). EMR 1 stopped 0.78 m past the spout, and its three corrections, each placed a fixed 1.2 m beyond the move, swung 0.78 past, 0.64 short, 0.62 past, 0.64 short, because the train really stops about 0.57 m before a correction's waypoint.
  - The mod now learns how far before its waypoint the train stops, per locomotive: from the approach (the aim past the spout against where the hatch ended up) and from each correction (the waypoint distance against the distance actually moved). The next approach aims with the learnt figure, and corrections use theirs (never under 1.15 m out, since a waypoint within about a metre counts as reached at once). The log shows both figures.

## 2.5.8
- Water stop now also works for an engineer on **Waypoint orders**. Fast forward (`FastForwardWrangler`) only runs while an engineer has a Waypoint order, so the stop, which started only from Road orders, never fired when the engineer was switched to Waypoint mode to fast forward. It now takes water for a column that lies before the waypoint (the engineer's own remaining waypoint distance decides; a column beyond the destination is skipped), and puts the original waypoint, direction and speed back afterwards. Hold (spotting failed) still stops in Road mode at speed 0. A stop in progress is cancelled if you change the mode, waypoint, direction or speed. Note: fast forward ends by itself when the engineer is about to arrive at a waypoint, which includes the spot waypoint at the column.

## 2.5.7
- Legacy speed limits: the speed boards are gone. Putting a board at each end of every limited stretch gave far too many on sidings. The mainline fix (clearing the legacy limit inside CTC blocks) is unchanged, and branches, yards and spurs keep their limits without boards. Boards left in a save by 2.5.5 are taken down once per load.
- Water stop: the log now records the game's time scale when a stop is planned and when it switches to Waypoint mode, to find out what happens during fast forward. Fast forward (`FastForwardWrangler`) only runs while an engineer has a Waypoint order, ends when none has, and stops on any game notice; a Road-mode water stop was not designed around that yet.

## 2.5.6
- Water stop, after the 2.5.5 run:
  - **Reach:** at Whittier the hatch stopped 0.45 m short, which is inside the spout's reach (column 0.2 m + F-71 hatch 0.4 m), but the mod's own tolerance was 0.35 m, so it tried to correct instead of filling. It now uses the loader's own reach (`CarLoadTargetLoader.radius` + `CarLoadTarget.radius`, less 5 cm) and counts the sideways offset too.
  - **Aim:** the two Whittier shoves show the train stopping 1.42-1.45 m before its waypoint (0.5 m allowance: 0.92 m short; 1.0 m: 0.45 m short), so the waypoint now goes 1.45 m past the spout position.
  - **Corrections:** Yard mode made no move for 0.45 m, just as a short waypoint made none for 0.92 m. Corrections are waypoints again, placed the move plus 1.2 m out (the game counts a waypoint within about a metre of the train as reached), at up to 10 mph, up to three, as far as 100 m either way. Backing up uses the other end of the train as the leading end.
  - **Approach speed:** at Dillsboro the column is about 60 m inside its block; the switch to Waypoint mode came at 25 mph with the hatch 62 m out, and the train ran 67 m past. Within the slow-down mile, the Road speed now also steps down to one the train can stop from in the distance left (gentle braking of 0.12 m/s², in 5 mph steps, at least 5 mph): about 25 mph at 600 m, 15 mph at 300 m, 5 mph under 100 m.
  - **Re-arming:** after you take over a stop, the engineer plans no other water stop for 5 minutes (it picked the next column within seconds).

## 2.5.5
- New, off by default: **legacy speed limits** (`LegacySpeedLimits`; "Fix legacy limits" in the settings window, effective within a few seconds). 0.6 removes the old speed boards (`TrackConditionController` deletes every "speed:" sign) but `TrackSegment.speedLimit` from the map still caps the auto engineer's track speed: on the Bryson map 382 segments are 25 mph, 83 are 15, 47 are 30 and 66 are 45, none of them shown (reported as forum topic 306).
  - Mainline, meaning any segment inside a CTC signal block (`CTCBlock` track spans; main tracks and signalled sidings): the legacy limit is cleared, so the railroad limit, curves and track class decide.
  - Branches, yards and spurs: the limit stays, and a speed board (the game's own "tsr" board, as used for player speed signs) goes up at each end of every limited stretch, with a tooltip. The boards are stored under the key prefix `aebf-legacy-speed:` and rebuilt on every load (host only).
  - In memory only: switching it off, or unloading the mod, puts the limits back and takes the boards down. `Player.log` gives the counts.
  - Side effect to know: the game's track wear and tie-paint checks use the same limit, so cleared mainline limits count as higher allowed speeds there too.
- Detailed status: a map limit now reads "Legacy limit" (it was "Posted limit", but nothing is posted), and the tooltip says where it comes from.
- Water stop spotting, after the second Whittier miss (hatch 0.92 m short, both corrections did nothing): the game completes a waypoint as soon as it is within 1 m of the train's end (`AutoEngineerPlanner.IsUnderTrain`), so a sub-metre waypoint is "reached" at once and the train never moves.
  - The waypoint now goes 1.0 m past the spout position (was 0.5 m, Waypoint Queue's figure).
  - Corrections are made in Yard mode with a move distance (the game counts down the metres actually travelled), up to three. A correction that moves the train less than 10 cm is counted as stuck; two of those end the attempt.
  - If no water flows within 30 s, the spout goes up, the spotting is measured again and corrected, and filling is tried once more.
- Water stop failure handling: when spotting fails or the water stops coming in below 95%, the engineer no longer drives off short of water. It stops at the column (Road mode, speed 0) and says so. If you take water by hand, it resumes its road orders once the tender is full and the spout is up; any orders you give end the stop. The detailed status shows "Water stop (mod): holding at …".

## 2.5.4
- New, off by default: **detailed engineer status** (`DetailedStatus`; "Detailed status" in the settings window under "Engineer status", effective at once). It replaces the planner's status line (`AutoEngineerPlanner.SetPlannerStatus`) with the same choice of limit, then:
  - says what sets a "Track Speed": "Posted limit", "Railroad limit", "Track class N", "Curve N°", or "Curve (equipment)" when the curve is sharper than the tightest-rated car in the train (the game cuts the speed towards 5 mph there);
  - adds the distance to a limit or stop ahead ("in 420 ft", "in 1.2 mi"), refreshed whenever the planner runs (every 0.5 s above 30 mph, 1 s below, 3 s stopped);
  - shows "Engine top speed" when the locomotive's top speed (where its tractive effort runs out) is below the limit;
  - names this mod's own events: water stop approach, spotting and filling (with the water level), approach hold, stall guard.
  - The status tooltip lists the reasoning (for equipment: which car, its rating, the curve and what the track alone allows), the engine top speed and the next five targets with distances. Host only for the tooltip; the line itself is the game's synced status.
  - `StatusMetric` ("Metric distances") switches feet and miles to metres and kilometres.
- Water stop: the tender stopped with its hatch out of the spout's reach on the 2.5.3 Whittier shove (no water flowed, it gave up after 90 s). The lead-end-to-hatch distance was a straight line, which is shorter than the track with 270 m of train on a curve; it is now measured along the track (the point on the train's own track nearest the hatch, in 1 m and then 5 cm steps).
- Water stop: once stopped, it measures how far the hatch is from the spout along the track and logs it. If it is more than 0.35 m off (the reach is 0.6 m), it pulls ahead or backs up by that much at 5 mph in Waypoint mode, up to twice, before calling for water.

## 2.5.3
- Water stop fix for shoved trains (cars ahead of the engine). On the reverse test the Whittier stop cancelled with "the column is no longer ahead on the lined route": the Whittier tower sits about 35 m inside block `wh-mm` from its `wh-e` end, so coming in from that end with 274 m of cars ahead of the tender the lead car reached the tower before it was seen in the block, and the "column ahead" check, which was measured from the lead car, failed.
  - Distances (search, slow-down point, "still ahead") are now measured from the tender's water hatch: the walk starts at the lead end moved back along the track by the lead-end-to-hatch distance. The stop is only abandoned once the hatch itself is within 5 m of the column or past it (a new column is still only picked 30 m or more ahead of the hatch).
  - The switch to Waypoint mode happens when any car of the train is in the column's block, or when the lead end has already passed the column.
  - With the engine leading nothing changes in practice (the hatch is about 23 m behind the pilot on the test train). `Player.log` lines now say "hatch ... m from".

## 2.5.2
- Water stop: the tender's water hatch now opens when the engineer calls for the spout and closes again when the spout is put away (left open if it was already open), with the same message as clicking it. The hatch key differs between tenders (`water.hatch` on the F-71 and T-17, `hatch.water` on the B-65, `hatch` / `top.hatch` on the K-28 saddle tank), so the mod picks a toggle with "hatch" in its key, preferring "water", then the one nearest the water filler; `Player.log` names the key used. Cosmetic: the column's loader does not check the hatch.

## 2.5.1
- New, off by default: **coast band** (`CoastBandMph`; slider "Coast band" in the settings window, effective at once; 1 mph suggested). Road and Waypoint modes, above 5 mph. It changes the speed controller's commands, not its plan:
  - Power off: the throttle closes when coasting would not lose speed (gravity at least the train's Davis resistance, the game's own `TrainPlantModel.DavisResistanceLbs`) and the train is within the band below its target. On a grade where gravity beats resistance by 10 lb per ton or more (about 0.1 mph a second of free acceleration), the throttle stays closed however far below the target the train is.
  - Brake on: a train-brake application waits until the train is more than the band over its target, unless a lower limit or a stop ahead needs it (the same look-ahead windows as brake tap suppression).
  - Why: on the 2.4.7 stock-fuel round trip the downhill leg spent 6.8 minutes powering and 7.6 minutes braking with only 3.1 minutes coasting while gravity was helping, and over the trip the throttle went straight to a brake application 46 times, usually within a second.
- The fuel summary has two new columns before `setup`: `powerCuts` and `brakesHeld` (commands changed by the coast band); `setup` has `band=`.
- Water stop and coast band share no state; nothing else changed.

## 2.5.0
- New, off by default: **water stop for Road-mode engineers** (setting `WaterStop`; switch and sliders in the settings window under "Water stop (Road mode)", effective at once).
  - When the tender's water falls below `WaterStopPercent` (default 30%), the engineer looks ahead along the route as it is lined (up to `WaterStopSearchKm`, default 30 km) for the nearest water column or tower, says where it will take water, and slows to `WaterStopApproachMph` (default 20 mph) within about a mile of it.
  - It switches to Waypoint mode only once the lead car is in the column's CTC signal block, so the player's routing and signals are kept up to that block (Waypoint mode may throw switches inside it). Where the column is not in a CTC block it switches within `WaterStopApproachMeters` (default 600 m).
  - The waypoint puts the tender's water hatch (the tender's `LoadTarget`) under the spout. Stopped there, it asks the column's own loader to load (the `CarLoaderSequencer` "wants loading" key, as the game's own sequence uses), waits until the tender is full, puts the spout away and returns to Road mode with the direction and speed it had.
  - The player changing the orders cancels it. If it cannot reach the column within 20 game minutes, or no water flows for 90 seconds, it gives up and returns to Road mode. A column it gave up on or cancelled is skipped for 10 game minutes. Host only.
  - `Player.log` lists the water columns found (id, place, block, fill rate, reach) once per session and every step of each stop.
  - The spotting arithmetic and the loader request follow the refuel feature of PrinceOfPluto's Waypoint Queue mod (MIT licence).

## 2.4.10
- Realistic fuel use (off by default): new evaporation curve, so less coal for the same steam. Water per pound of coal is now 8 at light firing, 7 at moderate firing (about 650 lb of water per sq ft of grate per hour) and 5.5 at heavy firing (935), falling slowly to 5 beyond; it was 7.5 at light firing falling to 5 at 1,000. The new points sit between Ralph Johnson's road figures (6.5 at 100 lb of coal per sq ft per hour, 5.5 above 170) and Goss's superheated test-plant result (about 7.3 at 200 psi; *Superheated Steam in Locomotive Service*, 1910). Water use is unchanged. Replayed over the logged Bryson to Sylva round trip (EMR 5000, about 1,931 tons), coal falls about 8% (9,247 to 8,505 lb; 126 to 116 lb per 1,000 gross ton-miles; 3.7 to 3.4 lb per indicated horsepower-hour) and water per pound of coal rises from 6.3 to 6.8. No setting or default changed; `CoalScale` still multiplies the result.
- README: the realistic fuel section now compares with Goss's test-plant figures and the ICC's Class I fuel statistics (1921-1943) instead of the second-hand McAuliffe figures.

## 2.4.9
- Documentation only, no change in behaviour. The README and the `SteamLbPerIhpHour` comment no longer claim a realistic range or "about 112 lb" for realistic fuel. They now give the measured figures (about 126 lb of coal and 95 gal of water per 1,000 gross ton-miles on the Bryson to Sylva round trip, against 172 and 153 with the game's own use) and cite the only outside figures found, McAuliffe's *Railway Fuel* (1927) as quoted on a Trains.com forum: 139, 161 and 159 lb of coal per 1,000 gross ton-miles for Union Pacific 1925, Santa Fe 1923 and all US railroads 1925. The older "80-110 lb" and "realistic water stops" wording had no source and is removed.

## 2.4.8
- Fix in realistic fuel use (off by default): the cutoff term of the steam rate now uses the real cutoff. `SteamEngine.reverser` is the lever position (the game maps lever 0.1 to 1 onto a cutoff of 0.15 to 0.85 with `TrainMath.ReverserToCutoff`), but the model used the lever value as if it were the cutoff, so it overstated the cutoff term at long cutoffs (lever 0.64 is cutoff 0.57). Replayed over the logged 38.4-mile round trips this lowers the modelled burn by about 2.4% coal and 1.7% water (9,477 to 9,246 lb coal, 7,105 to 6,985 gal water; 129.2 to 126.1 lb coal per 1,000 gross ton-miles). No setting or default changed (steam rate 21.5, cutoff effect 0.5); raise the steam rate by about 2% in `Settings.xml` to keep the old totals.

## 2.4.7
- Realistic fuel use can now be switched on and off in the settings window (Ctrl+F8, section "Fuel use"), with effect at once and no restart, so a stock-fuel run and a realistic run can be made in the same session. The patch on `SteamEngine.UpdateConsumption` is now installed whenever the engine members it needs exist, even when the setting is off at start-up, and does nothing while the setting is off (the game's own rates stay). The self-check line for it reads "off (can be switched on in the settings window)" when off. The steam rate, efficiency and coal scale are still edited in `Settings.xml`. Default unchanged (off). The "Reset these to defaults" button does not touch it.

## 2.4.6
- Logging only, no change to driving or fuel use. The sampler log (`ae_*.csv`) has a new `tons` column after `cars`: the gross weight in short tons of everything coupled to the locomotive (locomotive, tender and every car, loads included), refreshed every 2 seconds. The fuel summary (`fuel_summary_*.csv`) has four new columns before `setup`: `grossTons` (current), `gtMiles` (gross ton-miles run so far, using the weight at each moment), `coalPer1000Gtm` and `waterGalPer1000Gtm` (blank until 20 gross ton-miles). This lets runs with different trains be compared, and the realistic fuel steam rate be checked against published coal per 1,000 gross ton-miles. A new column shifts the later columns in the sampler log; read them by name.

## 2.4.5
- Fix (game bug worked around): a placed fusee no longer throws an error on every frame during fast-forward. `UI.FastForwardWrangler.Start` shows a `FadeOut`, and `FadeOutController.Show` sets the main camera's `enabled = false` half a second later (`Hide` turns it back on when fast-forward stops). `Camera.main` only returns an enabled camera, so it is null for the whole fast-forward, and `Effects.FuseeRenderer.Update` does `Camera.main.transform` without a check (the method it calls next, `UpdateEmission`, does check). Logged: 1,031 `NullReferenceException`s from `FuseeRenderer.Update` in one fast-forward with a fusee near the train, none in the same session without one. The new prefix on `FuseeRenderer.Update` skips the update while there is no main camera and does nothing otherwise. Setting `GuardFuseeCamera` (default on; also a switch in the settings window, effective at once). The first time it acts, `Player.log` says so. Reported to the developers.

## 2.4.4
- Fix (game bug worked around): fuel and water are charged for the game time that has really passed when time is sped up. `BaseLocomotive.PeriodicUpdateBody` waits `WaitForSeconds(1f)` and calls `PeriodicUpdate(1f)`; a coroutine resumes at most once per frame, so when a frame covers more than a second of game time (the 0.6.1 fast-forward) one second is charged and the rest is lost. Logged: at normal speed the tender lost 6,315 lb of coal against 6,322 lb computed from the burn rate; in fast-forward it lost 36% of the computed amount (coal and water alike, 2,250 lb against 6,319 lb). The new prefixes on `SteamLocomotive.PeriodicUpdate` and `DieselLocomotive.PeriodicUpdate` replace `dt` with the game time since the previous call for that locomotive, only when it is over 1.25 s (capped at 60 s), so normal speed is unchanged. Setting `FuelFollowsTimeWarp` (default on; also a switch in the settings window, effective at once). The first time it acts, `Player.log` says so.
- The logs' fuel numbers (`fuel_summary`, `coalLbs`) come from the burn rate in game time and were never affected.

## 2.4.3
- Defaults changed after A/B runs of Bryson to Sylva Interchange and back (EMR 5000, about 38.4 miles, 79 minutes moving, six configurations): `SpeedReductionLeadSeconds` 6 to **10** and `BrakeTapSuppressMph` 0 to **2**. Together they used about 3% less coal on the round trip (9,474 lb against 9,747 to 9,768 lb) for the same time, with the saving on downhill running (about 8%) and none uphill. A lead of 14 gave no further gain; Undershoot margin 1.5 and 2 and cutoff tolerance 8 gave nothing more and are left at their old values (1 and 3). Run-to-run noise was about 0.5 to 1%, and it is one route and one locomotive.
- Existing `Settings.xml` files are read as they are, so installs that already have the file keep their own values. To take the new defaults, set the two values by hand, use the window's reset button (which resets the window's values to the new defaults) or delete the file.
- The self-check now shows "Brake tap suppression: enabled" by default; set `BrakeTapSuppressMph` to 0 to turn it off.

## 2.4.2
- Logger: every `fuel_summary_*.csv` row gets a `setup` column with the settings window's values at that moment (for example `tap=2;lead=6;cut=3;over=1;under=1;margin=2;ceil=1;indep=1`), so a run records how it was set rather than relying on Settings.xml. The settings window also writes `Settings saved: ...` to `Player.log` on each save. Reason: a run meant to test Undershoot margin 2.0 left `PlanUndershootDeadbandScale` at 1 in Settings.xml and gave the same coal as the run before it, so it could not be told whether the value had been applied.
- Behaviour is otherwise unchanged.

## 2.4.1
- Fix: brake tap suppression never fired on a Bryson to Sylva run (`tapsSuppressed` 0 with the limit at 2 mph; 331 lb/mi against 329 without it). The run was in Waypoint mode ("Running to waypoint" in every target list) and the filter only allowed Road mode. It now allows Road and Waypoint. A stop target within the larger of 1500 m and 80 s of travel, or a lower limit within 600 m / 40 s, still leaves the application to the controller, so stopping accuracy at a waypoint is not changed. Yard mode is still left alone.
- Logger: `fuel_summary_*.csv` gains a `tapWhy` column, a running count of why first brake applications were not suppressed (`locoBrake`, `handleDown`, `cylinders`, `mode`, `slow`, `overLimit`, `gaining`, `limitAhead`, `stopAhead`), to show which condition is holding the filter back.
- Settings window: planner weights show as 1.00 (the "(stock)" text did not fit in the slider handle); the explanatory text and self-check lines are smaller and dimmer.
- Behaviour with `BrakeTapSuppressMph` at 0 is unchanged.

## 2.4.0
- New: in-game settings window, built with the game's own panel builder and opened with **Ctrl+F8** (`SettingsHotkey` and `SettingsHotkeyCtrl` in Settings.xml change the key). It has sliders for the brake tap limit, the slowing lead, the cutoff tolerance and the five planner weights, a switch for the fuel summary log, a reset button, and the self-check result for each feature. Changes apply at once and are written to Settings.xml as they are made (through a temporary file). The window is registered again after each map load, because the game forgets custom windows when a map unloads. It does nothing at the main menu.
- Features that the window can switch on while the game is running (brake tap suppression, early slowing, planner weights) now install their patches whenever the game members they need exist, even when the setting is off at start-up; each patch does nothing while its setting is off or at the game's value. Before this, a feature that was off at start-up needed a restart. Self-check lines for them read "off (can be switched on in the settings window)". Features that choose their patches by other settings (cutoff, realistic fuel, logging, and so on) are unchanged and are still edited in Settings.xml.
- Build: the project also references `Unity.InputSystem.dll`, `Unity.TextMeshPro.dll` and a few of the game's other assemblies when present, because the panel builder's method signatures mention their types.
- Behaviour with the default settings is otherwise unchanged.

## 2.3.1
- Fix: the fuel summary counted a train brake handle at a small reduction as braking. The engineer often parks the handle at a 5 psi reduction that leaves the car cylinders empty (log: set 0.0556, cylinders 0.02 psi, throttle 1.0 for 98 s), so the "both" column and the brake and tap counts were too high. Braking, overlap and brake applications are now judged by the average cylinder pressure (above 1 psi, locomotive or cars), read four times a second of game time.
- Fix: brake tap suppression only fired when the handle was fully released, so a handle parked at a small reduction defeated it. It now fires on the first real application: handle at most a 10 psi reduction and car cylinders at or below 1 psi. The self-check also needs `AutoEngineer.AverageTrainBrakeCylinder()` for this feature.
- Behaviour with `BrakeTapSuppressMph` at 0 is unchanged.

## 2.3.0
- New: fuel summary log. `logs\fuel_summary_*.csv` gets one cumulative row per engineer about every minute of game time (`LogFuelSummary`, on by default): minutes, miles, coal, coal per mile, water, and coal and minutes split into powered, coasting, braking, "both" (throttle open while the train brake is applied) and standing, plus counts of brake applications, brake taps (an application that took off under 3 mph) and taps suppressed. For a trip, subtract the first row from the last. Use it to compare settings on the same route.
- New: brake tap suppression (`BrakeTapSuppressMph`, default 0 = off, so defaults are unchanged). When the train is within this many mph over its target, is not gaining speed, and no lower limit or stop is near enough to matter, the controller's first train-brake application from released is replaced by a coast. If the speed climbs past the margin or something ahead comes into range, the controller's own command goes through. Road mode only. Suggested first value: 2. Does not depend on `FixEnabled`; the self-check turns it off if `SpeedControlLoop.ApplyRolloutCommand` or its fields are not found.

## 2.2.6
- Fix: the 2.2.4/2.2.5 workaround ran (`Player.log`: "CTC fix: block sy-w removed from signal sy-w-eb") but the signal still dropped to Stop in the same second block `sy-w` went occupied (23:22:05, emergency stop 23:22:09). Taking the block off the signal's own list was not enough: an interlocking signal also shows Stop when a block on the outlet its route leads into is occupied (`Interlocking.BlockAndNexSignal`). `CtcDropBlocks` now also removes the block from the interlocking outlet(s) that the signal's routes use in its direction of travel, and the log line says how many entries came from each place.
- Logger: `ctc_dump_*.txt` gains an interlockings section (switch sets, outlets with their blocks and next signal, routes) and each signal's interlocking and route mapping, so the outlet data can be checked.
- Behaviour is otherwise unchanged.

## 2.2.5
- Fix: the 2.2.4 CTC block workaround did not run on game 0.6.1. It was gated on `FixEnabled`, and the self-check switches `FixEnabled` off on 0.6.1 (the brake-drag members `trainBrakeController`, `independentController` and `throttleController` are not found, see the self-check in `Player.log`). The workaround now depends only on `CtcDropBlocks` being set. The statement in 2.2.4 that it applies with `FixEnabled` on is wrong.
- Behaviour is otherwise unchanged.

## 2.2.4
- Temporary workaround for the Sylva West eastbound signal (`sy-w-eb`) dropping to Stop about 45 m before a train, which makes the auto-engineer's emergency stop fire from 25 mph. Cause (from `ctc_dump_*.txt` and `Player.log`): the signal's protected-block list contains only block `sy-w`, whose span lies 12.9 to 50.6 m short of the signal, and the block goes occupied as the train enters it (the same second as the signal change in two runs). Every other signal with protected blocks has them starting 5 to 7 m ahead of it. New setting `CtcDropBlocks` (default `sy-w-eb:sy-w`; comma-separated `signalId:blockId` pairs; empty = off) takes the named block off the named signal's list at runtime each time a map loads, then asks the signal to recalculate. Nothing in the map or save is changed, and the pair is ignored if it is not on the map. Remove the setting when the game fixes the block (the game's own earlier fixes for the same kind of fault, RR-178, RR-783 and RR-787, moved block boundaries in the map).
- Does not need the logger. Self-check: new feature "CTC block fix".

## 2.2.3
- Logger: new file `logs\ctc_dump_*.txt`, written once per game session when the first engineer is enabled (`LogCtc`, default on; needs `LogEnabled`). It lists every CTC block with its track spans (segment, end, distance, position, length) and every signal with its position, its aspect, the blocks it protects and the distance from the signal to each span end of those blocks. Read-only; nothing in the game is changed.
- Why: the Sylva West eastbound signal (`sy-w-eb`) drops from Approach to Stop about 43 m before a train. `Player.log` shows block `sy-w` going occupied at the same second in two runs. A signal shows Stop whenever a block it protects is occupied, and a block is occupied when any car is on one of its spans, so the span of `sy-w` appears to start short of the signal. The game's own fixes for the same kind of fault (RR-178, RR-783, RR-787 in the release notes) moved block boundaries in the map. The dump gives the numbers needed to decide whether a runtime trim of that span is possible.
- Behaviour is unchanged.

## 2.2.2
- Logger: `sigMastM` is removed from the main CSV and from `signals_*.csv`. It read a constant (709.6 in the 28 Oct run) because the locomotive's `Component` transform does not follow the train, so it never measured anything.
- Logger: two columns replace it, in both files. `sigRawM` is the signal distance as the game built it, taken in `SetTargets` before this mod edits the target list, then reduced by the distance the locomotive has travelled since. `sigDistM` is still the distance in the list the engineer acts on, which includes this mod's edits (`SpeedReductionLeadSeconds` moves lower limits closer by speed x lead, about 40 m at 25 mph). `travelM` is the cumulative distance travelled by the locomotive since logging began, so two rows (or the signal flip and the emergency stop) can be compared by distance as well as by time.
- Why: in the Sylva West runs the signal flips from Approach to Stop about 4 s ahead of a train doing 25 mph, and the logged distance there was the shifted value, not the game's. `sigRawM` shows the true distance at the flip, so a game fault and a mod effect can be told apart from the CSV alone. A quick check without a new build: set `SpeedReductionLeadSeconds` to 0 in `Settings.xml`; then `sigDistM` and `sigRawM` should agree.
- Self-check: the "Signal logging" feature now also needs `AutoEngineer.SetTargets(Targets)` and the SetTargets patch, which captures the raw distance.
- Behaviour is unchanged.

## 2.2.1
- Logger: new column `sigMastM` in the main CSV and in `signals_*.csv`: the straight-line distance (3D) from the locomotive's centre to the tracked signal's mast. It does not depend on the planner, so comparing it with `sigDistM` (the AI's own distance) shows how far off the AI's distance is when a signal drops. The AI measures from the head of the train, so expect `sigMastM` to read up to about half a locomotive length more.
- Logger: reading a signal's shown aspect (`CTCSignal.LastShownAspect`, internal in the game) goes through reflection, as in the automatic compile fix to 2.2.0 (e1f8670).
- Behaviour is unchanged.

## 2.2.0
- Logger: signal events. With `LogSignals` on (default; needs `LogEnabled`) a new file `logs\signals_*.csv` gets one line each time the signal an engineer is tracking, or the aspect it shows, changes: game time, wall-clock time (matches `Player.log`), locomotive, event (`seen`, `aspect`, `next`, `passed`), signal id and name, the aspect before and after, speed, the distance to the signal, the first target and the full target list. It is checked every physics tick and also while the train stands still. The main CSV gets three columns, `sigId`, `sigAspect` and `sigDistM`. This is for catching a signal that drops from Approach to Stop while a train is still approaching it (reported for Sylva West eastbound on 0.6.1: Approach, then Stop 4-5 s later, then the game's emergency stop from about 25 mph).
- New setting `ApproachNoNextSignalMph` (default 25, which is what the game does, so nothing changes). When the game finds no signal after an Approach signal it sets 25 mph there; a lower value (for example 15) lowers that limit so the AI slows sooner for a Stop signal it cannot see. Only ever lowers; 25 or more, or below 5, change nothing.
- Self-check: new feature "Signal logging", and the signal-braking feature also covers the new setting.
- Known, not changed: the main CSV skips rows while the train is standing still, which is why a stop at a red signal shows as a gap in the log (the 250 s hole at 2509-2760 s in the 28 Oct run). The signal log is not affected.
- Known, not changed: the game's `EmergencyStop` (target distance below -3 m on a stop target) sets the consist's velocity to 0 outright, so a Stop signal that appears closer than the braking distance ends in a dead stop whatever the AI does.

## 2.1.0
- Gentle direction-change stop: changing direction on a moving train leaves the engineer with no speed order and no targets, and it stops at the game's maximum brake application (a 28 mph train stopped in 143 m). While the train is moving with no speed order and nothing ahead, the brake limit the planner uses (`TrainBrake.CalculateMaxEffectiveApplication`) is capped at `DirectionStopBrakeLimit` (default 0.15, about 13 psi; 0 = off). The cap ends when a target or speed limit appears again, or below 2 mph.
- Planner weights: five multipliers (`PlanOverspeedScale`, `PlanUndershootScale`, `PlanUndershootDeadbandScale`, `PlanCeilingScale`, `PlanIndependentUseScale`) scale the rollout controller's score weights through a postfix on `RolloutController.WeightsFor`. All 1 (the game's values) by default.
- Logger: new columns `maxApp`, `maxAppRaw`, `capActive` (the brake limit, before and after the cap) and, with `LogDecisions` on (default), `plan`, `planCost`, `planHoldCost`, `planAgeS` and `planRoots` (the controller's latest decision, its cost, the cost of holding, how old that decision is, and the best cost of each first action).
- Self-check covers the new targets (two new features, "Gentle direction-change stop" and "Planner weights", plus "Decision logging").

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
