namespace Hordinary.Agents;

/// <summary>
/// Etat physique et moral d'un survivant (specification section 19), charge depuis
/// <c>config/game_config.json</c>, section <c>survivor</c>.
/// </summary>
public sealed class SurvivorSettings
{
    /// <summary>Sante maximale. La specification exprime la sante sur 0-100.</summary>
    public float MaxHealth { get; init; } = 100f;

    /// <summary>Vitesse de marche, en cellules par seconde.</summary>
    public float WalkSpeed { get; init; } = 4f;

    /// <summary>Vitesse de course : plus rapide, mais trois fois plus bruyante (section 18.1).</summary>
    public float RunSpeed { get; init; } = 7f;

    /// <summary>Vitesse a laquelle la panique rejoint le danger percu (section 19.3).</summary>
    public float PanicRisePerSecond { get; init; } = 0.5f;

    /// <summary>La panique retombe plus lentement qu'elle ne monte : loin du danger, on souffle.</summary>
    public float PanicDecayPerSecond { get; init; } = 0.15f;

    /// <summary>
    /// Panique ajoutee par une blessure, exprimee en multiple de la fraction de sante perdue :
    /// perdre 10 % de sa sante avec un coefficient de 2,5 ajoute 0,25 de panique.
    /// </summary>
    public float PanicPerWound { get; init; } = 2.5f;

    /// <summary>
    /// Portee, en cellules, d'une fouille de piece (action <c>SearchRoom</c> du livrable 5). Fouiller
    /// n'est pas voir : on trouve ce qui est a portee de bras et d'etagere, pas ce qui est au fond du
    /// batiment.
    /// </summary>
    public float SearchRadius { get; init; } = 3f;

    /// <summary>
    /// Portee, en cellules, d'un ramassage — la longueur d'un bras, pas celle d'une piece.
    ///
    /// Fouiller et prendre ne se paient pas au meme prix, et c'est ce qui separe les deux verbes.
    /// <c>SearchRoom</c> se fait a l'echelle du local : on entre, on fait le tour des etageres, on
    /// <b>repere</b> ce qu'il y a. Prendre demande d'y aller. Sans cette distinction, un survivant
    /// pousse la porte de la pharmacie et la caisse apparait dans son sac sans qu'il ait fait un pas
    /// de plus — le joueur voit un objectif se remplir tout seul, et le trajet que le plan avait
    /// prevu ne sert plus a rien.
    ///
    /// Une case suffit, et pas moins : c'est <see cref="Config.PlannerSettings.ArrivalRadius"/>, le
    /// rayon auquel l'executeur declare un deplacement arrive. Exiger la case exacte ferait echouer
    /// tout ramassage qui suit un deplacement.
    /// </summary>
    public float PickUpRadius { get; init; } = 1f;

    /// <summary>
    /// Sante rendue par une boite de medicaments, sur l'echelle de <see cref="MaxHealth"/>. Un soin
    /// ne remet pas d'aplomb : c'est ce qui rend l'efficacite de Claire (competence de soin)
    /// reellement utile plutot que decorative.
    /// </summary>
    public float HealAmount { get; init; } = 45f;

    // --- Combat (livrable 8) -----------------------------------------------------------------

    /// <summary>
    /// Distance a laquelle un survivant peut frapper ou repousser un zombie. Une case : le combat du
    /// MVP est un corps a corps, pas un tir.
    /// </summary>
    public int MeleeRange { get; init; } = 1;

    /// <summary>
    /// Recul, en cellules, inflige par une poussee. Repousser ne blesse pas : cela achete les
    /// quelques secondes qu'il faut pour partir, ce qui est l'usage que la specification en fait.
    /// </summary>
    public int PushBackDistance { get; init; } = 3;

    // --- Coup d'oeil en arriere -------------------------------------------------------------

    /// <summary>
    /// Pression de poursuite a partir de laquelle un survivant se retourne en courant. En dessous,
    /// rien ne le suit d'assez pres pour meriter d'y perdre son champ de vision.
    /// </summary>
    public float RearGlanceThreshold { get; init; } = 0.15f;

    /// <summary>Delai entre deux coups d'oeil, en secondes.</summary>
    public float RearGlanceInterval { get; init; } = 1.5f;

    /// <summary>Duree d'un coup d'oeil. Courte : on regarde derriere soi, on ne s'arrete pas.</summary>
    public float RearGlanceDuration { get; init; } = 0.3f;

    public static SurvivorSettings Default { get; } = new();
}
