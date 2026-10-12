using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Runs;

namespace HandAndBrain.UI;

/// <summary>
/// Arrows beside the multiplayer player list, from each chooser to the player whose card
/// rewards they pick. Each arrow is a curve that starts with a dot beside its chooser's row,
/// bulges out to the right, and comes back with an arrowhead pointing at its recipient's row.
/// Each arrow has its own colour, picked by the chooser's place in the pick order. Longer
/// arrows bulge further than the ones they overlap, so they nest instead of tangling. Arrows
/// that don't involve the local player are dimmer.
/// </summary>
public sealed class PickerArrows
{
	private const string NodeName = "HandAndBrainPickerArrows";

	/// <summary>Gap between the widest row's content and where the arrows attach.</summary>
	private const float RowGap = 14f;

	/// <summary>How far the innermost curve bulges out, and how much further each outer lane goes.</summary>
	private const float BaseBulge = 46f;

	private const float LaneSpacing = 30f;

	/// <summary>Vertical distance between arrow ends that meet the same row.</summary>
	private const float EndSpacing = 20f;

	private const float LineWidth = 4f;

	private const float HeadLength = 16f;

	private const float HeadHalfWidth = 9f;

	private const float StartDotRadius = 5f;

	private const int CurveSegments = 32;

	/// <summary>One colour per arrow; a run has at most four players.</summary>
	private static readonly Color[] Palette = new Color[]
	{
		new Color("EFC851"),
		new Color("2AEBBE"),
		new Color("FF78A0"),
		new Color("9B8CFF")
	};

	private readonly NMultiplayerPlayerStateContainer _container;

	private readonly Control _root;

	private string? _renderedKey;

	private PickerArrows(NMultiplayerPlayerStateContainer container, NMultiplayerPlayerState firstRow)
	{
		_container = container;
		// A child of the first row so it moves and fades with the list. It can't be a child of the
		// container itself: the container assumes every child is a player row.
		_root = new Control
		{
			Name = NodeName,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = 1
		};
		Timer timer = new Timer
		{
			WaitTime = 0.25,
			OneShot = false,
			Autostart = true
		};
		timer.Timeout += Refresh;
		_root.AddChild(timer);
		firstRow.AddChildSafely(_root);
	}

	public static void Attach(NMultiplayerPlayerStateContainer container, bool retry = true)
	{
		NMultiplayerPlayerState? firstRow = container.GetChildren().OfType<NMultiplayerPlayerState>().FirstOrDefault();
		if (firstRow == null)
		{
			// The rows may be added deferred; singleplayer has none at all.
			if (retry)
			{
				Callable.From(delegate
				{
					if (GodotObject.IsInstanceValid(container))
					{
						Attach(container, retry: false);
					}
				}).CallDeferred();
			}
			return;
		}
		if (firstRow.GetNodeOrNull(NodeName) == null)
		{
			new PickerArrows(container, firstRow);
		}
	}

