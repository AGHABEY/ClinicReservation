# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore (cached layer: only project files)
COPY src/ClinicReservation.Domain/ClinicReservation.Domain.csproj src/ClinicReservation.Domain/
COPY src/ClinicReservation.Application/ClinicReservation.Application.csproj src/ClinicReservation.Application/
COPY src/ClinicReservation.Infrastructure/ClinicReservation.Infrastructure.csproj src/ClinicReservation.Infrastructure/
COPY src/ClinicReservation.Web/ClinicReservation.Web.csproj src/ClinicReservation.Web/
RUN dotnet restore src/ClinicReservation.Web/ClinicReservation.Web.csproj

# Publish
COPY src/ src/
RUN dotnet publish src/ClinicReservation.Web/ClinicReservation.Web.csproj -c Release -o /app/publish /p:UseAppHost=false --no-restore

# ---------- Runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "ClinicReservation.dll"]
