# Splitting into repositories

The code is already arranged in the shape this describes — Core, one project per
domain, one adaptor covering both Rhino hosts. Splitting is therefore a packaging
exercise, not a refactor. Nothing below changes a line of C#.

Do it when a domain first needs its own release cadence. Until then the project
boundaries give most of the benefit at none of the cost.

## Target

All under the `Otter-Logic` organisation. Every code repo is `OtterLogic.X`
with one purpose, matching the assembly names so the mapping needs no
explanation.

| Repo | Holds | Produces |
|---|---|---|
| `OtterLogic.Rhino` | both adaptors, `build/`, `assets/`, `docs/` | the `.yak` |
| `OtterLogic.Core` | `src/OtterLogic.Core` | NuGet package `OtterLogic.Core` |
| `OtterLogic.StructuralForm` | domain + its tests | NuGet package `OtterLogic.StructuralForm` |
| `.github` | `profile/README.md` only | the organisation landing page |

There is deliberately no plain `OtterLogic` repo. A meta repo holding a README,
a manifest and a workflow would force the release pipeline to reach across
repos for artifacts, which is the overhead dropped by merging the two adaptors
in the first place. The organisation profile does the landing-page job instead,
and `OtterLogic.Rhino` is where releases, issues and the manifest URL point.

Rhino and Grasshopper stay together deliberately. They must ship one
`OtterLogic.Core.dll` in one package; building them from one commit is what keeps
that honest. Separating them would allow the `.rhp` and `.gha` to be compiled
against different Core versions while only one ships, which surfaces as a
`MissingMethodException` on a user's machine.

Because the adaptor repo builds both front-ends, it can own packaging too — so
there is no need for a fourth release repo. The org profile README lives in a
repo called `.github`, which costs nothing.

Later domains (`Fabrication`, `FormFinding`, `Learning`) become siblings of
`StructuralForm` on the same pattern.

## How adaptors consume domains

Publish Core and each domain to **nuget.org**. GitHub Packages is the obvious
choice but still requires an authenticated feed to consume, which is friction for
anyone cloning the repo; nuget.org needs no token and is what a Rhino developer
expects.

Pin exactly, with brackets:

```xml
<PackageReference Include="OtterLogic.StructuralForm" Version="[0.3.1]" />
```

Square brackets mean *this version*, not "this or newer". Two adaptors in one
repo cannot disagree, but exact pins are what make a build reproducible and make
an upgrade a visible commit.

Do **not** use submodules. Two consumers each carrying a submodule pointer is the
drift hazard with no version number to catch it.

## Keeping the inner loop

The real cost of splitting is that changing a domain and seeing it in Rhino stops
being one build. Set this up in the first migration commit, not after a month of
friction:

```xml
<!-- A sibling checkout wins; the published package is the fallback. -->
<Choose>
  <When Condition="Exists('$(MSBuildThisFileDirectory)..\..\OtterLogic.StructuralForm\src\OtterLogic.StructuralForm\OtterLogic.StructuralForm.csproj')">
    <ItemGroup>
      <ProjectReference Include="..\..\OtterLogic.StructuralForm\src\OtterLogic.StructuralForm\OtterLogic.StructuralForm.csproj" />
    </ItemGroup>
  </When>
  <Otherwise>
    <ItemGroup>
      <PackageReference Include="OtterLogic.StructuralForm" Version="[0.3.1]" />
    </ItemGroup>
  </Otherwise>
</Choose>
```

Clone everything side by side under one folder and the inner loop is unchanged.
CI has no siblings, so it builds against the published package — which makes CI
the thing that catches you shipping against an unreleased domain.

## Versioning

While this is one person: **lockstep**. One version number across all repos,
bumped together at release. `pack.ps1` already reads it from
`Directory.Build.props`, so there is one number per repo to change.

Loosen to independent semver only when a domain gains consumers outside this
product.

## Release

In the adaptor repo:

1. Restore, which pulls the pinned domain packages.
2. Build Release. Every domain DLL lands in `dist/` beside the `.rhp` and `.gha`.
3. `yak build --version` then `yak push`.
4. Attach the `.yak` to a GitHub release tagged the same as the version.

Rehearse against `https://test.yak.rhino3d.com` before the first real push. The
public server has no delete — `yak yank` removes a version from the index but the
name and history stay.

## CI

Each repo gets the existing workflow. Two things carry over: builds only, because
tests boot Rhino through Rhino.Inside and a hosted runner has neither Rhino nor a
licence; and a domain's tests now sit behind a repo boundary, so a breaking change
in Core is not caught by the adaptor until its pin is bumped. The exact pin is
what makes that a visible, deliberate step rather than a surprise.

## Preserve the history

Needs a tool that is not installed by default:

```bash
pip install git-filter-repo
```

`git subtree split` is built in but only handles one prefix at a time, which
cannot carve `StructuralForm` and its tests together.

Do not start empty repos. `git filter-repo` carves each one out with its file
history intact:

```bash
git clone . ../OtterLogic.StructuralForm
cd ../OtterLogic.StructuralForm
git filter-repo \
  --path src/OtterLogic.StructuralForm/ \
  --path tests/OtterLogic.StructuralForm.Tests/
```

Repeat per repo with its own paths. Every commit that touched those files
survives, messages and all.

## Sequence

1. Push this repo as it stands, tagged `v0.1.0-monorepo`. That is the rollback
   and the history anchor, and everything below is easier from a pushed baseline.
2. Carve out Core. Publish `OtterLogic.Core` to nuget.org. The IDs
   `otterlogic`, `otterlogic.core` and `otterlogic.structuralform` were free as
   of the last check; nuget.org IDs are permanent, so claiming them early costs
   nothing and losing one later costs a rename.
3. Carve out `StructuralForm`. Add the pin and the sibling toggle. Confirm it
   builds against the published Core with no sibling present.
4. Reduce this repo to the adaptor and rename it `OtterLogic.Rhino`: both
   front-ends, `build/`, `assets/`, `docs/`. Add pins and toggles for both
   packages.
5. Run one full release end to end and install the resulting `.yak` from the test
   server before trusting it.
