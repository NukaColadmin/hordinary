using Hordinary.AI.Utility;
using Hordinary.Agents;

namespace Hordinary.Tests;

/// <summary>
/// La jauge <c>SANITY</c> : la motivation, affichee sur la fiche de personnage a cote de la sante.
///
/// Elle repond a une question que la panique ne pose pas. La panique est une emotion de l'instant :
/// elle monte devant un zombie et retombe des que la rue est vide, et c'est pour cela qu'elle est
/// une entree de decision et non un etat. La motivation est ce qu'il en reste apres coup — elle ne
/// se refait pas dans la meme minute, et c'est elle qui distingue un survivant frais d'un survivant
/// qui revient de trois missions.
/// </summary>
public class SanityTests
{
    private static Survivor Alone() => new MissionRun(team: false).Alice;

    // --- Ce qui la vide et ce qui la remplit -----------------------------------------------------

    [Fact]
    public void Un_survivant_part_avec_sa_motivation_pleine()
    {
        var alice = Alone();

        Assert.Equal(alice.Settings.MaxSanity, alice.Sanity);
        Assert.Equal(1f, alice.SanityRatio);
        Assert.True(alice.Motivated);
    }

    /// <summary>
    /// La motivation suit la <b>panique</b>, pas le danger percu, et la nuance porte tout le sens de
    /// la jauge : voir un zombie de loin sans s'affoler ne coute rien. C'est d'avoir eu peur qui coute.
    /// </summary>
    [Fact]
    public void La_peur_use_la_motivation()
    {
        var mission = new MissionRun(team: false);
        var alice = mission.Alice;

        alice.UpdatePanic(1f, 5f);
        Assert.True(alice.Panic > 0.5f, "La panique n'est pas montee : le test ne mesure rien.");

        float before = alice.Sanity;
        alice.UpdateSanity(1f);

        Assert.True(alice.Sanity < before, "La motivation n'a pas baisse malgre la panique.");
    }

    [Fact]
    public void Au_calme_la_motivation_se_refait_sans_depasser_son_maximum()
    {
        var alice = Alone();

        alice.DrainSanity(alice.Settings.MaxSanity * 0.5f);
        float wounded = alice.Sanity;

        // Panique nulle : la recuperation joue a plein.
        alice.UpdateSanity(1f);
        Assert.True(alice.Sanity > wounded);

        for (int second = 0; second < 500; second++) alice.UpdateSanity(1f);
        Assert.Equal(alice.Settings.MaxSanity, alice.Sanity);
    }

    /// <summary>
    /// Encaisser un coup entame la motivation, et ce n'est <b>pas</b> neutralise par le mode debug :
    /// l'interrupteur eteint la peur, pas ses causes. Une blessure reste une blessure.
    /// </summary>
    [Fact]
    public void Une_blessure_entame_la_motivation_meme_sans_panique()
    {
        var mission = new MissionRun(team: false);
        mission.Band.PanicEnabled = false;

        var alice = mission.Alice;
        float before = alice.Sanity;

        alice.TakeDamage(alice.Settings.MaxHealth * 0.5f);

        Assert.Equal(0f, alice.Panic);
        Assert.True(alice.Sanity < before, "Une blessure n'a rien coute a la motivation.");
    }

    /// <summary>
    /// Rassurer un allie lui rend de l'envie de continuer. C'est ce qui fait de Claire autre chose
    /// qu'une infirmiere : elle est la raison pour laquelle le groupe ne rentre pas a mi-chemin.
    /// </summary>
    [Fact]
    public void Rassurer_un_allie_lui_rend_de_la_motivation()
    {
        var mission = new MissionRun(team: true);
        var bob = mission.AgentOf("bob").Survivor;

        bob.DrainSanity(bob.Settings.MaxSanity * 0.5f);
        float before = bob.Sanity;

        bob.Calm(mission.AgentOf("claire").Survivor.Settings.CalmAmount);

        Assert.True(bob.Sanity > before, "Un survivant rassure n'a rien recupere.");
    }

    // --- Configuration ----------------------------------------------------------------------------

    /// <summary>
    /// Le maximum se redit dans le profil, comme la sante : ce sont trois personnes differentes, et
    /// elles ne tiennent pas pareil. Les trois valeurs viennent de <c>config/survivors.json</c>.
    /// </summary>
    [Fact]
    public void Le_profil_peut_redefinir_la_motivation_maximale()
    {
        var mission = new MissionRun(team: true);

        var maxima = mission.Band.Survivors
            .Select(survivor => survivor.Settings.MaxSanity)
            .ToList();

        Assert.All(maxima, maximum => Assert.True(maximum > 0f));
        Assert.True(maxima.Distinct().Count() > 1, "Les trois profils ont la meme motivation maximale.");
    }

    /// <summary>Un profil muet retombe sur la ligne de base de <c>config/game_config.json</c>.</summary>
    [Fact]
    public void Un_profil_muet_prend_la_motivation_de_la_ligne_de_base()
    {
        var baseline = MissionRun.Config.Survivor;
        var settings = new SurvivorProfile { Id = "x", Name = "X" }.ApplyTo(baseline);

        Assert.Equal(baseline.MaxSanity, settings.MaxSanity);
        Assert.Equal(baseline.SanityDrainPerSecond, settings.SanityDrainPerSecond);
    }

    // --- Effets -----------------------------------------------------------------------------------

    /// <summary>La Utility AI peut s'en servir : la jauge est une entree comme une autre.</summary>
    [Fact]
    public void La_motivation_est_lisible_par_la_utility_ai()
    {
        var mission = new MissionRun(team: false);
        var alice = mission.Alice;

        Assert.True(UtilityInputs.IsKnown("sanity"));
        Assert.True(UtilityInputs.IsKnown("weariness"));

        var context = new UtilityContext(alice, mission.World.Time, MissionRun.Config.UtilityAi);
        Assert.Equal(1f, UtilityInputs.Read("sanity", context));
        Assert.Equal(0f, UtilityInputs.Read("weariness", context));

        alice.DrainSanity(alice.Settings.MaxSanity);

        var empty = new UtilityContext(alice, mission.World.Time, MissionRun.Config.UtilityAi);
        Assert.Equal(0f, UtilityInputs.Read("sanity", empty));
        Assert.Equal(1f, UtilityInputs.Read("weariness", empty));
    }

    /// <summary>
    /// La consequence qui empeche la jauge d'etre decorative : a zero, le survivant n'a plus de
    /// raison de rester dehors. Il abandonne la mission et rentre au camp, sans que personne n'ait
    /// eu besoin d'aller le lui dire.
    /// </summary>
    [Fact]
    public void A_zero_le_survivant_rentre_au_camp_de_lui_meme()
    {
        var mission = new MissionRun(team: true);
        var bob = mission.AgentOf("bob");

        Assert.True(MissionRun.Config.Mission.AbandonAtZeroSanity, "Le reglage livre desarme le test.");

        bob.Survivor.DrainSanity(bob.Survivor.Settings.MaxSanity);
        Assert.False(bob.Survivor.Motivated);

        mission.Recall.Advance(mission.World.Time);

        Assert.True(bob.Survivor.Recalled);
        Assert.NotNull(bob.Survivor.RecallMessage);

        // Il etait au camp : il en sort donc dans la meme passe.
        Assert.True(bob.Survivor.Returned);
    }
}
