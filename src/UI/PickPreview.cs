using System.Collections.Generic;
using System.Linq;
using Godot;
using HandAndBrain.Patches;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Runs;

namespace HandAndBrain.UI;

/// <summary>Shared look for the mod's in-run labels.</summary>
public static class ModUi
{
	private static Font? _font;

	public static Font? LabelFont => _font ??= ResourceLoader.Load<Font>("res://fonts/kreon_bold.ttf", null, ResourceLoader.CacheMode.Reuse);

	public static Label MakeLabel(string text, int fontSize)
	{
		Label label = new Label
		{
			Text = text,
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		if (LabelFont is Font font)
		{
			label.AddThemeFontOverride("font", font);
		}
		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.AddThemeColorOverride("font_color", StsColors.cream);
		label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.8f));
		label.AddThemeConstantOverride("outline_size", 10);
		return label;
	}

	/// <summary>Size of a label's text when wrapped to <paramref name="width"/> (-1 for a single line).</summary>
	public static Vector2 MeasureText(string text, int fontSize, float width = -1f)
	{
		// Room for the spacing between wrapped lines, which the font doesn't know about.
		const float lineSpacing = 4f;
		Font font = LabelFont ?? ThemeDB.FallbackFont;
		Vector2 size = font.GetMultilineStringSize(text, HorizontalAlignment.Center, width, fontSize);
		float lineHeight = font.GetHeight(fontSize);
		return new Vector2(Mathf.Ceil(size.X), size.Y + lineSpacing * Mathf.Max(1f, Mathf.Round(size.Y / lineHeight)));
	}

	/// <summary>
	/// Fills in a card node's title, cost and text. NCard.Create hands out pooled nodes: a reused
	/// node is already ready and still shows whatever card it displayed last (often one from
	/// someone's deck) until UpdateVisuals is called. A new node fills its text in once ready.
	/// </summary>
	public static void UpdateCardVisuals(NCard node, PileType pile)
	{
		if (node.IsNodeReady())
		{
			node.UpdateVisuals(pile, CardPreviewMode.Normal);
			return;
		}
		node.Connect(Node.SignalName.Ready, Callable.From(delegate
		{
			if (node.Model != null)
			{
				node.UpdateVisuals(pile, CardPreviewMode.Normal);
			}
		}), (uint)GodotObject.ConnectFlags.OneShot);
	}
}

/// <summary>
/// Tells the recipient of a redirected card reward what happened to it. When the preview panel
/// is showing, the chosen card lifts out of it, grows, and flies into the deck, and a skip makes
/// the panel pulse. Elsewhere (e.g. on the map) the game's usual card preview and a centred
/// message are used instead.
/// </summary>
public static class PickNotifications
{
	private const float PreviewTime = 2f;

	public static void CardChosen(CardPileAddResult result, CardModel offered, Player chooser)
	{
		CardModel card = result.cardAdded;
		if (!LocalContext.IsMine(card) || LocalContext.IsMe(chooser))
		{
			return;
		}
		string chosenBy = ModStrings.Format(ModStrings.ChosenBy, Redirects.NameOf(chooser));
		if (PickPreviewPanel.Current?.AnimateChosen(offered, card, chosenBy) == true)
		{
			return;
		}
		CardCmd.PreviewCardPileAdd(result, PreviewTime);
		// The preview adds an NCard for the card to one of these containers; label it.
		NCard? node = new Node?[] { NRun.Instance?.GlobalUi.CardPreviewContainer, NRun.Instance?.GlobalUi.MessyCardPreviewContainer }
			.Where((Node? container) => container != null)
			.SelectMany((Node? container) => container!.GetChildren().OfType<NCard>())
			.LastOrDefault((NCard n) => n.Model == card);
		if (node != null)
		{
			AddChosenByLabel(node, chosenBy, PreviewTime);
		}
	}

	/// <summary>"Chosen by X" above a full-size card, fading out just before the card flies off.</summary>
	public static void AddChosenByLabel(NCard node, string text, float fadeDelay)
	{
		Label label = ModUi.MakeLabel(text, 36);
		label.AddThemeColorOverride("font_color", StsColors.gold);
		label.Size = new Vector2(500f, 50f);
		label.Position = new Vector2(-250f, -NCard.defaultSize.Y * 0.5f - 60f);
		node.AddChildSafely(label);
		Tween tween = label.CreateTween();
		tween.TweenProperty(label, "modulate:a", 0f, 0.2).SetDelay(fadeDelay + 0.05f);
	}

