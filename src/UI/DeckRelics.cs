using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HandAndBrain.Patches;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens;

namespace HandAndBrain.UI;

/// <summary>
/// While another player's deck is open, shows that player's relics under the top bar, in the place
/// of the local player's own (which slide away until the deck is closed).
/// </summary>
public sealed class DeckRelics
{
	private const string NodeName = "HandAndBrainDeckRelics";

	/// <summary>Where the game puts the local player's relics.</summary>
	private const float Left = 12f;

	private const float Top = 82f;

	private const float RightMargin = 84f;

	private const float RelicSize = 68f;

	private static readonly AccessTools.FieldRef<NDeckViewScreen, Player> _deckViewPlayer = AccessTools.FieldRefAccess<NDeckViewScreen, Player>("_player");

	private readonly NDeckViewScreen _screen;

	private readonly Player _player;

	private readonly HBoxContainer _row;

	private DeckRelics(NDeckViewScreen screen, Player player)
	{
		_screen = screen;
		_player = player;
		_row = new HBoxContainer
		{
			Name = NodeName,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_row.AddThemeConstantOverride("separation", 0);
		Label label = ModUi.MakeLabel(ModStrings.Format(ModStrings.DeckRelics, Redirects.NameOf(player)), 26);
		label.AutowrapMode = TextServer.AutowrapMode.Off;
		label.VerticalAlignment = VerticalAlignment.Center;
		label.CustomMinimumSize = new Vector2(0f, RelicSize);
		MarginContainer labelMargin = new MarginContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		labelMargin.AddThemeConstantOverride("margin_left", 8);
		labelMargin.AddThemeConstantOverride("margin_right", 12);
		labelMargin.AddChild(label);
		_row.AddChild(labelMargin);
		screen.AddChild(_row);

		Rebuild();
		player.RelicObtained += OnRelicsChanged;
		player.RelicRemoved += OnRelicsChanged;
		screen.Resized += Fit;
		screen.TreeExiting += delegate
		{
			player.RelicObtained -= OnRelicsChanged;
			player.RelicRemoved -= OnRelicsChanged;
		};
	}

	/// <summary>The player whose deck is on screen, or null if it's the local player's own.</summary>
	public static Player? OtherPlayerOf(NDeckViewScreen screen)
	{
		Player player = _deckViewPlayer(screen);
		return HandAndBrainState.IsActive && !LocalContext.IsMe(player) ? player : null;
	}

	public static void Attach(NDeckViewScreen screen)
	{
		Player? player = OtherPlayerOf(screen);
		if (player == null || screen.GetNodeOrNull(NodeName) != null)
		{
			return;
		}
		NRun.Instance?.GlobalUi.RelicInventory.AnimHide();
		new DeckRelics(screen, player);
	}

	public static void OnClosed(NDeckViewScreen screen)
	{
		if (OtherPlayerOf(screen) != null)
		{
			NRun.Instance?.GlobalUi.RelicInventory.AnimShow();
		}
	}

	private void OnRelicsChanged(RelicModel _)
	{
		if (GodotObject.IsInstanceValid(_row))
		{
			Rebuild();
		}
	}

	private void Rebuild()
	{
		foreach (NRelicBasicHolder old in _row.GetChildren().OfType<NRelicBasicHolder>())
		{
			_row.RemoveChild(old);
			old.QueueFreeSafely();
		}
		List<NRelicBasicHolder> holders = new List<NRelicBasicHolder>();
		foreach (RelicModel relic in _player.Relics)
		{
			NRelicBasicHolder? holder = NRelicBasicHolder.Create(relic);
			if (holder == null)
			{
				continue;
			}
			holder.MouseDefaultCursorShape = Control.CursorShape.Help;
			_row.AddChild(holder);
			holder.Connect(NClickableControl.SignalName.Released, Callable.From<NRelicBasicHolder>(Inspect));
			holders.Add(holder);
		}
		UpdateNavigation(holders);
		Fit();
	}

	private void Inspect(NRelicBasicHolder holder)
	{
		NGame.Instance?.GetInspectRelicScreen().Open(_player.Relics.ToList(), holder.Relic.Model);
	}

	/// <summary>Controller: the relics sit above the sort buttons.</summary>
	private void UpdateNavigation(List<NRelicBasicHolder> holders)
	{
		Control[] sorters = new[] { "%ObtainedSorter", "%CardTypeSorter", "%CostSorter", "%AlphabeticalSorter" }
			.Select((string name) => _screen.GetNodeOrNull<Control>(name))
			.Where((Control? sorter) => sorter != null)
			.ToArray()!;
		foreach (Control sorter in sorters)
		{
			sorter.FocusNeighborTop = (holders.Count > 0 ? holders[0] : sorter).GetPath();
		}
		for (int i = 0; i < holders.Count; i++)
		{
			holders[i].FocusNeighborLeft = holders[Math.Max(i - 1, 0)].GetPath();
			holders[i].FocusNeighborRight = holders[Math.Min(i + 1, holders.Count - 1)].GetPath();
			holders[i].FocusNeighborTop = holders[i].GetPath();
			holders[i].FocusNeighborBottom = (sorters.Length > 0 ? sorters[0] : holders[i]).GetPath();
		}
	}

	/// <summary>Keeps everything in one row (a second one would cover the sort buttons), shrinking it if needed.</summary>
	private void Fit()
	{
		if (!GodotObject.IsInstanceValid(_row) || !_row.IsInsideTree())
		{
			return;
		}
		_row.ResetSize();
		float available = _screen.GetViewportRect().Size.X - Left - RightMargin;
		float needed = _row.GetCombinedMinimumSize().X;
		float scale = needed > available ? available / needed : 1f;
		_row.Scale = Vector2.One * scale;
		_row.Position = new Vector2(Left, Top + RelicSize * (1f - scale) * 0.5f);
	}
}

[HarmonyPatch(typeof(NDeckViewScreen), nameof(NDeckViewScreen._Ready))]
public static class DeckViewReadyPatch
{
	public static void Postfix(NDeckViewScreen __instance)
	{
		DeckRelics.Attach(__instance);
	}
}

[HarmonyPatch(typeof(NDeckViewScreen), nameof(NDeckViewScreen.AfterCapstoneClosed))]
public static class DeckViewClosedPatch
{
	public static void Postfix(NDeckViewScreen __instance)
	{
		DeckRelics.OnClosed(__instance);
	}
}
