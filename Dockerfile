FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /app

COPY /app/* .

RUN dotnet restore
RUN dotnet publish -c Release -o output


FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime

WORKDIR /app

COPY --from=build /app/output .

ENTRYPOINT ["dotnet", "esphome-gitmanager.dll"]
