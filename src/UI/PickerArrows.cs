using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Godot;
using HandAndBrain.Patches;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;

namespace HandAndBrain.UI;

/// <summary>
/// While a row of the multiplayer player list is hovered (or focused with a controller), shows who
/// that player picks card rewards for and who picks theirs: a gold arrow from the player to the
/// player they pick for, a cream arrow from their chooser to them, and the game's hover tip
/// spelling it out. Nothing is drawn otherwise.
/// </summary>
public sealed class PickerArrows
{
	private const string NodeName = "HandAndBrainPickerArrows";

	private const string HoverOwnerName = "HandAndBrainHoverTipOwner";

	/// <summary>Gap between the widest row's content and where the arrows attach.</summary>
	private const float RowGap = 10f;

	/// <summary>How far the innermost curve bulges out, and how much further each outer lane goes.</summary>
	private const float BaseBulge = 34f;

	private const float LaneSpacing = 22f;

	/// <summary>Vertical distance between arrow ends that meet the same row.</summary>
	private const float EndSpacing = 14f;

	private const float LineWidth = 3f;

	private const float OutlineWidth = 2f;

	private const float HeadLength = 12f;

	private const float HeadHalfWidth = 7f;

	private const float StartDotRadius = 4f;

	private const float TipGap = 16f;

	private const float FadeTime = 0.15f;

	private const int CurveSegments = 32;

	private static readonly Color Outline = new Color(0f, 0f, 0f, 0.6f);

	private static readonly ConditionalWeakTable<NMultiplayerPlayerStateContainer, PickerArrows> _byContainer = new ConditionalWeakTable<NMultiplayerPlayerStateContainer, PickerArrows>();

	private readonly NMultiplayerPlayerStateContainer _container;

	private readonly Control _root;

	private NMultiplayerPlayerState? _shownFor;

	private Tween? _fade;

	private PickerArrows(NMultiplayerPlayerStateContainer container, NMultiplayerPlayerState firstRow)
	{
		_container = container;
		// A child of the first row so it moves and fades with the list. It can't be a child of the
		// container itself: the container assumes every child is a player row.
		_root = new Control
		{
			Name = NodeName,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = 1,
			Visible = false,
			Modulate = Colors.Transparent
		};
		firstRow.AddChildSafely(_root);
	}

	private static PickerArrows? For(NMultiplayerPlayerState row)
	{
		if (row.GetParent() is not NMultiplayerPlayerStateContainer container)
		{
			return null;
		}
		NMultiplayerPlayerState? firstRow = container.GetChildren().OfType<NMultiplayerPlayerState>().FirstOrDefault();
		if (firstRow == null)
		{
			return null;
		}
		if (_byContainer.TryGetValue(container, out PickerArrows? arrows) && GodotObject.IsInstanceValid(arrows._root) && arrows._root.GetParent() == firstRow)
		{
			return arrows;
		}
		arrows = new PickerArrows(container, firstRow);
		_byContainer.AddOrUpdate(container, arrows);
		return arrows;
	}

	public static void OnHighlightChanged(NMultiplayerPlayerState row, bool highlighted)
	{
		HandAndBrainSettings? settings = HandAndBrainState.Active;
		if (highlighted && (settings == null || !settings.IsInEffect))
		{
			return;
		}
		PickerArrows? arrows = For(row);
		if (arrows == null)
		{
			return;
		}
		if (highlighted)
		{
			arrows.Show(row, settings!);
		}
		else if (arrows._shownFor == row)
		{
			arrows.Hide();
		}
	}