	public static void RewardSkipped(RedirectedReward redirected)
	{
		if (!LocalContext.IsMe(redirected.Recipient) || LocalContext.IsMe(redirected.Chooser) || NRun.Instance == null)
		{
			return;
		}
		if (PickPreviewPanel.Current?.Pulse() == true)
		{
			return;
		}
		Control parent = NRun.Instance.GlobalUi.CardPreviewContainer;
		Label label = ModUi.MakeLabel(ModStrings.Format(ModStrings.PreviewSkipped, Redirects.NameOf(redirected.Chooser)), 44);
		label.AddThemeColorOverride("font_color", StsColors.gold);
		label.TopLevel = true;
		Vector2 viewport = parent.GetViewportRect().Size;
		label.Size = new Vector2(900f, 60f);
		label.PivotOffset = label.Size * 0.5f;
		label.Position = new Vector2((viewport.X - label.Size.X) * 0.5f, viewport.Y * 0.4f);
		parent.AddChildSafely(label);
		Tween tween = label.CreateTween();
		tween.TweenProperty(label, "scale", Vector2.One, 0.25).From(Vector2.One * 0.5f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
		tween.Parallel().TweenProperty(label, "modulate:a", 1f, 0.2).From(0f);
		tween.TweenInterval(1.6);
		tween.TweenProperty(label, "modulate:a", 0f, 0.4);
		tween.TweenCallback(Callable.From(label.QueueFree));
	}
}

/// <summary>
/// On the rewards and card reward screens, shows the recipient what their chooser is being
/// offered for them (unless the chooser hides it), and afterwards what was picked or skipped.
/// Sits in the top right corner, under the top bar and right of the screens' centred banners.
/// A second, smaller panel below it shows what is being offered to the other players.
/// </summary>
public sealed class PickPreviewPanel
{
	private const string NodeName = "HandAndBrainPickPreview";

	/// <summary>Just below the top bar.</summary>
	private const float MinTop = 112f;

	/// <summary>
	/// Gap under the game's version / seed / "modded" text in the top right corner. Negative because
	/// that text's box is a little taller than the text itself.
	/// </summary>
	private const float DebugInfoGap = -4f;

	private const float RightMargin = 20f;

	/// <summary>Half the width of the "Choose a Card" / rewards banners, which are centred on screen.</summary>
	private const float BannerHalfWidth = 327f;

	private const float BannerGap = 24f;

	private const float MinWidth = 260f;

	private const float MaxCardScale = 0.5f;

	private const float CardGap = 8f;

	/// <summary>The panel's border plus content margins on each side.</summary>
	private const float PanelPadding = 14f;

	private const float LabelGap = 8f;

	private const float SectionGap = 16f;

	private const int LabelFontSize = 22;

	private const float PanelGap = 12f;

	private const int OthersLabelFontSize = 20;

	private const float OthersMaxCardScale = 0.42f;

	/// <summary>Below this the cards are too small to make out, so the other players' panel is left out.</summary>
	private const float OthersMinCardScale = 0.2f;

	/// <summary>Distance between neighbouring cards on the card reward screen.</summary>
	private const float RewardCardSpacing = 350f;

	/// <summary>Kept free around the card reward screen's cards and the buttons the panel must not cover.</summary>
	private const float ObstacleGap = 16f;

	/// <summary>Top of the card reward screen's button row (Skip etc.), measured from the screen's centre.</summary>
	private const float RewardButtonsTop = 344f;

	/// <summary>Top of the rewards screen's proceed button, as a fraction of the screen height.</summary>
	private const float ProceedButtonTopRatio = 764f / 1080f;

	private const float HoldScale = 0.7f;

	private const float HoldTime = 1.6f;

	private static readonly AccessTools.FieldRef<NCardRewardSelectionScreen, Control> _rewardCardRow = AccessTools.FieldRefAccess<NCardRewardSelectionScreen, Control>("_cardRow");

	private static readonly AccessTools.FieldRef<NCardRewardSelectionScreen, IReadOnlyList<CardCreationResult>> _rewardOptions = AccessTools.FieldRefAccess<NCardRewardSelectionScreen, IReadOnlyList<CardCreationResult>>("_options");

	private static PickPreviewPanel? _current;

	private readonly NGlobalUi _globalUi;

