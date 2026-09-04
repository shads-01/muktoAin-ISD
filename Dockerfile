# S-3.3 (Shads): multi-stage build, nothing fancy.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY global.json .
COPY src/MuktoAin.Domain/MuktoAin.Domain.csproj MuktoAin.Domain/
COPY src/MuktoAin.Application/MuktoAin.Application.csproj MuktoAin.Application/
COPY src/MuktoAin.Infrastructure/MuktoAin.Infrastructure.csproj MuktoAin.Infrastructure/
COPY src/MuktoAin.Web/MuktoAin.Web.csproj MuktoAin.Web/
RUN dotnet restore MuktoAin.Web/MuktoAin.Web.csproj
COPY src/ .
# wwwroot/lib vendor libraries are git-ignored -- restore them via LibMan
# so Bootstrap/jQuery ship inside the published output.
RUN dotnet tool install -g Microsoft.Web.LibraryManager.Cli
WORKDIR /src/MuktoAin.Web
RUN /root/.dotnet/tools/libman restore
WORKDIR /src
RUN dotnet publish MuktoAin.Web/MuktoAin.Web.csproj -c Release -o /app/publish

FROM base AS final
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MuktoAin.Web.dll"]
