FROM mcr.microsoft.com/dotnet/core/aspnet:3.1
COPY /build /podcatcher
WORKDIR /podcatcher

ENTRYPOINT [ "dotnet", "Castos.Podcatcher.Application.dll" ]