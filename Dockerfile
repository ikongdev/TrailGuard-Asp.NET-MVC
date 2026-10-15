FROM mcr.microsoft.com/dotnet/sdk:10.0.402-noble@sha256:3a48280a9a7c6276c3eeaba1efa6a08820f18c84fb9e6274c187719ecfaf9fbc AS build
WORKDIR /src
COPY TrailGuard.csproj ./
RUN dotnet restore TrailGuard.csproj
COPY . ./
RUN dotnet publish TrailGuard.csproj --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

# The non-chiseled Noble runtime includes ICU and tzdata.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble@sha256:e4b445f93a5deeb6bd7de300febceb8dd7e481ac65ccf1b474eddc3642a3010f
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
