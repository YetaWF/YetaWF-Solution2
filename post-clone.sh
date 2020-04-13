#!/bin/bash
# Change ownership of all files/folder
sudo chown $USER -R .
# get libgdiplus for webp image support
sudo apt-get update
sudo apt-get install -y libgdiplus libc6-dev
# Build and run ProjectSettings tool to set MVC6 project files and all symlinks
dotnet build PublicTools/ProjectSettings
dotnet run -p PublicTools/ProjectSettings SymLinks
# Build the website
cd Website
npm install
npm rebuild cwebp-bin
sudo bower install --allow-root
dotnet build