	private readonly Control _root;

	/// <summary>Holds both panels, pinned to the top right corner.</summary>
	private readonly VBoxContainer _stack;

	/// <summary>The local player's own card reward.</summary>
	private readonly PanelContainer _panel;

	private readonly StyleBoxFlat _style;

	private readonly VBoxContainer _sections;

	/// <summary>The other players' card rewards.</summary>
	private readonly PanelContainer _othersPanel;

	private readonly GridContainer _othersGrid;

	private string? _renderedKey;

	private Tween? _pulseTween;

	/// <summary>The panel, if it exists and the local player's own card reward is showing in it.</summary>
	public static PickPreviewPanel? Current
	{
		get
		{
			if (_current == null || !GodotObject.IsInstanceValid(_current._root) || !_current._panel.IsVisibleInTree())
			{
				return null;
			}
			return _current;
		}
	}

	private PickPreviewPanel(NGlobalUi globalUi)
	{
		_globalUi = globalUi;
		_root = new Control
		{
			Name = NodeName,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false
		};

		_root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		// Grows left and down to fit its content.
		_stack = new VBoxContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_stack.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		_stack.GrowHorizontal = Control.GrowDirection.Begin;
		_stack.GrowVertical = Control.GrowDirection.End;
		_stack.AddThemeConstantOverride("separation", (int)PanelGap);
		_root.AddChild(_stack);

		(_panel, _style) = MakePanel(new Color(StsColors.gold, 0.6f));
		_sections = new VBoxContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_sections.AddThemeConstantOverride("separation", (int)SectionGap);
		_panel.AddChild(_sections);
		_stack.AddChild(_panel);

		(_othersPanel, _) = MakePanel(new Color(StsColors.cream, 0.35f));
		_othersGrid = new GridContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_othersGrid.AddThemeConstantOverride("h_separation", (int)SectionGap);
		_othersGrid.AddThemeConstantOverride("v_separation", (int)SectionGap);
		_othersPanel.AddChild(_othersGrid);
		_stack.AddChild(_othersPanel);

		Timer timer = new Timer
		{
			WaitTime = 0.2,
			OneShot = false,
			Autostart = true
		};
		timer.Timeout += Refresh;
		_root.AddChild(timer);

		globalUi.AddChild(_root);
		// Just above the overlay screens (rewards, card reward), below the map, deck view and top bar.
		globalUi.MoveChild(_root, globalUi.Overlays.GetIndex() + 1);
		_current = this;
	}

	private static (PanelContainer, StyleBoxFlat) MakePanel(Color borderColor)
	{
		PanelContainer panel = new PanelContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Visible = false,
			// Each panel is only as wide as its own content, against the right edge.
			SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd
		};
		StyleBoxFlat style = new StyleBoxFlat
		{
			BgColor = new Color(0.05f, 0.06f, 0.1f, 0.75f),
			BorderColor = borderColor
		};
		style.SetBorderWidthAll(2);
		style.SetCornerRadiusAll(10);
		style.SetContentMarginAll(12f);
		panel.AddThemeStyleboxOverride("panel", style);
		return (panel, style);
	}

	public static void Attach()
	{
		NGlobalUi? globalUi = NRun.Instance?.GlobalUi;
		if (globalUi != null && globalUi.GetNodeOrNull(NodeName) == null)
		{
			new PickPreviewPanel(globalUi);
		}
	}

	private static bool IsOnRewardScreens()
	{
		IScreenContext? screen = ActiveScreenContext.Instance.GetCurrentScreen();
		return screen is NRewardsScreen || screen is NCardRewardSelectionScreen;
	}

	/// <summary>Width of the free space between the centred banner and the right edge of the screen.</summary>
	private float AvailableWidth => _root.GetViewportRect().Size.X * 0.5f - BannerHalfWidth - BannerGap - RightMargin;

	/// <summary>Below the top bar and the version / seed / "modded" text that sits under it.</summary>
	private float Top
	{
		get
		{
			Control? debugInfo = _globalUi.GetNodeOrNull<Control>("%DebugInfo") ?? _globalUi.GetNodeOrNull<Control>("DebugInfo");
			if (debugInfo == null || !debugInfo.IsVisibleInTree())
			{
				return MinTop;
			}
			return Mathf.Max(MinTop, debugInfo.GetGlobalRect().End.Y + DebugInfoGap);
		}
	}

