using Godot;

namespace Hordinary.Game;

/// <summary>
/// Sonde de capture : instancie la scene principale, laisse la partie se mettre en place quelques
/// secondes, enregistre la vue dans un PNG puis quitte.
///
/// Elle sert a regarder l'interface sans ouvrir l'editeur, dans une session ou l'ecran ne peut pas
/// etre photographie de l'exterieur. Elle n'est jamais chargee par le jeu : rien ne la reference, et
/// on l'ouvre en passant sa scene en argument a Godot.
/// </summary>
public partial class ShotProbe : Node
{
    /// <summary>Secondes de jeu laissees avant la photo, le temps que la mission demarre.</summary>
    [Export] public float Delay { get; set; } = 5f;

    [Export] public string Output { get; set; } = "user://shot.png";

    public override void _Ready()
    {
        var scene = GD.Load<PackedScene>("res://Scenes/Main.tscn");
        AddChild(scene.Instantiate());

        var timer = GetTree().CreateTimer(Delay);
        timer.Timeout += Capture;
    }

    private void Capture()
    {
        // Une frame de plus apres le reveil du minuteur : sans elle, la texture rendue est celle
        // d'avant la derniere passe d'interface.
        RenderingServer.ForceDraw();

        var image = GetViewport().GetTexture().GetImage();
        image.SavePng(Output);

        GD.Print($"[Shot] capture ecrite dans {ProjectSettings.GlobalizePath(Output)}");
        GetTree().Quit();
    }
}
