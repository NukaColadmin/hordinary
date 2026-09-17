(define (domain hordinary)
  (:requirements :strips :typing :negative-preconditions :conditional-effects :action-costs)
  (:types ally door heavy item location threat window - object)
  (:constants objective remedy toolbox - item  agent - object)
  (:predicates
    (accessible ?arg0 - location)
    (alley ?arg0 - location)
    (at ?arg0 - object ?arg1 - location)
    (building ?arg0 - location)
    (door_barricaded ?arg0 - door)
    (door_closed ?arg0 - door)
    (door_locked ?arg0 - door)
    (door_of ?arg0 - door ?arg1 - location)
    (door_open ?arg0 - door)
    (equipped ?arg0 - item)
    (escaped)
    (explored)
    (extracted)
    (extraction ?arg0 - location)
    (extraction_known)
    (has ?arg0 - object ?arg1 - item)
    (healed ?arg0 - object)
    (heavy_at ?arg0 - heavy ?arg1 - location)
    (hidden)
    (inventory_full)
    (item_at ?arg0 - item ?arg1 - location)
    (knowledge_shared)
    (known ?arg0 - location)
    (mass_ok ?arg0 - heavy)
    (objective_collected)
    (objective_found)
    (observed ?arg0 - location)
    (pharmacy ?arg0 - location)
    (pushed ?arg0 - heavy)
    (pushed_back ?arg0 - threat)
    (searched ?arg0 - location)
    (searched_in_vain ?arg0 - object)
    (shared_with ?arg0 - ally)
    (start ?arg0 - location)
    (street ?arg0 - location)
    (threat_down ?arg0 - threat)
    (threat_handled)
    (usable ?arg0 - item)
    (used ?arg0 - item)
    (waited)
    (window_broken ?arg0 - window)
    (window_intact ?arg0 - window)
    (window_of ?arg0 - window ?arg1 - location)
    (zombie_adjacent ?arg0 - threat)
    (zombie_in_range ?arg0 - threat)
    (zombie_near ?arg0 - location)
  )
  (:functions
    (total-cost)
  )

  (:action MoveToLocation
    :parameters (?location - location)
    :precondition (and (known ?location) (not (at agent ?location)))
    :effect (and (forall (?l - location) (not (at agent ?l))) (at agent ?location) (increase (total-cost) 1)))

  (:action OpenDoor
    :parameters (?door - door ?location - location)
    :precondition (and (at agent ?location) (door_of ?door ?location) (door_closed ?door))
    :effect (and (not (door_closed ?door)) (door_open ?door) (accessible ?location) (increase (total-cost) 1)))

  (:action ForceDoor
    :parameters (?door - door ?location - location)
    :precondition (and (at agent ?location) (door_of ?door ?location) (door_locked ?door))
    :effect (and (not (door_locked ?door)) (door_open ?door) (accessible ?location) (increase (total-cost) 3)))

  (:action BreakWindow
    :parameters (?location - location ?window - window)
    :precondition (and (at agent ?location) (window_of ?window ?location) (window_intact ?window))
    :effect (and (not (window_intact ?window)) (window_broken ?window) (accessible ?location) (increase (total-cost) 4)))

  (:action SearchRoom
    :parameters (?location - location)
    :precondition (and (at agent ?location) (accessible ?location) (not (searched ?location)))
    :effect (and (searched ?location) (increase (total-cost) 2)))

  (:action LocateObjective
    :parameters ()
    :precondition (and (at agent objective_zone) (searched objective_zone) (not (objective_found)) (not (searched_in_vain objective_zone)))
    :effect (and (objective_found) (item_at objective objective_zone) (increase (total-cost) 1)))

  (:action PickUpObjective
    :parameters ()
    :precondition (and (at agent objective_zone) (objective_found) (not (has agent objective)))
    :effect (and (has agent objective) (objective_collected) (increase (total-cost) 1)))

  (:action PickUpSpottedObjective
    :parameters ()
    :precondition (and (at agent objective_spot) (objective_found) (not (has agent objective)))
    :effect (and (has agent objective) (objective_collected) (increase (total-cost) 1)))

  (:action DropItem
    :parameters (?item - item ?location - location)
    :precondition (and (has agent ?item) (at agent ?location))
    :effect (and (not (has agent ?item)) (not (objective_collected)) (item_at ?item ?location) (increase (total-cost) 1)))

  (:action ReachExtraction
    :parameters ()
    :precondition (and (extraction_known) (has agent objective) (not (at agent extraction)) (not (extracted)))
    :effect (and (forall (?l - location) (not (at agent ?l))) (at agent extraction) (extracted) (increase (total-cost) 1)))

  (:action EscapeLocation
    :parameters (?location - location)
    :precondition (and (at agent ?location) (zombie_near ?location) (known away_from_danger))
    :effect (and (forall (?l - location) (not (at agent ?l))) (at agent away_from_danger) (escaped) (increase (total-cost) 1)))

  (:action Hide
    :parameters ()
    :precondition (and (known cover) (not (hidden)))
    :effect (and (forall (?l - location) (not (at agent ?l))) (at agent cover) (hidden) (increase (total-cost) 1)))

  (:action Explore
    :parameters ()
    :precondition (and (known frontier) (not (explored)))
    :effect (and (forall (?l - location) (not (at agent ?l))) (at agent frontier) (explored) (increase (total-cost) 2)))

  (:action Observe
    :parameters (?location - location)
    :precondition (and (at agent ?location) (not (observed ?location)))
    :effect (and (observed ?location) (increase (total-cost) 1)))

  (:action PushBackZombie
    :parameters (?threat - threat)
    :precondition (and (zombie_adjacent ?threat) (not (pushed_back ?threat)))
    :effect (and (pushed_back ?threat) (threat_handled) (increase (total-cost) 2.5)))

  (:action AttackZombie
    :parameters (?threat - threat)
    :precondition (and (zombie_adjacent ?threat) (not (threat_down ?threat)))
    :effect (and (threat_down ?threat) (threat_handled) (increase (total-cost) 3)))

  (:action HealSelf
    :parameters ()
    :precondition (and (has agent remedy) (not (healed agent)))
    :effect (and (not (has agent remedy)) (healed agent) (increase (total-cost) 1)))

  (:action ShareKnowledge
    :parameters (?ally - ally)
    :precondition (and (at agent ally) (not (shared_with ?ally)))
    :effect (and (shared_with ?ally) (knowledge_shared) (increase (total-cost) 1)))

  (:action Wait
    :parameters ()
    :precondition (and (not (waited)))
    :effect (and (waited) (increase (total-cost) 1)))

  (:action PickUpItem
    :parameters (?item - item ?location - location)
    :precondition (and (at agent ?location) (item_at ?item ?location) (not (has agent ?item)) (not (inventory_full)))
    :effect (and (has agent ?item) (not (item_at ?item ?location)) (increase (total-cost) 1)))

  (:action UseItem
    :parameters (?item - item)
    :precondition (and (has agent ?item) (usable ?item) (not (used ?item)))
    :effect (and (used ?item) (increase (total-cost) 1)))

  (:action EquipWeapon
    :parameters (?item - item)
    :precondition (and (has agent ?item) (usable ?item) (not (equipped ?item)))
    :effect (and (forall (?i - item) (not (equipped ?i))) (equipped ?item) (increase (total-cost) 1)))

  (:action AttackWithWeapon
    :parameters (?item - item ?threat - threat)
    :precondition (and (zombie_in_range ?threat) (equipped ?item) (usable ?item) (not (threat_down ?threat)))
    :effect (and (threat_down ?threat) (threat_handled) (increase (total-cost) 2)))

  (:action PushHeavyObject
    :parameters (?heavy - heavy ?location - location)
    :precondition (and (at agent ?location) (heavy_at ?heavy ?location) (mass_ok ?heavy))
    :effect (and (not (heavy_at ?heavy ?location)) (pushed ?heavy) (increase (total-cost) 3)))

  (:action BarricadeDoor
    :parameters (?door - door ?location - location)
    :precondition (and (at agent ?location) (door_of ?door ?location) (door_closed ?door) (equipped toolbox))
    :effect (and (door_barricaded ?door) (not (door_open ?door)) (not (door_closed ?door)) (door_locked ?door) (increase (total-cost) 3)))
)
