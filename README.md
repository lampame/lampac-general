# lampac-general
General modules for Lampac

```yaml
- repository: https://github.com/lampame/lampac-general
  branch: main
  modules:
    - LMG.SubsStreamdata
    - LMG.StreamData
    - LMG.AYCW
    - LMG.Stremio
    - LMG.Subtitles
```

## LMG.Stremio

Manifest

/stremio/manifest.json?token=father

/stremio/father/manifest.json

```json
{
  "LMG.Stremio": {
    "enable": true,
    "cacheMinutes": 5,
    "tmdbApiKey": "SUPERKEY"
  }
}
```

## LMG.Subtitles

Module for integrating subtitles with scaling support through providers

```json
{
  "LMG.Subtitles" : {
    "enable": true,
    "cacheMinutes": 60,
    "providers": {
      "consumit": {
        "enable": true,
        "langs": ["ar","fr","es","de","it","pt","pt-br","tr","ru","nl","id","fa","hi","zh","ja","en"]
      }
    }
  }
}
```
