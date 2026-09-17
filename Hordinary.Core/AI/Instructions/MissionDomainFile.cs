using Hordinary.AI.Planning.Pddl;

namespace Hordinary.AI.Instructions;

/// <summary>
/// Le domaine compile d'une mission, ecrit sur disque (livrable 15, « Ou vit le domaine compile »).
///
/// <para>
/// <c>config/planner_domain.json</c> n'est <b>jamais</b> modifie, et le <c>domain.pddl</c> versionne
/// de la spec G non plus. La compilation produit un objet en memoire — celui que GBFS joue — et, pour
/// la duree de la mission, une trace lisible dans <c>missions/&lt;id&gt;/</c>. Retirer toutes les
/// instructions rend le jeu bit a bit identique a ce qu'il etait ; ces fichiers ne sont pas relus par
/// le jeu, et personne ne planifie a partir d'eux.
/// </para>
///
/// <para>
/// Deux fichiers, parce qu'il y a deux lecteurs et qu'ils ne veulent pas la meme chose :
/// </para>
///
/// <list type="bullet">
/// <item><c>domain.constrained.pddl</c> — le domaine reecrit, <b>sans</b> bloc <c>(:constraints …)</c>,
/// dans le meme fragment que la spec G. C'est celui du portillon 3 : Fast Downward ne prend pas les
/// contraintes de trajectoire PDDL 3 en entree, et lui en soumettre serait lui donner un fichier
/// qu'il refuse.</item>
/// <item><c>domain.declared.pddl</c> — le meme domaine <b>avec</b> le bloc PDDL 3, celui qui dit ce
/// que le joueur voulait plutot que ce que la compilation en a fait. C'est ce que VAL corrobore, et
/// c'est aussi ce qu'un lecteur ouvre pour comprendre l'instruction sans lire le compilateur.</item>
/// </list>
/// </summary>
public static class MissionDomainFile
{
    public const string CompiledName = "domain.constrained.pddl";

    public const string DeclaredName = "domain.declared.pddl";

    /// <summary>Le dossier d'une mission, sous la racine du projet.</summary>
    public static string DirectoryOf(string root, string missionId)
    {
        ArgumentNullException.ThrowIfNull(root);

        return Path.Combine(root, "missions", Planning.SymbolicStateBuilder.ObjectNameOf(missionId ?? string.Empty));
    }

    /// <summary>
    /// Ecrit les deux rendus et renvoie les chemins ecrits. Sans contrainte active, rien n'est ecrit :
    /// un domaine que personne n'a reecrit est deja dans <c>tools/pddl/</c>, et le recopier a chaque
    /// depart ne renseignerait sur rien.
    /// </summary>
    public static IReadOnlyList<string> Write(
        string root,
        string missionId,
        PddlDomain compiled,
        IReadOnlyList<TacticalConstraint> constraints)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        ArgumentNullException.ThrowIfNull(constraints);

        if (constraints.Count == 0) return [];

        string directory = DirectoryOf(root, missionId);
        Directory.CreateDirectory(directory);

        string header = Header(constraints);

        var declared = compiled.With(constraints: [.. constraints.Select(constraint => constraint.Declarative(compiled))]);

        string compiledPath = Path.Combine(directory, CompiledName);
        string declaredPath = Path.Combine(directory, DeclaredName);

        File.WriteAllText(compiledPath, header + PddlWriter.Render(compiled));
        File.WriteAllText(declaredPath, header + PddlWriter.Render(declared, includeConstraints: true));

        return [compiledPath, declaredPath];
    }

    /// <summary>
    /// Les phrases du joueur en tete de fichier. Elles ne servent a aucun outil — c'est un commentaire
    /// Lisp — et c'est justement pour ca qu'elles sont la : le fichier doit pouvoir s'ouvrir seul.
    /// </summary>
    private static string Header(IReadOnlyList<TacticalConstraint> constraints)
    {
        var lines = new List<string>
        {
            "; Domaine compile pour une mission (livrable 15). Genere, non versionne.",
            "; config/planner_domain.json et tools/pddl/domain.pddl restent inchanges.",
            ";",
        };

        foreach (var constraint in constraints)
        {
            lines.Add($"; {constraint.Utterance ?? constraint.Text}");
            if (constraint.Utterance is not null) lines.Add($";   → {constraint.Text}");
        }

        lines.Add(string.Empty);

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}
