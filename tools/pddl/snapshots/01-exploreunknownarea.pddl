; Instantane : objectif « ExploreUnknownArea », agent en (1,-7).
; 12 objets connus, 28 faits, 1 actions instanciees.
; Ce que l'agent n'a jamais percu n'y figure pas : c'est voulu.

(define (problem exploreunknownarea)
  (:domain hordinary)
  (:objects
    bob claire - ally
    door_6_m2 - door
    ally cover extraction frontier group objective_zone ruelle start - location
  )
  (:init
    (accessible ally)
    (accessible cover)
    (accessible extraction)
    (accessible frontier)
    (accessible group)
    (accessible objective_zone)
    (accessible ruelle)
    (accessible start)
    (alley ruelle)
    (at agent ally)
    (at agent extraction)
    (at agent group)
    (at agent ruelle)
    (at agent start)
    (door_closed door_6_m2)
    (door_of door_6_m2 objective_zone)
    (extraction extraction)
    (extraction_known)
    (known ally)
    (known cover)
    (known extraction)
    (known frontier)
    (known group)
    (known objective_zone)
    (known ruelle)
    (known start)
    (pharmacy objective_zone)
    (start start)
    (= (total-cost) 0)
  )
  (:goal (and (explored)))
  (:metric minimize (total-cost))
)
