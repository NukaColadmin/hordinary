namespace Hordinary.AI.Instructions;

/// <summary>
/// Section <c>instructions</c> de <c>config/game_config.json</c> (livrable 15).
///
/// <para>
/// Livree a <c>enabled: false</c> : le jeu est integralement jouable sans service de langage, par les
/// instructions pre-ecrites de <c>config/instruction_presets.json</c>. Ce qui est desactive ici, c'est
/// la <b>traduction</b> d'une phrase libre en contrainte, pas les contraintes elles-memes — les quatre
/// portillons et le compilateur tournent exactement pareil dans les deux cas.
/// </para>
/// </summary>
public sealed class InstructionSettings
{
    /// <summary>Appel a un modele de langage pour traduire une phrase libre. Les contraintes fonctionnent sans.</summary>
    public bool Enabled { get; init; }

    /// <summary>Longueur maximale d'une phrase de joueur, coupee au-dela.</summary>
    public int MaxChars { get; init; } = 200;

    /// <summary>
    /// Nombre de contraintes actives simultanement. Chacune reecrit le domaine ; au-dela d'une
    /// poignee, plus personne — ni le joueur, ni la recherche du coupable minimal — ne sait ce que le
    /// domaine autorise encore.
    /// </summary>
    public int MaxActiveConstraints { get; init; } = 5;

    /// <summary>Bornes du multiplicateur de <c>penalize</c>. En dehors, le portillon 1 refuse.</summary>
    public float MinPenalty { get; init; } = 0.1f;

    public float MaxPenalty { get; init; } = 10f;

    /// <summary>Les instructions pre-ecrites restent proposees quand le service est coupe.</summary>
    public bool PresetsWhenDisabled { get; init; } = true;
}
