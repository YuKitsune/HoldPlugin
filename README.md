<h1 align="center">
  Hold Plugin
</h1>

<h3 align="center">
  A vatSys plugin adding a Eurocat-style Hold list.

  [![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/yukitsune/HoldPlugin/build.yml?branch=main)](https://github.com/YuKitsune/HoldPlugin/actions/workflows/build.yml)
  [![License](https://img.shields.io/github/license/YuKitsune/HoldPlugin)](https://github.com/YuKitsune/HoldPlugin/blob/main/LICENSE)
  [![Latest Release](https://img.shields.io/github/v/release/YuKitsune/HoldPlugin?include_prereleases)](https://github.com/YuKitsune/HoldPlugin/releases)
  [![built with Fallout](https://img.shields.io/badge/built%20with-Fallout-F5C800?logo=data%3Aimage%2Fsvg%2Bxml%3Bbase64%2CPHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyNCAyNCI%2BPGNpcmNsZSBjeD0iMTIiIGN5PSIxMiIgcj0iMTIiIGZpbGw9IiNGNUM4MDAiLz48ZyBmaWxsPSIjMTExIj48Y2lyY2xlIGN4PSIxMiIgY3k9IjEyIiByPSIzLjEiLz48cGF0aCBkPSJNOS45OSA5LjAyQTMuNiAzLjYgMCAwIDEgMTQuMDEgOS4wMkwxOC40OSAyLjM4QTExLjYgMTEuNiAwIDAgMCA1LjUxIDIuMzhaTTE1LjU5IDExLjc1QTMuNiAzLjYgMCAwIDEgMTMuNTggMTUuMjRMMTcuMDkgMjIuNDNBMTEuNiAxMS42IDAgMCAwIDIzLjU3IDExLjE5Wk0xMC40MiAxNS4yNEEzLjYgMy42IDAgMCAxIDguNDEgMTEuNzVMMC40MyAxMS4xOUExMS42IDExLjYgMCAwIDAgNi45MSAyMi40M1oiLz48L2c%2BPC9zdmc%2B)](https://github.com/Fallout-build/Fallout)


  <!-- <img src="./README.png" width="320" /> -->
</h3>

## Usage

### Initiating a Hold

You can initiate a hold in one of these ways:

1. Select the aircraft, put the mouse cursor on the position where the aircraft must hold, then press `F8`.
   - If the cursor is near a waypoint on the route, the hold is at that waypoint.
   - If the cursor is not near a waypoint on the route, the aircraft goes direct to the cursor position, holds there, then goes to the waypoint on the route that is nearest to the hold.
   - The hold has a default delay of 10 minutes.
2. Right-click the ETO of a waypoint in the strip. Type the delay in the `HHMM` format, then press `ENTER`. If you type one or two digits, the plugin uses them as minutes.

You can change the `F8` key in `Settings` > `Keyboard`.

A hold shows only when the hold point is the next waypoint.
If you initiate a hold at a waypoint that is further along the route, the hold starts when the aircraft passes the waypoint before it.

> [!NOTE]
> You must have permission to change the flight plan (e.g. you have jurisdiction of the aircraft). If not, the `F8` key and the delay popup do nothing.

> [!NOTE]
> For compatibility with older versions of the plugin, you can also initiate a hold in the label data. Type `H/RIVET` or `H\RIVET`, where `RIVET` is the name of the hold point.
> You can shorten the waypoint name to three characters (e.g. `H/RIV`).
> To give an exit time, add the minutes past the hour after the hold point name. For example, `H/RIVET/29` makes the aircraft leave `RIVET` at 29 minutes past the hour. If there is no exit time, the exit time is 10 minutes after the ETO of the hold point.
> A hold in the label data starts immediately. It does not wait until the hold point is the next waypoint.

### Strip

When an aircraft holds, the hold point shows two times in the strip:

- The first time shows the time of arrival at the hold point, with an `A`. Before the aircraft gets to the hold point, this is the ETO. After the aircraft gets to the hold point, this is the ATO.
- The second time shows the hold exit time, with a `D`.

![Hold Strip](./hold-strip.png)

When the aircraft is in the hold, the strip starts at the hold point.

### Hold List

![Hold Window](./hold-list.png)

The hold list shows the flights ordered by their CFL.

When a block clearance has been issued, the CFL shows as `xxxByyy`, where `xxx` is the lower level, and `yyy` is the upper level.

An `X` shows if the aircraft is non-RVSM.

The label shows the hold entry and exit times. To change the exit time, click it, then select a new exit time.

You can also see and edit the `OP_DATA` from the hold label.

![Hold Diagram](./hold-diagram.png)

### Modifying a Hold

You can change a hold in one of these ways:

1. Right-click the ETO of the hold point in the strip (the arrival time or the exit time). Change the delay in the `HHMM` format, then press `ENTER`.
2. Click the hold exit time in the hold list, then select a new exit time.

### Terminating a Hold

A hold stops in one of these ways:

1. Middle-click the ACID in the hold list.
2. Select the aircraft, then press `F8`.
3. Re-route the aircraft past the hold point.
4. The hold stops automatically after the exit time.

Methods 1 and 2 remove the delay from the remarks.

> [!NOTE]
> Methods 3 and 4 apply only to holds that are in the remarks (see [How it works](#how-it-works)). To stop a hold that is only in the label data, remove it from the label data.

### Configuring Lists

Up to 4 holding lists are available. When a hold is initiated at a waypoint that does not already have a list, one is created automatically. When all holds at that waypoint are removed, the list is freed and the slot becomes available again.

Lists can also be configured manually via `Tools` > `Hold Setup`. Manually configured lists are not freed automatically; they must be cleared via the Hold Setup window.

Any aircraft holding at a waypoint when all 4 lists are already occupied will be placed in the `OTHER` list.

![All Windows](./all-windows.png)

## How it works

The plugin keeps each hold in the remarks of the flight plan, in the standard `DLE/` format: `DLE/[waypoint][HHMM]`, where `HHMM` is the length of the delay. For example, `DLE/TARAL0020` is a delay of 20 minutes at `TARAL`. The plugin also reads a `DLE/` that the pilot put in the flight plan (e.g. for aerial work).

The remarks can have more than one delay. The plugin uses the first delay along the route from the present position of the aircraft. A delay is active only when its waypoint is the next waypoint.

The exit time is the arrival time at the hold point plus the delay. Before the aircraft gets to the hold point, the exit time changes when the ETO changes.

While the hold is active:

- vatSys does not move the aircraft along its route past the hold point.
- vatSys calculates the ETOs after the hold point from the exit time.
- vatSys does not show a RAM alert for the aircraft.

If the remarks and the label data do not agree, the plugin uses the remarks.

## Installation

Ensure you have [vatSys](https://virtualairtrafficsystem.com/) version 1.4.20 or later installed, and .NET Framework 4.7.2 or later.

1. Download the [latest release from GitHub](https://github.com/YuKitsune/HoldPlugin/releases)
2. Extract `HoldPlugin.dll` into your vatSys plugins directory:
   ```
   Documents\vatSys Files\Profiles\<Profile Name>\Plugins\HoldPlugin
   ```
3. Run `unblock-dlls.bat` (included in the zip) to unblock the DLL files
4. Open vatSys and confirm `Hold Setup` appears under the `Tools` menu
