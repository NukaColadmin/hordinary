using System.Numerics;

namespace Hordinary.Agents;

/// <summary>
/// Le coup d'oeil par-dessus l'epaule.
///
/// Un survivant qui fuit regarde devant lui, donc ne voit plus ce qui le poursuit. La perception
/// conclut alors que la case ou il croyait le zombie est hors de vue — ce qui est vrai — et la
/// derniere position connue vieillit sans jamais etre rafraichie. Au bout de quelques secondes il
/// court sans savoir si la menace est encore derriere.
///
/// D'ou ce geste : pendant qu'il fuit, il tourne brievement son champ de vision vers l'arriere, le
/// temps d'une passe de perception. Le prix est reel — il ne voit plus devant pendant ce temps-la —
/// et c'est ce qui en fait un compromis plutot qu'un capteur gratuit.
///
/// La classe ne decide pas s'il faut fuir : elle recoit la pression de poursuite deja calculee par
/// la Utility AI et se contente de cadencer le geste.
/// </summary>
public sealed class RearGlance(SurvivorSettings? settings = null)
{
    private readonly SurvivorSettings _settings = settings ?? SurvivorSettings.Default;

    private double _startedAt = double.NegativeInfinity;
    private double _endsAt = double.NegativeInfinity;

    /// <summary>Vrai tant que le survivant regarde derriere lui.</summary>
    public bool IsGlancing { get; private set; }

    /// <summary>Nombre de coups d'oeil declenches. Alimente le panneau debug.</summary>
    public int Count { get; private set; }

    /// <summary>Emis au declenchement d'un coup d'oeil, avec la direction regardee.</summary>
    public event Action<Vector2>? Glanced;

    /// <summary>
    /// Direction du regard pour cette passe de perception : le cap du deplacement, ou son oppose
    /// pendant un coup d'oeil.
    /// </summary>
    /// <param name="travel">Cap du deplacement, tel que le suiveur de chemin le donne.</param>
    /// <param name="pursuit">Pression de poursuite ressentie, entre 0 et 1.</param>
    public Vector2 Gaze(Vector2 travel, float pursuit, double now)
    {
        var forward = travel.LengthSquared() > 0f ? Vector2.Normalize(travel) : new Vector2(0f, 1f);

        if (IsGlancing && now < _endsAt) return -forward;

        IsGlancing = false;

        // On ne se retourne que si quelque chose suit, et pas plus souvent que l'intervalle.
        if (pursuit < _settings.RearGlanceThreshold) return forward;
        if (now - _startedAt < _settings.RearGlanceInterval) return forward;

        _startedAt = now;
        _endsAt = now + Math.Max(0f, _settings.RearGlanceDuration);
        IsGlancing = true;
        Count++;

        var behind = -forward;
        Glanced?.Invoke(behind);
        return behind;
    }

    /// <summary>Oublie le rythme en cours : nouvelle partie, ou survivant hors de combat.</summary>
    public void Reset()
    {
        IsGlancing = false;
        _startedAt = double.NegativeInfinity;
        _endsAt = double.NegativeInfinity;
    }
}
