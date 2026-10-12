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

	public static Label MakeLabel(string text, int fontSize)
	{
		Label label = new Label
		{
			Text = text,
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_font ??= ResourceLoader.Load<Font>("res://fonts/kreon_bold.ttf", null, ResourceLoader.CacheMode.Reuse);
		if (_font != null)
		{
			label.AddThemeFontOverride("font", _font);
		}
		label.AddThemeFontSizeOverride("font_size", fontSize);
		label.AddThemeColorOverride("font_color", StsColors.cream);
		label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.8f));
		label.AddThemeConstantOverride("outline_size", 10);
		return label;
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

	private const float HoldScale = 0.7f;

	private const float HoldTime = 1.6f;

	private static PickPreviewPanel? _current;

	private readonly NGlobalUi _globalUi;

	private readonly Control _root;

	private readonly PanelContainer _panel;

	private readonly StyleBoxFlat _style;

	private readonly VBoxContainer _sections;

	private string? _renderedKey;

	private Tween? _pulseTween;

	/// <summary>The panel, if it exists and is on screen.</summary>
	public static PickPreviewPanel? Current
	{
		get
		{
			if (_current == null || !GodotObject.IsInstanceValid(_current._root) || !_current._root.IsVisibleInTree())
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
		_panel = new PanelContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		// Pinned to the top right corner; grows left and down to fit its content.
		_panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		_panel.GrowHorizontal = Control.GrowDirection.Begin;
		_panel.GrowVertical = Control.GrowDirection.End;
		_style = new StyleBoxFlat
		{
			BgColor = new Color(0.05f, 0.06f, 0.1f, 0.75f),
			BorderColor = new Color(StsColors.gold, 0.6f)
		};
		_style.SetBorderWidthAll(2);
		_style.SetCornerRadiusAll(10);
		_style.SetContentMarginAll(12f);
		_panel.AddThemeStyleboxOverride("panel", _style);
		_root.AddChild(_panel);

		_sections = new VBoxContainer
		{
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_sections.AddThemeConstantOverride("separation", 16);
		_panel.AddChild(_sections);

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

	/// <summary>One redirected reward as the panel shows it.</summary>
	private readonly record struct Section(string Text, List<CardModel> Cards, bool Hidden)
	{
		public string Key => Text + ":" + Hidden + ":" + string.Join(",", Cards.Select((CardModel c) => c.GetHashCode()));
	}

	private void Refresh()
	{
		// Once a card has been picked the panel has nothing left to say (the pick animation
		// already showed it), so those rewards drop out.
		List<Section> sections = HandAndBrainState.IsActive && IsOnRewardScreens()
			? Redirects.Current
				.Where((RedirectedReward r) => LocalContext.IsMe(r.Recipient) && !LocalContext.IsMe(r.Chooser) && r.Recipient.RunState.CurrentRoom == r.Room)
				.Select(Describe)
				.OfType<Section>()
				.ToList()
			: new List<Section>();
		_root.Visible = sections.Count > 0;
		if (sections.Count == 0)
		{
			_renderedKey = null;
			return;
		}
		string key = Mathf.RoundToInt(AvailableWidth) + "|" + Mathf.RoundToInt(Top) + "|" + string.Join("|", sections.Select((Section s) => s.Key));
		if (key == _renderedKey)
		{
			return;
		}
		_renderedKey = key;
		Render(sections);
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
		if (redirected.Skipped)
		{
			return new Section(ModStrings.Format(ModStrings.PreviewSkipped, name), new List<CardModel>(), false);
		}
		return new Section(ModStrings.Format(ModStrings.PreviewWaiting, name), new List<CardModel>(), false);
	}

	private void Render(List<Section> sections)
	{
		foreach (Node child in _sections.GetChildren())
		{
			_sections.RemoveChild(child);
			child.QueueFree();
		}
		// Cards sit in one row, as large as fits beside the banner (up to MaxCardScale).
		float rowWidth = AvailableWidth - 2f * PanelPadding;
		float width = MinWidth;
		foreach (Section entry in sections)
		{
			VBoxContainer section = new VBoxContainer
			{
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			section.AddThemeConstantOverride("separation", 8);
			Label label = ModUi.MakeLabel(entry.Text, 22);
			section.AddChild(label);
			int count = entry.Cards.Count;
			if (count > 0)
			{
				float scale = Mathf.Clamp((rowWidth - (count - 1) * CardGap) / count / NCard.defaultSize.X, 0.15f, MaxCardScale);
				Vector2 rowSize = new Vector2(count * NCard.defaultSize.X * scale + (count - 1) * CardGap, NCard.defaultSize.Y * scale);
				section.AddChild(entry.Hidden ? MakeHiddenRow(rowSize) : MakeCardRow(entry.Cards, scale));
				width = Mathf.Max(width, rowSize.X);
			}
			_sections.AddChild(section);
		}
		foreach (Label label in _sections.GetChildren().SelectMany((Node s) => s.GetChildren().OfType<Label>()))
		{
			label.CustomMinimumSize = new Vector2(Mathf.Min(width, rowWidth), 0f);
		}
		// Shrink back to the new content, still pinned to the corner.
		float top = Top;
		_panel.OffsetLeft = -RightMargin;
		_panel.OffsetRight = -RightMargin;
		_panel.OffsetTop = top;
		_panel.OffsetBottom = top;
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
			// The card's text is only filled in once it is in the scene tree; before that it
			// shows its placeholder text.
			node.Ready += delegate
			{
				node.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
			};
			slot.AddChildSafely(node);
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
		card.UpdateVisuals(PileType.Deck, CardPreviewMode.Normal);
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
