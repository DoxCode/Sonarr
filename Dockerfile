# Etapa 1: Imagen de compilación
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build

ARG TARGETARCH

ENV \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    NODE_VERSION=20.11.1

# Instala Node.js y Yarn
RUN apt-get update && apt-get install -y --no-install-recommends curl xz-utils && \
    # Añade lógica para corregir el nombre de la arquitectura para Node.js
    NODE_ARCH=$TARGETARCH && \
    if [ "$TARGETARCH" = "amd64" ]; then NODE_ARCH="x64"; fi && \
    # Descarga usando el nombre de arquitectura corregido
    curl -fsSL https://nodejs.org/dist/v${NODE_VERSION}/node-v${NODE_VERSION}-linux-${NODE_ARCH}.tar.xz | tar -xJ -C /usr/local --strip-components=1 && \
    npm install -g yarn && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /source

# --- Compilación del Frontend ---
# Copia solo los archivos necesarios para restaurar dependencias de yarn y aprovecha la caché
COPY . .
RUN yarn install --frozen-lockfile

# Copia el resto del código del frontend y lo compila

RUN yarn build

# --- Compilación del Backend ---
# Copia solo los archivos de proyecto para restaurar dependencias de .NET y aprovecha la caché

RUN dotnet restore src/Sonarr.sln

# Copia el resto del código y publica la aplicación para una arquitectura específica
COPY . .

# linux-x64 -t:PublishAllRids win-x64 linux-arm64
RUN dotnet publish src/Sonarr.sln \
    -c Release \
    -r linux-${TARGETARCH} \
    --no-restore \
    --framework net8.0 \
    -o /app/publish \
    -p:RunAnalyzers=false \
    -p:TreatWarningsAsErrors=false


# Etapa 2: Imagen final de runtime (mucho más pequeña)
FROM mcr.microsoft.com/dotnet/aspnet:8.0

# Instala dependencias de runtime necesarias
RUN apt-get update && apt-get install -y libsqlite3-0 && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Copia solo los archivos publicados desde la etapa de compilación
COPY --from=build /app/publish .

# Copia los artefactos de compilación del frontend a la carpeta UI
COPY --from=build /source/_output/UI ./UI

# Expone el puerto
EXPOSE 7878

# El ejecutable principal estará en la raíz del directorio de trabajo
CMD ["./Sonarr", "-data=/config"]