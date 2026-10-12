using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;

namespace HandAndBrain.Patches;

/// <summary>Tracks which card rewards have been handed to another player to choose.</summary>
public static class Redirects
{
	private static readonly ConditionalWeakTable<Reward, Player> _chooserByReward = new ConditionalWeakTable<Reward, Player>();

	public static void Register(Reward reward, Player chooser)
	{
		_chooserByReward.AddOrUpdate(reward, chooser);
	}

	/// <summary>The player choosing this reward on the recipient's behalf, or null for a normal reward.</summary>
	public static Player? ChooserOf(Reward reward)
	{
		return _chooserByReward.TryGetValue(reward, out Player? chooser) ? chooser : null;
	}

	public static string NameOf(Player player)
	{
		return PlatformUtil.GetPlayerName(RunManager.Instance.NetService.Platform, player.NetId);
	}

	/// <summary>
	/// Moves each player's end-of-combat card rewards into the rewards set of the player who
	/// chooses for them. Runs identically on every peer, so the result is deterministic.
	/// </summary>
	public static void RedirectCardRewards(IReadOnlyList<RewardsSet> sets)
	{
		HandAndBrainSettings? settings = HandAndBrainState.Active;
		if (settings == null || !settings.IsInEffect)
		{
			return;
		}
		Dictionary<ulong, RewardsSet> setsByPlayer = sets.ToDictionary((RewardsSet s) => s.Player.NetId);
		List<(RewardsSet from, RewardsSet to, CardReward reward)> moves = new List<(RewardsSet, RewardsSet, CardReward)>();
		foreach (RewardsSet recipientSet in sets)
		{
			ulong? chooserId = settings.ChooserOf(recipientSet.Player.NetId);
			if (!chooserId.HasValue || chooserId.Value == recipientSet.Player.NetId || !setsByPlayer.TryGetValue(chooserId.Value, out RewardsSet? chooserSet))
			{
				continue;
			}
			// Rewards sets of dead players are never offered, so leave things as vanilla would.
			if (recipientSet.Player.Creature.IsDead || chooserSet.Player.Creature.IsDead)
			{
				continue;
			}
			foreach (CardReward reward in recipientSet.Rewards.OfType<CardReward>())
			{
				moves.Add((recipientSet, chooserSet, reward));
			}
		}
		foreach (var (from, to, reward) in moves)
		{
			from.Rewards.Remove(reward);
			to.Rewards.Add(reward);
			Register(reward, to.Player);
			Log.Info($"[HandAndBrain] Player {to.Player.NetId} will choose the card reward of player {from.Player.NetId}");
		}
		foreach (RewardsSet set in moves.Select((m) => m.to).Distinct())
		{
			List<Reward> sorted = set.Rewards.OrderBy((Reward r) => r.RewardsSetIndex).ToList();
			set.Rewards.Clear();
			set.Rewards.AddRange(sorted);
		}
	}
}

/// <summary>Same as vanilla, but hands card rewards to the choosing player before they are offered.</summary>
[HarmonyPatch(typeof(CombatRoom), nameof(CombatRoom.OfferRoomEndRewards))]
public static class OfferRoomEndRewardsPatch
{
	public static bool Prefix(CombatRoom __instance, ref Task __result)
	{
		if (!HandAndBrainState.IsActive)
		{
			return true;
		}
		__result = OfferRoomEndRewards(__instance);
		return false;
	}

	private static async Task OfferRoomEndRewards(CombatRoom room)
	{
		List<RewardsSet> sets = new List<RewardsSet>();
		foreach (Player player in room.CombatState.Players)
		{
			sets.Add(await RewardsCmd.GenerateForRoomEnd(player, room));
		}
		Redirects.RedirectCardRewards(sets);
		foreach (RewardsSet set in sets)
		{
			await Hook.BeforeCombatRewardOffered(set, room.CombatState.RunState, room);
			_ = TaskHelper.RunSafely(set.Offer());
		}
	}
}

/// <summary>
/// Vanilla refuses to select a reward that isn't owned by the local player. Redirected card
/// rewards sit in the chooser's set but belong to the recipient, so do the same work without that check.
/// </summary>
[HarmonyPatch(typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.SelectLocalReward))]
public static class SelectLocalRewardPatch
{
	private static readonly AccessTools.FieldRef<RewardsSetSynchronizer, RunLocationTargetedMessageBuffer> _messageBuffer = AccessTools.FieldRefAccess<RewardsSetSynchronizer, RunLocationTargetedMessageBuffer>("_messageBuffer");

