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

A hold can be initiated in one of two ways:
1. Select the aircraft, move your mouse to the position you want them to hold, and press the `F8` key.
2. Right-click on the ETO field in the strip, enter the hold duration (in `HHMM` format), then press `ENTER`.

> [NOTE]
> For compatibility with older older versions of the plugin, holds can also be initiated by typing `H\RIVET` into the label data, where `RIVET` is the name of the holding waypoint.
> The waypoint name can be shortened to as little as three characters (e.g. `H/RIV`).
> An exit time can be specified by appending it to the holding point name, e.g. `H\RIVET\29` to depart `RIVET` at 29-minutes past the hour.

### Hold List

![Hold Window](./hold-list.png)

The hold list displays flights ordered by their CFL.

When a block clearance has been issued, the CFL is displayed as `xxxByyy`, where `xxx` is the lower level, and `yyy` is the upper level.

An `X` will be displayed if the aircraft is non-RVSM.

The hold entry and exit times are displayed in the label. The exit can be adjusted by clicking on it to select a new exit time.

The `OP_DATA` can also be viewed and edited from the hold label.

![Hold Diagram](./hold-diagram.png)

### Modifying a hold

The hold can be modified in a few different ways:
1. Right-click the ETO field of the hold point, and adjust the hold duration (in `HHMM` format), then press `ENTER`.
2. Left-click the hold exit time in the Hold List, and select a new exit time.

### Terminating a Hold

The hold can be cancelled a few different ways:
1. Middle click the ACID in the hold list
2. Press the `F8` key with the flight selected
3. Re-route the flight past the waypoint

### Configuring Lists

Up to 4 holding lists are available. When a hold is initiated at a waypoint that does not already have a list, one is created automatically. When all holds at that waypoint are removed, the list is freed and the slot becomes available again.

Lists can also be configured manually via `Tools` > `Hold Setup`. Manually configured lists are not freed automatically; they must be cleared via the Hold Setup window.

Any aircraft holding at a waypoint when all 4 lists are already occupied will be placed in the `OTHER` list.

![All Windows](./all-windows.png)

## How it works

## Installation

Ensure you have [vatSys](https://virtualairtrafficsystem.com/) version 1.4.20 or later installed, and .NET Framework 4.7.2 or later.

1. Download the [latest release from GitHub](https://github.com/YuKitsune/HoldPlugin/releases)
2. Extract `HoldPlugin.dll` into your vatSys plugins directory:
   ```
   Documents\vatSys Files\Profiles\<Profile Name>\Plugins\HoldPlugin
   ```
3. Run `unblock-dlls.bat` (included in the zip) to unblock the DLL files
4. Open vatSys and confirm `Hold Setup` appears under the `Tools` menu
