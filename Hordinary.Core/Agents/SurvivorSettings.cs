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
    /// Sante rendue par une boite de medicaments, sur l'echelle de <see cref="MaxHealth"/>. Un soin
    /// ne remet pas d'aplomb : c'est ce qui rend l'efficacite de Claire (competence <c>HealAlly</c>)
    /// reellement utile plutot que decorative.
    /// </summary>
    public float HealAmount { get; init; } = 45f;

    /// <summary>Panique retiree par un survivant qui en rassure un autre (action <c>CalmAlly</c>).</summary>
    public float CalmAmount { get; init; } = 0.4f;

    // --- Motivation ---------------------------------------------------------------------------

    /// <summary>
    /// Motivation maximale, la jauge <c>SANITY</c> affichee sur la fiche de personnage. Elle est
    /// volontairement sur une autre echelle que la sante : la sante dit ce que le corps encaisse
    /// pendant une mission, la motivation ce que la tete encaisse sur toute une campagne.
    /// </summary>
    public float MaxSanity { get; init; } = 200f;

    /// <summary>
    /// Motivation perdue par seconde a panique maximale. La peur est ce qui use la motivation :
    /// un survivant qui traverse la carte sans jamais croiser personne rentre entier.
    /// </summary>
    public float SanityDrainPerSecond { get; init; } = 6f;

    /// <summary>
    /// Motivation regagnee par seconde au calme complet. Plus lente que la perte : souffler ne
    /// rattrape pas une poursuite, ce qui donne a la jauge une pente descendante sur la duree.
    /// </summary>
    public float SanityRecoveryPerSecond { get; init; } = 3f;

    /// <summary>
    /// Motivation perdue par une blessure, en fraction de <see cref="MaxSanity"/> et par fraction de
    /// sante perdue : perdre la moitie de sa sante avec un coefficient de 0,5 coute un quart de la
    /// jauge. Le pendant moral de <see cref="PanicPerWound"/>.
    /// </summary>
    public float SanityPerWound { get; init; } = 0.5f;

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

    /// <summary>Coefficients de personnalite du survivant (section 10.2).</summary>
    public Personality Personality { get; init; } = new();

    public static SurvivorSettings Default { get; } = new();
}
