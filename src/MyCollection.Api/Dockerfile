FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY MyCollection.slnx ./
COPY src/MyCollection.Domain/*.csproj src/MyCollection.Domain/
COPY src/MyCollection.Application/*.csproj src/MyCollection.Application/
COPY src/MyCollection.Infrastructure/*.csproj src/MyCollection.Infrastructure/
COPY src/MyCollection.Api/*.csproj src/MyCollection.Api/
RUN dotnet restore src/MyCollection.Api/MyCollection.Api.csproj

COPY src/ src/
RUN dotnet publish src/MyCollection.Api/MyCollection.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "MyCollection.Api.dll"]
