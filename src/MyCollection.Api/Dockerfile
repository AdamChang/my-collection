# syntax=docker/dockerfile:1
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
# ImageSharp 3.2 起，Release 編譯需要 Six Labors license key。
# MSBuild 會把環境變數當成 property 讀取，所以只要設定同名環境變數就會生效。
# 用 secret mount 而不用 build-arg，是因為 build-arg 會留在 image history 裡。
RUN --mount=type=secret,id=sixlabors_license,env=SixLaborsLicenseKey,required=true \
    dotnet publish src/MyCollection.Api/MyCollection.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "MyCollection.Api.dll"]