	/// <summary>Space for the other players' panel: its outer width, and where its bottom must stay above.</summary>
	private (float Width, float Bottom) OthersArea
	{
		get
		{
			Vector2 viewport = _root.GetViewportRect().Size;
			if (ActiveScreenContext.Instance.GetCurrentScreen() is not NCardRewardSelectionScreen screen)
			{
				return (AvailableWidth, viewport.Y * ProceedButtonTopRatio - ObstacleGap);
			}
			// Right of the outermost card. Cards slide apart when the screen opens, so go by how many
			// there are; when one is taken the rest stay put, so also by where they are.
			float outermost = (_rewardOptions(screen).Count - 1) * RewardCardSpacing * 0.5f;
			foreach (NGridCardHolder holder in _rewardCardRow(screen).GetChildren().OfType<NGridCardHolder>())
			{
				outermost = Mathf.Max(outermost, holder.Position.X);
			}
			// Cards grow to full size when hovered.
			float cardsRight = viewport.X * 0.5f + outermost + NCard.defaultSize.X * 0.5f;
			float width = viewport.X - RightMargin - cardsRight - ObstacleGap;
			return (Mathf.Min(AvailableWidth, width), viewport.Y * 0.5f + RewardButtonsTop - ObstacleGap);
		}
	}

	/// <summary>One redirected reward as the panel shows it.</summary>
	private readonly record struct Section(string Text, List<CardModel> Cards, bool Hidden)
	{
		public string Key => Text + ":" + Hidden + ":" + string.Join(",", Cards.Select((CardModel c) => c.GetHashCode()));
	}

	private void Refresh()
	{
		// Once a card has been picked the panel has nothing left to say (the pick animation
		// already showed it), so those rewards drop out. The rewards the local player chooses
		// are left out too: they see those on the card reward screen itself.
		List<RedirectedReward> rewards = HandAndBrainState.IsActive && IsOnRewardScreens()
			? Redirects.Current
				.Where((RedirectedReward r) => !LocalContext.IsMe(r.Chooser) && r.Recipient.RunState.CurrentRoom == r.Room)
				.ToList()
			: new List<RedirectedReward>();
		List<Section> mine = rewards.Where((RedirectedReward r) => LocalContext.IsMe(r.Recipient)).Select(Describe).OfType<Section>().ToList();
		List<Section> others = rewards.Where((RedirectedReward r) => !LocalContext.IsMe(r.Recipient)).Select(DescribeOther).OfType<Section>().ToList();
		_root.Visible = mine.Count + others.Count > 0;
		if (!_root.Visible)
		{
			_renderedKey = null;
			_panel.Visible = false;
			_othersPanel.Visible = false;
			return;
		}
		(float othersWidth, float othersBottom) = OthersArea;
		string key = string.Join("|", new float[] { AvailableWidth, Top, othersWidth, othersBottom }.Select((float f) => Mathf.RoundToInt(f).ToString()))
			+ "|" + string.Join("|", mine.Select((Section s) => s.Key))
			+ "||" + string.Join("|", others.Select((Section s) => s.Key));
		if (key == _renderedKey)
		{
			return;
		}
		_renderedKey = key;
		float top = Top;
		float mineHeight = Render(mine);
		RenderOthers(others, othersWidth, othersBottom - top - (mineHeight > 0f ? mineHeight + PanelGap : 0f));
		// Shrink back to the new content, still pinned to the corner.
		_stack.OffsetLeft = -RightMargin;
		_stack.OffsetRight = -RightMargin;
		_stack.OffsetTop = top;
		_stack.OffsetBottom = top;
	}

	private static Section? Describe(RedirectedReward redirected)
	{
		string name = Redirects.NameOf(redirected.Chooser);
		if (redirected.IsBeingChosen)
		{
			// Hidden cards keep their place in the panel, so it doesn't jump in size.
			return new Section(ModStrings.Format(ModStrings.PreviewChoosing, name), redirected.Reward.Cards.ToList(), RunSync.IsHiddenBy(redirected.Chooser.NetId));
		}
		if (redirected.Picked.Count > 0)
		{
			return null;
		}
		return new Section(ModStrings.Format(redirected.Skipped ? ModStrings.PreviewSkipped : ModStrings.PreviewWaiting, name), new List<CardModel>(), false);
	}

