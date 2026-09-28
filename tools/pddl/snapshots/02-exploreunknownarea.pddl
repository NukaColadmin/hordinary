; Instantane : objectif « ExploreUnknownArea », agent en (-5,-7).
; 15 objets connus, 33 faits, 1 actions instanciees.
; Ce que l'agent n'a jamais percu n'y figure pas : c'est voulu.

(define (problem exploreunknownarea)
  (:domain hordinary)
  (:objects
    bob claire - ally
    door_6_m2 - door
    entrepot_ouest ruelle start - location
    window_m4_m6 window_m8_m1 - window
  )
  (:init
    (accessible ally)
    (accessible cover)
    (accessible entrepot_ouest)
    (accessible extraction)
    (accessible frontier)
    (accessible group)
    (accessible objective_zone)
    (accessible ruelle)
    (accessible start)
    (alley ruelle)
    (band_tended)
    (building cover)
    (building entrepot_ouest)
    (building frontier)
    (door_closed door_6_m2)
    (door_of door_6_m2 objective_zone)
    (extraction extraction)
    (extraction_known)
    (known ally)
    (known cover)
    (known entrepot_ouest)
    (known extraction)
    (known frontier)
    (known group)
    (known objective_zone)
    (known ruelle)
    (known start)
    (pharmacy objective_zone)
    (start start)
    (window_intact window_m4_m6)
    (window_intact window_m8_m1)
    (window_of window_m4_m6 entrepot_ouest)
    (window_of window_m8_m1 entrepot_ouest)
    (= (total-cost) 0)
  )
  (:goal (and (explored)))
  (:metric minimize (total-cost))
)
