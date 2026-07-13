# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Contato.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish Contato.csproj -c Release -o /app /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_ENVIRONMENT=Production
# a porta real vem da variável PORT (Render); Program.cs lê ela
ENTRYPOINT ["dotnet", "Contato.dll"]