	/// <summary>A reward that neither belongs to the local player nor is chosen by them.</summary>
	private static Section? DescribeOther(RedirectedReward redirected)
	{
		string chooser = Redirects.NameOf(redirected.Chooser);
		string recipient = Redirects.NameOf(redirected.Recipient);
		if (redirected.IsBeingChosen)
		{
			// "Hide from Y" only hides the cards from Y, so everyone else still sees them.
			return new Section(ModStrings.Format(ModStrings.PreviewOtherChoosing, chooser, recipient), redirected.Reward.Cards.ToList(), false);
		}
		if (redirected.Picked.Count > 0)
		{
			return null;
		}
		return new Section(ModStrings.Format(redirected.Skipped ? ModStrings.PreviewOtherSkipped : ModStrings.PreviewOtherWaiting, chooser, recipient), new List<CardModel>(), false);
	}

	private static void Clear(Container container)
	{
		foreach (Node child in container.GetChildren())
		{
			container.RemoveChild(child);
			child.QueueFree();
		}
	}

	private static Vector2 CardRowSize(int count, float scale)
	{
		return new Vector2(count * NCard.defaultSize.X * scale + (count - 1) * CardGap, NCard.defaultSize.Y * scale);
	}

	/// <summary>The scale at which a row of cards is exactly <paramref name="width"/> wide.</summary>
	private static float ScaleToFit(int count, float width)
	{
		return (width - (count - 1) * CardGap) / count / NCard.defaultSize.X;
	}

	private static Control MakeSection(Section entry, int fontSize, float width, float scale)
	{
		VBoxContainer section = new VBoxContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		section.AddThemeConstantOverride("separation", (int)LabelGap);
		Label label = ModUi.MakeLabel(entry.Text, fontSize);
		label.CustomMinimumSize = new Vector2(width, 0f);
		section.AddChild(label);
		if (entry.Cards.Count > 0)
		{
			section.AddChild(entry.Hidden ? MakeHiddenRow(CardRowSize(entry.Cards.Count, scale)) : MakeCardRow(entry.Cards, scale));
		}
		return section;
	}

	/// <summary>Fills in the local player's panel. Returns the height it takes up.</summary>
	private float Render(List<Section> sections)
	{
		Clear(_sections);
		_panel.Visible = sections.Count > 0;
		if (sections.Count == 0)
		{
			return 0f;
		}
		// Cards sit in one row, as large as fits beside the banner (up to MaxCardScale).
		float rowWidth = AvailableWidth - 2f * PanelPadding;
		List<float> scales = sections.Select((Section s) => s.Cards.Count == 0 ? 0f : Mathf.Clamp(ScaleToFit(s.Cards.Count, rowWidth), 0.15f, MaxCardScale)).ToList();
		float width = MinWidth;
		for (int i = 0; i < sections.Count; i++)
		{
			width = Mathf.Max(width, CardRowSize(sections[i].Cards.Count, scales[i]).X);
		}
		width = Mathf.Min(width, rowWidth);
		float height = 2f * PanelPadding + (sections.Count - 1) * SectionGap;
		for (int i = 0; i < sections.Count; i++)
		{
			_sections.AddChild(MakeSection(sections[i], LabelFontSize, width, scales[i]));
			height += ModUi.MeasureText(sections[i].Text, LabelFontSize, width).Y;
			if (sections[i].Cards.Count > 0)
			{
				height += LabelGap + CardRowSize(sections[i].Cards.Count, scales[i]).Y;
			}
		}
		return height;
	}