	private void Show(NMultiplayerPlayerState hovered, HandAndBrainSettings settings)
	{
		List<NMultiplayerPlayerState> rows = _container.GetChildren().OfType<NMultiplayerPlayerState>().ToList();
		int hoveredIndex = rows.IndexOf(hovered);
		if (rows.Count < 2 || hoveredIndex < 0)
		{
			return;
		}
		ulong hoveredId = hovered.Player.NetId;
		int Find(ulong? id) => id.HasValue ? rows.FindIndex((NMultiplayerPlayerState r) => r.Player.NetId == id.Value) : -1;
		int target = Find(settings.TargetOf(hoveredId));
		int chooser = Find(settings.ChooserOf(hoveredId));
		List<(int from, int to, Color color)> arrows = new List<(int, int, Color)>();
		if (target >= 0 && target != hoveredIndex)
		{
			arrows.Add((hoveredIndex, target, StsColors.gold));
		}
		if (chooser >= 0 && chooser != hoveredIndex)
		{
			arrows.Add((chooser, hoveredIndex, StsColors.cream));
		}
		if (arrows.Count == 0)
		{
			return;
		}
		Vector2 origin = rows[0].GlobalPosition;
		List<float> centers = rows.Select((NMultiplayerPlayerState r) => r.GlobalPosition.Y - origin.Y + r.Size.Y * 0.5f).ToList();
		float baseX = rows.Max(ContentRight) - origin.X + RowGap;
		float outerX = Render(centers, baseX, arrows);

		_shownFor = hovered;
		_fade?.Kill();
		_root.Visible = true;
		_fade = _root.CreateTween();
		_fade.TweenProperty(_root, "modulate:a", 1f, FadeTime).SetEase(Tween.EaseType.Out);

		ShowHoverTip(hovered, rows, target, chooser, origin + new Vector2(outerX + TipGap, hovered.GlobalPosition.Y - origin.Y));
	}

	private void Hide()
	{
		if (_shownFor != null && GodotObject.IsInstanceValid(_shownFor))
		{
			Control? owner = _shownFor.GetNodeOrNull<Control>(HoverOwnerName);
			if (owner != null)
			{
				NHoverTipSet.Remove(owner);
			}
		}
		_shownFor = null;
		_fade?.Kill();
		_fade = _root.CreateTween();
		_fade.TweenProperty(_root, "modulate:a", 0f, FadeTime);
		_fade.TweenCallback(Callable.From(delegate
		{
			_root.Visible = false;
		}));
	}

	/// <summary>The game's own hover tip, beside the arrows, saying who picks for whom.</summary>
	private static void ShowHoverTip(NMultiplayerPlayerState hovered, List<NMultiplayerPlayerState> rows, int target, int chooser, Vector2 position)
	{
		// The row itself owns the game's network-problem tip, and a control can own only one.
		Control? owner = hovered.GetNodeOrNull<Control>(HoverOwnerName);
		if (owner == null)
		{
			owner = new Control
			{
				Name = HoverOwnerName,
				MouseFilter = Control.MouseFilterEnum.Ignore
			};
			hovered.AddChildSafely(owner);
		}
		NHoverTipSet.Remove(owner);
		bool isMe = LocalContext.IsMe(hovered.Player);
		List<string> lines = new List<string>();
		Player? targetPlayer = target >= 0 ? rows[target].Player : null;
		if (targetPlayer != null && targetPlayer != hovered.Player)
		{
			lines.Add(Line(isMe ? ModStrings.HoverYouPickFor : ModStrings.HoverPicksFor, targetPlayer));
		}
		if (chooser >= 0 && rows[chooser].Player != hovered.Player)
		{
			lines.Add(Line(isMe ? ModStrings.HoverPicksForYou : ModStrings.HoverPickedBy, rows[chooser].Player));
		}
		// The hotkey opens the deck of the player you pick for, so mention it on the rows it concerns.
		Player? myRecipient = RecipientDeckHotkey.LocalRecipient();
		Key hotkey = NInputManager.Instance?.GetCurrentHotkey(RecipientDeckHotkey.Action) ?? Key.None;
		if (myRecipient != null && hotkey != Key.None && (isMe || hovered.Player == myRecipient))
		{
			LocString text = ModStrings.Get(ModStrings.HoverHotkey);
			text.Add("Hotkey", hotkey.ToString());
			text.Add("Player", Redirects.NameOf(myRecipient));
			lines.Add(text.GetFormattedText());
		}
		if (lines.Count == 0)
		{
			return;
		}
		HoverTip tip = new HoverTip(ModStrings.Get(ModStrings.HoverTitle), string.Join("\n", lines));
		NHoverTipSet.CreateAndShow(owner, tip)?.SetGlobalPosition(position);
	}

