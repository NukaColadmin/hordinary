# VAL — le validateur de plans du groupe Planning de King's College London.
# https://github.com/KCL-Planning/VAL — licence BSD 3 clauses.
#
# Contrairement a Fast Downward, VAL n'a **pas d'image officielle** : celle-ci clone et compile. C'est
# la seule raison d'etre de ce fichier, et c'est pour ca qu'il est ici plutot qu'une ligne de
# documentation disant « installez VAL ».
#
#   docker build -t hordinary-val tools/pddl/val
#   docker run --rm -v "<dossier>:/data" hordinary-val \
#       /data/domain.pddl /data/problem.pddl /data/plan.txt
#
# VAL sert de **temoin exterieur** au portillon 4 : on lui donne le domaine *declaratif*
# (`domain.declared.pddl`, celui qui porte le bloc `(:constraints …)` de PDDL 3) et le plan produit par
# le jeu. Le verificateur de trajectoire en C# fait deja ce travail et tourne sans binaire ; VAL ne lui
# ajoute pas une fonction, il lui ajoute un temoin qui n'est pas de nous.

FROM debian:stable-slim AS build

RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates cmake make g++ flex bison git \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src
RUN git clone --depth 1 https://github.com/KCL-Planning/VAL.git .

RUN cmake -S . -B build -DCMAKE_BUILD_TYPE=Release && cmake --build build -j"$(nproc)"

# Image d'execution : seulement les binaires et la bibliotheque standard C++. Compiler demande flex,
# bison et les en-tetes ; valider un plan ne demande rien.
FROM debian:stable-slim

RUN apt-get update && apt-get install -y --no-install-recommends libstdc++6 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /src/build/bin/ /usr/local/bin/

WORKDIR /data
ENTRYPOINT ["Validate"]