	/// <summary>
	/// Fills in the other players' panel: a grid with however many columns allow the largest cards
	/// within the given space.
	/// </summary>
	private void RenderOthers(List<Section> sections, float maxWidth, float maxHeight)
	{
		Clear(_othersGrid);
		_othersPanel.Visible = false;
		int bestColumns = 0;
		float bestScale = 0f;
		float bestHeight = float.MaxValue;
		for (int columns = 1; columns <= sections.Count; columns++)
		{
			float columnWidth = OthersColumnWidth(maxWidth, columns);
			if (columnWidth <= 0f)
			{
				break;
			}
			// What's left of the height, after labels and gaps, is shared by the rows that have cards.
			float fixedHeight = 2f * PanelPadding - SectionGap;
			int cardRows = 0;
			float scale = OthersMaxCardScale;
			foreach (Section[] row in sections.Chunk(columns))
			{
				fixedHeight += SectionGap + row.Max((Section s) => ModUi.MeasureText(s.Text, OthersLabelFontSize, columnWidth).Y);
				if (row.Any((Section s) => s.Cards.Count > 0))
				{
					fixedHeight += LabelGap;
					cardRows++;
				}
			}
			foreach (Section section in sections.Where((Section s) => s.Cards.Count > 0))
			{
				scale = Mathf.Min(scale, ScaleToFit(section.Cards.Count, columnWidth));
			}
			if (cardRows > 0)
			{
				scale = Mathf.Min(scale, (maxHeight - fixedHeight) / cardRows / NCard.defaultSize.Y);
			}
			float height = fixedHeight + cardRows * NCard.defaultSize.Y * scale;
			// Without any cards there's no scale to compare; fewest columns wins unless it's too tall.
			bool better = scale > bestScale || (scale == bestScale && bestHeight > maxHeight && height < bestHeight);
			if (better)
			{
				bestColumns = columns;
				bestScale = scale;
				bestHeight = height;
			}
		}
		if (bestColumns == 0 || bestScale < OthersMinCardScale || bestHeight > maxHeight + 1f)
		{
			return;
		}
		float maxColumnWidth = OthersColumnWidth(maxWidth, bestColumns);
		_othersPanel.Visible = true;
		_othersGrid.Columns = bestColumns;
		foreach (Section section in sections)
		{
			// Just wide enough for the cards and the text, so the text only wraps when it has to.
			float width = Mathf.Max(ModUi.MeasureText(section.Text, OthersLabelFontSize).X + 4f, section.Cards.Count > 0 ? CardRowSize(section.Cards.Count, bestScale).X : 0f);
			_othersGrid.AddChild(MakeSection(section, OthersLabelFontSize, Mathf.Min(width, maxColumnWidth), bestScale));
		}
	}

	private static float OthersColumnWidth(float panelWidth, int columns)
	{
		return (panelWidth - 2f * PanelPadding - (columns - 1) * SectionGap) / columns;
	}

