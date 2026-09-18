# SiLogg

SiLogg läser kontrollpostfiler i CSV-format och gör det möjligt att söka efter stämplingar samt hitta registrerade fel i kontrolltider.

## Förutsättningar

- .NET SDK 10
- CSV-filer med semikolon som avgränsare
- CSV-filerna ska innehålla kolumnerna `SIID` och `Punch DateTime`. Kolumnerna `Control time` och `Code number` används när de finns.

Kontrollera att rätt .NET-version är installerad:

```powershell
dotnet --version
```

## Starta webbappen

Kör kommandona från projektets rot (`Si-logg`):

```powershell
dotnet restore
dotnet run --project .\SiLogg.Web --launch-profile http
```

Öppna sedan [http://localhost:5112](http://localhost:5112).

För HTTPS använder du i stället:

```powershell
dotnet run --project .\SiLogg.Web --launch-profile https
```

Öppna därefter [https://localhost:7299](https://localhost:7299). Det lokala utvecklingscertifikatet kan behöva godkännas i webbläsaren.

### Importera CSV-filer i webbappen

1. Starta webbappen.
2. Öppna sidan **Hantera**.
3. Ladda upp CSV-filerna, eller (endast i lokal utveckling) ange sökvägen till mappen med CSV-filer, t.ex. den förifyllda mappen `..\controlpost-dump`.
4. Kör importen.
5. Sök sedan efter SIID eller kontrollkod på startsidan.

Uppladdade filer sparas bara temporärt under importen och tas bort direkt efteråt — de lagras inte kvar på servern. Vill man kunna titta på datat i efterhand sparar man själv en lokal kopia av CSV-filerna innan uppladdning.

Man kan ladda upp och importera CSV-filer en och en eller några i taget — varje import lägger till nya poster i databasen i stället för att ersätta allt. Laddas samma fil (samma filnamn) upp igen ersätts bara den filens egna rader, så omuppladdningar skapar inga dubbletter.

I Development-miljö lagras webbappens databas som `data\si-logg.db` i projektets rot. När datat inte längre behövs (t.ex. efter en tävling) kan databasen tömmas helt med knappen **Töm databas** på Hantera-sidan.

### Ta emot readouts från SiLogg.Reader

Webbappen har endpointen `POST /api/readouts`. Den kräver headern `X-Api-Key`.
Konfigurera samma hemliga nyckel på servern och i Readern utan att lägga den i källkod eller versionshantering:

```powershell
$env:SILOGG_READOUT_API_KEY = "<hemlig-nyckel>"
```

Readern skickar nyckeln som `X-Api-Key`. Fel eller saknad nyckel ger `401 Unauthorized`. En godkänd readout importeras till samma SQLite-databas som CSV-importen.

## CLI-verktyget

Kör CLI-kommandona från projektets rot:

```powershell
dotnet run --project .\SiLogg.Tool -- --help
```

### Importera CSV-filer till SQLite

```powershell
dotnet run --project .\SiLogg.Tool -- import .\controlpost-dump
```

Importen läser alla `*.csv`-filer i angiven mapp och dess undermappar. Befintliga poster i CLI-databasen tas bort innan importen görs.

### Söka efter en SIID

Sök i den importerade SQLite-databasen:

```powershell
dotnet run --project .\SiLogg.Tool -- search 1000696
```

Sök direkt i CSV-filer utan att använda databasen:

```powershell
dotnet run --project .\SiLogg.Tool -- search 1000696 .\controlpost-dump
```

Resultatet skrivs som JSON som standard. Använd textformat när resultatet ska vara lättläst i terminalen:

```powershell
dotnet run --project .\SiLogg.Tool -- search 1000696 --format text
dotnet run --project .\SiLogg.Tool -- search 1000696 .\controlpost-dump --text
```

Format kan anges som `--format json`, `--format text`, `--json` eller `--text`.

### Visa fel i kontrolltider

Efter en import kan fel rapporteras från SQLite-databasen:

```powershell
dotnet run --project .\SiLogg.Tool -- errors
dotnet run --project .\SiLogg.Tool -- errors --format text
```

Även här är JSON standardformatet.

### Hjälp

Följande varianter visar hjälptexten:

```powershell
dotnet run --project .\SiLogg.Tool -- --help
dotnet run --project .\SiLogg.Tool -- -h
dotnet run --project .\SiLogg.Tool -- --list
```

## Databaser och sökvägar

- Webbappen i Development använder `data\si-logg.db` i repo-roten.
- CLI-verktyget använder `si-logg.db` i sin körkatalog, normalt `SiLogg.Tool\bin\Debug\net10.0` när det körs med `dotnet run`.
- Webbappen använder i Development `..\controlpost-dump` som förvald importmapp.
- Vid publicerad körning förväntas databasen och CSV-mappen ligga under programmets körkatalog, tillsammans med publicerade filer.

## Bygga lösningen

```powershell
dotnet build .\SiLogg.slnx
```

## Publicera till Azure App Service

Appen kan köras i Azure App Service. Ange app setting `SILOGG_DATABASE_PATH` till en skrivbar persistent sökväg, till exempel `D:\home\data\si-logg.db` för Windows App Service. Appen skapar katalogen automatiskt.

Publicera lokalt:

```powershell
dotnet publish .\SiLogg.Web -c Release -o .\publish
Compress-Archive -Path .\publish\* -DestinationPath .\silogg.zip -Force
az webapp deployment source config-zip --resource-group rg-silogg --name <webbapp-namn> --src .\silogg.zip
```

Konfigurera databassökvägen:

```powershell
az webapp config appsettings set --resource-group rg-silogg --name <webbapp-namn> --settings SILOGG_DATABASE_PATH="D:\home\data\si-logg.db"
```

Aktivera därefter App Service Authentication med Microsoft Entra ID och begränsa åtkomsten till de användare eller den grupp som ska använda appen. Uppladdade CSV-filer sparas bara temporärt; spara en lokal arkivkopia före uppladdningen.
