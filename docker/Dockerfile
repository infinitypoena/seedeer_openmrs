FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY openmrs_seeder_v1/openmrs_seeder_v1/openmrs_seeder_v1.csproj .
RUN dotnet restore
COPY openmrs_seeder_v1/openmrs_seeder_v1/ .
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "openmrs_seeder_v1.dll"]
