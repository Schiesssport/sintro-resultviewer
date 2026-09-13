# Build and run the API in containers. The range PC gets a self-contained .exe
# instead (scripts/publish-win.sh); this image is for development and for anyone
# who wants to host the viewer on Linux.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, against the project file alone, so a source edit does not re-download packages.
COPY src/Sintro.ResultViewer.Api/Sintro.ResultViewer.Api.csproj src/Sintro.ResultViewer.Api/
RUN dotnet restore src/Sintro.ResultViewer.Api/Sintro.ResultViewer.Api.csproj

COPY . .
RUN dotnet publish src/Sintro.ResultViewer.Api/Sintro.ResultViewer.Api.csproj \
        -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
EXPOSE 8080

# The base image sets ASPNETCORE_HTTP_PORTS=8080, which appsettings.jsonc then overrides with
# the same port — announcing a conflict on every start that is not one. Cleared so the address
# has a single source, the file, exactly as on a range PC.
ENV ASPNETCORE_HTTP_PORTS=

ENTRYPOINT ["dotnet", "Sintro.ResultViewer.Api.dll"]
