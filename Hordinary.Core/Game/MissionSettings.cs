namespace Hordinary.Game;

/// <summary>
/// Reglages de la mission elle-meme, section <c>mission</c> de <c>config/game_config.json</c>.
///
/// Une mission n'est pas une partie ouverte : le van ne peut pas attendre indefiniment, et une
/// expedition qui s'eternise n'est plus une expedition. <see cref="TimeLimit"/> est ce temps
/// imparti ; passe ce delai, le groupe entier est rappele au van, ou qu'il en soit.
///
/// La limite est ici et non dans le code parce qu'elle est un reglage de jeu comme un autre : une
/// mission longue, une mission courte et une mission sans limite se reglent dans un fichier. Une
/// mission de <c>config/missions.json</c> peut la redire pour elle seule (<c>time_limit</c>), sur le
/// meme modele que <c>zombie_count</c> : une mission ne redit que ce qui la distingue.
/// </summary>
public sealed class MissionSettings
{
    /// <summary>
    /// Duree maximale d'une mission, en secondes. Zero ou moins : pas de limite, et le rappel n'a
    /// alors jamais lieu — c'est ce que veulent les tests qui observent autre chose que l'horloge.
    /// </summary>
    public double TimeLimit { get; init; } = 300d;

    /// <summary>
    /// Poids du rappel dans les scores de la Utility AI, sur le modele de
    /// <c>utility_ai.player_order_priority</c>.
    ///
    /// Il vaut plus qu'un ordre du joueur — le van part, ce n'est plus negociable avec la mission —
    /// sans pour autant devenir une contrainte : la securite personnelle module toujours le score,
    /// et quelqu'un qui a une horde sur les talons commence par lui echapper. Un rappel qui ne se
    /// discute meme plus serait un pilotage, ce que le livrable 15 refuse.
    /// </summary>
    public float RecallPriority { get; init; } = 1f;

    /// <summary>
    /// De combien de cases on peut s'ecarter du van sans cesser d'etre « rentre ».
    ///
    /// Sans cette tolerance, il n'y a que deux reponses possibles, et les deux sont fausses. Compter
    /// « rentre » celui qui est au van a cet instant seulement fait ressortir du van quiconque fait
    /// un pas de cote, et repousse la fin de la mission a un instant ou les trois sont sur la meme
    /// poignee de cases. Retenir le retour une fois pour toutes fait l'inverse, et bien pire : un
    /// survivant qui traverse le camp au debut de l'expedition reste « rentre » pour le reste de la
    /// partie, et la victoire se declare alors que le groupe est a l'autre bout du quartier — c'est
    /// exactement ce que faisait la carte livree.
    ///
    /// Deux cases : de quoi tourner autour du vehicule, pas de quoi repartir en mission.
    /// </summary>
    public int HomeRadius { get; init; } = 2;

    /// <summary>
    /// Combien de temps la carte reste a l'ecran une fois le verdict rendu, avant que le camp ne
    /// reprenne la main, en secondes.
    ///
    /// Il en faut un peu : le verdict tombe souvent au moment ou le dernier survivant franchit le
    /// seuil du van, et couper immediatement priverait le joueur de voir arriver celui qu'il
    /// attendait. Il n'en faut pas beaucoup : plus rien ne bouge derriere le verdict, et une carte
    /// figee n'a rien de plus a montrer.
    ///
    /// Zero ou moins : le camp revient a la frame suivante, ce que veut un test.
    /// </summary>
    public double DebriefSeconds { get; init; } = 5d;
}
