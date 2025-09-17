# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY *.csproj ./
RUN dotnet restore

# Copy all project files
COPY . ./

# Publish the app
RUN dotnet publish -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Copy the published output
COPY --from=build /app/publish .

# Expose port
EXPOSE 8080

# Entry point
ENTRYPOINT ["dotnet", "PdfToolsApi.dll"]
