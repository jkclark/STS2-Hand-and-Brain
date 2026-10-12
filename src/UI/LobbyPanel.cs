using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HandAndBrain.Patches;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Platform;

namespace HandAndBrain.UI;

/// <summary>
/// The "Hand and brain" box on the run setup screens. The host can toggle the mode and reorder
/// players; clients see the host's settings read-only. Only shown in multiplayer lobbies.
/// Built from plain Godot controls because the game does not register Godot scripts from mods.
/// </summary>
public sealed class LobbyPanel
{
	private const string NodeName = "HandAndBrainPanel";

	private const int FontSize = 22;

	private readonly Control _screen;

	private readonly PanelContainer _root;

	private readonly CheckBox _enabledToggle;

	private readonly VBoxContainer _rows;

	private readonly Label _footer;

	private string? _renderedKey;

	private LobbyPanel(Control screen)
	{
		_screen = screen;
		_root = new PanelContainer
		{
			Name = NodeName,
			Visible = false,
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		_root.AnchorLeft = 1f;
		_root.AnchorRight = 1f;
		_root.AnchorTop = 0f;
		_root.AnchorBottom = 0f;
		_root.OffsetLeft = -440f;
		_root.OffsetRight = -24f;
		_root.OffsetTop = 24f;
		_root.GrowHorizontal = Control.GrowDirection.Begin;

		MarginContainer margin = new MarginContainer();
		foreach (string side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
		{
			margin.AddThemeConstantOverride(side, 12);
		}
		_root.AddChild(margin);

		VBoxContainer column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 8);
		margin.AddChild(column);

		_enabledToggle = new CheckBox
		{
			Text = "Hand and brain",
			FocusMode = Control.FocusModeEnum.None
		};
		_enabledToggle.AddThemeFontSizeOverride("font_size", FontSize + 4);
		_enabledToggle.Toggled += OnEnabledToggled;
		column.AddChild(_enabledToggle);

		_rows = new VBoxContainer();
		_rows.AddThemeConstantOverride("separation", 4);
		column.AddChild(_rows);

		_footer = new Label
		{
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(380f, 0f)
		};
		_footer.AddThemeFontSizeOverride("font_size", FontSize - 4);
		column.AddChild(_footer);

		Timer timer = new Timer
		{
			WaitTime = 0.25,
			OneShot = false,
			Autostart = true
		};
		timer.Timeout += Refresh;
		_root.AddChild(timer);

		_screen.AddChild(_root);
	}

	public static void Attach(Control screen)
	{
		if (screen.GetNodeOrNull(NodeName) == null)
		{
			new LobbyPanel(screen);
		}
	}

	private StartRunLobby? Lobby
	{
		get
		{
			if (!GodotObject.IsInstanceValid(_screen))
			{
				return null;
			}
			return AccessTools.Field(_screen.GetType(), "_lobby")?.GetValue(_screen) as StartRunLobby;
		}
	}

	private static bool IsHost(StartRunLobby lobby)
	{
		return lobby.NetService.Type == NetGameType.Host;
	}

	private void Refresh()
	{
		StartRunLobby? lobby = Lobby;
		bool isMultiplayer = lobby != null && lobby.NetService.IsConnected && (lobby.NetService.Type == NetGameType.Host || lobby.NetService.Type == NetGameType.Client);
		_root.Visible = isMultiplayer;
		if (!isMultiplayer)
		{
			_renderedKey = null;
			return;
		}
		List<ulong> playerIds = lobby!.Players.Select((StartRunLobbyPlayer p) => p.id).ToList();
		if (IsHost(lobby))
		{
			HandAndBrainSettings normalized = HandAndBrainState.Lobby.NormalizedFor(playerIds);
			if (!normalized.SameAs(HandAndBrainState.Lobby))
			{
				LobbySync.HostSet(lobby.NetService, normalized);
			}
		}
		HandAndBrainSettings settings = HandAndBrainState.Lobby;
		List<ulong> order = settings.Order.Where(playerIds.Contains).ToList();
		order.AddRange(playerIds.Where((ulong id) => !order.Contains(id)));
		string key = $"{IsHost(lobby)}|{settings.Enabled}|{string.Join(",", order)}|{string.Join(",", order.Select((ulong id) => NameOf(lobby, id)))}";
		if (key == _renderedKey)
		{
			return;
		}
		_renderedKey = key;
		Render(lobby, settings.Enabled, order);
	}

	private void Render(StartRunLobby lobby, bool enabled, List<ulong> order)
	{
		bool isHost = IsHost(lobby);
		_enabledToggle.SetPressedNoSignal(enabled);
		_enabledToggle.Disabled = !isHost;
		foreach (Node child in _rows.GetChildren())
		{
			_rows.RemoveChild(child);
			child.QueueFree();
		}
		_rows.Modulate = enabled ? Colors.White : new Color(1f, 1f, 1f, 0.45f);
		bool canReorder = isHost && order.Count >= 3;
		for (int i = 0; i < order.Count; i++)
		{
			ulong chooser = order[i];
			ulong recipient = order[(i + 1) % order.Count];
			HBoxContainer row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 6);
			Label label = new Label
			{
				Text = (order.Count >= 2) ? $"{i + 1}. {NameOf(lobby, chooser)} picks for {NameOf(lobby, recipient)}" : $"{i + 1}. {NameOf(lobby, chooser)}",
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				ClipText = true
			};
			label.AddThemeFontSizeOverride("font_size", FontSize);
			row.AddChild(label);
			if (canReorder)
			{
				int index = i;
				row.AddChild(MakeMoveButton("▲", index > 0, delegate
				{
					Move(lobby, index, index - 1);
				}));
				row.AddChild(MakeMoveButton("▼", index < order.Count - 1, delegate
				{
					Move(lobby, index, index + 1);
				}));
			}
			_rows.AddChild(row);
		}
		string last = (order.Count >= 2) ? $" {NameOf(lobby, order[^1])} picks for {NameOf(lobby, order[0])}." : string.Empty;
		_footer.Text = isHost
			? ((order.Count >= 2) ? $"Card rewards after combat are chosen by the player above you.{last}" : "Waiting for more players.")
			: "Only the host can change these settings.";
	}

