# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore pehle (csproj alag copy karne se Docker layer cache kaam karta hai)
COPY Notification.csproj ./
RUN dotnet restore Notification.csproj

# Baaki source copy karke publish
COPY . ./
RUN dotnet publish Notification.csproj -c Release -o /app/publish /p:UseAppHost=false --no-restore

# ---------- Runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish ./

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

# Render ek PORT env var deta hai (default 10000). App ko usi par sunna hai.
# Firebase key ko image mein bake nahi kiya: Render "Secret File"
# (/etc/secrets/firebase-service-account.json) hai to startup par app folder
# mein copy ho jati hai, jahan Program.cs ise dhundhta hai.
EXPOSE 10000
CMD ["sh", "-c", "[ -f /etc/secrets/firebase-service-account.json ] && cp /etc/secrets/firebase-service-account.json /app/firebase-service-account.json; ASPNETCORE_URLS=http://0.0.0.0:${PORT:-10000} exec dotnet Notification.dll"]
