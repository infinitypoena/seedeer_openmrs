FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY openmrs_seeder_v1/openmrs_seeder_v1/openmrs_seeder_v1.csproj .
RUN dotnet restore
COPY openmrs_seeder_v1/openmrs_seeder_v1/ .
RUN dotnet publish -c Release -o /app/publish --no-restore

# App de consola (batch): corre la simulación y termina — sin servidor web ni puertos
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "openmrs_seeder_v1.dll"]
