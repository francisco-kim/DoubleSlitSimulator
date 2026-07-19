# Double-slit experiment simulator

An interactive, browser-based simulation of the double-slit experiment with
electrons — aimed at non-physicists. It answers, visually, the question that
makes quantum mechanics famous: *how can single particles build up an
interference pattern, and why does watching them destroy it?*

**Live demo:** <https://francisco-kim.github.io/DoubleSlitSimulator/>

The wave you see is not a cartoon: the app numerically solves the
two-dimensional time-dependent Schrödinger equation in your browser
(.NET WebAssembly, AOT-compiled) and samples each electron's impact position
from the resulting probability distribution.

## What you can do

- **Fire electrons one at a time** (or auto-fire up to 50/s) and watch single
  dots accumulate into interference fringes on the detection screen.
- **Observe which slit** each electron goes through: a detector appears at the
  slits, every electron is seen at exactly one of them — and the fringes
  vanish, leaving two classical heaps. Untick to bring the fringes back.
- Change the **slit separation and width** and see the fringe spacing follow
  Δx ≈ λL/d.
- Overlay the **quantum-mechanical prediction** on the measured histogram.

## Physics

- 2D time-dependent Schrödinger equation, ħ = m = 1, solved with the
  **split-step Fourier method** (own radix-2 FFT, no numerical dependencies).
- Gaussian wavepacket (σₓ = 64, σᵧ = 16 cells, k₀ = 1) on a 512 × 256 grid.
- Hard-wall barrier as a multiplicative mask; cos²-ramped absorbing boundary
  layers suppress periodic-FFT wraparound.
- The screen records the time-integrated |ψ|² at the detector row; electron
  impacts are drawn by inverse-transform sampling from that distribution.
- "Observing" an electron collapses it to one slit (50/50): the evolution is
  re-run with a single open slit, which is physically identical to a
  which-path measurement at the barrier.

## Repository layout

```
src/DoubleSlitPhysics/        physics library (no package dependencies)
  Models/                     slit modes, parameter records
  Representations/            wavefunction, experiment geometry + masks
  Services/                   split-step solver, screen accumulator, experiment
  Helpers/                    radix-2 FFT, Gaussian wavepacket factory
src/DoubleSlitWeb/            Blazor WebAssembly app (canvas rendering via
                              zero-copy [JSImport] MemoryView interop)
tests/DoubleSlitPhysics.Tests xUnit physics sanity tests (FFT identities, norm
                              conservation, packet spreading, fringe spacing)
```

## Getting started

```sh
dotnet test                                # physics sanity tests
dotnet run --project src/DoubleSlitWeb    # local dev server (slow, interpreted)
dotnet publish src/DoubleSlitWeb -c Release -p:EnableAot=true -o publish
```

The development server runs the .NET IL interpreter and is an order of
magnitude slower than the published AOT build — judge the wave-animation speed
from the published output, not `dotnet run`.

Deployment to GitHub Pages is automated by
`.github/workflows/deploy-pages.yml` on every push to `main`.

## License

MIT — see [LICENSE.txt](LICENSE.txt).
