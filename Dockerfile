# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .editorconfig ./
COPY src/ClinicQueue.Core/ClinicQueue.Core.csproj src/ClinicQueue.Core/
COPY src/ClinicQueue.Api/ClinicQueue.Api.csproj src/ClinicQueue.Api/
RUN dotnet restore src/ClinicQueue.Api/ClinicQueue.Api.csproj
COPY src/ src/
RUN dotnet publish src/ClinicQueue.Api/ClinicQueue.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# Clinic time zones (ticket numbering resets at local midnight) need the IANA database.
RUN apt-get update && apt-get install -y --no-install-recommends tzdata && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__ClinicQueue="Data Source=/data/clinicqueue.db"
RUN mkdir -p /data && chown app:app /data
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "ClinicQueue.Api.dll"]
