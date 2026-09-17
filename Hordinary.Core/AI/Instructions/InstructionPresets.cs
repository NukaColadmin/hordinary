using System.Text.Json;
using Hordinary.Config;

namespace Hordinary.AI.Instructions;

/// <summary>Une instruction pre-ecrite : sa phrase, et la contrainte qu'elle vaut.</summary>
public sealed class InstructionPreset
{
    public string Id { get; init; } = string.Empty;

    /// <summary>La phrase montree au joueur, celle qu'il aurait pu taper.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>La contrainte dans la grammaire fermee, telle qu'un modele aurait du la rendre.</summary>
    public string Constraint { get; init; } = string.Empty;

    public override string ToString() => $"{Label} → {Constraint}";
}

/// <summary>
/// Les instructions pre-ecrites de <c>config/instruction_presets.json</c>.
///
/// <para>
/// Elles ne sont pas un mode degrade : elles sont ce qui rend le livrable 15 demontrable sans reseau
/// et sans modele. La contrainte qu'elles portent passe les memes portillons que celle d'un modele —
/// <see cref="ConstraintGrammar"/> ne fait aucune difference entre les deux sources, et c'est
/// exactement la propriete qu'on veut : ce qui est verifie l'est parce que c'est une contrainte, pas
/// parce qu'on se mefie des modeles.
/// </para>
/// </summary>
public sealed class InstructionPresetCatalogue
{
    public List<InstructionPreset> Presets { get; init; } = [];

    public static InstructionPresetCatalogue Empty { get; } = new();

    public static InstructionPresetCatalogue FromJson(string json)
        => JsonSerializer.Deserialize<InstructionPresetCatalogue>(json, GameConfig.JsonOptions) ?? Empty;

    public InstructionPreset? Find(string? id)
        => id is null ? null : Presets.FirstOrDefault(preset => preset.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public override string ToString() => $"{Presets.Count} instructions pre-ecrites";
}
