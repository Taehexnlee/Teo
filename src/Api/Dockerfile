# ---- build stage ----
    FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
    WORKDIR /src
    
    # 솔루션 & 모든 csproj 먼저 복사 (restore 캐시)
    COPY Teo.sln ./
    COPY src/Api/Api.csproj src/Api/
    COPY src/Application/Application.csproj src/Application/
    COPY src/Domain/Domain.csproj src/Domain/
    COPY src/Infrastructure/Infrastructure.csproj src/Infrastructure/
    COPY src/Worker/Worker.csproj src/Worker/
    
    RUN dotnet restore
    
    # 전체 소스 복사 후 publish
    COPY . .
    RUN dotnet publish src/Api/Api.csproj -c Release -o /app/publish /p:UseAppHost=false
    
    # ---- runtime stage ----
    FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
    WORKDIR /app
    RUN mkdir -p /data
    # 컨테이너에서 8080으로 리슨 (compose가 5256:8080 매핑)
    ENV ASPNETCORE_URLS=http://0.0.0.0:8080
    # Sqlite 연결문자열을 ENV로 받도록 (compose에서 주입)
    ENV ConnectionStrings__Sqlite="Data Source=/home/teo.db"
    COPY --from=build /app/publish .
    EXPOSE 8080
    ENTRYPOINT ["dotnet", "Api.dll"]