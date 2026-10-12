using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;

namespace HandAndBrain.Patches;

/// <summary>
/// Adds the recipient's name to the card reward screen's banner, and a button that opens the
/// recipient's deck in the regular deck view.
/// </summary>
public static class RewardScreenDecorations
{
	private static readonly ConditionalWeakTable<NCardRewardSelectionScreen, Player> _recipients = new ConditionalWeakTable<NCardRewardSelectionScreen, Player>();

	private static readonly AccessTools.FieldRef<NCardRewardSelectionScreen, NCommonBanner> _banner = AccessTools.FieldRefAccess<NCardRewardSelectionScreen, NCommonBanner>("_banner");

	private static readonly AccessTools.FieldRef<NCardRewardSelectionScreen, Control> _rewardAlternativesContainer = AccessTools.FieldRefAccess<NCardRewardSelectionScreen, Control>("_rewardAlternativesContainer");

	public static void Decorate(NCardRewardSelectionScreen screen, Player recipient)
	{
		_recipients.AddOrUpdate(screen, recipient);
		LocString header = ModStrings.Get(ModStrings.ChooseCardHeader);
		header.Add("Player", Redirects.NameOf(recipient));
		_banner(screen).label.SetTextAutoSize(header.GetFormattedText());
		AddViewDeckButton(screen, recipient);
	}

	public static void OnOptionsRefreshed(NCardRewardSelectionScreen screen)
	{
		// RefreshOptions (e.g. after a reroll) frees every alternative button, including ours.
		if (_recipients.TryGetValue(screen, out Player? recipient))
		{
			AddViewDeckButton(screen, recipient);
		}
	}

	private static void AddViewDeckButton(NCardRewardSelectionScreen screen, Player recipient)
	{
		LocString label = ModStrings.Get(ModStrings.ViewDeck);
		label.Add("Player", Redirects.NameOf(recipient));
		// The draw pile hotkey is free outside of combat.
		NCardRewardAlternativeButton button = NCardRewardAlternativeButton.Create(label.GetFormattedText(), new string[1] { (string)MegaInput.viewDrawPile });
		((Node)(object)_rewardAlternativesContainer(screen)).AddChildSafely((Node?)(object)button);
		((GodotObject)button).Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(delegate
		{
			NDeckViewScreen.ShowScreen(recipient);
		}), 0u);
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
