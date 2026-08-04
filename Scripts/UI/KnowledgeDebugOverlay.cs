using Godot;
using Hordinary.Agents;
using Hordinary.World;

namespace Hordinary.UI;

/// <summary>
/// Affichage debug de la carte mentale d'un agent (livrable 2, etape « afficher la carte mentale »).
///
/// Les tuiles du decor sont dessinees par Godot a partir de la carte reelle : cet overlay pose
/// par-dessus le brouillard de ce que l'agent ignore. Trois etats sont distingues — jamais observe,
/// memorise mais hors de vue, et actuellement visible.
/// </summary>
public partial class KnowledgeDebugOverlay : Node2D
{
    private static readonly Color NeverObserved = new(0.02f, 0.02f, 0.05f, 0.88f);
    private static readonly Color Remembered = new(0.03f, 0.05f, 0.12f, 0.45f);
    private static readonly Color VisibleTint = new(1f, 0.95f, 0.70f, 0.07f);
    private static readonly Color ZombieColor = new(1f, 0.25f, 0.30f);
    private static readonly Color AllyColor = new(0.45f, 0.85f, 1f);
    private static readonly Color ItemColor = new(0.55f, 1f, 0.60f);
    private static readonly Color LocationColor = new(1f, 0.85f, 0.45f);

    private int _tileSize = 16;
    private AgentKnowledge? _knowledge;
    private HashSet<GridPosition> _visible = [];
    private double _now;

    public void Configure(AgentKnowledge knowledge, int tileSize)
    {
        _knowledge = knowledge;
        _tileSize = tileSize;
        QueueRedraw();
    }

    public void SetObservation(IReadOnlyList<GridPosition> visibleCells, double now)
    {
        _visible = [.. visibleCells];
        _now = now;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_knowledge is null) return;

        DrawFog();
        DrawBeliefs();
    }

    private void DrawFog()
    {
        foreach (var cell in _knowledge!.MentalMap.AllPositions())
        {
            if (_visible.Contains(cell))
            {
                DrawRect(CellRect(cell), VisibleTint);
                continue;
            }

            DrawRect(CellRect(cell), _knowledge.HasObserved(cell) ? Remembered : NeverObserved);
        }
    }

    private void DrawBeliefs()
    {
        var font = ThemeDB.FallbackFont;

        foreach (var belief in _knowledge!.KnownEntities.Values)
        {
            float confidence = _knowledge.ConfidenceIn(belief, _now);
            if (confidence <= 0f) continue;

            // L'opacite traduit la confiance : une position ancienne s'efface au lieu de mentir.
            var color = belief.Kind == EntityKind.Zombie ? ZombieColor : AllyColor;
            DrawCircle(CellCenter(belief.Value), _tileSize * 0.30f, color with { A = confidence });
            DrawRect(CellRect(belief.Value), color with { A = confidence * 0.8f }, filled: false, width: 1f);
        }

        foreach (var belief in _knowledge.KnownItems.Values)
        {
            var rect = CellRect(belief.Value).Grow(-_tileSize * 0.30f);
            DrawRect(rect, ItemColor with { A = 0.85f });
        }

        foreach (var belief in _knowledge.KnownLocations.Values)
        {
            DrawString(font, CellCenter(belief.Value) + new Vector2(-_tileSize, -_tileSize * 0.6f),
                belief.Name, HorizontalAlignment.Center, _tileSize * 2f, 7, LocationColor);
        }
    }

    private Rect2 CellRect(GridPosition cell)
        => new(cell.X * _tileSize, cell.Y * _tileSize, _tileSize, _tileSize);

    private Vector2 CellCenter(GridPosition cell)
        => new(cell.X * _tileSize + _tileSize * 0.5f, cell.Y * _tileSize + _tileSize * 0.5f);
}
