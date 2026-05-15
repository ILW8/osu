# Tournament multiplayer room controls in the left navigation panel

## Background

The tournament client's "Multiplayer Room" controls (room ID + password input,
Connect / Disconnect / Reconnect buttons, invite accept / dismiss buttons, and
status text) currently live inside `GameplayScreen`'s right-side `ControlPanel`.
They are therefore only visible while the user is on the gameplay screen, which
makes it inconvenient to join or switch rooms while preparing for a stream
(e.g. while sitting on the Schedule screen waiting for it to start).

The same `MultiplayerRoomConnectionControls` component is also rendered inside
`SetupScreen`'s main content. That copy is intentionally left in place by this
work — the redesign only moves the gameplay-screen copy.

## Goal

Make the Multiplayer Room controls accessible from every tournament screen by
moving them out of `GameplayScreen`'s right control panel and placing them on
the always-visible left navigation column, beneath the screen-navigation
buttons.

## Non-goals

- No changes to `MultiplayerRoomConnectionControls` itself (visuals, focus
  behavior, connect / invite handling all stay as-is).
- No changes to the IPC layer, room model, or any networking code.
- `SetupScreen` is **not** deduplicated; its in-content copy stays.
- No new visibility logic — the controls show whenever the IPC is
  multiplayer, just like today.

## Affected files

- `osu.Game.Tournament/TournamentSceneManager.cs` — layout change + new
  conditional injection of the controls.
- `osu.Game.Tournament/Screens/Gameplay/GameplayScreen.cs` — remove the
  multiplayer controls (and the preceding spacer) from the right
  `ControlPanel`.
- `osu.Game.Tournament.Tests/TestSceneTournamentSceneManagerMultiplayer.cs`
  (new) — sibling visual test scene that caches a `MultiplayerMatchIPCInfo`
  so the multiplayer code path is exercised. The existing
  `TestSceneTournamentSceneManager.cs` is unchanged and continues to cover
  the file-based path.

## Detailed design

### `TournamentSceneManager` layout

Current left column (200 px wide):

```text
Container (200 px, Color4.Black background)
└── buttons : FillFlowContainer
              RelativeSizeAxes = Axes.Both
              Direction = Vertical, Spacing = 5, Padding = 5
              Children: ScreenButton / Separator (× ~16)
```

New left column when IPC is `MultiplayerMatchIPCInfo`:

```text
Container (200 px, Color4.Black background)
└── OsuScrollContainer (RelativeSizeAxes = Axes.Both)
    └── buttons : FillFlowContainer
                  RelativeSizeAxes = Axes.X
                  AutoSizeAxes = Axes.Y
                  Direction = Vertical, Spacing = 5, Padding = 5
                  Children:
                      (existing ScreenButton / Separator entries)
                      Separator
                      MultiplayerRoomConnectionControls(multiplayerIpc)
```

When the IPC is file-based, the same layout is used minus the trailing
`Separator` + controls — i.e. it is identical in shape to today's panel, just
wrapped in a scroll container. Wrapping is unconditional to keep the layout
code simple; the practical behavior change in the file-based case is that the
nav button list now scrolls if it ever exceeds the column height. This is not
worse than today (today it would simply clip).

`load()` gains a `MatchIPCInfo ipc` parameter (resolved via BDL — already
cached in `TournamentGameBase.load()` as both `MatchIPCInfo` and, when
applicable, `MultiplayerMatchIPCInfo`). After the existing children are added,
the load method checks `if (ipc is MultiplayerMatchIPCInfo multiplayerIpc)`
and appends the separator + controls.

`MultiplayerRoomConnectionControls` already uses
`RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y` and renders its own centred
bold "Multiplayer Room" header, so it slots into the `FillFlowContainer`
without further styling.

### `GameplayScreen`

- Remove the `addMultiplayerControls` method (currently lines 189–196 of
  `GameplayScreen.cs`).
- Remove the `addMultiplayerControls(multiplayerIpc);` call from `load()`
  (currently line 135). All other multiplayer-specific behavior in that block
  — gameplay display creation, "Visible players" slider, chroma hiding,
  `IsConnected` fade binding, `addVolumeControls()` — is preserved.
