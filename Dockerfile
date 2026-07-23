# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY JogosSeletor.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish JogosSeletor.csproj -c Release -o /app /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_ENVIRONMENT=Production

# CAUSA DO CRASH NO BOOT (exit 139):
# por padrao o ASP.NET Core poe um FileSystemWatcher no appsettings.json para
# recarregar config a quente. O container do Render estoura o limite de inotify
# (128 instancias) e o app morre dentro de WebApplication.CreateBuilder(), antes
# de subir. Desligar o reload resolve — nao usamos config a quente aqui.
ENV DOTNET_hostBuilder__reloadConfigOnChange=false

# Render free = 512 MB. Workstation GC usa bem menos memoria que o Server GC
# (que cria uma heap por CPU). Nao tem relacao com o crash acima, mas ajuda.
ENV DOTNET_gcServer=0
# a porta real vem da variável PORT (Render); Program.cs lê ela
ENTRYPOINT ["dotnet", "JogosSeletor.dll"]
