FROM mcr.microsoft.com/dotnet/core/sdk:2.2 AS build-env

WORKDIR /app

RUN apt-get update

# Things we need for building
# https://github.com/nodesource/distributions/blob/master/README.md#debinstall
RUN curl -sL https://deb.nodesource.com/setup_12.x | bash - 
RUN apt-get install -y nodejs
RUN apt-get install -y build-essential
RUN npm install -g npm
RUN npm install -g gulp
RUN npm install -g bower

# Needed for webp-image support while building
# Depending on linux flavor, this may need to be adjusted
RUN apt-get install -y libgl1-mesa-glx libxi6

# Projects
COPY ./CoreComponents/ ./CoreComponents/
COPY ./DataProvider/ ./DataProvider/
COPY ./Localization/ ./Localization/
COPY ./Modules/ ./Modules/
COPY ./PublicTools/ ./PublicTools/
COPY ./Skins/ ./Skins/
COPY ./Website/ ./Website/
COPY ./*.sln .
COPY Docker.CopySite.txt .

# ProjectSettings, build the project, then run to create symlinks and to set the correct project files
WORKDIR /app/PublicTools/ProjectSettings
RUN cp ProjectSettings.csproj_MVC6 ProjectSettings.csproj
RUN dotnet restore
RUN dotnet build ProjectSettings.csproj
RUN dotnet run Symlinks
RUN dotnet run SetMVC6

# CopySite, build and later run to merge the published output with the supporting files we need at runtime
WORKDIR /app/PublicTools/CopySite
RUN dotnet restore
RUN dotnet build CopySite.csproj

# Build Website
WORKDIR /app/Website 
RUN npm install 
RUN npm rebuild node-sass
RUN bower install --allow-root

RUN dotnet restore
RUN dotnet publish -c Release -o /app/out -r linux-x64

# Merge all package Addons using CopySite, because dotnet publish doesn't follow symlinks and doesn't know about all the things YetaWF adds
RUN dotnet run -p /app/PublicTools/CopySite Backup "/app/Docker.CopySite.txt"

# Build runtime image
FROM mcr.microsoft.com/dotnet/core/aspnet:2.2 AS runtime
WORKDIR /app
COPY --from=build-env /app/final .
# We're renaming the /Data folder to /DataInit - It is only used for fresh installs and is renamed to /Data during YetaWF startup
# This is done so existing sites that are upgraded with a new image preserve their original /Data folder
# Same for /wwwroot/Maintenance
RUN mv ./Data ./DataInit
RUN cp ./wwwroot/Maintenance/_hc1.html ./wwwroot/_hc.html
RUN mv ./wwwroot/Maintenance ./wwwroot/MaintenanceInit

HEALTHCHECK --interval=30s --timeout=30s --start-period=15s --retries=3 CMD http://localhost/_hc.html || exit 1

# Needed for webp-image support
# Depending on linux flavor, this may need to be adjusted
RUN apt-get update
RUN apt-get install -y libgdiplus libc6-dev 

ENTRYPOINT ["dotnet", "YetaWF.dll"]
