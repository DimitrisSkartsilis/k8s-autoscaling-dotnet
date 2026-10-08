# ---- Stage 1: build, using the full SDK image ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the project file and restore packages (cached until the .csproj changes).
COPY src/Api/Api.csproj src/Api/
RUN dotnet restore src/Api/Api.csproj

# Copy the source and publish a Release build.
COPY src/Api/ src/Api/
RUN dotnet publish src/Api/Api.csproj -c Release -o /app/publish --no-restore

# ---- Stage 2: runtime, a small image with only the ASP.NET runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Take only the published output from stage 1.
COPY --from=build /app/publish .

# Run as the image's built-in non-root user.
USER $APP_UID

# .NET container images listen on port 8080 by default.
EXPOSE 8080
ENTRYPOINT ["dotnet", "Api.dll"]