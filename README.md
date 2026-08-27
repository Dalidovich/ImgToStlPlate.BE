# ImgToStlPlate — backend

ASP.NET Core 9 Web API that turns a bitmap image into a printable STL relief plate, plus the PowerShell
script that packages the API and the Angular SPA into a single Windows executable.

The pipeline is three HTTP calls: crop and threshold the source image, optionally denoise the result,
then extrude it into a two-level plate where white pixels are thinner than black ones.

This is the server half of a two-repository project. The client lives in
[ImgToStlPlate.FE](https://github.com/Dalidovich/ImgToStlPlate.FE), cloned as a sibling folder.

## Tech stack

| | |
|---|---|
| Runtime | .NET 9, ASP.NET Core controllers |
| Imaging | SixLabors.ImageSharp 3.1.6 |
| API docs | Swashbuckle (Swagger UI, Development environment only) |
| Tests | xUnit |
| Packaging | `build-single-exe-ImgToStlPlate.ps1` — self-contained single-file `win-x64` publish |

## Repository layout

```
ImgToStlPlate.BE/
├─ ImgToStlPlate.API/
│  ├─ Controllers/ConvertController.cs   the three endpoints and all request validation
│  ├─ Services/ImageProcessingService.cs crop, rotate, orient, threshold, denoise, height matrix
│  ├─ Services/StlGeneratorService.cs    height matrix → closed manifold binary STL
│  ├─ Imaging/SafeImageLoader.cs         decodes uploads under size caps
│  ├─ Binding/                           culture-independent double binder ("2.5" == "2,5")
│  ├─ Middleware/RequestSizeLimitMiddleware.cs
│  ├─ Models/                            CropSelection, ModelOrientation
│  ├─ AppConstants.cs                    every limit and ratio in one place
│  └─ wwwroot/                           generated SPA bundle, never tracked
├─ ImgToStlPlate.Tests/                  xUnit tests for both services, the binder, the controller
├─ docs/orientation.md                   orientation and coordinate-space semantics
└─ build-single-exe-ImgToStlPlate.ps1
```

## Prerequisites

- .NET SDK 9
- Node.js with npm — only for the packaging script, which builds the SPA
- Windows — only for the packaging script (`win-x64` publish, `netsh` firewall rule)

## Running in development

```powershell
dotnet run --project ImgToStlPlate.API
```

The `http` profile in `ImgToStlPlate.API/Properties/launchSettings.json` listens on
`http://localhost:5257`. Swagger UI is at `http://localhost:5257/swagger`.

The SPA is served from the same host — `UseDefaultFiles` + `UseStaticFiles` plus a fallback to
`index.html`, so anything outside `/api` returns the SPA — but only once `wwwroot` has been populated by
the packaging script. In development you normally run the Angular dev server instead, on
`http://localhost:4200`; its `proxy.conf.json` forwards `/api` here to port 5257. Change the port on one
side and you must change it on the other.

Tests:

```powershell
dotnet test
```

## Packaging the single-file exe

```powershell
.\build-single-exe-ImgToStlPlate.ps1
```

With no arguments, from a fresh clone: builds the SPA from `..\ImgToStlPlate.FE`, copies the bundle into
`ImgToStlPlate.API/wwwroot`, publishes a self-contained single-file `win-x64` exe listening on port
**5108** (written into `appsettings.json`), and copies the result to `..\publish`. The packaged exe
serves the API and the SPA from one origin — open `http://localhost:5108`.

| Parameter | Default | Notes |
|---|---|---|
| `-BackendDir` | script folder | Must contain `ImgToStlPlate.API` |
| `-FrontendDir` | `..\ImgToStlPlate.FE` | Must contain `package.json` |
| `-OutputDir` | `..\publish` | **Wiped before copying** |
| `-Port` | `5108` | Port of the packaged exe and of the firewall rule |
| `-ListenAddress` | `0.0.0.0` | Use `127.0.0.1` to keep it off the LAN |
| `-SkipFrontendBuild` | off | Requires `ImgToStlPlate.FE/dist/ImgToStlPlate/browser` to exist already (`npm run build`); `dist` is gitignored, so a fresh clone must build it |
| `-Launch` | off | Starts the exe when the build finishes |

Guards built into the script:

- `-OutputDir` and the `wwwroot` path are rejected before anything is deleted if they are empty, a drive
  or share root, or an ancestor of the repository.
- After the bundle is copied, the build fails if any `.js`/`.mjs`/`.css`/`.html` file still contains an
  absolute `http://localhost:` or `http://127.0.0.1:` origin — the SPA must call the API relatively.
- Run it as Administrator once to have the inbound firewall rule added automatically.

### wwwroot policy

`ImgToStlPlate.API/wwwroot` is **generated, never tracked**. It is gitignored, the script rewrites it on
every build, and the `EnsureSpaBundlePresent` target in the csproj fails the publish with an explicit
message if `wwwroot/index.html` is missing. Never commit a bundle there: the exe and the SPA would drift
apart with no way to tell which commit produced which.

## API contract

Base path `/api/convert`. All three endpoints take `multipart/form-data` and return binary content.
Failures return `application/problem+json` with a `detail` message; unexpected errors are logged and
answered with a generic detail, never an exception dump.

Request bodies are capped at 50 MB (`FormOptions`, Kestrel limits and `RequestSizeLimitMiddleware`).
Source images are capped at 10 000 px per side and 40 MP by `SafeImageLoader`.

`double` fields accept either a dot or a comma as the decimal separator — `2.5` and `2,5` are the same
value regardless of the server's locale, because `FlexibleDoubleModelBinder` parses them
culture-independently. A value carrying both separators, or more than one comma, is rejected as
ambiguous.

### `POST /api/convert/to-bw`

Crops, rotates, orients and thresholds the source image.

| Field | Type | Constraints |
|---|---|---|
| `image` | file | Required, non-empty |
| `selection` | string | Required, JSON `{"x":int,"y":int,"width":int,"height":int}`; offsets ≥ 0, size > 0 |
| `orientation` | string | `horizontal` or `vertical` (case-insensitive) |
| `fillSpace` | bool | Drop the background: white pixels become transparent holes instead of a backing layer |
| `invert` | bool | Swap black and white |
| `rotationDegrees` | double | Finite |

Response: `200 image/png` — a black-and-white PNG that preserves alpha.

### `POST /api/convert/denoise`

Removes speckle from a black-and-white image.

| Field | Type | Constraints |
|---|---|---|
| `bwImage` | file | Required, non-empty |
| `intensity` | int | 0–100 |

Response: `200 image/png`. A client that aborts the request gets `499`.

### `POST /api/convert/to-stl`

Extrudes the black-and-white image into a closed manifold mesh.

| Field | Type | Constraints |
|---|---|---|
| `bwImage` | file | Required, non-empty |
| `thickness` | double | 0.1–50 mm |
| `modelWidth` | double | 1–500 mm |
| `modelHeight` | double | 1–500 mm |
| `orientation` | string | `horizontal` or `vertical` |

The grid is sampled at `MmPerPixel` = 0.4 mm per pixel and must stay under `MaxModelPixels` = 2 000 000
pixels. White pixels get `WhitePixelThicknessRatio` = 50 % of `thickness`, black pixels the full value;
pixels left transparent by `fillSpace` get no geometry at all, so such a model is a cut-out silhouette
whose bounding box is smaller than `modelWidth` × `modelHeight`.

Response: `200 model/stl`, attachment `model.stl`.

### Orientation

`to-bw` is the only endpoint that applies orientation: the PNG it returns is already oriented, and it is
exactly what the client previews. `to-stl` still accepts and validates `orientation` for contract
compatibility but performs no orientation-dependent transform — it only flips the image vertically,
unconditionally, to convert top-down image rows into bottom-up STL `Y`. Full semantics, including the
pixel-to-matrix mapping, are in [docs/orientation.md](docs/orientation.md).

## The two-repo caveat

`ImgToStlPlate` (this repository, branch `master`) and `ImgToStlPlate.FE` (branch `main`) are independent
git repositories; the folder containing both is not a repository. No single commit can change the API
contract and its client atomically, and there is no cross-project history.

**Convention:** a change spanning both projects is committed to both repositories with the **same commit
subject**, so the pair can be found with `git log --oneline --grep="<subject>"` in each. Consolidating
into one repository remains the preferred long-term fix.
