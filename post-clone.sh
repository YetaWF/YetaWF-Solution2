#!/bin/bash
cp PublicTools/ProjectSettings/ProjectSettings.csproj_MVC6 PublicTools/ProjectSettings/ProjectSettings.csproj
dotnet build PublicTools/ProjectSettings
dotnet run -p PublicTools/ProjectSettings SetMVC6
dotnet run -p PublicTools/ProjectSettings SymLinks
cd Website
npm install
bower install
dotnet build
dotnet run
cd ..