	private static Button MakeMoveButton(string text, bool enabled, System.Action onPressed)
	{
		Button button = new Button
		{
			Text = text,
			Disabled = !enabled,
			FocusMode = Control.FocusModeEnum.None,
			CustomMinimumSize = new Vector2(36f, 0f)
		};
		button.AddThemeFontSizeOverride("font_size", FontSize);
		button.Pressed += onPressed;
		return button;
	}

	private void OnEnabledToggled(bool pressed)
	{
		StartRunLobby? lobby = Lobby;
		if (lobby == null || !IsHost(lobby))
		{
			return;
		}
		HandAndBrainSettings settings = HandAndBrainState.Lobby.Clone();
		settings.Enabled = pressed;
		LobbySync.HostSet(lobby.NetService, settings);
		Refresh();
	}

	private void Move(StartRunLobby lobby, int from, int to)
	{
		HandAndBrainSettings settings = HandAndBrainState.Lobby.NormalizedFor(lobby.Players.Select((StartRunLobbyPlayer p) => p.id));
		if (to < 0 || to >= settings.Order.Count)
		{
			return;
		}
		(settings.Order[from], settings.Order[to]) = (settings.Order[to], settings.Order[from]);
		LobbySync.HostSet(lobby.NetService, settings);
		Refresh();
	}

	private static string NameOf(StartRunLobby lobby, ulong playerId)
	{
		return PlatformUtil.GetPlayerName(lobby.NetService.Platform, playerId);
	}
}

[HarmonyPatch]
public static class RunSetupScreenReadyPatch
{
	public static IEnumerable<MethodBase> TargetMethods()
	{
		yield return AccessTools.Method(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen._Ready));
		yield return AccessTools.Method(typeof(NCustomRunScreen), nameof(NCustomRunScreen._Ready));
		yield return AccessTools.Method(typeof(NDailyRunScreen), nameof(NDailyRunScreen._Ready));
	}

	public static void Postfix(Control __instance)
	{
		LobbyPanel.Attach(__instance);
	}
}