	private static string Line(string key, Player player)
	{
		return ModStrings.Format(key, Redirects.NameOf(player));
	}

	/// <summary>The right edge of what a row actually draws: its name/info line and its health bar.</summary>
	private static float ContentRight(NMultiplayerPlayerState row)
	{
		float right = row.GlobalPosition.X + 248f;
		Control? info = row.GetNodeOrNull<Control>("TopInfoContainer");
		if (info != null)
		{
			right = info.GetGlobalRect().End.X;
		}
		NHealthBar? healthBar = row.GetNodeOrNull<NHealthBar>("%HealthBar");
		if (healthBar?.HpBarContainer != null)
		{
			right = Mathf.Max(right, healthBar.HpBarContainer.GetGlobalRect().End.X);
		}
		return right;
	}

	/// <summary>Draws the arrows and returns the x of their outermost point.</summary>
	private float Render(List<float> centers, float baseX, List<(int from, int to, Color color)> arrows)
	{
		foreach (Node child in _root.GetChildren())
		{
			_root.RemoveChild(child);
			child.QueueFree();
		}
		// Shortest arrows first. An arrow goes one lane further out than any placed arrow it
		// overlaps (sharing a single row is fine), so longer arrows wrap around shorter ones.
		var ordered = arrows.OrderBy((a) => Mathf.Abs(a.to - a.from)).ToList();
		List<(int lo, int hi, int lane)> placed = new List<(int, int, int)>();
		foreach (var arrow in ordered)
		{
			int lo = Mathf.Min(arrow.from, arrow.to);
			int hi = Mathf.Max(arrow.from, arrow.to);
			int lane = 0;
			foreach (var other in placed)
			{
				if (Mathf.Max(lo, other.lo) < Mathf.Min(hi, other.hi) || (lo == other.lo && hi == other.hi))
				{
					lane = Mathf.Max(lane, other.lane + 1);
				}
			}
			placed.Add((lo, hi, lane));
		}
		// Where each arrow meets a row. Arrows running up from a row attach to its upper half and
		// arrows running down attach to its lower half; within each half, outer lanes attach
		// further from the middle, so nested curves never cross.
		Dictionary<(int arrow, int row), float> attachY = new Dictionary<(int, int), float>();
		for (int row = 0; row < centers.Count; row++)
		{
			List<(int arrow, bool goesUp, int lane)> ends = new List<(int, bool, int)>();
			for (int i = 0; i < placed.Count; i++)
			{
				if (placed[i].lo == row || placed[i].hi == row)
				{
					ends.Add((i, placed[i].hi == row, placed[i].lane));
				}
			}
			List<(int arrow, bool goesUp, int lane)> sorted = ends.Where((e) => e.goesUp).OrderByDescending((e) => e.lane)
				.Concat(ends.Where((e) => !e.goesUp).OrderBy((e) => e.lane)).ToList();
			for (int i = 0; i < sorted.Count; i++)
			{
				attachY[(sorted[i].arrow, row)] = centers[row] + (i - (sorted.Count - 1) * 0.5f) * EndSpacing;
			}
		}
		float outerX = baseX;
		List<(Vector2[] curve, Vector2 start, Vector2 tip, Color color)> drawn = new List<(Vector2[], Vector2, Vector2, Color)>();
		for (int i = 0; i < ordered.Count; i++)
		{
			var arrow = ordered[i];
			float bulge = BaseBulge + placed[i].lane * LaneSpacing;
			Vector2 start = new Vector2(baseX, attachY[(i, arrow.from)]);
			Vector2 tip = new Vector2(baseX, attachY[(i, arrow.to)]);
			// The line stops where the arrowhead begins; the head finishes the curve.
			Vector2 end = tip + new Vector2(HeadLength * 0.8f, 0f);
			// Both control points sit straight out to the right, so the curve leaves and arrives
			// horizontally and the arrowhead points straight at the row.
			Vector2[] curve = Bezier(start, start + new Vector2(bulge * 1.35f, 0f), end + new Vector2(bulge * 1.35f, 0f), end);
			outerX = Mathf.Max(outerX, curve.Max((Vector2 p) => p.X));
			drawn.Add((curve, start, tip, arrow.color));
		}
		// Outlines first, then colours, so where two arrows cross both stay readable.
		foreach (var (curve, start, tip, _) in drawn)
		{
			AddLine(curve, LineWidth + OutlineWidth * 2f, Outline);
			AddDot(start, StartDotRadius + OutlineWidth, Outline);
			AddHead(tip - new Vector2(OutlineWidth, 0f), HeadLength + OutlineWidth * 2f, HeadHalfWidth + OutlineWidth * 1.5f, Outline);
		}
		foreach (var (curve, start, tip, color) in drawn)
		{
			AddLine(curve, LineWidth, color);
			AddDot(start, StartDotRadius, color);
			AddHead(tip, HeadLength, HeadHalfWidth, color);
		}
		return outerX + LineWidth;
	}