	private void Refresh()
	{
		HandAndBrainSettings? settings = HandAndBrainState.Active;
		List<NMultiplayerPlayerState> rows = GodotObject.IsInstanceValid(_container)
			? _container.GetChildren().OfType<NMultiplayerPlayerState>().ToList()
			: new List<NMultiplayerPlayerState>();
		if (settings == null || !settings.IsInEffect || rows.Count < 2)
		{
			_root.Visible = false;
			_renderedKey = null;
			return;
		}
		_root.Visible = true;
		Vector2 origin = rows[0].GlobalPosition;
		Dictionary<ulong, int> rowIndex = new Dictionary<ulong, int>();
		List<float> centers = new List<float>();
		List<float> rights = new List<float>();
		for (int i = 0; i < rows.Count; i++)
		{
			NMultiplayerPlayerState row = rows[i];
			rowIndex[row.Player.NetId] = i;
			centers.Add(row.GlobalPosition.Y - origin.Y + row.Size.Y * 0.5f);
			rights.Add(ContentRight(row) - origin.X);
		}
		List<(int from, int to, bool bright, bool dead, Color color)> arrows = new List<(int, int, bool, bool, Color)>();
		foreach (NMultiplayerPlayerState row in rows)
		{
			ulong? targetId = settings.TargetOf(row.Player.NetId);
			if (!targetId.HasValue || targetId.Value == row.Player.NetId || !rowIndex.TryGetValue(targetId.Value, out int to))
			{
				continue;
			}
			NMultiplayerPlayerState target = rows[to];
			bool bright = LocalContext.IsMe(row.Player) || LocalContext.IsMe(target.Player);
			bool dead = row.Player.Creature.IsDead || target.Player.Creature.IsDead;
			int orderIndex = Mathf.Max(0, settings.Order.IndexOf(row.Player.NetId));
			arrows.Add((rowIndex[row.Player.NetId], to, bright, dead, Palette[orderIndex % Palette.Length]));
		}
		string key = string.Join("|", centers.Select((float c) => Mathf.RoundToInt(c))) + "/" + string.Join("|", rights.Select((float r) => Mathf.RoundToInt(r))) + "/" + string.Join("|", arrows);
		if (key == _renderedKey)
		{
			return;
		}
		_renderedKey = key;
		Render(centers, rights, arrows);
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

	private void Render(List<float> centers, List<float> rights, List<(int from, int to, bool bright, bool dead, Color color)> arrows)
	{
		foreach (Node child in _root.GetChildren())
		{
			if (child is not Timer)
			{
				_root.RemoveChild(child);
				child.QueueFree();
			}
		}
		// Every arrow attaches on one vertical line beside the widest row, so they line up.
		float baseX = rights.Max() + RowGap;
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
				if (Mathf.Max(lo, other.lo) < Mathf.Min(hi, other.hi))
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
			float alpha = arrow.dead ? 0.3f : (arrow.bright ? 1f : 0.6f);
			drawn.Add((curve, start, tip, new Color(arrow.color, alpha)));
		}
		// Outlines first, then colours, so where two arrows cross both stay readable.
		foreach (var (curve, start, tip, color) in drawn)
		{
			Color outline = new Color(0f, 0f, 0f, 0.75f * color.A);
			AddLine(curve, LineWidth + 4f, outline);
			AddDot(start, StartDotRadius + 2f, outline);
			AddHead(tip - new Vector2(2f, 0f), HeadLength + 4f, HeadHalfWidth + 3f, outline);
		}
		foreach (var (curve, start, tip, color) in drawn)
		{
			AddLine(curve, LineWidth, color);
			AddDot(start, StartDotRadius, color);
			AddHead(tip, HeadLength, HeadHalfWidth, color);
		}
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
		Line2D line = new Line2D
		{
			Points = points,
			Width = width,
			DefaultColor = color,
			JointMode = Line2D.LineJointMode.Round,
			BeginCapMode = Line2D.LineCapMode.Round,
			EndCapMode = Line2D.LineCapMode.Round,
			Antialiased = true
		};
		_root.AddChild(line);
	}

	/// <summary>A triangle pointing left with its point at <paramref name="tip"/>.</summary>
	private void AddHead(Vector2 tip, float length, float halfWidth, Color color)
	{
		Polygon2D head = new Polygon2D
		{
			Polygon = new Vector2[]
			{
				tip,
				tip + new Vector2(length, -halfWidth),
				tip + new Vector2(length, halfWidth)
			},
			Color = color,
			Antialiased = true
		};
		_root.AddChild(head);
	}
}

[HarmonyPatch(typeof(NMultiplayerPlayerStateContainer), nameof(NMultiplayerPlayerStateContainer.Initialize))]
public static class PlayerStateContainerInitializePatch
{
	public static void Postfix(NMultiplayerPlayerStateContainer __instance)
	{
		PickerArrows.Attach(__instance);
	}
}
