using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Debug;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.addons.mega_text;

namespace HandAndBrain.Patches;

/// <summary>
/// Adds the recipient's name to the card reward screen's banner, a button that opens the
/// recipient's deck in the regular deck view, and a toggle that hides the offered cards from
/// the recipient.
/// </summary>
public static class RewardScreenDecorations
{
	private static readonly ConditionalWeakTable<NCardRewardSelectionScreen, Player> _recipients = new ConditionalWeakTable<NCardRewardSelectionScreen, Player>();

	private static readonly AccessTools.FieldRef<NCardRewardSelectionScreen, NCommonBanner> _banner = AccessTools.FieldRefAccess<NCardRewardSelectionScreen, NCommonBanner>("_banner");

	private static readonly AccessTools.FieldRef<NCardRewardSelectionScreen, Control> _rewardAlternativesContainer = AccessTools.FieldRefAccess<NCardRewardSelectionScreen, Control>("_rewardAlternativesContainer");

	public static void Decorate(NCardRewardSelectionScreen screen, Player recipient)
	{
		_recipients.AddOrUpdate(screen, recipient);
		_banner(screen).label.SetTextAutoSize(ModStrings.Format(ModStrings.ChooseCardHeader, Redirects.NameOf(recipient)));
		AddButtons(screen, recipient);
	}

	public static void OnOptionsRefreshed(NCardRewardSelectionScreen screen)
	{
		// RefreshOptions (e.g. after a reroll) frees every alternative button, including ours.
		if (_recipients.TryGetValue(screen, out Player? recipient))
		{
			AddButtons(screen, recipient);
		}
	}

	private static void AddButtons(NCardRewardSelectionScreen screen, Player recipient)
	{
		Control container = _rewardAlternativesContainer(screen);
		string name = Redirects.NameOf(recipient);

		// The draw pile hotkey is free outside of combat. Our own hotkey works everywhere
		// (RecipientDeckHotkey), so it isn't bound to the button as well, or it would toggle twice.
		NCardRewardAlternativeButton deckButton = NCardRewardAlternativeButton.Create(ModStrings.Format(ModStrings.ViewDeck, name), (string)MegaInput.viewDrawPile);
		((Node)(object)container).AddChildSafely((Node?)(object)deckButton);
		((GodotObject)deckButton).Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(delegate
		{
			// The hotkey stays bound while the deck view is open on top of this screen.
			if (ActiveScreenContext.Instance.GetCurrentScreen() == screen)
			{
				RecipientDeckHotkey.Toggle(recipient);
			}
		}), 0u);

		// Same idea for the discard pile hotkey.
		NCardRewardAlternativeButton hideButton = NCardRewardAlternativeButton.Create(HideButtonText(name), (string)MegaInput.viewDiscardPile);
		((Node)(object)container).AddChildSafely((Node?)(object)hideButton);
		((GodotObject)hideButton).Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(delegate
		{
			if (ActiveScreenContext.Instance.GetCurrentScreen() != screen)
			{
				return;
			}
			RunSync.SetHideFromRecipient(!RunSync.HideFromRecipient);
			((Node)(object)hideButton).GetNode<MegaLabel>("Label").SetTextAutoSize(HideButtonText(name));
		}), 0u);
	}

	private static string HideButtonText(string recipientName)
	{
		return ModStrings.Format(RunSync.HideFromRecipient ? ModStrings.ShowCards : ModStrings.HideCards, recipientName);
	}
}

[HarmonyPatch(typeof(NCardRewardSelectionScreen), nameof(NCardRewardSelectionScreen.RefreshOptions))]
public static class RefreshOptionsPatch
{
	public static void Postfix(NCardRewardSelectionScreen __instance)
	{
		RewardScreenDecorations.OnOptionsRefreshed(__instance);
	}
}