	private static Vector2[] Bezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
	{
		Vector2[] points = new Vector2[CurveSegments + 1];
		for (int i = 0; i <= CurveSegments; i++)
		{
			float t = (float)i / CurveSegments;
			float u = 1f - t;
			points[i] = u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
		}
		return points;
	}

	private void AddDot(Vector2 center, float radius, Color color)
	{
		Vector2[] points = new Vector2[16];
		for (int i = 0; i < points.Length; i++)
		{
			float angle = Mathf.Tau * i / points.Length;
			points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
		}
		_root.AddChild(new Polygon2D
		{
			Polygon = points,
			Color = color,
			Antialiased = true
		});
	}

	private void AddLine(Vector2[] points, float width, Color color)
	{
		_root.AddChild(new Line2D
		{
			Points = points,
			Width = width,
			DefaultColor = color,
			JointMode = Line2D.LineJointMode.Round,
			BeginCapMode = Line2D.LineCapMode.Round,
			EndCapMode = Line2D.LineCapMode.Round,
			Antialiased = true
		});
	}

	/// <summary>A triangle pointing left with its point at <paramref name="tip"/>.</summary>
	private void AddHead(Vector2 tip, float length, float halfWidth, Color color)
	{
		_root.AddChild(new Polygon2D
		{
			Polygon = new Vector2[]
			{
				tip,
				tip + new Vector2(length, -halfWidth),
				tip + new Vector2(length, halfWidth)
			},
			Color = color,
			Antialiased = true
		});
	}
}

/// <summary>A player row lights up when hovered or focused; show the arrows for it at the same time.</summary>
[HarmonyPatch(typeof(NMultiplayerPlayerState), "UpdateHighlightedState")]
public static class PlayerStateHighlightPatch
{
	private static readonly AccessTools.FieldRef<NMultiplayerPlayerState, bool> _isHighlighted = AccessTools.FieldRefAccess<NMultiplayerPlayerState, bool>("_isHighlighted");

	public static void Prefix(NMultiplayerPlayerState __instance, out bool __state)
	{
		__state = _isHighlighted(__instance);
	}

	public static void Postfix(NMultiplayerPlayerState __instance, bool __state)
	{
		bool highlighted = _isHighlighted(__instance);
		if (highlighted != __state)
		{
			PickerArrows.OnHighlightChanged(__instance, highlighted);
		}
	}
}
