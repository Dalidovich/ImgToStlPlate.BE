# ImgToStlPlate — backend

ASP.NET Core 9 API for ImgToStlPlate, plus the script that packages the API and the SPA into a single
Windows exe. It is the server half of a two-repository project; the Angular client lives in
`ImgToStlPlate.FE` next to this folder (`https://github.com/Dalidovich/ImgToStlPlate.FE`).

**The full documentation is the root `README.md` (`../README.md`)**: repository layout, the two-repo
caveat and commit-pair convention, running both halves in development, packaging, and the request and
response contract of the three endpoints. The root README is not tracked by either repository — it lives
in the folder that contains both clones.

## Quick reference

| Task | Command |
|---|---|
| Run the API on `http://localhost:5257` | `dotnet run --project ImgToStlPlate.API` |
| Tests | `dotnet test` |
| Package the single-file exe into `..\publish` | `.\build-single-exe-ImgToStlPlate.ps1` |

The build script runs with no arguments from a fresh clone: it builds the SPA from `..\ImgToStlPlate.FE`,
copies it into `ImgToStlPlate.API/wwwroot`, publishes a self-contained `win-x64` exe on port 5108 and
copies the artifacts to `..\publish`. Its parameters, safety guards and bundle checks are
documented in the root README.

`ImgToStlPlate.API/wwwroot` is generated, never tracked: it is gitignored, the script rewrites it on every
build, and the csproj fails the publish if `wwwroot/index.html` is missing.