- The right-side `ControlPanel` after this change contains: Warmup,
  Show chat, Chroma width, Players per team, (multiplayer only) Visible
  players, Volume header + Master / Music / Effects sliders.

### `SetupScreen`

No changes. The `MultiplayerRoomConnectionControls` instance added in
`reload()` (currently lines 126–127) is kept; users will see the controls
twice while on SetupScreen.

### `MultiplayerRoomConnectionControls`

No changes. Reused unchanged in the new location.

## Data flow

Unchanged. The component is constructed with the resolved
`MultiplayerMatchIPCInfo` and binds directly to its `IsConnected`,
`ConnectionError`, and `PendingInvite` bindables. The new placement only
changes where it is rendered, not how it talks to the IPC.

## Visual treatment

The left column's background is `Color4.Black`. The controls' text colors are:

- Header "Multiplayer Room": default white.
- "Disconnected" status: `OsuColour.Gray(0.6f)`.
- "Connected" status: `Colour4.LightGreen`.
- Error status: `Colour4.OrangeRed`.
- Invite text: `Colour4.Orange`.

All read clearly on the black background — no styling changes are needed.
A single `Separator` (the existing `TournamentSceneManager.Separator`
private class — 20 px tall, full width) divides the navigation list from the
room controls, matching how the nav-button groups are themselves separated.

## Conditional rendering

Mirrors today's logic in `GameplayScreen` and `SetupScreen`: the controls
are only constructed and added when `ipc is MultiplayerMatchIPCInfo`. With
the file-based IPC there is no functional or visual change to the left
column beyond the unconditional scroll wrapper.

## Testing

### Automated

- `TestSceneTournamentSceneManager` already constructs `TournamentSceneManager`
  against the default `MatchIPCInfo` (file-based code path). This scene
  continues to pass and visually confirms the no-multiplayer layout still
  looks like today (controls absent, nav buttons unchanged).
- Add `TestSceneTournamentSceneManagerMultiplayer` that caches a
  `MultiplayerMatchIPCInfo` so the controls render. The scene allows visual
  inspection of layout, scroll behavior at varying heights, and the
  Separator placement.

### Manual

1. With multiplayer mode disabled, launch the tournament client. The left
   navigation column should look identical to today.
2. Enable "Use multiplayer spectating" in SetupScreen, save, restart. The
   left column should now show the screen-nav buttons, a separator, and the
   Multiplayer Room controls.
3. Switch through Setup / Schedule / Bracket / MapPool / Gameplay / Win.
   Confirm the controls remain visible and interactive on every screen.
4. On GameplayScreen, confirm the right `ControlPanel` no longer shows the
   Multiplayer Room section; it still shows Warmup, Show chat, Chroma width,
   Players per team, Visible players, and the Volume sliders.
5. Connect to a room from a non-gameplay screen (e.g. Schedule). Confirm
   that the IPC connects, the status text turns green, and switching to
   Gameplay shows the player grid fade in normally.
6. Trigger an invite while sitting on a non-gameplay screen. Confirm the
   invite text + Accept / Dismiss buttons appear in the left panel and
   work.
7. Resize the window vertically until the combined nav + controls flow
   exceeds the column height. Confirm scrolling reaches everything.

## Risks

- **Scroll wrapping is a behavior change in the file-based case.** Today
  the nav buttons can clip; tomorrow they scroll. This is strictly an
  improvement in reachability but is a visible change worth noting.
- **No new title bar on the left column.** The right-side `ControlPanel`
  has its own "Control Panel" title; the left column does not, and we
  rely on the controls' own bold "Multiplayer Room" header to label the
  section. If a future change moves more components into the left column
  we may want to introduce a section title pattern, but that is out of
  scope here.
- **SetupScreen duplication.** While on SetupScreen the controls render
  twice. This is intentional per design discussion and is left for a
  potential follow-up if it becomes annoying in practice.

## Out of scope / follow-ups

- Deduplicating the SetupScreen copy.
- Adding a section title pattern to the left column.
- Compactifying or restyling `MultiplayerRoomConnectionControls` for the
  narrower visual context (200 px wide — already fine in practice).
