FROM node:24.21.0-bookworm-slim AS web-build
WORKDIR /workspace/src/Web
COPY src/Web/package.json src/Web/package-lock.json ./
RUN npm ci --ignore-scripts
COPY src/Web/ ./
COPY assets/plates/salon-background.png /workspace/assets/plates/salon-background.png
COPY assets/plates/salon-photo.png /workspace/assets/plates/salon-photo.png
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS server-build
WORKDIR /workspace
COPY global.json Directory.Build.props ./
COPY src/Server/Server.csproj src/Server/packages.lock.json src/Server/
RUN dotnet restore src/Server/Server.csproj --locked-mode
COPY src/Server/ src/Server/
COPY --from=web-build /workspace/src/Server/wwwroot/ src/Server/wwwroot/
RUN dotnet publish src/Server/Server.csproj --no-restore -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12 AS runtime
WORKDIR /app
COPY --from=server-build /out ./
COPY docs/identity-email-licenses.txt /app/identity-email-licenses.txt
COPY src/Web/src/assets/fonts/Unna-OFL.txt /app/Unna-OFL.txt
RUN mkdir /app/keys && chown $APP_UID:$APP_UID /app/keys
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Server.dll"]
