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

- **Fire one electron** (always shows the wave propagating), **fire a burst**
  of *N* electrons, or **auto-fire** continuously at up to 50/s, and watch
  single impacts accumulate into interference fringes on the detection screen.
- **Observe which slit** each electron goes through: a detector appears at the
  slits, every electron is seen at exactly one of them — and the fringes
  vanish, leaving two sharp classical stripes. Untick to bring the fringes
  back. An optional, clearly-labelled *imagined path* overlay can animate a
  guessed trajectory in the unobserved case purely for intuition — it
  measures nothing and leaves the interference physics untouched.
- Change the **slit separation and width** and see the fringe spacing follow
  Δx ≈ λL/d.
- Overlay the **theoretical prediction** and/or the **measured histogram**
  independently on the detection screen.

## Physics

The simulation solves the actual quantum mechanics of the setup rather than
drawing a pre-baked interference pattern. This section is the derivation of
what the code computes, in the units the code uses throughout: **ħ = m = 1**
and a unit lattice spacing (dx = 1), so momentum, position and time are all
plain numbers in "grid units."

### The equation being solved

An electron's wavefunction ψ(x, y, t) on the 512 × 256 grid evolves under the
free-particle 2D time-dependent Schrödinger equation, with the slit barrier
and screen entering as boundary conditions rather than a potential term:

$$
i \frac{\partial \psi}{\partial t} \;=\; -\frac{1}{2}\nabla^2 \psi \;=\; -\frac{1}{2}\left(\frac{\partial^2 \psi}{\partial x^2} + \frac{\partial^2 \psi}{\partial y^2}\right)
$$

The initial state (`GaussianWavePacket`) is a normalised Gaussian wave packet
launched from the electron gun with mean momentum k₀ along +y:

$$
\psi_0(x,y) \;=\; A \exp\!\left[-\frac{(x-x_0)^2}{4\sigma_x^2} - \frac{(y-y_0)^2}{4\sigma_y^2}\right] e^{\,i k_0 y}, \qquad \sum_{x,y} |\psi_0(x,y)|^2 = 1
$$

with σₓ = 64, σᵧ = 16 grid cells and k₀ = 1 (wavelength λ = 2π/k₀ ≈ 6.3 cells).

### Split-step Fourier integration

Each time step Δt = 0.4 is applied with the standard split-step (Strang
splitting) method (`SplitStepSolver.Step`), which is exact in the limit
Δt → 0 and unconditionally norm-conserving at any Δt because both factors
below are unitary:

$$
\psi(t+\Delta t) \;=\; \mathcal{F}^{-1}\Big[\, e^{-i(k_x^2+k_y^2)\Delta t/2}\; \mathcal{F}\big[\psi(t)\big]\Big]
$$

where $\mathcal F$ is the 2D discrete Fourier transform and $k_x, k_y$ are the
grid's spatial frequencies. In code this is one 2D FFT, a per-frequency phase
multiply (the free-particle kinetic propagator), and one inverse 2D FFT — the
FFT itself is a from-scratch, dependency-free radix-2 Cooley–Tukey
implementation (`Fft.cs`) operating on interleaved single-precision
re/im pairs, run as 256 row-transforms and 512 column-transforms per step.

### Barrier and absorbing boundaries

The slit wall and the domain edges are folded into one multiplicative
amplitude mask $M(x,y)\in[0,1]$, applied before and after the kinetic
half-step (`ExperimentGeometry.BuildMask`):

$$
\psi \;\leftarrow\; M(x,y)\,\psi(x,y)
$$

- **Barrier**: $M=0$ inside the wall except in the two slit apertures
  (width *w*, separation *d*), where $M=1$ — a hard, lossless wall.
- **Absorbers**: a cos²-ramped amplitude decay over the outer 32 cells of
  every edge, so probability leaving the physical region is absorbed instead
  of wrapping around (a discrete Fourier transform is implicitly periodic;
  without this, the packet re-enters from the opposite edge).

**Why a mask instead of a potential term.** The split-step method needs both
propagator factors to be unitary and numerically well-behaved: the kinetic
factor above, and $e^{-iV\Delta t/2}$ for a potential $V$. Both the wall and
the absorbers are awkward to express that way:

