using System.Text.Json;
using Hordinary.Config;

namespace Hordinary.Agents;

/// <summary>
/// Un survivant tel que <c>config/survivors.json</c> le declare : un identifiant, un nom, une image.
///
/// Il n'y a rien d'autre, et c'est le choix qui structure le jeu. Pas de classe, pas de role, pas de
/// caracteristiques, pas d'aptitudes, pas de competences par action : Alice, Bob et Claire partent
/// avec la meme sante, la meme vue, la meme discretion et le meme repertoire d'actions au meme prix.
///
/// Ce qui les distingue, c'est ce qu'ils <b>savent</b> — chacun sa carte mentale, chacun ce qu'il a
/// vu — et ce que le joueur leur dit. Une fiche de personnage aurait mis une seconde explication a
/// cote de celle-la : « Bob est passe par la porte parce qu'il est bricoleur » se serait confondu
/// avec « Bob est passe par la porte parce que le joueur a interdit le bruit ». Un seul de ces deux
/// enonces est le sujet du jeu.
///
/// Le profil reste une <b>donnee</b> : ajouter un quatrieme survivant, c'est ajouter une entree au
/// fichier, jamais une classe.
/// </summary>
public sealed class SurvivorProfile
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Le personnage dessine qui represente ce survivant : <c>female_1</c>, <c>male_3</c>… Absent, le
    /// roster en attribue un par rang. Ce n'est pas une identite — deux survivants peuvent partager un
    /// personnage, et c'est l'anneau de couleur au sol qui les separe (livrable 12).
    /// </summary>
    public string? Character { get; init; }

    /// <summary>Profil de repli : celui d'un survivant cree sans fiche.</summary>
    public static SurvivorProfile Default { get; } = new() { Id = "survivant", Name = "Survivant" };

    public override string ToString() => Name;
}

/// <summary>
/// L'equipe, chargee depuis <c>config/survivors.json</c>.
///
/// Comme <c>UtilityGoalSet</c> pour les objectifs et <c>PlanningDomain</c> pour les actions, le
/// fichier est valide des le chargement : un profil sans identifiant ou un doublon doit se voir tout
/// de suite, et non produire un survivant silencieusement inerte.
/// </summary>
public sealed class SurvivorRoster
{
    public List<SurvivorProfile> Survivors { get; init; } = [];

    public static SurvivorRoster Empty { get; } = new();

    public SurvivorProfile? Find(string? id)
        => id is null
            ? null
            : Survivors.FirstOrDefault(profile => profile.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static SurvivorRoster FromJson(string json)
    {
        var roster = JsonSerializer.Deserialize<SurvivorRoster>(json, GameConfig.JsonOptions) ?? Empty;
        roster.Validate();
        return roster;
    }

    public void Validate()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in Survivors)
        {
            if (string.IsNullOrWhiteSpace(profile.Id))
                throw new InvalidOperationException("Un survivant sans identifiant a ete declare.");

            if (!seen.Add(profile.Id))
                throw new InvalidOperationException($"Le survivant « {profile.Id} » est declare deux fois.");

            if (string.IsNullOrWhiteSpace(profile.Name))
                throw new InvalidOperationException($"Le survivant « {profile.Id} » n'a pas de nom.");
        }
    }
}