	private static readonly AccessTools.FieldRef<RewardsSetSynchronizer, INetGameService> _netService = AccessTools.FieldRefAccess<RewardsSetSynchronizer, INetGameService>("_netService");

	private static readonly MethodInfo _getLocalPlayer = AccessTools.PropertyGetter(typeof(RewardsSetSynchronizer), "LocalPlayer");

	private static readonly MethodInfo _getRewardStateForPlayer = AccessTools.Method(typeof(RewardsSetSynchronizer), "GetRewardStateForPlayer");

	public static bool Prefix(RewardsSetSynchronizer __instance, Reward reward, ref Task<bool> __result)
	{
		Player? chooser = Redirects.ChooserOf(reward);
		if (chooser == null)
		{
			return true;
		}
		Player localPlayer = (Player)_getLocalPlayer.Invoke(__instance, null)!;
		if (chooser != localPlayer)
		{
			return true;
		}
		__result = SelectRedirectedReward(__instance, localPlayer, reward);
		return false;
	}

	private static async Task<bool> SelectRedirectedReward(RewardsSetSynchronizer synchronizer, Player localPlayer, Reward reward)
	{
		object rewardState = _getRewardStateForPlayer.Invoke(synchronizer, new object[] { localPlayer })!;
		IList rewardsStack = (IList)AccessTools.Field(rewardState.GetType(), "rewardsStack").GetValue(rewardState)!;
		if (rewardsStack.Count <= 0)
		{
			throw new InvalidOperationException("Tried to sync reward for local player, but they are not currently viewing any reward set!");
		}
		object setState = rewardsStack[rewardsStack.Count - 1]!;
		RewardsSet set = (RewardsSet)AccessTools.Field(setState.GetType(), "set").GetValue(setState)!;
		RewardSelectedMessage message = new RewardSelectedMessage
		{
			location = _messageBuffer(synchronizer).CurrentLocation,
			setId = set.Id,
			rewardIndex = set.Rewards.IndexOf(reward)
		};
		_netService(synchronizer).SendMessage(message);
		MethodInfo selectRewardForPlayer = AccessTools.Method(typeof(RewardsSetSynchronizer), "SelectRewardForPlayer", new Type[] { setState.GetType(), typeof(Reward) });
		return await (Task<bool>)selectRewardForPlayer.Invoke(synchronizer, new object[] { setState, reward })!;
	}
}

/// <summary>
/// A redirected card reward is chosen by another player: the chooser sees the screen and
/// sends the choice; every other peer (including the recipient) waits for the chooser.
/// The cards, relic effects and run history all stay the recipient's.
/// </summary>
[HarmonyPatch(typeof(CardReward), "OnSelect")]
public static class CardRewardOnSelectPatch
{
	private static readonly AccessTools.FieldRef<CardReward, List<CardCreationResult>> _cards = AccessTools.FieldRefAccess<CardReward, List<CardCreationResult>>("_cards");

	private static readonly AccessTools.FieldRef<CardReward, PlayerChoiceSynchronizer> _synchronizer = AccessTools.FieldRefAccess<CardReward, PlayerChoiceSynchronizer>("_synchronizer");

	private static readonly AccessTools.FieldRef<CardReward, NCardRewardSelectionScreen?> _currentlyShownScreen = AccessTools.FieldRefAccess<CardReward, NCardRewardSelectionScreen?>("_currentlyShownScreen");

	private static readonly MethodInfo _onRelicObtained = AccessTools.Method(typeof(CardReward), "OnRelicObtained");

	public static bool Prefix(CardReward __instance, ref Task<bool> __result)
	{
		Player? chooser = Redirects.ChooserOf(__instance);
		if (chooser == null)
		{
			return true;
		}
		__result = OnSelectForRecipient(__instance, chooser);
		return false;
	}

