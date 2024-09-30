# Deployment

## SSH

```sh
sudo apt update && sudo apt upgrade -y

cd LongevityWorldCup/ && git pull && sudo systemctl stop longevityworldcup.service

sudo dotnet publish LongevityWorldCup.Website/LongevityWorldCup.Website.csproj --configuration Release --output /var/www/LongevityWorldCup/publish && sudo systemctl start longevityworldcup.service

sudo systemctl status longevityworldcup.service
```

## Browser

1. Check https://www.longevityworldcup.com/ on Desktop
2. Check the website on mobile
