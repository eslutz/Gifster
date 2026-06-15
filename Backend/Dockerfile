# Build with Native AOT, then run the resulting self-contained binary.
# Intended deployment target: Azure Container Apps consumption workload profile.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ARG TARGETARCH
RUN apt-get update \
  && apt-get install -y --no-install-recommends clang zlib1g-dev \
  && rm -rf /var/lib/apt/lists/*
COPY Backend/Gifster.Backend.csproj Backend/
RUN dotnet restore Backend/Gifster.Backend.csproj
COPY Backend/ Backend/
RUN case "$TARGETARCH" in \
    amd64) runtime_id=linux-x64 ;; \
    arm64) runtime_id=linux-arm64 ;; \
    *) echo "Unsupported target architecture: $TARGETARCH" && exit 1 ;; \
  esac \
  && dotnet publish Backend/Gifster.Backend.csproj -c Release -r "$runtime_id" --self-contained true -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["./Gifster.Backend"]
