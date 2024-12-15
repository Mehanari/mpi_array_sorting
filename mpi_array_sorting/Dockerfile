FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["mpi_array_sorting/mpi_array_sorting.csproj", "mpi_array_sorting/"]
RUN dotnet restore "mpi_array_sorting/mpi_array_sorting.csproj"
COPY . .
WORKDIR "/src/mpi_array_sorting"
RUN dotnet build "mpi_array_sorting.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "mpi_array_sorting.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "mpi_array_sorting.dll"]
