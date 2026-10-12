using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization;

namespace HandAndBrain;

/// <summary>
/// The mod's in-game strings. They are merged into the game's "gameplay_ui" table on demand
/// (and again after a language change reloads the tables), so they can be used as LocStrings.
/// </summary>
public static class ModStrings
{
	private const string Table = "gameplay_ui";

	public const string RewardDescription = "HAND_AND_BRAIN.REWARD_DESCRIPTION";

	public const string ChooseCardHeader = "HAND_AND_BRAIN.CHOOSE_CARD_HEADER";

	public const string ViewDeck = "HAND_AND_BRAIN.VIEW_DECK";

	public const string HideCards = "HAND_AND_BRAIN.HIDE_CARDS";

	public const string ShowCards = "HAND_AND_BRAIN.SHOW_CARDS";

	public const string PreviewWaiting = "HAND_AND_BRAIN.PREVIEW_WAITING";

	public const string PreviewChoosing = "HAND_AND_BRAIN.PREVIEW_CHOOSING";

	public const string PreviewHiddenCards = "HAND_AND_BRAIN.PREVIEW_HIDDEN_CARDS";

	public const string PreviewSkipped = "HAND_AND_BRAIN.PREVIEW_SKIPPED";

	public const string ChosenBy = "HAND_AND_BRAIN.CHOSEN_BY";

	private static readonly Dictionary<string, string> _english = new Dictionary<string, string>
	{
		[RewardDescription] = "Choose a card for {Player}",
		[ChooseCardHeader] = "Choose a Card for {Player}",
		[ViewDeck] = "{Player}'s Deck",
		[HideCards] = "Hide from {Player}",
		[ShowCards] = "Show to {Player}",
		[PreviewWaiting] = "{Player} will choose a card for you",
		[PreviewChoosing] = "{Player} is choosing for you from:",
		[PreviewHiddenCards] = "(Hidden)",
		[PreviewSkipped] = "{Player} skipped your card reward",
		[ChosenBy] = "Chosen by {Player}"
	};

	/// <summary>The formatted string with {Player} filled in.</summary>
	public static string Format(string key, string playerName)
	{
		LocString text = Get(key);
		text.Add("Player", playerName);
		return text.GetFormattedText();
	}

	public static LocString Get(string key)
	{
		EnsureMerged();
		return new LocString(Table, key);
	}

	private static void EnsureMerged()
	{
		LocTable table = LocManager.Instance.GetTable(Table);
		if (!table.HasEntry(RewardDescription))
		{
			table.MergeWith(new Dictionary<string, string>(_english));
		}
	}
}