/// <summary>
/// The hotkey (R by default) opens the deck of the player you pick for, and closes it again,
/// anywhere in a hand-and-brain run, like the top bar's deck button does for your own deck.
/// </summary>
public static class RecipientDeckHotkey
{
	/// <summary>Input action for the hotkey; bound through the game's keybinding menu (see RecipientDeckKeybind).</summary>
	public const string Action = "hand_and_brain_view_recipient_deck";

	private static readonly AccessTools.FieldRef<NDeckViewScreen, Player> _deckViewPlayer = AccessTools.FieldRefAccess<NDeckViewScreen, Player>("_player");

	private static readonly Func<RunManager, RunState?> _runState = AccessTools.MethodDelegate<Func<RunManager, RunState?>>(AccessTools.PropertyGetter(typeof(RunManager), "State"));

	/// <summary>The player whose card rewards the local player picks, or null outside a hand-and-brain run.</summary>
	public static Player? LocalRecipient()
	{
		RunState? state = _runState(RunManager.Instance);
		HandAndBrainSettings? settings = HandAndBrainState.Active;
		if (state == null || settings == null || !settings.IsInEffect || RunManager.Instance.NetService.Type == NetGameType.Singleplayer)
		{
			return null;
		}
		Player? me = LocalContext.GetMe(state);
		ulong? targetId = me == null ? null : settings.TargetOf(me.NetId);
		if (targetId == null || targetId.Value == me!.NetId)
		{
			return null;
		}
		return state.Players.FirstOrDefault((Player p) => p.NetId == targetId.Value);
	}

	/// <summary>Handles a press of the hotkey. Returns false when it doesn't apply, so the game handles the input.</summary>
	public static bool OnPressed()
	{
		Player? recipient = LocalRecipient();
		NCapstoneContainer? capstones = NCapstoneContainer.Instance;
		if (recipient == null || capstones == null || NModalContainer.Instance?.OpenModal != null)
		{
			return false;
		}
		// Closing is always allowed. Opening follows the top bar's deck button, which is disabled
		// whenever the game doesn't let you look at decks.
		NButton? ownDeckButton = NRun.Instance?.GlobalUi.TopBar.Deck;
		if (!IsShowing(recipient) && ownDeckButton != null && (!ownDeckButton.IsEnabled || !ownDeckButton.IsVisibleInTree()))
		{
			return false;
		}
		Toggle(recipient);
		return true;
	}

	private static bool IsShowing(Player player)
	{
		return NCapstoneContainer.Instance?.CurrentCapstoneScreen is NDeckViewScreen deckView && _deckViewPlayer(deckView) == player;
	}

	public static void Toggle(Player recipient)
	{
		NCapstoneContainer? capstones = NCapstoneContainer.Instance;
		if (capstones == null)
		{
			return;
		}
		if (IsShowing(recipient))
		{
			capstones.Close();
		}
		else
		{
			NDeckViewScreen.ShowScreen(recipient);
		}
	}
}

/// <summary>
/// The game's input manager turns the bound key into our action; catch it before the hotkey
/// bindings of whatever screen is open.
/// </summary>
[HarmonyPatch(typeof(NHotkeyManager), nameof(NHotkeyManager._UnhandledInput))]
public static class RecipientDeckHotkeyPatch
{
	public static bool Prefix(NHotkeyManager __instance, InputEvent inputEvent)
	{
		if (!inputEvent.IsActionPressed(RecipientDeckHotkey.Action) || inputEvent.IsEcho() || !NGame.IsGameFocusedWindow() || NDevConsole.IsConsoleVisible)
		{
			return true;
		}
		Viewport viewport = __instance.GetViewport();
		Control? focus = viewport?.GuiGetFocusOwner();
		if ((focus is LineEdit lineEdit && lineEdit.IsEditing()) || (focus is NMegaTextEdit textEdit && textEdit.IsEditing()))
		{
			return true;
		}
		if (!RecipientDeckHotkey.OnPressed())
		{
			return true;
		}
		viewport?.SetInputAsHandled();
		return false;
	}
}
