using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.addons.mega_text;

namespace HandAndBrain.Patches;

/// <summary>
/// Adds the recipient's name to the card reward screen's banner, a button (and R hotkey) that
/// opens the recipient's deck in the regular deck view, and a toggle that hides the offered
/// cards from the recipient.
/// </summary>
public static class RewardScreenDecorations
{
	/// <summary>Input action for the R hotkey; registered in <see cref="ModEntry.Init"/>.</summary>
	public const string ViewDeckAction = "hand_and_brain_view_recipient_deck";

	private static readonly ConditionalWeakTable<NCardRewardSelectionScreen, Player> _recipients = new ConditionalWeakTable<NCardRewardSelectionScreen, Player>();

	private static readonly AccessTools.FieldRef<NCardRewardSelectionScreen, NCommonBanner> _banner = AccessTools.FieldRefAccess<NCardRewardSelectionScreen, NCommonBanner>("_banner");

	private static readonly AccessTools.FieldRef<NCardRewardSelectionScreen, Control> _rewardAlternativesContainer = AccessTools.FieldRefAccess<NCardRewardSelectionScreen, Control>("_rewardAlternativesContainer");

	public static void RegisterInputActions()
	{
		if (InputMap.HasAction(ViewDeckAction))
		{
			return;
		}
		InputMap.AddAction(ViewDeckAction);
		InputMap.ActionAddEvent(ViewDeckAction, new InputEventKey
		{
			Keycode = Key.R
		});
	}

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

		// The draw pile hotkey is free outside of combat; R is our own action.
		NCardRewardAlternativeButton deckButton = NCardRewardAlternativeButton.Create(ModStrings.Format(ModStrings.ViewDeck, name), new string[2] { (string)MegaInput.viewDrawPile, ViewDeckAction });
		((Node)(object)container).AddChildSafely((Node?)(object)deckButton);
		((GodotObject)deckButton).Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(delegate
		{
			// The hotkey stays bound while the deck view is open on top of this screen.
			if (ActiveScreenContext.Instance.GetCurrentScreen() == screen)
			{
				NDeckViewScreen.ShowScreen(recipient);
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
