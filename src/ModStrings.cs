using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Localization;

namespace HandAndBrain;

/// <summary>
/// The mod's in-game strings. They are merged into the game's "gameplay_ui" and "settings_ui"
/// tables on demand (and again after a language change reloads the tables), so they can be used
/// as LocStrings.
/// </summary>
public static class ModStrings
{
	private const string Table = "gameplay_ui";

	private const string SettingsTable = "settings_ui";

	/// <summary>Key in commandToLocTitle for the keybinding row; the settings menu looks up INPUT_SETTINGS.INPUT_TITLE.{this}.</summary>
	public const string KeybindTitle = "handAndBrainViewDeck";

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

	public const string HoverTitle = "HAND_AND_BRAIN.HOVER_TITLE";

	public const string HoverPicksFor = "HAND_AND_BRAIN.HOVER_PICKS_FOR";

	public const string HoverPickedBy = "HAND_AND_BRAIN.HOVER_PICKED_BY";

	public const string HoverYouPickFor = "HAND_AND_BRAIN.HOVER_YOU_PICK_FOR";

	public const string HoverPicksForYou = "HAND_AND_BRAIN.HOVER_PICKS_FOR_YOU";

	public const string HoverHotkey = "HAND_AND_BRAIN.HOVER_HOTKEY";

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
		[ChosenBy] = "Chosen by {Player}",
		[HoverTitle] = "Hand and Brain",
		[HoverPicksFor] = "Picks card rewards for [gold]{Player}[/gold].",
		[HoverPickedBy] = "Card rewards picked by [gold]{Player}[/gold].",
		[HoverYouPickFor] = "You pick card rewards for [gold]{Player}[/gold].",
		[HoverPicksForYou] = "[gold]{Player}[/gold] picks your card rewards.",
		[HoverHotkey] = "Press [gold]{Hotkey}[/gold] to view [gold]{Player}[/gold]'s deck."
	};

	private static readonly Dictionary<string, string> _englishSettings = new Dictionary<string, string>
	{
		["INPUT_SETTINGS.INPUT_TITLE." + KeybindTitle] = "Hand and Brain: View Deck You Pick For"
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

	public static void EnsureMerged()
	{
		Merge(Table, _english);
		Merge(SettingsTable, _englishSettings);
	}

	private static void Merge(string tableName, Dictionary<string, string> entries)
	{
		LocTable table = LocManager.Instance.GetTable(tableName);
		if (!table.HasEntry(entries.Keys.First()))
		{
			table.MergeWith(new Dictionary<string, string>(entries));
		}
	}
}
