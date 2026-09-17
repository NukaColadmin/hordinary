using Hordinary.Agents;
using Hordinary.Config;
using Hordinary.Game;
using Hordinary.World;
using Hordinary.World.MapGeneration;

namespace Hordinary.Tests;

/// <summary>
/// Tests du livrable 16 : les missions aleatoires et leurs recompenses.
///
/// Un generateur ne se teste pas comme une fonction. On ne verifie jamais « l'offre attendue » —
/// il y en a des milliers — mais deux familles de proprietes : ce qui doit valoir pour <b>toute</b>
/// offre rendue (une zone jouable, des exigences realisables, une recompense qui existe), et ce qui
/// doit valoir <b>une fois pour toutes</b> (meme graine, memes offres ; l'essence debitee une seule
/// fois ; une recompense versee au plus une fois).
///
/// Aucun nom de zone, d'objet ni d'arme n'est ecrit ici : les tests lisent <c>config/zones.json</c>,
/// les catalogues livres et le bloc <c>mission_generation</c> de <c>config/game_config.json</c>.
/// Retirer une zone du fichier ou une arme des recompenses doit faire bouger la suite toute seule —
/// c'est la meme regle que partout ailleurs, et elle vaut aussi pour ce qui verifie.
/// </summary>
public class MissionGenerationTests
{
    private static GameConfig Config => MissionRun.Config;

    private static MissionGenerationSettings Settings => Config.MissionGeneration;

    private static ZoneCatalogue Zones { get; } = ZoneCatalogue.FromJson(DemoMap.ReadConfig("zones.json"));

    private static MapGenConfig MapGen { get; } = MapGenConfig.FromJson(DemoMap.ReadConfig("mapgen.json"));

    /// <summary>Quelques graines fixes : la suite doit etre reproductible, pas aleatoire.</summary>
    private static readonly int[] Seeds = [7, 42, 1234, 20260910, 90210];

    private static MissionGenerator Generator(MissionGenerationSettings? settings = null)
        => new(settings ?? Settings, Zones, DemoMap.Items);

    /// <summary>
    /// Tout ce qu'une offre promet, rendu en une chaine. C'est ce qu'on compare pour le
    /// determinisme : <c>MissionDefinition</c> est un <c>record</c>, mais ses recompenses sont une
    /// liste, et l'egalite structurelle d'un <c>record</c> compare une liste par reference. Une
    /// chaine dit exactement ce que le livrable exige de reproduire — et un echec se lit.
    /// </summary>
    private static string Describe(MissionDefinition offer)
        => string.Join(" | ",
            offer.Id, offer.Name, offer.Seed, offer.Type, offer.Difficulty,
            offer.Target, offer.ObjectiveZone, offer.Requirements,
            offer.TimeLimit, offer.EntryFuelCost, offer.RecommendedTeam,
            offer.ZombieCount, offer.MapProfile, offer.MapSeed,
            string.Join(",", offer.Rewards.Select(reward => $"{reward.Category}:{reward.Item}x{reward.Quantity}")));

    // --- L'offre du jour ----------------------------------------------------------------------------

    /// <summary>La configuration livree propose bien le nombre d'offres qu'elle annonce.</summary>
    [Fact]
    public void La_configuration_livree_propose_trois_missions()
    {
        Assert.Equal(3, Settings.OfferCount);

        var offers = Generator().Offer(missionSeed: 2026);

        Assert.Equal(Settings.OfferCount, offers.Count);
        Assert.All(offers, offer => Assert.True(offer.IsOffer, $"« {offer.Name} » n'est pas une offre generee."));
        Assert.Equal(offers.Count, offers.Select(offer => offer.Id).Distinct().Count());
    }

