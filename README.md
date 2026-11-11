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

### Delays and Holds

The plugin uses two items in the remarks of the flight plan:

- A **delay** (`DLE/`) typically comes from the pilot (e.g. for aerial work). A delay is only advisory. The plugin uses the delay to calculate the departure time at the waypoint, and the ETOs after it. vatSys continues to move the aircraft along its route past the waypoint. The hold list does not show aircraft with a delay at a waypoint.
- A **hold** (`HLD/`) comes from the controller. The plugin also uses the hold to calculate the departure time at the waypoint, the same as a delay. vatSys does not move the aircraft along its route past the hold point until the hold stops. The hold list will show aircraft with a hold in the remarks.

See [How it works](#how-it-works) for the format.

### Initiating a Hold

You can initiate a hold in one of these ways:

1. Select the aircraft, put the mouse cursor on the position where the aircraft must hold, then press `F8`.
   - If the cursor is near a waypoint on the route, the hold is at that waypoint.
   - If the cursor is not near a waypoint on the route, the aircraft goes direct to the cursor position, holds there, then goes to the waypoint on the route that is nearest to the hold.
   - The hold has a default length of 10 minutes.
2. Type `HLD/` in the remarks (see [How it works](#how-it-works)).

You can change the `F8` key in `Settings` > `Keyboard`.

The hold has an effect on the ETOs as soon as it is in the flight plan.

> [!NOTE]
> Only the controller with jurisdiction of the aircraft can initiate, modify, and terminate a hold.

### Changing a Delay

To change a delay, right-click the ETO of the waypoint in the strip (the arrival time or the departure time). Type the delay in the `HHMM` format, then press `ENTER`. If you type one or two digits, the plugin uses them as minutes. This changes the `DLE/` in the remarks.

> [!NOTE]
> Only the controller with jurisdiction of the aircraft can modify delays.

### Strip

When the flight plan has a delay or a hold, the waypoint shows two times in the strip:

- The first time shows the time of arrival at the waypoint, with an `A`. Before the aircraft gets to the waypoint, this is the `ETO`. After the aircraft gets to the waypoint, this is the `ATO`.
- The second time shows the departure time from the waypoint, with a `D`.

![Hold Strip](./hold-strip.png)

### Hold List

![Hold Window](./hold-list.png)

The hold list shows the aircraft with a hold (`HLD/` in the remarks). The hold list shows only the aircraft in the jurisdiction or handover state. It does not show an aircraft with only a delay (`DLE/`).

The hold list shows the flights ordered by their `CFL`.

The strip shows the hold entry and exit times. To change the exit time, click it, then select a new exit time.

You can also see and edit the `CFL` and `OP_DATA` from the hold label.

![Hold Diagram](./hold-diagram.png)

### Modifying a Hold

You can change a hold in one of these ways:

1. Click the hold exit time in the hold list, then select a new exit time. This changes the `HLD/` in the remarks.
2. Change the `HLD/` in the remarks.

### Terminating a Hold

A hold stops in one of these ways:

1. Middle-click the ACID in the hold list.
2. Select the aircraft, then press `F8`.
3. Re-route the aircraft past the hold point.

### Configuring Lists

Up to 4 holding lists are available. When a hold is initiated at a waypoint that does not already have a list, one is created automatically. When all holds at that waypoint are removed, the list is freed and the slot becomes available again.

Lists can also be configured manually via `Tools` > `Hold Setup`. Manually configured lists are not freed automatically; they must be cleared via the Hold Setup window.

Any aircraft holding at a waypoint when all 4 lists are already occupied will be placed in the `OTHER` list.

![All Windows](./all-windows.png)

## How it works

The plugin keeps the delays and the holds in the remarks of the flight plan:

- A delay from the pilot uses the standard `DLE/` format: `DLE/[waypoint][HHMM]`, where `HHMM` is the length of the delay. For example, `DLE/TARAL0020` is a delay of 20 minutes at `TARAL`.
- A hold from the controller uses the same format, with `HLD/`. For example, `HLD/TARAL0020` is a hold of 20 minutes at `TARAL`.

The remarks can have more than one delay, but only one hold. When you initiate a hold, the new `HLD/` replaces the old one.

### Delays

The plugin uses all the delays along the route after the last `ATO` of the aircraft. A delay does not change the `ETO` of its waypoint. The `ETO`s after the waypoint start at the `ETO` of the waypoint plus the delay. vatSys continues to move the aircraft along its route as usual, and can show a RAM alert.

After vatSys sets an `ATO` at a position after the waypoint, the times already contain the delay, and the plugin stops adding it.

### Holds

The plugin uses the hold when its waypoint is on the route ahead of the aircraft.

The exit time is the arrival time at the hold point plus the length of the hold. Before the aircraft gets to the hold point, the exit time changes when the `ETO` changes.

While the flight plan has a hold:

- vatSys calculates the `ETO`s after the hold point from the exit time.
- vatSys does not move the aircraft along its route past the hold point.
- vatSys does not show a RAM alert for the aircraft when the hold point is the next waypoint, or when the aircraft is in the hold.

If a delay and a hold are at the same waypoint, the plugin uses the hold.

## Installation

Ensure you have [vatSys](https://virtualairtrafficsystem.com/) version 1.4.20 or later installed, and .NET Framework 4.7.2 or later.

1. Download the [latest release from GitHub](https://github.com/YuKitsune/HoldPlugin/releases)
2. Extract `HoldPlugin.dll` into your vatSys plugins directory:
   ```
   Documents\vatSys Files\Profiles\<Profile Name>\Plugins\HoldPlugin
   ```
3. Run `unblock-dlls.bat` (included in the zip) to unblock the DLL files
4. Open vatSys and confirm `Hold Setup` appears under the `Tools` menu
