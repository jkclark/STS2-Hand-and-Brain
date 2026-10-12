# Hand and Brain (Slay the Spire 2 mod)

A multiplayer variant inspired by "hand and brain" chess. Everyone plays their own deck, but
after each combat **another player picks your card reward**.

With players A, B and C in that order: A picks B's cards, B picks C's, and C picks A's.

## How it plays

- **Run setup.** In a multiplayer lobby (standard, daily or custom), a **Hand and brain** box
  appears in the top-right corner. The host ticks the checkbox and uses ▲/▼ to set the order.
  Each row reads "X picks for Y". Clients see the host's settings read-only.
- **After combat.** Your reward screen holds the card reward of the player you pick for, labelled
  "Choose a card for Y". Opening it shows the cards *Y* would have been offered: Y's character
  pool, Y's relic effects, and Y's Skip/Reroll/Sacrifice options. The card you pick goes into
  **Y's** deck.
- **Their deck.** The card reward screen has a **"Y's Deck"** button (hotkey **R**, or the
  draw-pile hotkey) that opens the regular deck view for Y's deck.
- **Who picks for whom.** Curved arrows beside the player list on the left run from each chooser
  (dot) to the player they pick for (arrowhead), each in its own colour. Arrows that
  don't involve you are dimmer.
- **Watching your pick.** On the rewards and card reward screens, a panel in the top right corner
  shows the cards your chooser is being offered for you (dimmed). When a card is picked for you, it
  lifts out of the panel with "Chosen by X" and flies into your deck, and the panel goes away. If
  they skip, the panel says so; the first skip makes it flash, later ones don't.
- **Hiding the cards.** The chooser's card reward screen has a **"Hide from Y"** toggle
  (discard-pile hotkey). While it's on, Y's panel shows "(Hidden)" where the cards would be. The
  setting carries over between combats for the rest of the run.
- **Everything else is unchanged:** events, shops, gold, potions, relics, and card rewards that
  aren't end-of-combat rewards.
- Saved runs resume with the same pairing. The settings are stored per run in
  `%APPDATA%/SlayTheSpire2/hand_and_brain/` (Godot's user data folder).

Every player in the lobby needs the mod installed.

## Building

Requirements: the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) and a Slay the
Spire 2 install. The project builds against the game's own `sts2.dll`, `GodotSharp.dll` and
`0Harmony.dll`, so there are no other dependencies (BaseLib is not needed).

```sh
# Build only (output: bin/Release/HandAndBrain.dll + HandAndBrain.json)
dotnet build -c Release

# Build and copy into <game>/mods/HandAndBrain/
dotnet build -c Release -p:InstallMod=true

# Non-default install location
dotnet build -c Release -p:InstallMod=true -p:GameDir="D:\SteamLibrary\steamapps\common\Slay the Spire 2"
```

To install by hand, copy `HandAndBrain.dll` and `HandAndBrain.json` into
`<game>/mods/HandAndBrain/`. The game asks you to confirm loading mods on first launch.

## Testing

You can test multiplayer alone by running several copies of the game, connected over local
networking instead of Steam. Run these from the game folder:

```sh
# Host: goes straight to character select for a standard run
SlayTheSpire2.exe --force-steam off --fastmp host_standard

# Clients: join the host on 127.0.0.1 (use --clientId 2000, 3000 for more players)
SlayTheSpire2.exe --force-steam off --fastmp join --clientId 1000
```

`--force-steam off` keeps each copy on its own test profile instead of your Steam save.

Add `--hab-enemies-1hp` to make every enemy spawn with 1 HP. Pass it to **every** copy: combat is
simulated on each machine, so copies that disagree about enemy HP will desync.

## How it works

The game simulates every player's rewards on every machine. Only the owner's machine shows the
reward screen, and it broadcasts the choice. The mod hooks that flow with Harmony:

| Patch | Purpose |
| --- | --- |
| `CombatRoom.OfferRoomEndRewards` | After all reward sets are generated normally, moves each player's `CardReward` into their chooser's set (same order on every peer, so it stays deterministic). |
| `RewardsSetSynchronizer.SelectLocalReward` | Allows selecting a reward owned by another player when you are its chooser. |
| `CardReward.OnSelect` | For redirected rewards, the *chooser* sees the screen and sends the network choice. The card, relic hooks and run history stay the recipient's. |
| `CardReward.Description`, `NCardRewardSelectionScreen.RefreshOptions` | "Choose a card for Y" text, the "Y's Deck" button and the "Hide from Y" toggle. |
| `NRewardsScreen._Ready` | Adds the recipient's preview panel. |
| `NMultiplayerPlayerStateContainer.Initialize` | Adds the chooser → recipient arrows. |
| `StartRunLobby` / `LoadRunLobby` | Sync settings host → clients with two small custom net messages. |
| `RunManager.SetUp*Multiplayer` | Registers the run's one custom message: the chooser's "hide cards" toggle. |
| `RunManager.SetUp*` | Lock in the settings when the run starts or loads. |
| `N{CharacterSelect,CustomRun,DailyRun}Screen._Ready` | Adds the lobby settings panel. |

Source layout:

- `src/HandAndBrainSettings.cs`: settings, global state, per-run settings file
- `src/HandAndBrainSettingsMessage.cs`: lobby network messages
- `src/RunSync.cs`: in-run network message (hide cards toggle)
- `src/Patches/RewardPatches.cs`: reward redirection
- `src/Patches/RewardScreenPatches.cs`: card reward screen additions
- `src/Patches/LobbyPatches.cs`: lobby sync and run start/load
- `src/Patches/TestingPatches.cs`: the `--hab-enemies-1hp` testing flag
- `src/UI/LobbyPanel.cs`: the lobby settings panel
- `src/UI/PickPreview.cs`: the recipient's preview panel and pick/skip notifications
- `src/UI/PickerArrows.cs`: the arrows beside the player list

Built and verified against game version **v0.111.0**. Patches that reach into private game code
(`CardReward.OnSelect`, `RewardsSetSynchronizer`) may need updating when the game updates.

## Known limitations

- Only end-of-combat card rewards are redirected. Card rewards from other sources (events, relics
  such as Orrery, etc.) are still picked by their owner.
- If the chooser or the recipient is dead when rewards are handed out, the card reward stays
  with its owner, as in vanilla.
- The lobby panel, preview panel and arrows use plain Godot controls, not the game's styled widgets.
- Hiding cards is honor-system UI: every peer simulates every reward, so a modified client could
  still read them.