    /// <summary>
    /// Une mission generee n'a pas de briefing. C'est une clause du livrable et non un detail
    /// d'affichage : le joueur compare trois offres sur des chiffres, et une phrase inventee serait
    /// la seule partie de l'offre qui ne se compare a rien.
    /// </summary>
    [Fact]
    public void Une_mission_generee_n_a_pas_de_description()
    {
        foreach (int seed in Seeds)
        {
            foreach (var offer in Generator().Offer(seed))
            {
                Assert.Null(offer.Description);

                // Le titre est fait du type et du nom de la zone, et de rien d'autre.
                Assert.Contains(" — ", offer.Name, StringComparison.Ordinal);
                Assert.StartsWith(MissionObjectiveNames.Of(offer.Type!.Value), offer.Name, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// A graine et version egales, offres egales — jusqu'a la derniere quantite de recompense. Deux
    /// generateurs distincts, pour qu'aucun etat interne ne puisse expliquer le resultat.
    /// </summary>
    [Fact]
    public void Deux_generations_sur_la_meme_graine_donnent_les_memes_offres()
    {
        foreach (int seed in Seeds)
        {
            var first = Generator().Offer(seed, day: 3);
            var second = Generator().Offer(seed, day: 3);

            Assert.Equal(
                first.Select(Describe).ToList(),
                second.Select(Describe).ToList());
        }
    }

    /// <summary>
    /// Une offre est figee des qu'elle est affichee : la relire ne la change pas. Le corollaire est
    /// que deux graines differentes doivent, elles, produire autre chose — sans quoi la generation
    /// ne genererait rien.
    /// </summary>
    [Fact]
    public void Deux_graines_differentes_finissent_par_differer()
    {
        var rendered = Seeds.Select(seed => string.Join(" / ", Generator().Offer(seed).Select(Describe))).ToList();

        Assert.True(
            rendered.Distinct().Count() > 1,
            "Toutes les graines essayees produisent exactement les memes offres.");
    }

    /// <summary>Le jour de campagne entre dans la graine : les offres du lendemain sont d'autres offres.</summary>
    [Fact]
    public void Le_jour_de_campagne_renouvelle_l_offre()
    {
        var days = Enumerable.Range(0, 5)
            .Select(day => string.Join(" / ", Generator().Offer(2026, day).Select(Describe)))
            .ToList();

        Assert.True(days.Distinct().Count() > 1, "Les offres ne changent pas d'un jour a l'autre.");
    }

    /// <summary>
    /// La version du generateur fait partie de la graine. Changer les regles sans la changer ferait
    /// mentir la promesse « meme graine, memes missions » ; la changer l'annonce honnetement.
    /// </summary>
    [Fact]
    public void La_version_du_generateur_change_les_offres_a_graine_egale()
    {
        var v1 = Generator().Offer(2026);

        var settings = Settings;
        var v2 = Generator(CloneWith(settings, version: settings.GeneratorVersion + 1)).Offer(2026);

        Assert.NotEqual(
            string.Join(" / ", v1.Select(Describe)),
            string.Join(" / ", v2.Select(Describe)));
    }

    // --- Les zones visees ---------------------------------------------------------------------------

    /// <summary>
    /// Chaque offre vise une zone qui existe, qui n'est ni le camp ni la sortie, et qui offre une
    /// case praticable sur le quartier reellement genere pour elle. Une offre qui viserait un lieu
    /// absent enverrait le groupe nulle part, et rien dans le jeu ne le dirait.
    /// </summary>
    [Fact]
    public void Chaque_offre_vise_une_zone_jouable_et_distincte_du_camp()
    {
        foreach (int seed in Seeds.Take(3))
        {
            foreach (var offer in Generator().Offer(seed))
            {
                Assert.NotNull(Zones.Find(offer.ObjectiveProfile));
                Assert.NotEqual(offer.Camp, offer.Objective);
                Assert.DoesNotContain(offer.Objective, (ZoneKind[])[ZoneKind.Start, ZoneKind.Extraction]);

                var map = District(offer).Map;
                var zone = offer.ObjectiveIn(map);

                Assert.True(zone is not null, $"« {offer.Name} » vise une zone absente du quartier genere.");
                Assert.True(
                    map.FindWalkableIn(zone!).HasValue,
                    $"« {offer.Name} » : {zone!.Name} n'offre aucune case praticable.");
            }
        }
    }

    /// <summary>Les offres d'un meme jour visent des zones differentes tant que le catalogue le permet.</summary>
    [Fact]
    public void Les_offres_d_un_meme_jour_visent_des_zones_differentes()
    {
        foreach (int seed in Seeds)
        {
            var offers = Generator().Offer(seed);
            var targets = offers.Select(offer => offer.ObjectiveProfile).ToList();

            Assert.Equal(Math.Min(offers.Count, Zones.Zones.Count), targets.Distinct().Count());
        }
    }

    // --- Les trois types ----------------------------------------------------------------------------

    /// <summary>
    /// Les trois types sont reellement produits, et chacun avec des exigences valides : un nombre de
    /// zombies inferieur a ce que la zone recevra, des objets du catalogue, des ressources vitales.
    /// </summary>
    [Fact]
    public void Les_trois_types_sont_generes_avec_des_exigences_valides()
    {
        var seen = new HashSet<MissionObjectiveKind>();

        for (int seed = 0; seed < 60; seed++)
        {
            foreach (var offer in Generator().Offer(seed))
            {
                var kind = offer.Type!.Value;
                var requirements = offer.Requirements!;
                seen.Add(kind);

                switch (kind)
                {
                    case MissionObjectiveKind.KillZombies:
                        Assert.True(requirements.Zombies > 0, $"« {offer.Name} » ne demande aucune elimination.");

                        float density = Zones.Find(offer.ObjectiveProfile)!.ZombieDensity;
                        int assigned = (int)MathF.Floor((offer.ZombieCount ?? 0) * Math.Clamp(density, 0.05f, 1f));

                        Assert.True(
                            assigned >= requirements.Zombies,
                            $"« {offer.Name} » demande {requirements.Zombies} morts pour {assigned} zombies affectes.");
                        break;

                    case MissionObjectiveKind.CollectObjects:
                        Assert.NotEmpty(requirements.Items);
                        Assert.All(requirements.Items, line =>
                        {
                            Assert.True(line.Quantity > 0);
                            Assert.NotNull(DemoMap.Items.Find(line.Item));

                            // « Compatible avec la table de butin de la zone » : on ne demande pas un
                            // fusil a la quincaillerie.
                            Assert.Contains(line.Item, Zones.Find(offer.ObjectiveProfile)!.Loot.Keys);
                        });
                        break;

                    default:
                        Assert.NotEmpty(requirements.Resources);
                        Assert.All(requirements.Resources, line =>
                        {
                            Assert.True(line.Quantity > 0);
                            Assert.Contains(line.Resource, MissionRequirements.PriorityResources);
                        });
                        break;
                }
            }

            if (seen.Count == Enum.GetValues<MissionObjectiveKind>().Length) return;
        }

        Assert.Fail($"Types jamais generes : {string.Join(", ", Enum.GetValues<MissionObjectiveKind>().Except(seen))}.");
    }

    /// <summary>
    /// Ce qu'une offre de recuperation demande est reellement pose dans sa zone, en quantite. La
    /// verification passe par le quartier genere et le harnais de mission — c'est-a-dire par le meme
    /// chemin que le jeu, <see cref="MissionDefinition.SuppliesFor"/> compris.
    /// </summary>
    [Fact]
    public void Les_objets_requis_sont_poses_dans_la_zone_cible_en_quantite()
    {
        var offer = FirstOfKind(kind => kind is MissionObjectiveKind.CollectObjects);
        var run = new MissionRun(map: District(offer).Map, definition: offer, settings: Untimed);

        var zone = offer.ObjectiveIn(run.Map)!;

        foreach (var line in offer.Requirements!.Items)
        {
            int posed = run.World.Items.Values.Count(
                item => item.ItemId.Equals(line.Item, StringComparison.OrdinalIgnoreCase)
                     && zone.Contains(item.Position));

            Assert.True(
                posed >= line.Quantity,
                $"« {offer.Name} » demande {line.Quantity} {line.Item} et n'en pose que {posed} dans {zone.Name}.");
        }
    }

    /// <summary>
    /// Une ressource vitale est un objet transportable comme un autre : la mission demande
    /// « 2 essences », le monde pose deux jerricans, et c'est le catalogue qui fait la traduction.
    /// Aucun code ne nomme le jerrican.
    /// </summary>
    [Fact]
    public void Une_ressource_prioritaire_est_suivie_par_l_objet_qui_la_porte()
    {
        var offer = FirstOfKind(kind => kind is MissionObjectiveKind.CollectPriorityResources);
        var payload = offer.Requirements!.Payload(MissionObjectiveKind.CollectPriorityResources, DemoMap.Items);

        Assert.NotEmpty(payload);

        foreach (var line in offer.Requirements.Resources)
        {
            var stocker = CampStock.StockerOf(line.Resource, DemoMap.Items);

            Assert.NotNull(stocker);
            Assert.Contains(payload, entry => entry.Item == stocker!.Id && entry.Quantity == line.Quantity);
        }
    }

    /// <summary>
    /// Ce qu'une mission de ravitaillement demande rentre reellement dans la reserve qu'elle visait.
    /// C'est la seconde moitie du contrat, et la seule qui interesse le camp : rapporter deux
    /// jerricans doit faire monter l'essence, pas remplir un sac.
    ///
    /// Le versement n'a lieu qu'apres le verdict — vider les sacs plus tot ferait redescendre les
    /// compteurs au moment precis ou la mission se remplit.
    /// </summary>
    [Fact]
    public void Les_ressources_rapportees_alimentent_la_reserve_visee()
    {
        var offer = FirstOfKind(kind => kind is MissionObjectiveKind.CollectPriorityResources);
        var camp = new CampStock(new CampSettings { Food = 0f, Water = 0f, Fuel = 0f, Ammo = 0f });
        var carrier = MissionRun.Lone("alice", new GridPosition(0, 0));

        foreach (var line in offer.Requirements!.Payload(offer.Type!.Value, DemoMap.Items))
        {
            for (int copy = 0; copy < line.Quantity; copy++) carrier.AddToInventory(line.Item);
        }

        camp.Deposit(carrier, DemoMap.Items);

        foreach (var line in offer.Requirements.Resources)
        {
            Assert.True(
                camp.Of(line.Resource) > 0f,
                $"« {offer.Name} » demandait {line.Quantity} {line.Resource}, et la reserve est restee vide.");
        }

        // Ce qui a ete verse a quitte le sac : un jerrican ne compte pas deux fois.
        Assert.All(
            offer.Requirements.Payload(offer.Type.Value, DemoMap.Items),
            line => Assert.False(carrier.Carries(line.Item)));
    }

    // --- Les compteurs ------------------------------------------------------------------------------

    /// <summary>Un zombie abattu dans la zone cible compte, et il ne compte qu'une fois.</summary>
    [Fact]
    public void Un_zombie_tue_dans_la_zone_cible_compte_une_seule_fois()
    {
        var run = Field(Kill(2));
        var horde = Horde(run);

        // Deux zombies pour deux morts demandees : une horde plus courte que l'objectif rendrait la
        // mission impossible des la premiere passe, et c'est exactement ce que le generateur evite.
        horde.Spawn("z1", InTarget(run));
        horde.Spawn("z2", InTarget(run));
        run.Tracker.Evaluate(run.World.Time);

        Assert.Equal(0, run.Objectives!.Killed);

        horde.Remove("z1");

        // Trois evaluations : le compteur monte a la premiere et ne bouge plus.
        for (int pass = 0; pass < 3; pass++) run.Tracker.Evaluate(run.World.Time);

        Assert.Equal(1, run.Objectives.Killed);
    }

    /// <summary>
    /// Un zombie abattu ailleurs ne fait pas progresser l'objectif. C'est ce qui donne son sens a la
    /// zone cible : sans cette regle, « eliminer huit zombies au supermarche » se remplirait dans la
    /// rue devant le camp.
    /// </summary>
    [Fact]
    public void Un_zombie_tue_hors_de_la_zone_cible_ne_compte_pas()
    {
        var run = Field(Kill(2));
        var horde = Horde(run);

        horde.Spawn("z1", new GridPosition(1, 6));
        run.Tracker.Evaluate(run.World.Time);
        horde.Remove("z1");
        run.Tracker.Evaluate(run.World.Time);

        Assert.Equal(0, run.Objectives!.Killed);
        Assert.False(run.Objectives.Fulfilled);
    }

    /// <summary>
    /// Un objet ramasse dans la zone cible compte ; le meme objet perdu ensuite rend l'objectif
    /// incomplet. La progression n'est pas un acquis, c'est un etat — et c'est pourquoi la mission
    /// se conclut au retour et pas au ramassage.
    /// </summary>
    [Fact]
    public void Un_objet_ramasse_dans_la_zone_compte_et_le_perdre_defait_le_compteur()
    {
        var run = Field(Collect("bandage", 1));
        var alice = run.Alice;

        alice.Position = InTarget(run);
        alice.AddToInventory("bandage");
        run.Tracker.Evaluate(run.World.Time);

        Assert.True(run.Objectives!.Fulfilled);

        alice.RemoveFromInventory("bandage");
        run.Tracker.Evaluate(run.World.Time);

        Assert.False(run.Objectives.Fulfilled);
    }

    /// <summary>Ramasser ailleurs ne remplit rien : c'est dans la zone que l'objectif se joue.</summary>
    [Fact]
    public void Un_objet_ramasse_hors_de_la_zone_ne_compte_pas()
    {
        var run = Field(Collect("bandage", 1));

        run.Alice.Position = new GridPosition(1, 6);
        run.Alice.AddToInventory("bandage");
        run.Tracker.Evaluate(run.World.Time);

        Assert.False(run.Objectives!.Fulfilled);
    }

    /// <summary>
    /// Le ramassage ne suffit pas : il faut rentrer. La mission se conclut au camp, avec le groupe
    /// au complet — c'est la regle du livrable 8, et le livrable 16 ne la change pas.
    /// </summary>
    [Fact]
    public void Une_recuperation_ne_se_conclut_qu_au_retour_au_camp()
    {
        var run = Field(Collect("bandage", 1));

        run.Alice.Position = InTarget(run);
        run.Alice.AddToInventory("bandage");
        run.Tracker.Evaluate(run.World.Time);

        Assert.True(run.Objectives!.Fulfilled);
        Assert.Equal(MissionOutcome.Running, run.Tracker.Outcome);

        run.Alice.Position = run.Spawn(ZoneKind.Start);
        run.Tracker.Evaluate(run.World.Time);

        Assert.Equal(MissionOutcome.Victory, run.Tracker.Outcome);
    }

    /// <summary>
    /// Une elimination n'a rien a rapporter : le compteur de morts et le retour du groupe suffisent.
    /// </summary>
    [Fact]
    public void Une_elimination_se_conclut_sur_le_compteur_et_le_retour()
    {
        var run = Field(Kill(1));
        var horde = Horde(run);

        horde.Spawn("z1", InTarget(run));
        run.Alice.Position = InTarget(run);
        run.Tracker.Evaluate(run.World.Time);

        horde.Remove("z1");
        run.Tracker.Evaluate(run.World.Time);

        Assert.True(run.Objectives!.Fulfilled);
        Assert.Equal(MissionOutcome.Running, run.Tracker.Outcome);

        run.Alice.Position = run.Spawn(ZoneKind.Start);
        run.Tracker.Evaluate(run.World.Time);

        Assert.Equal(MissionOutcome.Victory, run.Tracker.Outcome);
    }

    /// <summary>
    /// La mission jouee est celle qui a ete choisie, et elle ne se regenere pas en cours de route :
    /// ce que le suivi de partie regarde a la fin est mot pour mot ce que l'offre annoncait.
    /// </summary>
    [Fact]
    public void Une_mission_generee_n_est_pas_regeneree_pendant_son_execution()
    {
        var offer = FirstOffer(_ => true);
        var run = new MissionRun(map: District(offer).Map, definition: offer, settings: Untimed);

        string before = Describe(run.Tracker.Mission);

        for (int tick = 0; tick < 200; tick++) run.Tick();

        Assert.Equal(before, Describe(run.Tracker.Mission));
        Assert.Same(offer, run.Tracker.Mission);
    }

    // --- Le cout d'entree ---------------------------------------------------------------------------

    /// <summary>Le cout affiche est celui de la difficulte, et c'est la meme ligne qui les donne.</summary>
    [Fact]
    public void Le_cout_d_entree_suit_la_difficulte()
    {
        foreach (int seed in Seeds)
        {
            foreach (var offer in Generator().Offer(seed))
            {
                var level = MissionDifficultyNames.Parse(offer.Difficulty);

                Assert.NotNull(level);
                Assert.Equal(Settings.EntryFuelCost(level!.Value), offer.EntryFuelCost);
            }
        }
    }

    /// <summary>
    /// Le lancement debite une fois, et une seule. Le relancer ne redebite pas : c'est l'invariant
    /// qui fait qu'un ecran de camp reactif ne peut pas vider les reserves en trois clics.
    /// </summary>
    [Fact]
    public void Le_lancement_debite_le_cout_d_entree_une_seule_fois()
    {
        var camp = new CampStock(new CampSettings { Fuel = 10f });
        var offer = FirstOffer(_ => true);
        var ledger = new MissionRewardService(camp, offer, DemoMap.Items);

        float before = camp.Fuel;

        Assert.True(ledger.PayEntry());
        Assert.True(ledger.Paid);
        Assert.Equal(before - offer.EntryFuelCost, camp.Fuel, 3);

        Assert.True(ledger.PayEntry());
        Assert.Equal(before - offer.EntryFuelCost, camp.Fuel, 3);
    }

    /// <summary>Sans essence, on ne part pas — et rien n'est debite.</summary>
    [Fact]
    public void Un_camp_a_sec_ne_peut_pas_lancer_la_mission()
    {
        var offer = FirstOffer(offer => offer.EntryFuelCost > 0);
        var camp = new CampStock(new CampSettings { Fuel = offer.EntryFuelCost - 1 });
        var ledger = new MissionRewardService(camp, offer, DemoMap.Items);

        Assert.False(ledger.CanLaunch);
        Assert.False(ledger.PayEntry());
        Assert.False(ledger.Paid);
        Assert.Equal(offer.EntryFuelCost - 1, camp.Fuel, 3);
    }

    /// <summary>
    /// Consulter une offre, en ouvrir le detail ou en changer ne coute rien : seul le depart paie.
    /// Et l'essence de l'aller n'est pas rendue par une defaite — le van a roule.
    /// </summary>
    [Fact]
    public void Consulter_une_offre_ne_coute_rien_et_une_defaite_ne_rembourse_pas()
    {
        var camp = new CampStock(new CampSettings { Fuel = 10f });
        var offers = Generator().Offer(2026);

        foreach (var browsed in offers)
        {
            var reader = new MissionRewardService(camp, browsed, DemoMap.Items);
            _ = reader.CanLaunch;
            _ = MissionRewardService.CanAfford(camp, browsed);
        }

        Assert.Equal(10f, camp.Fuel, 3);

        var chosen = offers[0];
        var ledger = new MissionRewardService(camp, chosen, DemoMap.Items);
        ledger.PayEntry();

        float after = camp.Fuel;
        ledger.Grant(new MissionVerdict(MissionOutcome.Defeat, "le groupe est tombe", 12d));

        Assert.Equal(after, camp.Fuel, 3);
    }

    // --- Les recompenses ----------------------------------------------------------------------------

    /// <summary>
    /// Ce qui est affiche avant le depart est ce qui est verse apres la victoire — le meme objet, la
    /// meme quantite. Le joueur choisit son expedition sur cette promesse.
    /// </summary>
    [Fact]
    public void Les_recompenses_annoncees_sont_celles_qui_sont_versees()
    {
        var offer = FirstOffer(_ => true);
        var camp = new CampStock(new CampSettings { Fuel = 20f });
        var ledger = new MissionRewardService(camp, offer, DemoMap.Items);

        Assert.NotEmpty(offer.Rewards);

        ledger.PayEntry();
        var awarded = ledger.Grant(new MissionVerdict(MissionOutcome.Victory, "rentres", 30d));

        Assert.Equal(
            offer.Rewards.Select(reward => $"{reward.Item}x{reward.Quantity}"),
            awarded.Select(reward => $"{reward.Item}x{reward.Quantity}"));

        foreach (var reward in offer.Rewards) Assert.Equal(reward.Quantity, camp.CountOf(reward.Item));
    }

    /// <summary>
    /// Le verdict est relu a chaque frame par la boucle de jeu. Une recompense doit donc etre versee
    /// au plus une fois, quel que soit le nombre de relectures.
    /// </summary>
    [Fact]
    public void Une_recompense_n_est_versee_qu_une_fois_meme_reevaluee()
    {
        var offer = FirstOffer(_ => true);
        var camp = new CampStock(new CampSettings { Fuel = 20f });
        var ledger = new MissionRewardService(camp, offer, DemoMap.Items);

        var victory = new MissionVerdict(MissionOutcome.Victory, "rentres", 30d);

        for (int pass = 0; pass < 5; pass++) ledger.Grant(victory);

        foreach (var reward in offer.Rewards) Assert.Equal(reward.Quantity, camp.CountOf(reward.Item));
    }

    /// <summary>Une defaite, un abandon ou la mort du groupe n'attribuent rien.</summary>
    [Theory]
    [InlineData(MissionOutcome.Defeat)]
    [InlineData(MissionOutcome.Running)]
    public void Une_mission_perdue_ou_inachevee_n_attribue_aucune_recompense(MissionOutcome outcome)
    {
        var offer = FirstOffer(_ => true);
        var camp = new CampStock(new CampSettings { Fuel = 20f });
        var ledger = new MissionRewardService(camp, offer, DemoMap.Items);

        Assert.Empty(ledger.Grant(new MissionVerdict(outcome, "sans victoire", 30d)));
        Assert.Empty(camp.Stored);
    }

    /// <summary>
    /// Un entrepot plein ne fait pas disparaitre une recompense : elle attend dans la file, et le
    /// total de ce qui est range et de ce qui attend est exactement ce qui a ete promis.
    /// </summary>
    [Fact]
    public void Une_recompense_qui_ne_rentre_pas_attend_au_lieu_de_disparaitre()
    {
        var camp = new CampStock(new CampSettings { Fuel = 20f, StorageSlots = 1 });

        var offer = new MissionDefinition
        {
            Id = "test", Name = "Test",
            Type = MissionObjectiveKind.KillZombies,
            Requirements = new MissionRequirements { Zombies = 1 },
            Rewards = [new MissionReward(RewardCategory.Medikit, "medikit", 3)],
            Target = ZoneKind.Building,
        };

        new MissionRewardService(camp, offer, DemoMap.Items)
            .Grant(new MissionVerdict(MissionOutcome.Victory, "rentres", 1d));

        Assert.Equal(1, camp.CountOf("medikit"));
        Assert.Equal(2, camp.Pending.Sum(reward => reward.Quantity));

        // De la place se libere : la file se vide dans l'entrepot au fur et a mesure. Un exemplaire
        // sorti du camp, un range, un qui attend encore — les trois promis sont toujours la.
        camp.Withdraw("medikit");
        camp.ClaimPending();

        Assert.Equal(1, camp.CountOf("medikit"));
        Assert.Equal(1, camp.Pending.Sum(reward => reward.Quantity));
    }

    /// <summary>Une recompense inconnue du catalogue n'est jamais versee en silence.</summary>
    [Fact]
    public void Une_recompense_inconnue_du_catalogue_n_est_pas_versee()
    {
        var camp = new CampStock(new CampSettings { Fuel = 20f });

        var offer = new MissionDefinition
        {
            Id = "test", Name = "Test",
            Type = MissionObjectiveKind.KillZombies,
            Requirements = new MissionRequirements { Zombies = 1 },
            Rewards = [new MissionReward(RewardCategory.Rare, "lance_roquettes", 1)],
            Target = ZoneKind.Building,
        };

        var awarded = new MissionRewardService(camp, offer, DemoMap.Items)
            .Grant(new MissionVerdict(MissionOutcome.Victory, "rentres", 1d));

        Assert.Empty(awarded);
        Assert.Empty(camp.Stored);
    }

    /// <summary>
    /// Chaque recompense generee respecte la table du livrable : une arme est une arme, un medikit
    /// et un objet rare n'en sont pas, et la quantite reste dans les bornes du fichier.
    /// </summary>
    [Fact]
    public void Chaque_recompense_generee_respecte_sa_categorie_et_ses_bornes()
    {
        foreach (int seed in Seeds)
        {
            foreach (var offer in Generator().Offer(seed))
            {
                Assert.NotEmpty(offer.Rewards);

                foreach (var reward in offer.Rewards)
                {
                    var item = DemoMap.Items.Find(reward.Item);

                    Assert.NotNull(item);
                    Assert.Equal(reward.Category == RewardCategory.Weapon, item!.Category == ItemCategory.Weapon);

                    var range = Settings.QuantityOf(reward.Category);
                    Assert.InRange(reward.Quantity, (int)range.Min, (int)range.Max);
                }
            }
        }
    }

    // --- Validation de la configuration ---------------------------------------------------------------

    /// <summary>
    /// Le bloc livre se valide contre les catalogues livres — et son profil de secours dit
    /// reellement quelque chose. Un <c>fallback</c> mal orthographie se deserialiserait en un objet
    /// vide sans lever, et le repli poserait alors une mission sans exigence : c'est le seul endroit
    /// du generateur ou l'absence de contenu ne se verrait pas.
    /// </summary>
    [Fact]
    public void Le_bloc_mission_generation_livre_est_valide()
    {
        Settings.Validate(DemoMap.Items, Zones);

        var fallback = Settings.Fallback;

        if (fallback.Type != MissionObjectiveKind.KillZombies)
        {
            Assert.NotEmpty(fallback.ToRequirements().Payload(fallback.Type, DemoMap.Items));
        }
        else
        {
            Assert.True(fallback.Zombies > 0, "Le profil de secours n'a aucun zombie a abattre.");
        }
    }

    [Fact]
    public void Une_recompense_inconnue_est_refusee_au_chargement()
    {
        var settings = CloneWith(Settings, pools: new Dictionary<RewardCategory, List<string>>
        {
            [RewardCategory.Weapon] = ["shotgun"],
            [RewardCategory.Medikit] = ["medikit"],
            [RewardCategory.Rare] = ["objet_qui_n_existe_pas"],
        });

        Assert.Throws<InvalidOperationException>(() => settings.Validate(DemoMap.Items));
    }

    /// <summary>Une arme promise comme objet rare est un mensonge de fichier, pas un choix de design.</summary>
    [Fact]
    public void Une_arme_promise_hors_de_sa_categorie_est_refusee()
    {
        var settings = CloneWith(Settings, pools: new Dictionary<RewardCategory, List<string>>
        {
            [RewardCategory.Weapon] = ["shotgun"],
            [RewardCategory.Medikit] = ["medikit"],
            [RewardCategory.Rare] = ["machete"],
        });

        Assert.Throws<InvalidOperationException>(() => settings.Validate(DemoMap.Items));
    }

    /// <summary>Une categorie promise sans vivier ne peut rien donner : le chargement le refuse.</summary>
    [Fact]
    public void Une_categorie_promise_sans_vivier_est_refusee()
    {
        var settings = CloneWith(Settings, pools: new Dictionary<RewardCategory, List<string>>
        {
            [RewardCategory.Medikit] = ["medikit"],
        });

        Assert.Throws<InvalidOperationException>(() => settings.Validate(DemoMap.Items));
    }

    // --- Le profil de secours -------------------------------------------------------------------------

    /// <summary>
    /// Quand aucune combinaison ne passe, le generateur emploie le profil de secours ecrit dans la
    /// configuration, et dit pourquoi dans son journal. Il ne relache jamais une contrainte pour
    /// « faire passer » une offre : ce serait la seule mission impossible du jeu.
    /// </summary>
    [Fact]
    public void Une_offre_impossible_est_remplacee_par_le_profil_de_secours()
    {
        // Un catalogue d'une seule zone, sans zombie et sans butin : plus aucun type n'est eligible.
        var barren = ZoneCatalogue.FromJson("""
            {
              "zones": [
                {
                  "id": "terrain_vague", "name": "Terrain vague", "kind": "Street",
                  "footprint": [[0, 0], [8, 8]], "layout": "open", "zombie_density": 0.0
                }
              ]
            }
            """);

        var settings = CloneWith(Settings, fallback: new MissionFallbackProfile
        {
            Type = MissionObjectiveKind.KillZombies,
            TargetZone = "terrain_vague",
            Difficulty = MissionDifficulty.Easy,
            Zombies = 2,
        });

        var generator = new MissionGenerator(settings, barren, DemoMap.Items);
        var offers = generator.Offer(2026);

        Assert.Equal(settings.OfferCount, offers.Count);
        Assert.All(offers, offer => Assert.StartsWith("secours-", offer.Id, StringComparison.Ordinal));
        Assert.NotEmpty(generator.Journal);
        Assert.All(offers, offer => Assert.NotEmpty(offer.Rewards));
    }

    // --- Le MVP reste le MVP -----------------------------------------------------------------------

    /// <summary>
    /// Les missions ecrites a la main restent chargeables et jouables : elles n'ont ni type, ni
    /// exigences, ni recompense, ni cout d'entree, et leur condition de victoire est toujours celle
    /// du livrable 8. C'est ce qui rend le livrable 16 retirable.
    /// </summary>
    [Fact]
    public void Les_missions_ecrites_a_la_main_restent_intactes()
    {
        var catalogue = MissionCatalogue.FromJson(DemoMap.ReadConfig("missions.json"), Zones);

        Assert.NotEmpty(catalogue.Missions);

        foreach (var mission in catalogue.Missions)
        {
            Assert.False(mission.IsOffer);
            Assert.Null(mission.Type);
            Assert.Empty(mission.Rewards);
            Assert.Equal(0, mission.EntryFuelCost);

            var supplies = mission.SuppliesFor(DemoMap.Items);

            Assert.Single(supplies);
            Assert.Equal(mission.Item, supplies[0].Item);
            Assert.Equal(mission.Supplies, supplies[0].Quantity);
        }

        // Et une mission manuelle jouee ne recoit aucun compteur : la victoire reste « rapporter
        // la caisse au van ».
        var run = new MissionRun(settings: Untimed);
        Assert.Null(run.Objectives);
    }

    // --- Outillage ------------------------------------------------------------------------------------

    private static MissionSettings Untimed => new() { TimeLimit = 0d };

    /// <summary>La premiere offre generee qui reponde a un critere, en balayant les graines.</summary>
    private static MissionDefinition FirstOffer(Func<MissionDefinition, bool> wanted)
    {
        for (int seed = 0; seed < 200; seed++)
        {
            foreach (var offer in Generator().Offer(seed))
            {
                if (wanted(offer)) return offer;
            }
        }

        throw new InvalidOperationException("Aucune offre generee ne repond au critere du test.");
    }

    private static MissionDefinition FirstOfKind(Func<MissionObjectiveKind, bool> wanted)
        => FirstOffer(offer => offer.Type is { } kind && wanted(kind));

    private static GeneratedDistrict District(MissionDefinition offer)
        => new DistrictGenerator(MapGen).Generate(new DistrictRequest
        {
            Mission = offer,
            Seed = offer.MapSeed ?? 1,
            ZombieCount = offer.ZombieCount ?? 8,
            MinimumSpawnDistance = Config.Zombies.MinimumSpawnDistance,
        });

    /// <summary>
    /// Un terrain minuscule, avec un camp et une zone cible. Les tests de compteur n'ont pas besoin
    /// d'un quartier : ils ont besoin de deux rectangles et de savoir dans lequel on se tient.
    /// </summary>
    private static MissionRun Field(MissionDefinition definition)
    {
        var map = TestMaps.Parse(
            "............",
            "............",
            "............",
            "............",
            "............",
            "............",
            "............",
            "............",
            "............",
            "............",
            "............",
            "............");

        map.AddZone(new MapZone("Camp", ZoneKind.Start, new GridPosition(0, 0), new GridPosition(2, 2)));
        map.AddZone(new MapZone("Camp (retour)", ZoneKind.Extraction, new GridPosition(0, 0), new GridPosition(2, 2)));
        map.AddZone(new MapZone("Cible", ZoneKind.Building, new GridPosition(8, 8), new GridPosition(11, 11)));

        return new MissionRun(map: map, definition: definition, settings: Untimed);
    }

    /// <summary>Une case de la zone cible du terrain de test.</summary>
    private static GridPosition InTarget(MissionRun run)
        => run.Map.FindWalkableIn(run.Definition.ObjectiveIn(run.Map)!)!.Value;

    private static EntityManager Horde(MissionRun run)
    {
        var horde = new EntityManager(run.World, Config.Zombies, Config.Knowledge, Config.Perception);
        run.Band.Horde = horde;
        return horde;
    }

    private static MissionDefinition Kill(int zombies) => new()
    {
        Id = "elimination", Name = "Elimination — Cible",
        Type = MissionObjectiveKind.KillZombies,
        Requirements = new MissionRequirements { Zombies = zombies },
        Target = ZoneKind.Building,
        ObjectiveZone = "Cible",
        Camp = ZoneKind.Start,
        Item = "bandage",
        Rewards = [new MissionReward(RewardCategory.Medikit, "medikit", 1)],
    };

    private static MissionDefinition Collect(string item, int quantity) => new()
    {
        Id = "recuperation", Name = "Recuperation — Cible",
        Type = MissionObjectiveKind.CollectObjects,
        Requirements = new MissionRequirements { Items = [new ItemRequirement(item, quantity)] },
        Target = ZoneKind.Building,
        ObjectiveZone = "Cible",
        Camp = ZoneKind.Start,
        Item = item,
        Rewards = [new MissionReward(RewardCategory.Medikit, "medikit", 1)],
    };

    /// <summary>
    /// Les reglages livres, avec une ligne changee. Il n'y a pas de <c>with</c> possible — le type
    /// est une classe, comme les autres sections de <c>GameConfig</c> — alors on recopie ce que le
    /// test regarde et on laisse le reste aux valeurs par defaut du fichier.
    /// </summary>
    private static MissionGenerationSettings CloneWith(
        MissionGenerationSettings source,
        int? version = null,
        Dictionary<RewardCategory, List<string>>? pools = null,
        MissionFallbackProfile? fallback = null)
        => new()
        {
            Enabled = source.Enabled,
            OfferCount = source.OfferCount,
            GeneratorVersion = version ?? source.GeneratorVersion,
            MaxAttemptsPerOffer = source.MaxAttemptsPerOffer,
            Types = source.Types,
            Zombies = source.Zombies,
            Objects = source.Objects,
            Resources = source.Resources,
            Difficulty = source.Difficulty,
            EntryFuelCostByDifficulty = source.EntryFuelCostByDifficulty,
            TimeLimitByDifficulty = source.TimeLimitByDifficulty,
            RecommendedTeamByDifficulty = source.RecommendedTeamByDifficulty,
            MapProfileByDifficulty = source.MapProfileByDifficulty,
            RewardsByDifficulty = source.RewardsByDifficulty,
            RewardPools = pools ?? source.RewardPools,
            RewardQuantities = source.RewardQuantities,
            Fallback = fallback ?? source.Fallback,
        };
}
