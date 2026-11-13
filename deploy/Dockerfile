# Image Linux de l'API. Construite depuis la racine du dépôt :
#   docker build -f deploy/Dockerfile -t gestionstock-api .
# global.json n'est pas copié : il fixe la version du SDK des postes de développement,
# l'image utilise le SDK .NET 10 qu'elle contient.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS compilation
WORKDIR /source

COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/Host/GestionStock.Api/GestionStock.Api.csproj \
    --configuration Release \
    --output /publication \
    -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS execution
WORKDIR /app

# Répertoire des fichiers (volume), accessible en écriture à l'utilisateur non root de l'image.
RUN mkdir -p /donnees/fichiers && chown "$APP_UID" /donnees/fichiers

COPY --from=compilation /publication .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    Stockage__RepertoireRacine=/donnees/fichiers
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "GestionStock.Api.dll"]
