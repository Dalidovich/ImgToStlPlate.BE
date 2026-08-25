# Orientation semantics

`orientation` describes how the cropped image is placed on the printed plate.
Valid values are defined by `ModelOrientation`: `horizontal` and `vertical`.

## Where it is applied

`POST /api/convert/to-bw` is the **only** place that applies orientation.
The black-and-white image it returns is already oriented, and it is exactly what
step 2 shows in the preview.

`POST /api/convert/to-stl` consumes that already-oriented image. It still accepts
and validates `orientation` for request-contract compatibility, but performs no
orientation-dependent transform.

## Meaning

- `horizontal` — the crop is used as-is. The bottom edge of the crop is the
  bottom edge of the plate.
- `vertical` — the crop is turned 90 degrees clockwise. The right edge of the
  crop becomes the bottom edge of the plate; the crop's top edge becomes the
  plate's right edge.

## Row-to-Y correction

Image rows grow downward, STL `Y` grows upward (`StlGeneratorService` uses
`y = row * mmPerPixel`). `to-stl` therefore flips the image vertically before
building the matrix. This correction is unconditional: it is a coordinate-space
conversion, not an orientation choice.

The result is that an STL viewed from `+Z` with `X` to the right and `Y` up
matches the step 2 preview pixel for pixel, in both orientations, with no
mirroring.

## Pixel to matrix mapping

`to-bw` writes pure black or pure white while preserving the source alpha:

- alpha `0` — transparent: rotation corners left by `Rotate`, genuine
  transparency in the uploaded PNG, and (when `fillSpace` is on) white areas.
- black — material at full thickness.
- white — material at `WhitePixelThicknessRatio` of the thickness.

`to-stl` maps `A < 128` to `-1` (hole), `R < 128` to `1`, otherwise `0`.
