FROM mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:28e7a5db4f5d40cc805acd939a065668ba2e17d697a09153054dce98db240d0e AS build
WORKDIR /src
COPY TrailGuard.csproj ./
RUN dotnet restore TrailGuard.csproj
COPY . ./
RUN dotnet publish TrailGuard.csproj --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

# The non-chiseled Noble runtime includes ICU and tzdata.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble@sha256:ed6a2d26633ddcd3d42a1d9f9866214ecbbc11ba6ac5e0e843da02c13da24072
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    TZ=Asia/Manila
COPY --from=build /app/publish ./
COPY deployment/certificates/supabase-prod-ca-2021.crt ./certificates/supabase-prod-ca-2021.crt
RUN chown 1654:1654 /app/certificates/supabase-prod-ca-2021.crt \
    && chmod 0444 /app/certificates/supabase-prod-ca-2021.crt
USER 1654
RUN test -r /app/certificates/supabase-prod-ca-2021.crt
EXPOSE 8080
ENTRYPOINT ["dotnet", "TrailGuard.dll"]
