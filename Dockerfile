FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY DatingMatchBot.csproj ./
RUN dotnet restore DatingMatchBot.csproj

COPY . ./
RUN dotnet publish DatingMatchBot.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8000
EXPOSE 8000

COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "DatingMatchBot.dll"]
