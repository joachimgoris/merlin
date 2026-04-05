FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Merlin.slnx .
COPY src/Merlin.Web/Merlin.Web.csproj src/Merlin.Web/
RUN dotnet restore src/Merlin.Web/Merlin.Web.csproj

COPY src/ src/
RUN dotnet publish src/Merlin.Web/Merlin.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:5050
ENV HOST_PROC_PATH=/host/proc
ENV HOST_SYS_PATH=/host/sys
ENV PODMAN_SOCKET_PATH=/var/run/podman/podman.sock

EXPOSE 5050

RUN mkdir -p /app/data
VOLUME ["/app/data"]

COPY --from=build /app .

ENTRYPOINT ["dotnet", "Merlin.Web.dll"]
