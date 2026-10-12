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

	private static readonly Dictionary<string, string> _english = new Dictionary<string, string>
	{
		[RewardDescription] = "Choose a card for {Player}",
		[ChooseCardHeader] = "Choose a Card for {Player}",
		[ViewDeck] = "{Player}'s Deck"
	};

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