- The slit wall is an infinite barrier. $V=\infty$ has no finite value to
  exponentiate, and a large-but-finite $V$ only works if $V\Delta t \ll \pi$
  — otherwise the phase just wraps around instead of blocking the wave,
  forcing an artificially tiny $\Delta t$. Setting $\psi=0$ outside the slits
  directly is an exact, unconditionally stable hard wall at any $\Delta t$.
- The domain edges need absorption, not oscillation. The standard textbook
  fix for FFT wraparound is a complex absorbing potential
  $V \rightarrow V - i\Gamma(x,y)$; under the propagator its only effect is
  amplitude decay $e^{-\Gamma\Delta t}$. It's simpler to apply that decay
  directly as a mask than to carry a separate imaginary-potential array
  through an extra exponential every step just to arrive at the same number.

So $M(x,y)$ is mathematically equivalent to a potential term — infinite at
the wall, imaginary in the absorbing layers — just applied as a direct
constraint on $\psi$ rather than an exponentiated $V$, which sidesteps the
infinity/aliasing problem and skips a redundant exponential each step.

### What the detection screen records

Rather than reading off a single final-time snapshot, the screen accumulates
the time-integrated arrival probability at its row over the whole flight
(`ScreenAccumulator`, Born rule applied continuously):

$$
P(x) \;\propto\; \int_0^{T} |\psi(x,\, y_{\text{screen}},\, t)|^2 \, dt, \qquad \text{normalised so } \sum_x P(x) = 1
$$

Each electron's impact position is one draw from $P(x)$, obtained by
inverting its cumulative distribution function against a uniform random
number (`ScreenAccumulator.Sample` / the runner's `CachedDistribution`) — this
is literally sampling the Born-rule probability, not an approximation of it.
For two open slits this $P(x)$ is the genuine two-slit interference pattern;
its fringe spacing in the far field follows the familiar

$$
\Delta x \;\approx\; \frac{\lambda L}{d}
$$

where *L* is the barrier-to-screen distance. (At this geometry the Fresnel
number $d^2/\lambda L \approx 3$ is only moderately large, so the fringes run
somewhat wider than this far-field estimate — see the tolerance note in
`InterferenceTests.cs`.)

### Observed ("which-slit") mode

Ticking **Observe which slit** does not run any quantum evolution at all — it
switches to the classical prediction that measuring the slit actually
produces. Each electron is assigned to the left or right slit with equal
probability and lands in a sharp stripe directly behind that slit: uniform
across the aperture, convolved with a small Gaussian blur $\sigma_b$ modelling
finite detector/screen resolution:

$$
P_{\text{observed}}(x) \;=\; \tfrac12\Big[\,U_w(x-x_L) + U_w(x-x_R)\Big] * \mathcal{N}(0,\sigma_b^2)
$$

where $U_w$ is the uniform density on an aperture of width *w* centred at slit
position $x_{L,R}$, and $*$ is convolution. Critically, this is a **classical
mixture** of two independent one-slit distributions — there is no cross term,
which is exactly the mathematical statement that interference has vanished
once which-path information exists. This mirrors the standard textbook result
that a real quantum which-path measurement collapses the wavefunction to one
slit and destroys the cross term in $|\psi_L + \psi_R|^2$; the app reproduces
that end state directly instead of re-deriving it from a mid-flight
measurement operator.

### Reproducibility

The wave evolution is fully deterministic (no randomness enters the physics),
so its screen distribution $P(x)$ is computed once per geometry/slit-width
setting and cached; the only randomness in the whole app is which slit an
observed electron is assigned to and where, within $P(x)$, each electron's
impact position is drawn from — exactly the randomness quantum mechanics
itself predicts.

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
python3 -m http.server -d publish/wwwroot 8080   # serve the published build, then open localhost:8080
rm -rf publish/                            # publish/ is gitignored, safe to delete
```

The development server runs the .NET IL interpreter and is an order of
magnitude slower than the published AOT build — judge the wave-animation speed
from the published output, not `dotnet run`.

Deployment to GitHub Pages is automated by
`.github/workflows/deploy-pages.yml` on every push to `main`.

## License

MIT — see [LICENSE.txt](LICENSE.txt).
