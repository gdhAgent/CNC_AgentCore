# syntax=docker/dockerfile:1
# CNC_AgentCore (.NET 10 Web API) 多阶段镜像

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# CPM：Directory.Build.props / Directory.Packages.props 必须位于 src/ 之上才能 restore/publish
COPY Directory.Build.props Directory.Packages.props ./
COPY src/CNC_AgentCore.Api/CNC_AgentCore.Api.csproj              src/CNC_AgentCore.Api/
COPY src/CNC_AgentCore.Application/CNC_AgentCore.Application.csproj  src/CNC_AgentCore.Application/
COPY src/CNC_AgentCore.Infrastructure/CNC_AgentCore.Infrastructure.csproj  src/CNC_AgentCore.Infrastructure/
COPY src/CNC_AgentCore.Domain/CNC_AgentCore.Domain.csproj        src/CNC_AgentCore.Domain/
RUN dotnet restore src/CNC_AgentCore.Api/CNC_AgentCore.Api.csproj
COPY src/ src/
# -c Release 对齐 Directory.Build.props(TreatWarningsAsErrors/InvariantGlobalization)；UseAppHost=false 保持纯 DLL
RUN dotnet publish src/CNC_AgentCore.Api/CNC_AgentCore.Api.csproj \
    -c Release --no-restore -o /app/out /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
# compose 健康检查需要 curl（aspnet 镜像不自带）
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/out ./
# compose 会再覆盖一次；这里默认 0.0.0.0 使裸 docker run 也可达（仓库 .env 绑的是 localhost 且不进镜像）
ENV ASPNETCORE_URLS=http://0.0.0.0:8000
EXPOSE 8000
USER 1654            # aspnet:10.0 内置非 root app 用户(APP_UID=1654)
ENTRYPOINT ["dotnet", "CNC_AgentCore.Api.dll"]