	private static Control MakeCardRow(List<CardModel> cards, float scale)
	{
		HBoxContainer row = new HBoxContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Alignment = BoxContainer.AlignmentMode.Center
		};
		row.AddThemeConstantOverride("separation", (int)CardGap);
		foreach (CardModel card in cards)
		{
			row.AddChild(MakeCard(card, scale));
		}
		return row;
	}

	/// <summary>"(Hidden)" in the space the cards would take up.</summary>
	private static Control MakeHiddenRow(Vector2 size)
	{
		CenterContainer row = new CenterContainer
		{
			CustomMinimumSize = size,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		Label label = ModUi.MakeLabel(ModStrings.Get(ModStrings.PreviewHiddenCards).GetFormattedText(), 28);
		label.AddThemeColorOverride("font_color", StsColors.lightGray);
		// A CenterContainer gives its child no width, so wrapping would break it per letter.
		label.AutowrapMode = TextServer.AutowrapMode.Off;
		row.AddChild(label);
		return row;
	}

	/// <summary>A card on offer, dimmed to set it apart from the real card reward screen.</summary>
	private static Control MakeCard(CardModel card, float scale)
	{
		Vector2 size = NCard.defaultSize * scale;
		Control slot = new Control
		{
			CustomMinimumSize = size,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			// Dimmed rather than see-through: a card is several stacked layers, and transparency
			// lets the ones behind the name banner show through.
			Modulate = new Color(0.8f, 0.8f, 0.8f, 1f)
		};
		NCard? node = NCard.Create(card);
		if (node != null)
		{
			slot.AddChildSafely(node);
			ModUi.UpdateCardVisuals(node, PileType.None);
			node.Scale = Vector2.One * scale;
			// NCard draws centred on its origin.
			node.Position = size * 0.5f;
		}
		return slot;
	}

	/// <summary>
	/// The chosen card lifts out of the panel (or pops out of its header if the cards were
	/// hidden), grows beside it with "Chosen by X", then flies into the deck.
	/// </summary>
	public bool AnimateChosen(CardModel offered, CardModel added, string chosenBy)
	{
		Node trailContainer = _globalUi.TopBar.TrailContainer;
		NCard? source = _sections.FindChildren("*", "", true, false).OfType<NCard>()
			.FirstOrDefault((NCard n) => n.Model == offered || n.Model == added);
		Rect2 panelRect = _panel.GetGlobalRect();
		Vector2 startPosition;
		float startScale;
		if (source != null)
		{
			startPosition = source.GlobalPosition;
			startScale = source.Scale.X;
			// Leave a gap behind, as if the card was lifted out.
			((CanvasItem)source.GetParent()).Modulate = Colors.Transparent;
		}
		else
		{
			startPosition = new Vector2(panelRect.GetCenter().X, panelRect.Position.Y + 30f);
			startScale = 0.05f;
		}
		NCard? card = NCard.Create(added);
		if (card == null)
		{
			return false;
		}
		trailContainer.AddChildSafely(card);
		card.GlobalPosition = startPosition;
		card.Scale = Vector2.One * startScale;
		ModUi.UpdateCardVisuals(card, PileType.Deck);
		// Whichever card was picked, it grows to the same spot: where the middle card sits (the card
		// row is centred in the panel), nudged to keep its label on screen and below the top bar.
		Vector2 viewport = _root.GetViewportRect().Size;
		Vector2 half = NCard.defaultSize * HoldScale * 0.5f;
		float labelHalfWidth = NCard.defaultSize.X * 1.4f * 0.5f;
		float rowCenterY = source?.GlobalPosition.Y ?? panelRect.GetCenter().Y;
		Vector2 hold = new Vector2(
			Mathf.Min(panelRect.GetCenter().X, viewport.X - RightMargin - Mathf.Max(half.X, labelHalfWidth)),
			Mathf.Max(rowCenterY, MinTop + 70f + half.Y));
		AddChosenByLabel(card, chosenBy);
		Tween tween = card.CreateTween();
		tween.TweenProperty(card, "scale", Vector2.One * HoldScale, 0.35).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
		tween.Parallel().TweenProperty(card, "global_position", hold, 0.35).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
		tween.TweenInterval(HoldTime);
		tween.TweenCallback(Callable.From(delegate
		{
			if (!GodotObject.IsInstanceValid(card))
			{
				return;
			}
			NCardFlyVfx? fly = NCardFlyVfx.Create(card, PileType.Deck, isAddingToPile: true, added.Owner.Character.TrailPath);
			if (fly != null && GodotObject.IsInstanceValid(trailContainer))
			{
				trailContainer.AddChildSafely(fly);
			}
			else
			{
				card.QueueFreeSafely();
			}
		}));
		Pulse();
		return true;
	}

	private static void AddChosenByLabel(NCard card, string text)
	{
		// The label is a child of the card, so counter the card's scale to keep the text readable.
		Label label = ModUi.MakeLabel(text, 30);
		label.AddThemeColorOverride("font_color", StsColors.gold);
		label.Size = new Vector2(NCard.defaultSize.X * 1.4f, 44f);
		label.Scale = Vector2.One / HoldScale;
		label.Position = new Vector2(-label.Size.X * 0.5f / HoldScale, -NCard.defaultSize.Y * 0.5f - 56f / HoldScale);
		card.AddChildSafely(label);
		Tween tween = label.CreateTween();
		tween.TweenProperty(label, "modulate:a", 1f, 0.2).From(0f).SetDelay(0.2);
		tween.TweenProperty(label, "modulate:a", 0f, 0.2).SetDelay(HoldTime - 0.25f);
	}

	/// <summary>Flashes the panel's border to draw the eye to a change.</summary>
	public bool Pulse()
	{
		_pulseTween?.Kill();
		_pulseTween = _panel.CreateTween();
		_pulseTween.TweenMethod(Callable.From<float>(SetHighlight), 1f, 0f, 1.2).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
		return true;
	}

	private void SetHighlight(float amount)
	{
		_style.BorderColor = new Color(StsColors.gold, 0.6f + 0.4f * amount);
		_style.SetBorderWidthAll(2 + Mathf.RoundToInt(3f * amount));
		_style.BgColor = new Color(0.05f, 0.06f, 0.1f, 0.75f).Lerp(new Color(0.35f, 0.28f, 0.08f, 0.85f), amount * 0.6f);
	}
}

[HarmonyPatch(typeof(NRewardsScreen), nameof(NRewardsScreen._Ready))]
public static class RewardsScreenReadyPatch
{
	public static void Postfix()
	{
		if (HandAndBrainState.IsActive && RunManager.Instance.NetService.Type != NetGameType.Singleplayer)
		{
			PickPreviewPanel.Attach();
		}
	}
}
