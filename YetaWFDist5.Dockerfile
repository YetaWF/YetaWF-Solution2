
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build-env

WORKDIR /app

RUN apt-get update

# Things we need for building
# https://github.com/nodesource/distributions/blob/master/README.md#debinstall
RUN curl -sL https://deb.nodesource.com/setup_12.x | bash -
RUN apt-get install -y nodejs
RUN apt-get install -y build-essential
RUN npm install -g npm
RUN npm install -g gulp

# Needed for webp-image support while building
# Depending on linux flavor, this may need to be adjusted
RUN apt-get install -y libgl1-mesa-glx libxi6 libjpeg62

# Projects
COPY ./CoreComponents/ ./CoreComponents/
COPY ./DataProvider/ ./DataProvider/
COPY ./Localization/ ./Localization/
COPY ./Modules/ ./Modules/
COPY ./PublicTools/ ./PublicTools/
COPY ./Skins/ ./Skins/
COPY ./Website/ ./Website/
COPY ./*.sln .
COPY DeploySite.Docker.yaml .

# ProjectSettings, build the project, then run to create symlinks and to set the correct project files
WORKDIR /app/PublicTools/ProjectSettings
RUN dotnet restore
RUN dotnet build ProjectSettings.csproj
RUN dotnet run Symlinks

# DeploySite, build and later run to merge the published output with the supporting files we need at runtime
WORKDIR /app/PublicTools/DeploySite
RUN dotnet restore
RUN dotnet build -c Release DeploySite.csproj

# Build Website
WORKDIR /app/Website
RUN npm install

RUN dotnet restore
RUN dotnet publish -c Release -o /app/out -r linux-x64

# Merge all package Addons using DeploySite, because dotnet publish doesn't follow symlinks and doesn't know about all the things YetaWF adds
# The merged output is placed in /app/final. See Docker.DeploySite.yaml.
RUN dotnet run -p /app/PublicTools/DeploySite Backup "/app/DeploySite.Docker.yaml"

RUN cp /app/Website/wwwroot/Maintenance/_hc1.html /app/final/wwwroot/_hc.html  # Docker deploy indicator

# We're renaming the /Data folder to /DataInit - It is only used for fresh installs and is renamed to /Data during YetaWF startup
# This is done so existing sites that are upgraded with a new image preserve their original /Data folder
# Same for /wwwroot/Maintenance
RUN mv /app/final/Data /app/final/DataInit
RUN mv /app/final/wwwroot/Maintenance /app/final/wwwroot/MaintenanceInit

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS runtime
WORKDIR /app
COPY --from=build-env /app/final .

# Needed for webp-image support. Depending on linux flavor, this may need to be adjusted
RUN apt-get update
RUN apt-get install -y libgdiplus libc6-dev

ENTRYPOINT ["dotnet", "YetaWF.dll"]