	// Mirrors CardReward.OnSelect, with the chooser driving the UI and the network choice.
	private static async Task<bool> OnSelectForRecipient(CardReward reward, Player chooser)
	{
		Player recipient = reward.Player;
		List<CardCreationResult> cards = _cards(reward);
		PlayerChoiceSynchronizer synchronizer = _synchronizer(reward);
		bool chooserIsLocal = LocalContext.IsMe(chooser);
		Log.Info($"[HandAndBrain] Player {chooser.NetId} selected the card reward of player {recipient.NetId}");
		bool rewardComplete = false;
		bool endSelection = false;
		List<CardModel> chosenCards = new List<CardModel>();
		IReadOnlyList<CardRewardAlternative> alternatives = CardRewardAlternative.Generate(reward);
		if (chooserIsLocal)
		{
			NCardRewardSelectionScreen? screen = NCardRewardSelectionScreen.ShowScreen(cards, alternatives);
			_currentlyShownScreen(reward) = screen;
			if (screen != null)
			{
				RewardScreenDecorations.Decorate(screen, recipient);
			}
		}
		while (!endSelection)
		{
			uint choiceId = synchronizer.ReserveChoiceId(chooser);
			int? index;
			if (chooserIsLocal)
			{
				NCardRewardSelectionScreen? screen = _currentlyShownScreen(reward);
				if (screen == null)
				{
					throw new InvalidOperationException("Hand and Brain does not support card rewards without a selection screen.");
				}
				index = await screen.OptionSelected();
				synchronizer.SyncLocalChoice(chooser, choiceId, PlayerChoiceResult.FromIndex(index));
			}
			else
			{
				index = (await synchronizer.WaitForRemoteChoice(chooser, choiceId)).AsIndexOrNull();
			}
			CardModel? obtainedCard = null;
			CardRewardAlternative? alternative = null;
			NCardHolder? cardHolder = null;
			if (index.HasValue)
			{
				if (index.Value < cards.Count)
				{
					obtainedCard = cards[index.Value].Card;
					rewardComplete = true;
					endSelection = !Hook.ShouldAllowSelectingMoreCardRewards(recipient.RunState, recipient, reward);
					cardHolder = _currentlyShownScreen(reward)?.GetCardHolder(obtainedCard);
				}
				else
				{
					if (index.Value >= cards.Count + alternatives.Count)
					{
						Log.Error($"[HandAndBrain] Received bad player choice index {index} for a card reward with {cards.Count} cards and {alternatives.Count} alternatives!");
						continue;
					}
					alternative = alternatives[index.Value - cards.Count];
					rewardComplete = alternative.AfterSelected == PostAlternateCardRewardAction.EndSelectionAndCompleteReward;
					endSelection = alternative.AfterSelected == PostAlternateCardRewardAction.EndSelectionAndCompleteReward || alternative.AfterSelected == PostAlternateCardRewardAction.EndSelectionAndDoNotCompleteReward;
				}
			}
			else
			{
				rewardComplete = false;
				endSelection = true;
			}
			if (obtainedCard == null && alternative == null && !rewardComplete)
			{
				continue;
			}
			if (obtainedCard != null)
			{
				// The card is owned by the recipient, so it goes into the recipient's deck.
				CardPileAddResult result = await CardPileCmd.Add(obtainedCard, PileType.Deck);
				if (result.success)
				{
					CardModel added = result.cardAdded;
					chosenCards.Add(added);
					cards.RemoveAll((CardCreationResult c) => c.Card == obtainedCard);
					if (cardHolder != null)
					{
						((Node)(object)cardHolder).QueueFreeSafely();
					}
					Log.Info($"[HandAndBrain] Player {chooser.NetId} chose {added.Id} for player {recipient.NetId}");
				}
			}
			else if (alternative != null)
			{
				await alternative.OnSelect();
			}
		}
		recipient.RelicObtained -= (Action<RelicModel>)Delegate.CreateDelegate(typeof(Action<RelicModel>), reward, _onRelicObtained);
		foreach (CardModel card in chosenCards)
		{
			recipient.RunState.CurrentMapPointHistoryEntry.GetEntry(recipient.NetId).CardChoices.Add(new CardChoiceHistoryEntry(card, wasPicked: true));
		}
		if (rewardComplete)
		{
			foreach (CardCreationResult card in cards)
			{
				recipient.RunState.CurrentMapPointHistoryEntry.GetEntry(recipient.NetId).CardChoices.Add(new CardChoiceHistoryEntry(card.Card, wasPicked: false));
			}
		}
		NCardRewardSelectionScreen? shownScreen = _currentlyShownScreen(reward);
		if (shownScreen != null)
		{
			NOverlayStack.Instance?.Remove(shownScreen);
			_currentlyShownScreen(reward) = null;
		}
		return rewardComplete;
	}
}

/// <summary>Reward button text: "Choose a card for {Player}".</summary>
[HarmonyPatch(typeof(CardReward), nameof(CardReward.Description), MethodType.Getter)]
public static class CardRewardDescriptionPatch
{
	public static void Postfix(CardReward __instance, ref LocString __result)
	{
		if (Redirects.ChooserOf(__instance) == null)
		{
			return;
		}
		LocString text = ModStrings.Get(ModStrings.RewardDescription);
		text.Add("Player", Redirects.NameOf(__instance.Player));
		__result = text;
	}
}
