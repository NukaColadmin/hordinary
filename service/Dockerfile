# Le service de langage du livrable 15, en conteneur.
#
# Il existe pour une raison : le lancer sans installer Python ni composer un environnement virtuel.
# Ce n'est pas une dependance du jeu — Hordinary tourne sans ce service, et la suite .NET passe avec
# lui arrete. C'est l'image qui rend le service reproductible, pas le service qui devient requis.
#
# La cle d'API n'entre **jamais** dans l'image : elle se passe au lancement, par l'environnement.
# Une image se pousse, se partage et se garde en cache ; une cle qui y entre est publiee avec elle.
#
#   docker build -t hordinary-service service/
#   docker run --rm -p 127.0.0.1:8001:8001 -e OPENROUTER_API_KEY -e HORDINARY_SERVICE_TOKEN hordinary-service
# Generer HORDINARY_SERVICE_TOKEN et le partager avec le jeu : voir docs/securite.md du depot.
#
# Sans cle, le service demarre quand meme : /health repond et /instruction refuse proprement avec
# « provider_unavailable ». C'est voulu, et c'est ce qui permet d'eprouver le repli du jeu sans payer.

FROM python:3.12-slim

WORKDIR /service

# Les dependances d'abord, et seules : une couche qui ne se reconstruit pas a chaque edition du code.
COPY requirements.lock .
RUN pip install --no-cache-dir -r requirements.lock

COPY app/ app/
COPY evals/ evals/
COPY tests/ tests/

RUN useradd --uid 10001 --create-home hordinary
ENV PYTHONDONTWRITEBYTECODE=1
USER 10001

# 0.0.0.0 et non 127.0.0.1 : dans un conteneur, se lier a la boucle locale revient a n'ecouter
# personne, et le port publie ne verrait rien passer.
EXPOSE 8001
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8001"]
